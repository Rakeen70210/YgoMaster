---
name: hooking-il2cpp
description: Use when adding, modifying, or debugging YgoMasterClient hooks - intercepting game methods, calling IL2CPP APIs, duel.dll functions, crashes inside masterduel.exe, or thread/GC issues in hook code.
---

# Hooking IL2CPP

## Overview

`YgoMasterClient.exe` never modifies game files. It launches `masterduel.exe`, injects `YgoMasterLoader.dll` (C++, MinHook/Detours), which boots a .NET runtime *inside* the game process and runs `Program.DllMain`. All behavior changes are runtime hooks on (a) the game's IL2CPP managed code via `GameAssembly.dll` metadata, or (b) native `duel.dll` exports.

Injection chain: `GameLauncher` (Detours mode default) → loader hooks `LoadLibraryW` → after `il2cpp_init` → CLR/Mono host → `Program.DllMain` → static constructors of every class in `Program.cs`'s `nativeTypes` list register `Hook<T>` objects → `WL_EnableAllHooks(true)`.

## The recurring hook pattern

Every hook in the codebase follows this shape (good worked examples: `DuelStarter.cs`, `AssetHelper.cs`, `SoloVisualNovel.cs`):

```csharp
// In a static constructor (runs at startup via nativeTypes registration):
IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
IL2Class cls = asm.GetClass("SomeGameClass", "Namespace");   // nested: cls.GetNestedType("Inner")
IL2Method target = cls.GetMethod("Play");

delegate void Del_Play(IntPtr thisPtr, IntPtr arg);           // match native signature; IntPtr for objects
static Hook<Del_Play> hookPlay = new Hook<Del_Play>(MyPlay, target);

static void MyPlay(IntPtr thisPtr, IntPtr arg)
{
    if (HandleMyLogic(thisPtr, arg)) return;                  // swallow, or...
    hookPlay.Original(thisPtr, arg);                          // ...fall through to the game
}
```

To **register** a new hook class: add it to the `nativeTypes` list in `Program.cs` (its static ctor is what creates the hooks) and add the file to `YgoMasterClient.csproj` (`<Compile Include>` — old-style project).

## Quick reference — the marshaling toolbox (`YgoMasterClient/IL2CPP/`)

| Need | How |
|---|---|
| Call a game method | `cls.GetMethod("Name").Invoke(instancePtr, new IntPtr[]{ args })` → `.GetValueObj<T>()` / `.GetValueRef<T>()` |
| Managed string → game | `new IL2String(s).ptr` |
| Read/write a field | `cls.GetField("name").GetValue(obj)` / `.SetValue(...)` (offset = `il2cpp_field_get_offset` + 0x10 header) |
| Hook a native duel.dll export | `PInvoke.GetProcAddress(PInvoke.LoadLibrary("masterduel_Data/Plugins/x86_64/duel.dll"), "DLL_...")` then `Hook<T>` — see `DuelDll.cs` |
| Find available game surfaces | `Docs/updatediff.cs` (dumped game API) and `DuelDll.ProxyFunctions.cs` (~150 wrapped `DLL_Duel*` functions: state queries, command masks, dialogs, lists) |
| Expensive reflection | Do it once in the static ctor, cache in static fields (see `SoloVisualNovel.cs`) |

## Threading rules (crashes live here)

From the header of `DuelDll.cs` — these are load-bearing:

- **Duel thread** (inside `DLL_DuelSysAct` and duel.dll hooks): safe to read/modify `PvpEngineState` and duel state; **unsafe** to call UnityEngine/game UI methods.
- **Game (Unity) thread**: safe for IL2CPP invocation and UI; unsafe to race duel state.
- To run on the duel thread: `lock (DuelDll.ActionsToRunInNextSysAct) { ...Add(action); }` — executes on the next SysAct tick (this is how LLM broker commits are scheduled).
- To run on the game thread: `TradeUtils.AddAction(action)`.
- Broker HTTP / anything slow: background thread, then queue the result back via `ActionsToRunInNextSysAct`. Never block the duel thread on network.

**GC pitfall:** any managed delegate handed to native code must be kept alive/pinned (`il2cpp_gchandle_new(..., true)`) until native code can no longer call it; free with `il2cpp_gchandle_free`. Early collection = dangling function pointer = crash with no managed stack (see `AssetHelper` load requests for the pattern).

## Debugging hooks

| Symptom | Check |
|---|---|
| Client exits with "Unsupported game version" | Game updated; `SupportedGameVersion` mismatch in `ClientSettings.json` → see recovering-from-game-updates |
| `GetMethod`/`GetClass` returns null after an update | IL2CPP metadata churn → run `updatediff` console command; enable `ReflectionValidatorValidate` (recovering-from-game-updates) |
| "Hook already exists" | Same address hooked twice — one `Hook<T>` per target; share it |
| Crash, no managed stack | Wrong delegate signature (calling convention/arity), or GC'd delegate — check pinning |
| Works in solo, breaks in PvP | PvP relays through `Pvp.cs` with struct-offset access → offsets are version-pinned (recovering-from-game-updates) |
| Need visibility | `ShowConsole: true` in ClientSettings; `AssetHelperLog`; console `crc`/`carddata`/`itemid` commands |

## Constraints

- .NET Framework 4.8, x64 only. No SDK-style project; no async/await idioms in hook paths.
- Hooks can't be unit-tested — only a live launch proves them (validating-changes gate 5). Keep hook bodies thin; put logic in testable classes (the `Llm/` split exists precisely for this).
- Every hooked member is recorded in `Docs/ReflectionDump.json` via `ReflectionValidator` — regenerate after adding hooks (`ReflectionValidatorDump` setting) so update-breakage detection keeps working.

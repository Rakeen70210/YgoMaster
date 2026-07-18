---
name: ygomaster-map
description: Use when starting any task in the YgoMaster repo, orienting in an unfamiliar area, deciding which files/projects a change touches, or unsure which docs to trust.
---

# YgoMaster Project Map

## Overview

YgoMaster is an offline Yu-Gi-Oh! Master Duel private server (fork of pixeltris/YgoMaster) plus this fork's additions: the LE/Requiem solo campaigns and an LLM-controlled PvP opponent. Three languages, one repo: C# (.NET Framework 4.8) game code, C++ (injection loader), Python (Tools/ pipeline).

**Read this first, then load the skill for your task type (routing table at the bottom).**

## Component map

| Path | What it is |
|---|---|
| `YgoMasterServer/` | `YgoMaster.exe` — HTTP game server. Acts dispatch in `GameServer.Process()`, handlers in `Acts/Act_*.cs`. See extending-server-acts. |
| `YgoMasterServer/Llm/` | LLM opponent core (protocol, extraction, validation, logging). **Compiled into 4 projects** — see blast radius below. |
| `YgoMasterClient/` | `YgoMasterClient.exe` — launches `masterduel.exe`, injects, hooks IL2CPP + `duel.dll`. See hooking-il2cpp. |
| `YgoMasterClient/IL2CPP/` | Reflection/hooking infrastructure (IL2Class, IL2Method, Hook, Assembler). |
| `YgoMasterLoader.cpp` | Native bootstrap DLL (MinHook/Detours) that spins up .NET inside the game process. |
| `YgoMasterLlmTestHarness/` | The C# test suite. **Not xunit/nunit** — a hand-rolled runner: assertion methods called from `Program.cs Main()`, nonzero exit on failure. Two csproj: net48 (Windows) and `.Net8.csproj` (cross-platform, for local verification). |
| `Tools/` | Python: LLM broker (`llm_broker.py`), runtime deploy (`prepare_llm_runtime.py`), log analysis, campaign import/validation, `test_*.py` unittest suite. |
| `YgoMaster/Data/` | Game data (~242 MB): `Solo.json`, `SoloDuels/`, `ClientData/`, card lists, shop. See editing-campaign-content. |
| `Docs/` | Operational docs only (`LlmBroker.md`, setup, updating). **Not** project work logs. |
| Obsidian vault `…/projects/ygomaster/` | **Authoritative** LLM work items, decisions, and progress (`work/YGOMASTER-LLM-00N…`, `decisions.md`, `INDEX.md`). |

## Traps that waste sessions (all observed)

1. **Link-compiled sources, no project references.** `YgoMasterServer/Llm/*.cs` is `<Compile Include>`-ed into `YgoMasterServer/YgoMaster.csproj`, `YgoMasterClient.csproj`, and both harness csproj files. A "server" file change rebuilds the **client** (which is the process that actually sends broker requests). Grep the csproj files to find a file's true blast radius.
2. **Old-style csproj.** No SDK globbing. A new `.cs` file does nothing until you add a `<Compile Include>` line to *every* csproj that needs it.
3. **No CI.** Nothing runs automatically. validating-changes is the manual gate.
4. **Two machines.** Dev box (this repo checkout; needs `python3`, optionally a .NET SDK for the net8 harness) vs the game machine (Linux + Steam + GE-Proton10-34, where the runtime actually runs). Live validation only happens on the game machine. See running-the-runtime.
5. **There is no `YgomGame/` or game source.** The client only *hooks* the game's IL2CPP assemblies and `duel.dll` exports. Don't search for game UI source; search `DuelDll.ProxyFunctions.cs` and `Docs/updatediff.cs` for the available surfaces.
6. **`Data/Players/` is gitignored.** Player saves. Never commit; back up before replacing binaries.

## Which docs to trust

Code > vault project memory (`/home/rakeenhuq/Onedrive/Obsidian Vault/projects/ygomaster/`) > operational `Docs/*.md`. Vault work items go stale when only code changes without a Progress entry — two past operational-doc examples (both corrected 2026-07-04): `Docs/LlmBroker.md` claimed the broker controls the "non-local player" (code: `LlmBrokerControlPolicy.ShouldControlPlayer` requires `player == myId && player == controlPlayer`), and `Docs/LE-Requiem-Linux-Setup.md` described Proton 10.0 (actual: GE-Proton10-34, client-first, per `YgoMasterLaunch.sh`).

When a doc contradicts code, the code wins; fix the operational doc in the same commit and append vault Progress/decisions for durable project state. **Do not** recreate repo `Docs/work/`, root `work/`, `Docs/decisions.md`, `decisions.md`, or `INDEX.md`.

## Conventions

- Commits: `feat:`/`fix:`/`docs:`/`data:`/`test:`/`chore:` prefixes; update with `git pull --ff-only`.
- All JSON config is JSON-with-comments, parsed by `MiniJSON.DeserializeStripped()`. Preserve comments when editing; `prepare_llm_runtime.py` does this for you for `ClientSettings.json`.
- New risky features ship as **default-off vertical slices**: setting flag off in tracked source, log-only capture before control, runtime validation before widening scope. See extending-broker-actions.

## Routing

| Task | Skill |
|---|---|
| LLM opponent behavior/architecture questions | llm-broker-pipeline |
| Broker not committing, rejected events, duel hangs | debugging-llm-decisions |
| "Is my change done?" — builds/tests/verification | validating-changes |
| Add/extend broker actions, schema fields | extending-broker-actions |
| Add/fix a client hook, duel.dll interaction | hooking-il2cpp |
| Master Duel updated and things broke | recovering-from-game-updates |
| Solo campaign/duel/reward data edits | editing-campaign-content |
| New server endpoint, player data, shop logic | extending-server-acts |
| Build, deploy, launch, PvP setup on the game machine | running-the-runtime |

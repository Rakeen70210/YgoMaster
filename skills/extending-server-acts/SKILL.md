---
name: extending-server-acts
description: Use when changing YgoMaster.exe server behavior - adding endpoints (acts), player data, shop/solo/duel logic, settings, persistence, or debugging server-side request handling.
---

# Extending Server Acts

## Overview

`YgoMaster.exe` is a plain .NET Framework 4.8 HTTP server, no framework. The client sends acts (e.g. `User.home`, `Shop.purchase`) as LZ4-compressed MessagePack; `GameServer.Process()` (in `YgoMasterServer/GameServer.cs`) deserializes, dispatches through one big `switch` on the act name, and serializes `request.Response` back. State is in-memory + JSON files — there is no database.

## Adding or changing an act

1. Handler: a method on the partial `GameServer` class in the matching `YgoMasterServer/Acts/Act_*.cs` file:
   ```csharp
   void Act_MyFeature(GameServerWebRequest request)
   {
       object value;
       if (request.ActParams.TryGetValue("some_param", out value)) { /* ... */ }
       // mutate request.Player...; then:
       request.Player.RequiresSaving = true;              // persisted after the act returns
       request.Response["MyData"] = new Dictionary<string, object>() { /* ... */ };
   }
   ```
2. Register the case in the `GameServer.Process()` switch.
3. Reuse the `Write*` helpers (`GameServer.Writers.cs`: `WriteUser`, `WriteDeck`, `WriteCards`, `WriteItem`) — the client expects those exact response shapes.
4. New file? Add `<Compile Include>` to `YgoMasterServer/YgoMaster.csproj` (old-style project).

Language constraints: C# on .NET Framework 4.8 — manual threads and locks, no async/await patterns, x64 only. Match the existing style.

## State, persistence, threading

| Fact | Consequence |
|---|---|
| Player = `Player.cs` object, saved to `Data/Players/<Code>/Player.json` | Set `RequiresSaving = true` after mutations; `SavePlayerNow()` runs post-act. Never hand-edit saves while the server runs. |
| Player code is `MD5(token) % 999999999` (single-player: fixed `1111111111`) | Same token → same account, deterministically. Token comes from the client's `MultiplayerToken`. |
| Multiplayer requests run on the ThreadPool; single-player is synchronous | All shared state is lock-guarded: `playersLock`, `duelRoomsLocker`, per-player collection locks, per-room `MembersLocker`. Take the matching lock; watch ordering when taking two. |
| Settings load **once at startup** in `GameServer.State.cs LoadSettings()` | Any `Settings.json` / data change requires a server restart. JSON-with-comments via `MiniJSON.DeserializeStripped()`. |
| An update thread ticks every 10 s | Room expiry, IP token release, session pings — long-lived state cleanup belongs there, not in act handlers. |
| PvP duels relay through `Pvp.cs` + TCP session server (ports 4988/4989) | Duel simulation is `duel.dll` via P/Invoke, incl. headless modes (`--pvp`, `--cpucontest-sim`). PvP struct offsets are game-version-pinned — see recovering-from-game-updates. |

## Data the server reads at startup

`Data/Settings.json` (network, feature flags, duel-room config, unlock cheats), `Solo.json` + `SoloDuels/` (see editing-campaign-content), `CardList.json`/`Regulation*.json`, `Shop.json`, `StructureDecks/`, `YdkIds.txt` (`YdkHelper` card-ID mapping). Missing files degrade specific subsystems (empty card lists, broken shop) rather than failing loudly — if a feature returns empty data, check the data file before the code.

Useful dev settings: `MultiplayerEnabled: false` for local iteration; `UnlockAllCards`/`UnlockAllSoloChapters` to skip grind; `PvpLogToConsole`.

## Worked example to imitate

The LE Combined port (commit `eeda38b`, contract-tested by `Tools/test_le_combined_source.py`) is the model server change: behavior added in `Act_Solo.cs`/`Act_Duel.cs`/`Act_Shop.cs`/`ShopInfo.cs`/`DuelSettings.cs`, driven by data, guarded by settings (`RandomiseLEChapterBattlefield`), with a Python contract test pinning that the source markers exist. Note the pattern: **server logic changes get a `Tools/test_*.py` contract test** since there's no C# test coverage for acts.

## Common mistakes

| Mistake | Reality |
|---|---|
| Mutating `request.Player` without `RequiresSaving = true` | Change evaporates on restart. |
| Editing `Settings.json` and expecting live pickup | Loaded once. Restart the server. |
| Accessing player collections without the lock | Fine in single-player testing, corrupts under multiplayer ThreadPool load. |
| Returning ad-hoc response shapes | The client is the real game; it needs the exact dictionary structure. Copy from a working act / `Write*` helpers, or capture real traffic (Fiddler, `Docs/Updating.md`). |
| Verifying only by build | Build, then boot the server against real `Data/` and drive the act from the client (validating-changes). |

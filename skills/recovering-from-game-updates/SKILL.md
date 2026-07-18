---
name: recovering-from-game-updates
description: Use when Master Duel updates and YgoMaster breaks - "Unsupported game version" errors, hooks failing to resolve, PvP desync/crashes, custom audio/images crashing, or importing fresh game data.
---

# Recovering From Game Updates

## Overview

Master Duel updates every few weeks (watch SteamDB) and routinely breaks YgoMaster in four independent ways. Diagnose which one(s) you're facing; each has its own recovery procedure. Full upstream procedures: `Docs/Updating.md`; PvP offsets: `Docs/UpdatingPvPOffsets.md`.

**First move on any breakage after an update:** don't debug your own code. Check Steam for a game update, and pin/prevent auto-update on the game machine until recovery is done. Back up `YgoMaster/Data/Players/` before touching anything.

## The four breakage classes

### 1. Version gate

Client refuses to start: `SupportedGameVersion` in `ClientSettings.json` no longer matches. Bumping it past the check is step one — the real question is whether classes 2–4 below also broke.

### 2. IL2CPP metadata churn (hooks fail to resolve)

Game classes/methods renamed or resignatured → `Assembler`/`GetMethod` lookups return null or hooks misbehave.

Recovery loop:
1. In-game console (`ShowConsole: true`): run `updatediff` — diffs the live game API against `Docs/updatediff.cs` and reports what moved.
2. Fix the affected lookups/hooks in `YgoMasterClient/`.
3. Set `ReflectionValidatorValidate: true` — validates every hooked member against `Docs/ReflectionDump.json` at startup and logs signature mismatches *before* they crash a duel.
4. When green, regenerate the dump (`ReflectionValidatorDump: true`) and commit the updated `ReflectionDump.json` + `updatediff.cs`.

### 3. PvP struct offsets (PvP-only crashes/desync; solo fine)

`Settings.json` carries hardcoded offsets into duel.dll objects:
`MultiplayerPvpClientDoCommandUserOffset`, `MultiplayerPvpClientRunDialogUserOffset` (v2.5.0 values: 15504/0x3C90, 15400/0x3C28).

Recovery (x64dbg, per `Docs/UpdatingPvPOffsets.md`): load the game, find `duel.dll`, locate `DLL_DuelListInitString` and `DLL_DUELCOMGetRecommendSide` in the disassembly, read the displacement operands (e.g. `mov dword ptr ds:[rdx+3C28]`), convert hex→decimal, update `Settings.json`.

**LLM-opponent note:** the broker commit path writes through these same PvP surfaces — wrong offsets mean broker commits corrupt state. After any update, run a full live PvP validation (validating-changes gate 5) before trusting LLM duels.

### 4. UnityPlayer RVAs (custom audio/images crash)

Custom asset loading uses RVAs into `UnityPlayer.dll` (`UnityPlayerRVA_AudioClip_CUSTOM_*`, `UnityPlayerRVA_DownloadHandlerTexture_CUSTOM_*`). New UnityPlayer.dll → fetch its PDB and regenerate via `UnityPlayerPdb.Update()` (Windows, uses MSDIA). Wrong RVA = instant crash on first custom sound/image.

## Fresh game data import (cards/solo/shop drift)

When new cards/content ship, `Data/` needs re-import. Condensed from `Docs/Updating.md` (Windows + Fiddler with the YgoMaster inspector plugin):

| Data | Source |
|---|---|
| Card list / ban list | Log `System.info`, `User.entry`, `User.home` responses |
| Card data files | Client console `carddata` command → versioned folder → `Data/CardData/` (English language setting required) |
| Solo content | Log `Solo.info` + per-duel `Duel.begin`/`Solo.detail` |
| Shop | Fresh account + console commands (`solo_clear`, `dismantle_all_cards`, `craft_secrets`, `auto_free_pull`), log `Shop.get_list`, merge with `--mergeshops` |
| Structure decks | `YgoMaster.exe --extractstructure` |
| ItemID / BGM | Console `itemid`; PvP `Duel.begin` log for `Bgm.json` |

**Fork caution:** this fork's LE/Requiem campaign data and server behavior are custom (see editing-campaign-content). Never overwrite `Solo.json`, `SoloDuels/`, or fork-modified `Settings.json`/`Shop.json` wholesale with upstream data — merge selectively (`Docs/UpdatingLeMod.md` documents the selective-merge convention), then run the three campaign validators.

## After recovery — verify in this order

1. Solution build (redirected output) + net8 harness (validating-changes).
2. Solo duel launches and completes on the game machine.
3. PvP room duel between two local clients completes.
4. LLM broker live validation with analyzer thresholds.
5. Commit the regenerated `ReflectionDump.json`, `updatediff.cs`, new offsets, and a dated vault Progress entry under `/home/rakeenhuq/Onedrive/Obsidian Vault/projects/ygomaster/work/` (or a new work item) recording game version → what broke → what changed.

## Common mistakes

| Mistake | Reality |
|---|---|
| Debugging hook code before checking for a game update | Updates are the #1 cause of sudden breakage. Check SteamDB/Steam first. |
| Fixing the version gate and shipping | Classes 2–4 fail independently and later. Run the full verify ladder. |
| Copying upstream YgoMaster release binaries/data to "fix" it | Upstream lacks this fork's LE server behavior; campaign data will half-work. Rebuild from this repo. |
| Letting Steam auto-update mid-development | Pin the game version until you have time for recovery. |

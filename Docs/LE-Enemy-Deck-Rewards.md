# Link Evolution Enemy-Deck Card Rewards

This document records the local YgoMaster modification that grants random cards from the opponent's deck after winning Link Evolution solo duels — including repeat wins on already-cleared chapters.

## Goal

Make collecting LE campaign cards faster by awarding **up to 3 cards per win** drawn from the **CPU opponent's deck**, without relying solely on shop pack unlocks and purchases.

## Summary

| Item | Value |
|------|-------|
| Cards per win | Up to 3 (one per reward entry) |
| Card source | Opponent deck in `YgoMaster/Data/SoloDuels/{chapterId}.json` → `Deck[1]` |
| Repeat wins | Yes — works on chapters already marked `COMPLETE` |
| Ownership cap | `cardOwnedLimit: 3` — no drops for cards you already own 3+ copies of |
| Chapters covered | 670 across gates 1100, 1101–1106, 1111–1116, 1121 |
| Implementation | Data-only (`extraRewards` in `YgoMaster/Data/Solo.json`) |
| Server rebuild required | No |

## How it works

YgoMaster processes solo duel wins in `SoloUpdateChapterStatus` (compiled into `YgoMaster.exe`). Before checking whether chapter progress changed, the server reads an optional per-chapter `extraRewards` block from `YgoMaster/Data/Solo.json` and calls `GiveDuelReward`.

Because `extraRewards` runs **before** the early-return for already-complete chapters, rewards fire on **every win**, not only first clear.

```text
Duel.end (solo win)
  -> SoloUpdateChapterStatus
       -> extraRewards (always, if present)
       -> chapter status / unlock_secret (first clear only)
  -> GiveDuelReward (global gems/CP from Settings.json)
```

### Reward entry format

Each LE chapter has:

```json
"extraRewards": {
  "win": [
    {
      "type": "Card",
      "ids": [<unique opponent card IDs>],
      "rate": 100,
      "cardOwnedLimit": 3
    },
    { "...same structure..." },
    { "...same structure..." }
  ]
}
```

- **`ids`**: deduplicated list of opponent Main + Extra + Side card IDs from `SoloDuels`
- **`rate: 100`**: guaranteed roll for that entry
- **`cardOwnedLimit: 3`**: only cards with **fewer than 3 owned copies** are eligible; see [Ownership cap](#ownership-cap-cardownedlimit-3)
- Three `win` entries = up to three separate card drops per win

Upstream reference: [pixeltris/YgoMaster `Act_Solo.cs`](https://github.com/pixeltris/YgoMaster/blob/master/YgoMasterServer/Acts/Act_Solo.cs), [`Act_Duel.cs` → `GiveDuelRewardImpl`](https://github.com/pixeltris/YgoMaster/blob/master/YgoMasterServer/Acts/Act_Duel.cs), [`DuelRewardInfo.cs`](https://github.com/pixeltris/YgoMaster/blob/master/YgoMasterServer/Infos/DuelRewardInfo.cs).

## Scope: which duels are affected

| Gate | Content | Chapters |
|------|---------|----------|
| 1100 | Starter structure-deck gate | 5 |
| 1101 | Duel Monsters campaign | 98 |
| 1102 | GX campaign | 78 |
| 1103 | 5D's campaign | 82 |
| 1104 | ZEXAL campaign | 58 |
| 1105 | ARC-V campaign | 72 |
| 1106 | VRAINS campaign | 58 |
| 1111–1116 | Duelist challenge gates | 217 |
| 1121 | Mystery LE duels | 2 |
| **Total** | | **670** |

Opponent deck index: **`Deck[1]`** in each `YgoMaster/Data/SoloDuels/{chapterId}.json` file (`Deck[0]` is the player loaner deck).

## In-game behavior

### Repeat wins

If a chapter is already `COMPLETE` in `YgoMaster/Data/Players/*/Player.json` → `SoloChapters`, you still receive enemy-deck card drops.

### Ownership cap (`cardOwnedLimit: 3`)

YgoMaster filters the drop pool per roll:

- **0–2 copies owned** → card can drop
- **3+ copies owned** → card excluded from that roll's pool

Each of the three `win` entries rolls independently. Possible outcomes:

- Still farming: usually **3 cards** per win
- Partially collected: **1–2 cards** (only missing cards remain in pool)
- Fully collected (3 of every card in that opponent deck): **0 cards** — rolls find an empty pool and skip silently

### Duplicates within one win

Even with `cardOwnedLimit: 3`, the same card can appear in **multiple** of the three rolls in a single win while you own fewer than 3 copies (roughly 10–15% chance for typical ~25-card pools).

### Collection vs deck building

- **Collection**: can hold up to 3 copies per card via these drops (cap enforced at reward time)
- **Deck editor**: normal Master Duel rule — max 3 copies per card in a deck (`DeckEditorDisableLimits` is `false` in client settings)

### Card finish and dismantling

- Drops are **Normal** finish (not shine/royal from the duel file)
- Cards are **dismantleable** (`DisableNoDismantle: false` in `YgoMaster/Data/Settings.json`)

### What does NOT repeat on farm runs

| Reward type | Repeat win? |
|-------------|-------------|
| Enemy-deck `extraRewards` cards | Yes |
| Generic `DuelRewards` (gems, CP) | Yes (`ChapterStatusChangedNoRewards: false`) |
| `unlock_secret` shop packs | No — first clear only |
| Gate-clear Campaign/Extra packs | No — first clear only |

## Files involved

### Modified by this feature

| File | Change |
|------|--------|
| [`YgoMaster/Data/Solo.json`](../YgoMaster/Data/Solo.json) | `extraRewards` on 670 LE chapters (~336 KB added) |
| [`YgoMaster/Data/Settings.json`](../YgoMaster/Data/Settings.json) | `SoloRewardsInDuelResult: true` (show drops on result screen) |
| [`Tools/inject_le_enemy_deck_rewards.py`](../Tools/inject_le_enemy_deck_rewards.py) | Injector script |
| [`Tools/validate_le_enemy_deck_rewards.py`](../Tools/validate_le_enemy_deck_rewards.py) | Validator script |
| [`.gitignore`](../.gitignore) | Git tracking rules for this mod install |

### Read by injector (not modified)

| File | Role |
|------|------|
| `YgoMaster/Data/SoloDuels/{chapterId}.json` | Opponent deck card IDs |
| `YgoMaster/Data/CardList.json` | Validator checks all IDs exist |

### On-disk backups (not in git)

Timestamped copies created on each `--apply`:

```text
YgoMaster/Data/Solo.json.bak-enemy-rewards-YYYYMMDD-HHMMSS
```

Examples from initial rollout:

- `YgoMaster/Data/Solo.json.bak-enemy-rewards-20260622-121952`
- `YgoMaster/Data/Solo.json.bak-enemy-rewards-20260622-122323`

## Tools

### Inject rewards

```bash
cd "/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMaster"

# Preview changes
python3 Tools/inject_le_enemy_deck_rewards.py --dry-run

# Write Solo.json (creates timestamped backup)
python3 Tools/inject_le_enemy_deck_rewards.py --apply
```

Configurable constants in the injector (edit script to change behavior):

```python
CARD_REWARD_COUNT = 3      # number of win entries
CARD_REWARD_RATE = 100     # percent chance per entry
CARD_OWNED_LIMIT = 3       # skip cards with this many copies already owned
```

### Validate

```bash
python3 Tools/validate_le_enemy_deck_rewards.py
```

Checks all 670 chapters for correct `extraRewards` structure, deduped `ids`, `cardOwnedLimit: 3`, and `CardList.json` coverage.

### After any data change

```bash
python3 Tools/inject_le_enemy_deck_rewards.py --apply
python3 Tools/validate_le_enemy_deck_rewards.py
# restart YgoMaster.exe
```

## Pipeline order (important)

Run the injector **last** after any tool that rewrites `YgoMaster/Data/Solo.json`:

```text
import_requiem_optional_duels.py
import_requiem_challenge_decks.py
linearize_requiem_campaign_layout.py
compact_requiem_campaign_layout.py
add_requiem_reverse_duels.py
  ↓
inject_le_enemy_deck_rewards.py --apply
  ↓
validate_le_enemy_deck_rewards.py
  ↓
restart YgoMaster.exe
```

Requiem and layout importers do **not** add `extraRewards`; re-running them without re-injecting will remove or leave stale reward data.

## Git version control

Git was initialized on 2026-06-22 for this YgoMaster LE install so reward and config changes can be reverted.

### Commit history

```text
742a1fc feat: cap LE enemy-deck drops at 3 owned copies per card
f877ad8 feat: grant 3 enemy-deck cards on every LE solo win
54b1071 chore: initialize git tracking for YgoMaster LE mod
```

### What git tracks

- `Tools/`, `Docs/`, `YgoMaster/Data/Solo.json`, `YgoMaster/Data/Settings.json`, `YgoMaster/Data/SoloDuels/`, and other campaign config JSON
- Excludes: `*.exe`, `*.dll`, `YgoMaster/Data/Players/` (saves), `YgoMaster/Data/Backups/`, `YgoMaster/Data/ClientData/`, binary card assets

### Revert commands

**Undo only the 3-copy cap** (restore unlimited farming):

```bash
git revert 742a1fc --no-edit
```

**Undo the entire enemy-deck reward feature** (back to pre-feature baseline):

```bash
git revert 742a1fc f877ad8 --no-edit
```

**Restore specific files from baseline commit:**

```bash
git checkout 54b1071 -- YgoMaster/Data/Solo.json YgoMaster/Data/Settings.json
```

After any revert: restart `YgoMaster.exe`.

## Settings reference

In [`YgoMaster/Data/Settings.json`](../YgoMaster/Data/Settings.json):

```json
"SoloRewardsInDuelResult": true,
"SoloRewardsInDuelResultAreRare": false,
"ChapterStatusChangedNoRewards": false,
"DisableNoDismantle": false
```

- `SoloRewardsInDuelResult: true` — chapter/extra card drops appear on the duel result screen
- `ChapterStatusChangedNoRewards: false` — generic gem/CP rewards still drop on repeat wins

## Smoke test

1. Restart `YgoMaster.exe` after editing `Solo.json` or `Settings.json`
2. Pick an LE chapter already at `COMPLETE` in your player save
3. Win the duel
4. Confirm up to 3 cards appear on the result screen and in your collection
5. Re-run the same duel after owning 3 of every card in that deck — expect 0 card drops

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| No card drops at all | `YgoMaster.exe` not restarted after `Solo.json` edit | Restart server |
| Drops stopped after Requiem import | `Solo.json` rewritten without re-injecting | Run injector `--apply` again |
| Fewer than 3 cards | `cardOwnedLimit: 3` — most of deck already at 3 copies | Expected; farm other duels or dismantle extras |
| Validator fails on `ids` | `SoloDuels` file changed but `Solo.json` not re-injected | Run injector `--apply` |
| Want unlimited copies again | `cardOwnedLimit` set to 3 | Set `CARD_OWNED_LIMIT = 0` in injector and re-apply, or `git revert 742a1fc` |

## Alternative approach (not used)

A server-source patch to [`BernardQuek/YgoMaster` `le_combined`](https://github.com/BernardQuek/YgoMaster/tree/le_combined) could read the CPU deck at runtime in `Act_Duel.cs` and grant cards dynamically (smaller `Solo.json`, guaranteed unique picks). This install uses the **data-only** approach to avoid rebuilding `YgoMaster.exe` on Linux.

## Related documentation

- [`README-LOCAL-SETUP.md`](../README-LOCAL-SETUP.md) — Linux/Steam YgoMaster setup
- [`CAMPAIGN-TEXT-AND-ICONS.md`](../CAMPAIGN-TEXT-AND-ICONS.md) — campaign text and portrait editing
- [`Docs/RequiemImport.md`](RequiemImport.md) — Requiem duel import pipeline
- [YgoMaster Settings docs](https://github.com/pixeltris/YgoMaster/blob/master/Docs/Settings.md) — upstream `DuelRewards` and settings reference

## Change log

| Date | Change |
|------|--------|
| 2026-06-22 | Initial feature: 670 chapters, 3 card drops per win, git initialized |
| 2026-06-22 | Added `cardOwnedLimit: 3` to stop drops once 3 copies owned |
| 2026-06-22 | This document created |
---
name: editing-campaign-content
description: Use when changing solo/campaign data - Solo.json, SoloDuels, gates, chapters, rewards, LE/Requiem duels, campaign text (IDS_SOLO), character portraits, or running the import/validation tools.
---

# Editing Campaign Content

## Overview

The LE/Requiem campaigns are **data plus fork-specific server behavior**. Content lives in `YgoMaster/Data/`; the server code that interprets it (loaner-deck upgrades, battlefield randomization, enemy-deck rewards) lives in `YgoMasterServer/Acts/Act_Solo.cs`, `Act_Duel.cs`, `ShopInfo.cs`. Upstream binaries + this data = broken. Reference docs: `Docs/SoloFileFormat.md` (schema), `Docs/RequiemImport.md` (import history), `Docs/LE-Enemy-Deck-Rewards.md`, `Docs/Campaign-Text-And-Icons.md`.

## Data model in one screen

| Artifact | Role |
|---|---|
| `Data/Solo.json` (2.4 MB) | Campaign backbone: `gate` / `chapter` / `unlock` / `unlock_item` / `reward` maps, plus this fork's per-chapter `extraRewards`. Chapter ID = `gate_id * 10000 + seq` (e.g. gate 1101 → 11010065). |
| `Data/SoloDuels/<chapterId>.json` | Per-duel config. `Deck[0]` = player loaner, `Deck[1]` = opponent deck (also the source pool for enemy-deck rewards). |
| `Data/ClientData/IDS/IDS_SOLO.txt` | Chapter story text: `[IDS_SOLO.CHAPTER<id>_EXPLANATION]`. Quality gates enforced by validators: 180–520 chars, ASCII-only, no placeholder phrasing, no duplicate headers. |
| `Data/ClientData/LinkEvolution/` | Portrait atlas: `chars.png` + `chars.dfymoo` (tile metadata) + `CharNames.json`. Every `p1_img`/`p2_img` in Solo.json must resolve here. |
| `unlock.type` | `2` = CHAPTER_OR, `4` = CHAPTER_AND, `3` = ITEM — the classic silent-lock mistake. |
| LE gates | Story 1101–1106, challenge 1111–1116, plus 1100/1121. 670 LE chapters carry `extraRewards` (exactly 3 entries, `rate: 100`, `cardOwnedLimit: 3`, ids from `Deck[1]`, all present in `CardList.json`). |

## Iron rules

1. **Pipeline order is fixed.** The import tools rewrite Solo.json and do not preserve each other's output. If you re-run any stage, everything after it must re-run, and `inject_le_enemy_deck_rewards.py` is **always last**:
   `import_requiem_optional_duels.py` → `import_requiem_challenge_decks.py` → `linearize_requiem_campaign_layout.py` → `add_requiem_reverse_duels.py` → `inject_le_enemy_deck_rewards.py --apply`.
2. **Dry-run first, backup always.** Every tool defaults to dry-run and writes timestamped backups under `Data/Backups/` on `--apply`. Never re-apply an importer over already-imported content — it duplicates chapters.
3. **Validate before declaring done, then restart `YgoMaster.exe`:**
   ```bash
   python3 Tools/validate_requiem_campaigns.py
   python3 Tools/validate_le_enemy_deck_rewards.py
   python3 Tools/validate_campaign_portraits.py
   python3 -m unittest discover -s Tools -p 'test_*.py'   # includes test_le_requiem_repository.py, the integration gate
   ```
   `test_le_requiem_repository.py` pins exact chapter counts per gate — adding/removing a chapter means updating it deliberately.
4. **Card IDs go through two mappings** (LE `.ydc` → passcode → Master Duel internal ID via `Data/YdkIds.txt`), and ~17 cards don't exist in Master Duel — explicit substitutions live in the importers. Validate any new deck's IDs against `CardList.json`.
5. **No parent cycles.** Chapter `parent_chapter` links form the campaign graph; the linearizer enforces the three-row layout. `validate_requiem_campaigns.py` catches cycles — run it after any manual parent edits.

## Adding a single new duel (manual path)

1. Pick a free chapter ID in the right gate range; create `Data/SoloDuels/<id>.json` (copy a sibling; set `Deck[1]` opponent deck, optional `Deck[0]` loaner).
2. Add the chapter under `Solo.json` → `chapter.<gateId>.<chapterId>` (`parent_chapter`, `npc_id`, `mydeck_set_id`/`set_id`, `unlock_id`). Icon semantics (duel vs practice vs reward vs lock) are driven by which fields are present — table in `Docs/SoloFileFormat.md`.
3. Add `[IDS_SOLO.CHAPTER<id>_EXPLANATION]` text meeting the quality gates; ensure `p1_img`/`p2_img` exist in the atlas (or extend it per `Docs/Campaign-Text-And-Icons.md`).
4. If it's an LE chapter and should drop enemy cards: re-run `inject_le_enemy_deck_rewards.py --apply`.
5. Update the expected counts in `test_le_requiem_repository.py`, run rule 3's validators, restart the server, verify in game.

## Common mistakes

| Mistake | Reality |
|---|---|
| Editing Solo.json, skipping validators | The validators encode ~all invariants (counts, text quality, parents, portraits, reward shape). They're seconds to run. |
| Running an importer "just to refresh one thing" | Importers assume pristine pre-import state. Restore from `Data/Backups/` first, then run the full ordered pipeline. |
| Rewriting Solo.json with a generic JSON tool | It's large and structured; tools in `Tools/` exist for every bulk operation. Manual edits are for single chapters. |
| Forgetting rewards vanish after other tools touch Solo.json | `extraRewards` injection is last-stage for a reason. |
| Testing with a played save | Chapter unlock state is cached in `Data/Players/*/Player.json`. Test unlock changes on a fresh player or expect stale state. |

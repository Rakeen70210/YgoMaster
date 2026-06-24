# Requiem Campaign and Challenge Import Notes

This document records the work done to import Requiem Link Evolution content into the local YgoMaster campaign data.

Source reference:

- Requiem guide: https://github.com/MoonlitDeath/Link-Evolution-Editing-Guide/wiki/Requiem-Mod-Guide
- Requiem no-animation archive used locally: `/tmp/Requiem_Final_no_animations.rar`
- Extracted archive data used locally:
  - `/tmp/requiem-extract-core`
  - `/tmp/requiem-extract-core/decks`
  - `/tmp/requiem-parsed.json`

## Files Changed

Primary game data changed:

- `YgoMaster/Data/Solo.json`
- `YgoMaster/Data/ClientData/IDS/IDS_SOLO.txt`
- New files under `YgoMaster/Data/SoloDuels/`

Importer and test tooling added:

- `Tools/import_requiem_optional_duels.py`
- `Tools/test_import_requiem_optional_duels.py`
- `Tools/import_requiem_challenge_decks.py`
- `Tools/test_import_requiem_challenge_decks.py`
- `Tools/add_requiem_reverse_duels.py`
- `Tools/test_add_requiem_reverse_duels.py`
- `Tools/validate_requiem_campaigns.py`

Reports:

- `YgoMaster/Data/Backups/requiem-optional-duels-report.json`
- `YgoMaster/Data/Backups/requiem-challenge-decks-report.json`
- `YgoMaster/Data/Backups/requiem-campaign-validation-report.json`
- Dry-run reports now default to:
  - `YgoMaster/Data/Backups/requiem-optional-duels-plan-report.json`
  - `YgoMaster/Data/Backups/requiem-challenge-decks-plan-report.json`

Backups created before applying:

- `YgoMaster/Data/Backups/requiem-optional-duels-20260620-220847/`
- `YgoMaster/Data/Backups/requiem-challenge-decks-20260620-222047/`
- `YgoMaster/Data/Backups/requiem-dm-afterstory-before-20260621-222105/`
- `YgoMaster/Data/Backups/requiem-dm-reverse-icons-before-20260622/`
- `YgoMaster/Data/Backups/requiem-non-dm-reverses-before-20260622-110412/`

## Requiem Story Duels

Imported 40 Requiem bonus story duels into the existing Link Evolution campaign gates as optional duels.

The imported Requiem duel IDs are `186` through `225`.

Gate distribution:

| Gate | Campaign | Added |
| --- | --- | ---: |
| `1101` | Yu-Gi-Oh! | 17 |
| `1102` | GX | 7 |
| `1103` | 5D's | 9 |
| `1104` | ZEXAL | 3 |
| `1105` | ARC-V | 3 |
| `1106` | VRAINS | 1 |

Chapter ranges added:

| Gate | Added chapter IDs |
| --- | --- |
| `1101` | `11010065` - `11010081` |
| `1102` | `11020065` - `11020071` |
| `1103` | `11030065` - `11030073` |
| `1104` | `11040053` - `11040055` |
| `1105` | `11050067` - `11050069` |
| `1106` | `11060057` |

Current placement:

- The Requiem story duels are spliced into the campaign route using a reconciled order.
- Requiem guide numbering controls story order.
- The final Requiem archive controls actual duel names, characters, decks, and replacements where the guide text is stale.
- Original campaign chapter parent links are preserved to avoid route cycles in branched maps.
- Imported Requiem chapters parent to their story anchor, or to the prior inserted Requiem chapter when multiple guide entries share an anchor.
- This replaced earlier attempts: hidden side branches near display anchors, a temporary post-campaign chain, and an unsafe splice that could create parent cycles in GX.
- Duel Monsters has one targeted after-story exception: `Sera vs Anubis`, `Dark Side of Dimensions Pt. 1`, `Dark Side of Dimensions Pt. 2`, `Shadi Vs. Grandpa`, and `Rebecca Vs Tristan` are chained after the forward Yugi vs Atem chapter `11010063`. The reverse Yugi vs Atem duel still branches from `11010063`, keeping the three-row layout intact while the campaign continues into the late/movie duels.
- The Duel Monsters gate clear chapter is now `11010080`, which makes the new after-story tail the visible end of the campaign route.

Duel Monsters reverse-duel pass:

- Added optional reverse chapters `11010100` through `11010116` for the 17 imported Duel Monsters Requiem story duels.
- Each reverse chapter parents to its matching forward Requiem duel, matching the normal Link Evolution pattern where reverse duels branch from the story duel.
- Each reverse duel swaps `p1_img` / `p2_img`, swaps the two duel names, and swaps the two loaner decks in `YgoMaster/Data/SoloDuels/<chapter>.json`.
- Added matching IDS explanation blocks for the new reverse chapters.

Other-campaign reverse-duel pass:

- Added optional reverse chapters for the 23 imported Requiem story duels in GX, 5D's, ZEXAL, ARC-V, and VRAINS.
- Added chapter ranges:
  - GX: `11020100` - `11020106`
  - 5D's: `11030100` - `11030108`
  - ZEXAL: `11040100` - `11040102`
  - ARC-V: `11050100` - `11050102`
  - VRAINS: `11060100`
- Each reverse chapter parents to its matching forward Requiem duel, keeping the three-row layout as main story spine plus one-level reverse branches.
- Each reverse duel swaps `p1_img` / `p2_img`, swaps the two duel names, and swaps the two loaner decks in `YgoMaster/Data/SoloDuels/<chapter>.json`.
- Added matching IDS explanation blocks for the new reverse chapters.

Portrait cleanup:

- Replaced placeholder atlas tiles for `joeymindcontrolled`, `shadi`, `solomonmuto`, and `tristantaylor` in `YgoMaster/Data/ClientData/LinkEvolution/chars.png`.
- The atlas keys and `chars.dfymoo` coordinates were left unchanged, so existing campaign chapter references continue to work.

Campaign text expansion pass:

- Reviewed all 423 Link Evolution story chapters across gates `1101` through `1106`.
- Expanded the remaining short story explanations in `YgoMaster/Data/ClientData/IDS/IDS_SOLO.txt`, mostly reverse duels plus thin late ZEXAL, ARC-V, and VRAINS entries.
- The pass keeps the existing format: title, story setup, matchup line, and reward text where applicable.
- Reverse-duel text now uses player-facing framing while adding context about why the opposing character is fighting.
- Text remains ASCII-only and avoids changing localization section headers.
- Post-pass coverage check found 0 story explanations under 180 characters and no explanation over 520 characters.

Requiem placeholder cleanup pass:

- Used Antigravity through `peer-agents-mcp` to audit the Requiem-added bonus entries for generic placeholder wording.
- Replaced all 57 Requiem placeholder explanations:
  - 40 forward `Bonus:` campaign duels.
  - 17 Duel Monsters `Reverse:` bonus duels.
- Removed generic phrases such as `This Requiem bonus duel`, `Play the Requiem bonus duel`, `swaps the loaner decks`, and `wider cast`.
- Forward Requiem bonus duels now describe the story situation, character motivation, or side-story premise.
- Duel Monsters Requiem reverse duels now describe the alternate side of the same conflict instead of only saying the decks are swapped.
- Normalized `Téa` to `Tea` in matchup lines so `IDS_SOLO.txt` is fully ASCII.
- Removed player-facing `Requiem` meta wording from the story-gate bonus duel descriptions so they read like campaign entries instead of mod notes.

Guardrail updates:

- `Tools/import_requiem_optional_duels.py` now generates story-specific descriptions for the 40 Requiem story duel IDs instead of the old generic placeholder template.
- `Tools/test_import_requiem_optional_duels.py` checks that generated descriptions do not contain the old placeholder wording.
- `Tools/validate_requiem_campaigns.py` now validates story text quality in addition to campaign structure:
  - No duplicate IDS explanation headers.
  - No missing Link Evolution story explanations.
  - Story explanations must stay within the current length band of 180 to 520 characters.
  - Story explanations must be ASCII-only.
  - Story explanations must not contain the old placeholder phrases.
- `Tools/add_requiem_reverse_duels.py` adds the missing non-Duel Monsters Requiem reverse duels after the story import and layout pass.
- `Tools/test_add_requiem_reverse_duels.py` checks reverse chapter IDs, swapped portraits, swapped duel names, swapped decks, and non-meta reverse descriptions.
- `Tools/validate_requiem_campaigns.py` now validates all Requiem reverse duels:
  - 17 Duel Monsters reverse chapters.
  - 23 GX, 5D's, ZEXAL, ARC-V, and VRAINS reverse chapters.
  - Reverse chapters must parent to their forward chapter.
  - Reverse chapters must swap portraits, duel names, and loaner decks.
  - Reverse IDS explanation blocks and `YgoMaster/Data/SoloDuels/<chapter>.json` files must exist.

Validation pass:

- `Tools/validate_requiem_campaigns.py` checks the current imported campaign state against `YgoMaster/Data/Backups/requiem-optional-duels-report.json`.
- The validation confirms all 40 Requiem story duels are present in `YgoMaster/Data/Solo.json`.
- The validation confirms all 17 Duel Monsters Requiem reverse duels are present with swapped portraits, names, and decks.
- The validation confirms all 23 non-Duel Monsters Requiem reverse duels are present with swapped portraits, names, and decks.
- Gate distribution is `17 / 7 / 9 / 3 / 3 / 1` for DM, GX, 5D's, ZEXAL, ARC-V, and VRAINS.
- Within each campaign gate, imported Requiem duels are in guide-order sequence.
- Parent links match the reconciled placement report, except for the documented Duel Monsters three-row story spine and after-story tail.
- All imported story duel files, IDS text blocks, and portrait keys are present.
- LE story and challenge gates are checked for parent cycles.
- Latest validation report: `YgoMaster/Data/Backups/requiem-campaign-validation-report.json`.

Implementation details:

- Each story duel is inserted according to the reconciled guide/archive table in `YgoMaster/Data/Solo.json` object order.
- Within each campaign, the inserted Requiem duels remain in guide-number order, with final-archive replacements applied.
- Each imported chapter has `unlock_id: 0`, so the duel is optional/unlocked.
- Each imported chapter uses `npc_id: 1`, `set_id: 1`, and `mydeck_set_id: 1`, matching the local Link Evolution story-duel format.
- Each imported chapter has `p1_img` and `p2_img` set from Requiem character codes.
- Each new `YgoMaster/Data/SoloDuels/<chapter>.json` file contains two Requiem-converted loaner decks.
- Each imported chapter has an IDS text block appended to `YgoMaster/Data/ClientData/IDS/IDS_SOLO.txt`.

Portrait atlas update:

- 33 missing Requiem character portraits were extracted from `pdui/dialog_chars/*_neutral.png` in the Requiem archive.
- These portraits were appended to `YgoMaster/Data/ClientData/LinkEvolution/chars.png`.
- Matching entries were appended to `YgoMaster/Data/ClientData/LinkEvolution/chars.dfymoo`.
- Matching names were appended to `YgoMaster/Data/ClientData/LinkEvolution/CharNames.json`.

Important reconciliations:

- Guide slot 13 has stale text about `Paradox vs Anubis`; the final archive and changelog use `Sera vs Anubis`.
- `Fonda vs Sarina` uses the final archive's combined replacement for the older separate GX guide rows.
- `Shadow Rider Duel` uses the final archive's replacement slot in GX.
- `Kuriboh Vs Banner` is kept after Jasmine/Mindy according to guide numbering, even though the archive display value sorts it earlier.

## Requiem Challenge Decks

Imported 59 Requiem challenge decks into the existing Link Evolution challenge gates.

The guide mentioned 56 challenge decks, but the extracted final Requiem archive contains 59 Requiem-specific `_hard` deck entries. All 59 were imported.

Gate distribution:

| Gate | Campaign | Added |
| --- | --- | ---: |
| `1111` | Yu-Gi-Oh! Challenge | 18 |
| `1112` | GX Challenge | 16 |
| `1113` | 5D's Challenge | 14 |
| `1114` | ZEXAL Challenge | 3 |
| `1115` | ARC-V Challenge | 6 |
| `1116` | VRAINS Challenge | 2 |

Chapter ranges added:

| Gate | Added chapter IDs |
| --- | --- |
| `1111` | `11110025` - `11110042` |
| `1112` | `11120028` - `11120043` |
| `1113` | `11130030` - `11130043` |
| `1114` | `11140029` - `11140031` |
| `1115` | `11150032` - `11150037` |
| `1116` | `11160020` - `11160021` |

Implementation details:

- Each challenge chapter uses the existing local challenge-duel format.
- Player deck is empty, so the player uses their own deck.
- Opponent deck is the converted Requiem `_hard` deck.
- Each imported challenge chapter has `unlock_id: 0`, `npc_id: 1`, `set_id: 0`, and `mydeck_set_id: 2`.
- Each imported challenge chapter has an IDS text block appended to `YgoMaster/Data/ClientData/IDS/IDS_SOLO.txt`.

Archive filename fix:

- Requiem metadata lists `requiem137_chazz_hard`.
- The actual extracted deck file is `requiem136_chazz_hard.ydc`.
- `Tools/import_requiem_challenge_decks.py` includes an explicit alias so Chazz's challenge deck is imported from the real file.

## Card Conversion

Requiem decks are Link Evolution `.ydc` files. The importers convert them to Master Duel internal card IDs by using:

- Link Evolution internal card ID to passcode mapping: `/tmp/Lotd-src/Lotd/YdkIds.txt`
- Passcode to Master Duel internal ID mapping: `YgoMaster/Data/YdkIds.txt`
- Card availability validation: `YgoMaster/Data/CardList.json`

The `.ydc` parser reads:

- 8-byte header
- Main deck count and card IDs
- Extra deck count and card IDs
- Side deck count and card IDs

The importers preserve Main, Extra, and Side sections during conversion.

## Explicit Card Substitutions

Some Requiem / Link Evolution cards do not exist in the local Master Duel card data. These are substituted explicitly in `Tools/import_requiem_optional_duels.py`.

Story-duel substitutions:

| Missing card | Replacement |
| --- | --- |
| Spellbinding Circle | Shadow Spell |
| Mooyan Curry | Dian Keto the Cure Master |
| Question | The Shallow Grave |
| Bait Doll | Nobleman of Extermination |
| Double Spell | Spell Reproduction |
| Convulsion of Nature | Field Barrier |

Challenge-deck substitutions:

| Missing card | Replacement |
| --- | --- |
| Blue-Eyes Alternative Ultimate Dragon | Blue-Eyes Ultimate Dragon |
| Red-Eyes Alternative Black Dragon | Red-Eyes Darkness Metal Dragon |
| Magician of Black Chaos MAX | Magician of Black Chaos |

Reports record the exact substitution counts:

- Story duels: 14 substituted card instances
- Challenge decks: 3 substituted card instances

## Verification Performed

Tests run:

```bash
python3 Tools/test_import_requiem_optional_duels.py
python3 Tools/test_import_requiem_challenge_decks.py
python3 Tools/test_add_requiem_reverse_duels.py
```

All three test suites passed.

Story-duel strict audit verified:

- 40 expected Requiem story duels are present.
- 40 expected Requiem story reverse duels are present: 17 for Duel Monsters and 23 across GX, 5D's, ZEXAL, ARC-V, and VRAINS.
- Imported chapters are optional/unlocked.
- Story chapters are spliced into explicit story-order spines for each Link Evolution campaign.
- Requiem campaign layout is linearized into a three-row pattern: forward duels form the main story spine, and reverse duels are one-level side branches.
- Every campaign uses corrected story-order spine validation because the Requiem guide `display` values are original-duel indexes, while YgoMaster stores forward and reverse duels as separate chapters.
- All imported Requiem `p1_img` / `p2_img` keys exist in the Link Evolution portrait atlas metadata.
- Former placeholder campaign portraits have real atlas art and are checked by `Tools/validate_campaign_portraits.py`.
- New `SoloDuels` deck files match converted Requiem source decks.
- Deck sizes are legal.
- IDS text blocks exist.
- No missing card groups remain.

Challenge-deck strict audit verified:

- 59 expected Requiem challenge decks are present.
- Imported challenge chapters are optional/unlocked.
- Player deck is empty for each challenge.
- Opponent deck matches converted Requiem source deck.
- Deck sizes are legal.
- IDS text blocks exist.
- No missing card groups remain.

## Re-running the Imports

Dry run story-duel import:

```bash
python3 Tools/import_requiem_optional_duels.py
```

Apply story-duel import:

```bash
python3 Tools/import_requiem_optional_duels.py --apply --report YgoMaster/Data/Backups/requiem-optional-duels-report.json
```

Force the story-duel map into the three-row layout after importing:

```bash
python3 Tools/linearize_requiem_campaign_layout.py --apply
```

Add missing Requiem reverse duels for GX, 5D's, ZEXAL, ARC-V, and VRAINS after importing:

```bash
python3 Tools/add_requiem_reverse_duels.py --apply
```

Validate campaign portrait coverage:

```bash
python3 Tools/validate_campaign_portraits.py
```

Dry run challenge-deck import:

```bash
python3 Tools/import_requiem_challenge_decks.py
```

Apply challenge-deck import:

```bash
python3 Tools/import_requiem_challenge_decks.py --apply --report YgoMaster/Data/Backups/requiem-challenge-decks-report.json
```

Important: the importers choose the next available chapter IDs from the current `YgoMaster/Data/Solo.json`. Re-running `--apply` after the content is already imported can create duplicates unless the existing import is removed or restored from backup first. If story duels are regenerated, rerun `Tools/linearize_requiem_campaign_layout.py --apply` afterward so the campaign map stays on the main row plus top/bottom reverse rows.

## Useful Audit Files

For exact per-entry lists, use:

- `YgoMaster/Data/Backups/requiem-optional-duels-report.json`
- `YgoMaster/Data/Backups/requiem-challenge-decks-report.json`

These reports include chapter IDs, gates, titles, character names, deck file names, missing-card results, and substitution records.

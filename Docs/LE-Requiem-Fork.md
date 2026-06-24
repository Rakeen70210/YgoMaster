# YgoMaster LE + Requiem Fork

This branch combines the current YgoMaster source tree with the playable
Link Evolution campaign configuration used by LE Combined and the local
Requiem campaign extensions.

## Included

- Link Evolution campaign, challenge, starter, and mystery gates
- Requiem story, reverse, and challenge duels
- Expanded campaign text and character portrait mappings
- Campaign portrait atlas and pack/shop artwork used by this data set
- Invalid profile-icon cleanup
- Three enemy-deck card rewards per LE win, capped at three owned copies
- Requiem import, layout, migration, and validation tools
- LE Combined server behavior, ported separately onto current upstream source

## Repository Layout

- `YgoMaster/Data/`: active game configuration and client assets
- `Tools/`: importers, validators, manifests, and repository tests
- `Docs/RequiemImport.md`: Requiem import details
- `Docs/Campaign-Text-And-Icons.md`: campaign text and portrait notes
- `Docs/LE-Enemy-Deck-Rewards.md`: enemy-deck reward behavior
- `Docs/LE-Requiem-Linux-Setup.md`: local Linux launch notes

Player saves, rollback backups, compiled executables, DLLs, Python bytecode,
and machine-specific Steam configuration are intentionally excluded.

## Provenance

The source code originates from
[pixeltris/YgoMaster](https://github.com/pixeltris/YgoMaster) under the MIT
license.

The campaign data and assets were assembled from community projects discussed
in these upstream issues:

- [Original Link Evolution data import](https://github.com/pixeltris/YgoMaster/issues/1)
- [LE mod feature extensions](https://github.com/pixeltris/YgoMaster/issues/320)
- [YGO Master LE Remaster](https://github.com/pixeltris/YgoMaster/issues/377)
- [LE Combined](https://github.com/pixeltris/YgoMaster/issues/536)

LE Combined credits BernardQuek, KawaiiTinaTenshi, Mizar, Senator John, and
other community contributors. Requiem content was imported from the Requiem
mod and the Link Evolution editing resources referenced in
`Docs/RequiemImport.md`.

The upstream MIT license covers YgoMaster source code. It does not establish
ownership or redistribution permission for every third-party image, game-data
extract, deck list, or text asset included by community mods. Keep that
distinction in mind before publishing or distributing releases from this fork.

## Validation

Run from the repository root:

```bash
python3 -m unittest discover -s Tools -p 'test_*.py'
python3 Tools/validate_requiem_campaigns.py
python3 Tools/validate_le_enemy_deck_rewards.py
python3 Tools/validate_campaign_portraits.py
```

# YgoMaster LE + Requiem

Offline Yu-Gi-Oh! Master Duel (PC) with Link Evolution and Requiem campaign
content.

*Progress is not shared with the live game.*

## About This Fork

This repository is a community fork of
[pixeltris/YgoMaster](https://github.com/pixeltris/YgoMaster). It keeps the
upstream offline server and client while adding the complete LE Combined
campaign data and the Requiem extensions assembled for this project.

Compared with upstream YgoMaster, this fork includes:

- Link Evolution story, challenge, starter, and mystery gates
- Requiem story, reverse, optional, and challenge duels
- Campaign text, character portraits, icons, and pack/shop artwork
- Enemy-deck card rewards for Link Evolution victories
- LE Combined shop, secret unlock, alternate-art, loaner, CPU deck, and
  battlefield behavior ported onto the current upstream source
- Import, migration, sanitation, and validation tools for maintaining the
  combined campaign

The custom data depends on the server changes in this fork. Using an upstream
`YgoMaster.exe` or copying only `Data/` will produce an incomplete setup.

See [Docs/LE-Requiem-Fork.md](Docs/LE-Requiem-Fork.md) for provenance,
included components, validation commands, and third-party asset notes.

## Features

- Create decks and open packs
- Full upstream solo content
- Link Evolution and Requiem campaigns
- Custom CPU duels
- [PvP duels, friends, and trading](Docs/PvP.md)
- Duel replays
- YDK and YDKe support
- Card collection statistics and deck editor improvements

## Requirements

- Yu-Gi-Oh! Master Duel installed through Steam
- The in-game tutorial completed so Master Duel has downloaded all game data
- .NET Framework 4.8
- Git
- Visual Studio 2022 with **Desktop development with C++** to build from source

After Master Duel is fully downloaded, YgoMaster is portable and does not
require Steam on the destination machine.

## Setup

There is not yet a prebuilt release for this fork. Build this repository so
the custom server code and campaign data stay together.

1. Clone the fork:

   ```bash
   git clone https://github.com/Rakeen70210/YgoMaster.git
   cd YgoMaster
   ```

2. On Windows, run:

   ```bat
   Build.bat
   ```

   This builds `YgoMaster.exe`, `YgoMasterClient.exe`,
   `YgoMasterLoader.dll`, and `MonoRun.exe` into the repository's
   `YgoMaster` folder.

3. Open the Master Duel installation directory from Steam using
   **Manage > Browse local files**.

4. Copy the built `YgoMaster` folder itself into the Master Duel installation
   directory. The result should resemble:

   ```text
   Yu-Gi-Oh! Master Duel/
   ├── masterduel.exe
   └── YgoMaster/
       ├── Data/
       ├── YgoMaster.exe
       ├── YgoMasterClient.exe
       └── YgoMasterLoader.dll
   ```

5. Run `YgoMasterClient.exe`. It should start `YgoMaster.exe` automatically.

If the client shows file-load errors, corrupt screens, or an infinite loading
screen, follow [Docs/FileLoadError.md](Docs/FileLoadError.md).

## Linux Setup

1. Complete the build above on Windows or in a Windows build environment.
2. Copy the resulting `YgoMaster` folder into the Master Duel installation.
3. Download
   [YgoMaster-Linux-Data-v1.zip](https://github.com/pixeltris/YgoMaster/releases/download/v1.50/YgoMaster-Linux-Data-v1.zip)
   from upstream and merge its `mono/` directory and `MonoRun.exe` into that
   folder.
4. Follow [Docs/Linux.md](Docs/Linux.md) to add `MonoRun.exe` to Steam and
   launch it through Proton.

The resulting folder must contain this fork's compiled executables and data,
plus the upstream Linux runtime files:

```text
YgoMaster/
├── Data/
├── mono/
├── MonoRun.exe
├── YgoMaster.exe
├── YgoMasterClient.exe
└── YgoMasterLoader.dll
```

The machine-specific launch and troubleshooting record used while developing
this fork is available in
[Docs/LE-Requiem-Linux-Setup.md](Docs/LE-Requiem-Linux-Setup.md).

## Updating

Back up `YgoMaster/Data/Players/` before changing builds. Player saves are
intentionally excluded from Git.

Update and rebuild the fork:

```bash
git pull --ff-only
```

Then run `Build.bat` again and replace the installed binaries and data. Restore
the backed-up `Data/Players/` directory afterward if needed.

Do not replace this fork's executables with binaries from an upstream release;
the LE Combined server behavior is required by the included campaign data.

## Configuration

- [Server settings](Docs/Settings.md)
- [Changing language](Docs/ChangingLanguage.md)
- [Requiem import and maintenance](Docs/RequiemImport.md)
- [Campaign text and portrait notes](Docs/Campaign-Text-And-Icons.md)
- [Enemy-deck reward behavior](Docs/LE-Enemy-Deck-Rewards.md)

The custom duel starter is available through the **DUEL** button on the home
screen. The optional
[VG.TCG.Decks.7z](https://github.com/pixeltris/YgoMaster/releases/download/v1.4/VG.TCG.Decks.7z)
archive provides approximately 6,000 decks from Yu-Gi-Oh! video games.

## Validation

From the repository root:

```bash
python3 -m unittest discover -s Tools -p 'test_*.py'
python3 Tools/validate_requiem_campaigns.py
python3 Tools/validate_le_enemy_deck_rewards.py
python3 Tools/validate_campaign_portraits.py
```

## Related

- [Upstream YgoMaster](https://github.com/pixeltris/YgoMaster)
- [Community mods](https://www.nexusmods.com/yugiohmasterduel/mods)
- [Master Duel modding guide](https://www.nexusmods.com/yugiohmasterduel/articles/3)
- [Master Duel Modding Wiki](https://github.com/SethPDA/MasterDuel-Modding/wiki)
- [MD Replay Editor](https://github.com/crazydoomy/MD-Replay-Editor)

## Screenshots

![YgoMaster screenshot](Docs/Pics/ss1.jpg)
![YgoMaster screenshot](Docs/Pics/ss2.jpg)
![YgoMaster screenshot](Docs/Pics/ss3.jpg)
![YgoMaster screenshot](Docs/Pics/ss4.jpg)
![YgoMaster screenshot](Docs/Pics/ss5.jpg)
![YgoMaster screenshot](Docs/Pics/ss6.jpg)
![YgoMaster screenshot](Docs/Pics/ss7.jpg)

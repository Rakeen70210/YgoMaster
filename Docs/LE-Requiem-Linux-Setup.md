# YgoMaster + LE Combined Local Setup

This document records the working setup on this machine for running YgoMaster on Linux/Steam and playing the Link Evolution-style campaign content through the LE Combined mod.

## Final Working Result

The Steam library shortcut named `YgoMaster` launches this script:

```text
/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMaster/YgoMasterLaunch.sh
```

That script launches:

```text
Proton 10.0 -> MonoRun.exe -> YgoMaster.exe
systemd user services -> socat LAN forwarders for 4988/4989
Proton 10.0 -> MonoRun.exe -> YgoMasterClient.exe -> masterduel.exe
```

The active YgoMaster install now contains LE Combined v1.3.0, including:

```text
Data/ClientData/LinkEvolution
Data/Settings.json
Data/Solo.json
YgoMaster.exe
YgoMasterClient.exe
YgoMasterLoader.dll
```

The shortcut should have no Steam launch options.

## Installed Locations

Master Duel install:

```text
/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel
```

YgoMaster install:

```text
/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMaster
```

Steam shortcut config:

```text
~/.local/share/Steam/userdata/122613594/config/shortcuts.vdf
```

Steam compatibility data used by the launcher:

```text
~/.local/share/Steam/steamapps/compatdata/2900371502
```

Proton used by the launcher:

```text
~/.local/share/Steam/steamapps/common/Proton 10.0/proton
```

## Files Currently Required In YgoMaster Folder

These are the important files/folders in the working setup:

```text
Data/
mono/
MonoRun.exe
YgoMaster.exe
YgoMasterClient.exe
YgoMasterLaunch.sh
YgoMasterLoader.dll
```

`Data/`, `YgoMaster.exe`, `YgoMasterClient.exe`, and `YgoMasterLoader.dll` came from LE Combined v1.3.0.

`mono/` and `MonoRun.exe` came from the official YgoMaster Linux data package.

`YgoMasterLaunch.sh` was created locally to make Steam launch YgoMaster correctly on Linux.

## Sources Used

Base YgoMaster project:

```text
https://github.com/pixeltris/YgoMaster
```

Official Linux support package used:

```text
https://github.com/pixeltris/YgoMaster/releases/download/v1.50/YgoMaster-Linux-Data-v1.zip
```

LE Combined GitHub issue:

```text
https://github.com/pixeltris/YgoMaster/issues/536
```

LE Combined v1.3.0 download link from that issue:

```text
https://mega.nz/file/JZFCFJbT#T7aOZi5j39CyorL2KWtp46Os663kjJpl0L10RiHM5Ew
```

The LE Combined issue states that the archive includes a full `YgoMaster` folder and that its included client/server should be used for the custom content/features to work correctly.

## What We Installed

### 1. Base YgoMaster

The base YgoMaster folder was placed inside the Master Duel install folder:

```text
.../Yu-Gi-Oh!  Master Duel/YgoMaster
```

The base setup originally had:

```text
Data/
YgoMaster.exe
YgoMasterClient.exe
YgoMasterLoader.dll
```

### 2. Linux Support Files

The official Linux data zip was downloaded:

```bash
curl -L -o /tmp/YgoMaster-Linux-Data-v1.zip \
  https://github.com/pixeltris/YgoMaster/releases/download/v1.50/YgoMaster-Linux-Data-v1.zip
```

It provided:

```text
mono/
MonoRun.exe
```

Those files were merged into the active YgoMaster folder.

### 3. Local Linux Launch Script

The following launcher was created as `YgoMasterLaunch.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

steam_root="${HOME}/.local/share/Steam"
proton="${steam_root}/steamapps/common/Proton 10.0/proton"
compat_data="${steam_root}/steamapps/compatdata/2900371502"
game_dir="/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMaster"

export STEAM_COMPAT_CLIENT_INSTALL_PATH="${steam_root}"
export STEAM_COMPAT_DATA_PATH="${compat_data}"

mkdir -p "${compat_data}"
cd "${game_dir}"

"${proton}" run "${game_dir}/MonoRun.exe" YgoMaster.exe &
sleep 3

systemctl --user stop ygomaster-forward-4989.service ygomaster-forward-4988.service >/dev/null 2>&1 || true
systemd-run --user --unit=ygomaster-forward-4989 --collect socat TCP-LISTEN:4989,bind=192.168.68.83,reuseaddr,fork TCP:127.0.0.1:4989 >/dev/null
systemd-run --user --unit=ygomaster-forward-4988 --collect socat TCP-LISTEN:4988,bind=192.168.68.83,reuseaddr,fork TCP:127.0.0.1:4988 >/dev/null

exec "${proton}" run "${game_dir}/MonoRun.exe" YgoMasterClient.exe
```

It was made executable:

```bash
chmod +x YgoMasterLaunch.sh
```

This script is needed because launching `YgoMasterClient.exe` or `MonoRun.exe` directly through Steam did not reliably start the server/client chain on Linux. For WAN PvP, the game server remains bound to localhost for the local game client, while user-systemd `socat` forwarders expose the ports on the LAN IP.

### 4. Steam Shortcut Setup

The Steam Non-Steam shortcut named `YgoMaster` was changed to launch:

```text
"/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMaster/YgoMasterLaunch.sh"
```

Its `StartDir` was set to:

```text
/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMaster/
```

Its launch options were cleared:

```text

```

Steam does not need a specific Proton version set for this shortcut because the launcher script explicitly runs Proton 10.0.

### 5. LE Combined v1.3.0 Download

The LE Combined archive was downloaded from the Mega link in GitHub issue #536.

It was saved as:

```text
/tmp/YgoMaster LE Combined v1.3.0.zip
```

The archive was inspected and confirmed to contain:

```text
YgoMaster LE Combined v1.3.0/
YgoMaster LE Combined v1.3.0/CHANGELOG.txt
YgoMaster LE Combined v1.3.0/YgoMaster/Data/
YgoMaster LE Combined v1.3.0/YgoMaster/YgoMaster.exe
YgoMaster LE Combined v1.3.0/YgoMaster/YgoMasterClient.exe
YgoMaster LE Combined v1.3.0/YgoMaster/YgoMasterLoader.dll
```

It was extracted to:

```text
/tmp/ygomaster-le-combined-v1.3.0-extract
```

### 6. LE Combined Install

Before installing the mod, the original base YgoMaster payload was moved into a backup folder:

```text
backup-base-before-le-combined-20260619-123444
```

That backup contains the old:

```text
Data/
YgoMaster.exe
YgoMasterClient.exe
YgoMasterLoader.dll
```

Then these LE Combined files were copied into the active YgoMaster folder:

```text
Data/
YgoMaster.exe
YgoMasterClient.exe
YgoMasterLoader.dll
```

The Linux support files were left in place:

```text
mono/
MonoRun.exe
YgoMasterLaunch.sh
```

## Verification Performed

The active folder was checked and showed:

```text
Data/
MonoRun.exe
YgoMaster.exe
YgoMasterClient.exe
YgoMasterLaunch.sh
YgoMasterLoader.dll
backup-base-before-le-combined-20260619-123444/
mono/
```

The LE Combined content was confirmed by checking:

```text
Data/ClientData/LinkEvolution
```

The LE Combined settings were confirmed in:

```text
Data/Settings.json
```

Including:

```json
"UpgradeLoanerDeckToOwnedAltArts": true
"RandomiseLEChapterBattlefield": true
"UsePlayerBattlefieldForLEChapter": false
```

A launch smoke test was run through `YgoMasterLaunch.sh`. It successfully started:

```text
MonoRun.exe YgoMaster.exe
masterduel.exe
```

The test processes were then stopped so Steam could launch cleanly afterward.

## Backups Created

YgoMaster payload backup before installing LE Combined:

```text
/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMaster/backup-base-before-le-combined-20260619-123444
```

Steam shortcut backups created during setup:

```text
~/.local/share/Steam/userdata/122613594/config/shortcuts.vdf.bak-ygomaster
~/.local/share/Steam/userdata/122613594/config/shortcuts.vdf.bak-script-ygomaster
~/.local/share/Steam/userdata/122613594/config/shortcuts.vdf.bak-le-wrapper
```

Steam config backups created during setup:

```text
~/.local/share/Steam/userdata/122613594/config/localconfig.vdf.bak-ygomaster
~/.local/share/Steam/config/config.vdf.bak-ygomaster
```

## How To Launch

Preferred official-app launch:

1. Open Steam.
2. Launch the official `Yu-Gi-Oh! Master Duel` library entry.
3. Steam runs `YgoMasterLaunch.sh` through the official app launch options.
4. Steam should show you as playing `Yu-Gi-Oh! Master Duel`.

Fallback Non-Steam shortcut:

1. Open Steam.
2. Go to the `YgoMaster` Non-Steam shortcut.
3. Click Play.
4. The shortcut should run `YgoMasterLaunch.sh`.
5. The script starts YgoMaster through Proton 10.0.
6. Master Duel should open with YgoMaster active.

Do not manually set the shortcut target back to `YgoMasterClient.exe`.

Do not put `YgoMasterClient.exe` in Steam launch options for this final setup.

The official Master Duel app launch options were set to:

```text
"/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMaster/YgoMasterLaunch.sh" %command%
```

Backup created before this official-app conversion:

```text
~/.local/share/Steam/userdata/122613594/config/localconfig.vdf.bak-masterduel-official-ygomaster
```

## Expected In-Game Campaign Flow

LE Combined adds Link Evolution-style content through YgoMaster's solo/custom content system.

Based on GitHub issue #536, the mod includes:

```text
Starter deck gate
Link Evolution campaign gates
Duelist/challenge gates
Campaign-related packs
Custom pack images
Progression/unlocks tied to solo chapters
```

The campaign content should appear through Solo mode gates after the mod's starting progression/tutorial flow.

## Local Campaign Gate Tweak

On 2026-06-19, `Data/Solo.json` was edited so the main Link Evolution campaign gates are available from the start without marking every chapter complete.

Changed gates:

```text
1002-1006
1102-1106
```

For those gates, `unlock_id` and `view_gate` were set to `0`.

This means these campaigns should be directly accessible:

```text
Campaign
Campaign: GX
Campaign: 5D's
Campaign: ZEXAL
Campaign: ARC-V
Campaign: VRAINS
```

This did not enable `UnlockAllSoloChapters`; that setting remains:

```json
"UnlockAllSoloChapters": false
```

Backup created before this tweak:

```text
Data/Solo.json.bak-campaign-unlock-20260619-223223
```

## Local Starter Structure Deck Tweak

On 2026-06-19, the five LE starter structure decks were made available without needing to complete `A New Beginning`.

Added to `Data/Settings.json` `DefaultItems`:

```text
1129001 Starter Deck: Basic
1129002 Starter Deck: Fusion
1129003 Starter Deck: Synchro
1129004 Starter Deck: Xyz
1129005 Starter Deck: Pendulum
```

Current player saves were also patched to own those structure deck item IDs and the card contents from all five starter decks:

```text
Data/Players/8591019/Player.json
Data/Players/Local/Player.json
```

Backups created before this tweak:

```text
Data/Settings.json.bak-starter-structures-20260619-224046
Data/Players/8591019/Player.json.bak-starter-structures-20260619-224046
Data/Players/Local/Player.json.bak-starter-structures-20260619-224046
```

## Restoring The Previous Base YgoMaster Payload

To revert the LE Combined payload manually:

1. Close Steam and make sure Master Duel/YgoMaster are not running.
2. In the active YgoMaster folder, move the current LE Combined payload aside:

```text
Data/
YgoMaster.exe
YgoMasterClient.exe
YgoMasterLoader.dll
```

3. Restore those same items from:

```text
backup-base-before-le-combined-20260619-123444/
```

Keep these Linux runner files in the active folder:

```text
mono/
MonoRun.exe
YgoMasterLaunch.sh
```

## Troubleshooting

If Steam launches but nothing happens, check the Steam shortcut:

```text
Exe should be YgoMasterLaunch.sh
StartDir should be the YgoMaster folder
LaunchOptions should be empty
```

If launching directly from terminal, run:

```bash
./YgoMasterLaunch.sh
```

If Steam overwrites the shortcut settings, close Steam completely before editing `shortcuts.vdf`.

If the game is running extremely slowly, make sure there are not multiple leftover `masterduel.exe`, `MonoRun.exe`, or `wineserver` processes still running from previous tests.

Useful process check:

```bash
pgrep -a -f 'masterduel|YgoMaster|MonoRun|wineserver'
```

Useful stop command:

```bash
pkill -f 'MonoRun.exe YgoMaster.exe|masterduel.exe|wineserver'
```

Only run the stop command when you intentionally want to close the test/game processes.

## WAN PvP / Dueling Friends Over The Internet

YgoMaster supports PvP duel rooms, friends list, spectators, and trading through its own YgoMaster server. This is not Konami live-server multiplayer. Everyone connects to the YgoMaster server you host.

This local install was configured for WAN PvP on 2026-06-19.

Host public IPv4 at setup time:

```text
139.68.209.5
```

Host LAN IPv4 at setup time:

```text
192.168.68.83
```

Tailscale IPv4 at setup time:

```text
100.98.215.25
```

The active host config keeps YgoMaster itself on localhost:

`Data/Settings.json`

```json
"BaseIP": "localhost"
"SessionServerIP": "{BaseIP}"
"MultiplayerPvpClientConnectIP": "{SessionServerIP}"
"BindIP": "http://{BaseIP}:{BasePort}/"
"MultiplayerEnabled": true
```

The local client config also stays on localhost:

`Data/ClientData/ClientSettings.json`

```json
"BaseIP": "localhost"
"MultiplayerToken": "8ce1a5f9890932bf7fcb2ad0df155eff"
"DontAutoRunServerExe": true
```

WAN/LAN access is provided by these user-systemd forwarders started by `YgoMasterLaunch.sh`:

```text
192.168.68.83:4989 -> 127.0.0.1:4989
192.168.68.83:4988 -> 127.0.0.1:4988
```

Every player needs a unique `MultiplayerToken`. Do not reuse the host token for a friend.

Example friend settings:

`Data/ClientData/ClientSettings.json`

```json
"BaseIP": "139.68.209.5"
"BasePort": 4989
"SessionServerPort": 4988
"SessionServerIP": "{BaseIP}"
"MultiplayerToken": "2be7ce009dbb849dee7995e1ddc4f569"
```

Your friend also needs the same/sufficiently compatible YgoMaster + LE Combined setup. The safest path is for them to use the same LE Combined v1.3.0 package and matching Master Duel data.

### Router / Firewall Requirements

For true WAN play, forward these ports on the router to the host LAN IP `192.168.68.83`:

```text
TCP 4989 -> 192.168.68.83
TCP 4988 -> 192.168.68.83
```

If the router supports UDP/TCP selectors, TCP is the important one for these YgoMaster HTTP/session ports.

If the public IP changes, update `BaseIP` in:

```text
Data/Settings.json
Data/ClientData/ClientSettings.json
```

Friends must also update their `BaseIP` to the new public IP.

### Easier Internet Option: Tailscale

If router port forwarding is annoying or blocked by CGNAT, use Tailscale instead.

For Tailscale-based play, set `BaseIP` to:

```text
100.98.215.25
```

Do this on the host and on friends' clients. Friends must be in the same Tailscale network or otherwise able to reach that Tailscale IP.

### Starting PvP Duels

After everyone is connected:

1. Launch YgoMaster.
2. Click `DUEL` on the home menu.
3. Choose `Duel Room (PvP)`.
4. Create or join a duel room like normal Master Duel.

### WAN PvP Verification

After launching YgoMaster, the host should listen on ports `4988` and `4989`.

Check locally:

```bash
ss -ltnp | rg ':4988|:4989'
```

Working local bind result after setup:

```text
127.0.0.1:4989
127.0.0.1:4988
192.168.68.83:4989
192.168.68.83:4988
```

For WAN, an outside machine should be able to reach:

```text
139.68.209.5:4988
139.68.209.5:4989
```

---
name: running-the-runtime
description: Use when building, deploying, launching, or operating the game runtime - Build.bat, Proton/Steam launch, two-client PvP setup, deploying test binaries, port/firewall issues, or leftover processes.
---

# Running the Runtime

## Overview

Development happens in this repo; **the game runs from a deployed copy inside the Master Duel install** (`.../Yu-Gi-Oh! Master Duel/YgoMaster/` with `Data/`, the EXEs, and on Linux `mono/` + `MonoRun.exe`). Confusing repo checkout with live runtime — or clobbering the live runtime with a build — is the classic operational failure here.

## Build

| Platform | Command | Produces |
|---|---|---|
| Windows (full) | `Build.bat` | `YgoMaster.exe`, `YgoMasterClient.exe`, `YgoMasterLoader.dll` (C++, needs VS2022 Desktop C++), `MonoRun.exe` → repo `YgoMaster/` |
| Any (.NET SDK, C# only) | `dotnet build YgoMaster.sln -v:minimal -p:OutputPath=/tmp/ygomaster-build/solution/` | Redirected server+client EXEs for testing/deploy |

**Always redirect** `dotnet build` output on the game machine — the csproj default output path is the live runtime folder. The C++ loader rarely changes; C#-only iterations can reuse the deployed loader.

## Deploy safely (game machine)

Never hand-copy EXEs. `Tools/prepare_llm_runtime.py` is the deploy tool for validation builds (dry-run by default, refuses while `YgoMaster`/`YgoMasterClient`/`masterduel.exe` run, backs up to `backup-llm-runtime-*`, transactional rollback):

```bash
python3 Tools/prepare_llm_runtime.py --status              # dashboard: processes, binary hashes, broker health, settings
python3 Tools/prepare_llm_runtime.py --deploy-binaries     # dry-run preview
python3 Tools/prepare_llm_runtime.py --deploy-binaries --write
```

After deploy, `--status` must show `game_processes` empty beforehand and both root binaries `matches_build: true` after. It also mutates runtime `ClientSettings.json` (`--settings --control-player N`, `--disable`) while preserving JSON comments — keep tracked source defaults off; only the ignored runtime copy gets flipped (`--include-source-settings` is a deliberate exception).

## Launch (Linux/Proton — the validated environment)

Facts as of 2026-06-30, encoded in `YgoMasterLaunch.sh` (`Docs/LE-Requiem-Linux-Setup.md` carries the same correction; its Proton 10.0 mentions are historical record):

- **GE-Proton10-34** is the pinned runtime (`~/.steam/root/compatibilitytools.d/GE-Proton10-34/`). Valve Proton 10.0 aborts on `coremessaging.dll.DllGetActivationFactory`.
- **Client first, server delayed.** Starting `YgoMaster.exe` before the client can wedge the client's Proton process before `MonoRun.exe` starts. The launcher script encodes the working order: client → 5 s delay → server + socat forwarders.
- Launch via the Steam shortcut that runs `YgoMasterLaunch.sh` (it sets Steam app identity vars Proton needs). Ports: TCP 4988 (session) / 4989 (PvP), with `systemd-run` socat forwarders exposing them on the LAN IP for remote players (WAN: port-forward both; Tailscale works for CGNAT).

Windows: run `YgoMasterClient.exe` from the deployed folder; it starts `YgoMaster.exe` itself. `Docs/PvP.md` covers LAN/WAN configs. First-load errors/infinite loading → `Docs/FileLoadError.md` (game not fully updated, or stale `LocalData/` subfolders).

## Two-client PvP (required for any LLM broker validation)

1. Start the broker first: `python3 Tools/llm_broker.py`; check `curl http://127.0.0.1:4991/health`.
2. Deploy binaries/settings as above (broker + decision log enabled, control player set).
3. Launch the root client through Steam.
4. Launch the second client from a **copied folder** (e.g. `../YgoMasterP2`) with a different `MultiplayerToken` in its `ClientSettings.json`. **Do not start a second server.** On Linux use `Tools/launch_p2_client.sh` (pins GE-Proton10-34, sets Steam env, logs to `/tmp/ygomaster-p2-client.log`). For broker control of seat 1 it is the **P2 copy** that must carry `LlmBrokerEnabled` + `LlmBrokerControlPlayer: 1` (copy the folder after applying settings, or edit its `ClientSettings.json`) — the control policy only lets a client drive its own seat. Verify both copies' settings match your intent; a stale copied config is the classic double-committer bug (see debugging-llm-decisions).
5. Root: DUEL → Duel Room (PvP) → Create a Room → sit at Table 1. P2: Enter a Room → join → ENTRY at Table 1. Accept prompts until both boards load.
6. For broker control of seat 1: pass the root player's first turn; the **P2 folder's** `Data/ClientData/LlmDecisionLog.jsonl` should then show `decision_window` → `llm_broker_committed` for `acting_player: 1` (the requester is the client that owns the seat).
7. Afterward: analyzer thresholds (validating-changes gate 5), then `prepare_llm_runtime.py --disable --settings --write`.

## Hygiene checklist

- **Before rebuild/redeploy:** back up `Data/Players/` (gitignored, irreplaceable saves).
- **After sessions:** hunt zombies before assuming deploy-safe or diagnosing "slowness":
  `pkill -f 'MonoRun.exe YgoMaster.exe|masterduel.exe|wineserver'` (check first with `pgrep -fl`).
- **Every client folder needs a unique `MultiplayerToken`** — token determines the account (`MD5(token)`), duplicates collide.
- **Don't touch `Data/Players/` while the server runs**; don't restart the server mid-duel (stuck clients → restart all).
- Upstream Linux runtime files (`mono/`, `MonoRun.exe`) come from the upstream `YgoMaster-Linux-Data-v1.zip`; everything else must be this fork's build (upstream EXEs break LE content).

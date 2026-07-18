---
name: validating-changes
description: Use before declaring any YgoMaster change done, when deciding which builds/tests to run, after editing code or data, or when asked "how do I verify this" - there is no CI, so this is the gate.
---

# Validating Changes

## Overview

**There is no CI.** Every gate below is manual. Which gates apply depends on what you touched and which machine you're on (dev box vs game machine — see ygomaster-map).

## Step 0 — Know your blast radius

Sources are shared by `<Compile Include>`, not project references. Before validating, grep the csproj files for every file you touched:

```bash
grep -l "MyFile.cs" YgoMasterServer/YgoMaster.csproj YgoMasterClient.csproj \
  YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.csproj YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.Net8.csproj
```

`YgoMasterServer/Llm/*.cs` is in all four. A schema change also hits Python (`Tools/llm_broker.py`, `Tools/test_llm_broker.py` fixtures hardcode `schema_version: 3`) and `Docs/LlmBroker.md`.

## The gates, in order

### 1. Python suite (any machine, always cheap — run it even for C#-only changes if Tools/ interacts)

```bash
python3 -m unittest discover -s Tools -p 'test_*.py'        # full suite (~57 tests, seconds)
python3 Tools/test_llm_broker.py                            # focused: broker
```

### 2. C# build + harness (run `which dotnet` first — a fresh dev machine may have no .NET SDK; install via your package manager or the dotnet-install script)

```bash
# ALWAYS redirect output — a plain build overwrites live runtime EXEs on the game machine
dotnet build YgoMaster.sln -v:minimal -p:OutputPath=/tmp/ygomaster-build/solution/

# The C# test suite (hand-rolled runner in Program.cs, nonzero exit on failure — not dotnet test)
dotnet build YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.Net8.csproj \
  -v:minimal -p:OutputPath=/tmp/ygomaster-build/harness/
dotnet /tmp/ygomaster-build/harness/YgoMasterLlmTestHarness.Net8.dll
```

Expected: 0 warnings/0 errors on the solution build; harness prints pass/fail per named check. The net8 harness link-compiles the same `Llm/` sources the game uses — it is real coverage, not a mock demo. On Windows, `Build.bat` is the full build (also compiles the C++ loader; needs VS2022 Desktop C++ — irrelevant for pure C#/data changes).

Do not combine `dotnet run` with a redirected `OutputPath`: the SDK can build the
new assembly under `/tmp` and still execute a stale default `bin/Debug` output.
Build to the redirected directory, then execute that exact DLL as shown above.

### 3. Data validation (after any `YgoMaster/Data/` or campaign-tool change)

```bash
python3 Tools/validate_requiem_campaigns.py
python3 Tools/validate_le_enemy_deck_rewards.py
python3 Tools/validate_campaign_portraits.py
```

### 4. Broker smoke test (after broker/protocol changes)

```bash
python3 Tools/llm_broker.py &         # deterministic mode, no API key
curl http://127.0.0.1:4991/health     # expect service: ygomaster_llm_broker, status: ok
```

### 5. Live validation (game machine only — required for anything touching the duel path)

The game machine has its own clone of this repo: sync via `git pull --ff-only` there, build with the redirected `dotnet build` (the `~/.dotnet/dotnet` SDK per `Docs/LlmBroker.md`), then deploy with the tool below — never copy binaries across machines by hand.

The broker path is PvP-only; no offline test exercises real `duel.dll` behavior. Deploy with the guarded tool (dry-run first, refuses while game processes run, backs up, rolls back on failure), run the two-client room duel, then gate on the analyzer:

```bash
python3 Tools/prepare_llm_runtime.py --status
python3 Tools/prepare_llm_runtime.py --settings --control-player 1            # dry-run
python3 Tools/prepare_llm_runtime.py --settings --deploy-binaries --control-player 1 --write
# ... run the PvP duel (see running-the-runtime) ...
python3 Tools/analyze_llm_duel_log.py Data/ClientData/LlmDecisionLog.jsonl \
  --min-turns 2 --min-broker-commits 1 --require-broker-action-type command   # nonzero exit on miss
python3 Tools/prepare_llm_runtime.py --disable --settings --write             # cleanup
```

For a schema change, also confirm the new field appears in a recorded `request_json` in the decision log.

## What "done" means per change type

| You changed | Minimum gates |
|---|---|
| `Tools/*.py` | 1 (+3 if campaign tools, +4 if broker) |
| `YgoMasterServer/Llm/` or `DuelDll.cs` LLM path | 1, 2, 4 — and 5 before claiming runtime behavior |
| Server acts / `YgoMasterServer/` non-Llm | 2; live server boot on game machine if behavior-visible |
| Client hooks / IL2CPP | 2 builds it, but only 5 (a real launch) proves it — hooks can't be unit-tested |
| `YgoMaster/Data/` campaign content | 3, then restart `YgoMaster.exe` and spot-check in game |
| Schema/protocol fields | 1, 2, 4, 5 + update harness assertions + Python fixtures + `Docs/LlmBroker.md`. `SchemaVersion` bump rule: purely additive optional request fields may keep the version; anything changing response shape or field semantics bumps it (v2→v3 bumped for controlled-player + card metadata semantics). |

## Common mistakes

| Mistake | Reality |
|---|---|
| "Build passed, done" | Build ≠ tests. The harness and Python suite are separate invocations; nothing runs them for you. |
| Plain `dotnet build` on the game machine | Overwrites live runtime EXEs mid-session. Always `-p:OutputPath=/tmp/ygomaster-build/solution/`. |
| Skipping the harness because "it's a console app" | It's the entire C# test suite. Run it. |
| `dotnet run` plus redirected `OutputPath` | It can execute stale `bin/Debug` output. Build redirected, then run the exact `/tmp/...dll`. |
| Editing a linked `Llm/` file and rebuilding only the server | The client is the runtime consumer. Rebuild the solution. |
| Claiming duel-path behavior from offline tests alone | Engine semantics (seq advancement, commit effects) only exist live. Say "verified offline; live validation pending" honestly. |

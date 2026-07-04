# LLM Broker

YgoMaster's in-game LLM integration is disabled by default and talks only to a local broker. The game process sends a legal-action request to the broker, receives one `action_id`, rebuilds the current legal-action list, rejects stale or unknown actions, and only then commits the selected action.

## Game Settings

In `YgoMaster/Data/ClientData/ClientSettings.json`:

```json
"LlmDecisionLogEnabled": false,
"LlmBrokerEnabled": false,
"LlmBrokerUrl": "http://127.0.0.1:4991/decide",
"LlmBrokerTimeoutMs": 2000,
"LlmBrokerControlPlayer": -1
```

`LlmBrokerControlPlayer` must be `0` or `1`. `-1` disables broker control even if the broker flag is accidentally enabled. This slice supports broker control of the non-local player only; if the configured player is `Duel.MyID`, the normal player UI stays in control.

Broker failures are written through the normal PvP log path, so enable `PvpLogToConsole` or `PvpLogToFile` while testing.

When `LlmDecisionLogEnabled` is true, the client writes structured audit events to `Data/ClientData/LlmDecisionLog.jsonl`. The broker path now records the full lifecycle:

- `decision_window`: current public state and legal actions.
- `llm_broker_request_started`: a broker request was launched for a decision window.
- `llm_broker_response`: broker result, success/failure, selected action metadata, and raw request/response JSON strings for audit reconstruction.
- `llm_broker_rejected`: response reached the game but failed current-state validation.
- `llm_broker_commit_skipped`: response could not be committed because the duel/view/control state changed or commit failed.
- `llm_broker_committed`: validated broker action was committed.

Supported broker action kinds in this slice:

- `move_phase`
- `command`
- `dialog_result`
- `list_index`

Broker requests use schema version `3` and include `acting_player` plus `controlled_player`.
`controlled_player` is the player whose normal private player knowledge may be serialized.

The schema includes a conservative `public_state` object:

- `players[].life_points`
- `players[].positions[]` with counts for positions `0..18`
- `players[].known_cards[]` for the controlled player's own hand, revealed hand cards, and graveyard cards
- card metadata when the card ID resolves through the existing Master Duel card-data parser

Position map used by the current YgoMaster capture path: `0..12` field locations, `13` hand, `14` deck, `15` extra deck, `16` graveyard, `17` banished, `18` transient selection. This slice intentionally does not serialize deck order, extra-deck identities, opponent closed hand identities, transient selection identities, or field/banished identities until the `DLL_DuelGetCardFace` values are validated in a real duel log.

The `public_state` payload is still hidden-information aware. It exposes the controlled player's own hand because a real player knows their hand, but it does not expose the opponent's closed hand, deck order, extra deck identities, face-down identities, or unvalidated transient zones.

Command legal actions include `card_id` and a compact `card` object when metadata is available:

```json
{
  "action_id": 2,
  "kind": "command",
  "command": "Summon",
  "card_unique_id": 37,
  "card_id": 5037,
  "card": {
    "card_id": 5037,
    "name": "Example Monster",
    "text": "Once per turn...",
    "kind": "Effect",
    "attribute": "4",
    "level": 4,
    "atk": 1800,
    "def": 1000,
    "scale": 0
  }
}
```

Card metadata is loaded lazily from the existing `YdkHelper.LoadCardDataFromGame` path and normalized/capped before serialization. If the catalog cannot be loaded or a card ID is missing, the request still keeps the raw `card_id`/`card_unique_id` and simply omits the `card` object.

The broker-visible action list filters obvious non-strategic commands before assigning `action_id`s: `Look`, `Surrender`, forced/simple `Draw`, and single-option `Decide` windows fall back to the normal CPU/default path instead of consuming a model call.

`list_index` currently commits one `DLL_DuelListSetIndex` choice and is only exposed for single-select list windows (`is_multi_mode == 0`, `select_min == 1`, `select_max == 1`). Multi-select or optional multi-pick windows fall back to the built-in CPU path until runtime validation exists for those flows.

Cancel/skip actions are not exposed to the broker yet. The codebase has low-level cancel calls, but this slice only exposes actions with a clear current-state legality surface.

## Run The Broker

Mock mode chooses a deterministic legal action and requires no API key:

```bash
python3 Tools/llm_broker.py
```

Provider mode is selected with `YGO_LLM_BROKER_PROVIDER`. Keep keys and CLI auth outside YgoMaster config.

Supported values:

- `deepseek_api`
- `grok_cli`
- `opencode_cli`
- `openai_compatible`
- `deterministic`

If `YGO_LLM_BROKER_PROVIDER` is unset, the broker preserves the original behavior: it uses the OpenAI-compatible API path only when `YGO_LLM_BROKER_API_KEY`, `YGO_LLM_BROKER_BASE_URL`, and `YGO_LLM_BROKER_MODEL` are all present; otherwise it uses deterministic fallback.

DeepSeek mode uses an OpenAI-compatible chat-completions request. `YGO_LLM_BROKER_BASE_URL` defaults to `https://api.deepseek.com/chat/completions`, and `YGO_LLM_BROKER_MODEL` defaults to `deepseek-chat`:

```bash
export YGO_LLM_BROKER_PROVIDER="deepseek_api"
export YGO_LLM_BROKER_API_KEY="..."
python3 Tools/llm_broker.py
```

For another OpenAI-compatible endpoint, set the provider explicitly and provide all three API variables:

```bash
export YGO_LLM_BROKER_PROVIDER="openai_compatible"
export YGO_LLM_BROKER_BASE_URL="https://example.test/v1/chat/completions"
export YGO_LLM_BROKER_MODEL="model-name"
export YGO_LLM_BROKER_API_KEY="..."
python3 Tools/llm_broker.py
```

Grok CLI mode shells out to the local `grok` command in single-turn JSON mode:

```bash
grok login
export YGO_LLM_BROKER_PROVIDER="grok_cli"
export YGO_LLM_BROKER_GROK_COMMAND="grok"
export YGO_LLM_BROKER_GROK_MODEL="grok-code-fast-1" # optional
export YGO_LLM_BROKER_CLI_TIMEOUT="20"
python3 Tools/llm_broker.py
```

OpenCode CLI mode shells out to `opencode run`:

```bash
opencode providers
export YGO_LLM_BROKER_PROVIDER="opencode_cli"
export YGO_LLM_BROKER_OPENCODE_COMMAND="opencode"
export YGO_LLM_BROKER_OPENCODE_MODEL="opencode/grok-code-fast-1" # optional
export YGO_LLM_BROKER_CLI_TIMEOUT="20"
python3 Tools/llm_broker.py
```

The CLI providers are experimental. They are useful for trying subscription/OAuth-backed local tools, but they are slower and more operationally fragile than a direct API call. Raise both the game timeout and CLI timeout together when using them:

```bash
python3 Tools/prepare_llm_runtime.py --settings --control-player 1 --timeout-ms 20000 --write
export YGO_LLM_BROKER_CLI_TIMEOUT="18"
```

The provider should return only JSON with `run_effect_seq`, `action_id`, a card/phase-specific `reason`, optional `confidence`, and optional `plan`:

```json
{
  "run_effect_seq": 219,
  "action_id": 2,
  "reason": "Normal Summon Duza to start the Cubic line.",
  "confidence": 0.74,
  "plan": "Develop a monster first, then use available follow-up actions."
}
```

The broker tolerates wrapper text by scanning for balanced JSON objects, using the last valid object that matches the current `run_effect_seq` and a legal `action_id`. The game still validates both fields against the current duel state before committing anything. `reason`, `confidence`, and `plan` are preserved into audit logs for review, but action legality still depends only on the current validated action list.

Provider parse, envelope, timeout, and HTTP failures return broker-side `provider_error`, which lets the game use its existing CPU fallback path instead of silently committing a non-LLM deterministic choice. The game HTTP transport preserves structured non-2xx broker error bodies, so PvP logs can report `provider_error` instead of a generic transport failure when the broker returns that envelope. The provider timeout defaults to `1.5` seconds so it stays below the game's default `LlmBrokerTimeoutMs` of `2000`; for hosted models, raise both `LlmBrokerTimeoutMs` and `YGO_LLM_BROKER_PROVIDER_TIMEOUT` together.

The broker exposes `GET /health` for validation tooling. A healthy broker reports `service: "ygomaster_llm_broker"`, `status: "ok"`, `provider`, and `provider_configured`. `provider_configured` is `true` when the selected API provider has the required environment variables or the selected CLI provider command exists; without that, the broker still runs but uses deterministic fallback only when provider selection is not explicit.

## Runtime Validation Prep

Build to a redirected output path first so the live runtime EXEs are not overwritten during normal verification:

```bash
~/.dotnet/dotnet build YgoMaster.sln -v:minimal -p:OutputPath=/tmp/ygomaster-build/solution/
```

Use `Tools/prepare_llm_runtime.py` to update settings and deploy the redirected binaries. It is dry-run by default, preserves the JSON-with-comments settings file shape, writes backups under `backup-llm-runtime-*`, and refuses to copy binaries while `YgoMaster`, `YgoMasterClient`, or `masterduel.exe` processes are running. Write mode preflights deploy blockers before settings mutation and rolls back prior settings/binary writes if a later write fails.

Inspect the current runtime state at any point:

```bash
python3 Tools/prepare_llm_runtime.py --status
```

Before binary deploy, `game_processes` should be empty and both root runtime binaries should report `matches_build: true` after deployment. During a validation launch, `broker.health.ok` should be `true`; for real model-backed validation, `broker.ready_for_llm_validation` must also be `true`, which requires both the broker identity check and provider environment variables. `decision_log.exists` should become `true` once the LLM decision logging hook has seen a PvP decision window.

Dry-run the runtime settings update:

```bash
python3 Tools/prepare_llm_runtime.py --settings --control-player 1
```

Use `--include-source-settings` only when you intentionally want to change the tracked source default under `YgoMaster/Data/ClientData/ClientSettings.json`; the normal validation path keeps source defaults off and only changes the ignored runtime settings file.

Dry-run binary deployment to verify the redirected build outputs and runtime destinations:

```bash
python3 Tools/prepare_llm_runtime.py --deploy-binaries
```

Apply the validation settings and binaries only after the dry-runs look right. The deploy write refuses to run while the game is active:

```bash
python3 Tools/prepare_llm_runtime.py --settings --control-player 1 --write
python3 Tools/prepare_llm_runtime.py --deploy-binaries --write
```

The same preflight and rollback protections also apply to a combined write:

```bash
python3 Tools/prepare_llm_runtime.py --settings --deploy-binaries --control-player 1 --write
```

Use `--disable --settings --write` to turn the broker and LLM decision logging back off after validation.

## Local Verification

On Windows, build the original .NET Framework harness:

```bash
dotnet build YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.csproj -v:minimal
```

On Linux with a local .NET SDK, use the cross-platform runner that links the same harness and LLM source files:

```bash
dotnet run --project YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.Net8.csproj
```

The net8 runner is for local verification only; the game/client projects still target .NET Framework 4.8.

After any longer manual duel, summarize audit coverage from the JSONL log:

```bash
python3 Tools/analyze_llm_duel_log.py Data/ClientData/LlmDecisionLog.jsonl \
  --min-turns 2 \
  --min-broker-commits 1 \
  --require-broker-action-type command
```

The analyzer prints event counts, broker request/response/commit totals, observed turns, observed action types, and basic quality counters such as generic reasons, low confidence, and cardless command choices. It exits non-zero if a requested coverage threshold is missing, which makes it usable for scripted validation after a longer duel.

Render a human-readable markdown report when you need to inspect decisions:

```bash
python3 Tools/render_llm_decision_report.py Data/ClientData/LlmDecisionLog.jsonl \
  -o /tmp/llm-decision-report.md
```

The report expands runtime `request_json` strings, shows selected cards and text snippets when schema v3 metadata was present, and flags generic reasons or low confidence.

## Local PvP Duel Validation

The current LLM control path is PvP-only and controls the non-local player. A solo/PvE duel against the built-in CPU does not exercise the broker path.

Use two local client folders and one server:

1. Start the broker first and verify `/health`.
2. Build and deploy validation binaries/settings with broker enabled.
3. Launch the root runtime through Steam so it starts `YgoMasterClient.exe`, `masterduel.exe`, and the delayed `YgoMaster.exe` server.
4. Launch the second client from a copied folder such as `../YgoMasterP2` with a different `Data/ClientData/ClientSettings.json` `MultiplayerToken`. Do not start a second server. On this Linux/Proton setup, use `Tools/launch_p2_client.sh`; it sets the Steam app identity variables that direct Proton launches need.
5. In the root client, go to `DUEL` -> `Duel Room (PvP)` -> `Create a Room`, choose a valid deck, and sit at `Table 1`.
6. In the P2 client, go to `DUEL` -> `Duel Room (PvP)` -> `Enter a Room`, join the room, choose a valid deck, and click `ENTRY` at `Table 1`.
7. Start/accept the duel prompts until both boards load.
8. If `LlmBrokerControlPlayer` is `1`, pass the root player's first turn so control reaches player `1`. The root client should then request a broker decision for the non-local player.
9. Confirm `Data/ClientData/LlmDecisionLog.jsonl` contains a `decision_window` for `acting_player: 1`, `llm_broker_request_started`, `llm_broker_response`, and `llm_broker_committed`. Run `Tools/analyze_llm_duel_log.py` with the thresholds above after a longer duel. For a real model-backed run, also confirm `python3 Tools/prepare_llm_runtime.py --status` reports `broker.health.ok: true` and `broker.ready_for_llm_validation: true`.

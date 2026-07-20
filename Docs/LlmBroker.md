# LLM Broker

YgoMaster's in-game LLM integration is disabled by default and talks only to a local broker. The game process sends a legal-action request to the broker, receives one `action_id`, rebuilds the current legal-action list, rejects stale or unknown actions, and only then commits the selected action.

## Game Settings

In `YgoMaster/Data/ClientData/ClientSettings.json`:

```json
"LlmDecisionLogEnabled": false,
"LlmBrokerEnabled": false,
"LlmBrokerUrl": "http://127.0.0.1:4991/decide",
"LlmBrokerTimeoutMs": 55000,
"LlmBrokerControlPlayer": -1
```

`LlmBrokerControlPlayer` must be `0` or `1`. `-1` disables broker control even if the broker flag is accidentally enabled. The broker controls only the local player of the client it is enabled on: a client issues broker requests only when the acting player equals both `Duel.MyID` and `LlmBrokerControlPlayer` (enforced by `LlmBrokerControlPolicy.ShouldControlPlayer`; harness check `BrokerControlPolicyOnlyControlsLocalConfiguredPlayer`, "p2 controls p2"). To let the LLM drive a seat, enable the broker in the client folder that owns that seat. A client whose `Duel.MyID` differs from the configured player ignores the broker entirely and keeps the normal UI/CPU behavior.

Broker failures are written through the normal PvP log path, so enable `PvpLogToConsole` or `PvpLogToFile` while testing.

When `LlmDecisionLogEnabled` is true, the client writes structured audit events to `Data/ClientData/LlmDecisionLog.jsonl`. The broker path now records the full lifecycle:

- `decision_window`: current public state and legal actions.
- `llm_broker_window_routed`: the adapter route chosen for a controlled-player prompt (`Broker`, `Automatic`, `CpuFallback`, `Default`, or `Suppressed`) with prompt family, reason, counts, and whether an automatic action was available.
- `llm_broker_request_started`: a broker request was launched for a decision window.
- `llm_broker_response`: broker result, success/failure, selected action metadata, and raw request/response JSON strings for audit reconstruction.
- `llm_broker_rejected`: response reached the game but failed current-state validation.
- `llm_broker_commit_skipped`: response could not be committed because the duel/view/control state changed or commit failed.
- `llm_broker_automatic_action`: a filtered mechanical window was auto-handled without a model call.
- `llm_broker_unsupported_window`: a controlled-player prompt was routed to CPU fallback because the adapter could not safely model it for broker control.
- `llm_broker_stuck_window_recovered`: the third recurrence of the same no-progress controlled `WaitInput` fingerprint was handed to the server-authoritative temporary CPU recovery path. Automatic empty CheckTiming/CheckChain declines remain visible to this watchdog and preserve its history across commit; unrelated automatic actions reset it. The event includes the sequence-independent fingerprint and bounded route history.
- `llm_broker_committed`: validated broker action was committed.
- `summon_interaction_started`, `summon_placement_retry`, `summon_interaction_preserved`, `summon_interaction_completion_confirmed`, `summon_interaction_recovery`, `summon_interaction_rejected`, and `summon_interaction_reset`: bounded summon-placement ownership and recovery telemetry. These records include the stable interaction signature, originating sequence, attempt number, and disposition without serializing hidden card state.

Supported broker / adapter action kinds in this slice:

- `move_phase`
- `command`
- `dialog_result`
- `list_index`
- `cancel` for validated optional `CheckTiming` or `CheckChain` decline (`CancelCommand2(false)`). When that decline is the sole extractable action on an empty cancellable timing/chain window, it is marked mechanical and auto-committed with **no model call**. If activations are present, activation plus decline remain strategic broker choices.

Broker requests use schema version `4` and include `acting_player` plus `controlled_player`.
`controlled_player` is the player whose normal private player knowledge may be serialized.

The schema includes a conservative `public_state` object:

- `players[].life_points`
- `players[].positions[]` with counts for positions `0..18`
- `players[].known_cards[]` for the controlled player's own hand, revealed hand cards, graveyard cards, and public face-up field cards
- card metadata when the card ID resolves through the existing Master Duel card-data parser

Position map used by the current YgoMaster capture path: `0..12` field locations, `13` hand, `14` deck, `15` extra deck, `16` graveyard, `17` banished, `18` transient selection. Live validation established raw face value `1` as public face-up and `0` as hidden on the current runtime, so face-up field identities are serialized while face-down field identities remain redacted. This slice intentionally does not serialize deck order, extra-deck identities, opponent closed hand identities, hidden field identities, transient selection identities, or unvalidated banished identities.

The `public_state` payload is still hidden-information aware. It exposes the controlled player's own hand because a real player knows their hand, but it does not expose the opponent's closed hand, deck order, extra deck identities, face-down identities, or unvalidated transient zones.

Schema v4 also includes `board_context`, `opponent_context`, `turn_memory`, immutable public `duel_history`, and strategic-window metadata. `board_context` is a safe summary derived from the same public state: LP, field counts, graveyard counts, and known public card names. `opponent_context` is the model-facing version of the opponent summary: opponent player id, LP, field/graveyard counts, known public threats from already-serialized visible zones, and a summary string that explicitly says when an identity is unavailable. If a public opponent card is not already present in `known_cards`, `opponent_context` reports the count rather than inventing an identity. `turn_memory` records recent committed broker actions, cards already used this turn, whether a broker normal summon has already been committed this turn, and a compact phase plan. It resets on duel generation changes and is derived from committed broker actions only. Strategic-window metadata includes `is_strategic_window`, `strategic_window_reason`, `strategic_action_count`, and `mechanical_action_count`.

Command actions may also include `effect_applicability`. This annotation is authoritative only when `is_grounded` is true. A grounded `effect_expected_to_apply: false` action is rejected with `effect_applicability_contradiction` unless the same annotation carries a separately grounded secondary benefit. Coverage is intentionally catalog-driven and conservative: unknown, ambiguous, hidden-state-dependent, or unsupported interactions fail open and remain legal rather than being guessed. The current generic predicate covers activated monster effects whose source original ATK is at or below a target's grounded immunity threshold; the analyzer does not branch on card names.

After a broker-controlled attack opens an engine target prompt, multiple current targets remain broker-owned. The follow-up request carries public-safe `attack_target` legal actions with lease-bound `target_token` values plus `interaction_origin` containing the originating attack label, reason, and plan. Face-up targets may be named when DLL visibility permits it; face-down targets are anonymous zone labels with zero card/unique ids. One legal target remains automatic. Generic recovery cannot select a different attack target without provider intent; a CPU handoff emits `llm_attack_target_divergence`.

### Default-off planning-search audit (LLM-005 Layer A / Slice 6B)

When seat-gated planning-search audit is enabled (default-off; not a live provider contract), `LlmPlanningSearchAudit` builds a bounded Layer A graph and projects it for local decision-log / harness inspection. The projection remains hidden-information-safe and obeys hard UTF-8 byte budgets (default 16 KiB).

Slice 6B offline/default-audit additions (accepted 2026-07-16; **not deployed as a behavior-changing release**):

- **Template lines** may appear among non-root continuations with lowercase projection keys such as `template_id` and nested `summon_requirement` (family, rank/level, material counts/ids/sources). Provenance is `rules_inferred`; future steps are never `commit_eligible`. Templates are offline-validated Xyz/Synchro setups (for example flip Cave Dragon + face-up Level 4 partner toward Rank 4 Utopia), not exact `duel.dll` simulations.
- **Coverage / limits** telemetry on the audit graph (`status`, depth/node/beam/wall limits, `continuation_expansion_count`, `expanded_nodes`, `pruned_nodes`, `boundary_nodes`, prune reasons including `template_*` budget tokens) describes bounded search completeness. Root shells for every strategic legal action remain mandatory.
- **`model_hypothesis` lines** are admission infrastructure only: non-commit-eligible, score-capped to grounded authority, with candidate score features stripped. They are **not currently ingested or ranked by the provider**, and they never enter `LlmActionCommitPlan`.
- **Planning-audit `intended_followup` in serialized turn memory** remains separate from the live provider response contract. Schema-v4 responses may now return structured `intended_followups` for a selected `SummonSp` root. The client records only follow-ups attached to the action that actually commits, then reconciles each promise at the next authoritative strategic boundary as `intended_followup_matched` or `intended_followup_unavailable`. Recovery cannot transfer intent from a different provider action. This is audit and validation infrastructure, not schema-v5 parent-line ranking.

Schema-v5 provider line ranking and planning-search request integration remain deferred to [[YGOMASTER-LLM-005]] Slice 3 after [[YGOMASTER-LLM-004]] schema-v4 live acceptance. Broker decide requests remain schema version `4` for the live path documented above.

Command legal actions include `card_id` and a compact `card` object when metadata is available:

```json
{
  "action_id": 2,
  "kind": "command",
  "action_label": "Summon Example Monster",
  "action_group": "summon",
  "is_mechanical": false,
  "strategic_role": "board_development",
  "requires_target": false,
  "target_scope": null,
  "consequence_hint": "normal_summon_consumes_turn_summon",
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

The broker-visible action list filters obvious non-strategic commands before assigning `action_id`s: `Look`, `Surrender`, forced/simple `Draw`, and single-option `Decide` windows fall back to the normal CPU/default path instead of consuming a model call. Known mechanical follow-up windows such as summon placement are automatic or delegated under bounded interaction guards. Multiple engine-confirmed battle targets are strategic broker choices; a single battle target is automatic. Optional effect yes/no prompts are exposed as strategic `dialog_result` actions with `is_yes_no_prompt: true`, so the broker can choose whether to activate effects such as on-summon triggers.

`list_index` currently commits one `DLL_DuelListSetIndex` choice and is only exposed for single-select list windows (`is_multi_mode == 0`, `select_min == 1`, `select_max == 1`). Multi-select or optional multi-pick windows fall back to the built-in CPU path until runtime validation exists for those flows.

Generic cancel/skip is still not a free-form broker action family. Multi-select and unspecified skip windows remain unsupported (CPU or temporary-CPU handoff). The one validated exception is optional **CheckChain** decline: when activations exist, decline can appear as a strategic cancel alongside chain options; when the CheckChain window is otherwise empty but cancellable, the adapter auto-commits a sole mechanical decline (`CancelCommand2(false)`) without a model call so NativeDefault cannot loop the prompt.

### Bounded summon-placement ownership (LLM-005 Slice 2D)

Summon-placement choices are treated as one interaction across engine-generated material, overlay, cut-in, dialog/list, and placement views. The interaction key includes duel generation, acting seat, summon card unique ID/card ID, source family, and expected follow-up family; it deliberately excludes the later `run_effect_seq` so an unchanged placement prompt can be retried. The first accepted placement is attempt 1, one identical retry is allowed, and a third unchanged placement is delegated through the server's temporary-CPU lease instead of being sent to the broker or falling into an uncontrolled `CpuThinking` loop.

The interaction is not marked complete from a cut-in or a single accepted command. Completion requires authoritative self-resource evidence that the Extra Deck card left when applicable, the summoned card appeared on the controlled field, required material-source counts resolved, and the engine reached the next decision boundary. A changed seat, duel generation, card identity, or source/follow-up identity is rejected. Temporary CPU remains active through summon transition views and `WaitInput/Location` only while the server still reports a real summon-placement prompt; it restores human control at the next ordinary boundary, cancellation, duel end, disconnect, or shutdown.

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
export YGO_LLM_BROKER_GROK_MODEL="grok-4.5"
export YGO_LLM_BROKER_GROK_ARGS="--reasoning-effort low"
export YGO_LLM_BROKER_CLI_TIMEOUT="55"
python3 Tools/llm_broker.py
```

### Full provider reasoning log (debug)

`LlmDecisionLog.jsonl` stores structured audit fields (`reason`, `plan`, `why_now`, …). For the **raw model output** — wrapper text before the decision JSON, CLI stdout/stderr, and API thinking fields such as `reasoning_content` — enable a separate broker-side JSONL:

```bash
# default path: Data/ClientData/LlmReasoningLog.jsonl (relative to broker cwd)
export YGO_LLM_BROKER_REASONING_LOG=1

# or an absolute path
export YGO_LLM_BROKER_REASONING_LOG="/tmp/LlmReasoningLog.jsonl"

# optional: also store the full prompt (large)
export YGO_LLM_BROKER_REASONING_LOG_INCLUDE_PROMPT=1

python3 Tools/llm_broker.py
```

Each line is a `provider_reasoning` event with `raw_text` / `raw_stdout` / `raw_stderr`, extracted `reasoning_text`, optional full `api_response`, parse result (`parsed` or `error`), latency, and request metadata (`run_effect_seq`, acting player). Failures are logged too. Logging never blocks the decide path if the file cannot be written.

OpenCode CLI mode shells out to `opencode run`:

```bash
opencode providers
export YGO_LLM_BROKER_PROVIDER="opencode_cli"
export YGO_LLM_BROKER_OPENCODE_COMMAND="opencode"
export YGO_LLM_BROKER_OPENCODE_MODEL="xai/grok-4.5"
export YGO_LLM_BROKER_CLI_TIMEOUT="20"
python3 Tools/llm_broker.py
```

AGY CLI mode shells out to `agy --print` and can target Gemini models, including Gemini 3.5 Flash on this install:

```bash
agy models
export YGO_LLM_BROKER_PROVIDER="agy_cli"
export YGO_LLM_BROKER_AGY_COMMAND="agy"
export YGO_LLM_BROKER_AGY_MODEL="google/gemini-3.5-flash"
export YGO_LLM_BROKER_CLI_TIMEOUT="20"
python3 Tools/llm_broker.py
```

The CLI providers are experimental. They are useful for trying subscription/OAuth-backed local tools, but they are slower and more operationally fragile than a direct API call. Raise both the game timeout and CLI timeout together when using them:

```bash
python3 Tools/prepare_llm_runtime.py --settings --control-player 1 --timeout-ms 55000 --write
export YGO_LLM_BROKER_CLI_TIMEOUT="55"
```

The provider should return only JSON with `run_effect_seq`, `action_id`, a card/phase-specific `reason`, `confidence`, `plan`, and audit rationale fields:

```json
{
  "run_effect_seq": 219,
  "action_id": 2,
  "reason": "Normal Summon Duza to start the Cubic line.",
  "confidence": 0.74,
  "plan": "Develop a monster first, then use available follow-up actions.",
  "opponent_board_assessment": "Opponent has one unknown field card and no known graveyard threats.",
  "why_now": "Use the Normal Summon before considering Battle or End Phase.",
  "alternatives_considered": [
    "Set Duza preserves information but loses its summon effect.",
    "End Phase gives up tempo with playable cards in hand."
  ],
  "risk": "Duza can still be removed before the follow-up.",
  "intended_followups": [
    {
      "action_family": "effect_activation",
      "card_id": 123,
      "card_name": "Duza the Meteor Cubic Vessel",
      "description": "Activate Duza's on-summon effect when the engine exposes it."
    }
  ]
}
```

The broker tolerates wrapper text by scanning for balanced JSON objects, using the last valid object that matches the current `run_effect_seq` and a legal `action_id`. The game still validates both fields against the current duel state before committing anything. `reason`, `confidence`, `plan`, `opponent_board_assessment`, `why_now`, `alternatives_considered`, `risk`, and structured `intended_followups` are preserved for audit. Follow-up action families are limited to `effect_activation`, `attack`, and `move_phase`; providers return an empty list when the selected action does not rely on a future continuation. Low-confidence responses (`confidence < 0.5`), clearly generic reasons such as `first option`, mechanical choices, cardless strategic summon/set/activate choices, early `End Phase` choices while playable commands remain, grounded applicability contradictions, and over-latency strategic responses are rejected before commit. For recoverable quality failures, the client first tries deterministic recovery via `LlmBrokerRecovery` and commits a still-legal provider choice or safe recovery action; attack-target windows are the explicit exception and cannot heuristically substitute a target. Only if recovery cannot pick an allowed action does the normal CPU/default fallback path run. Rejections still emit `llm_broker_rejected`; successful recovery emits `llm_broker_recovered` and a normal `llm_broker_committed` for analyzer commit coverage. Runtime parsing tolerates missing audit fields and missing `intended_followups` for backward compatibility, while the broker provider parser and provider JSON schema require `opponent_board_assessment` and `intended_followups` so new model-backed runs account for visible opponent context and declare any promised continuation explicitly.

Provider parse, envelope, timeout, and HTTP failures return broker-side `provider_error`, which lets the game use its existing CPU fallback path instead of silently committing a non-LLM deterministic choice. Deterministic fallback for explicit provider failures is opt-in with `YGO_LLM_BROKER_PROVIDER_ERROR_FALLBACK=1`. The game HTTP transport preserves structured non-2xx broker error bodies, so PvP logs can report `provider_error` instead of a generic transport failure when the broker returns that envelope. The provider timeout defaults to `1.5` seconds, while the runtime-prep default `LlmBrokerTimeoutMs` is `55000` for slower CLI-backed validation; for hosted models, raise or lower both `LlmBrokerTimeoutMs` and `YGO_LLM_BROKER_PROVIDER_TIMEOUT` together.

The broker exposes `GET /health` for validation tooling. A healthy broker reports `service: "ygomaster_llm_broker"`, `status: "ok"`, `provider`, and `provider_configured`. `provider_configured` is `true` when the selected API provider has the required environment variables or the selected CLI provider command exists; without that, the broker still runs but uses deterministic fallback only when provider selection is not explicit.

Optional local strategy hints can be supplied with `YGO_LLM_BROKER_STRATEGY_HINTS=/path/to/LlmStrategyHints.json` or by placing `Data/ClientData/LlmStrategyHints.json` in the runtime folder. The file may be either a list or `{ "hints": [...] }`; each hint can include `deck_name`, `cards`, `game_plan`, `starter_cards`, `extenders`, `removal`, `win_condition`, `combo_hints`, and `cards_to_avoid_wasting`. Hints are included only when their listed cards match visible card names in the current request.

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
  --require-broker-action-type command \
  --max-generic-reason-rate 0.25 \
  --max-cardless-command-rate 0.0 \
  --max-duplicate-decide-commits 0
```

The analyzer prints event counts, broker request/response/commit totals, observed turns, observed action types, latency p50/p95 when present, and quality counters such as generic reasons, low confidence, cardless command choices, mechanical skipped windows, strategic-window rate, duplicate `Decide` commits, stale/timeout rate, and CPU fallback rate. It exits non-zero if a requested coverage or quality threshold is missing, which makes it usable for scripted validation after a longer duel.

Render a human-readable markdown report when you need to inspect decisions:

```bash
python3 Tools/render_llm_decision_report.py Data/ClientData/LlmDecisionLog.jsonl \
  -o /tmp/llm-decision-report.md
```

The report expands runtime `request_json` strings, shows selected cards and text snippets when card metadata is present, surfaces action labels/roles/consequence hints, reports broker-owned attack-target windows and explicit target divergences, surfaces model opponent assessment/timing rationale/rejected alternatives/risk notes, and flags generic reasons or low confidence.

To evaluate provider/prompt changes without relaunching the game, extract and score saved broker requests:

```bash
python3 Tools/evaluate_llm_replay.py Data/ClientData/LlmDecisionLog.jsonl \
  --write-fixtures /tmp/llm-request-fixtures
```

The evaluator runs the configured provider path against captured requests and reports JSON validity, known `action_id` use, card-specific reasons, generic/low-confidence flags, immediate End Phase choices, filtered/mechanical selections, heuristic-baseline comparison, fallback recommendations, and latency p50/p95.

## Local PvP Duel Validation

The current LLM control path is PvP-only and controls the local seat of the client the broker settings are enabled on. A solo/PvE duel against the built-in CPU does not exercise the broker path.

Use two local client folders and one server:

1. Start the broker first and verify `/health`.
2. Build and deploy validation binaries/settings with broker enabled.
3. Launch the root runtime through Steam so it starts `YgoMasterClient.exe`, `masterduel.exe`, and the delayed `YgoMaster.exe` server.
4. Launch the second client from a copied folder such as `../YgoMasterP2` with a different `Data/ClientData/ClientSettings.json` `MultiplayerToken`. Copy the folder after applying the broker settings (or edit the copy's `ClientSettings.json` to match) — the P2 client is the one that must carry `LlmBrokerEnabled` and `LlmBrokerControlPlayer: 1`, because it owns seat `1`. Do not start a second server. On this Linux/Proton setup, use `Tools/launch_p2_client.sh`; it sets the Steam app identity variables that direct Proton launches need.
5. In the root client, go to `DUEL` -> `Duel Room (PvP)` -> `Create a Room`, choose a valid deck, and sit at `Table 1`.
6. In the P2 client, go to `DUEL` -> `Duel Room (PvP)` -> `Enter a Room`, join the room, choose a valid deck, and click `ENTRY` at `Table 1`.
7. Start/accept the duel prompts until both boards load.
8. If `LlmBrokerControlPlayer` is `1`, pass the root player's first turn so control reaches player `1`. The P2 client (whose `Duel.MyID` is `1`) then requests the broker decision for its own seat; the root client ignores the broker because its `Duel.MyID` is `0`.
9. Confirm the P2 folder's `Data/ClientData/LlmDecisionLog.jsonl` contains a `decision_window` for `acting_player: 1`, `llm_broker_request_started`, `llm_broker_response`, and `llm_broker_committed`. Run `Tools/analyze_llm_duel_log.py` with the thresholds above after a longer duel. For a real model-backed run, also confirm `python3 Tools/prepare_llm_runtime.py --status` reports `broker.health.ok: true` and `broker.ready_for_llm_validation: true`.

---
id: YGOMASTER-LLM-002
project: ygomaster
status: in-progress
type: plan
created: 2026-07-01
aliases: ["YGOMASTER-LLM-002"]
---

# YGOMASTER-LLM-002: LLM decision quality improvement plan

## Context

[[YGOMASTER-LLM-001]] proved the broker plumbing: local PvP requests reach the broker, model responses are audited, stale responses are rejected or retried, and committed actions are validated against the current legal-action list. The live test also exposed the next product problem: player 2 can make bizarre choices that are worse than the pre-LLM CPU.

The root cause is not primarily model quality. Schema v2 intentionally kept the payload conservative and public-only. That was correct for proving safety, but it means the model often sees only low-level choices such as `Summon` / `Action` / `Decide` against `card_unique_id` values. It does not reliably see the controlled player's own card names, card text, or tactical role, and the action list still contains engine-level choices such as `Look`, `Surrender`, forced `Draw`, and single-option `Decide` windows.

Goal: make LLM control at least no worse than the built-in CPU in normal validation duels, then use richer card context and tactical prompting to improve meaningful decisions.

## Non-goals

- Do not enable this in live/ranked play.
- Do not expose opponent hidden hand, future deck order, or face-down identities.
- Do not ask the LLM to solve unsupported multi-select, optional-pick, or cancel/skip windows until those flows have validated commit semantics.
- Do not rely on prompt wording alone while the payload lacks card identity.

## Design principles

- `duel.dll` and YgoMaster remain the source of legal actions. The model chooses only from validated `action_id` values.
- The controlled player may see its own private hand. That is normal player knowledge, not hidden-information cheating.
- The opponent's hidden zones stay hidden. Known public zones and revealed cards may be serialized.
- The CPU path is the quality floor. The LLM should control only windows where it has enough context to make a better choice.
- Mechanical windows should not consume model calls.
- Every broker decision should be explainable from the log without manually decoding `card_unique_id` values.

## Plan

### 1. Add schema v3 card context

Add a schema v3 broker request that enriches `public_state` and `legal_actions` with card identity where policy allows it.

Implementation anchors:

- `YgoMasterServer/Llm/LegalActionTypes.cs`
- `YgoMasterServer/Llm/LegalActionExtractor.cs`
- `YgoMasterServer/Llm/LlmDecisionLogSerializer.cs`
- `YgoMasterServer/Llm/LlmBrokerProtocol.cs`
- `YgoMasterServer/Llm/PvpEngineStateLegalActionQuery.cs`

Payload changes:

- Add `controlled_player` or reuse `acting_player` explicitly as the player whose private information can be shown.
- Add card metadata to known cards: `card_id`, `name`, `text`, `kind`, `attribute`, `level`, `atk`, `def`, `scale`, and compact tags where available.
- For `command` legal actions, attach a `card` object when `card_unique_id` resolves to an allowed visible card.
- Expose the controlled player's own hand identities for decision making.
- Expose public/revealed cards already allowed by policy: graveyard identities, revealed hand cards, and eventually face-up field/banished cards after face-state validation.
- Keep opponent closed hand, deck order, extra deck identities, and face-down identities omitted.

Example target shape:

```json
{
  "schema_version": 3,
  "run_effect_seq": 219,
  "acting_player": 1,
  "controlled_player": 1,
  "legal_actions": [
    {
      "action_id": 2,
      "kind": "command",
      "command": "Summon",
      "player": 1,
      "position": 13,
      "index": 0,
      "card_unique_id": 37,
      "card": {
        "card_id": 5037,
        "name": "Example Monster",
        "text": "Once per turn...",
        "kind": "Effect",
        "level": 4,
        "atk": 1800,
        "def": 1000
      }
    }
  ]
}
```

Validation target: a fresh `llm_broker_response.request_json` must be readable by a human and show card names for the controlled player's hand and for card-bound legal actions.

### 2. Use the existing card data parser

Reuse the repo's existing Master Duel card-data parser rather than introducing a second database.

Implementation anchors:

- `YgoMasterServer/YdkHelper.cs` already has `LoadCardDataFromGame(dataDir)` and `GameCardInfo` with `Name`, `Desc`, `Kind`, `Attr`, `Level`, `Atk`, `Def`, `Icon`, `Type`, and `Scale`.
- Runtime card data exists under `Data/CardData/en-US/CARD_Name.bytes`, `CARD_Desc.bytes`, `CARD_Indx.bytes`, and `Data/CardData/#/CARD_Prop.bytes`.

Implementation details:

- Add a small `LlmCardCatalog` wrapper that lazily loads `YdkHelper.LoadCardDataFromGame(Utils.GetDataDirectory(true))` once per process.
- Return compact card metadata by `card_id`; do not reload files per decision.
- Normalize whitespace in card text.
- Cap long card text to a configurable length, for example 800 to 1200 characters, so token use stays bounded.
- If card metadata cannot be resolved, still include `card_id` and `card_unique_id` and log a catalog miss for audit.

Validation target: unit tests should prove known card IDs serialize with names/text, missing card IDs degrade safely, and catalog loading failures do not crash duel execution.

### 3. Clean up the legal-action list

The current action list exposes too many low-level engine choices. Some are legal but not useful strategic decisions.

Immediate filters:

- Hide `Look` from broker decisions unless a debug flag explicitly allows it.
- Hide `Surrender` unless a future surrender policy explicitly allows it.
- Do not ask the model to choose forced/simple `Draw` windows.
- Do not ask the model to choose a single available `Decide` option.
- Keep `MovePhase End` available, but make it less attractive in prompt/ranking when meaningful playable actions exist.

Auto/fallback behavior:

- If a window has zero meaningful choices after filtering, use the CPU path or existing automatic behavior.
- If a window has exactly one safe mechanical choice, auto-commit it only when the same action is known to be non-strategic and current validation passes.
- Otherwise call the broker with the filtered action list.

Action grouping:

- Preserve `action_id` as the final commit key.
- Add human-friendly labels such as `Normal Summon: Duza the Meteor Cubic Vessel`, `Activate: Cubic Karma`, `Set Spell/Trap: ...`, `Battle Phase`, `End Phase`.
- Group duplicate command choices by card where possible so the prompt is scannable.

Validation target: live broker requests should no longer show dozens of deck `Look` actions or extra-deck `Surrender` actions, and model calls should be reserved for meaningful decisions.

### 4. Improve the broker prompt and response contract

The current prompt asks for one legal action, but it does not teach tactical priorities. After schema v3, make the prompt ask for real decision quality.

Prompt requirements:

- State that the model is controlling player `acting_player`.
- Summarize that the model can use its own hand and public board/graveyard information only.
- Require the model to choose from `legal_actions` by `action_id`.
- Tell the model to prefer board development, advantage, lethal damage, removal of threats, and resource preservation.
- Tell the model not to end the phase while strong proactive legal actions remain unless it has a reason.
- Tell the model not to choose generic or debug actions such as `Look`.
- Ask for a concise tactical reason naming the card or phase chosen.

Response changes:

```json
{
  "run_effect_seq": 219,
  "action_id": 2,
  "reason": "Normal Summon Duza to start the Cubic line and load a Cubic card.",
  "confidence": 0.74,
  "plan": "Develop a monster first, then use available follow-up actions."
}
```

Validation target: accepted provider responses should include card-specific reasons. Generic reasons such as `first option`, `summon`, or `play card` should be treated as low-confidence during evaluation, even if they are still technically valid JSON.

### 5. Add hybrid CPU fallback as a quality floor

The LLM should not replace the CPU for every window until it is demonstrably better.

Fallback rules:

- Use CPU fallback when card context is unavailable for a meaningful card-bound action window.
- Use CPU fallback on provider timeout, parse failure, invalid action, stale response without retryable current snapshot, or low confidence.
- Use CPU fallback for unsupported list/dialog flows.
- Use CPU fallback when the provider chooses a filtered/debug action, gives a generic reason, or returns confidence below threshold.
- Consider a per-provider threshold: CLI providers may need stricter latency/fallback rules than direct API providers.

Longer-term option:

- Investigate a shadow-CPU comparison mode where CPU choices are logged for the same class of windows without committing them. If the engine cannot expose CPU's intended action safely, keep this as an offline/simulator goal rather than live runtime behavior.

Validation target: after fallback rules, the broker should never make obvious mechanical nonsense choices when the CPU path would have handled them cleanly.

### 6. Add a readable decision report tool

Create a tool that turns JSONL audit logs into a markdown report.

Proposed tool:

- `Tools/render_llm_decision_report.py`

Inputs:

- `Data/ClientData/LlmDecisionLog.jsonl`
- Optional output path, for example `/tmp/llm-decision-report.md`

Report sections:

- Duel summary: turns, broker requests, responses, commits, stale rejects, timeouts.
- One section per broker response.
- Request seq, turn, phase, acting player, selected action, committed action.
- Selected card name and text snippet.
- Model reason, confidence, and plan.
- Top-level list of filtered or hidden actions if debug mode is enabled.
- Flags for suspicious choices: generic reason, low confidence, immediate End Phase, unsupported unknown card, repeated stale response.

Validation target: a user can open one markdown report and understand why P2 chose each play without reading raw JSON.

### 7. Add offline replay and provider evaluation

Use captured broker requests as fixtures so we can improve decision quality without constantly relaunching the game.

Implementation ideas:

- Extract `request_json` objects from `llm_broker_response` events into fixtures.
- Run providers against those fixtures in dry-run mode.
- Score output for JSON validity, known `action_id`, card-specific reason, confidence, latency, and whether the action is obviously filtered/mechanical.
- Keep a small suite of hand-curated decision windows from Cubic and starter decks.
- Compare provider behavior across `deepseek_api`, `openai_compatible`, `grok_cli`, and `opencode_cli`.

Validation target: provider changes can be tested before another live duel, and bad prompt changes are caught by replay fixtures.

### 8. Add turn memory and tactical continuity

Individual decision windows are too local. The model needs a compact memory of the duel plan.

Possible fields:

- Last N committed broker actions with card names and reasons.
- Cards this player has already used this turn.
- Whether normal summon has been used when detectable.
- Current phase plan: `develop board`, `attack`, `pass`, `respond defensively`.
- Known threats on opponent board.

Implementation caution:

- Keep this derived from logged/visible state, not hidden zones.
- Reset on duel start and generation changes.
- Do not let stale provider text from an old duel leak into a new duel.

Validation target: model reasons should reference previous choices coherently, not repeatedly choose disconnected one-step actions.

### 9. Improve public board and threat context

Schema v3 should start with own hand and legal-action card names, but real strategy also needs public board context.

Next visibility targets after runtime validation:

- Face-up monster/spell/trap identities on both fields.
- Graveyard card identities for both players.
- Banished face-up identities if face-state semantics are validated.
- Current attack/defense, battle position, and face state when exposed safely.
- Known LP and counts already present in schema v2.

Validation target: the model can explain choices in relation to public threats, not only its own hand.

### 10. Tune provider and latency strategy

The subscription-backed CLI path is useful for testing, but it is slow and can cause stale responses. Better decision quality also needs enough time to think.

Actions:

- Prefer direct API providers for serious quality testing once credentials exist.
- Keep `grok_cli` and `opencode_cli` as experimental adapters.
- Raise `LlmBrokerTimeoutMs` and provider timeout together for high-latency providers.
- Keep stale-response retry, but measure retry rate and reject rate.
- Consider prewarming or persistent provider sessions if CLI adapters support it.
- Track latency p50/p95 by provider.

Validation target: quality testing should separate bad reasoning from late responses. A model that often responds after the state changes is not usable even if its chosen action would have been good.

### 11. Add strategy packs or archetype hints

Card text alone may be too broad for strong play. For local testing decks, add optional strategy hints that describe the deck's normal game plan.

Possible file:

- `Data/ClientData/LlmStrategyHints.json`

Possible contents:

- Deck name or hash.
- One-paragraph game plan.
- Starter cards, extenders, removal, win condition.
- Short combo hints.
- Cards to avoid wasting.

Rules:

- Hints are optional and local-only.
- Hints must not contain hidden information from the opponent.
- Hints should be included only when the controlled player's deck is known.

Validation target: Cubic/player-2 decisions should stop being generic and start reflecting the deck's intended lines.

### 12. Add explicit quality metrics

Current analyzer proves the broker ran. It does not prove the model played well.

Add metrics:

- Meaningful model-call count vs mechanical skipped count.
- Filtered action count by reason.
- Generic-reason rate.
- Low-confidence rate.
- Immediate End Phase while playable actions existed.
- Timeout/stale/retry rate.
- CPU fallback rate.
- Duel completion rate.
- Manual reviewer rating per committed broker action.

Validation target: every test duel ends with both coverage metrics and quality metrics.

## Milestones

### Milestone 1: Decision report and evidence baseline

- Add `Tools/render_llm_decision_report.py`.
- Use the last live duel log as baseline evidence.
- Report all current bad choices with selected action, reason, and missing card context.
- No runtime behavior changes yet.

Exit criteria:

- A markdown report clearly explains the last duel's broker choices.
- The report flags `first option`, `select first`, and cardless `summon` reasons as low-quality.

### Milestone 2: Schema v3 card catalog and serialization

- Add lazy card catalog loading from `YdkHelper.LoadCardDataFromGame`.
- Add card metadata to known cards and command actions.
- Expose controlled-player hand identities.
- Keep opponent hidden zones hidden.
- Update docs and tests.

Exit criteria:

- Unit/harness tests pass.
- Captured broker requests include card names/text for controlled-player hand actions.
- No opponent hidden hand/deck identities appear in request JSON.

### Milestone 3: Action filtering and mechanical-window handling

- Filter `Look` and `Surrender` from broker-visible choices.
- Skip or auto-handle forced `Draw` and single-option `Decide` windows.
- Preserve fallback for unsupported windows.
- Add audit events for filtered/skipped decisions.

Exit criteria:

- Live requests no longer contain deck `Look` spam or `Surrender` spam.
- Analyzer/report shows fewer but more meaningful broker calls.

### Milestone 4: Tactical prompt, confidence, and fallback

- Extend provider JSON parsing to accept optional `confidence` and `plan`.
- Add tactical prompt text using schema v3 card context.
- Add low-confidence/generic-reason fallback policy.
- Keep strict action validation unchanged.

Exit criteria:

- Provider reasons name actual cards or phases.
- Generic `first option` responses are flagged or fall back.
- Live duel does not regress into mechanical nonsense choices.

### Milestone 5: Offline replay evaluation

- Extract request fixtures from live logs.
- Add provider dry-run evaluation against saved fixtures.
- Track latency and quality metrics across providers.
- Keep a small suite of Cubic/starter-deck decision windows.

Exit criteria:

- Prompt/provider changes can be evaluated without relaunching Master Duel.
- Provider choice can be based on measured validity, latency, and card-specific reasoning.

### Milestone 6: Longer live-duel validation

- Deploy schema v3/filter/fallback build to root and P2.
- Run a local PvP room duel for at least 4 turns or until completion.
- Generate analyzer summary and markdown decision report.
- Manually compare P2 decisions against the pre-LLM CPU baseline.

Exit criteria:

- Broker decisions are card-aware and explainable.
- Stale/retry rate is acceptable for the chosen provider.
- P2 behavior is at least comparable to pre-LLM CPU for mechanical decisions.

## Validation gates

Local checks:

- `python3 -m unittest Tools/test_llm_broker.py Tools/test_prepare_llm_runtime.py Tools/test_llm_duel_log_analyzer.py`
- `python3 -m py_compile Tools/llm_broker.py Tools/analyze_llm_duel_log.py Tools/render_llm_decision_report.py`
- `~/.dotnet/dotnet run --project YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.Net8.csproj`
- `~/.dotnet/dotnet build YgoMaster.sln -v:minimal -p:OutputPath=/tmp/ygomaster-build/solution/`
- `git diff --check` for touched files.

Runtime checks:

- `Tools/prepare_llm_runtime.py --status` confirms root binaries match redirected build before launch.
- Broker `/health` reports expected provider and `provider_configured=true` for provider-backed tests.
- Root `Data/ClientData/LlmDecisionLog.jsonl` contains schema v3 request JSON with card names.
- P2 log may still contain only local decision windows; root log remains the broker audit source.
- Decision report shows no hidden opponent hand/deck identities.
- Analyzer passes minimum turn and commit thresholds.

Quality checks:

- No broker-visible `Look` or `Surrender` spam in ordinary main-phase windows.
- No accepted generic reason unless the action was genuinely forced.
- LLM decisions name the selected card where applicable.
- Immediate `End` choices are justified by board/context or fall back.
- Manual review rates the LLM decisions as at least comparable to CPU for the same deck and turn class.

## Risks and mitigations

- Token bloat from full card text.
  - Mitigation: cap text length, include only cards relevant to current legal actions plus known public threats, and move archetype guidance to compact strategy hints.

- Hidden-information leakage.
  - Mitigation: explicit visibility policy, tests that assert opponent hidden zones are absent, and decision reports that list serialized hidden-zone counts without identities.

- Incorrect card identity mapping from unique IDs.
  - Mitigation: serialize both `card_unique_id` and resolved `card_id`, log catalog misses, and validate against visible in-game cards during live tests.

- CLI provider latency.
  - Mitigation: keep retry, measure stale rate, tune timeouts, and prefer direct API for serious quality validation.

- LLM overuses phase/end actions.
  - Mitigation: prompt guidance, action ranking, low-confidence fallback, and report flags for early End Phase choices.

- CPU fallback hides LLM weakness.
  - Mitigation: log every fallback reason and track CPU fallback rate separately from successful LLM commits.

- Strategy hints become too deck-specific.
  - Mitigation: keep hints optional and local; schema v3 card context should still work without hints.

## Open questions

- What exact visibility policy should apply to the controlled player's extra deck? The player normally knows its extra deck, but exposing all extra-deck identities may increase token cost and is not always needed for a current action.
- Should face-up field and banished identities be included in the first schema v3 slice, or should they remain a separate visibility-validation milestone?
- What confidence threshold should trigger CPU fallback for each provider?
- Should `Draw` ever be model-controlled, or should draw/forced-progress windows stay mechanical forever?
- Can the built-in CPU's intended action be observed without committing it, or is CPU comparison limited to live/manual baseline review and simulator work?
- How much card text is enough: full text, first N characters, or a separate card-summary cache?

## Progress

- **2026-07-01:** Implemented the first decision-quality slice in the YgoMaster checkout. Added schema v3 request serialization with `controlled_player`, lazy card metadata through the existing `YdkHelper.LoadCardDataFromGame` path, controlled-player hand visibility, command-action `card_id`/`card` metadata, and hidden-opponent-hand tests. Filtered broker-visible `Look`, `Surrender`, `Draw`, and single-option `Decide` mechanical actions so those windows fall back instead of consuming model calls. Extended provider responses/logs with optional `confidence` and `plan`, updated tactical broker prompting, and made the analyzer/report tools surface generic reasons, low confidence, cardless command selections, selected card names, and card text snippets.
- **2026-07-01:** Live duel validation reached the first broker-controlled `WaitInput` at `run_effect_seq=71`; schema v3 request/response/commit succeeded and selected a command action. The live log also exposed that all serialized `card` metadata was `null` even though raw `card_id` values were present and opponent hidden-card visibility remained closed. Traced the likely root cause to runtime catalog path resolution using a single base-directory candidate and swallowing load failures. Added `LlmCardDataDirectoryResolver` so the catalog tries base-directory, assembly-location, and current-directory candidates, requires the expected `Data/CardData` files, and logs missing/load failures through `YdkLlmCardCatalog`.
- **2026-07-01:** Investigated the stalled live summon after the broker selected `Diskblade Rider`. Root had committed `DLL_DuelComDoCommand player:1 pos:13 indx:0 cmd:4 seq:132`, then the duel remained at `run_effect_seq=134` with `Diskblade Rider` still in player 1's hand. P2's local log had no broker request/response events, proving root was trying to control remote player 1 while P2, whose `MyID` is 1, never owned the broker decision. Added `LlmBrokerControlPolicy` so only the local client whose `MyID` matches `LlmBrokerControlPlayer` starts broker control, while the other client falls back to CPU thinking for remote views instead of committing remote actions. Also copied `Data/CardData` into the P2 runtime so P2 can resolve card names locally on the next launch.
- **2026-07-02:** The next live rerun proved P2 now owned the broker request and committed the selected summon locally, but the card still did not move because normal summon is a two-step engine flow. P2 committed `DLL_DuelComDoCommand player:1 pos:13 indx:5 cmd:4 seq:130` for `Flying Kamakiri #1`, then duel.dll entered a follow-up placement `WaitInput` at `run_effect_seq=132` where the extractor returned no legal actions. A later manual placement command, `DLL_DuelComDoCommand player:1 pos:2 indx:0 cmd:12 seq:132`, advanced to `CardMove` / `RunSummon`. Added summon-placement extraction from `DLL_DlgProcGetSummoningMonsterUniqueID` plus `DLL_DuelDlgGetPosMaskOfThisSummon`, producing `Decide` command actions for available monster zones.

## Validation

- **2026-07-01:** Passed `python3 -m unittest Tools.test_llm_broker Tools.test_prepare_llm_runtime Tools.test_llm_duel_log_analyzer Tools.test_render_llm_decision_report`, `python3 -m py_compile Tools/llm_broker.py Tools/analyze_llm_duel_log.py Tools/render_llm_decision_report.py Tools/prepare_llm_runtime.py`, `~/.dotnet/dotnet run --project YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.Net8.csproj`, `~/.dotnet/dotnet build YgoMaster.sln -v:minimal -p:OutputPath=/tmp/ygomaster-build/solution/`, `python3 Tools/llm_broker.py --self-test`, and `git diff --check`. No live duel validation has been run yet for this slice.
- **2026-07-01:** Live analyzer result after the duel start showed `request_starts=1`, `successful_responses=1`, `commits=1`, broker action type `command`, turns `[0, 1]`, and no errors. Quality flags were `cardless_command_choices=1` and `generic_reasons=1` because the catalog metadata was absent in the live request. After the resolver fix, `~/.dotnet/dotnet run --project YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.Net8.csproj` and `~/.dotnet/dotnet build YgoMaster.sln -v:minimal -p:OutputPath=/tmp/ygomaster-build/solution/` both passed; `git diff --check` reported only existing line-ending warnings. Fixed binaries were built under `/tmp/ygomaster-build/solution/` but were not deployed into the running live clients.
- **2026-07-01:** Re-ran live duel validation after deploying the resolver-fixed binaries to both runtime folders. The fresh log had `card_refs=230`, `card_named=230`, `card_null=0`, and `opponent_known_card_leaks=0` before the first broker response. The first player-1 broker request at `run_effect_seq=132` produced a successful command commit: `Summon` `Diskblade Rider` (`card_id=7608`) with reason `Summon Diskblade Rider to develop board with 1700 ATK effect monster` and plan `Normal summon strongest hand monster first`. Analyzer passed with `request_starts=1`, `successful_responses=1`, `commits=1`, broker action type `command`, no errors, `cardless_command_choices=0`, and `low_confidence=0`.
- **2026-07-01:** Added focused harness coverage for local broker ownership and remote-view fallback, then passed `~/.dotnet/dotnet run --project YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.Net8.csproj` and `~/.dotnet/dotnet build YgoMaster.sln -v:minimal -p:OutputPath=/tmp/ygomaster-build/solution/`. `git diff --check` reported only the existing LF/CRLF warnings. Deployed the rebuilt `YgoMaster.exe` and `YgoMasterClient.exe` to root and `../YgoMasterP2`; both runtime settings now show `LlmBrokerControlPlayer: 1`, `LlmBrokerTimeoutMs: 60000`, and broker logging enabled. Live rerun is still required because the stuck duel is using old in-memory processes.
- **2026-07-02:** Added harness coverage for summon-placement extraction and commit planning, then passed `~/.dotnet/dotnet run --project YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.Net8.csproj` and `~/.dotnet/dotnet build YgoMaster.sln -v:minimal -p:OutputPath=/tmp/ygomaster-build/solution/`. `git diff --check` again reported only existing LF/CRLF warnings. Deployed the rebuilt binaries to root and `../YgoMasterP2`, restarted both sessions, and confirmed two `masterduel.exe` processes plus root server ports `4988`/`4989`, LAN forwarders, and broker port `4991`.

## Operational gotchas

- **2026-07-01:** In a two-client PvP validation, a client must not broker-control a remote player just because `LlmBrokerControlPlayer` matches that remote player. Gate broker ownership on `player == MyID == LlmBrokerControlPlayer`; otherwise the wrong client can log a committed remote `DLL_DuelComDoCommand` and show a local cut-in while the authoritative player state never moves.
- **2026-07-02:** Normal summon in this PvP flow is not complete after the hand-card `Summon` command. The engine may enter a follow-up placement `WaitInput`; if `DLL_DlgProcGetSummoningMonsterUniqueID` and `DLL_DuelDlgGetPosMaskOfThisSummon` are nonzero, commit a placement with `DLL_DuelComDoCommand(actingPlayer, selectedMonsterZone, 0, Decide)` before expecting the card to move to the field.

## Resolution

Planned. This work item exists because [[YGOMASTER-LLM-001]] proved the broker integration but also showed that schema v2 is too under-informed for good strategy. The next implementation should prioritize schema v3 card context, action filtering, readable decision reporting, tactical prompting, and hybrid CPU fallback before judging provider/model quality.

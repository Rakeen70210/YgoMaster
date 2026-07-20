---
name: llm-broker-pipeline
description: Use when working on or reasoning about the LLM opponent - broker requests, legal actions, decision windows, run_effect_seq, public_state, action validation, commit, or CPU fallback.
---

# LLM Broker Pipeline

## Overview

The LLM opponent is a **thin adapter + external loopback broker** (architecture decisions in the vault `projects/ygomaster/decisions.md`, wiki-link `[[projects/ygomaster/decisions]]`): the game process never calls a model API directly. The game enumerates legal actions, POSTs them to `http://127.0.0.1:4991/decide`, gets back one `action_id`, **re-validates it against the current duel state**, and only then commits through the same `duel.dll` calls a human click would use. Everything is default-off and PvP-only.

## The decision lifecycle

```
duel.dll view (WaitInput | RunDialog | RunList)  [PvP only]
  → LlmBrokerControlPolicy.ShouldControlPlayer?      (client: DuelDll.cs TryStartLlmBrokerDecision)
  → LegalActionExtractor.Extract → DecisionSnapshot   (run_effect_seq, public_state, legal_actions)
  → LlmBrokerRequestGate.Evaluate                     (StartRequest | SuppressForPending | Fallback)
  → background thread: LlmBrokerClient.RequestDecision → HTTP POST /decide
  → response queued via ActionsToRunInNextSysAct      (commits on duel thread, next SysAct tick)
  → TryCommitLlmBrokerDecision: re-extract snapshot, LlmBrokerProtocol.ValidateResponse
  → commit (LlmActionCommitPlan → DLL_DuelComDoCommand / MovePhase / DlgSetResult / ListSetIndex)
  → JSONL audit event at every step
```

Key files, all under `YgoMasterServer/Llm/` unless noted (link-compiled into server, client, and both harness projects — see ygomaster-map):
`LlmBrokerProtocol.cs` (schema v3, serialize/parse/validate) · `LegalActionExtractor.cs` + `LegalActionTypes.cs` (enumeration) · `LlmBrokerClient.cs` (transport) · `LlmBrokerRequestGate.cs` (in-flight state machine) · `LlmBrokerControlPolicy.cs` (who acts) · `LlmActionCommitPlan.cs` (action → native call) · `LlmDecisionLogSerializer.cs` (audit) · `LlmCardCatalog.cs`/`YdkLlmCardCatalog.cs` (card metadata via `YdkHelper.LoadCardDataFromGame`) · client integration in `YgoMasterClient/DuelDll.cs` · broker service `Tools/llm_broker.py`.

## Invariants — do not break these

1. **Control policy**: broker acts only when `player == myId && player == controlPlayer` (`LlmBrokerControlPolicy.ShouldControlPlayer`; harness test `BrokerControlPolicyOnlyControlsLocalConfiguredPlayer`, "p2 controls p2"). The client that owns the seat runs the broker — in the two-client validation flow that is the **P2 client** for seat 1, and its folder is where the broker decision log appears. `LlmBrokerControlPlayer: -1` is the safe default (disables even if `LlmBrokerEnabled` is accidentally true).
2. **Hidden information**: `public_state` exposes the controlled player's own hand, revealed cards, graveyards, LP, and position **counts** only. Never serialize: opponent closed hand identities, deck order, extra-deck identities, face-down identities, transient-selection identities. Harness tests `DoesNotExposeHiddenPublicStateCards` / `DoesNotExposeOpponentHiddenHandCardMetadata` enforce this — extend them with any schema change.
3. **Legality lives in the engine**: the broker's `reason`/`confidence`/`plan` are audit-only. An action commits only if it survives `ValidateResponse` against a **freshly re-extracted** snapshot (stale seq → `stale_run_effect_seq`; id missing → `unknown_action_id`; id now means something else → `action_changed` via `IsSameAction`).
4. **Fallback must always exist**: any failure (timeout, parse, rejection, commit exception) falls back to the built-in CPU path. A broker outage must never hang a duel.
5. **No secrets in game config**: API keys live in broker environment variables (`YGO_LLM_BROKER_*`), never in `ClientSettings.json`.
6. **Duel generation**: `LlmBrokerDuelGeneration` increments on duel start/end; late responses from a previous duel are discarded.

## Quick reference

| Concept | Meaning |
|---|---|
| `run_effect_seq` | Monotonic engine counter (incremented per `RunEffect`, see `Pvp.cs`). Timestamps every snapshot; response must match the *current* one at commit time. |
| `action_id` | **Index into the request's legal_actions list** — not stable across snapshots. Same seq can still reorder if state merged in between (`action_changed`). |
| Action kinds | `move_phase`, `command`, `dialog_result`, `list_index` (strict single-select only: `is_multi_mode==0`, min==max==1), and validated cancellable `CheckTiming`/`CheckChain` decline `cancel` (`CancelCommand2(false)`). When either response window is cancellable and the only extractable choice is empty-window decline, it is **mechanical** and auto-committed with **no model call**. Generic skip/pass and multi-select remain unsupported → CPU/temporary-CPU fallback. |
| Filtered/skipped windows | `Look`, `Surrender`, forced `Draw`, single-option `Decide`, and mechanical follow-up windows marked `is_mechanical` never reach the broker unless they are safely auto-committed (including sole empty optional CheckTiming/CheckChain decline). Optional effect yes/no prompts are strategic `dialog_result` actions with `is_yes_no_prompt: true`. Cancellable timing/chain windows with extractable activations plus decline stay strategic for the model. Non-strategic snapshots are logged as `llm_broker_skipped_window` and fall back to CPU/default handling unless auto-committed. |
| Position map | 0–12 field zones, 13 hand, 14 deck, 15 extra, 16 GY, 17 banished, 18 transient selection. |
| Schema version | `3` (`LlmBrokerProtocol.SchemaVersion`). Hardcoded in harness + `Tools/test_llm_broker.py` fixtures — schema changes touch those too. |
| Providers | `deterministic` (default, no key), `deepseek_api`, `openai_compatible`, `grok_cli`, `opencode_cli` via `YGO_LLM_BROKER_PROVIDER`. CLI providers are slow/fragile; raise `LlmBrokerTimeoutMs` and `YGO_LLM_BROKER_CLI_TIMEOUT` together. |
| Threading | Request runs on a background thread; commit is queued through `ActionsToRunInNextSysAct` so it executes on the duel thread. Never commit from the HTTP thread. |

Settings (`Data/ClientData/ClientSettings.json`): `LlmBrokerEnabled`, `LlmBrokerUrl`, `LlmBrokerTimeoutMs` (55000 runtime-prep default for slower CLI-backed validation; broker provider timeout must stay below it), `LlmBrokerControlPlayer`, `LlmDecisionLogEnabled` → `Data/ClientData/LlmDecisionLog.jsonl`.

## Where the project is headed

Open roadmap lives in the vault work items under `/home/rakeenhuq/Onedrive/Obsidian Vault/projects/ygomaster/work/` (e.g. `[[YGOMASTER-LLM-002]]` decision quality, `[[YGOMASTER-LLM-004]]` public history, `[[YGOMASTER-LLM-005]]` lookahead). Read those before proposing "new" ideas — most are already scoped with rationale.

## Common mistakes

| Mistake | Reality |
|---|---|
| Treating `llm_broker_rejected` as a broker bug | It means the *duel state changed* between request and commit. See debugging-llm-decisions. |
| Testing the broker in a solo/PvE duel | The path is PvP-only (`IsPvpDuel` gate). Use the two-client room-duel flow in running-the-runtime. |
| Exposing new state "because the model needs it" | Hidden-info invariant #2 is a hard line. Identity exposure needs validated engine reads first (LLM-002 milestone 8). |
| Trusting `action_id` across snapshots | It's a list index. Re-validate; never cache. |

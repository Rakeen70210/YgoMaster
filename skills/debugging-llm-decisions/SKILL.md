---
name: debugging-llm-decisions
description: Use when the LLM opponent misbehaves - broker responses never commit, llm_broker_rejected or commit_skipped events repeat, duels hang or freeze, the wrong player is controlled, or decisions look low-quality.
---

# Debugging LLM Decisions

## Overview

Every broker decision leaves a complete JSONL audit trail. **Debug from the log, not from code speculation** — `Data/ClientData/LlmDecisionLog.jsonl` (on the game machine, runtime folder; requires `LlmDecisionLogEnabled: true`) plus `python3 Tools/prepare_llm_runtime.py --status` answer most questions in minutes.

Background required: llm-broker-pipeline (lifecycle, seq/action_id semantics).

## Step 1 — Get the dashboard

```bash
python3 Tools/prepare_llm_runtime.py --status
```
Checks: broker `/health` ok + provider configured, game processes running, runtime binaries `matches_build`, decision log exists, runtime vs source settings. Most "broker does nothing" reports die here (broker not running, `LlmBrokerControlPlayer: -1`, stale binaries deployed).

## Step 2 — Read the event sequence

Event kinds per decision window (emitted by `LlmDecisionLogSerializer`, written from `DuelDll.cs`):

| Event | Meaning | If it's the last one you see |
|---|---|---|
| `decision_window` | Snapshot captured (state + legal actions) | Broker never asked: control policy denied (wrong seat/settings), zero legal actions, or gate suppressed. Check `acting_player` vs the client's `MyID` vs `LlmBrokerControlPlayer`. |
| `llm_broker_request_started` | HTTP request launched | Timeout or transport failure → check broker terminal, `LlmBrokerTimeoutMs` vs provider timeout. |
| `llm_broker_response` | Broker replied (`success` true/false) | `success: false` → read `error`/`error_detail`. `provider_error` = provider-side (parse/timeout/HTTP); game fell back to CPU correctly. |
| `llm_broker_rejected` | Response failed re-validation against current state | See triage table below. |
| `llm_broker_commit_skipped` | Valid response but couldn't commit (duel ended, generation changed, commit threw) | Usually benign duel-end race; repeated → read `reason`. |
| `llm_broker_committed` | Action applied | Success. |

## Step 3 — Triage `llm_broker_rejected`

By construction, rejected = *the response was valid for the state it was asked about, but the state moved*. Compare `request_run_effect_seq` vs `current_run_effect_seq` in the event:

| `error` | Seqs | Cause | Fix direction |
|---|---|---|---|
| `stale_run_effect_seq` | differ | Broker latency lost the race — something advanced the duel while the model thought (remote client auto-acked, CPU fallback from an earlier timeout, timer views). The retry path re-requests at the new seq (`retrySnapshot`, re-queued in the `finally` of `TryCommitLlmBrokerDecision` — **unbounded**, no counter/backoff), so slow CLI providers can loop forever: respond → stale → retry → stale… | Raise `LlmBrokerTimeoutMs` **and** provider/CLI timeout together; prefer API providers over CLI. Occasional stale+retry-then-commit is normal latency, not a bug. |
| `unknown_action_id` / `action_changed` | equal | Legal-action list shifted at the same seq — `action_id` is a list index and engine state merged between the two `Extract` calls. No retry; CPU falls back once per window. | This is an ordering/extraction issue; capture both snapshots from `request_json` vs current and diff the action lists. |

Also check the server console for `OnDuelComDoCommand bad seq` (`Pvp.cs`) — evidence a *second actor* (other client's UI/CPU) is committing over the broker. Classic setup error: the P2 folder was copied with broker settings still enabled, or the broker is enabled on the client that doesn't own the seat (see control policy in llm-broker-pipeline).

## Step 4 — Bulk analysis for longer duels

```bash
python3 Tools/analyze_llm_duel_log.py Data/ClientData/LlmDecisionLog.jsonl \
  --min-turns 2 --min-broker-commits 1 --require-broker-action-type command
python3 Tools/render_llm_decision_report.py Data/ClientData/LlmDecisionLog.jsonl -o /tmp/llm-report.md
```
The analyzer exits nonzero on missing coverage (scriptable gate); counts rejections/skips and quality flags (generic reasons, low confidence, cardless commands). The report renders decision-by-decision markdown with card names — the right tool for "the model played badly" complaints.

## Other frequent failures

| Symptom | Check |
|---|---|
| Duel hangs at opponent's turn | Broker enabled but unreachable *and* fallback not firing? Enable `PvpLogToFile`/`PvpLogToConsole` — broker failures log through the normal PvP path. Verify with `/health`: `curl http://127.0.0.1:4991/health`. |
| Controlled client loops `RunDialog` → empty `WaitInput` → `RunDialog` every ~3 seqs | Signature: owning seat (`controlled=true`, `MyID` matches acting), `WaitInput` params `(5,0,2)` = `DuelMenuActType.CheckChain` + `DuelMenuParamType.TrueCancel`, `legal_actions: []`, DuelLog `has_local_interaction=false` then **NativeDefault** that does not advance (live 2026-07-17 seq 650–1099). Root cause: empty optional CheckChain omitted decline. Fix semantics: sole mechanical `Cancel` (`CancelCommand2(false)`, scope `empty_check_chain_decline`) auto-committed — **no model call**. Not the non-owner remote-wait pattern (remote `CpuThinking` + only `DLL_DuelSysAct`). |
| Controlled client cycles `WaitInput(4,0,2)` → `RunDialog` → `RunDialog` under advancing seqs | `4` is `CheckTiming`, not `CheckChain`. Empty cancellable CheckTiming uses the same automatic `CancelCommand2(false)` decline (`empty_check_timing_decline`). A separate logical-window watchdog fingerprints repeated controlled `WaitInput` routes without `run_effect_seq`; the third no-progress recurrence requests an explicit server-authoritative temporary-CPU recovery and logs `llm_broker_stuck_window_recovered`. |
| CheckTiming decline advances seq but the same three-view cycle continues indefinitely | The automatic decline itself is part of the no-progress cycle. It must remain observable by `LlmStuckWindowWatchdog`, and committing `empty_check_timing_decline` / `empty_check_chain_decline` must preserve watchdog history; all unrelated automatic actions still reset it. Live signature: `WaitInput(4,0,2)` → `RunDialog(4,1,4)` → `RunDialog(0,273,3)`. |
| Strategic activation+decline window repeats under new `run_effect_seq` after successful Decline (Call / Xyz Soul) | Milestone 3C: `LlmSemanticOptionalResponseTracker` fingerprints without seq/action_id; second identical window reuses decline (`llm_semantic_window_decision_reused`); third uses temporary CPU `semantic_optional_response_loop`. Decline commits must not full-reset semantic history. |
| Provider error activates optional card via list order (`quality_passer`) | Milestone 3D: recovery prefers `optional_decline` over activations when no parsed provider intent; lease clear reason is `provider_recovery`, not `provider_commit`. Do not enable `YGO_LLM_BROKER_PROVIDER_ERROR_FALLBACK=1` as a substitute. |
| Strategic optional-effect `stale_run_effect_seq` after slow provider while mechanical empty `RunDialog` advanced (Goblindbergh `202→203→205`) | Adapter race, not model quality. Expect `llm_strategic_prompt_lease_*` events: grant before provider, PvP holds `DLL_DuelSysAct`, mechanical ack suppressed under lease, matching native input releases once. Missing lease hold → overtake; late response after expiry → temporary CPU + `llm_strategic_continuation_diverged`, never remap stale action. |
| Xyz summon repeats or an effect selection hangs | Check for `WaitInput` with `view_param1=8` (`Selection`). Never auto-submit the generic `Decide`, and do not use the client-side `CpuThinking` view: PvP seats are engine `Human` seats, so that view cannot choose. The owning client must request the seat-validated temporary CPU handoff; all selection/dialog/list sub-prompts stay suppressed until the PvP worker restores `Human` control at the first resolution or ordinary-input boundary. |
| Attack/defense summon-position dialog appears on the wrong client | Check for `RunDialog` with `view_param1=10` (`SelStand`). It is interactive even when `GetDialogSelectItemNum()` is zero, so it must pass through ownership routing: `RunDefault` on the owning client and `CpuThinking` on the remote client. If logged as `empty RunDialog: native default`, the no-choice classifier is bypassing ownership. |
| Forced `OK` popup stalls the broker-controlled client | A no-choice informational `RunDialog` is mechanical, not an LLM decision. The controlled owning client must commit `dialog_acknowledgement` with the engine default result from `view_param3`; the remote client uses `CpuThinking`, while selectable, Yes/No, Confirm, and `SelStand` dialogs remain interactive. Live example: Dust Tornado's "There is no applicable card in your hand" popup was `RunDialog(0,339,0)` at sequence 332. |
| Broker works, but deterministic choices only | `/health` shows `provider_configured: false` → `YGO_LLM_BROKER_*` env vars missing in the broker's environment. |
| Summon commits but card never hits the field | Normal summon in PvP is two-step (command, then placement). Placement windows are their own decision (`DLL_DlgProcGetSummoningMonsterUniqueID` + position mask extraction in `LegalActionExtractor`). |
| Nothing logs at all | `LlmDecisionLogEnabled: false`, or you're in a solo duel (path is PvP-only), or you're reading the *source* `YgoMaster/Data/...` instead of the *runtime* folder on the game machine. |
| Need full model thinking / raw CLI output | Decision log only keeps structured fields. Enable broker env `YGO_LLM_BROKER_REASONING_LOG=1` (or a path) → `Data/ClientData/LlmReasoningLog.jsonl` with raw stdout/stderr, API `reasoning_content`, and parse errors. Optional `YGO_LLM_BROKER_REASONING_LOG_INCLUDE_PROMPT=1`. |

## Reproducing offline (no game needed)

The harness fakes the whole engine: `dotnet run --project YgoMasterLlmTestHarness/YgoMasterLlmTestHarness.Net8.csproj`. To replay a live bug, lift `request_json` from an `llm_broker_response` event and POST it at a local broker (`python3 Tools/llm_broker.py`, deterministic mode) — no client rebuild required.

#!/usr/bin/env python3
"""Summarize CampaignCpuAuditLog.jsonl for PR4b/PR5 acceptance gates.

Segments the audit by pack_loaded, counts decisions/commits/leases/safety events,
optionally loads a chapter pack for required_for_slice / never-fired reports, and
exits nonzero when --require-* gates fail.

This does not prove duel.dll behavior; it scores live or fixture audit evidence.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple


# Product first-slice pin (Track B design / 11010078 pack).
DEFAULT_DECK_PIN = (
    "sha256:d1ed3390a63030a78a6da913cf8436878a6e26e39f55dc1ec8e38dc7985e7835"
)

SAFETY_EVENTS = (
    "post_commit_stall",
    "commit_indeterminate",
    "location_mask_empty",
)


def iter_events(paths: Sequence[str]) -> Iterable[Tuple[Path, int, Dict[str, Any]]]:
    for path_str in paths:
        path = Path(path_str)
        with path.open("r", encoding="utf-8") as reader:
            for line_number, line in enumerate(reader, 1):
                line = line.strip()
                if not line:
                    continue
                try:
                    event = json.loads(line)
                except json.JSONDecodeError as exc:
                    raise ValueError(
                        "%s:%d invalid JSON: %s" % (path, line_number, exc)
                    ) from exc
                if isinstance(event, dict):
                    yield path, line_number, event


def load_pack(path: Optional[str]) -> Optional[Dict[str, Any]]:
    if not path:
        return None
    pack_path = Path(path)
    with pack_path.open("r", encoding="utf-8") as reader:
        # Packs are JSON-with-comments compatible; strip // and /* */ is not needed
        # for checked-in chapter packs (strict JSON). Fall back to MiniJSON-style
        # only if raw json fails.
        text = reader.read()
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        # Minimal comment strip for JSON-with-comments packs.
        cleaned_lines = []
        for line in text.splitlines():
            stripped = line.strip()
            if stripped.startswith("//"):
                continue
            cleaned_lines.append(line)
        return json.loads("\n".join(cleaned_lines))


def pack_rule_ids(pack: Dict[str, Any]) -> Dict[str, Any]:
    """Extract authored rule ids and required_for_slice set from a chapter pack."""
    priority = pack.get("priority") or []
    fallback = pack.get("fallback_scoring") or []
    never = pack.get("never") or []
    policy = pack.get("policy") or {}
    all_ids: List[str] = []
    required: List[str] = []
    for rule in list(priority) + list(fallback):
        if not isinstance(rule, dict):
            continue
        rid = rule.get("id")
        if not rid:
            continue
        all_ids.append(str(rid))
        if rule.get("required_for_slice"):
            required.append(str(rid))
    never_ids = [str(r.get("id")) for r in never if isinstance(r, dict) and r.get("id")]
    return {
        "all_rule_ids": all_ids,
        "required_for_slice": required,
        "never_rule_ids": never_ids,
        "chapter_id": pack.get("chapter_id"),
        "deck_hash": pack.get("deck_hash"),
        "phase_exit_policy": policy.get("phase_exit_policy")
        or pack.get("phase_exit_policy")
        or "battle_then_end",
        "position_safety_exceptions": policy.get("position_safety_exceptions")
        or pack.get("position_safety_exceptions")
        or [],
        "opening_set_safety_enabled": bool(
            policy.get("opening_set_safety_enabled")
            or pack.get("opening_set_safety_enabled")
        ),
        "opening_set_safety_card_ids": policy.get(
            "opening_set_safety_card_ids"
        )
        or pack.get("opening_set_safety_card_ids")
        or [],
    }


def empty_duel(pack_event: Dict[str, Any], line: int) -> Dict[str, Any]:
    return {
        "start_line": line,
        "end_line": line,
        "pack_ts": pack_event.get("ts"),
        "chapter_id": pack_event.get("chapter_id"),
        "mode": pack_event.get("mode"),
        "log_only": pack_event.get("log_only"),
        "deck_hash": pack_event.get("deck_hash"),
        "allow_scripted_commits": pack_event.get("allow_scripted_commits"),
        # Ownership / generation from pack_loaded (production emits these).
        "my_id": pack_event.get("my_id"),
        "owned_seat": pack_event.get("owned_seat"),
        "duel_generation": pack_event.get("duel_generation"),
        "event_counts": Counter(),
        "decision_rule_ids": Counter(),
        "commit_rule_ids": Counter(),
        "decision_action_identities": Counter(),
        "commit_action_identities": Counter(),
        "decision_routes": Counter(),
        "shadow_decisions": 0,
        "live_decisions": 0,
        "commits": 0,
        "lease_begin_reasons": Counter(),
        "restore_reasons": Counter(),
        "safety": Counter(),
        "duel_end_summary": None,
        "seat_owned_confirmed": None,
        "g1_g2_hits": Counter(),
        "mechanical_hits": Counter(),
        "human_seat_rule_commits": 0,
        # Exact S10 invariant: every live RuleCommit must act as owned_seat.
        "non_owned_rule_commits": 0,
        # Live RuleCommit rows lacking resolvable ownership context (fail closed on gate).
        "ownership_context_missing_rule_commits": 0,
        # Invalid/collapsed seats or row ownership contradicting the segment.
        "ownership_context_invalid_rule_commits": 0,
        "recapture_candidates": 0,
        "recapture_attempts": 0,
        "stable_main_boundary_attempts": 0,
        "stable_main_direct_commit_attempts": 0,
        "stable_main_direct_commits_applied": 0,
        "stable_main_direct_ownership_violations": 0,
        "legacy_cpu_thinking_recapture_attempts": 0,
        "recapture_confirmed": 0,
        "recapture_abandoned": 0,
        "recapture_transition_failures": 0,
        "recapture_timeouts": 0,
        "unsafe_lineage_recapture_attempts": 0,
        "invalid_recapture_confirmations": 0,
        "dominated_position_filtered": 0,
        "dominated_position_commits": 0,
        "native_stance_changes_recapturable": 0,
        "recapture_attempts_by_turn": Counter(),
        "action_chain_lineage": [],
        # Full production-shaped decision rows retained for offline replay. These
        # rows are the controlled CampaignCpu seat's own hand/public context; no
        # closed PvP opponent hand is copied here.
        "decision_rows": [],
        "deterministic_pairs": [],  # (identity, rule_id) multi-hit evidence for S9
    }


def finalize_counters(duel: Dict[str, Any]) -> Dict[str, Any]:
    out = dict(duel)
    for key in (
        "event_counts",
        "decision_rule_ids",
        "commit_rule_ids",
        "decision_action_identities",
        "commit_action_identities",
        "decision_routes",
        "lease_begin_reasons",
        "restore_reasons",
        "safety",
        "g1_g2_hits",
        "mechanical_hits",
        "recapture_attempts_by_turn",
    ):
        out[key] = dict(out[key])
    return out


def analyze_events(
    events: Iterable[Tuple[Path, int, Dict[str, Any]]],
    *,
    last_n_duels: Optional[int] = None,
    chapter_id: Optional[int] = None,
    mode: Optional[str] = None,
) -> Dict[str, Any]:
    duels: List[Dict[str, Any]] = []
    current: Optional[Dict[str, Any]] = None
    orphan_events = Counter()
    paths_seen = set()

    for path, line, event in events:
        paths_seen.add(str(path))
        name = event.get("event")
        if name == "pack_loaded":
            if current is not None:
                current["end_line"] = line - 1
                duels.append(current)
            current = empty_duel(event, line)
            current["event_counts"][name] += 1
            continue

        if current is None:
            if name:
                orphan_events[name] += 1
            continue

        # Generation-aware segmentation: a mid-stream generation change without pack_loaded
        # is rare, but when present start a synthetic segment so ownership context stays coherent.
        event_gen = event.get("duel_generation")
        if (
            event_gen is not None
            and current.get("duel_generation") is not None
            and event_gen != current.get("duel_generation")
            and name
            not in (
                "pack_loaded",  # handled above
            )
        ):
            # Close prior segment; open a generation-only segment carrying prior ownership if any.
            current["end_line"] = line - 1
            duels.append(current)
            synthetic = empty_duel(
                {
                    "chapter_id": current.get("chapter_id"),
                    "mode": current.get("mode"),
                    "log_only": current.get("log_only"),
                    "deck_hash": current.get("deck_hash"),
                    "allow_scripted_commits": current.get("allow_scripted_commits"),
                    "my_id": current.get("my_id"),
                    "owned_seat": current.get("owned_seat"),
                    "duel_generation": event_gen,
                    "ts": event.get("ts"),
                },
                line,
            )
            synthetic["synthetic_generation_segment"] = True
            current = synthetic

        current["end_line"] = line
        if name:
            current["event_counts"][name] += 1

        if name == "seat_owned":
            current["seat_owned_confirmed"] = bool(event.get("confirmed"))
            # Prefer seat_owned row fields when pack_loaded lacked them (legacy audits).
            if event.get("my_id") is not None and current.get("my_id") is None:
                current["my_id"] = event.get("my_id")
            if event.get("owned_seat") is not None and current.get("owned_seat") is None:
                current["owned_seat"] = event.get("owned_seat")
            if event.get("duel_generation") is not None and current.get("duel_generation") is None:
                current["duel_generation"] = event.get("duel_generation")
        elif name == "campaign_cpu_decision":
            decision_row = dict(event)
            decision_row["_audit_path"] = str(path)
            decision_row["_audit_line"] = line
            current["decision_rows"].append(decision_row)
            rule_id = event.get("rule_id") or "unknown"
            identity = event.get("action_identity") or ""
            route = event.get("route") or ""
            current["decision_rule_ids"][rule_id] += 1
            if identity:
                current["decision_action_identities"][identity] += 1
            if route:
                current["decision_routes"][route] += 1
            if event.get("shadow_only"):
                current["shadow_decisions"] += 1
            else:
                current["live_decisions"] += 1
            if rule_id in (
                "g1-opening-special-or-action",
                "g2-next-preferred-without-top",
            ):
                current["g1_g2_hits"][rule_id] += 1
            filtered_identities = (
                event.get("tactical_filtered_action_identities") or []
            )
            current["dominated_position_filtered"] += len(filtered_identities)
            # Exact S10 ownership applies to live RuleCommit only. MechanicalAuto may
            # sample acting=MyId under dual-Human and is intentionally excluded.
            #
            # Ownership resolution (production schema first, then pack segment):
            # 1) row my_id / owned_seat when present
            # 2) else enclosing pack_loaded (or seat_owned) segment values
            # Fail closed: live RuleCommit with no resolvable my_id+owned_seat cannot
            # prove exact owned-seat safety — counted as ownership_context_missing.
            acting = event.get("acting_player")
            row_owned = event.get("owned_seat")
            segment_owned = current.get("owned_seat")
            owned = row_owned if row_owned is not None else segment_owned
            row_my_id = event.get("my_id")
            segment_my_id = current.get("my_id")
            my_id = row_my_id if row_my_id is not None else segment_my_id
            if (
                route == "RuleCommit"
                and not event.get("shadow_only")
            ):
                if identity and identity in filtered_identities:
                    current["dominated_position_commits"] += 1
                if my_id is None or owned is None or acting is None:
                    current["ownership_context_missing_rule_commits"] += 1
                else:
                    valid_seat_context = (
                        isinstance(my_id, int)
                        and not isinstance(my_id, bool)
                        and isinstance(owned, int)
                        and not isinstance(owned, bool)
                        and isinstance(acting, int)
                        and not isinstance(acting, bool)
                        and my_id in (0, 1)
                        and owned in (0, 1)
                        and acting in (0, 1)
                        and my_id != owned
                    )
                    row_contradicts_segment = (
                        row_my_id is not None
                        and segment_my_id is not None
                        and row_my_id != segment_my_id
                    ) or (
                        row_owned is not None
                        and segment_owned is not None
                        and row_owned != segment_owned
                    )
                    if not valid_seat_context or row_contradicts_segment:
                        current["ownership_context_invalid_rule_commits"] += 1
                    if acting != owned:
                        current["non_owned_rule_commits"] += 1
                    if acting == my_id and acting != owned:
                        current["human_seat_rule_commits"] += 1
        elif name == "commit_applied":
            current["commits"] += 1
            rule_id = event.get("rule_id") or "unknown"
            action = event.get("action") or ""
            current["commit_rule_ids"][rule_id] += 1
            if action:
                current["commit_action_identities"][action] += 1
            # Count mechanical once per applied commit (decision+commit pairs
            # would double-count if both sides incremented).
            if isinstance(rule_id, str) and rule_id.startswith("mechanical_"):
                current["mechanical_hits"][rule_id] += 1
            # G1/G2: prefer decision_rule_ids path. If a commit-only row appears
            # without a decision (should be rare), still record the hit.
            if rule_id in (
                "g1-opening-special-or-action",
                "g2-next-preferred-without-top",
            ) and current["g1_g2_hits"][rule_id] == 0:
                current["g1_g2_hits"][rule_id] += 1
        elif name == "temporary_cpu_begin":
            current["lease_begin_reasons"][event.get("reason") or "unknown"] += 1
        elif name == "temporary_cpu_restore":
            current["restore_reasons"][event.get("reason") or "unknown"] += 1
        elif name == "owned_main_boundary_probe":
            if event.get("candidate"):
                current["recapture_candidates"] += 1
        elif name == "same_main_recapture_attempt":
            current["recapture_attempts"] += 1
            boundary = event.get("boundary")
            if boundary == "pre_sysact_stable_owned_main":
                current["stable_main_boundary_attempts"] += 1
            else:
                current["legacy_cpu_thinking_recapture_attempts"] += 1
            turn = event.get("turn")
            current["recapture_attempts_by_turn"][str(turn)] += 1
            classification = event.get("lease_origin_classification")
            if classification != "ScriptedResponseContinuation":
                current["unsafe_lineage_recapture_attempts"] += 1
            current["action_chain_lineage"].append(
                {
                    "kind": "attempt",
                    "origin_view_seq": event.get("origin_view_seq"),
                    "view_seq": event.get("view_seq"),
                    "turn": turn,
                    "classification": classification,
                    "boundary": boundary
                    or "legacy_cpu_thinking_callback",
                }
            )
        elif name == "same_main_direct_commit_attempt":
            current["recapture_attempts"] += 1
            current["stable_main_boundary_attempts"] += 1
            current["stable_main_direct_commit_attempts"] += 1
            if (
                event.get("ownership_transition_attempted") is not False
                or event.get("owned_is_human_readback") != 0
                or event.get("my_is_human_readback") != 1
            ):
                current["stable_main_direct_ownership_violations"] += 1
            turn = event.get("turn")
            current["recapture_attempts_by_turn"][str(turn)] += 1
            classification = event.get("lease_origin_classification")
            if classification != "ScriptedResponseContinuation":
                current["unsafe_lineage_recapture_attempts"] += 1
            current["action_chain_lineage"].append(
                {
                    "kind": "direct_commit_attempt",
                    "origin_view_seq": event.get("origin_view_seq"),
                    "view_seq": event.get("view_seq"),
                    "turn": turn,
                    "classification": classification,
                    "boundary": event.get("boundary")
                    or "pre_sysact_stable_owned_main_direct_commit",
                    "commit_outcome": event.get("commit_outcome"),
                    "ownership_transition_attempted": event.get(
                        "ownership_transition_attempted"
                    ),
                }
            )
        elif name == "same_main_direct_commit_applied":
            current["stable_main_direct_commits_applied"] += 1
            current["action_chain_lineage"].append(
                {
                    "kind": "direct_commit_applied",
                    "view_seq": event.get("view_seq"),
                    "turn": event.get("turn"),
                    "action": event.get("action"),
                    "rule_id": event.get("rule_id"),
                    "owned_is_human_readback": event.get(
                        "owned_is_human_readback"
                    ),
                }
            )
        elif name == "same_main_recapture_confirmed":
            current["recapture_confirmed"] += 1
            acting = event.get("acting_player")
            owned = event.get("owned_seat", current.get("owned_seat"))
            generation = event.get("duel_generation")
            expected_generation = current.get("duel_generation")
            if (
                acting is None
                or owned is None
                or acting != owned
                or (
                    generation is not None
                    and expected_generation is not None
                    and generation != expected_generation
                )
            ):
                current["invalid_recapture_confirmations"] += 1
            current["action_chain_lineage"].append(
                {
                    "kind": "confirmed",
                    "origin_view_seq": event.get("origin_view_seq"),
                    "view_seq": event.get("view_seq"),
                    "turn": event.get("turn"),
                }
            )
        elif name == "same_main_recapture_abandoned":
            current["recapture_abandoned"] += 1
            reason = event.get("reason") or "unknown"
            if "timeout" in reason:
                current["recapture_timeouts"] += 1
            current["action_chain_lineage"].append(
                {
                    "kind": "abandoned",
                    "origin_view_seq": event.get("origin_view_seq"),
                    "view_seq": event.get("view_seq"),
                    "reason": reason,
                }
            )
        elif name == "player_type_transition_failed":
            path = str(event.get("path") or "")
            if path.startswith("same_turn_main_recapture") or path.startswith(
                "stable_main_pre_sysact_recapture"
            ):
                current["recapture_transition_failures"] += 1
        elif name == "owned_monster_stance_changed":
            if (
                event.get("lease_origin_classification")
                == "ScriptedResponseContinuation"
            ):
                current["native_stance_changes_recapturable"] += 1
        elif name in SAFETY_EVENTS:
            current["safety"][name] += 1
        elif name == "duel_end_summary":
            current["duel_end_summary"] = {
                "decisions": event.get("decisions"),
                "state": event.get("state"),
                "scripting_disabled": event.get("scripting_disabled"),
                "scripting_disabled_reason": event.get("scripting_disabled_reason"),
                "ts": event.get("ts"),
            }

    if current is not None:
        duels.append(current)

    # Filter
    filtered = []
    for duel in duels:
        if chapter_id is not None and duel.get("chapter_id") != chapter_id:
            continue
        if mode is not None and duel.get("mode") != mode:
            continue
        filtered.append(duel)
    if last_n_duels is not None and last_n_duels >= 0:
        filtered = filtered[-last_n_duels:]

    # S9-ish: within a duel, same action_identity -> same rule_id on decisions
    for duel in filtered:
        for decision_row in duel.get("decision_rows") or []:
            # Legacy decision rows may omit ownership fields. Replay must use the
            # same enclosing pack/segment context that the S10 analyzer uses, while
            # retaining any explicit row value for contradiction checks.
            decision_row["_segment_my_id"] = duel.get("my_id")
            decision_row["_segment_owned_seat"] = duel.get("owned_seat")
            decision_row["_segment_duel_generation"] = duel.get("duel_generation")
        by_identity: Dict[str, set] = defaultdict(set)
        # Reconstruct from counters is insufficient; recompute from identities
        # only when a single identity maps to multiple rules via dual counters.
        # Full pairwise needs per-event list — store identity->rules from second pass
        # is expensive; instead note multi-count identities that also multi-count rules.
        # Better: keep pairs during analysis — patch by re-scanning is avoided:
        # We already lost per-event mapping. Keep deterministic_pairs empty unless
        # we re-scan. For analyzer quality, re-scan only filtered line ranges is
        # overkill; use decision_action_identities counts for "repeated identity".
        duel["repeated_action_identities"] = {
            k: v for k, v in duel["decision_action_identities"].items() if v > 1
        }

    finalized = [finalize_counters(d) for d in filtered]
    aggregate = aggregate_duels(finalized)
    return {
        "paths": sorted(paths_seen),
        "duel_count": len(finalized),
        "duels": finalized,
        "aggregate": aggregate,
        "orphan_events_before_first_pack": dict(orphan_events),
    }


def aggregate_duels(duels: List[Dict[str, Any]]) -> Dict[str, Any]:
    event_counts = Counter()
    decision_rules = Counter()
    commit_rules = Counter()
    safety = Counter()
    lease_begins = Counter()
    restores = Counter()
    mechanical = Counter()
    g1_g2 = Counter()
    modes = Counter()
    chapters = Counter()
    live_decisions = 0
    shadow_decisions = 0
    commits = 0
    human_seat = 0
    non_owned = 0
    ownership_missing = 0
    ownership_invalid = 0
    recapture_candidates = 0
    recapture_attempts = 0
    stable_main_boundary_attempts = 0
    stable_main_direct_commit_attempts = 0
    stable_main_direct_commits_applied = 0
    stable_main_direct_ownership_violations = 0
    legacy_cpu_thinking_recapture_attempts = 0
    recapture_confirmed = 0
    recapture_abandoned = 0
    recapture_transition_failures = 0
    recapture_timeouts = 0
    unsafe_recapture = 0
    invalid_confirmations = 0
    dominated_filtered = 0
    dominated_commits = 0
    native_stance_recapturable = 0
    max_recaptures_per_turn = 0
    stalls = 0
    indeterminate = 0
    completed = 0
    scripting_disabled = 0
    deck_hashes = Counter()

    for d in duels:
        event_counts.update(d.get("event_counts") or {})
        decision_rules.update(d.get("decision_rule_ids") or {})
        commit_rules.update(d.get("commit_rule_ids") or {})
        safety.update(d.get("safety") or {})
        lease_begins.update(d.get("lease_begin_reasons") or {})
        restores.update(d.get("restore_reasons") or {})
        mechanical.update(d.get("mechanical_hits") or {})
        g1_g2.update(d.get("g1_g2_hits") or {})
        modes[str(d.get("mode"))] += 1
        chapters[str(d.get("chapter_id"))] += 1
        live_decisions += int(d.get("live_decisions") or 0)
        shadow_decisions += int(d.get("shadow_decisions") or 0)
        commits += int(d.get("commits") or 0)
        human_seat += int(d.get("human_seat_rule_commits") or 0)
        non_owned += int(d.get("non_owned_rule_commits") or 0)
        ownership_missing += int(d.get("ownership_context_missing_rule_commits") or 0)
        ownership_invalid += int(d.get("ownership_context_invalid_rule_commits") or 0)
        recapture_candidates += int(d.get("recapture_candidates") or 0)
        recapture_attempts += int(d.get("recapture_attempts") or 0)
        stable_main_boundary_attempts += int(
            d.get("stable_main_boundary_attempts") or 0
        )
        stable_main_direct_commit_attempts += int(
            d.get("stable_main_direct_commit_attempts") or 0
        )
        stable_main_direct_commits_applied += int(
            d.get("stable_main_direct_commits_applied") or 0
        )
        stable_main_direct_ownership_violations += int(
            d.get("stable_main_direct_ownership_violations") or 0
        )
        legacy_cpu_thinking_recapture_attempts += int(
            d.get("legacy_cpu_thinking_recapture_attempts") or 0
        )
        recapture_confirmed += int(d.get("recapture_confirmed") or 0)
        recapture_abandoned += int(d.get("recapture_abandoned") or 0)
        recapture_transition_failures += int(
            d.get("recapture_transition_failures") or 0
        )
        recapture_timeouts += int(d.get("recapture_timeouts") or 0)
        unsafe_recapture += int(d.get("unsafe_lineage_recapture_attempts") or 0)
        invalid_confirmations += int(
            d.get("invalid_recapture_confirmations") or 0
        )
        dominated_filtered += int(d.get("dominated_position_filtered") or 0)
        dominated_commits += int(d.get("dominated_position_commits") or 0)
        native_stance_recapturable += int(
            d.get("native_stance_changes_recapturable") or 0
        )
        per_turn = d.get("recapture_attempts_by_turn") or {}
        if per_turn:
            max_recaptures_per_turn = max(
                max_recaptures_per_turn,
                max(int(value) for value in per_turn.values()),
            )
        stalls += int((d.get("safety") or {}).get("post_commit_stall") or 0)
        indeterminate += int((d.get("safety") or {}).get("commit_indeterminate") or 0)
        if d.get("duel_end_summary") is not None:
            completed += 1
            if d["duel_end_summary"].get("scripting_disabled"):
                scripting_disabled += 1
        if d.get("deck_hash"):
            deck_hashes[str(d.get("deck_hash"))] += 1

    return {
        "event_counts": dict(event_counts),
        "decision_rule_ids": dict(decision_rules),
        "commit_rule_ids": dict(commit_rules),
        "safety": dict(safety),
        "lease_begin_reasons": dict(lease_begins),
        "restore_reasons": dict(restores),
        "mechanical_hits": dict(mechanical),
        "g1_g2_hits": dict(g1_g2),
        "modes": dict(modes),
        "chapters": dict(chapters),
        "live_decisions": live_decisions,
        "shadow_decisions": shadow_decisions,
        "commits": commits,
        "human_seat_rule_commits": human_seat,
        "non_owned_rule_commits": non_owned,
        "ownership_context_missing_rule_commits": ownership_missing,
        "ownership_context_invalid_rule_commits": ownership_invalid,
        "recapture_candidates": recapture_candidates,
        "recapture_attempts": recapture_attempts,
        "stable_main_boundary_attempts": stable_main_boundary_attempts,
        "stable_main_direct_commit_attempts":
            stable_main_direct_commit_attempts,
        "stable_main_direct_commits_applied":
            stable_main_direct_commits_applied,
        "stable_main_direct_ownership_violations":
            stable_main_direct_ownership_violations,
        "legacy_cpu_thinking_recapture_attempts":
            legacy_cpu_thinking_recapture_attempts,
        "recapture_confirmed": recapture_confirmed,
        "recapture_abandoned": recapture_abandoned,
        "recapture_transition_failures": recapture_transition_failures,
        "recapture_timeouts": recapture_timeouts,
        "unsafe_lineage_recapture_attempts": unsafe_recapture,
        "invalid_recapture_confirmations": invalid_confirmations,
        "max_recaptures_per_turn": max_recaptures_per_turn,
        "dominated_position_filtered": dominated_filtered,
        "dominated_position_commits": dominated_commits,
        "native_stance_changes_recapturable": native_stance_recapturable,
        "post_commit_stall": stalls,
        "commit_indeterminate": indeterminate,
        "completed_duels": completed,
        "scripting_disabled_duels": scripting_disabled,
        "deck_hashes": dict(deck_hashes),
    }


def analyze_paths(
    paths: Sequence[str],
    *,
    last_n_duels: Optional[int] = None,
    chapter_id: Optional[int] = None,
    mode: Optional[str] = None,
    pack_path: Optional[str] = None,
) -> Dict[str, Any]:
    summary = analyze_events(
        iter_events(paths),
        last_n_duels=last_n_duels,
        chapter_id=chapter_id,
        mode=mode,
    )
    pack = load_pack(pack_path) if pack_path else None
    if pack is not None:
        meta = pack_rule_ids(pack)
        fired = set(summary["aggregate"].get("decision_rule_ids") or {})
        fired |= set(summary["aggregate"].get("commit_rule_ids") or {})
        required = meta["required_for_slice"]
        required_hits = {rid: (rid in fired) for rid in required}
        never_fired = [rid for rid in meta["all_rule_ids"] if rid not in fired]
        summary["pack"] = {
            "path": pack_path,
            "chapter_id": meta["chapter_id"],
            "deck_hash": meta["deck_hash"],
            "required_for_slice": required,
            "required_hits": required_hits,
            "required_missing": [rid for rid, hit in required_hits.items() if not hit],
            "never_fired_rules": never_fired,
            "all_rule_ids": meta["all_rule_ids"],
            "phase_exit_policy": meta["phase_exit_policy"],
            "position_safety_exceptions": meta["position_safety_exceptions"],
            "opening_set_safety_enabled": meta[
                "opening_set_safety_enabled"
            ],
            "opening_set_safety_card_ids": meta[
                "opening_set_safety_card_ids"
            ],
        }
    return summary


def _replay_int(value: Any) -> Optional[int]:
    if isinstance(value, bool) or value is None:
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, str):
        try:
            return int(value)
        except ValueError:
            return None
    return None


def _replay_action_identity(action: Dict[str, Any]) -> str:
    if not isinstance(action, dict):
        return ""
    identity = action.get("identity") or action.get("canonical_identity")
    return str(identity) if identity is not None else ""


def _replay_action_summary(action: Optional[Dict[str, Any]]) -> Optional[Dict[str, Any]]:
    if not isinstance(action, dict):
        return None
    return {
        "identity": _replay_action_identity(action),
        "kind": action.get("kind"),
        "command": action.get("command"),
        "phase": action.get("phase"),
        "card_id": action.get("card_id"),
        "position": action.get("position"),
        "index": action.get("index"),
        "is_mechanical": bool(action.get("is_mechanical", False)),
        "target_scope": action.get("target_scope") or "",
    }


def _replay_legal_actions(row: Dict[str, Any]) -> List[Dict[str, Any]]:
    actions = row.get("legal_actions")
    if not isinstance(actions, list):
        return []
    return [action for action in actions if isinstance(action, dict)]


def _replay_fingerprint(actions: Sequence[Dict[str, Any]]) -> str:
    identities = sorted(
        identity
        for identity in (_replay_action_identity(action) for action in actions)
        if identity
    )
    return ";".join(identities) if identities else "empty"


def _replay_known_stat(monster: Dict[str, Any], value_key: str, flag_key: str) -> bool:
    if monster.get(flag_key) is False:
        return False
    value = _replay_int(monster.get(value_key))
    return value is not None and value >= 0


def _replay_owned_seat(row: Dict[str, Any]) -> Optional[int]:
    value = row.get("owned_seat")
    if value is None:
        value = row.get("_segment_owned_seat")
    return _replay_int(value)


def _replay_find_own_monster(
    row: Dict[str, Any], action: Dict[str, Any]
) -> Optional[Dict[str, Any]]:
    owned_seat = _replay_owned_seat(row)
    if owned_seat is None:
        return None
    action_card = _replay_int(action.get("card_id"))
    action_position = _replay_int(action.get("position"))
    action_index = _replay_int(action.get("index"))
    for monster in row.get("tactical_monsters") or []:
        if not isinstance(monster, dict):
            continue
        if _replay_int(monster.get("player")) != owned_seat:
            continue
        if _replay_int(monster.get("position")) != action_position:
            continue
        if _replay_int(monster.get("index")) != action_index:
            continue
        monster_card = _replay_int(monster.get("card_id"))
        if action_card is not None and action_card > 0 and monster_card != action_card:
            continue
        return monster
    return None


def _replay_phase_action(
    actions: Sequence[Dict[str, Any]], phase: str
) -> Optional[Dict[str, Any]]:
    for action in actions:
        if not isinstance(action, dict):
            continue
        if str(action.get("phase") or "").lower() == phase.lower():
            return action
        identity = _replay_action_identity(action)
        if identity.startswith("MovePhase|%s|" % phase):
            return action
    return None


def _replay_position_safety(
    row: Dict[str, Any],
    actions: Sequence[Dict[str, Any]],
    exception_card_ids: Sequence[int],
) -> Dict[str, Any]:
    """Re-evaluate only the pure dominated-position rule from captured state.

    The replay deliberately refuses to infer hidden stance or stats. A legal
    TurnDef/TurnAtk action does establish the current stance of that referenced
    monster because the engine only offers the opposite-position transition.
    This remains a policy admission report, not a second duel engine or a
    replacement for live DLL behavior.
    """
    owned_seat = _replay_owned_seat(row)
    monsters = [m for m in (row.get("tactical_monsters") or []) if isinstance(m, dict)]
    opposing_atk = []
    if owned_seat is not None:
        for monster in monsters:
            if _replay_int(monster.get("player")) == owned_seat:
                continue
            if monster.get("face_known") is not True or monster.get("face_up") is not True:
                continue
            if _replay_known_stat(monster, "atk", "has_atk"):
                opposing_atk.append(_replay_int(monster.get("atk")))

    if not opposing_atk:
        return {
            "evaluated": False,
            "reason": "unknown_tactical_state",
            "max_opposing_face_up_atk": None,
            "filtered_action_identities": [],
            "remaining_action_identities": [
                _replay_action_identity(action) for action in actions
            ],
            "known_face_up_attacker": False,
            "monsters": monsters,
        }

    maximum = max(opposing_atk)
    known_attacker = False
    filtered: List[str] = []
    filter_reason = "not_dominated"
    exception_ids = set(exception_card_ids)
    for monster in monsters:
        if (
            owned_seat is not None
            and _replay_int(monster.get("player")) == owned_seat
            and monster.get("face_known") is True
            and monster.get("face_up") is True
            and monster.get("turn_known") is True
            and monster.get("is_attack") is True
            and _replay_known_stat(monster, "atk", "has_atk")
            and _replay_int(monster.get("atk")) > 0
        ):
            known_attacker = True

    for action in actions:
        command = str(action.get("command") or "")
        if command not in ("TurnDef", "TurnAtk"):
            continue
        card_id = _replay_int(action.get("card_id"))
        if card_id in exception_ids:
            continue
        own = _replay_find_own_monster(row, action)
        if own is None:
            continue
        if (
            own.get("face_known") is not True
            or own.get("face_up") is not True
            or not _replay_known_stat(own, "atk", "has_atk")
            or not _replay_known_stat(own, "def", "has_def")
        ):
            continue
        atk = _replay_int(own.get("atk"))
        defense = _replay_int(own.get("def"))
        if command == "TurnDef" and atk is not None and atk > 0:
            known_attacker = True
        dominated = (
            command == "TurnDef"
            and atk is not None
            and defense is not None
            and atk >= maximum
            and defense < maximum
        ) or (
            command == "TurnAtk"
            and atk is not None
            and defense is not None
            and defense >= maximum
            and atk < maximum
        )
        if dominated:
            filtered.append(_replay_action_identity(action))
            filter_reason = (
                "dominated_turn_defense"
                if command == "TurnDef"
                else "dominated_turn_attack"
            )

    filtered_set = set(filtered)
    return {
        "evaluated": True,
        "reason": filter_reason,
        "max_opposing_face_up_atk": maximum,
        "filtered_action_identities": filtered,
        "remaining_action_identities": [
            _replay_action_identity(action)
            for action in actions
            if _replay_action_identity(action) not in filtered_set
        ],
        "known_face_up_attacker": known_attacker,
        "monsters": monsters,
    }


def _replay_tactical_fallback(
    actions: Sequence[Dict[str, Any]],
    tactical: Dict[str, Any],
    phase_exit_policy: str,
) -> Dict[str, Any]:
    fallback = None
    fallback_reason = None
    if phase_exit_policy.lower() == "battle_then_end" and tactical.get(
        "known_face_up_attacker"
    ):
        fallback = _replay_phase_action(actions, "Battle")
        fallback_reason = "phase_exit_battle"
    if fallback is None:
        fallback = _replay_phase_action(actions, "End")
        fallback_reason = "phase_exit_end"
    if fallback is not None:
        return {
            "route": "RuleCommit",
            "action_identity": _replay_action_identity(fallback),
            "reason": tactical.get("reason") or "dominated_position",
            "fallback_reason": fallback_reason,
            "action": _replay_action_summary(fallback),
        }
    return {
        "route": "FallbackNative",
        "action_identity": None,
        "reason": "dominated_position_no_legal_fallback",
        "action": None,
    }


def _replay_selected_level(row: Dict[str, Any]) -> Optional[int]:
    for key in ("selected_card_level", "card_level", "level"):
        value = _replay_int(row.get(key))
        if value is not None:
            return value
    chosen = row.get("chosen")
    if isinstance(chosen, dict):
        for key in ("selected_card_level", "card_level", "level"):
            value = _replay_int(chosen.get(key))
            if value is not None:
                return value
    return None


def _replay_candidate(
    row: Dict[str, Any],
    actions: Sequence[Dict[str, Any]],
    tactical: Dict[str, Any],
    phase_exit_policy: str,
) -> Dict[str, Any]:
    recorded_route = str(row.get("route") or "Unknown")
    recorded_identity = row.get("action_identity")
    recorded_identity = str(recorded_identity) if recorded_identity is not None else None
    legal_by_identity = {
        _replay_action_identity(action): action
        for action in actions
        if _replay_action_identity(action)
    }
    recorded_action = legal_by_identity.get(recorded_identity or "")
    filtered_identities = set(tactical.get("filtered_action_identities") or [])

    if not actions:
        return {
            "route": "Unscorable",
            "action_identity": None,
            "reason": "missing_legal_action_context",
            "action": None,
        }

    if recorded_route == "FallbackNative" and filtered_identities:
        return _replay_tactical_fallback(actions, tactical, phase_exit_policy)

    if not recorded_identity or recorded_action is None:
        return {
            "route": "Unscorable",
            "action_identity": None,
            "reason": "missing_legal_action_context",
            "action": None,
        }

    if recorded_identity in filtered_identities:
        return _replay_tactical_fallback(actions, tactical, phase_exit_policy)

    if recorded_route != "RuleCommit":
        return {
            "route": recorded_route,
            "action_identity": recorded_identity,
            "reason": "recorded_route",
            "action": _replay_action_summary(recorded_action),
        }

    command = str(row.get("command") or recorded_action.get("command") or "")
    if command in ("Summon", "SetMonst"):
        level = _replay_selected_level(row)
        if level is None:
            return {
                "route": "Unscorable",
                "action_identity": None,
                "reason": "normal_summon_level_unknown",
                "action": None,
            }
        if level < 1 or level > 4:
            return {
                "route": "FallbackNative",
                "action_identity": None,
                "reason": "unsupported_normal_summon_continuation",
                "card_level": level,
                "action": None,
            }
        return {
            "route": "RuleCommit",
            "action_identity": recorded_identity,
            "reason": "safe_normal_summon_continuation",
            "card_level": level,
            "action": _replay_action_summary(recorded_action),
        }

    if command in ("MovePhase", "Attack", "End"):
        return {
            "route": "RuleCommit",
            "action_identity": recorded_identity,
            "reason": "explicit_phase_action",
            "action": _replay_action_summary(recorded_action),
        }

    return {
        "route": "FallbackNative",
        "action_identity": None,
        "reason": "unsupported_scripted_continuation",
        "action": None,
    }


def _replay_opening_set_candidate(
    duel: Dict[str, Any],
    row: Dict[str, Any],
    actions: Sequence[Dict[str, Any]],
) -> Optional[Dict[str, Any]]:
    if not duel.get("_opening_set_safety_enabled"):
        return None
    allowlist = {
        value
        for value in (
            _replay_int(item)
            for item in (duel.get("_opening_set_safety_card_ids") or [])
        )
        if value is not None
    }
    recorded_identity = str(row.get("action_identity") or "")
    legal_by_identity = {
        _replay_action_identity(action): action
        for action in actions
        if _replay_action_identity(action)
    }
    selected = legal_by_identity.get(recorded_identity)
    owned = _replay_int(row.get("owned_seat"))
    if (
        selected is None
        or str(row.get("route") or "") != "RuleCommit"
        or not str(row.get("reason") or "").startswith("fallback")
        or str(selected.get("kind") or "") != "Command"
        or str(selected.get("command") or "") != "Summon"
        or _replay_int(selected.get("card_id")) not in allowlist
        or owned not in (0, 1)
        or _replay_int(selected.get("player")) != owned
        or _replay_int(row.get("acting_player")) != owned
        or _replay_int(row.get("turn_player")) != owned
        or _replay_int(row.get("turn")) != 0
        or row.get("is_main_phase_wait_input") is not True
        or str(row.get("window_class") or "").lower()
        != "waitinput_mainphase"
        or list(row.get("self_field_face_up_card_ids") or [])
        or list(row.get("opp_field_face_up_card_ids") or [])
    ):
        return None

    level = _replay_int(selected.get("basic_level"))
    atk = _replay_int(selected.get("basic_atk"))
    defense = _replay_int(selected.get("basic_def"))
    if (
        level is None
        or level < 1
        or level > 4
        or atk is None
        or defense is None
        or defense <= atk
    ):
        return None

    card_id = _replay_int(selected.get("card_id"))
    player = _replay_int(selected.get("player"))
    position = _replay_int(selected.get("position"))
    index = _replay_int(selected.get("index"))
    for action in actions:
        if str(action.get("kind") or "") == "MovePhase" and str(
            action.get("phase") or ""
        ).lower() == "battle":
            return None
    for action in actions:
        if (
            str(action.get("kind") or "") == "Command"
            and str(action.get("command") or "") == "SetMonst"
            and _replay_int(action.get("card_id")) == card_id
            and _replay_int(action.get("player")) == player
            and _replay_int(action.get("position")) == position
            and _replay_int(action.get("index")) == index
        ):
            return {
                "route": "RuleCommit",
                "action_identity": _replay_action_identity(action),
                "reason": "opening_position_safety",
                "rule_id": "opening_defensive_set",
                "replaced_action_identity": recorded_identity,
                "card_level": level,
                "action": _replay_action_summary(action),
            }
    return None


def _replay_recorded_opening_set_valid(
    duel: Dict[str, Any],
    row: Dict[str, Any],
    actions: Sequence[Dict[str, Any]],
) -> bool:
    named = (
        str(row.get("rule_id") or "") == "opening_defensive_set"
        or str(row.get("reason") or "") == "opening_position_safety"
    )
    if not named:
        return True
    if (
        str(row.get("rule_id") or "") != "opening_defensive_set"
        or str(row.get("reason") or "") != "opening_position_safety"
        or not duel.get("_opening_set_safety_enabled")
    ):
        return False

    allowlist = {
        value
        for value in (
            _replay_int(item)
            for item in (duel.get("_opening_set_safety_card_ids") or [])
        )
        if value is not None
    }
    legal_by_identity = {
        _replay_action_identity(action): action
        for action in actions
        if _replay_action_identity(action)
    }
    selected = legal_by_identity.get(str(row.get("action_identity") or ""))
    replaced = legal_by_identity.get(
        str(row.get("replaced_action_identity") or "")
    )
    if (
        selected is None
        or replaced is None
        or str(selected.get("command") or "") != "SetMonst"
        or str(replaced.get("command") or "") != "Summon"
    ):
        return False

    card_id = _replay_int(selected.get("card_id"))
    level = _replay_int(replaced.get("basic_level"))
    atk = _replay_int(replaced.get("basic_atk"))
    defense = _replay_int(replaced.get("basic_def"))
    owned = _replay_int(row.get("owned_seat"))
    return bool(
        card_id in allowlist
        and card_id == _replay_int(replaced.get("card_id"))
        and _replay_int(selected.get("player"))
        == _replay_int(replaced.get("player"))
        == owned
        and _replay_int(selected.get("position"))
        == _replay_int(replaced.get("position"))
        and _replay_int(selected.get("index"))
        == _replay_int(replaced.get("index"))
        and level is not None
        and 1 <= level <= 4
        and atk is not None
        and defense is not None
        and defense > atk
        and _replay_int(row.get("acting_player")) == owned
        and _replay_int(row.get("turn_player")) == owned
        and _replay_int(row.get("turn")) == 0
        and row.get("is_main_phase_wait_input") is True
        and str(row.get("window_class") or "").lower()
        == "waitinput_mainphase"
        and not list(row.get("self_field_face_up_card_ids") or [])
        and not list(row.get("opp_field_face_up_card_ids") or [])
        and not any(
            str(action.get("kind") or "") == "MovePhase"
            and str(action.get("phase") or "").lower() == "battle"
            for action in actions
        )
    )


def _replay_invariants(
    row: Dict[str, Any],
    actions: Sequence[Dict[str, Any]],
    candidate: Dict[str, Any],
    replayable: bool,
) -> Dict[str, Optional[bool]]:
    if not replayable:
        return {
            "legality": None,
            "ownership": None,
            "generation_context": None,
            "unsafe_continuation": None,
            "native_fallback": None,
            "controlled_context": None,
        }

    legal_ids = {
        _replay_action_identity(action)
        for action in actions
        if _replay_action_identity(action)
    }
    candidate_route = candidate.get("route")
    candidate_identity = candidate.get("action_identity")
    legality = candidate_route != "RuleCommit" or candidate_identity in legal_ids
    native_fallback = candidate_route != "FallbackNative" or candidate_identity is None

    if candidate_route != "RuleCommit":
        unsafe_continuation = True
    else:
        action = next(
            (
                action
                for action in actions
                if _replay_action_identity(action) == candidate_identity
            ),
            None,
        )
        command = str((action or {}).get("command") or "")
        if command in ("MovePhase", "Attack", "End"):
            unsafe_continuation = True
        elif command in ("Summon", "SetMonst"):
            level = candidate.get("card_level")
            unsafe_continuation = isinstance(level, int) and 1 <= level <= 4
        else:
            unsafe_continuation = False

    if not replayable or candidate_route != "RuleCommit":
        ownership = True
        generation_context = True
    else:
        my_id = _replay_int(
            row.get("my_id")
            if row.get("my_id") is not None
            else row.get("_segment_my_id")
        )
        owned = _replay_int(
            row.get("owned_seat")
            if row.get("owned_seat") is not None
            else row.get("_segment_owned_seat")
        )
        acting = _replay_int(row.get("acting_player"))
        ownership = (
            my_id in (0, 1)
            and owned in (0, 1)
            and acting in (0, 1)
            and my_id != owned
            and acting == owned
        )
        row_generation = _replay_int(row.get("duel_generation"))
        segment_generation = _replay_int(row.get("_segment_duel_generation"))
        generation_context = (
            segment_generation is not None
            and row_generation is not None
            and row_generation == segment_generation
        )

    context = row.get("self_hand_card_ids")
    controlled_context = isinstance(context, list)
    return {
        "legality": legality,
        "ownership": ownership,
        "generation_context": generation_context,
        "unsafe_continuation": unsafe_continuation,
        "native_fallback": native_fallback,
        "controlled_context": controlled_context,
    }


def _replay_decision_row(
    duel: Dict[str, Any],
    row: Dict[str, Any],
    *,
    phase_exit_policy: str = "battle_then_end",
    exception_card_ids: Sequence[int] = (),
) -> Dict[str, Any]:
    actions = _replay_legal_actions(row)
    route = str(row.get("route") or "Unknown")
    has_replay_context = bool(actions) and route in ("RuleCommit", "FallbackNative")
    tactical = _replay_position_safety(row, actions, exception_card_ids)
    candidate = _replay_candidate(row, actions, tactical, phase_exit_policy)
    opening_candidate = _replay_opening_set_candidate(
        duel,
        row,
        actions,
    )
    if opening_candidate is not None:
        candidate = opening_candidate
    replayable = has_replay_context and candidate.get("route") != "Unscorable"
    invariants = _replay_invariants(row, actions, candidate, replayable)
    invariants["opening_set_policy"] = (
        _replay_recorded_opening_set_valid(duel, row, actions)
    )

    if not replayable:
        classification = "unscorable"
    elif (
        candidate.get("route") == "RuleCommit"
        and candidate.get("action_identity") != row.get("action_identity")
        and candidate.get("reason")
        in (
            "dominated_turn_defense",
            "dominated_turn_attack",
            "opening_position_safety",
        )
    ):
        classification = "materially_changed"
    elif candidate.get("route") != route or candidate.get("action_identity") != row.get(
        "action_identity"
    ):
        classification = "safety_deferred"
    else:
        classification = "unchanged"

    return {
        "source": {
            "path": row.get("_audit_path"),
            "line": row.get("_audit_line"),
        },
        "view_seq": row.get("view_seq"),
        "turn": row.get("turn"),
        "turn_player": row.get("turn_player"),
        "phase": row.get("phase"),
        "rule_id": row.get("rule_id"),
        "replayable": replayable,
        "classification": classification,
        "recorded": {
            "route": route,
            "action_identity": row.get("action_identity"),
            "command": row.get("command"),
            "card_id": row.get("card_id"),
            "rule_id": row.get("rule_id"),
            "score": row.get("score"),
            "reason": row.get("reason"),
        },
        "candidate": candidate,
        "controlled_hand": list(row.get("self_hand_card_ids") or []),
        "visible_field": {
            "self_face_up_card_ids": list(row.get("self_field_face_up_card_ids") or []),
            "opp_face_up_card_ids": list(row.get("opp_field_face_up_card_ids") or []),
        },
        "legal": {
            "recorded_fingerprint": row.get("legal_fingerprint")
            or _replay_fingerprint(actions),
            "candidate_fingerprint": ";".join(
                sorted(
                    identity
                    for identity in (tactical.get("remaining_action_identities") or [])
                    if identity
                )
            )
            or "empty",
            "actions": [_replay_action_summary(action) for action in actions],
        },
        "tactical": tactical,
        "invariants": invariants,
    }


def build_offline_replay_report(summary: Dict[str, Any]) -> Dict[str, Any]:
    """Replay the deterministic safety policy against captured decision rows.

    This is intentionally read-only. It does not rerun the C# scorer, invent card
    stats, expose a PvP hidden hand, or claim downstream duel.dll behavior. Rows with
    missing legal/tactical context are marked ``unscorable`` rather than guessed.
    """
    agg = summary.get("aggregate") or {}
    duels = summary.get("duels") or []
    pack = summary.get("pack") or {}
    phase_exit_policy = str(pack.get("phase_exit_policy") or "battle_then_end")
    exception_card_ids = [
        value
        for value in (
            _replay_int(item) for item in (pack.get("position_safety_exceptions") or [])
        )
        if value is not None
    ]
    opening_set_safety_enabled = bool(
        pack.get("opening_set_safety_enabled")
    )
    opening_set_safety_card_ids = list(
        pack.get("opening_set_safety_card_ids") or []
    )

    decisions: List[Dict[str, Any]] = []
    classification_counts = Counter()
    invariant_failures = Counter()
    tactical_filter_count = 0
    unsafe_deferral_count = 0
    unknown_level_count = 0
    opening_set_safety_count = 0
    duel_replay_counts: Dict[Tuple[Any, Any], Counter] = defaultdict(Counter)
    for duel in duels:
        for row in duel.get("decision_rows") or []:
            replay_duel = dict(duel)
            replay_duel["_opening_set_safety_enabled"] = (
                opening_set_safety_enabled
            )
            replay_duel["_opening_set_safety_card_ids"] = (
                opening_set_safety_card_ids
            )
            replay = _replay_decision_row(
                replay_duel,
                row,
                phase_exit_policy=phase_exit_policy,
                exception_card_ids=exception_card_ids,
            )
            decisions.append(replay)
            classification_counts[replay["classification"]] += 1
            key = (duel.get("start_line"), duel.get("duel_generation"))
            duel_replay_counts[key][replay["classification"]] += 1
            tactical_filter_count += len(
                replay["tactical"].get("filtered_action_identities") or []
            )
            if replay["candidate"].get("reason") == "unsupported_scripted_continuation":
                unsafe_deferral_count += 1
            if replay["candidate"].get("reason") == "normal_summon_level_unknown":
                unknown_level_count += 1
            if replay["candidate"].get("reason") == "opening_position_safety":
                opening_set_safety_count += 1
            for invariant, passed in replay["invariants"].items():
                if passed is False:
                    invariant_failures[invariant] += 1

    replayable_rows = (
        int(classification_counts.get("unchanged", 0))
        + int(classification_counts.get("materially_changed", 0))
        + int(classification_counts.get("safety_deferred", 0))
    )
    meaningful_delta = int(classification_counts.get("materially_changed", 0)) > 0
    all_passed = not invariant_failures
    admission_reasons = []
    if not decisions:
        admission_reasons.append("no_campaign_cpu_decision_rows")
    if replayable_rows == 0:
        admission_reasons.append("no_replayable_decision_rows")
    if not meaningful_delta:
        admission_reasons.append("no_meaningful_decision_delta")
    if not all_passed:
        admission_reasons.append("replay_invariant_failure")
    admission_reasons.append("different_engine_boundary_not_proven")

    tactical_issues = []
    if int(agg.get("recapture_timeouts") or 0) > 0:
        tactical_issues.append("recapture_timeouts")
    if int(agg.get("unsafe_lineage_recapture_attempts") or 0) > 0:
        tactical_issues.append("unsafe_lineage_recapture_attempts")
    if int(agg.get("dominated_position_commits") or 0) > 0:
        tactical_issues.append("dominated_position_commits")
    if int(agg.get("post_commit_stall") or 0) > 0:
        tactical_issues.append("post_commit_stall")
    if int(agg.get("commit_indeterminate") or 0) > 0:
        tactical_issues.append("commit_indeterminate")

    duel_summaries = []
    for duel in duels:
        key = (duel.get("start_line"), duel.get("duel_generation"))
        counts = duel_replay_counts.get(key) or Counter()
        duel_summaries.append(
            {
                "start_line": duel.get("start_line"),
                "end_line": duel.get("end_line"),
                "chapter_id": duel.get("chapter_id"),
                "mode": duel.get("mode"),
                "live_decisions": duel.get("live_decisions"),
                "commits": duel.get("commits"),
                "replayable_rows": int(counts.get("unchanged", 0))
                + int(counts.get("materially_changed", 0))
                + int(counts.get("safety_deferred", 0)),
                "classification_counts": dict(counts),
                "action_chain_lineage": list(duel.get("action_chain_lineage") or []),
                "recapture_attempts": duel.get("recapture_attempts"),
                "recapture_confirmed": duel.get("recapture_confirmed"),
                "recapture_timeouts": duel.get("recapture_timeouts"),
                "unsafe_lineage_recapture_attempts": duel.get(
                    "unsafe_lineage_recapture_attempts"
                ),
                "dominated_position_commits": duel.get("dominated_position_commits"),
                "safety": duel.get("safety"),
                "duel_end_summary": duel.get("duel_end_summary"),
            }
        )

    return {
        "schema_version": 2,
        "paths": summary.get("paths"),
        "duel_count": summary.get("duel_count"),
        "completed_duels": agg.get("completed_duels"),
        "policy": {
            "name": "campaign_cpu_successive_main_safety_replay",
            "phase_exit_policy": phase_exit_policy,
            "position_safety_exceptions": exception_card_ids,
            "opening_set_safety_enabled": opening_set_safety_enabled,
            "opening_set_safety_card_ids": opening_set_safety_card_ids,
            "unknown_normal_summon_level": "unscorable",
            "unsupported_continuation": "native_fallback",
        },
        "corpus": {
            "decision_rows": len(decisions),
            "replayable_rows": replayable_rows,
            "unscorable_rows": int(classification_counts.get("unscorable", 0)),
        },
        "classification_counts": dict(classification_counts),
        "admission": {
            "meaningful_decision_delta": meaningful_delta,
            "all_replay_invariants_passed": all_passed,
            "offline_replay_gate_passed": bool(
                meaningful_delta and all_passed and replayable_rows
            ),
            # The report cannot prove a new duel.dll transport boundary. Keep this
            # false until the separately reviewed implementation and one written
            # proof-duel target exist; this prevents an offline report from
            # authorizing another unchanged CpuThinking experiment.
            "ready_for_live_proof": False,
            "blocking_reasons": admission_reasons,
            "live_transport_gate": "not_assessed",
        },
        "invariants": {
            "all_passed": all_passed,
            "checked_rows": replayable_rows,
            "failures": dict(invariant_failures),
        },
        "replay": {
            "dominated_position_filtered": tactical_filter_count,
            "unsupported_continuation_native_fallback": unsafe_deferral_count,
            "normal_summon_level_unknown_unscorable": unknown_level_count,
            "opening_set_safety_applied": opening_set_safety_count,
        },
        "tactical_issues": tactical_issues,
        "recapture": {
            "candidates": agg.get("recapture_candidates"),
            "attempts": agg.get("recapture_attempts"),
            "stable_main_boundary_attempts":
                agg.get("stable_main_boundary_attempts"),
            "stable_main_direct_commit_attempts":
                agg.get("stable_main_direct_commit_attempts"),
            "stable_main_direct_commits_applied":
                agg.get("stable_main_direct_commits_applied"),
            "stable_main_direct_ownership_violations":
                agg.get("stable_main_direct_ownership_violations"),
            "legacy_cpu_thinking_recapture_attempts":
                agg.get("legacy_cpu_thinking_recapture_attempts"),
            "confirmed": agg.get("recapture_confirmed"),
            "abandoned": agg.get("recapture_abandoned"),
            "timeouts": agg.get("recapture_timeouts"),
        },
        "safety": {
            "post_commit_stall": agg.get("post_commit_stall"),
            "commit_indeterminate": agg.get("commit_indeterminate"),
            "dominated_position_commits": agg.get("dominated_position_commits"),
            "unsafe_lineage_recapture_attempts": agg.get(
                "unsafe_lineage_recapture_attempts"
            ),
        },
        "decisions": decisions,
        "duels": duel_summaries,
    }


def validate_requirements(
    summary: Dict[str, Any],
    *,
    min_duels: int = 0,
    min_commits: int = 0,
    min_live_decisions: int = 0,
    min_shadow_decisions: int = 0,
    require_zero_stalls: bool = False,
    require_zero_indeterminate: bool = False,
    require_zero_human_seat_commits: bool = False,
    require_zero_location_mask_empty: bool = False,
    require_rules: Optional[Sequence[str]] = None,
    require_deck_hash: Optional[str] = None,
    require_chapter: Optional[int] = None,
    require_mode: Optional[str] = None,
    require_required_for_slice: bool = False,
    require_completed: bool = False,
    require_successive_main_safety: bool = False,
) -> List[str]:
    errors: List[str] = []
    agg = summary.get("aggregate") or {}
    duel_count = int(summary.get("duel_count") or 0)

    if duel_count < min_duels:
        errors.append("duel_count %d below min_duels %d" % (duel_count, min_duels))
    if int(agg.get("commits") or 0) < min_commits:
        errors.append(
            "commits %d below min_commits %d" % (int(agg.get("commits") or 0), min_commits)
        )
    if int(agg.get("live_decisions") or 0) < min_live_decisions:
        errors.append(
            "live_decisions %d below min_live_decisions %d"
            % (int(agg.get("live_decisions") or 0), min_live_decisions)
        )
    if int(agg.get("shadow_decisions") or 0) < min_shadow_decisions:
        errors.append(
            "shadow_decisions %d below min_shadow_decisions %d"
            % (int(agg.get("shadow_decisions") or 0), min_shadow_decisions)
        )
    if require_zero_stalls and int(agg.get("post_commit_stall") or 0) != 0:
        errors.append(
            "post_commit_stall count %d (require zero)"
            % int(agg.get("post_commit_stall") or 0)
        )
    if require_zero_indeterminate and int(agg.get("commit_indeterminate") or 0) != 0:
        errors.append(
            "commit_indeterminate count %d (require zero)"
            % int(agg.get("commit_indeterminate") or 0)
        )
    if require_zero_human_seat_commits:
        missing = int(agg.get("ownership_context_missing_rule_commits") or 0)
        invalid = int(agg.get("ownership_context_invalid_rule_commits") or 0)
        non_owned = int(agg.get("non_owned_rule_commits") or 0)
        if missing != 0:
            # Fail closed: cannot prove S10 ownership without production context.
            errors.append(
                "ownership_context_missing_rule_commits %d "
                "(require-zero-human-seat-commits needs my_id+owned_seat+acting on "
                "live RuleCommit rows or enclosing pack_loaded segment)"
                % missing
            )
        if invalid != 0:
            errors.append(
                "ownership_context_invalid_rule_commits %d "
                "(require valid distinct seats and row/segment agreement)"
                % invalid
            )
        if non_owned != 0:
            errors.append(
                "non_owned_rule_commits %d "
                "(require every live RuleCommit acting_player == owned_seat)"
                % non_owned
            )
        if int(agg.get("human_seat_rule_commits") or 0) != 0:
            errors.append(
                "human_seat_rule_commits %d (require zero)"
                % int(agg.get("human_seat_rule_commits") or 0)
            )
    mask_empty = int((agg.get("safety") or {}).get("location_mask_empty") or 0)
    if require_zero_location_mask_empty and mask_empty != 0:
        errors.append("location_mask_empty count %d (require zero)" % mask_empty)
    if require_successive_main_safety:
        for key in (
            "unsafe_lineage_recapture_attempts",
            "invalid_recapture_confirmations",
            "recapture_transition_failures",
            "recapture_timeouts",
            "legacy_cpu_thinking_recapture_attempts",
            "stable_main_direct_ownership_violations",
            "dominated_position_commits",
        ):
            value = int(agg.get(key) or 0)
            if value != 0:
                errors.append("%s %d (require zero)" % (key, value))

    fired = set(agg.get("decision_rule_ids") or {})
    fired |= set(agg.get("commit_rule_ids") or {})
    for rid in require_rules or []:
        if rid not in fired:
            errors.append("required rule id not observed: %s" % rid)

    if require_deck_hash:
        hashes = agg.get("deck_hashes") or {}
        if require_deck_hash not in hashes:
            errors.append(
                "required deck_hash not observed: %s (seen %s)"
                % (require_deck_hash, sorted(hashes))
            )
        # All selected duels must match pin when required.
        mismatched = [h for h in hashes if h != require_deck_hash]
        if mismatched:
            errors.append("deck_hash mismatch present: %s" % mismatched)

    if require_chapter is not None:
        chapters = agg.get("chapters") or {}
        if str(require_chapter) not in chapters:
            errors.append("required chapter_id %s not observed" % require_chapter)

    if require_mode is not None:
        modes = agg.get("modes") or {}
        if require_mode not in modes:
            errors.append("required mode %s not observed" % require_mode)

    if require_required_for_slice:
        pack = summary.get("pack")
        if not pack:
            errors.append("require_required_for_slice needs --pack")
        else:
            missing = pack.get("required_missing") or []
            if missing:
                errors.append(
                    "required_for_slice rules missing live/shadow hits: %s" % missing
                )

    if require_completed:
        if int(agg.get("completed_duels") or 0) < duel_count or duel_count == 0:
            errors.append(
                "completed_duels %d of %d (require all selected duels end_summary)"
                % (int(agg.get("completed_duels") or 0), duel_count)
            )

    return errors


def parse_args(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Summarize CampaignCpuAuditLog.jsonl for PR4b/PR5 gates."
    )
    parser.add_argument(
        "logs",
        nargs="+",
        help="CampaignCpuAuditLog.jsonl path(s)",
    )
    parser.add_argument(
        "--last-n-duels",
        type=int,
        default=None,
        help="Only score the last N pack_loaded segments (after filters).",
    )
    parser.add_argument(
        "--chapter",
        type=int,
        default=None,
        help="Filter to this chapter_id.",
    )
    parser.add_argument(
        "--mode",
        default=None,
        help="Filter to pack mode (e.g. pr4b_scripted, pr3_capture_shadow).",
    )
    parser.add_argument(
        "--pack",
        default=None,
        help="Chapter pack JSON for required_for_slice / never-fired reports.",
    )
    parser.add_argument("--min-duels", type=int, default=0)
    parser.add_argument("--min-commits", type=int, default=0)
    parser.add_argument("--min-live-decisions", type=int, default=0)
    parser.add_argument("--min-shadow-decisions", type=int, default=0)
    parser.add_argument(
        "--require-zero-stalls",
        action="store_true",
        help="S10: fail if any post_commit_stall in the selected set.",
    )
    parser.add_argument(
        "--require-zero-indeterminate",
        action="store_true",
        help="S10: fail if any commit_indeterminate in the selected set.",
    )
    parser.add_argument(
        "--require-zero-human-seat-commits",
        action="store_true",
        help=(
            "S10: require every live RuleCommit acting_player == owned_seat "
            "with valid distinct and consistent ownership context."
        ),
    )
    parser.add_argument(
        "--require-zero-location-mask-empty",
        action="store_true",
        help="Fail if TemporaryCpu location_mask_empty residual appears.",
    )
    parser.add_argument(
        "--require-successive-main-safety",
        action="store_true",
        help=(
            "Fail on unsafe-lineage recapture, invalid confirmation, transition "
            "failure/timeout, or a dominated position commit."
        ),
    )
    parser.add_argument(
        "--require-rule",
        action="append",
        default=[],
        help="Require this rule_id in decisions or commits (repeatable).",
    )
    parser.add_argument(
        "--require-deck-hash",
        default=None,
        help="S11: require this deck_hash on all selected packs (use 'pin' for 11010078 default).",
    )
    parser.add_argument(
        "--require-chapter",
        type=int,
        default=None,
        help="Require this chapter_id appears in the selected set.",
    )
    parser.add_argument(
        "--require-mode",
        default=None,
        help="Require this mode appears in the selected set.",
    )
    parser.add_argument(
        "--require-required-for-slice",
        action="store_true",
        help="S8: with --pack, every required_for_slice rule must have a hit.",
    )
    parser.add_argument(
        "--require-completed",
        action="store_true",
        help="Every selected duel must have duel_end_summary.",
    )
    parser.add_argument(
        "--compact",
        action="store_true",
        help="Omit per-duel legal detail; print aggregate-focused JSON.",
    )
    parser.add_argument(
        "--offline-replay-report",
        action="store_true",
        help="Emit a deterministic offline replay report for review.",
    )
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = parse_args(argv or sys.argv[1:])
    deck_hash = args.require_deck_hash
    if deck_hash == "pin":
        deck_hash = DEFAULT_DECK_PIN

    summary = analyze_paths(
        args.logs,
        last_n_duels=args.last_n_duels,
        chapter_id=args.chapter,
        mode=args.mode,
        pack_path=args.pack,
    )
    errors = validate_requirements(
        summary,
        min_duels=args.min_duels,
        min_commits=args.min_commits,
        min_live_decisions=args.min_live_decisions,
        min_shadow_decisions=args.min_shadow_decisions,
        require_zero_stalls=args.require_zero_stalls,
        require_zero_indeterminate=args.require_zero_indeterminate,
        require_zero_human_seat_commits=args.require_zero_human_seat_commits,
        require_zero_location_mask_empty=args.require_zero_location_mask_empty,
        require_successive_main_safety=args.require_successive_main_safety,
        require_rules=args.require_rule,
        require_deck_hash=deck_hash,
        require_chapter=args.require_chapter,
        require_mode=args.require_mode,
        require_required_for_slice=args.require_required_for_slice,
        require_completed=args.require_completed,
    )

    payload: Dict[str, Any] = dict(summary)
    if args.compact:
        compact_duels = []
        for d in summary.get("duels") or []:
            compact_duels.append(
                {
                    "start_line": d.get("start_line"),
                    "end_line": d.get("end_line"),
                    "pack_ts": d.get("pack_ts"),
                    "chapter_id": d.get("chapter_id"),
                    "mode": d.get("mode"),
                    "log_only": d.get("log_only"),
                    "live_decisions": d.get("live_decisions"),
                    "shadow_decisions": d.get("shadow_decisions"),
                    "commits": d.get("commits"),
                    "commit_rule_ids": d.get("commit_rule_ids"),
                    "decision_rule_ids": d.get("decision_rule_ids"),
                    "safety": d.get("safety"),
                    "mechanical_hits": d.get("mechanical_hits"),
                    "lease_begin_reasons": d.get("lease_begin_reasons"),
                    "duel_end_summary": d.get("duel_end_summary"),
                    "deck_hash": d.get("deck_hash"),
                }
            )
        payload = {
            "paths": summary.get("paths"),
            "duel_count": summary.get("duel_count"),
            "duels": compact_duels,
            "aggregate": summary.get("aggregate"),
            "pack": summary.get("pack"),
            "orphan_events_before_first_pack": summary.get(
                "orphan_events_before_first_pack"
            ),
        }
    elif args.offline_replay_report:
        payload = build_offline_replay_report(summary)
    payload["errors"] = errors
    print(json.dumps(payload, indent=2, sort_keys=True))
    offline_gate_failed = bool(
        args.offline_replay_report
        and not (payload.get("admission") or {}).get("offline_replay_gate_passed")
    )
    return 1 if errors or offline_gate_failed else 0


if __name__ == "__main__":
    raise SystemExit(main())

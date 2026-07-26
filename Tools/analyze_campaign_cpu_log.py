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
        # Live RuleCommit rows lacking resolvable ownership context (fail closed on gate).
        "ownership_context_missing_rule_commits": 0,
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
            # Human-seat RuleCommit is a S10 failure (PR2b). Mechanical auto may
            # report acting=0 under dual-Human while turn is owned — only flag
            # non-mechanical RuleCommit when acting is the human seat and not owned.
            #
            # Ownership resolution (production schema first, then pack segment):
            # 1) row my_id / owned_seat when present
            # 2) else enclosing pack_loaded (or seat_owned) segment values
            # Fail closed: live RuleCommit with no resolvable my_id+owned_seat cannot
            # prove human-seat safety — counted as ownership_context_missing.
            acting = event.get("acting_player")
            owned = event.get("owned_seat")
            if owned is None:
                owned = current.get("owned_seat")
            my_id = event.get("my_id")
            if my_id is None:
                my_id = current.get("my_id")
            if (
                route == "RuleCommit"
                and not event.get("shadow_only")
            ):
                if my_id is None or owned is None or acting is None:
                    current["ownership_context_missing_rule_commits"] += 1
                elif acting == my_id and acting != owned:
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
    ownership_missing = 0
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
        ownership_missing += int(d.get("ownership_context_missing_rule_commits") or 0)
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
        "ownership_context_missing_rule_commits": ownership_missing,
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
        }
    return summary


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
        if missing != 0:
            # Fail closed: cannot prove S10 ownership without production context.
            errors.append(
                "ownership_context_missing_rule_commits %d "
                "(require-zero-human-seat-commits needs my_id+owned_seat+acting on "
                "live RuleCommit rows or enclosing pack_loaded segment)"
                % missing
            )
        if int(agg.get("human_seat_rule_commits") or 0) != 0:
            errors.append(
                "human_seat_rule_commits %d (require zero)"
                % int(agg.get("human_seat_rule_commits") or 0)
            )
    mask_empty = int((agg.get("safety") or {}).get("location_mask_empty") or 0)
    if require_zero_location_mask_empty and mask_empty != 0:
        errors.append("location_mask_empty count %d (require zero)" % mask_empty)

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
        help="S10: fail if any RuleCommit acting as MyId (non-owned).",
    )
    parser.add_argument(
        "--require-zero-location-mask-empty",
        action="store_true",
        help="Fail if TemporaryCpu location_mask_empty residual appears.",
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
    payload["errors"] = errors
    print(json.dumps(payload, indent=2, sort_keys=True))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())

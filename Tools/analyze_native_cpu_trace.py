#!/usr/bin/env python3
"""Validate NS2 read-only native CPU candidate/choice trace rows."""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple


SUPPORTED_DUEL_DLL_SHA256 = (
    "97bd4d136e39b0872e4a8a9171632f1f0bd9bb04d69af08e836c56e684d43c44"
)
MAX_CANDIDATES = 299
ELIGIBLE_SOLO_GAME_MODES = {0, 2, 9}


def iter_events(
    paths: Sequence[str],
) -> Iterable[Tuple[Path, int, Dict[str, Any]]]:
    for path_text in paths:
        path = Path(path_text)
        with path.open("r", encoding="utf-8") as reader:
            for line_number, line in enumerate(reader, 1):
                line = line.strip()
                if not line:
                    continue
                try:
                    event = json.loads(line)
                except json.JSONDecodeError as exc:
                    raise ValueError(
                        f"{path}:{line_number} invalid JSON: {exc}"
                    ) from exc
                if isinstance(event, dict):
                    yield path, line_number, event


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def validate_trace(event: Dict[str, Any]) -> List[str]:
    errors: List[str] = []
    if event.get("read_only") is not True:
        errors.append("not_read_only")
    if (
        str(event.get("duel_dll_sha256") or "").lower()
        != SUPPORTED_DUEL_DLL_SHA256
    ):
        errors.append("unsupported_duel_dll_hash")
    if event.get("native_scores_available") is not False:
        errors.append("unproven_native_scores")
    if "native_score" in event:
        errors.append("unproven_native_score_field")

    count = event.get("candidate_count")
    candidates = event.get("candidates")
    selected_index = event.get("selected_index")
    if not _is_int(count) or count < 1 or count > MAX_CANDIDATES:
        errors.append("candidate_count_out_of_range")
    if not isinstance(candidates, list):
        errors.append("candidates_not_list")
        candidates = []
    if _is_int(count) and len(candidates) != count:
        errors.append("candidate_count_mismatch")
    if (
        not _is_int(selected_index)
        or not _is_int(count)
        or selected_index < 0
        or selected_index >= count
    ):
        errors.append("selected_index_out_of_range")

    selected_rows: List[Dict[str, Any]] = []
    for index, candidate in enumerate(candidates):
        if not isinstance(candidate, dict):
            errors.append("candidate_not_object")
            continue
        if candidate.get("index") != index:
            errors.append("candidate_index_mismatch")
        if not _is_int(candidate.get("raw")):
            errors.append("candidate_raw_missing")
        if candidate.get("is_selected") is True:
            selected_rows.append(candidate)
    if len(selected_rows) != 1:
        errors.append("selected_marker_count")
    elif _is_int(selected_index):
        selected = selected_rows[0]
        if selected.get("index") != selected_index:
            errors.append("selected_marker_mismatch")
        if selected.get("raw") != event.get("native_chosen_raw"):
            errors.append("native_chosen_mismatch")

    my_id = event.get("my_id")
    owned_seat = event.get("owned_seat")
    player = event.get("player")
    if my_id not in (0, 1) or owned_seat not in (0, 1):
        errors.append("seat_out_of_range")
    elif my_id == owned_seat:
        errors.append("owned_seat_is_my_id")
    if player != owned_seat:
        errors.append("player_owned_seat_mismatch")
    if not _is_int(event.get("duel_generation")):
        errors.append("duel_generation_missing")
    for key in ("turn", "turn_player", "phase"):
        if not _is_int(event.get(key)):
            errors.append(f"{key}_missing")
    return sorted(set(errors))


def analyze_paths(paths: Sequence[str]) -> Dict[str, Any]:
    return _scan_paths(paths)["report"]


def collect_correlated_candidate_traces(
    paths: Sequence[str],
) -> List[Dict[str, Any]]:
    return _scan_paths(paths)["correlated_traces"]


def _scan_paths(paths: Sequence[str]) -> Dict[str, Any]:
    event_counts: Counter[str] = Counter()
    rejection_reasons: Counter[str] = Counter()
    invalid_reasons: Counter[str] = Counter()
    activation_reasons: Counter[str] = Counter()
    hook_invocation_players: Counter[str] = Counter()
    valid_trace_count = 0
    correlated_valid_trace_count = 0
    invalid_trace_count = 0
    supported_ready_count = 0
    correlated_sessions = set()
    ready_epochs: Dict[Path, int] = {}
    current_ready_supported: Dict[Path, bool] = {}
    sessions: Dict[Tuple[Any, ...], Dict[str, Any]] = {}
    correlated_traces: List[Dict[str, Any]] = []

    for path, _line, event in iter_events(paths):
        event_name = str(event.get("event") or "")
        event_counts[event_name] += 1
        if event_name == "native_cpu_trace_hook_ready":
            ready_epochs[path] = ready_epochs.get(path, 0) + 1
            supported = (
                str(event.get("duel_dll_sha256") or "").lower()
                == SUPPORTED_DUEL_DLL_SHA256
                and event.get("read_only") is True
                and event.get("native_scores_available") is False
            )
            current_ready_supported[path] = supported
            if supported:
                supported_ready_count += 1
        elif event_name == "native_cpu_trace_duel_observed":
            activation_reasons[
                str(event.get("activation_reason") or "unknown")
            ] += 1
            session_key = _session_key(
                path, ready_epochs.get(path, 0), event
            )
            if (
                current_ready_supported.get(path, False)
                and session_key is not None
                and event.get("active") is True
                and event.get("activation_reason") == "active"
                and event.get("read_only") is True
                and event.get("game_mode") in ELIGIBLE_SOLO_GAME_MODES
                and event.get("is_pvp_duel") is False
                and event.get("is_pvp_spectator") is False
            ):
                sessions[session_key] = {
                    "observed": True,
                    "begun": False,
                    "game_mode": event.get("game_mode"),
                    "observed_ts": event.get("ts"),
                    "trace_ordinal": 0,
                }
        elif event_name == "native_cpu_trace_duel_begin":
            session_key = _session_key(
                path, ready_epochs.get(path, 0), event
            )
            session = sessions.get(session_key)
            if (
                session is not None
                and event.get("read_only") is True
                and event.get("game_mode") == session.get("game_mode")
                and str(event.get("duel_dll_sha256") or "").lower()
                == SUPPORTED_DUEL_DLL_SHA256
            ):
                session["begun"] = True
                session["begin_ts"] = event.get("ts")
        elif event_name == "native_cpu_trace_hook_invoked":
            player = event.get("player")
            hook_invocation_players[
                str(player) if _is_int(player) else "unknown"
            ] += 1
        elif event_name == "native_cpu_candidate_trace":
            errors = validate_trace(event)
            if errors:
                invalid_trace_count += 1
                invalid_reasons.update(errors)
            else:
                valid_trace_count += 1
                session_key = _session_key(
                    path, ready_epochs.get(path, 0), event
                )
                session = sessions.get(session_key)
                if session is not None and session.get("begun") is True:
                    correlated_valid_trace_count += 1
                    correlated_sessions.add(session_key)
                    correlated_traces.append(
                        {
                            "path": str(path),
                            "observed_ts": session.get("observed_ts"),
                            "begin_ts": session.get("begin_ts"),
                            "trace_ordinal": session.get("trace_ordinal", 0),
                            "event": dict(event),
                        }
                    )
                    session["trace_ordinal"] = session.get(
                        "trace_ordinal", 0
                    ) + 1
        elif event_name == "native_cpu_candidate_trace_rejected":
            rejection_reasons[
                str(event.get("reason") or "unknown")
            ] += 1

    ns2_gate = (
        supported_ready_count > 0
        and len(correlated_sessions) > 0
        and invalid_trace_count == 0
    )
    return {
        "report": {
            "supported_duel_dll_sha256": SUPPORTED_DUEL_DLL_SHA256,
            "event_counts": dict(sorted(event_counts.items())),
            "supported_hook_ready_count": supported_ready_count,
            "valid_trace_count": valid_trace_count,
            "correlated_valid_trace_count": correlated_valid_trace_count,
            "correlated_valid_session_count": len(correlated_sessions),
            "invalid_trace_count": invalid_trace_count,
            "invalid_reasons": dict(sorted(invalid_reasons.items())),
            "activation_reasons": dict(sorted(activation_reasons.items())),
            "hook_invocation_players": dict(
                sorted(hook_invocation_players.items())
            ),
            "rejection_count": sum(rejection_reasons.values()),
            "rejection_reasons": dict(sorted(rejection_reasons.items())),
            "ns2_trace_gate_passed": ns2_gate,
            "rerank_authorized": False,
            "duel_required_next": False,
        },
        "correlated_traces": correlated_traces,
    }


def _session_key(
    path: Path,
    ready_epoch: int,
    event: Dict[str, Any],
) -> Optional[Tuple[Any, ...]]:
    generation = event.get("duel_generation")
    my_id = event.get("my_id")
    owned_seat = event.get("owned_seat")
    duel_hash = str(event.get("duel_dll_sha256") or "").lower()
    if (
        not _is_int(generation)
        or my_id not in (0, 1)
        or owned_seat not in (0, 1)
        or my_id == owned_seat
        or duel_hash != SUPPORTED_DUEL_DLL_SHA256
    ):
        return None
    return (
        str(path),
        ready_epoch,
        generation,
        duel_hash,
        my_id,
        owned_seat,
    )


def validate_requirements(
    report: Dict[str, Any],
    require_valid_trace: bool,
) -> List[str]:
    errors: List[str] = []
    if require_valid_trace:
        if int(report.get("supported_hook_ready_count") or 0) < 1:
            errors.append("supported native trace hook-ready row not observed")
        if int(report.get("correlated_valid_session_count") or 0) < 1:
            errors.append(
                "fully correlated active native trace session not observed"
            )
        if int(report.get("invalid_trace_count") or 0) != 0:
            errors.append(
                "invalid native candidate traces observed: %s"
                % report.get("invalid_reasons")
            )
    return errors


def parse_args(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="+", help="CampaignCpu audit JSONL paths")
    parser.add_argument(
        "--require-valid-trace",
        action="store_true",
        help=(
            "require one supported, active duel session with a matching "
            "opponent-seat candidate trace"
        ),
    )
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = parse_args(argv)
    try:
        report = analyze_paths(args.paths)
    except (OSError, ValueError) as exc:
        print(str(exc), file=sys.stderr)
        return 2
    print(json.dumps(report, indent=2, sort_keys=True))
    errors = validate_requirements(report, args.require_valid_trace)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

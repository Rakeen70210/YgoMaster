#!/usr/bin/env python3
import argparse
import json
import math
import sys
from collections import Counter, defaultdict
from pathlib import Path


LOW_QUALITY_REASON_PATTERNS = (
    "first option",
    "select first",
    "first",
    "summon",
    "play card",
)

# Synthetic sentinel tokens used by offline hidden-info leak fixtures (LLM-004 Slice 5).
HIDDEN_INFO_SENTINELS = (
    "SENTINEL_OPP_HAND_LEAK_99501",
    "SENTINEL_FACEDOWN_SET_LEAK_99502",
    "SENTINEL_DECK_LEAK_99503",
    "SENTINEL_EXTRA_LEAK_99504",
)


def iter_events(paths):
    for path in paths:
        log_path = Path(path)
        with log_path.open("r", encoding="utf-8") as reader:
            for line_number, line in enumerate(reader, 1):
                line = line.strip()
                if not line:
                    continue
                try:
                    event = json.loads(line)
                except json.JSONDecodeError as exc:
                    raise ValueError(
                        "%s:%d invalid JSON: %s" % (log_path, line_number, exc)
                    ) from exc
                if isinstance(event, dict):
                    yield log_path, event


def analyze_paths(paths):
    event_counts = Counter()
    turns = set()
    turn_players = set()
    acting_players = set()
    phases = set()
    decision_action_types = set()
    broker_action_types = set()
    broker_commit_seqs = []
    broker_response_seqs = []
    response_latencies = []
    last_decide_commit = None
    route_counts = Counter()
    route_prompt_families = Counter()
    unsupported_windows = 0
    attack_target_broker_windows = 0
    attack_target_divergences = Counter()
    grounded_blocked_actions = 0
    applicability_contradiction_rejections = 0
    promised_followup_matched = 0
    promised_followup_unavailable = Counter()

    broker = Counter()
    quality = Counter()
    normalized_paths = [str(Path(path)) for path in paths]

    # Slice 5 duel_history audit collectors.
    public_event_count = 0
    evidence_counts = Counter()
    public_event_kind_counts = Counter()
    actor_player_counts = Counter()
    # gen -> Counter(event_id -> count)
    event_ids_by_generation = defaultdict(Counter)
    request_payload_bytes = []
    compacted_request_count = 0
    citation_issues = []
    assessment_issues = []
    hidden_info_leaks = []
    request_schema_counts = Counter()
    unparseable_request_count = 0

    for _path, event in iter_events(paths):
        kind = event.get("kind", "unknown")
        event_counts[kind] += 1

        if kind == "decision_window":
            add_int(turns, event.get("turn"))
            add_int(turn_players, event.get("turn_player"))
            add_int(acting_players, event.get("acting_player"))
            add_int(phases, event.get("current_phase"))
            collect_window_quality(quality, event)
            for action in event.get("legal_actions", []):
                if isinstance(action, dict):
                    action_kind = action.get("kind")
                    if action_kind:
                        decision_action_types.add(action_kind)
                    applicability = action.get("effect_applicability")
                    if (
                        isinstance(applicability, dict)
                        and applicability.get("is_grounded") is True
                        and applicability.get("effect_expected_to_apply") is False
                    ):
                        grounded_blocked_actions += 1
            collect_hidden_leaks(event, "decision_window", hidden_info_leaks)
            history = event.get("duel_history")
            if isinstance(history, dict) and history.get("history_compacted") is True:
                compacted_request_count += 1
        elif kind == "llm_public_duel_event":
            public_event_count += 1
            evidence = event.get("evidence")
            if isinstance(evidence, str) and evidence:
                evidence_counts[evidence] += 1
            public_kind = event.get("public_event_kind") or event.get("history_kind")
            if isinstance(public_kind, str) and public_kind:
                public_event_kind_counts[public_kind] += 1
            actor = event.get("actor_player")
            if not isinstance(actor, bool) and actor is not None:
                try:
                    actor_player_counts[str(int(actor))] += 1
                except (TypeError, ValueError):
                    pass
            gen = normalize_generation(event.get("duel_generation"))
            event_id = coerce_positive_int(event.get("event_id"))
            if event_id is not None:
                event_ids_by_generation[gen][event_id] += 1
            collect_hidden_leaks(event, "llm_public_duel_event", hidden_info_leaks)
        elif kind == "llm_broker_request_started":
            broker["request_starts"] += 1
        elif kind == "llm_broker_response":
            broker["responses"] += 1
            add_int(broker_response_seqs, event.get("request_run_effect_seq"))
            add_latency(response_latencies, event.get("latency_ms"))
            if event.get("success") is True:
                broker["successful_responses"] += 1
                collect_quality_metrics(quality, event)
            else:
                broker["failed_responses"] += 1
                if is_provider_timeout(event):
                    broker["provider_timeouts"] += 1
            unparseable = collect_response_history_metrics(
                event,
                request_payload_bytes,
                citation_issues,
                assessment_issues,
                hidden_info_leaks,
                request_schema_counts,
            )
            if unparseable:
                unparseable_request_count += 1
            # Compacted requests counted from request payload when present.
            request = parse_request_payload(event.get("request_json"))
            if isinstance(request, dict):
                history = request.get("duel_history")
                if isinstance(history, dict) and history.get("history_compacted") is True:
                    compacted_request_count += 1
        elif kind == "llm_broker_committed":
            broker["commits"] += 1
            add_int(broker_commit_seqs, event.get("request_run_effect_seq"))
            action_type = event.get("action_type")
            if action_type:
                broker_action_types.add(action_type)
            decide_key = decide_commit_key(event.get("action"))
            if decide_key is not None and decide_key == last_decide_commit:
                quality["duplicate_decide_commits"] += 1
            last_decide_commit = decide_key
        elif kind == "llm_broker_automatic_action":
            broker["automatic_actions"] += 1
            quality["mechanical_skipped"] += 1
        elif kind == "llm_broker_skipped_window":
            broker["skipped_windows"] += 1
            collect_window_quality(quality, event)
        elif kind == "llm_broker_window_routed":
            route = event.get("route") or "unknown"
            prompt_family = event.get("prompt_family") or "unknown"
            route_counts[str(route)] += 1
            route_prompt_families[str(prompt_family)] += 1
            if route == "Broker" and event.get("reason") == "attack_target":
                attack_target_broker_windows += 1
        elif kind == "llm_broker_unsupported_window":
            unsupported_windows += 1
        elif kind == "llm_attack_target_divergence":
            attack_target_divergences[str(event.get("reason") or "unknown")] += 1
        elif kind == "llm_broker_rejected":
            broker["rejected"] += 1
            if event.get("error") == "stale_run_effect_seq":
                broker["stale_rejects"] += 1
            if event.get("error") == "effect_applicability_contradiction":
                applicability_contradiction_rejections += 1
        elif kind == "llm_broker_commit_skipped":
            broker["skipped"] += 1
        elif kind == "intended_followup_matched":
            promised_followup_matched += 1
        elif kind == "intended_followup_unavailable":
            promised_followup_unavailable[str(event.get("reason") or "unknown")] += 1

    cpu_fallbacks = broker["failed_responses"] + broker["rejected"] + broker["skipped"]
    request_starts = broker["request_starts"]
    duplicates, gaps = build_generation_scoped_id_findings(event_ids_by_generation)
    payload_sorted = sorted(request_payload_bytes)
    return {
        "files": normalized_paths,
        "events": dict(sorted(event_counts.items())),
        "broker": {
            "request_starts": broker["request_starts"],
            "responses": broker["responses"],
            "successful_responses": broker["successful_responses"],
            "failed_responses": broker["failed_responses"],
            "commits": broker["commits"],
            "rejected": broker["rejected"],
            "stale_rejects": broker["stale_rejects"],
            "skipped": broker["skipped"],
            "automatic_actions": broker["automatic_actions"],
            "skipped_windows": broker["skipped_windows"],
            "provider_timeouts": broker["provider_timeouts"],
        },
        "routes": {
            "total": sum(route_counts.values()),
            "by_route": dict(sorted(route_counts.items())),
            "by_prompt_family": dict(sorted(route_prompt_families.items())),
            "unsupported_windows": unsupported_windows,
        },
        "attack_targets": {
            "broker_owned_windows": attack_target_broker_windows,
            "fallback_divergences": sum(attack_target_divergences.values()),
            "divergence_reasons": dict(sorted(attack_target_divergences.items())),
        },
        "effect_applicability": {
            "grounded_blocked_actions": grounded_blocked_actions,
            "contradiction_rejections": applicability_contradiction_rejections,
        },
        "promised_followups": {
            "matched": promised_followup_matched,
            "unavailable": sum(promised_followup_unavailable.values()),
            "unavailable_reasons": dict(sorted(promised_followup_unavailable.items())),
        },
        "coverage": {
            "turns": sorted(turns),
            "turn_players": sorted(turn_players),
            "acting_players": sorted(acting_players),
            "phases": sorted(phases),
            "decision_action_types": sorted(decision_action_types),
            "broker_action_types": sorted(broker_action_types),
            "broker_commit_seqs": sorted(set(broker_commit_seqs)),
            "broker_response_seqs": sorted(set(broker_response_seqs)),
        },
        "quality": {
            "generic_reasons": quality["generic_reasons"],
            "low_confidence": quality["low_confidence"],
            "cardless_command_choices": quality["cardless_command_choices"],
            "missing_opponent_board_assessments": quality[
                "missing_opponent_board_assessments"
            ],
            "immediate_end_phase_choices": quality["immediate_end_phase_choices"],
            "duplicate_decide_commits": quality["duplicate_decide_commits"],
            "meaningful_model_calls": request_starts,
            "mechanical_skipped": quality["mechanical_skipped"],
            "strategic_windows": quality["strategic_windows"],
            "mechanical_windows": quality["mechanical_windows"],
            "strategic_window_rate": safe_rate(
                quality["strategic_windows"],
                quality["strategic_windows"] + quality["mechanical_windows"],
            ),
            "generic_reason_rate": safe_rate(quality["generic_reasons"], request_starts),
            "cardless_command_rate": safe_rate(
                quality["cardless_command_choices"], request_starts
            ),
            "timeout_or_stale_rate": safe_rate(
                broker["provider_timeouts"] + broker["stale_rejects"], request_starts
            ),
            "cpu_fallbacks": cpu_fallbacks,
            "cpu_fallback_rate": safe_rate(cpu_fallbacks, request_starts),
            "latency_ms_p50": percentile(response_latencies, 50),
            "latency_ms_p95": percentile(response_latencies, 95),
        },
        "duel_history": {
            "public_event_count": public_event_count,
            "evidence_counts": dict(sorted(evidence_counts.items())),
            "public_event_kind_counts": dict(sorted(public_event_kind_counts.items())),
            "actor_player_counts": dict(sorted(actor_player_counts.items())),
            "event_id_duplicates": duplicates,
            "event_id_gaps": gaps,
            "compacted_request_count": compacted_request_count,
            "request_payload_bytes": payload_sorted,
            "request_payload_bytes_p50": nearest_rank_percentile(payload_sorted, 50),
            "request_payload_bytes_p95": nearest_rank_percentile(payload_sorted, 95),
            "citation_issues": citation_issues,
            "assessment_issues": assessment_issues,
            "hidden_info_leaks": hidden_info_leaks,
            "request_schema_counts": {
                str(key): request_schema_counts[key]
                for key in sorted(request_schema_counts.keys(), key=lambda item: str(item))
            },
            "legacy_schema3_request_count": int(request_schema_counts.get(3, 0)),
            "unparseable_request_count": unparseable_request_count,
        },
    }


def normalize_generation(value):
    if isinstance(value, bool) or value is None:
        return 0
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def coerce_positive_int(value):
    if isinstance(value, bool) or value is None:
        return None
    try:
        number = int(value)
    except (TypeError, ValueError):
        return None
    return number


def nearest_rank_percentile(sorted_values, percentile_value):
    """Deterministic nearest-rank: index = ceil(p/100 * n) - 1."""
    if not sorted_values:
        return 0
    n = len(sorted_values)
    idx = int(math.ceil((percentile_value / 100.0) * n) - 1)
    if idx < 0:
        idx = 0
    if idx >= n:
        idx = n - 1
    return sorted_values[idx]


def build_generation_scoped_id_findings(event_ids_by_generation):
    duplicates = []
    gaps = []
    for gen in sorted(event_ids_by_generation.keys()):
        counts = event_ids_by_generation[gen]
        for event_id in sorted(counts.keys()):
            if counts[event_id] > 1:
                duplicates.append({"duel_generation": gen, "event_id": event_id})
        ordered_ids = sorted(counts.keys())
        for index in range(len(ordered_ids) - 1):
            current = ordered_ids[index]
            nxt = ordered_ids[index + 1]
            if nxt > current + 1:
                gaps.append(
                    {"duel_generation": gen, "after": current, "before": nxt}
                )
    return duplicates, gaps


def collect_response_history_metrics(
    event,
    request_payload_bytes,
    citation_issues,
    assessment_issues,
    hidden_info_leaks,
    request_schema_counts=None,
):
    """
    Collect history audit metrics for one broker response.
    Returns True when request_json was present but unparseable.
    """
    raw_request = event.get("request_json")
    if isinstance(raw_request, str):
        # Exact original UTF-8 bytes of the runtime string payload.
        request_payload_bytes.append(len(raw_request.encode("utf-8")))
    elif isinstance(raw_request, dict):
        # Object form: measure a deterministic serialization for metrics pairing.
        request_payload_bytes.append(
            len(json.dumps(raw_request, separators=(",", ":"), ensure_ascii=False).encode("utf-8"))
        )

    request = parse_request_payload(raw_request)
    if isinstance(raw_request, (str, dict)):
        collect_hidden_leaks(
            raw_request if isinstance(raw_request, dict) else request,
            "request_json",
            hidden_info_leaks,
            raw_string=raw_request if isinstance(raw_request, str) else None,
        )

    if raw_request is not None and not isinstance(request, dict):
        # Present but unparseable — not a schema-v4 history violation.
        return True

    if not isinstance(request, dict):
        return False

    schema_version = coerce_positive_int(request.get("schema_version"))
    if request_schema_counts is not None and schema_version is not None:
        request_schema_counts[schema_version] += 1

    # Schema-v4 history grounding enforcement only — never retroactively flag schema 3.
    if schema_version is None or schema_version < 4:
        return False

    seq = event.get("request_run_effect_seq")
    history = request.get("duel_history") if isinstance(request.get("duel_history"), dict) else {}
    classify_citations(event, history, seq, citation_issues)
    classify_assessment(event, history, request, seq, assessment_issues)
    return False


def classify_citations(event, history, seq, citation_issues):
    if "history_event_ids_used" not in event:
        citation_issues.append(
            {
                "kind": "missing_history_event_ids_used",
                "request_run_effect_seq": seq,
            }
        )
        return

    citations = event.get("history_event_ids_used")
    if not isinstance(citations, list):
        citation_issues.append(
            {
                "kind": "invalid_history_event_id",
                "request_run_effect_seq": seq,
            }
        )
        return

    detailed_ids = set()
    for evt in history.get("events") or []:
        if isinstance(evt, dict):
            event_id = coerce_positive_int(evt.get("event_id"))
            if event_id is not None and event_id > 0:
                detailed_ids.add(event_id)

    first_detailed = coerce_positive_int(history.get("first_detailed_event_id")) or 0
    last_event_id = coerce_positive_int(history.get("last_event_id")) or 0
    summary_ranges = []
    for summary in history.get("prior_turn_summaries") or []:
        if not isinstance(summary, dict):
            continue
        first = coerce_positive_int(summary.get("first_event_id"))
        last = coerce_positive_int(summary.get("last_event_id"))
        if first is not None and last is not None and first > 0 and last >= first:
            summary_ranges.append((first, last))

    seen = set()
    for raw in citations:
        if isinstance(raw, bool) or not isinstance(raw, int):
            citation_issues.append(
                {
                    "kind": "invalid_history_event_id",
                    "request_run_effect_seq": seq,
                    "event_id": raw,
                }
            )
            continue
        event_id = int(raw)
        if event_id <= 0:
            citation_issues.append(
                {
                    "kind": "invalid_history_event_id",
                    "request_run_effect_seq": seq,
                    "event_id": event_id,
                }
            )
            continue
        if event_id in seen:
            citation_issues.append(
                {
                    "kind": "duplicate_history_event_id",
                    "request_run_effect_seq": seq,
                    "event_id": event_id,
                }
            )
            continue
        seen.add(event_id)

        if event_id in detailed_ids:
            continue

        in_summary = any(first <= event_id <= last for first, last in summary_ranges)
        if in_summary:
            kind = "compacted_away_history_event_id"
        elif last_event_id > 0 and event_id > last_event_id:
            kind = "newer_history_event_id"
        elif first_detailed > 0 and event_id < first_detailed and not in_summary:
            kind = "stale_history_event_id"
        else:
            kind = "unknown_history_event_id"
        citation_issues.append(
            {
                "kind": kind,
                "request_run_effect_seq": seq,
                "event_id": event_id,
            }
        )


def has_opponent_authored_history(history, controlled_player):
    if not isinstance(history, dict):
        return False
    try:
        controlled = int(controlled_player)
    except (TypeError, ValueError):
        controlled = 1

    for evt in history.get("events") or []:
        if not isinstance(evt, dict):
            continue
        actor = coerce_positive_int(evt.get("actor_player"))
        if actor is None:
            actor = evt.get("actor_player")
            try:
                actor = int(actor)
            except (TypeError, ValueError):
                continue
        if actor >= 0 and actor != controlled:
            return True

    for summary in history.get("prior_turn_summaries") or []:
        if not isinstance(summary, dict):
            continue
        actors = summary.get("actor_players")
        if not isinstance(actors, list):
            continue
        for actor in actors:
            try:
                seat = int(actor)
            except (TypeError, ValueError):
                continue
            if seat >= 0 and seat != controlled:
                return True
    return False


def classify_assessment(event, history, request, seq, assessment_issues):
    controlled = request.get("controlled_player", 1)
    if not has_opponent_authored_history(history, controlled):
        return
    assessment = event.get("opponent_action_assessment")
    if not isinstance(assessment, str) or not assessment.strip():
        assessment_issues.append(
            {
                "kind": "missing_opponent_action_assessment",
                "request_run_effect_seq": seq,
            }
        )


def collect_hidden_leaks(payload, sink, hidden_info_leaks, raw_string=None):
    if raw_string is not None and isinstance(raw_string, str):
        # Also scan the raw string so path can still be derived from parsed form when possible.
        parsed = parse_request_payload(raw_string)
        if isinstance(parsed, dict):
            payload = parsed
        else:
            for token in HIDDEN_INFO_SENTINELS:
                if token in raw_string:
                    hidden_info_leaks.append(
                        {
                            "token": token,
                            "sink": sink,
                            "path": "$",
                        }
                    )
            return

    if not isinstance(payload, dict):
        return

    for path, value in walk_json(payload, "$"):
        if not isinstance(value, str):
            continue
        for token in HIDDEN_INFO_SENTINELS:
            if token in value:
                hidden_info_leaks.append(
                    {
                        "token": token,
                        "sink": sink,
                        "path": path,
                    }
                )


def walk_json(value, path):
    if isinstance(value, dict):
        for key in sorted(value.keys(), key=lambda item: str(item)):
            child_path = "%s.%s" % (path, key)
            child = value[key]
            yield child_path, child
            for nested in walk_json(child, child_path):
                yield nested
    elif isinstance(value, list):
        for index, child in enumerate(value):
            child_path = "%s[%d]" % (path, index)
            yield child_path, child
            for nested in walk_json(child, child_path):
                yield nested


def collect_window_quality(quality, event):
    if event.get("is_strategic_window") is True:
        quality["strategic_windows"] += 1
    elif event.get("is_strategic_window") is False:
        quality["mechanical_windows"] += 1


def collect_quality_metrics(quality, event):
    if is_generic_reason(event.get("reason")):
        quality["generic_reasons"] += 1
    if is_low_confidence(event.get("confidence")):
        quality["low_confidence"] += 1
    if not has_meaningful_text(event.get("opponent_board_assessment")):
        quality["missing_opponent_board_assessments"] += 1

    request = parse_request_payload(event.get("request_json"))
    if not isinstance(request, dict):
        return

    selected = selected_action(request, event.get("action_id"))
    if not isinstance(selected, dict):
        return
    if selected.get("kind") == "command" and not isinstance(selected.get("card"), dict):
        quality["cardless_command_choices"] += 1
    if selected.get("kind") == "move_phase" and selected.get("phase") == "End":
        if any(
            isinstance(action, dict) and action.get("kind") == "command"
            for action in request.get("legal_actions", [])
        ):
            quality["immediate_end_phase_choices"] += 1


def decide_commit_key(action):
    if not isinstance(action, dict):
        return None
    if action.get("kind") != "command" or action.get("command") != "Decide":
        return None
    return (
        action.get("player"),
        action.get("position"),
        action.get("index"),
        action.get("card_unique_id"),
        action.get("card_id"),
    )


def parse_request_payload(value):
    if isinstance(value, dict):
        return value
    if isinstance(value, str):
        try:
            parsed = json.loads(value)
        except json.JSONDecodeError:
            return None
        if isinstance(parsed, dict):
            return parsed
    return None


def selected_action(request, action_id):
    if isinstance(action_id, bool):
        return None
    try:
        selected_action_id = int(action_id)
    except (TypeError, ValueError):
        return None
    for action in request.get("legal_actions", []):
        if isinstance(action, dict) and action.get("action_id") == selected_action_id:
            return action
    return None


def is_generic_reason(reason):
    if not reason:
        return True
    lowered = str(reason).strip().lower()
    for pattern in LOW_QUALITY_REASON_PATTERNS:
        if pattern in lowered:
            return True
    return len(lowered) < 12


def is_low_confidence(confidence):
    if confidence is None or isinstance(confidence, bool):
        return False
    try:
        return float(confidence) < 0.5
    except (TypeError, ValueError):
        return False


def has_meaningful_text(value):
    return isinstance(value, str) and len(value.strip()) >= 12


def is_provider_timeout(event):
    error_text = " ".join(
        str(event.get(key) or "") for key in ("error", "error_detail")
    ).lower()
    return "timeout" in error_text or "timed out" in error_text


def add_latency(collection, value):
    if isinstance(value, bool) or value is None:
        return
    try:
        number = float(value)
    except (TypeError, ValueError):
        return
    if number >= 0:
        collection.append(number)


def percentile(values, percentile_value):
    if not values:
        return None
    ordered = sorted(values)
    if len(ordered) == 1:
        return normalize_number(ordered[0])
    rank = int(round((percentile_value / 100.0) * (len(ordered) - 1)))
    rank = max(0, min(rank, len(ordered) - 1))
    return normalize_number(ordered[rank])


def normalize_number(value):
    if int(value) == value:
        return int(value)
    return value


def safe_rate(numerator, denominator):
    if denominator <= 0:
        return 0.0
    return float(numerator) / float(denominator)


def add_int(collection, value):
    if isinstance(value, bool):
        return
    if isinstance(value, int):
        if hasattr(collection, "add"):
            collection.add(value)
        else:
            collection.append(value)


def validate_requirements(
    summary,
    min_broker_commits=0,
    min_turns=0,
    required_broker_action_types=None,
    max_generic_reason_rate=None,
    max_cardless_command_rate=None,
    max_duplicate_decide_commits=None,
    require_strategic_window_rate=None,
):
    errors = []
    broker = summary.get("broker", {})
    coverage = summary.get("coverage", {})
    commit_count = broker.get("commits", 0)
    turn_count = len(coverage.get("turns", []))

    if commit_count < min_broker_commits:
        errors.append(
            "expected at least %d broker commits, got %d"
            % (min_broker_commits, commit_count)
        )
    if turn_count < min_turns:
        errors.append("expected at least %d turns, got %d" % (min_turns, turn_count))

    required = sorted(set(required_broker_action_types or []))
    present = set(coverage.get("broker_action_types", []))
    missing = [action_type for action_type in required if action_type not in present]
    if missing:
        errors.append("missing broker action types: %s" % ", ".join(missing))
    quality = summary.get("quality", {})
    if max_generic_reason_rate is not None:
        actual = quality_rate(
            quality,
            "generic_reason_rate",
            "generic_reasons",
            "meaningful_model_calls",
        )
        if actual > max_generic_reason_rate:
            errors.append(
                "generic reason rate %.3f exceeds %.3f"
                % (actual, max_generic_reason_rate)
            )
    if max_cardless_command_rate is not None:
        actual = quality_rate(
            quality,
            "cardless_command_rate",
            "cardless_command_choices",
            "meaningful_model_calls",
        )
        if actual > max_cardless_command_rate:
            errors.append(
                "cardless command rate %.3f exceeds %.3f"
                % (actual, max_cardless_command_rate)
            )
    if max_duplicate_decide_commits is not None:
        actual = int(quality.get("duplicate_decide_commits", 0))
        if actual > max_duplicate_decide_commits:
            errors.append(
                "duplicate Decide commits %d exceeds %d"
                % (actual, max_duplicate_decide_commits)
            )
    if require_strategic_window_rate is not None:
        actual = float(quality.get("strategic_window_rate", 0.0))
        if actual < require_strategic_window_rate:
            errors.append(
                "strategic window rate %.3f below %.3f"
                % (actual, require_strategic_window_rate)
            )
    return errors


def quality_rate(quality, rate_key, numerator_key, denominator_key):
    if rate_key in quality:
        return float(quality.get(rate_key) or 0.0)
    return safe_rate(
        int(quality.get(numerator_key, 0) or 0),
        int(quality.get(denominator_key, 0) or 0),
    )


def parse_args(argv):
    parser = argparse.ArgumentParser(
        description="Summarize YgoMaster LLM decision JSONL coverage."
    )
    parser.add_argument("logs", nargs="+", help="LlmDecisionLog.jsonl paths")
    parser.add_argument("--min-broker-commits", type=int, default=0)
    parser.add_argument("--min-turns", type=int, default=0)
    parser.add_argument(
        "--require-broker-action-type",
        action="append",
        default=[],
        help="Require at least one committed broker action of this type.",
    )
    parser.add_argument("--max-generic-reason-rate", type=float)
    parser.add_argument("--max-cardless-command-rate", type=float)
    parser.add_argument("--max-duplicate-decide-commits", type=int)
    parser.add_argument("--require-strategic-window-rate", type=float)
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv or sys.argv[1:])
    summary = analyze_paths(args.logs)
    errors = validate_requirements(
        summary,
        min_broker_commits=args.min_broker_commits,
        min_turns=args.min_turns,
        required_broker_action_types=args.require_broker_action_type,
        max_generic_reason_rate=args.max_generic_reason_rate,
        max_cardless_command_rate=args.max_cardless_command_rate,
        max_duplicate_decide_commits=args.max_duplicate_decide_commits,
        require_strategic_window_rate=args.require_strategic_window_rate,
    )
    payload = dict(summary)
    payload["errors"] = errors
    print(json.dumps(payload, indent=2, sort_keys=True))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())

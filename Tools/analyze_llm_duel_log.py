#!/usr/bin/env python3
import argparse
import json
import sys
from collections import Counter
from pathlib import Path


LOW_QUALITY_REASON_PATTERNS = (
    "first option",
    "select first",
    "first",
    "summon",
    "play card",
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

    broker = Counter()
    quality = Counter()
    normalized_paths = [str(Path(path)) for path in paths]

    for _path, event in iter_events(paths):
        kind = event.get("kind", "unknown")
        event_counts[kind] += 1

        if kind == "decision_window":
            add_int(turns, event.get("turn"))
            add_int(turn_players, event.get("turn_player"))
            add_int(acting_players, event.get("acting_player"))
            add_int(phases, event.get("current_phase"))
            for action in event.get("legal_actions", []):
                if isinstance(action, dict):
                    action_kind = action.get("kind")
                    if action_kind:
                        decision_action_types.add(action_kind)
        elif kind == "llm_broker_request_started":
            broker["request_starts"] += 1
        elif kind == "llm_broker_response":
            broker["responses"] += 1
            add_int(broker_response_seqs, event.get("request_run_effect_seq"))
            if event.get("success") is True:
                broker["successful_responses"] += 1
                collect_quality_metrics(quality, event)
            else:
                broker["failed_responses"] += 1
        elif kind == "llm_broker_committed":
            broker["commits"] += 1
            add_int(broker_commit_seqs, event.get("request_run_effect_seq"))
            action_type = event.get("action_type")
            if action_type:
                broker_action_types.add(action_type)
        elif kind == "llm_broker_rejected":
            broker["rejected"] += 1
        elif kind == "llm_broker_commit_skipped":
            broker["skipped"] += 1

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
            "skipped": broker["skipped"],
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
            "immediate_end_phase_choices": quality["immediate_end_phase_choices"],
        },
    }


def collect_quality_metrics(quality, event):
    if is_generic_reason(event.get("reason")):
        quality["generic_reasons"] += 1
    if is_low_confidence(event.get("confidence")):
        quality["low_confidence"] += 1

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
    return errors


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
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv or sys.argv[1:])
    summary = analyze_paths(args.logs)
    errors = validate_requirements(
        summary,
        min_broker_commits=args.min_broker_commits,
        min_turns=args.min_turns,
        required_broker_action_types=args.require_broker_action_type,
    )
    payload = dict(summary)
    payload["errors"] = errors
    print(json.dumps(payload, indent=2, sort_keys=True))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())

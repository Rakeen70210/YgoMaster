#!/usr/bin/env python3
import argparse
import importlib.util
import json
import sys
import time
from pathlib import Path


BROKER_PATH = Path(__file__).with_name("llm_broker.py")
spec = importlib.util.spec_from_file_location("llm_broker", BROKER_PATH)
llm_broker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(llm_broker)


def migrate_schema_v3_request(request):
    """Explicit schema-v3 → v4 migration for offline replay fixtures only."""
    return llm_broker.migrate_schema_v3_request(request)


def accept_schema_v3_replay_fixture(request):
    """Alias for migrate_schema_v3_request (explicit compatibility path)."""
    return llm_broker.accept_schema_v3_replay_fixture(request)


LOW_QUALITY_REASON_PATTERNS = (
    "first option",
    "select first",
    "first legal",
    "play card",
)


def iter_jsonl_events(path):
    with Path(path).open("r", encoding="utf-8") as reader:
        for line_number, line in enumerate(reader, 1):
            line = line.strip()
            if not line:
                continue
            try:
                event = json.loads(line)
            except json.JSONDecodeError as exc:
                raise ValueError("%s:%d invalid JSON: %s" % (path, line_number, exc)) from exc
            if isinstance(event, dict):
                yield event


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


def extract_requests_from_logs(paths):
    requests = []
    seen = set()
    for path in paths:
        for event in iter_jsonl_events(path):
            if event.get("kind") != "llm_broker_response":
                continue
            request = parse_request_payload(event.get("request_json"))
            if not isinstance(request, dict):
                continue
            key = request_key(request)
            if key in seen:
                continue
            seen.add(key)
            requests.append(request)
    return requests


def load_request_files(paths):
    requests = []
    for path in paths:
        with Path(path).open("r", encoding="utf-8") as reader:
            data = json.load(reader)
        if isinstance(data, dict) and isinstance(data.get("requests"), list):
            requests.extend(request for request in data["requests"] if isinstance(request, dict))
        elif isinstance(data, list):
            requests.extend(request for request in data if isinstance(request, dict))
        elif isinstance(data, dict):
            requests.append(data)
    return requests


def request_key(request):
    return (
        request.get("run_effect_seq"),
        json.dumps(request.get("legal_actions") or [], sort_keys=True, separators=(",", ":")),
    )


def known_action_ids(request):
    result = set()
    for action in request.get("legal_actions") or []:
        if isinstance(action, dict) and not isinstance(action.get("action_id"), bool):
            try:
                result.add(int(action.get("action_id")))
            except (TypeError, ValueError):
                pass
    return result


def selected_action(request, action_id):
    if isinstance(action_id, bool):
        return None
    try:
        selected_id = int(action_id)
    except (TypeError, ValueError):
        return None
    for action in request.get("legal_actions") or []:
        if isinstance(action, dict) and action.get("action_id") == selected_id:
            return action
    return None


def is_generic_reason(reason):
    if not reason:
        return True
    lowered = str(reason).strip().lower()
    if len(lowered) < 12:
        return True
    return any(pattern in lowered for pattern in LOW_QUALITY_REASON_PATTERNS)


def is_low_confidence(confidence):
    if confidence is None or isinstance(confidence, bool):
        return False
    try:
        return float(confidence) < 0.5
    except (TypeError, ValueError):
        return False


def is_card_specific_reason(reason, action):
    if not isinstance(action, dict) or not reason:
        return False
    card = action.get("card")
    if not isinstance(card, dict):
        return False
    name = card.get("name")
    return isinstance(name, str) and name.strip().lower() in str(reason).lower()


def is_immediate_end_phase_choice(request, action):
    if not isinstance(action, dict):
        return False
    if action.get("kind") != "move_phase" or action.get("phase") != "End":
        return False
    return any(
        isinstance(candidate, dict) and candidate.get("kind") == "command"
        for candidate in request.get("legal_actions") or []
    )


def empty_duel_history():
    return {
        "history_version": 1,
        "duel_generation": 0,
        "last_event_id": 0,
        "first_detailed_event_id": 1,
        "history_compacted": False,
        "budget_status": "ok",
        "budget_reason": None,
        "events": [],
        "prior_turn_summaries": [],
        "revealed_card_context": [],
        "gap_warnings": [],
    }


def evaluate_history_present_vs_removed(request, decider=None):
    """
    Paired evaluation of the same schema-v4 request with history present vs removed.
    Never mutates the caller's request object. Rejects schema v3 unless pre-migrated.
    """
    import copy
    import time as _time

    if not isinstance(request, dict):
        raise ValueError("request must be an object")
    schema_version = request.get("schema_version")
    if schema_version != 4:
        raise ValueError("evaluate_history_present_vs_removed requires schema_version 4")
    if not isinstance(request.get("legal_actions"), list):
        raise ValueError("request must include legal_actions")

    decider = decider or llm_broker.choose_response
    present_request = copy.deepcopy(request)
    removed_request = copy.deepcopy(request)
    removed_request["duel_history"] = empty_duel_history()

    def _run(req):
        started = _time.monotonic()
        response = decider(req)
        latency_ms = int(round((_time.monotonic() - started) * 1000))
        if not isinstance(response, dict):
            raise ValueError("provider returned non-object response")
        return {
            "action_id": response.get("action_id"),
            "reason": response.get("reason"),
            "opponent_action_assessment": response.get("opponent_action_assessment"),
            "history_event_ids_used": response.get("history_event_ids_used")
            if isinstance(response.get("history_event_ids_used"), list)
            else [],
            "latency_ms": latency_ms,
            "response": response,
            "request": req,
        }

    present = _run(present_request)
    removed = _run(removed_request)
    delta = {
        "action_id_changed": present.get("action_id") != removed.get("action_id"),
        "reason_changed": present.get("reason") != removed.get("reason"),
        "opponent_action_assessment_changed": present.get("opponent_action_assessment")
        != removed.get("opponent_action_assessment"),
        "history_event_ids_used_changed": present.get("history_event_ids_used")
        != removed.get("history_event_ids_used"),
        "latency_ms_delta": int(removed.get("latency_ms") or 0)
        - int(present.get("latency_ms") or 0),
    }
    return {
        "present": present,
        "removed": removed,
        "delta": delta,
        "removed_request": removed_request,
    }


def evaluate_requests(requests, decider=None):
    decider = decider or llm_broker.choose_response
    results = []
    latencies = []
    summary = {
        "requests": len(requests),
        "valid_responses": 0,
        "provider_errors": 0,
        "known_action_ids": 0,
        "card_specific_reasons": 0,
        "generic_reasons": 0,
        "low_confidence": 0,
        "immediate_end_phase_choices": 0,
        "filtered_or_mechanical_choices": 0,
        "below_heuristic_baseline": 0,
    }

    for request in requests:
        started = time.monotonic()
        result = {
            "run_effect_seq": request.get("run_effect_seq"),
            "valid_response": False,
            "known_action_id": False,
            "card_specific_reason": False,
            "generic_reason": False,
            "low_confidence": False,
            "immediate_end_phase_choice": False,
            "filtered_or_mechanical_choice": False,
            "below_heuristic_baseline": False,
            "fallback_recommended": False,
        }
        try:
            response = decider(request)
            latency_ms = int(round((time.monotonic() - started) * 1000))
            latencies.append(latency_ms)
            result["latency_ms"] = latency_ms
            result["response"] = response
            if not isinstance(response, dict):
                raise ValueError("provider returned non-object response")
            result["valid_response"] = True
            summary["valid_responses"] += 1
            action_id = response.get("action_id")
            result["action_id"] = action_id
            result["known_action_id"] = action_id in known_action_ids(request)
            if result["known_action_id"]:
                summary["known_action_ids"] += 1
            action = selected_action(request, action_id)
            best_action, best_score = heuristic_best_action(request)
            selected_score = heuristic_score_action(request, action)
            result["heuristic_best_action_id"] = (
                best_action.get("action_id") if isinstance(best_action, dict) else None
            )
            result["heuristic_best_score"] = best_score
            result["selected_heuristic_score"] = selected_score
            result["below_heuristic_baseline"] = (
                best_score is not None
                and selected_score is not None
                and selected_score + 3 < best_score
            )
            if result["below_heuristic_baseline"]:
                summary["below_heuristic_baseline"] += 1
            result["card_specific_reason"] = is_card_specific_reason(response.get("reason"), action)
            if result["card_specific_reason"]:
                summary["card_specific_reasons"] += 1
            result["generic_reason"] = is_generic_reason(response.get("reason"))
            if result["generic_reason"]:
                summary["generic_reasons"] += 1
            result["low_confidence"] = is_low_confidence(response.get("confidence"))
            if result["low_confidence"]:
                summary["low_confidence"] += 1
            result["immediate_end_phase_choice"] = is_immediate_end_phase_choice(request, action)
            if result["immediate_end_phase_choice"]:
                summary["immediate_end_phase_choices"] += 1
            result["filtered_or_mechanical_choice"] = is_filtered_or_mechanical(action)
            if result["filtered_or_mechanical_choice"]:
                summary["filtered_or_mechanical_choices"] += 1
            result["fallback_recommended"] = any(
                result[name]
                for name in (
                    "generic_reason",
                    "low_confidence",
                    "immediate_end_phase_choice",
                    "filtered_or_mechanical_choice",
                    "below_heuristic_baseline",
                )
            )
        except Exception as exc:
            result["error"] = str(exc)
            summary["provider_errors"] += 1
        results.append(result)

    summary["latency_ms_p50"] = percentile(latencies, 50)
    summary["latency_ms_p95"] = percentile(latencies, 95)
    return {"summary": summary, "results": results}


def is_filtered_or_mechanical(action):
    if not isinstance(action, dict):
        return False
    if action.get("is_mechanical") is True:
        return True
    if action.get("kind") != "command":
        return False
    return action.get("command") in ("Look", "Surrender", "Draw")


def heuristic_best_action(request):
    best_action = None
    best_score = None
    for action in request.get("legal_actions") or []:
        if not isinstance(action, dict):
            continue
        score = heuristic_score_action(request, action)
        if score is None:
            continue
        if best_score is None or score > best_score:
            best_action = action
            best_score = score
    return best_action, best_score


def heuristic_score_action(request, action):
    if not isinstance(action, dict):
        return None
    if action.get("is_mechanical") is True:
        return -5

    kind = action.get("kind")
    if kind == "move_phase":
        phase = action.get("phase")
        if phase == "End":
            return -3 if has_playable_command(request) else 0
        if phase == "Battle":
            return 1
        if phase == "Main2":
            return 0
        return 0

    if kind != "command":
        return -2

    command = action.get("command")
    if command in ("Look", "Surrender", "Draw", "Decide"):
        return -5
    if command in ("Summon", "SummonSp", "Pendulum"):
        return 5 if isinstance(action.get("card"), dict) else 2
    if command == "Action":
        return 4 if isinstance(action.get("card"), dict) else 1
    if command in ("Set", "SetMonst"):
        return 3 if isinstance(action.get("card"), dict) else 1
    if command == "Attack":
        return 3
    if command in ("Reverse", "TurnAtk", "TurnDef"):
        return 1
    return 0


def has_playable_command(request):
    for action in request.get("legal_actions") or []:
        if not isinstance(action, dict):
            continue
        if action.get("kind") == "command" and not is_filtered_or_mechanical(action):
            return True
    return False


def percentile(values, percentile_value):
    if not values:
        return None
    ordered = sorted(values)
    if len(ordered) == 1:
        return ordered[0]
    rank = int(round((percentile_value / 100.0) * (len(ordered) - 1)))
    return ordered[max(0, min(rank, len(ordered) - 1))]


def write_fixtures(requests, output_dir):
    output = Path(output_dir)
    output.mkdir(parents=True, exist_ok=True)
    for index, request in enumerate(requests, 1):
        seq = request.get("run_effect_seq", index)
        path = output / ("%03d-seq-%s.json" % (index, seq))
        path.write_text(json.dumps(request, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def parse_args(argv):
    parser = argparse.ArgumentParser(
        description="Evaluate YgoMaster LLM provider decisions against saved broker requests."
    )
    parser.add_argument("paths", nargs="+", help="LlmDecisionLog.jsonl or request JSON paths")
    parser.add_argument(
        "--requests",
        action="store_true",
        help="treat input paths as request JSON fixtures instead of JSONL logs",
    )
    parser.add_argument("--write-fixtures", help="write extracted request fixtures to this directory")
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv or sys.argv[1:])
    requests = load_request_files(args.paths) if args.requests else extract_requests_from_logs(args.paths)
    if args.write_fixtures:
        write_fixtures(requests, args.write_fixtures)
    result = evaluate_requests(requests)
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0 if result["summary"]["provider_errors"] == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())

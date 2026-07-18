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


def collect_windows(paths):
    windows = []
    windows_by_seq = {}
    current = None
    counts = Counter()
    route_counts = Counter()
    unsupported_events = []

    for path, event in iter_events(paths):
        kind = event.get("kind", "unknown")
        counts[kind] += 1
        if kind == "decision_window":
            current = {
                "source": str(path),
                "window": event,
                "response": None,
                "commit": None,
                "request_started": None,
                "route": None,
            }
            windows.append(current)
            windows_by_seq[event.get("run_effect_seq")] = current
        elif kind == "llm_broker_window_routed":
            route = event.get("route") or "unknown"
            route_counts[str(route)] += 1
            routed_window = windows_by_seq.get(event.get("run_effect_seq"))
            if routed_window is not None:
                routed_window["route"] = event
        elif kind == "llm_broker_unsupported_window":
            unsupported_events.append(event)
        elif kind == "llm_broker_request_started":
            if current is not None:
                current["request_started"] = event
        elif kind == "llm_broker_response":
            if current is not None:
                current["response"] = event
        elif kind == "llm_broker_committed":
            if current is not None:
                current["commit"] = event

    return windows, counts, route_counts, unsupported_events


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


def reason_flags(reason):
    if not reason:
        return ["missing reason"]
    lowered = reason.strip().lower()
    flags = []
    for pattern in LOW_QUALITY_REASON_PATTERNS:
        if pattern in lowered:
            flags.append("generic reason")
            break
    if len(lowered) < 12:
        flags.append("too short")
    return flags


def confidence_flags(confidence):
    if confidence is None:
        return []
    try:
        value = float(confidence)
    except (TypeError, ValueError):
        return ["invalid confidence"]
    if value < 0.5:
        return ["low confidence"]
    return []


def action_label(action):
    if not isinstance(action, dict):
        return "unknown"
    if action.get("action_label"):
        return str(action["action_label"])
    parts = [action.get("kind", "unknown")]
    if action.get("command"):
        parts.append(str(action["command"]))
    if action.get("phase"):
        parts.append(str(action["phase"]))
    if action.get("card") and isinstance(action["card"], dict):
        name = action["card"].get("name")
        if name:
            parts.append(str(name))
    elif action.get("card_unique_id") is not None:
        parts.append("card_unique_id=%s" % action["card_unique_id"])
    return ": ".join(parts)


def summarize_request(request):
    if not isinstance(request, dict):
        return "unavailable"
    actions = request.get("legal_actions") or []
    action_bits = []
    for action in actions[:8]:
        label = action_label(action)
        if action.get("action_id") is not None:
            label = "#%s %s" % (action["action_id"], label)
        action_bits.append(label)
    if len(actions) > 8:
        action_bits.append("... +%d more" % (len(actions) - 8))
    return ", ".join(action_bits) if action_bits else "no legal actions"


HIDDEN_INFO_SENTINELS = (
    "SENTINEL_OPP_HAND_LEAK_99501",
    "SENTINEL_FACEDOWN_SET_LEAK_99502",
    "SENTINEL_DECK_LEAK_99503",
    "SENTINEL_EXTRA_LEAK_99504",
)
SET_EVENT_KINDS = ("set_monster", "set_spell_trap")


def render_report(paths):
    windows, counts, route_counts, unsupported_events = collect_windows(paths)
    lines = ["# LLM Decision Report", ""]
    lines.append("## Duel Summary")
    lines.append("")
    lines.append("- Files: %s" % ", ".join(str(Path(p)) for p in paths))
    lines.append("- Decision windows: %d" % counts.get("decision_window", 0))
    lines.append("- Broker responses: %d" % counts.get("llm_broker_response", 0))
    lines.append("- Broker commits: %d" % counts.get("llm_broker_committed", 0))
    lines.append("- Stale rejects: %d" % counts.get("llm_broker_rejected", 0))
    lines.append("- Commit skips: %d" % counts.get("llm_broker_commit_skipped", 0))
    lines.append("- Request starts: %d" % counts.get("llm_broker_request_started", 0))
    lines.append("- Routed windows: %d" % sum(route_counts.values()))
    for route, count in sorted(route_counts.items()):
        lines.append("- Route %s: %d" % (route, count))
    lines.append("- Unsupported windows: %d" % len(unsupported_events))
    for event in unsupported_events[:10]:
        lines.append(
            "- unsupported window: %s (%s)"
            % (event.get("prompt_family"), event.get("reason"))
        )
    lines.append("")

    for index, item in enumerate(windows, 1):
        window = item["window"]
        route = item["route"] or {}
        response = item["response"] or {}
        commit = item["commit"] or {}
        request = parse_request_payload(response.get("request_json")) if response else None
        if request is None and isinstance(window.get("duel_history"), dict):
            request = {
                "legal_actions": window.get("legal_actions") or [],
                "duel_history": window.get("duel_history"),
            }
        selected_action = None
        if isinstance(request, dict):
            for action in request.get("legal_actions") or []:
                if action.get("action_id") == response.get("action_id"):
                    selected_action = action
                    break

        history = {}
        if isinstance(request, dict) and isinstance(request.get("duel_history"), dict):
            history = request["duel_history"]
        elif isinstance(window.get("duel_history"), dict):
            history = window["duel_history"]

        lines.append("## Window %d" % index)
        lines.append("")
        lines.extend(render_duel_timeline(history, response.get("history_event_ids_used")))
        lines.append("- run_effect_seq: %s" % window.get("run_effect_seq"))
        lines.append("- turn: %s, phase: %s, acting_player: %s" % (
            window.get("turn"), window.get("current_phase"), window.get("acting_player")
        ))
        if route:
            lines.append(
                "- route: %s (%s, %s)"
                % (route.get("route"), route.get("prompt_family"), route.get("reason"))
            )
        if "is_strategic_window" in window:
            lines.append(
                "- strategic window: %s (%s)"
                % (window.get("is_strategic_window"), window.get("strategic_window_reason"))
            )
        lines.append("- response: %s" % ("success" if response.get("success") else "missing or failed"))
        if response:
            lines.append("- selected action_id: %s" % response.get("action_id"))
            lines.append("- reason: %s" % (response.get("reason") or ""))
            if response.get("confidence") is not None:
                lines.append("- confidence: %s" % response.get("confidence"))
            if response.get("plan"):
                lines.append("- plan: %s" % response.get("plan"))
            if response.get("why_now"):
                lines.append("- why now: %s" % response.get("why_now"))
            alternatives = response.get("alternatives_considered")
            if isinstance(alternatives, list):
                for alternative in alternatives:
                    lines.append("- alternative: %s" % alternative)
            elif alternatives:
                lines.append("- alternative: %s" % alternatives)
            if response.get("risk"):
                lines.append("- risk: %s" % response.get("risk"))
            if response.get("opponent_board_assessment"):
                lines.append(
                    "- opponent assessment: %s"
                    % response.get("opponent_board_assessment")
                )

            schema_version = None
            if isinstance(request, dict):
                schema_version = coerce_int(request.get("schema_version"))
            # Schema-v4 history grounding only. Legacy schema 3 gets a neutral marker.
            if schema_version is not None and schema_version < 4:
                lines.append(
                    "- history grounding unavailable for legacy schema 3"
                )
            elif schema_version is None and response.get("request_json") is not None:
                # Present request payload that did not parse — not a v4 history violation.
                lines.append("- request_json unparseable; history grounding skipped")
            else:
                if "opponent_action_assessment" in response:
                    assessment = response.get("opponent_action_assessment")
                    lines.append(
                        "- opponent_action_assessment: %s"
                        % (assessment if isinstance(assessment, str) else "")
                    )
                    if not isinstance(assessment, str) or not assessment.strip():
                        if has_opponent_authored_history(
                            history, (request or {}).get("controlled_player", 1)
                        ):
                            lines.append(
                                "- assessment missing: missing_opponent_action_assessment"
                            )
                if "history_event_ids_used" in response:
                    lines.append(
                        "- history_event_ids_used: %s"
                        % json.dumps(
                            response.get("history_event_ids_used"), separators=(",", ":")
                        )
                    )
                for issue in classify_citation_issues(
                    history, response.get("history_event_ids_used")
                ):
                    lines.append(
                        "- citation issue: %s event_id=%s"
                        % (issue["kind"], issue.get("event_id"))
                    )
                if "history_event_ids_used" not in response:
                    lines.append("- citation issue: missing_history_event_ids_used")
            flags = reason_flags(response.get("reason"))
            flags.extend(confidence_flags(response.get("confidence")))
            if flags:
                lines.append("- flags: %s" % ", ".join(flags))
        if commit:
            lines.append("- committed action_type: %s" % commit.get("action_type"))
        if isinstance(request, dict):
            lines.append("- request legal actions: %s" % summarize_request(request))
            if selected_action is not None:
                lines.append("- selected action: %s" % action_label(selected_action))
                if selected_action.get("strategic_role"):
                    lines.append("- strategic role: %s" % selected_action.get("strategic_role"))
                if selected_action.get("consequence_hint"):
                    lines.append("- consequence: %s" % selected_action.get("consequence_hint"))
                card = selected_action.get("card")
                if isinstance(card, dict):
                    card_name = card.get("name")
                    card_text = card.get("text")
                    if card_name and not is_hidden_sentinel(card_name):
                        lines.append("- selected card: %s" % card_name)
                    if card_text and not is_hidden_sentinel(card_text):
                        snippet = card_text.replace("\n", " ").strip()
                        if len(snippet) > 180:
                            snippet = snippet[:180].rstrip() + "..."
                        lines.append("- card text: %s" % snippet)
        lines.append("")

    return "\n".join(lines).rstrip() + "\n"


def is_hidden_sentinel(value):
    if not isinstance(value, str):
        return False
    return any(token in value for token in HIDDEN_INFO_SENTINELS)


def sanitize_event_for_timeline(event):
    if not isinstance(event, dict):
        return {}
    kind = str(event.get("kind") or event.get("public_event_kind") or "")
    cleaned = {
        "event_id": event.get("event_id"),
        "kind": kind,
        "actor_player": event.get("actor_player"),
        "phase": event.get("phase"),
        "turn": event.get("turn"),
        "evidence": event.get("evidence"),
        "destination_zone": event.get("destination_zone"),
        "source_zone": event.get("source_zone"),
    }
    # Fail-closed: never echo set/hand/deck/extra identities (including sentinels).
    if kind in SET_EVENT_KINDS:
        return cleaned
    name = event.get("card_name")
    card_id = event.get("card_id")
    if isinstance(name, str) and not is_hidden_sentinel(name):
        cleaned["card_name"] = name
    if card_id is not None and not is_hidden_sentinel(str(card_id)):
        # Numeric card ids for public events are allowed; sentinel numeric ids still blocked via name.
        try:
            cleaned["card_id"] = int(card_id)
        except (TypeError, ValueError):
            if not is_hidden_sentinel(str(card_id)):
                cleaned["card_id"] = card_id
    return cleaned


def render_duel_timeline(history, citations):
    lines = ["### Duel timeline", ""]
    events = []
    if isinstance(history, dict):
        raw_events = history.get("events") or []
        if isinstance(raw_events, list):
            events = [evt for evt in raw_events if isinstance(evt, dict)]
    events = sorted(
        events,
        key=lambda evt: (
            int(evt.get("event_id") or 0)
            if not isinstance(evt.get("event_id"), bool)
            else 0
        ),
    )
    cited = set()
    if isinstance(citations, list):
        for item in citations:
            try:
                if not isinstance(item, bool):
                    cited.add(int(item))
            except (TypeError, ValueError):
                pass
    if not events:
        lines.append("- (no detailed public events)")
        lines.append("")
        return lines
    for event in events:
        cleaned = sanitize_event_for_timeline(event)
        event_id = cleaned.get("event_id")
        try:
            event_id_int = int(event_id)
        except (TypeError, ValueError):
            event_id_int = None
        marker = " **cited**" if event_id_int in cited else ""
        bits = ["event_id=%s" % event_id, "kind=%s" % cleaned.get("kind")]
        if cleaned.get("card_name"):
            bits.append("card_name=%s" % cleaned["card_name"])
        if cleaned.get("destination_zone"):
            bits.append("destination_zone=%s" % cleaned["destination_zone"])
        lines.append("- %s%s" % (" ".join(bits), marker))
    lines.append("")
    return lines


def coerce_int(value):
    if isinstance(value, bool) or value is None:
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def has_opponent_authored_history(history, controlled_player):
    if not isinstance(history, dict):
        return False
    controlled = coerce_int(controlled_player)
    if controlled is None:
        controlled = 1
    for evt in history.get("events") or []:
        if not isinstance(evt, dict):
            continue
        actor = coerce_int(evt.get("actor_player"))
        if actor is not None and actor >= 0 and actor != controlled:
            return True
    for summary in history.get("prior_turn_summaries") or []:
        if not isinstance(summary, dict):
            continue
        actors = summary.get("actor_players")
        if not isinstance(actors, list):
            continue
        for actor in actors:
            seat = coerce_int(actor)
            if seat is not None and seat >= 0 and seat != controlled:
                return True
    return False


def classify_citation_issues(history, citations):
    if not isinstance(history, dict) or not isinstance(citations, list):
        return []
    detailed_ids = set()
    for evt in history.get("events") or []:
        if isinstance(evt, dict):
            event_id = coerce_int(evt.get("event_id"))
            if event_id is not None and event_id > 0:
                detailed_ids.add(event_id)
    first_detailed = coerce_int(history.get("first_detailed_event_id")) or 0
    last_event_id = coerce_int(history.get("last_event_id")) or 0
    summary_ranges = []
    for summary in history.get("prior_turn_summaries") or []:
        if not isinstance(summary, dict):
            continue
        first = coerce_int(summary.get("first_event_id"))
        last = coerce_int(summary.get("last_event_id"))
        if first is not None and last is not None and first > 0 and last >= first:
            summary_ranges.append((first, last))

    issues = []
    seen = set()
    for raw in citations:
        if isinstance(raw, bool) or not isinstance(raw, int):
            issues.append({"kind": "invalid_history_event_id", "event_id": raw})
            continue
        event_id = int(raw)
        if event_id <= 0:
            issues.append({"kind": "invalid_history_event_id", "event_id": event_id})
            continue
        if event_id in seen:
            issues.append({"kind": "duplicate_history_event_id", "event_id": event_id})
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
        issues.append({"kind": kind, "event_id": event_id})
    return issues


def parse_args(argv):
    parser = argparse.ArgumentParser(
        description="Render a markdown report from YgoMaster LLM decision logs."
    )
    parser.add_argument("logs", nargs="+", help="LlmDecisionLog.jsonl paths")
    parser.add_argument("-o", "--output", help="Write report to this path instead of stdout")
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv or sys.argv[1:])
    report = render_report(args.logs)
    if args.output:
        Path(args.output).write_text(report, encoding="utf-8")
    else:
        sys.stdout.write(report)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

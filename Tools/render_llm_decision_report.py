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
    current = None
    counts = Counter()

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
            }
            windows.append(current)
        elif kind == "llm_broker_request_started":
            if current is not None:
                current["request_started"] = event
        elif kind == "llm_broker_response":
            if current is not None:
                current["response"] = event
        elif kind == "llm_broker_committed":
            if current is not None:
                current["commit"] = event

    return windows, counts


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


def render_report(paths):
    windows, counts = collect_windows(paths)
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
    lines.append("")

    for index, item in enumerate(windows, 1):
        window = item["window"]
        response = item["response"] or {}
        commit = item["commit"] or {}
        request = parse_request_payload(response.get("request_json")) if response else None
        selected_action = None
        if isinstance(request, dict):
            for action in request.get("legal_actions") or []:
                if action.get("action_id") == response.get("action_id"):
                    selected_action = action
                    break

        lines.append("## Window %d" % index)
        lines.append("")
        lines.append("- run_effect_seq: %s" % window.get("run_effect_seq"))
        lines.append("- turn: %s, phase: %s, acting_player: %s" % (
            window.get("turn"), window.get("current_phase"), window.get("acting_player")
        ))
        lines.append("- response: %s" % ("success" if response.get("success") else "missing or failed"))
        if response:
            lines.append("- selected action_id: %s" % response.get("action_id"))
            lines.append("- reason: %s" % (response.get("reason") or ""))
            if response.get("confidence") is not None:
                lines.append("- confidence: %s" % response.get("confidence"))
            if response.get("plan"):
                lines.append("- plan: %s" % response.get("plan"))
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
                card = selected_action.get("card")
                if isinstance(card, dict):
                    card_name = card.get("name")
                    card_text = card.get("text")
                    if card_name:
                        lines.append("- selected card: %s" % card_name)
                    if card_text:
                        snippet = card_text.replace("\n", " ").strip()
                        if len(snippet) > 180:
                            snippet = snippet[:180].rstrip() + "..."
                        lines.append("- card text: %s" % snippet)
        lines.append("")

    return "\n".join(lines).rstrip() + "\n"


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

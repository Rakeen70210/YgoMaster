#!/usr/bin/env python3
import argparse
import copy
import json
import os
import shlex
import shutil
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse


class ProviderResponseError(ValueError):
    pass


PROVIDER_TRANSPORT_ERRORS = (urllib.error.URLError, TimeoutError)
DEFAULT_DEEPSEEK_BASE_URL = "https://api.deepseek.com/chat/completions"
DEFAULT_DEEPSEEK_MODEL = "deepseek-chat"
DEFAULT_STRATEGY_HINTS_PATH = os.path.join("Data", "ClientData", "LlmStrategyHints.json")
DEFAULT_REASONING_LOG_PATH = os.path.join("Data", "ClientData", "LlmReasoningLog.jsonl")
_REASONING_LOG_LOCK = threading.Lock()
# Live broker request contract (schema v4). Schema-v3 fixtures only via migrate_schema_v3_request.
LIVE_SCHEMA_VERSION = 4
SCHEMA_VERSION = LIVE_SCHEMA_VERSION
DECISION_PROMPT = (
    "You control the request's acting_player in a local Yu-Gi-Oh! Master Duel test duel. "
    "Use only the provided legal_actions, your own hand/card context, public state, and duel_history. "
    "Use action_label, action_group, strategic_role, target_scope, and consequence_hint to understand each choice. "
    "Ignore actions marked is_mechanical unless no strategic action is available. "
    "Prefer board development, card advantage, lethal damage, threat removal, and resource preservation. "
    "Do not end the phase while strong proactive legal actions remain unless you have a tactical reason. "
    "Do not choose debug or generic actions such as Look or Surrender. "
    "Choose exactly one action_id from legal_actions and name the selected card or phase in the reason. "
    "Read duel_history before choosing. Use history_event_ids_used to identify the public events that materially affected the decision. "
    "Infer the opponent's plan only from those events and current public state. "
    "Never invent a facedown or hidden-zone identity. "
    "Separate fact from inference: cite public duel_history events as facts and label plan guesses as inference. "
    "Read opponent_context before choosing; if opponent field identities are unavailable, say so instead of inventing them. "
    "Explain why this action is better now than saving it or choosing another legal action. "
    "Use opponent_board_assessment for the visible opponent board/graveyard context you accounted for, "
    "opponent_action_assessment for how public history actions affected the choice, "
    "why_now for timing, alternatives_considered for rejected legal alternatives, and risk for uncertainty or downside. "
    "Reply only with JSON: {\"run_effect_seq\":number,\"action_id\":number,"
    "\"reason\":\"card-specific reason\",\"confidence\":number,\"plan\":\"short plan\","
    "\"opponent_board_assessment\":\"visible opponent context considered\","
    "\"opponent_action_assessment\":\"public history actions considered\","
    "\"history_event_ids_used\":[1],"
    "\"why_now\":\"timing rationale\",\"alternatives_considered\":[\"rejected option\"],"
    "\"risk\":\"main downside or hidden-info uncertainty\"}."
)
DECISION_JSON_SCHEMA = json.dumps(
    {
        "type": "object",
        "additionalProperties": False,
        "required": [
            "run_effect_seq",
            "action_id",
            "reason",
            "confidence",
            "plan",
            "opponent_board_assessment",
            "opponent_action_assessment",
            "history_event_ids_used",
            "why_now",
            "alternatives_considered",
            "risk",
        ],
        "properties": {
            "run_effect_seq": {"type": "integer"},
            "action_id": {"type": "integer"},
            "reason": {"type": "string"},
            "confidence": {"type": "number"},
            "plan": {"type": "string"},
            "opponent_board_assessment": {"type": "string"},
            "opponent_action_assessment": {"type": "string"},
            "history_event_ids_used": {
                "type": "array",
                "items": {"type": "integer"},
            },
            "why_now": {"type": "string"},
            "alternatives_considered": {
                "type": "array",
                "items": {"type": "string"},
            },
            "risk": {"type": "string"},
        },
    },
    separators=(",", ":"),
)


def empty_duel_history():
    """Canonical empty duel_history for schema-v3 → v4 migration."""
    return {
        "history_version": 1,
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


def migrate_schema_v3_request(request):
    """
    Explicit schema-v3 → v4 migration for replay fixtures only.
    Live path uses LIVE_SCHEMA_VERSION and never calls this implicitly.
    """
    if not isinstance(request, dict):
        raise ValueError("schema_v3_request must be an object")
    version = request.get("schema_version")
    if version == LIVE_SCHEMA_VERSION:
        raise ValueError("already_schema_v4")
    if version != 3:
        raise ValueError("unsupported_schema_version")
    if "legal_actions" not in request:
        raise ValueError("missing_legal_actions")
    if "run_effect_seq" not in request:
        raise ValueError("missing_run_effect_seq")

    migrated = copy.deepcopy(request)
    migrated["schema_version"] = LIVE_SCHEMA_VERSION
    migrated["duel_history"] = empty_duel_history()
    return migrated


def accept_schema_v3_replay_fixture(request):
    """Alias for migrate_schema_v3_request (explicit compatibility path)."""
    return migrate_schema_v3_request(request)


def deterministic_response(request):
    actions = request.get("legal_actions") or []
    if not actions:
        raise ValueError("request has no legal_actions")

    selected = None
    for action in actions:
        if action.get("kind") == "command":
            selected = action
            break
    if selected is None:
        selected = actions[0]

    return {
        "run_effect_seq": int(request["run_effect_seq"]),
        "action_id": int(selected["action_id"]),
        "reason": "deterministic fallback",
        "confidence": 0.0,
        "plan": "Fallback selected the first command action when available.",
        "opponent_board_assessment": summarize_opponent_context(
            request.get("opponent_context")
        ),
        "opponent_action_assessment": summarize_opponent_action_assessment(request),
        "history_event_ids_used": [],
        "why_now": "No model-backed timing rationale is available in deterministic fallback.",
        "alternatives_considered": [],
        "risk": "Deterministic fallback may choose a legal but strategically weak action.",
    }


def summarize_opponent_action_assessment(request):
    history = request.get("duel_history") if isinstance(request, dict) else None
    if not isinstance(history, dict):
        return "No duel_history was provided; no public opponent actions assessed."
    events = history.get("events") or []
    summaries = history.get("prior_turn_summaries") or []
    event_count = len(events) if isinstance(events, list) else 0
    summary_count = len(summaries) if isinstance(summaries, list) else 0
    if event_count == 0 and summary_count == 0:
        return "Public duel_history is empty; no opponent actions to assess."
    return (
        "Deterministic fallback inspected duel_history "
        "(%d detailed event(s), %d prior-turn summary(ies)) without model inference."
        % (event_count, summary_count)
    )


def iter_json_object_candidates(text):
    start = -1
    depth = 0
    in_string = False
    escaped = False

    for index, char in enumerate(text):
        if start < 0:
            if char == "{":
                start = index
                depth = 1
                in_string = False
                escaped = False
            continue

        if in_string:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == "\"":
                in_string = False
            continue

        if char == "\"":
            in_string = True
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                yield text[start : index + 1]
                start = -1


def strict_provider_int(value, field_name):
    if isinstance(value, bool) or not isinstance(value, int):
        raise ProviderResponseError("provider %s must be an integer" % field_name)
    return value


def strict_optional_provider_number(parsed, field_name):
    value = parsed.get(field_name)
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ProviderResponseError("provider %s must be a number" % field_name)
    return float(value)


def strict_provider_string(parsed, field_name):
    value = parsed.get(field_name)
    if not isinstance(value, str) or not value.strip():
        raise ProviderResponseError("provider %s must be a non-empty string" % field_name)
    return value


def strict_provider_string_present(parsed, field_name):
    """Require a string field to be present; empty string is allowed."""
    if field_name not in parsed:
        raise ProviderResponseError("provider %s must be present" % field_name)
    value = parsed.get(field_name)
    if not isinstance(value, str):
        raise ProviderResponseError("provider %s must be a string" % field_name)
    return value


def strict_optional_provider_string_list(parsed, field_name):
    value = parsed.get(field_name)
    if value is None:
        return []
    if not isinstance(value, list):
        raise ProviderResponseError("provider %s must be an array" % field_name)
    result = []
    for item in value:
        if not isinstance(item, str):
            raise ProviderResponseError("provider %s items must be strings" % field_name)
        result.append(item)
    return result


def strict_history_event_ids_used(parsed):
    """
    history_event_ids_used is required and must be a list of unique positive integers.
    Missing key is rejected; empty list is valid.
    """
    if "history_event_ids_used" not in parsed:
        raise ProviderResponseError("provider history_event_ids_used must be present")
    value = parsed.get("history_event_ids_used")
    if not isinstance(value, list):
        raise ProviderResponseError("provider history_event_ids_used must be a list")
    result = []
    seen = set()
    for item in value:
        if isinstance(item, bool) or not isinstance(item, int):
            raise ProviderResponseError(
                "provider history_event_ids_used items must be integers"
            )
        if item <= 0:
            raise ProviderResponseError(
                "provider history_event_ids_used contains invalid_history_event_id"
            )
        if item in seen:
            raise ProviderResponseError(
                "provider history_event_ids_used contains duplicate_history_event_id"
            )
        seen.add(item)
        result.append(item)
    return result


def legal_action_ids(request):
    actions = request.get("legal_actions") or []
    if not actions:
        raise ValueError("request has no legal_actions")

    ids = set()
    for action in actions:
        ids.add(int(action["action_id"]))
    return ids


def summarize_opponent_context(opponent_context):
    if not isinstance(opponent_context, dict):
        return "Opponent context was not provided."

    summary = opponent_context.get("summary")
    threats = opponent_context.get("known_public_threats") or []
    threat_names = []
    if isinstance(threats, list):
        for threat in threats:
            if not isinstance(threat, dict):
                continue
            name = threat.get("name")
            zone = threat.get("zone")
            if isinstance(name, str) and name.strip():
                if isinstance(zone, str) and zone.strip():
                    threat_names.append("%s in %s" % (name.strip(), zone.strip()))
                else:
                    threat_names.append(name.strip())

    parts = []
    if isinstance(summary, str) and summary.strip():
        parts.append(summary.strip())
    if threat_names:
        parts.append("known public threats: " + ", ".join(threat_names))
    if not parts:
        parts.append("No known public opponent threats were serialized.")
    return "; ".join(parts)


def parse_provider_decision(content, request):
    if not isinstance(content, str):
        raise ProviderResponseError("provider message content must be a string")

    expected_run_effect_seq = int(request["run_effect_seq"])
    known_action_ids = legal_action_ids(request)
    matching_decisions = []
    candidate_errors = []
    saw_decision_object = False
    for candidate in iter_json_object_candidates(content):
        try:
            parsed = json.loads(candidate)
        except json.JSONDecodeError:
            continue
        for parsed_decision in iter_provider_decision_dicts(parsed):
            if "run_effect_seq" not in parsed_decision or "action_id" not in parsed_decision:
                continue

            saw_decision_object = True
            try:
                run_effect_seq = strict_provider_int(
                    parsed_decision["run_effect_seq"], "run_effect_seq"
                )
                action_id = strict_provider_int(parsed_decision["action_id"], "action_id")
            except ProviderResponseError as exc:
                candidate_errors.append(str(exc))
                continue

            if run_effect_seq != expected_run_effect_seq:
                continue

            if action_id not in known_action_ids:
                candidate_errors.append("provider selected unknown action_id %d" % action_id)
                continue

            try:
                matching_decisions.append(
                    {
                        "run_effect_seq": run_effect_seq,
                        "action_id": action_id,
                        "reason": str(parsed_decision.get("reason") or "provider"),
                        "confidence": strict_optional_provider_number(
                            parsed_decision, "confidence"
                        ),
                        "plan": str(parsed_decision.get("plan") or ""),
                        "opponent_board_assessment": strict_provider_string(
                            parsed_decision, "opponent_board_assessment"
                        ),
                        "opponent_action_assessment": strict_provider_string_present(
                            parsed_decision, "opponent_action_assessment"
                        ),
                        "history_event_ids_used": strict_history_event_ids_used(
                            parsed_decision
                        ),
                        "why_now": str(parsed_decision.get("why_now") or ""),
                        "alternatives_considered": strict_optional_provider_string_list(
                            parsed_decision, "alternatives_considered"
                        ),
                        "risk": str(parsed_decision.get("risk") or ""),
                    }
                )
            except ProviderResponseError as exc:
                candidate_errors.append(str(exc))

    if matching_decisions:
        matched_action_ids = set(decision["action_id"] for decision in matching_decisions)
        if len(matched_action_ids) > 1:
            raise ProviderResponseError(
                "provider response contained multiple legal action_id values for run_effect_seq %d"
                % expected_run_effect_seq
            )
        return matching_decisions[-1]

    if candidate_errors:
        raise ProviderResponseError(candidate_errors[-1])
    if saw_decision_object:
        raise ProviderResponseError(
            "provider response did not contain a decision for run_effect_seq %d"
            % expected_run_effect_seq
        )

    raise ProviderResponseError("provider response did not contain a decision JSON object")


def provider_message_content_to_text(content):
    if isinstance(content, str):
        return content

    if isinstance(content, list):
        parts = []
        for block in content:
            if isinstance(block, str):
                parts.append(block)
            elif isinstance(block, dict) and isinstance(block.get("text"), str):
                parts.append(block["text"])
        if parts:
            return "\n".join(parts)

    raise ProviderResponseError("provider message content did not contain text")


def parse_provider_chat_response(response_data, request):
    try:
        content = response_data["choices"][0]["message"]["content"]
    except (KeyError, IndexError, TypeError) as exc:
        raise ProviderResponseError("provider response missing choices[0].message.content") from exc

    return parse_provider_decision(provider_message_content_to_text(content), request)


def iter_provider_decision_dicts(parsed):
    if not isinstance(parsed, dict):
        return

    if "run_effect_seq" in parsed and "action_id" in parsed:
        yield parsed

    text = parsed.get("text")
    if isinstance(text, str):
        for candidate in iter_json_object_candidates(text):
            try:
                nested = json.loads(candidate)
            except json.JSONDecodeError:
                continue
            if isinstance(nested, dict):
                yield nested

    for key in ("structuredOutput", "structured_output"):
        structured = parsed.get(key)
        if isinstance(structured, dict):
            yield structured


def provider_timeout_seconds():
    return float(os.environ.get("YGO_LLM_BROKER_PROVIDER_TIMEOUT", "1.5"))


def cli_timeout_seconds():
    return float(os.environ.get("YGO_LLM_BROKER_CLI_TIMEOUT", "20"))


def cli_retry_count():
    try:
        return max(0, int(os.environ.get("YGO_LLM_BROKER_CLI_RETRIES", "1")))
    except ValueError:
        return 1


def provider_error_fallback_enabled():
    value = os.environ.get("YGO_LLM_BROKER_PROVIDER_ERROR_FALLBACK", "0")
    return value.strip().lower() not in ("0", "false", "no", "off")


def reasoning_log_path():
    """Return the reasoning log path when enabled, else None.

    Enable with YGO_LLM_BROKER_REASONING_LOG=1 (default path) or a file path.
    Disable with unset / 0 / false / no / off.
    """
    value = os.environ.get("YGO_LLM_BROKER_REASONING_LOG")
    if value is None:
        return None
    stripped = value.strip()
    if not stripped or stripped.lower() in ("0", "false", "no", "off"):
        return None
    if stripped.lower() in ("1", "true", "yes", "on"):
        return DEFAULT_REASONING_LOG_PATH
    return stripped


def reasoning_log_include_prompt():
    value = os.environ.get("YGO_LLM_BROKER_REASONING_LOG_INCLUDE_PROMPT", "0")
    return value.strip().lower() not in ("0", "false", "no", "off")


def _utc_timestamp():
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def _safe_request_meta(request):
    if not isinstance(request, dict):
        return {}
    meta = {}
    for key in (
        "run_effect_seq",
        "acting_player",
        "controlled_player",
        "schema_version",
        "kind",
    ):
        if key in request:
            meta[key] = request[key]
    legal_actions = request.get("legal_actions")
    if isinstance(legal_actions, list):
        meta["legal_action_count"] = len(legal_actions)
    return meta


def _compact_parsed_decision(decision):
    if not isinstance(decision, dict):
        return None
    compact = {}
    for key in (
        "run_effect_seq",
        "action_id",
        "reason",
        "confidence",
        "plan",
        "why_now",
        "alternatives_considered",
        "risk",
        "opponent_board_assessment",
        "opponent_action_assessment",
        "history_event_ids_used",
    ):
        if key in decision:
            compact[key] = decision[key]
    return compact


def _collect_text_fragments(value, out, depth=0):
    if depth > 6 or value is None:
        return
    if isinstance(value, str):
        text = value.strip()
        if text:
            out.append(text)
        return
    if isinstance(value, list):
        for item in value:
            _collect_text_fragments(item, out, depth + 1)
        return
    if isinstance(value, dict):
        for key, item in value.items():
            key_lower = str(key).lower()
            if key_lower in (
                "reasoning",
                "reasoning_content",
                "reasoning_text",
                "thinking",
                "thought",
                "thoughts",
                "chain_of_thought",
                "cot",
                "analysis",
            ):
                _collect_text_fragments(item, out, depth + 1)
            elif key_lower in ("text", "content") and isinstance(item, str):
                _collect_text_fragments(item, out, depth + 1)


def extract_reasoning_text(response_data, message_content=None):
    fragments = []
    if isinstance(response_data, dict):
        choices = response_data.get("choices")
        if isinstance(choices, list):
            for choice in choices:
                if not isinstance(choice, dict):
                    continue
                message = choice.get("message")
                if isinstance(message, dict):
                    for key in (
                        "reasoning_content",
                        "reasoning",
                        "thinking",
                        "thought",
                        "analysis",
                    ):
                        _collect_text_fragments(message.get(key), fragments)
                _collect_text_fragments(choice.get("reasoning"), fragments)
                _collect_text_fragments(choice.get("thinking"), fragments)
        _collect_text_fragments(response_data.get("reasoning"), fragments)
        _collect_text_fragments(response_data.get("thinking"), fragments)

    # Deduplicate while preserving order.
    seen = set()
    unique = []
    for fragment in fragments:
        if fragment not in seen:
            seen.add(fragment)
            unique.append(fragment)
    if unique:
        return "\n\n".join(unique)
    return None


def append_reasoning_log(entry):
    path = reasoning_log_path()
    if not path:
        return None

    record = dict(entry or {})
    record.setdefault("kind", "provider_reasoning")
    record.setdefault("ts", _utc_timestamp())
    record.setdefault("provider", selected_provider_name())

    line = json.dumps(record, ensure_ascii=False, separators=(",", ":"), default=str)
    try:
        directory = os.path.dirname(path)
        with _REASONING_LOG_LOCK:
            if directory:
                os.makedirs(directory, exist_ok=True)
            with open(path, "a", encoding="utf-8") as writer:
                writer.write(line + "\n")
    except OSError:
        # Never let debug logging break decision flow.
        return None
    return path


def log_provider_reasoning(
    request,
    *,
    provider=None,
    attempt=None,
    transport=None,
    raw_text=None,
    raw_stdout=None,
    raw_stderr=None,
    api_response=None,
    message_content=None,
    prompt=None,
    parsed=None,
    error=None,
    latency_ms=None,
    extra=None,
):
    if reasoning_log_path() is None:
        return None

    if message_content is None and isinstance(raw_text, str):
        message_content = raw_text
    reasoning_text = extract_reasoning_text(api_response, message_content)
    if reasoning_text is None and isinstance(message_content, str):
        # When no structured thinking fields exist, the full model text is the
        # best available reasoning trace (includes wrapper text before JSON).
        reasoning_text = message_content

    entry = {
        "kind": "provider_reasoning",
        "provider": provider or selected_provider_name(),
        "transport": transport,
        "attempt": attempt,
        "ok": error is None and parsed is not None,
        "error": error,
        "latency_ms": latency_ms,
        "request": _safe_request_meta(request),
        "raw_text": raw_text,
        "raw_stdout": raw_stdout,
        "raw_stderr": raw_stderr,
        "message_content": message_content,
        "reasoning_text": reasoning_text,
        "parsed": _compact_parsed_decision(parsed),
    }
    if api_response is not None:
        entry["api_response"] = api_response
    if reasoning_log_include_prompt() and prompt is not None:
        entry["prompt"] = prompt
    elif prompt is not None:
        entry["prompt_chars"] = len(prompt)
    if isinstance(extra, dict) and extra:
        entry["extra"] = extra
    return append_reasoning_log(entry)


def normalize_provider_name(value):
    provider = (value or "").strip().lower().replace("-", "_")
    aliases = {
        "": "deterministic",
        "none": "deterministic",
        "mock": "deterministic",
        "fallback": "deterministic",
        "deterministic": "deterministic",
        "deepseek": "deepseek_api",
        "deepseek_api": "deepseek_api",
        "api": "deepseek_api",
        "openai": "openai_compatible",
        "openai_compatible": "openai_compatible",
        "grok": "grok_cli",
        "grok_cli": "grok_cli",
        "agy": "agy_cli",
        "agy_cli": "agy_cli",
        "opencode": "opencode_cli",
        "opencode_cli": "opencode_cli",
    }
    if provider not in aliases:
        raise ProviderResponseError("unsupported provider '%s'" % value)
    return aliases[provider]


def api_provider_is_configured(provider):
    api_key = os.environ.get("YGO_LLM_BROKER_API_KEY")
    if provider == "deepseek_api":
        return bool(api_key)
    return bool(
        api_key
        and os.environ.get("YGO_LLM_BROKER_BASE_URL")
        and os.environ.get("YGO_LLM_BROKER_MODEL")
    )


def selected_provider_name():
    configured = os.environ.get("YGO_LLM_BROKER_PROVIDER")
    if configured:
        return normalize_provider_name(configured)
    if api_provider_is_configured("openai_compatible"):
        base_url = os.environ.get("YGO_LLM_BROKER_BASE_URL") or ""
        if "deepseek" in base_url.lower():
            return "deepseek_api"
        return "openai_compatible"
    return "deterministic"


def command_tokens(command):
    tokens = shlex.split(command or "")
    if not tokens:
        raise ProviderResponseError("provider command is empty")
    return tokens


def command_exists(command):
    try:
        command_name = command_tokens(command)[0]
    except ProviderResponseError:
        return False
    if os.path.isabs(command_name):
        return os.path.isfile(command_name) and os.access(command_name, os.X_OK)
    return shutil.which(command_name) is not None


def cli_provider_command(provider):
    if provider == "grok_cli":
        return os.environ.get("YGO_LLM_BROKER_GROK_COMMAND", "grok")
    if provider == "agy_cli":
        return os.environ.get("YGO_LLM_BROKER_AGY_COMMAND", "agy")
    if provider == "opencode_cli":
        return os.environ.get("YGO_LLM_BROKER_OPENCODE_COMMAND", "opencode")
    raise ProviderResponseError("provider '%s' is not a CLI provider" % provider)


def provider_is_configured(provider):
    if provider == "deterministic":
        return False
    if provider in ("deepseek_api", "openai_compatible"):
        return api_provider_is_configured(provider)
    if provider in ("grok_cli", "agy_cli", "opencode_cli"):
        return command_exists(cli_provider_command(provider))
    return False


def api_provider_settings(provider):
    api_key = os.environ.get("YGO_LLM_BROKER_API_KEY")
    base_url = os.environ.get("YGO_LLM_BROKER_BASE_URL")
    model = os.environ.get("YGO_LLM_BROKER_MODEL")

    if provider == "deepseek_api":
        base_url = base_url or DEFAULT_DEEPSEEK_BASE_URL
        model = model or DEFAULT_DEEPSEEK_MODEL

    missing = []
    if not api_key:
        missing.append("YGO_LLM_BROKER_API_KEY")
    if not base_url:
        missing.append("YGO_LLM_BROKER_BASE_URL")
    if not model:
        missing.append("YGO_LLM_BROKER_MODEL")
    if missing:
        raise ProviderResponseError(
            "%s requires %s" % (provider, ", ".join(missing))
        )

    return api_key, base_url, model


def strategy_hints_path():
    return os.environ.get("YGO_LLM_BROKER_STRATEGY_HINTS") or DEFAULT_STRATEGY_HINTS_PATH


def load_strategy_hints(path=None):
    hint_path = path or strategy_hints_path()
    if not hint_path or not os.path.exists(hint_path):
        return []
    try:
        with open(hint_path, "r", encoding="utf-8") as reader:
            data = json.load(reader)
    except (OSError, json.JSONDecodeError):
        return []
    if isinstance(data, dict):
        data = data.get("hints") or []
    if not isinstance(data, list):
        return []
    return [hint for hint in data if isinstance(hint, dict)]


def visible_card_names(request):
    names = set()
    for action in request.get("legal_actions") or []:
        add_card_name(names, action.get("card") if isinstance(action, dict) else None)
    public_state = request.get("public_state") or {}
    for player in public_state.get("players") or []:
        if not isinstance(player, dict):
            continue
        for known_card in player.get("known_cards") or []:
            if isinstance(known_card, dict):
                add_card_name(names, known_card.get("card"))
    return names


def add_card_name(names, card):
    if isinstance(card, dict):
        name = card.get("name")
        if isinstance(name, str) and name.strip():
            names.add(name.strip().lower())


def hint_matches_request(hint, card_names):
    cards = hint.get("cards") or hint.get("starter_cards") or []
    if isinstance(cards, str):
        cards = [cards]
    if not isinstance(cards, list):
        return False
    for card in cards:
        if isinstance(card, str) and card.strip().lower() in card_names:
            return True
    return False


def normalize_strategy_hint(hint):
    allowed_keys = (
        "deck_name",
        "game_plan",
        "starter_cards",
        "extenders",
        "removal",
        "win_condition",
        "combo_hints",
        "cards_to_avoid_wasting",
    )
    result = {}
    for key in allowed_keys:
        value = hint.get(key)
        if value:
            result[key] = value
    return result


def enrich_request_for_prompt(request):
    enriched = copy.deepcopy(request)
    card_names = visible_card_names(enriched)
    hints = []
    for hint in load_strategy_hints():
        if hint_matches_request(hint, card_names):
            normalized = normalize_strategy_hint(hint)
            if normalized:
                hints.append(normalized)
    if hints:
        enriched["strategy_hints"] = hints[:3]
    return enriched


def build_decision_prompt(request):
    request = enrich_request_for_prompt(request)
    return (
        DECISION_PROMPT
        + "\nDecision request JSON:\n"
        + json.dumps(request, separators=(",", ":"))
    )


def provider_response(request):
    provider = selected_provider_name()
    if provider == "deterministic":
        return None
    if provider in ("deepseek_api", "openai_compatible"):
        return api_provider_response(provider, request)
    if provider in ("grok_cli", "agy_cli", "opencode_cli"):
        return cli_provider_response(provider, request)
    raise ProviderResponseError("unsupported provider '%s'" % provider)


def api_provider_response(provider, request):
    api_key, base_url, model = api_provider_settings(provider)
    prompt_request = enrich_request_for_prompt(request)
    prompt = (
        DECISION_PROMPT
        + "\nDecision request JSON:\n"
        + json.dumps(prompt_request, separators=(",", ":"))
    )

    payload = {
        "model": model,
        "messages": [
            {
                "role": "system",
                "content": DECISION_PROMPT,
            },
            {
                "role": "user",
                "content": json.dumps(prompt_request, separators=(",", ":")),
            },
        ],
        "temperature": float(os.environ.get("YGO_LLM_BROKER_TEMPERATURE", "0")),
    }

    data = json.dumps(payload).encode("utf-8")
    http_request = urllib.request.Request(
        base_url,
        data=data,
        headers={
            "Authorization": "Bearer " + api_key,
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
        method="POST",
    )
    started = time.monotonic()
    raw_body = None
    response_data = None
    message_content = None
    try:
        with urllib.request.urlopen(http_request, timeout=provider_timeout_seconds()) as response:
            raw_body = response.read().decode("utf-8")
        try:
            response_data = json.loads(raw_body)
        except json.JSONDecodeError as exc:
            raise ProviderResponseError("provider returned invalid JSON") from exc

        try:
            content = response_data["choices"][0]["message"]["content"]
            message_content = provider_message_content_to_text(content)
        except (KeyError, IndexError, TypeError, ProviderResponseError):
            message_content = None

        decision = parse_provider_chat_response(response_data, request)
        log_provider_reasoning(
            request,
            provider=provider,
            transport="api",
            attempt=1,
            raw_text=raw_body,
            api_response=response_data,
            message_content=message_content,
            prompt=prompt,
            parsed=decision,
            latency_ms=int((time.monotonic() - started) * 1000),
            extra={"model": model, "base_url": base_url},
        )
        return decision
    except ProviderResponseError as exc:
        log_provider_reasoning(
            request,
            provider=provider,
            transport="api",
            attempt=1,
            raw_text=raw_body,
            api_response=response_data,
            message_content=message_content,
            prompt=prompt,
            parsed=None,
            error=str(exc),
            latency_ms=int((time.monotonic() - started) * 1000),
            extra={"model": model, "base_url": base_url},
        )
        raise
    except PROVIDER_TRANSPORT_ERRORS as exc:
        log_provider_reasoning(
            request,
            provider=provider,
            transport="api",
            attempt=1,
            raw_text=raw_body,
            api_response=response_data,
            message_content=message_content,
            prompt=prompt,
            parsed=None,
            error=str(exc),
            latency_ms=int((time.monotonic() - started) * 1000),
            extra={"model": model, "base_url": base_url},
        )
        raise


def provider_model_arg(provider):
    if provider == "grok_cli":
        return os.environ.get("YGO_LLM_BROKER_GROK_MODEL") or os.environ.get(
            "YGO_LLM_BROKER_CLI_MODEL"
        )
    if provider == "agy_cli":
        return os.environ.get("YGO_LLM_BROKER_AGY_MODEL") or os.environ.get(
            "YGO_LLM_BROKER_CLI_MODEL"
        )
    if provider == "opencode_cli":
        return os.environ.get("YGO_LLM_BROKER_OPENCODE_MODEL") or os.environ.get(
            "YGO_LLM_BROKER_CLI_MODEL"
        )
    return None


def extra_cli_args(provider):
    if provider == "grok_cli":
        return shlex.split(os.environ.get("YGO_LLM_BROKER_GROK_ARGS", ""))
    if provider == "agy_cli":
        return shlex.split(os.environ.get("YGO_LLM_BROKER_AGY_ARGS", ""))
    if provider == "opencode_cli":
        return shlex.split(os.environ.get("YGO_LLM_BROKER_OPENCODE_ARGS", ""))
    return []


def build_grok_cli_args(prompt):
    args = command_tokens(cli_provider_command("grok_cli"))
    args.extend(
        [
            "--single",
            prompt,
            "--output-format",
            "json",
            "--json-schema",
            DECISION_JSON_SCHEMA,
            "--no-alt-screen",
            "--max-turns",
            "1",
            "--verbatim",
            "--disable-web-search",
            "--no-subagents",
        ]
    )
    model = provider_model_arg("grok_cli")
    if model:
        args.extend(["--model", model])
    args.extend(extra_cli_args("grok_cli"))
    return args


def build_agy_cli_args(prompt):
    args = command_tokens(cli_provider_command("agy_cli"))
    args.extend(["--print", "--prompt", prompt])
    model = provider_model_arg("agy_cli")
    if model:
        args.extend(["--model", model])
    args.extend(extra_cli_args("agy_cli"))
    return args


def build_opencode_cli_args(prompt):
    args = command_tokens(cli_provider_command("opencode_cli"))
    args.extend(["run", "--pure"])
    model = provider_model_arg("opencode_cli")
    if model:
        args.extend(["--model", model])
    args.extend(extra_cli_args("opencode_cli"))
    args.append(prompt)
    return args


def cli_provider_attempt(provider, args, request, attempt=1, prompt=None):
    started = time.monotonic()
    stdout = ""
    stderr = ""
    try:
        completed = subprocess.run(
            args,
            input=None,
            text=True,
            capture_output=True,
            timeout=cli_timeout_seconds(),
            check=False,
        )
    except subprocess.TimeoutExpired as exc:
        error = "%s timed out after %.1f seconds" % (provider, cli_timeout_seconds())
        log_provider_reasoning(
            request,
            provider=provider,
            transport="cli",
            attempt=attempt,
            raw_text=None,
            raw_stdout=getattr(exc, "stdout", None) or None,
            raw_stderr=getattr(exc, "stderr", None) or None,
            prompt=prompt,
            parsed=None,
            error=error,
            latency_ms=int((time.monotonic() - started) * 1000),
            extra={"command": args[0] if args else None},
        )
        raise ProviderResponseError(error) from exc
    except OSError as exc:
        error = "%s failed to start: %s" % (provider, exc)
        log_provider_reasoning(
            request,
            provider=provider,
            transport="cli",
            attempt=attempt,
            prompt=prompt,
            parsed=None,
            error=error,
            latency_ms=int((time.monotonic() - started) * 1000),
            extra={"command": args[0] if args else None},
        )
        raise ProviderResponseError(error) from exc

    stdout = completed.stdout or ""
    stderr = completed.stderr or ""
    output = stdout.strip()
    if stderr:
        output = (output + "\n" + stderr.strip()).strip()

    if completed.returncode != 0:
        detail = output or "no output"
        error = "%s exited with code %d: %s" % (provider, completed.returncode, detail)
        log_provider_reasoning(
            request,
            provider=provider,
            transport="cli",
            attempt=attempt,
            raw_text=output or None,
            raw_stdout=stdout if stdout else None,
            raw_stderr=stderr if stderr else None,
            message_content=output or None,
            prompt=prompt,
            parsed=None,
            error=error,
            latency_ms=int((time.monotonic() - started) * 1000),
            extra={
                "command": args[0] if args else None,
                "returncode": completed.returncode,
            },
        )
        raise ProviderResponseError(error)
    if not output:
        error = "%s produced no output" % provider
        log_provider_reasoning(
            request,
            provider=provider,
            transport="cli",
            attempt=attempt,
            raw_text=None,
            raw_stdout=stdout if stdout else None,
            raw_stderr=stderr if stderr else None,
            prompt=prompt,
            parsed=None,
            error=error,
            latency_ms=int((time.monotonic() - started) * 1000),
            extra={
                "command": args[0] if args else None,
                "returncode": completed.returncode,
            },
        )
        raise ProviderResponseError(error)

    try:
        decision = parse_provider_decision(output, request)
    except ProviderResponseError as exc:
        log_provider_reasoning(
            request,
            provider=provider,
            transport="cli",
            attempt=attempt,
            raw_text=output,
            raw_stdout=stdout if stdout else None,
            raw_stderr=stderr if stderr else None,
            message_content=output,
            prompt=prompt,
            parsed=None,
            error=str(exc),
            latency_ms=int((time.monotonic() - started) * 1000),
            extra={
                "command": args[0] if args else None,
                "returncode": completed.returncode,
            },
        )
        raise

    log_provider_reasoning(
        request,
        provider=provider,
        transport="cli",
        attempt=attempt,
        raw_text=output,
        raw_stdout=stdout if stdout else None,
        raw_stderr=stderr if stderr else None,
        message_content=output,
        prompt=prompt,
        parsed=decision,
        latency_ms=int((time.monotonic() - started) * 1000),
        extra={
            "command": args[0] if args else None,
            "returncode": completed.returncode,
        },
    )
    return decision


def cli_provider_response(provider, request):
    prompt = build_decision_prompt(request)
    if provider == "grok_cli":
        args = build_grok_cli_args(prompt)
    elif provider == "agy_cli":
        args = build_agy_cli_args(prompt)
    elif provider == "opencode_cli":
        args = build_opencode_cli_args(prompt)
    else:
        raise ProviderResponseError("provider '%s' is not a CLI provider" % provider)

    last_error = None
    attempts = cli_retry_count() + 1
    for attempt_index in range(attempts):
        try:
            return cli_provider_attempt(
                provider,
                args,
                request,
                attempt=attempt_index + 1,
                prompt=prompt,
            )
        except ProviderResponseError as exc:
            last_error = exc

    raise last_error


def choose_response(request):
    try:
        response = provider_response(request)
    except ProviderResponseError:
        if not provider_error_fallback_enabled():
            raise
        response = None
    if response is not None:
        return response
    return deterministic_response(request)


def broker_health_response():
    provider = selected_provider_name()
    provider_configured = provider_is_configured(provider)
    return {
        "service": "ygomaster_llm_broker",
        "status": "ok",
        "schema_version": 1,
        "provider": provider,
        "provider_configured": provider_configured,
    }


class BrokerHandler(BaseHTTPRequestHandler):
    server_version = "YgoMasterLlmBroker/1.0"

    def do_GET(self):
        if urlparse(self.path).path != "/health":
            self.send_error(404, "not found")
            return

        self.write_json(200, broker_health_response())

    def do_POST(self):
        if urlparse(self.path).path != "/decide":
            self.send_error(404, "not found")
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
            request = json.loads(self.rfile.read(length).decode("utf-8"))
            response = choose_response(request)
            self.write_json(200, response)
        except ProviderResponseError as exc:
            self.write_json(502, {"error": "provider_error", "detail": str(exc)})
        except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
            self.write_json(400, {"error": str(exc)})
        except PROVIDER_TRANSPORT_ERRORS as exc:
            self.write_json(502, {"error": "provider_error", "detail": str(exc)})
        except Exception as exc:
            self.write_json(500, {"error": "broker_error", "detail": str(exc)})

    def log_message(self, fmt, *args):
        if os.environ.get("YGO_LLM_BROKER_LOG_HTTP") == "1":
            super().log_message(fmt, *args)

    def write_json(self, status, payload):
        data = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def run_server(host, port):
    server = HTTPServer((host, port), BrokerHandler)
    print("YgoMaster LLM broker listening on http://%s:%d/decide" % (host, port))
    server.serve_forever()


def assert_raises(expected_type, fn):
    try:
        fn()
    except expected_type:
        return
    except Exception as exc:
        raise AssertionError(
            "expected %s, got %s" % (expected_type.__name__, type(exc).__name__)
        ) from exc
    raise AssertionError("expected %s" % expected_type.__name__)


def self_test():
    health = broker_health_response()
    assert health["service"] == "ygomaster_llm_broker"
    assert health["status"] == "ok"

    request = {
        "kind": "decision_request",
        "schema_version": LIVE_SCHEMA_VERSION,
        "run_effect_seq": 42,
        "public_state": {"players": []},
        "duel_history": empty_duel_history(),
        "legal_actions": [
            {"action_id": 0, "kind": "move_phase", "phase": "Battle", "phase_id": 3},
            {"action_id": 1, "kind": "command", "command": "Attack", "command_id": 0},
        ],
    }
    response = deterministic_response(request)
    assert response["run_effect_seq"] == 42
    assert response["action_id"] == 1
    assert isinstance(response["opponent_action_assessment"], str)
    assert response["history_event_ids_used"] == []

    dialog_request = {
        "kind": "decision_request",
        "schema_version": LIVE_SCHEMA_VERSION,
        "run_effect_seq": 43,
        "public_state": {"players": []},
        "duel_history": empty_duel_history(),
        "legal_actions": [
            {"action_id": 4, "kind": "dialog_result", "result": 0},
            {"action_id": 5, "kind": "list_index", "index": 1},
        ],
    }
    dialog_response = deterministic_response(dialog_request)
    assert dialog_response["run_effect_seq"] == 43
    assert dialog_response["action_id"] == 4

    provider_request = {
        "kind": "decision_request",
        "schema_version": LIVE_SCHEMA_VERSION,
        "run_effect_seq": 44,
        "public_state": {"players": []},
        "duel_history": empty_duel_history(),
        "legal_actions": [
            {"action_id": 5, "kind": "list_index", "index": 1},
        ],
    }
    provider_v4_fields = (
        '"opponent_board_assessment":"Opponent context accounted for.",'
        '"opponent_action_assessment":"No material public events.",'
        '"history_event_ids_used":[]'
    )
    provider_text = (
        "Ignore this example {\"run_effect_seq\":0,\"action_id\":999}. "
        "Use this decision {\"run_effect_seq\":44,\"action_id\":5,\"reason\":\"valid\","
        + provider_v4_fields
        + "}."
    )
    provider_decision = parse_provider_decision(provider_text, provider_request)
    assert provider_decision["run_effect_seq"] == 44
    assert provider_decision["action_id"] == 5
    assert provider_decision["reason"] == "valid"
    assert provider_decision["history_event_ids_used"] == []

    stale_tail_text = (
        "{\"run_effect_seq\":44,\"action_id\":5,\"reason\":\"valid\","
        + provider_v4_fields
        + "} "
        "{\"run_effect_seq\":0,\"action_id\":0,\"reason\":\"stale\"}"
    )
    provider_decision = parse_provider_decision(stale_tail_text, provider_request)
    assert provider_decision["run_effect_seq"] == 44
    assert provider_decision["action_id"] == 5

    ambiguous_request = dict(provider_request)
    ambiguous_request["legal_actions"] = [
        {"action_id": 5, "kind": "list_index", "index": 1},
        {"action_id": 7, "kind": "list_index", "index": 2},
    ]
    assert_raises(
        ProviderResponseError,
        lambda: parse_provider_decision(
            "{\"run_effect_seq\":44,\"action_id\":5} "
            "{\"run_effect_seq\":44,\"action_id\":7}",
            ambiguous_request,
        ),
    )

    assert_raises(
        ProviderResponseError,
        lambda: parse_provider_decision(
            "{\"run_effect_seq\":44,\"action_id\":true}", provider_request
        ),
    )
    assert_raises(
        ProviderResponseError,
        lambda: parse_provider_decision(
            "{\"run_effect_seq\":44,\"action_id\":1.5}", provider_request
        ),
    )
    assert_raises(
        ProviderResponseError,
        lambda: parse_provider_decision(
            "{\"run_effect_seq\":44,\"action_id\":999}", provider_request
        ),
    )

    block_response = {
        "choices": [
            {
                "message": {
                    "content": [
                        {"type": "text", "text": "{\"run_effect_seq\":44,"},
                        {
                            "type": "text",
                            "text": (
                                "\"action_id\":5,"
                                + provider_v4_fields
                                + "}"
                            ),
                        },
                    ]
                }
            }
        ]
    }
    provider_decision = parse_provider_chat_response(block_response, provider_request)
    assert provider_decision["run_effect_seq"] == 44
    assert provider_decision["action_id"] == 5
    assert provider_decision["opponent_action_assessment"] == "No material public events."

    assert_raises(
        ProviderResponseError,
        lambda: parse_provider_chat_response({"choices": []}, provider_request),
    )

    original_provider_timeout = os.environ.pop("YGO_LLM_BROKER_PROVIDER_TIMEOUT", None)
    try:
        assert provider_timeout_seconds() == 1.5
    finally:
        if original_provider_timeout is not None:
            os.environ["YGO_LLM_BROKER_PROVIDER_TIMEOUT"] = original_provider_timeout
    assert issubclass(TimeoutError, PROVIDER_TRANSPORT_ERRORS)

    print(json.dumps(response, separators=(",", ":")))


def main():
    parser = argparse.ArgumentParser(description="Local YgoMaster LLM decision broker")
    parser.add_argument("--host", default=os.environ.get("YGO_LLM_BROKER_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("YGO_LLM_BROKER_PORT", "4991")))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        self_test()
        return 0

    run_server(args.host, args.port)
    return 0


if __name__ == "__main__":
    sys.exit(main())

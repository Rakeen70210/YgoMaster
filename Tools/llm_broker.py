#!/usr/bin/env python3
import argparse
import json
import os
import shlex
import shutil
import subprocess
import sys
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse


class ProviderResponseError(ValueError):
    pass


PROVIDER_TRANSPORT_ERRORS = (urllib.error.URLError, TimeoutError)
DEFAULT_DEEPSEEK_BASE_URL = "https://api.deepseek.com/chat/completions"
DEFAULT_DEEPSEEK_MODEL = "deepseek-chat"
DECISION_PROMPT = (
    "You control the request's acting_player in a local Yu-Gi-Oh! Master Duel test duel. "
    "Use only the provided legal_actions, your own hand/card context, and public state. "
    "Prefer board development, card advantage, lethal damage, threat removal, and resource preservation. "
    "Do not end the phase while strong proactive legal actions remain unless you have a tactical reason. "
    "Do not choose debug or generic actions such as Look or Surrender. "
    "Choose exactly one action_id from legal_actions and name the selected card or phase in the reason. "
    "Reply only with JSON: {\"run_effect_seq\":number,\"action_id\":number,"
    "\"reason\":\"card-specific reason\",\"confidence\":number,\"plan\":\"short plan\"}."
)
DECISION_JSON_SCHEMA = json.dumps(
    {
        "type": "object",
        "additionalProperties": False,
        "required": ["run_effect_seq", "action_id"],
        "properties": {
            "run_effect_seq": {"type": "integer"},
            "action_id": {"type": "integer"},
            "reason": {"type": "string"},
            "confidence": {"type": "number"},
            "plan": {"type": "string"},
        },
    },
    separators=(",", ":"),
)


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
    }


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


def legal_action_ids(request):
    actions = request.get("legal_actions") or []
    if not actions:
        raise ValueError("request has no legal_actions")

    ids = set()
    for action in actions:
        ids.add(int(action["action_id"]))
    return ids


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
        if not isinstance(parsed, dict):
            continue
        if "run_effect_seq" not in parsed or "action_id" not in parsed:
            continue

        saw_decision_object = True
        try:
            run_effect_seq = strict_provider_int(parsed["run_effect_seq"], "run_effect_seq")
            action_id = strict_provider_int(parsed["action_id"], "action_id")
        except ProviderResponseError as exc:
            candidate_errors.append(str(exc))
            continue

        if run_effect_seq != expected_run_effect_seq:
            continue

        if action_id not in known_action_ids:
            candidate_errors.append("provider selected unknown action_id %d" % action_id)
            continue

        matching_decisions.append(
            {
                "run_effect_seq": run_effect_seq,
                "action_id": action_id,
                "reason": str(parsed.get("reason") or "provider"),
                "confidence": strict_optional_provider_number(parsed, "confidence"),
                "plan": str(parsed.get("plan") or ""),
            }
        )

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


def provider_timeout_seconds():
    return float(os.environ.get("YGO_LLM_BROKER_PROVIDER_TIMEOUT", "1.5"))


def cli_timeout_seconds():
    return float(os.environ.get("YGO_LLM_BROKER_CLI_TIMEOUT", "20"))


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
    if provider == "opencode_cli":
        return os.environ.get("YGO_LLM_BROKER_OPENCODE_COMMAND", "opencode")
    raise ProviderResponseError("provider '%s' is not a CLI provider" % provider)


def provider_is_configured(provider):
    if provider == "deterministic":
        return False
    if provider in ("deepseek_api", "openai_compatible"):
        return api_provider_is_configured(provider)
    if provider in ("grok_cli", "opencode_cli"):
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


def build_decision_prompt(request):
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
    if provider in ("grok_cli", "opencode_cli"):
        return cli_provider_response(provider, request)
    raise ProviderResponseError("unsupported provider '%s'" % provider)


def api_provider_response(provider, request):
    api_key, base_url, model = api_provider_settings(provider)

    payload = {
        "model": model,
        "messages": [
            {
                "role": "system",
                "content": DECISION_PROMPT,
            },
            {
                "role": "user",
                "content": json.dumps(request, separators=(",", ":")),
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
    with urllib.request.urlopen(http_request, timeout=provider_timeout_seconds()) as response:
        try:
            response_data = json.loads(response.read().decode("utf-8"))
        except json.JSONDecodeError as exc:
            raise ProviderResponseError("provider returned invalid JSON") from exc

    return parse_provider_chat_response(response_data, request)


def provider_model_arg(provider):
    if provider == "grok_cli":
        return os.environ.get("YGO_LLM_BROKER_GROK_MODEL") or os.environ.get(
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


def build_opencode_cli_args(prompt):
    args = command_tokens(cli_provider_command("opencode_cli"))
    args.extend(["run", "--pure"])
    model = provider_model_arg("opencode_cli")
    if model:
        args.extend(["--model", model])
    args.extend(extra_cli_args("opencode_cli"))
    args.append(prompt)
    return args


def cli_provider_response(provider, request):
    prompt = build_decision_prompt(request)
    if provider == "grok_cli":
        args = build_grok_cli_args(prompt)
    elif provider == "opencode_cli":
        args = build_opencode_cli_args(prompt)
    else:
        raise ProviderResponseError("provider '%s' is not a CLI provider" % provider)

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
        raise ProviderResponseError(
            "%s timed out after %.1f seconds" % (provider, cli_timeout_seconds())
        ) from exc
    except OSError as exc:
        raise ProviderResponseError("%s failed to start: %s" % (provider, exc)) from exc

    output = (completed.stdout or "").strip()
    if completed.stderr:
        output = (output + "\n" + completed.stderr.strip()).strip()
    if completed.returncode != 0:
        detail = output or "no output"
        raise ProviderResponseError(
            "%s exited with code %d: %s" % (provider, completed.returncode, detail)
        )
    if not output:
        raise ProviderResponseError("%s produced no output" % provider)
    return parse_provider_decision(output, request)


def choose_response(request):
    response = provider_response(request)
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
        "schema_version": 3,
        "run_effect_seq": 42,
        "public_state": {"players": []},
        "legal_actions": [
            {"action_id": 0, "kind": "move_phase", "phase": "Battle", "phase_id": 3},
            {"action_id": 1, "kind": "command", "command": "Attack", "command_id": 0},
        ],
    }
    response = deterministic_response(request)
    assert response["run_effect_seq"] == 42
    assert response["action_id"] == 1

    dialog_request = {
        "kind": "decision_request",
        "schema_version": 3,
        "run_effect_seq": 43,
        "public_state": {"players": []},
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
        "schema_version": 3,
        "run_effect_seq": 44,
        "public_state": {"players": []},
        "legal_actions": [
            {"action_id": 5, "kind": "list_index", "index": 1},
        ],
    }
    provider_text = (
        "Ignore this example {\"run_effect_seq\":0,\"action_id\":999}. "
        "Use this decision {\"run_effect_seq\":44,\"action_id\":5,\"reason\":\"valid\"}."
    )
    provider_decision = parse_provider_decision(provider_text, provider_request)
    assert provider_decision["run_effect_seq"] == 44
    assert provider_decision["action_id"] == 5
    assert provider_decision["reason"] == "valid"

    stale_tail_text = (
        "{\"run_effect_seq\":44,\"action_id\":5,\"reason\":\"valid\"} "
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
                        {"type": "text", "text": "\"action_id\":5}"},
                    ]
                }
            }
        ]
    }
    provider_decision = parse_provider_chat_response(block_response, provider_request)
    assert provider_decision["run_effect_seq"] == 44
    assert provider_decision["action_id"] == 5

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

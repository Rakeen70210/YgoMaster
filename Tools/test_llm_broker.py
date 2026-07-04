import importlib.util
import json
import os
import socket
import threading
import types
import unittest
import urllib.request
from http.server import HTTPServer
from pathlib import Path
from unittest import mock


MODULE_PATH = Path(__file__).with_name("llm_broker.py")
spec = importlib.util.spec_from_file_location("llm_broker", MODULE_PATH)
broker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(broker)


class LlmBrokerTests(unittest.TestCase):
    def decision_request(self):
        return {
            "kind": "decision_request",
            "schema_version": 3,
            "run_effect_seq": 44,
            "acting_player": 1,
            "controlled_player": 1,
            "public_state": {"players": []},
            "legal_actions": [
                {"action_id": 5, "kind": "move_phase", "phase": "End", "phase_id": 5},
                {
                    "action_id": 7,
                    "kind": "command",
                    "command": "Summon",
                    "command_id": 0,
                    "card": {"name": "Cubic Seed", "text": "Starts the Cubic line."},
                },
            ],
        }

    def test_build_decision_prompt_requests_tactical_card_specific_response(self):
        prompt = broker.build_decision_prompt(self.decision_request())

        self.assertIn("confidence", prompt)
        self.assertIn("plan", prompt)
        self.assertIn("do not end the phase", prompt.lower())
        self.assertIn("Cubic Seed", prompt)

    def test_provider_decision_preserves_confidence_and_plan(self):
        provider_decision = broker.parse_provider_decision(
            (
                '{"run_effect_seq":44,"action_id":7,'
                '"reason":"Normal Summon Cubic Seed to develop.",'
                '"confidence":0.74,"plan":"Use Cubic Seed before ending."}'
            ),
            self.decision_request(),
        )

        self.assertEqual(provider_decision["run_effect_seq"], 44)
        self.assertEqual(provider_decision["action_id"], 7)
        self.assertEqual(provider_decision["reason"], "Normal Summon Cubic Seed to develop.")
        self.assertEqual(provider_decision["confidence"], 0.74)
        self.assertEqual(provider_decision["plan"], "Use Cubic Seed before ending.")

    def test_decision_json_schema_allows_confidence_and_plan(self):
        schema = json.loads(broker.DECISION_JSON_SCHEMA)

        self.assertIn("confidence", schema["properties"])
        self.assertIn("plan", schema["properties"])

    def test_health_endpoint_accepts_query_string(self):
        with socket.socket() as socket_probe:
            socket_probe.bind(("127.0.0.1", 0))
            port = socket_probe.getsockname()[1]

        server = HTTPServer(("127.0.0.1", port), broker.BrokerHandler)
        thread = threading.Thread(target=server.serve_forever)
        thread.start()
        try:
            with urllib.request.urlopen(
                "http://127.0.0.1:%d/health?probe=1" % port,
                timeout=2,
            ) as response:
                payload = json.loads(response.read().decode("utf-8"))

            self.assertEqual(response.status, 200)
            self.assertEqual(payload["service"], "ygomaster_llm_broker")
            self.assertEqual(payload["status"], "ok")
        finally:
            server.shutdown()
            thread.join(timeout=2)
            server.server_close()

    def test_grok_cli_provider_invokes_single_turn_json_command(self):
        calls = []

        def fake_run(args, input, text, capture_output, timeout, check):
            calls.append(
                {
                    "args": args,
                    "input": input,
                    "text": text,
                    "capture_output": capture_output,
                    "timeout": timeout,
                    "check": check,
                }
            )
            return types.SimpleNamespace(
                returncode=0,
                stdout='{"run_effect_seq":44,"action_id":7,"reason":"cli"}',
                stderr="",
            )

        original_subprocess = getattr(broker, "subprocess", None)
        broker.subprocess = types.SimpleNamespace(run=fake_run, TimeoutExpired=TimeoutError)
        try:
            with mock.patch.dict(
                os.environ,
                {
                    "YGO_LLM_BROKER_PROVIDER": "grok_cli",
                    "YGO_LLM_BROKER_GROK_COMMAND": "/usr/bin/grok",
                    "YGO_LLM_BROKER_CLI_TIMEOUT": "12",
                },
                clear=False,
            ):
                response = broker.provider_response(self.decision_request())
        finally:
            if original_subprocess is None:
                del broker.subprocess
            else:
                broker.subprocess = original_subprocess

        self.assertEqual(response["run_effect_seq"], 44)
        self.assertEqual(response["action_id"], 7)
        self.assertEqual(response["reason"], "cli")
        self.assertEqual(calls[0]["args"][0], "/usr/bin/grok")
        self.assertIn("--single", calls[0]["args"])
        self.assertIn("--json-schema", calls[0]["args"])
        self.assertIn('"legal_actions"', " ".join(calls[0]["args"]))
        self.assertEqual(calls[0]["input"], None)
        self.assertEqual(calls[0]["timeout"], 12.0)

    def test_opencode_cli_provider_invokes_run_command(self):
        calls = []

        def fake_run(args, input, text, capture_output, timeout, check):
            calls.append(
                {
                    "args": args,
                    "input": input,
                    "text": text,
                    "capture_output": capture_output,
                    "timeout": timeout,
                    "check": check,
                }
            )
            return types.SimpleNamespace(
                returncode=0,
                stdout='Final: {"run_effect_seq":44,"action_id":5,"reason":"opencode"}',
                stderr="",
            )

        original_subprocess = getattr(broker, "subprocess", None)
        broker.subprocess = types.SimpleNamespace(run=fake_run, TimeoutExpired=TimeoutError)
        try:
            with mock.patch.dict(
                os.environ,
                {
                    "YGO_LLM_BROKER_PROVIDER": "opencode_cli",
                    "YGO_LLM_BROKER_OPENCODE_COMMAND": "/usr/bin/opencode",
                    "YGO_LLM_BROKER_OPENCODE_MODEL": "opencode/grok-code-fast-1",
                },
                clear=False,
            ):
                response = broker.provider_response(self.decision_request())
        finally:
            if original_subprocess is None:
                del broker.subprocess
            else:
                broker.subprocess = original_subprocess

        self.assertEqual(response["run_effect_seq"], 44)
        self.assertEqual(response["action_id"], 5)
        self.assertEqual(response["reason"], "opencode")
        self.assertEqual(calls[0]["args"][0], "/usr/bin/opencode")
        self.assertEqual(calls[0]["args"][1], "run")
        self.assertIn("--model", calls[0]["args"])
        self.assertIn("opencode/grok-code-fast-1", calls[0]["args"])
        self.assertEqual(calls[0]["input"], None)

    def test_health_reports_selected_cli_provider_as_configured_when_command_exists(self):
        with mock.patch.dict(
            os.environ,
            {
                "YGO_LLM_BROKER_PROVIDER": "grok_cli",
                "YGO_LLM_BROKER_GROK_COMMAND": "/usr/bin/grok",
            },
            clear=False,
        ), mock.patch.object(broker, "command_exists", return_value=True):
            health = broker.broker_health_response()

        self.assertEqual(health["provider"], "grok_cli")
        self.assertEqual(health["provider_configured"], True)


if __name__ == "__main__":
    unittest.main()

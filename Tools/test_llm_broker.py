import importlib.util
import json
import os
import socket
import tempfile
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
            "schema_version": 4,
            "run_effect_seq": 44,
            "acting_player": 1,
            "controlled_player": 1,
            "opponent_context": {
                "opponent_player": 0,
                "life_points": 6200,
                "field_count": 1,
                "graveyard_count": 1,
                "known_public_threats": [
                    {"card_id": 5702, "name": "Known Grave Threat", "zone": "graveyard"}
                ],
                "summary": "opponent has 1 field card(s); identities unavailable",
            },
            "public_state": {"players": []},
            "turn_memory": {"recent_actions": [], "phase_plan": ""},
            "duel_history": {
                "history_version": 1,
                "last_event_id": 21,
                "first_detailed_event_id": 18,
                "history_compacted": False,
                "events": [
                    {
                        "event_id": 18,
                        "run_effect_seq": 126,
                        "turn": 2,
                        "phase": "Main1",
                        "actor_player": 0,
                        "kind": "normal_summon",
                        "card_id": 1234,
                        "card_name": "Public Monster",
                        "source_zone": "hand",
                        "destination_zone": "monster_zone",
                        "evidence": "accepted_command",
                    },
                    {
                        "event_id": 20,
                        "run_effect_seq": 139,
                        "turn": 2,
                        "phase": "Main1",
                        "actor_player": 0,
                        "kind": "activate_effect",
                        "card_id": 1234,
                        "card_name": "Public Monster",
                        "evidence": "accepted_command",
                    },
                    {
                        "event_id": 21,
                        "run_effect_seq": 145,
                        "turn": 2,
                        "phase": "Main1",
                        "actor_player": 0,
                        "kind": "card_moved",
                        "card_id": 5678,
                        "card_name": "Public Grave Card",
                        "source_zone": "deck",
                        "destination_zone": "graveyard",
                        "evidence": "public_state_delta",
                    },
                ],
                "prior_turn_summaries": [],
                "revealed_card_context": [
                    {
                        "card_id": 1234,
                        "name": "Public Monster",
                        "text": "Synthetic public card text.",
                    }
                ],
            },
            "legal_actions": [
                {"action_id": 5, "kind": "move_phase", "phase": "End", "phase_id": 5},
                {
                    "action_id": 7,
                    "kind": "command",
                    "command": "Summon",
                    "command_id": 0,
                    "action_label": "Summon Cubic Seed",
                    "action_group": "summon",
                    "is_mechanical": False,
                    "strategic_role": "board_development",
                    "consequence_hint": "normal_summon_consumes_turn_summon",
                    "card": {"name": "Cubic Seed", "text": "Starts the Cubic line."},
                },
            ],
        }

    def schema_v4_provider_fields(self):
        """Canonical schema-v4 provider fields required by strict parse/adapters."""
        return {
            "opponent_action_assessment": (
                "The opponent used event 18 to establish a monster, event 20 to use its "
                "effect, and event 21 to load the graveyard."
            ),
            "history_event_ids_used": [18, 20, 21],
        }

    def provider_decision_json(self, **overrides):
        payload = {
            "run_effect_seq": 44,
            "action_id": 7,
            "reason": "Normal Summon Cubic Seed to develop.",
            "confidence": 0.74,
            "plan": "Use Cubic Seed before ending.",
            "why_now": "Use the Normal Summon before considering phase changes.",
            "alternatives_considered": [
                "End Phase gives up tempo.",
                "Move to Battle has no attacker.",
            ],
            "risk": "Cubic Seed may be vulnerable before follow-up.",
            "opponent_board_assessment": (
                "Opponent has one unknown field card and a known grave threat."
            ),
            "opponent_action_assessment": (
                "The opponent used event 18 to establish a monster, event 20 to use its "
                "effect, and event 21 to load the graveyard."
            ),
            "history_event_ids_used": [18, 20, 21],
        }
        payload.update(overrides)
        return json.dumps(payload, separators=(",", ":"))

    def test_build_decision_prompt_requests_tactical_card_specific_response(self):
        prompt = broker.build_decision_prompt(self.decision_request())

        self.assertIn("confidence", prompt)
        self.assertIn("plan", prompt)
        self.assertIn("why_now", prompt)
        self.assertIn("alternatives_considered", prompt)
        self.assertIn("risk", prompt)
        self.assertIn("opponent_board_assessment", prompt)
        self.assertIn("Known Grave Threat", prompt)
        self.assertIn("opponent has 1 field card", prompt)
        self.assertIn("do not end the phase", prompt.lower())
        self.assertIn("action_label", prompt)
        self.assertIn("strategic_role", prompt)
        self.assertIn("ignore actions marked is_mechanical", prompt.lower())
        self.assertIn("Cubic Seed", prompt)
        # Schema-v4 history grounding (exact semantics).
        self.assertIn("duel_history", prompt)
        self.assertIn("history_event_ids_used", prompt)
        self.assertIn("Read duel_history before choosing", prompt)
        self.assertIn(
            "Use history_event_ids_used to identify the public events that materially affected the decision",
            prompt,
        )
        self.assertIn(
            "Infer the opponent's plan only from those events and current public state",
            prompt,
        )
        self.assertIn(
            "Never invent a facedown or hidden-zone identity",
            prompt,
        )
        self.assertTrue(
            "fact" in prompt.lower() and "inference" in prompt.lower(),
            "prompt must separate fact from inference",
        )

    def test_attack_target_prompt_requires_replanning_from_interaction_origin(self):
        request = self.decision_request()
        request["run_effect_seq"] = 539
        request["interaction_origin"] = {
            "kind": "attack",
            "run_effect_seq": 534,
            "action_label": "Attack: Chaosrider Gustaph",
            "reason": "Attack Duza to remove the public threat.",
        }
        request["legal_actions"] = [
            {
                "action_id": 0,
                "kind": "command",
                "action_label": "Attack target: Duza the Meteor Cubic Vessel",
                "target_scope": "attack_target",
                "target_token": "attack:7:534:1:0:501:0:2:0:701",
            },
            {
                "action_id": 1,
                "kind": "command",
                "action_label": "Attack target: face-down monster in zone 4",
                "target_scope": "attack_target",
                "target_token": "attack:7:534:1:0:501:0:4:0:0",
            },
        ]

        prompt = broker.build_decision_prompt(request)
        self.assertIn("interaction_origin", prompt)
        self.assertIn("re-evaluate every current legal attack target", prompt)
        self.assertIn("never invent a face-down target identity", prompt)

    def test_prompt_treats_grounded_effect_applicability_as_authoritative(self):
        request = self.decision_request()
        request["legal_actions"][1]["effect_applicability"] = {
            "is_grounded": True,
            "effect_expected_to_apply": False,
            "reason": "blocked_by_activated_monster_effect_immunity",
            "source_original_atk": 1600,
            "source_original_atk_at_most": 3000,
        }

        prompt = broker.build_decision_prompt(request)
        self.assertIn("effect_applicability", prompt)
        self.assertIn("authoritative", prompt.lower())
        self.assertIn("must not select", prompt.lower())

    def test_provider_decision_preserves_structured_intended_followups(self):
        payload = json.loads(self.provider_decision_json())
        payload["intended_followups"] = [
            {
                "action_family": "effect_activation",
                "card_id": 11263,
                "card_name": "Castel, the Skyblaster Musketeer",
                "description": "detach two to shuffle",
            }
        ]
        parsed = broker.parse_provider_decision(json.dumps(payload), self.decision_request())
        self.assertEqual(parsed["intended_followups"], payload["intended_followups"])

        schema = json.loads(broker.DECISION_JSON_SCHEMA)
        self.assertIn("intended_followups", schema["required"])
        self.assertEqual(
            schema["properties"]["intended_followups"]["items"]["properties"]
            ["action_family"]["type"],
            "string",
        )

    def test_build_decision_prompt_includes_matching_strategy_hint(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            hint_path = Path(temp_dir) / "LlmStrategyHints.json"
            hint_path.write_text(
                json.dumps(
                    {
                        "hints": [
                            {
                                "deck_name": "Cubic",
                                "cards": ["Cubic Seed"],
                                "game_plan": "Normal Summon a Cubic starter before ending.",
                                "starter_cards": ["Cubic Seed"],
                                "cards_to_avoid_wasting": ["Cubic Karma"],
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )

            with mock.patch.dict(
                os.environ,
                {"YGO_LLM_BROKER_STRATEGY_HINTS": str(hint_path)},
                clear=False,
            ):
                prompt = broker.build_decision_prompt(self.decision_request())

        self.assertIn("strategy_hints", prompt)
        self.assertIn("Normal Summon a Cubic starter", prompt)
        self.assertIn("Cubic Karma", prompt)

    def test_build_decision_prompt_omits_nonmatching_strategy_hint(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            hint_path = Path(temp_dir) / "LlmStrategyHints.json"
            hint_path.write_text(
                json.dumps(
                    [
                        {
                            "deck_name": "Blue-Eyes",
                            "cards": ["Blue-Eyes White Dragon"],
                            "game_plan": "Summon a large normal monster.",
                        }
                    ]
                ),
                encoding="utf-8",
            )

            with mock.patch.dict(
                os.environ,
                {"YGO_LLM_BROKER_STRATEGY_HINTS": str(hint_path)},
                clear=False,
            ):
                prompt = broker.build_decision_prompt(self.decision_request())

        self.assertNotIn("Blue-Eyes", prompt)

    def test_provider_decision_preserves_tactical_audit_fields(self):
        provider_decision = broker.parse_provider_decision(
            self.provider_decision_json(),
            self.decision_request(),
        )

        self.assertEqual(provider_decision["run_effect_seq"], 44)
        self.assertEqual(provider_decision["action_id"], 7)
        self.assertEqual(provider_decision["reason"], "Normal Summon Cubic Seed to develop.")
        self.assertEqual(provider_decision["confidence"], 0.74)
        self.assertEqual(provider_decision["plan"], "Use Cubic Seed before ending.")
        self.assertEqual(
            provider_decision["why_now"],
            "Use the Normal Summon before considering phase changes.",
        )
        self.assertEqual(
            provider_decision["alternatives_considered"],
            ["End Phase gives up tempo.", "Move to Battle has no attacker."],
        )
        self.assertEqual(
            provider_decision["risk"],
            "Cubic Seed may be vulnerable before follow-up.",
        )
        self.assertEqual(
            provider_decision["opponent_board_assessment"],
            "Opponent has one unknown field card and a known grave threat.",
        )
        self.assertEqual(
            provider_decision["opponent_action_assessment"],
            self.schema_v4_provider_fields()["opponent_action_assessment"],
        )
        self.assertEqual(provider_decision["history_event_ids_used"], [18, 20, 21])

    def test_provider_decision_rejects_missing_opponent_assessment(self):
        with self.assertRaises(broker.ProviderResponseError) as ctx:
            broker.parse_provider_decision(
                (
                    '{"run_effect_seq":44,"action_id":7,'
                    '"reason":"Normal Summon Cubic Seed."}'
                ),
                self.decision_request(),
            )

        message = str(ctx.exception)
        self.assertTrue(
            "opponent_board_assessment" in message
            or "opponent_action_assessment" in message
            or "history_event_ids_used" in message,
            message,
        )

    def test_provider_decision_rejects_missing_opponent_action_assessment(self):
        payload = json.loads(self.provider_decision_json())
        del payload["opponent_action_assessment"]
        with self.assertRaises(broker.ProviderResponseError) as ctx:
            broker.parse_provider_decision(
                json.dumps(payload, separators=(",", ":")),
                self.decision_request(),
            )
        self.assertIn("opponent_action_assessment", str(ctx.exception))

    def test_provider_decision_rejects_missing_history_event_ids_used(self):
        payload = json.loads(self.provider_decision_json())
        del payload["history_event_ids_used"]
        with self.assertRaises(broker.ProviderResponseError) as ctx:
            broker.parse_provider_decision(
                json.dumps(payload, separators=(",", ":")),
                self.decision_request(),
            )
        self.assertIn("history_event_ids_used", str(ctx.exception))

    def test_provider_decision_accepts_empty_history_event_ids_used(self):
        provider_decision = broker.parse_provider_decision(
            self.provider_decision_json(history_event_ids_used=[]),
            self.decision_request(),
        )
        self.assertEqual(provider_decision["history_event_ids_used"], [])
        self.assertIn("opponent_action_assessment", provider_decision)

    def test_provider_decision_rejects_non_list_history_event_ids_used(self):
        with self.assertRaises(broker.ProviderResponseError) as ctx:
            broker.parse_provider_decision(
                self.provider_decision_json(history_event_ids_used="18"),
                self.decision_request(),
            )
        self.assertIn("history_event_ids_used", str(ctx.exception))

    def test_provider_decision_rejects_non_int_history_event_id_elements(self):
        with self.assertRaises(broker.ProviderResponseError) as ctx:
            broker.parse_provider_decision(
                self.provider_decision_json(history_event_ids_used=[18, "20"]),
                self.decision_request(),
            )
        self.assertIn("history_event_ids_used", str(ctx.exception))

    def test_provider_decision_rejects_non_string_opponent_action_assessment(self):
        with self.assertRaises(broker.ProviderResponseError) as ctx:
            broker.parse_provider_decision(
                self.provider_decision_json(opponent_action_assessment=12),
                self.decision_request(),
            )
        self.assertIn("opponent_action_assessment", str(ctx.exception))

    def test_deterministic_response_summarizes_opponent_context(self):
        response = broker.deterministic_response(self.decision_request())

        self.assertIn(
            "opponent has 1 field card(s); identities unavailable",
            response["opponent_board_assessment"],
        )
        self.assertIn("Known Grave Threat in graveyard", response["opponent_board_assessment"])
        self.assertIn("opponent_action_assessment", response)
        self.assertIsInstance(response["opponent_action_assessment"], str)
        self.assertIn("history_event_ids_used", response)
        self.assertIsInstance(response["history_event_ids_used"], list)

    def test_provider_decision_reads_cli_structured_output_wrapper(self):
        try:
            provider_decision = broker.parse_provider_decision(
                json.dumps(
                    {
                        "text": (
                            '{"run_effect_seq":44,"action_id":7,'
                            '"reason":"Normal Summon Cubic Seed."}'
                        ),
                        "structuredOutput": {
                            "run_effect_seq": 44,
                            "action_id": 7,
                            "reason": "Normal Summon Cubic Seed.",
                            "confidence": 0.8,
                            "plan": "Develop the board.",
                            "why_now": "Use the normal summon this main phase.",
                            "alternatives_considered": ["End Phase passes without tempo."],
                            "risk": "The summon can be answered.",
                            "opponent_board_assessment": "Opponent has one visible threat.",
                            "opponent_action_assessment": (
                                "Opponent public events 18/20/21 informed the choice."
                            ),
                            "history_event_ids_used": [18, 20],
                        },
                    }
                ),
                self.decision_request(),
            )
        except broker.ProviderResponseError as exc:
            self.fail("CLI structuredOutput wrapper was not parsed: %s" % exc)

        self.assertEqual(provider_decision["run_effect_seq"], 44)
        self.assertEqual(provider_decision["action_id"], 7)
        self.assertEqual(provider_decision["reason"], "Normal Summon Cubic Seed.")
        self.assertEqual(provider_decision["confidence"], 0.8)
        self.assertEqual(
            provider_decision["opponent_action_assessment"],
            "Opponent public events 18/20/21 informed the choice.",
        )
        self.assertEqual(provider_decision["history_event_ids_used"], [18, 20])

    def test_decision_json_schema_allows_tactical_audit_fields(self):
        schema = json.loads(broker.DECISION_JSON_SCHEMA)

        self.assertIn("confidence", schema["properties"])
        self.assertIn("plan", schema["properties"])
        self.assertIn("why_now", schema["properties"])
        self.assertIn("alternatives_considered", schema["properties"])
        self.assertIn("opponent_board_assessment", schema["properties"])
        self.assertIn("opponent_action_assessment", schema["properties"])
        self.assertIn("history_event_ids_used", schema["properties"])
        self.assertIn("why_now", schema["required"])
        self.assertIn("alternatives_considered", schema["required"])
        self.assertIn("risk", schema["required"])
        self.assertIn("opponent_board_assessment", schema["required"])
        self.assertIn("opponent_action_assessment", schema["required"])
        self.assertIn("history_event_ids_used", schema["required"])
        self.assertEqual(
            schema["properties"]["alternatives_considered"]["items"]["type"],
            "string",
        )
        self.assertEqual(
            schema["properties"]["history_event_ids_used"]["type"],
            "array",
        )
        self.assertEqual(
            schema["properties"]["history_event_ids_used"]["items"]["type"],
            "integer",
        )
        self.assertIn("risk", schema["properties"])

    def test_slice5_schema_v4_fixtures_include_history_and_strict_audit_fields(self):
        """Slice 5: live fixtures stay schema-v4 with history + audit response fields."""
        request = self.decision_request()
        self.assertEqual(4, request["schema_version"])
        self.assertIn("duel_history", request)
        history = request["duel_history"]
        self.assertIn("events", history)
        self.assertIn("prior_turn_summaries", history)
        self.assertIn("history_compacted", history)
        self.assertIn("first_detailed_event_id", history)
        self.assertIn("last_event_id", history)

        response = broker.deterministic_response(request)
        self.assertIn("opponent_action_assessment", response)
        self.assertIn("history_event_ids_used", response)
        self.assertIsInstance(response["history_event_ids_used"], list)
        self.assertIn("opponent_board_assessment", response)

        # Strict parse still requires both fields (empty citations allowed).
        parsed = broker.parse_provider_decision(
            self.provider_decision_json(history_event_ids_used=[]),
            request,
        )
        self.assertEqual([], parsed["history_event_ids_used"])
        self.assertIn("opponent_action_assessment", parsed)

        schema = json.loads(broker.DECISION_JSON_SCHEMA)
        self.assertIn("opponent_action_assessment", schema["required"])
        self.assertIn("history_event_ids_used", schema["required"])

    def test_schema_v3_replay_fixtures_require_explicit_compatibility_path(self):
        v3_request = {
            "kind": "decision_request",
            "schema_version": 3,
            "run_effect_seq": 44,
            "public_state": {"players": []},
            "legal_actions": [
                {"action_id": 7, "kind": "command", "command": "Summon"},
            ],
        }
        # Live path must not silently treat schema v3 as v4.
        live_version = getattr(broker, "LIVE_SCHEMA_VERSION", None)
        if live_version is None:
            live_version = getattr(broker, "SCHEMA_VERSION", None)
        self.assertEqual(
            live_version,
            4,
            "live broker schema constant must be 4 (not silently still 3)",
        )
        migrate = getattr(broker, "migrate_schema_v3_request", None) or getattr(
            broker, "accept_schema_v3_replay_fixture", None
        )
        self.assertTrue(
            callable(migrate),
            "schema-v3 fixtures require explicit migrate_schema_v3_request "
            "or accept_schema_v3_replay_fixture",
        )
        migrated = migrate(v3_request)
        self.assertIsInstance(migrated, dict)
        self.assertEqual(migrated.get("schema_version"), 4)
        self.assertIn("duel_history", migrated)

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
                stdout=(
                    '{"run_effect_seq":44,"action_id":7,"reason":"cli",'
                    '"opponent_board_assessment":"Opponent context accounted for.",'
                    '"opponent_action_assessment":"Public events 18/20 informed the choice.",'
                    '"history_event_ids_used":[18,20]}'
                ),
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
                    "YGO_LLM_BROKER_GROK_MODEL": "grok-4.5",
                    "YGO_LLM_BROKER_GROK_ARGS": "--reasoning-effort low",
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
        self.assertEqual(
            response["opponent_action_assessment"],
            "Public events 18/20 informed the choice.",
        )
        self.assertEqual(response["history_event_ids_used"], [18, 20])
        self.assertEqual(calls[0]["args"][0], "/usr/bin/grok")
        self.assertIn("--single", calls[0]["args"])
        self.assertIn("--json-schema", calls[0]["args"])
        self.assertIn("--model", calls[0]["args"])
        self.assertIn("grok-4.5", calls[0]["args"])
        self.assertIn("--reasoning-effort", calls[0]["args"])
        self.assertIn("low", calls[0]["args"])
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
                stdout=(
                    'Final: {"run_effect_seq":44,"action_id":5,"reason":"opencode",'
                    '"opponent_board_assessment":"Opponent context accounted for.",'
                    '"opponent_action_assessment":"Public history considered.",'
                    '"history_event_ids_used":[18]}'
                ),
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
                    "YGO_LLM_BROKER_OPENCODE_MODEL": "xai/grok-4.5",
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
        self.assertEqual(response["history_event_ids_used"], [18])
        self.assertEqual(calls[0]["args"][0], "/usr/bin/opencode")
        self.assertEqual(calls[0]["args"][1], "run")
        self.assertIn("--model", calls[0]["args"])
        self.assertIn("xai/grok-4.5", calls[0]["args"])
        self.assertEqual(calls[0]["input"], None)

    def test_agy_cli_provider_invokes_print_command(self):
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
                stdout=(
                    '{"run_effect_seq":44,"action_id":5,"reason":"agy",'
                    '"opponent_board_assessment":"Opponent context accounted for.",'
                    '"opponent_action_assessment":"Public history considered.",'
                    '"history_event_ids_used":[]}'
                ),
                stderr="",
            )

        original_subprocess = getattr(broker, "subprocess", None)
        broker.subprocess = types.SimpleNamespace(run=fake_run, TimeoutExpired=TimeoutError)
        try:
            with mock.patch.dict(
                os.environ,
                {
                    "YGO_LLM_BROKER_PROVIDER": "agy_cli",
                    "YGO_LLM_BROKER_AGY_COMMAND": "/usr/bin/agy",
                    "YGO_LLM_BROKER_AGY_MODEL": "google/gemini-3.5-flash",
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
        self.assertEqual(response["reason"], "agy")
        self.assertEqual(response["history_event_ids_used"], [])
        self.assertEqual(calls[0]["args"][0], "/usr/bin/agy")
        self.assertIn("--print", calls[0]["args"])
        self.assertIn("--prompt", calls[0]["args"])
        self.assertIn("--model", calls[0]["args"])
        self.assertIn("google/gemini-3.5-flash", calls[0]["args"])
        self.assertEqual(calls[0]["input"], None)

    def test_cli_provider_retries_transient_failure_then_uses_legal_fallback(self):
        calls = []

        def fake_run(args, input, text, capture_output, timeout, check):
            calls.append(args)
            return types.SimpleNamespace(
                returncode=1,
                stdout="",
                stderr=(
                    'Error: {"name":"UnknownError","data":{"message":'
                    '"Unexpected server error. Check server logs for details.",'
                    '"ref":"err_43cd3b3b"}}'
                ),
            )

        original_subprocess = getattr(broker, "subprocess", None)
        broker.subprocess = types.SimpleNamespace(run=fake_run, TimeoutExpired=TimeoutError)
        try:
            with mock.patch.dict(
                os.environ,
                {
                    "YGO_LLM_BROKER_PROVIDER": "opencode_cli",
                    "YGO_LLM_BROKER_OPENCODE_COMMAND": "/usr/bin/opencode",
                    "YGO_LLM_BROKER_CLI_RETRIES": "1",
                    "YGO_LLM_BROKER_PROVIDER_ERROR_FALLBACK": "1",
                },
                clear=False,
            ):
                try:
                    response = broker.choose_response(self.decision_request())
                except broker.ProviderResponseError as exc:
                    self.fail("CLI provider failure escaped instead of using fallback: %s" % exc)
        finally:
            if original_subprocess is None:
                del broker.subprocess
            else:
                broker.subprocess = original_subprocess

        self.assertEqual(response["run_effect_seq"], 44)
        self.assertEqual(response["action_id"], 7)
        self.assertEqual(response["reason"], "deterministic fallback")
        self.assertEqual(len(calls), 2)

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

    def test_explicit_provider_failure_raises_by_default(self):
        def fake_run(args, input, text, capture_output, timeout, check):
            return types.SimpleNamespace(
                returncode=1,
                stdout="",
                stderr="provider unavailable",
            )

        original_subprocess = getattr(broker, "subprocess", None)
        broker.subprocess = types.SimpleNamespace(run=fake_run, TimeoutExpired=TimeoutError)
        try:
            with mock.patch.dict(
                os.environ,
                {
                    "YGO_LLM_BROKER_PROVIDER": "opencode_cli",
                    "YGO_LLM_BROKER_OPENCODE_COMMAND": "/usr/bin/opencode",
                },
                clear=True,
            ):
                with self.assertRaises(broker.ProviderResponseError):
                    broker.choose_response(self.decision_request())
        finally:
            if original_subprocess is None:
                del broker.subprocess
            else:
                broker.subprocess = original_subprocess

    def test_health_reports_agy_provider_as_configured_when_command_exists(self):
        with mock.patch.dict(
            os.environ,
            {
                "YGO_LLM_BROKER_PROVIDER": "agy_cli",
                "YGO_LLM_BROKER_AGY_COMMAND": "/usr/bin/agy",
            },
            clear=False,
        ), mock.patch.object(broker, "command_exists", return_value=True):
            health = broker.broker_health_response()

        self.assertEqual(health["provider"], "agy_cli")
        self.assertEqual(health["provider_configured"], True)

    def test_reasoning_log_path_disabled_by_default(self):
        with mock.patch.dict(os.environ, {}, clear=True):
            self.assertIsNone(broker.reasoning_log_path())

        with mock.patch.dict(
            os.environ,
            {"YGO_LLM_BROKER_REASONING_LOG": "0"},
            clear=False,
        ):
            self.assertIsNone(broker.reasoning_log_path())

    def test_reasoning_log_path_accepts_flag_and_custom_path(self):
        with mock.patch.dict(
            os.environ,
            {"YGO_LLM_BROKER_REASONING_LOG": "1"},
            clear=False,
        ):
            self.assertEqual(
                broker.reasoning_log_path(),
                broker.DEFAULT_REASONING_LOG_PATH,
            )

        with mock.patch.dict(
            os.environ,
            {"YGO_LLM_BROKER_REASONING_LOG": "/tmp/custom-reasoning.jsonl"},
            clear=False,
        ):
            self.assertEqual(
                broker.reasoning_log_path(),
                "/tmp/custom-reasoning.jsonl",
            )

    def test_cli_provider_writes_full_raw_reasoning_log(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            log_path = Path(temp_dir) / "LlmReasoningLog.jsonl"
            raw_stdout = (
                "thinking about board development...\n"
                '{"run_effect_seq":44,"action_id":7,"reason":"cli",'
                '"opponent_board_assessment":"Opponent context accounted for.",'
                '"opponent_action_assessment":"Public events 18/20 informed the choice.",'
                '"history_event_ids_used":[18,20],'
                '"why_now":"summon now","alternatives_considered":["end phase"],'
                '"risk":"removal"}'
            )

            def fake_run(args, input, text, capture_output, timeout, check):
                return types.SimpleNamespace(
                    returncode=0,
                    stdout=raw_stdout,
                    stderr="debug: model thinking trace",
                )

            original_subprocess = getattr(broker, "subprocess", None)
            broker.subprocess = types.SimpleNamespace(
                run=fake_run, TimeoutExpired=TimeoutError
            )
            try:
                with mock.patch.dict(
                    os.environ,
                    {
                        "YGO_LLM_BROKER_PROVIDER": "grok_cli",
                        "YGO_LLM_BROKER_GROK_COMMAND": "/usr/bin/grok",
                        "YGO_LLM_BROKER_REASONING_LOG": str(log_path),
                    },
                    clear=False,
                ):
                    response = broker.provider_response(self.decision_request())
            finally:
                if original_subprocess is None:
                    del broker.subprocess
                else:
                    broker.subprocess = original_subprocess

            self.assertEqual(response["action_id"], 7)
            self.assertTrue(log_path.is_file())
            records = [
                json.loads(line)
                for line in log_path.read_text(encoding="utf-8").splitlines()
                if line.strip()
            ]
            self.assertEqual(len(records), 1)
            record = records[0]
            self.assertEqual(record["kind"], "provider_reasoning")
            self.assertEqual(record["provider"], "grok_cli")
            self.assertEqual(record["transport"], "cli")
            self.assertEqual(record["ok"], True)
            self.assertIn("thinking about board development", record["raw_stdout"])
            self.assertEqual(record["raw_stderr"], "debug: model thinking trace")
            self.assertIn("thinking about board development", record["reasoning_text"])
            self.assertEqual(record["parsed"]["action_id"], 7)
            self.assertEqual(record["request"]["run_effect_seq"], 44)
            self.assertNotIn("prompt", record)
            self.assertIn("prompt_chars", record)

    def test_api_provider_logs_structured_reasoning_fields(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            log_path = Path(temp_dir) / "api-reasoning.jsonl"
            api_payload = {
                "choices": [
                    {
                        "message": {
                            "content": (
                                '{"run_effect_seq":44,"action_id":7,"reason":"api",'
                                '"opponent_board_assessment":"no known threats",'
                                '"opponent_action_assessment":"No material public events.",'
                                '"history_event_ids_used":[]}'
                            ),
                            "reasoning_content": "I considered End Phase but Summon develops first.",
                        }
                    }
                ]
            }

            class FakeResponse:
                def read(self):
                    return json.dumps(api_payload).encode("utf-8")

                def __enter__(self):
                    return self

                def __exit__(self, exc_type, exc, tb):
                    return False

            def fake_urlopen(request, timeout=None):
                return FakeResponse()

            original_urlopen = broker.urllib.request.urlopen
            broker.urllib.request.urlopen = fake_urlopen
            try:
                with mock.patch.dict(
                    os.environ,
                    {
                        "YGO_LLM_BROKER_PROVIDER": "deepseek_api",
                        "YGO_LLM_BROKER_API_KEY": "test-key",
                        "YGO_LLM_BROKER_REASONING_LOG": str(log_path),
                    },
                    clear=False,
                ):
                    response = broker.provider_response(self.decision_request())
            finally:
                broker.urllib.request.urlopen = original_urlopen

            self.assertEqual(response["action_id"], 7)
            records = [
                json.loads(line)
                for line in log_path.read_text(encoding="utf-8").splitlines()
                if line.strip()
            ]
            self.assertEqual(len(records), 1)
            record = records[0]
            self.assertEqual(record["ok"], True)
            self.assertEqual(record["transport"], "api")
            self.assertIn(
                "I considered End Phase but Summon develops first.",
                record["reasoning_text"],
            )
            self.assertEqual(
                record["api_response"]["choices"][0]["message"]["reasoning_content"],
                "I considered End Phase but Summon develops first.",
            )

    def test_failed_cli_parse_still_writes_reasoning_log(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            log_path = Path(temp_dir) / "failed.jsonl"

            def fake_run(args, input, text, capture_output, timeout, check):
                return types.SimpleNamespace(
                    returncode=0,
                    stdout="not valid decision json at all",
                    stderr="",
                )

            original_subprocess = getattr(broker, "subprocess", None)
            broker.subprocess = types.SimpleNamespace(
                run=fake_run, TimeoutExpired=TimeoutError
            )
            try:
                with mock.patch.dict(
                    os.environ,
                    {
                        "YGO_LLM_BROKER_PROVIDER": "opencode_cli",
                        "YGO_LLM_BROKER_OPENCODE_COMMAND": "/usr/bin/opencode",
                        "YGO_LLM_BROKER_REASONING_LOG": str(log_path),
                        "YGO_LLM_BROKER_CLI_RETRIES": "0",
                    },
                    clear=False,
                ):
                    with self.assertRaises(broker.ProviderResponseError):
                        broker.provider_response(self.decision_request())
            finally:
                if original_subprocess is None:
                    del broker.subprocess
                else:
                    broker.subprocess = original_subprocess

            records = [
                json.loads(line)
                for line in log_path.read_text(encoding="utf-8").splitlines()
                if line.strip()
            ]
            self.assertEqual(len(records), 1)
            self.assertEqual(records[0]["ok"], False)
            self.assertEqual(records[0]["raw_text"], "not valid decision json at all")
            self.assertIsNotNone(records[0]["error"])


if __name__ == "__main__":
    unittest.main()

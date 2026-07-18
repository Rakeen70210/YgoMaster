import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("evaluate_llm_replay.py")
spec = importlib.util.spec_from_file_location("evaluate_llm_replay", MODULE_PATH)
evaluator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evaluator)


class EvaluateLlmReplayTests(unittest.TestCase):
    def write_log(self, events):
        temp_dir = tempfile.TemporaryDirectory()
        path = Path(temp_dir.name) / "LlmDecisionLog.jsonl"
        with path.open("w", encoding="utf-8") as writer:
            for event in events:
                writer.write(json.dumps(event, separators=(",", ":")) + "\n")
        return temp_dir, path

    def request(self):
        return {
            "kind": "decision_request",
            "schema_version": 4,
            "run_effect_seq": 44,
            "duel_history": {
                "history_version": 1,
                "last_event_id": 0,
                "first_detailed_event_id": 1,
                "history_compacted": False,
                "events": [],
                "prior_turn_summaries": [],
                "revealed_card_context": [],
            },
            "legal_actions": [
                {"action_id": 1, "kind": "move_phase", "phase": "End"},
                {
                    "action_id": 2,
                    "kind": "command",
                    "command": "Summon",
                    "card": {"name": "Cubic Seed", "text": "Starts the Cubic line."},
                },
            ],
        }

    def test_schema_v3_log_fixtures_require_explicit_migration(self):
        v3_request = {
            "kind": "decision_request",
            "schema_version": 3,
            "run_effect_seq": 44,
            "legal_actions": [
                {"action_id": 2, "kind": "command", "command": "Summon"},
            ],
        }
        migrate = getattr(evaluator, "migrate_schema_v3_request", None) or getattr(
            evaluator, "accept_schema_v3_replay_fixture", None
        )
        self.assertTrue(
            callable(migrate),
            "evaluate_llm_replay must expose migrate_schema_v3_request "
            "or accept_schema_v3_replay_fixture for schema-v3 fixtures",
        )
        migrated = migrate(v3_request)
        self.assertEqual(migrated.get("schema_version"), 4)
        self.assertIn("duel_history", migrated)
        # Live extract path must not silently rewrite schema_version without migration.
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "llm_broker_response",
                    "success": True,
                    "request_run_effect_seq": 44,
                    "request_json": v3_request,
                }
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        fixtures = evaluator.extract_requests_from_logs([path])
        self.assertEqual(1, len(fixtures))
        self.assertEqual(3, fixtures[0]["schema_version"])

    def test_extracts_request_fixtures_from_runtime_log(self):
        request = self.request()
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "llm_broker_response",
                    "success": True,
                    "request_run_effect_seq": 44,
                    "request_json": json.dumps(request, separators=(",", ":")),
                },
                {
                    "kind": "llm_broker_response",
                    "success": True,
                    "request_run_effect_seq": 44,
                    "request_json": request,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        fixtures = evaluator.extract_requests_from_logs([path])

        self.assertEqual(1, len(fixtures))
        self.assertEqual(44, fixtures[0]["run_effect_seq"])
        self.assertEqual("decision_request", fixtures[0]["kind"])

    def test_evaluates_provider_decision_quality_and_latency(self):
        request = self.request()

        def fake_decider(_request):
            return {
                "run_effect_seq": 44,
                "action_id": 2,
                "reason": "Normal Summon Cubic Seed to start the Cubic line.",
                "confidence": 0.82,
                "plan": "Develop a monster before changing phases.",
                "why_now": "Main Phase is open.",
                "alternatives_considered": ["End Phase gives up tempo."],
                "risk": "Opponent may remove it.",
            }

        result = evaluator.evaluate_requests([request], fake_decider)

        self.assertEqual(1, result["summary"]["requests"])
        self.assertEqual(1, result["summary"]["valid_responses"])
        self.assertEqual(1, result["summary"]["card_specific_reasons"])
        self.assertEqual(0, result["summary"]["low_confidence"])
        self.assertEqual(0, result["summary"]["below_heuristic_baseline"])
        self.assertGreaterEqual(result["summary"]["latency_ms_p50"], 0)
        self.assertEqual(2, result["results"][0]["action_id"])
        self.assertEqual(True, result["results"][0]["known_action_id"])
        self.assertEqual(2, result["results"][0]["heuristic_best_action_id"])
        self.assertEqual(False, result["results"][0]["fallback_recommended"])

    def test_evaluates_bad_provider_decision(self):
        request = self.request()

        def fake_decider(_request):
            return {
                "run_effect_seq": 44,
                "action_id": 1,
                "reason": "first option",
                "confidence": 0.1,
            }

        result = evaluator.evaluate_requests([request], fake_decider)

        self.assertEqual(1, result["summary"]["valid_responses"])
        self.assertEqual(1, result["summary"]["generic_reasons"])
        self.assertEqual(1, result["summary"]["low_confidence"])
        self.assertEqual(1, result["summary"]["immediate_end_phase_choices"])
        self.assertEqual(0, result["summary"]["card_specific_reasons"])
        self.assertEqual(1, result["summary"]["below_heuristic_baseline"])
        self.assertEqual(2, result["results"][0]["heuristic_best_action_id"])
        self.assertEqual(True, result["results"][0]["fallback_recommended"])

    # ------------------------------------------------------------------
    # YGOMASTER-LLM-004 Slice 5 RED: history-present vs history-removed
    # ------------------------------------------------------------------

    def _history_rich_request(self):
        return {
            "kind": "decision_request",
            "schema_version": 4,
            "run_effect_seq": 44,
            "controlled_player": 1,
            "legal_actions": [
                {"action_id": 1, "kind": "move_phase", "phase": "End"},
                {
                    "action_id": 2,
                    "kind": "command",
                    "command": "Summon",
                    "card": {"name": "Cubic Seed", "text": "Starts the Cubic line."},
                },
            ],
            "duel_history": {
                "history_version": 1,
                "last_event_id": 21,
                "first_detailed_event_id": 18,
                "history_compacted": False,
                "budget_status": "ok",
                "events": [
                    {
                        "event_id": 18,
                        "actor_player": 0,
                        "kind": "normal_summon",
                        "card_name": "Public Monster",
                        "evidence": "accepted_command",
                    },
                    {
                        "event_id": 20,
                        "actor_player": 0,
                        "kind": "activate_effect",
                        "card_name": "Public Monster",
                        "evidence": "accepted_command",
                    },
                    {
                        "event_id": 21,
                        "actor_player": 0,
                        "kind": "card_moved",
                        "card_name": "Public Grave Card",
                        "evidence": "public_state_delta",
                    },
                ],
                "prior_turn_summaries": [],
                "revealed_card_context": [
                    {"card_id": 1234, "name": "Public Monster", "text": "Synthetic."}
                ],
            },
        }

    def test_slice5_history_present_vs_removed_paired_evaluation(self):
        request = self._history_rich_request()
        source_snapshot = json.dumps(request, sort_keys=True, separators=(",", ":"))

        def decider(req):
            history = req.get("duel_history") or {}
            events = history.get("events") or []
            if events:
                return {
                    "run_effect_seq": 44,
                    "action_id": 2,
                    "reason": "Normal Summon Cubic Seed after reading duel_history.",
                    "confidence": 0.9,
                    "opponent_action_assessment": "Opponent events 18/20/21 informed the choice.",
                    "history_event_ids_used": [18, 20, 21],
                    "plan": "Develop with history.",
                    "why_now": "Main Phase.",
                    "alternatives_considered": ["End Phase."],
                    "risk": "Removal.",
                }
            return {
                "run_effect_seq": 44,
                "action_id": 1,
                "reason": "End Phase without public history.",
                "confidence": 0.55,
                "opponent_action_assessment": "No public events in empty history.",
                "history_event_ids_used": [],
                "plan": "Pass.",
                "why_now": "No history.",
                "alternatives_considered": ["Summon."],
                "risk": "Missed development.",
            }

        evaluate_pair = getattr(evaluator, "evaluate_history_present_vs_removed", None)
        self.assertTrue(
            callable(evaluate_pair),
            "evaluate_llm_replay must expose evaluate_history_present_vs_removed",
        )
        result = evaluate_pair(request, decider)

        self.assertIn("present", result)
        self.assertIn("removed", result)
        self.assertIn("delta", result)

        present = result["present"]
        removed = result["removed"]
        delta = result["delta"]

        self.assertEqual(2, present["action_id"])
        self.assertEqual(1, removed["action_id"])
        self.assertEqual([18, 20, 21], present["history_event_ids_used"])
        self.assertEqual([], removed["history_event_ids_used"])
        self.assertIn("opponent_action_assessment", present)
        self.assertIn("opponent_action_assessment", removed)
        self.assertIn("latency_ms", present)
        self.assertIn("latency_ms", removed)

        # Removal uses canonical empty duel_history and does not mutate source.
        self.assertEqual(
            source_snapshot,
            json.dumps(request, sort_keys=True, separators=(",", ":")),
            "source request must not be mutated",
        )
        removed_req = removed.get("request") or result.get("removed_request")
        self.assertIsInstance(removed_req, dict)
        self.assertEqual(4, removed_req.get("schema_version"))
        empty = removed_req.get("duel_history")
        self.assertIsInstance(empty, dict)
        self.assertEqual([], empty.get("events"))
        self.assertEqual([], empty.get("prior_turn_summaries"))
        self.assertEqual(0, empty.get("last_event_id"))
        self.assertEqual(False, empty.get("history_compacted"))
        # Legal actions unchanged.
        self.assertEqual(
            request["legal_actions"],
            removed_req["legal_actions"],
        )

        # Structured deltas for action/reason/assessment/citations/latency.
        for key in (
            "action_id_changed",
            "reason_changed",
            "opponent_action_assessment_changed",
            "history_event_ids_used_changed",
            "latency_ms_delta",
        ):
            self.assertIn(key, delta, "delta missing %s" % key)
        self.assertEqual(True, delta["action_id_changed"])
        self.assertEqual(True, delta["reason_changed"])
        self.assertEqual(True, delta["history_event_ids_used_changed"])

    def test_slice5_v3_migration_remains_separate_from_history_pair(self):
        # Explicit migration path still exists and is not mixed into the pair evaluator.
        self.assertTrue(callable(evaluator.migrate_schema_v3_request))
        v3 = {
            "kind": "decision_request",
            "schema_version": 3,
            "run_effect_seq": 1,
            "legal_actions": [{"action_id": 0, "kind": "command", "command": "Summon"}],
        }
        migrated = evaluator.migrate_schema_v3_request(v3)
        self.assertEqual(4, migrated["schema_version"])
        self.assertIn("duel_history", migrated)
        # Pair evaluator requires schema-v4 history-present input, not silent v3.
        evaluate_pair = getattr(evaluator, "evaluate_history_present_vs_removed", None)
        self.assertTrue(callable(evaluate_pair))
        with self.assertRaises((ValueError, TypeError, AssertionError)):
            evaluate_pair(v3, lambda _r: {"run_effect_seq": 1, "action_id": 0})


if __name__ == "__main__":
    unittest.main()

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("analyze_llm_duel_log.py")
spec = importlib.util.spec_from_file_location("analyze_llm_duel_log", MODULE_PATH)
analyzer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyzer)


class LlmDuelLogAnalyzerTests(unittest.TestCase):
    def write_log(self, events):
        temp_dir = tempfile.TemporaryDirectory()
        path = Path(temp_dir.name) / "LlmDecisionLog.jsonl"
        with path.open("w", encoding="utf-8") as writer:
            for event in events:
                writer.write(json.dumps(event, separators=(",", ":")) + "\n")
        return temp_dir, path

    def test_analyzes_broker_commit_coverage(self):
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 30,
                    "turn": 0,
                    "turn_player": 1,
                    "acting_player": 1,
                    "current_phase": 2,
                    "legal_actions": [{"kind": "command"}, {"kind": "move_phase"}],
                },
                {"kind": "llm_broker_request_started", "request_run_effect_seq": 30},
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 30,
                    "success": True,
                    "action_id": 1,
                    "reason": "first option",
                    "confidence": 0.22,
                    "request_json": json.dumps(
                        {
                            "legal_actions": [
                                {"action_id": 1, "kind": "command", "command": "Summon"}
                            ]
                        },
                        separators=(",", ":"),
                    ),
                },
                {
                    "kind": "llm_broker_committed",
                    "request_run_effect_seq": 30,
                    "commit_run_effect_seq": 30,
                    "action_id": 1,
                    "action_type": "command",
                },
                {
                    "kind": "decision_window",
                    "run_effect_seq": 61,
                    "turn": 1,
                    "turn_player": 0,
                    "acting_player": 0,
                    "current_phase": 2,
                    "legal_actions": [{"kind": "move_phase"}],
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([path])

        self.assertEqual(summary["events"]["decision_window"], 2)
        self.assertEqual(summary["events"]["llm_broker_committed"], 1)
        self.assertEqual(summary["broker"]["commits"], 1)
        self.assertEqual(summary["broker"]["responses"], 1)
        self.assertEqual(summary["broker"]["successful_responses"], 1)
        self.assertEqual(summary["coverage"]["turns"], [0, 1])
        self.assertEqual(summary["coverage"]["broker_action_types"], ["command"])
        self.assertEqual(summary["coverage"]["decision_action_types"], ["command", "move_phase"])
        self.assertEqual(summary["quality"]["generic_reasons"], 1)
        self.assertEqual(summary["quality"]["low_confidence"], 1)
        self.assertEqual(summary["quality"]["cardless_command_choices"], 1)

    def test_requirement_check_reports_missing_coverage(self):
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 30,
                    "turn": 0,
                    "turn_player": 1,
                    "acting_player": 1,
                    "current_phase": 2,
                    "legal_actions": [{"kind": "command"}],
                }
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        summary = analyzer.analyze_paths([path])

        errors = analyzer.validate_requirements(
            summary,
            min_broker_commits=1,
            min_turns=2,
            required_broker_action_types=["command"],
        )

        self.assertIn("expected at least 1 broker commits, got 0", errors)
        self.assertIn("expected at least 2 turns, got 1", errors)
        self.assertIn("missing broker action types: command", errors)


if __name__ == "__main__":
    unittest.main()

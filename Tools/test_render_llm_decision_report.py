import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("render_llm_decision_report.py")
spec = importlib.util.spec_from_file_location("render_llm_decision_report", MODULE_PATH)
renderer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(renderer)


class RenderLlmDecisionReportTests(unittest.TestCase):
    def write_log(self, events):
        temp_dir = tempfile.TemporaryDirectory()
        path = Path(temp_dir.name) / "LlmDecisionLog.jsonl"
        with path.open("w", encoding="utf-8") as writer:
            for event in events:
                writer.write(json.dumps(event, separators=(",", ":")) + "\n")
        return temp_dir, path

    def test_renders_window_summary_and_flags_generic_reason(self):
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 30,
                    "turn": 4,
                    "current_phase": 2,
                    "acting_player": 1,
                    "legal_actions": [{"action_id": 1, "kind": "command", "command": "Summon"}],
                },
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 30,
                    "success": True,
                    "action_id": 1,
                    "reason": "first option",
                    "confidence": 0.22,
                    "plan": "Try the first summon.",
                    "request_json": {
                        "legal_actions": [
                            {
                                "action_id": 1,
                                "kind": "command",
                                "command": "Summon",
                                "card": {"name": "Duza", "text": "Once per turn..."},
                            }
                        ]
                    },
                },
                {
                    "kind": "llm_broker_committed",
                    "request_run_effect_seq": 30,
                    "commit_run_effect_seq": 30,
                    "action_type": "command",
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        report = renderer.render_report([path])

        self.assertIn("# LLM Decision Report", report)
        self.assertIn("Decision windows: 1", report)
        self.assertIn("flags: generic reason", report)
        self.assertIn("confidence: 0.22", report)
        self.assertIn("plan: Try the first summon.", report)
        self.assertIn("selected card: Duza", report)
        self.assertIn("card text: Once per turn...", report)

    def test_parses_request_json_string_from_runtime_logs(self):
        request_json = json.dumps(
            {
                "legal_actions": [
                    {
                        "action_id": 2,
                        "kind": "command",
                        "command": "Summon",
                        "card": {"name": "Runtime Duza", "text": "Runtime text."},
                    }
                ]
            },
            separators=(",", ":"),
        )
        temp_dir, path = self.write_log(
            [
                {"kind": "decision_window", "run_effect_seq": 31},
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 31,
                    "success": True,
                    "action_id": 2,
                    "reason": "Summon Runtime Duza.",
                    "request_json": request_json,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        report = renderer.render_report([path])

        self.assertIn("selected card: Runtime Duza", report)
        self.assertIn("card text: Runtime text.", report)


if __name__ == "__main__":
    unittest.main()

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

    def test_renders_attack_target_ownership_and_divergence(self):
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "llm_broker_window_routed",
                    "run_effect_seq": 539,
                    "route": "Broker",
                    "prompt_family": "WaitInput",
                    "reason": "attack_target",
                },
                {
                    "kind": "llm_attack_target_divergence",
                    "run_effect_seq": 539,
                    "origin_run_effect_seq": 534,
                    "reason": "provider_error",
                    "fallback": "cpu",
                    "legal_target_count": 2,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        report = renderer.render_report([path])
        self.assertIn("Broker-owned attack-target windows: 1", report)
        self.assertIn("Attack-target divergences: 1", report)
        self.assertIn(
            "attack-target divergence: seq 539 from attack 534 (provider_error -> cpu)",
            report,
        )

    def test_renders_applicability_rejection_and_followup_unavailable(self):
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "llm_broker_rejected",
                    "request_run_effect_seq": 863,
                    "error": "effect_applicability_contradiction",
                },
                {
                    "kind": "intended_followup_unavailable",
                    "origin_run_effect_seq": 881,
                    "current_run_effect_seq": 929,
                    "action_family": "effect_activation",
                    "expected_card_name": "Castel, the Skyblaster Musketeer",
                    "reason": "effect_not_legal",
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        report = renderer.render_report([path])
        self.assertIn("Effect-applicability contradiction rejects: 1", report)
        self.assertIn("Promised followups unavailable: 1", report)
        self.assertIn(
            "followup unavailable: root 881 -> seq 929 effect_activation Castel, the Skyblaster Musketeer (effect_not_legal)",
            report,
        )

    def test_renders_window_summary_and_flags_generic_reason(self):
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 30,
                    "turn": 4,
                    "current_phase": 2,
                    "acting_player": 1,
                    "is_strategic_window": True,
                    "strategic_window_reason": "strategic_choices",
                    "legal_actions": [{"action_id": 1, "kind": "command", "command": "Summon"}],
                },
                {
                    "kind": "llm_broker_window_routed",
                    "run_effect_seq": 30,
                    "route": "Broker",
                    "prompt_family": "WaitInput",
                    "reason": "strategic_choices",
                },
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 30,
                    "success": True,
                    "action_id": 1,
                    "reason": "first option",
                    "confidence": 0.22,
                    "plan": "Try the first summon.",
                    "why_now": "Use the summon before changing phases.",
                    "alternatives_considered": [
                        "End Phase gives up tempo.",
                        "Battle Phase has no attacker.",
                    ],
                    "risk": "Summoned monster can be removed.",
                    "opponent_board_assessment": "Opponent has one unknown field card and one known grave threat.",
                    "request_json": {
                        "legal_actions": [
                            {
                                "action_id": 1,
                                "kind": "command",
                                "command": "Summon",
                                "action_label": "Summon Duza",
                                "strategic_role": "board_development",
                                "consequence_hint": "normal_summon_consumes_turn_summon",
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
                {
                    "kind": "llm_broker_window_routed",
                    "run_effect_seq": 40,
                    "route": "CpuFallback",
                    "prompt_family": "RunList",
                    "reason": "unsupported_window",
                },
                {
                    "kind": "llm_broker_unsupported_window",
                    "run_effect_seq": 40,
                    "prompt_family": "RunList",
                    "reason": "unsupported_window",
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        report = renderer.render_report([path])

        self.assertIn("# LLM Decision Report", report)
        self.assertIn("Decision windows: 1", report)
        self.assertIn("Routed windows: 2", report)
        self.assertIn("Route Broker: 1", report)
        self.assertIn("Route CpuFallback: 1", report)
        self.assertIn("Unsupported windows: 1", report)
        self.assertIn("route: Broker (WaitInput, strategic_choices)", report)
        self.assertIn("unsupported window: RunList (unsupported_window)", report)
        self.assertIn("strategic window: True (strategic_choices)", report)
        self.assertIn("flags: generic reason", report)
        self.assertIn("confidence: 0.22", report)
        self.assertIn("plan: Try the first summon.", report)
        self.assertIn("why now: Use the summon before changing phases.", report)
        self.assertIn("alternative: End Phase gives up tempo.", report)
        self.assertIn("alternative: Battle Phase has no attacker.", report)
        self.assertIn("risk: Summoned monster can be removed.", report)
        self.assertIn(
            "opponent assessment: Opponent has one unknown field card and one known grave threat.",
            report,
        )
        self.assertIn("selected action: Summon Duza", report)
        self.assertIn("strategic role: board_development", report)
        self.assertIn("consequence: normal_summon_consumes_turn_summon", report)
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

    # ------------------------------------------------------------------
    # YGOMASTER-LLM-004 Slice 5 RED: timeline + citation highlighting (hardened)
    # ------------------------------------------------------------------

    SENTINEL_SET = "SENTINEL_FACEDOWN_SET_LEAK_99502"
    PUBLIC_GRAVE = "Public Grave Threat"
    PUBLIC_FACEUP = "Public Face-Up Monster"

    def test_slice5_string_request_json_timeline_highlights_and_classifies_citations(self):
        """
        Runtime shape: request_json is a JSON *string*.
        History: first_detailed=10, last=12, summary [1,3], detailed events {10,12}.
        Citations: 10 valid; 5 stale; 2 compacted; 99 newer; 11 unknown.
        """
        request = {
            "kind": "decision_request",
            "schema_version": 4,
            "run_effect_seq": 240,
            "legal_actions": [
                {
                    "action_id": 3,
                    "kind": "command",
                    "command": "Summon",
                    "action_label": "Summon Public Monster",
                    "card": {"name": "Public Monster", "text": "Synthetic text."},
                }
            ],
            "duel_history": {
                "history_version": 1,
                "duel_generation": 3,
                "last_event_id": 12,
                "first_detailed_event_id": 10,
                "history_compacted": True,
                "events": [
                    {
                        "event_id": 10,
                        "turn": 3,
                        "phase": "Main1",
                        "actor_player": 1,
                        "kind": "normal_summon",
                        "card_name": self.PUBLIC_FACEUP,
                        "evidence": "accepted_command",
                    },
                    {
                        "event_id": 12,
                        "turn": 3,
                        "phase": "Main1",
                        "actor_player": 0,
                        "kind": "card_moved",
                        "card_name": self.PUBLIC_GRAVE,
                        "destination_zone": "graveyard",
                        "evidence": "public_state_delta",
                    },
                ],
                "prior_turn_summaries": [
                    {
                        "turn": 1,
                        "first_event_id": 1,
                        "last_event_id": 3,
                        "actor_players": [0],
                        "event_count": 3,
                    }
                ],
                "revealed_card_context": [
                    {"card_id": 1234, "name": self.PUBLIC_FACEUP, "text": "Face-up text."},
                    {"card_id": 5678, "name": self.PUBLIC_GRAVE, "text": "Grave text."},
                ],
            },
        }
        request_str = json.dumps(request, separators=(",", ":"), ensure_ascii=False)
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 240,
                    "turn": 3,
                    "current_phase": 2,
                    "acting_player": 1,
                    "is_strategic_window": True,
                    "duel_generation": 3,
                    "duel_history": request["duel_history"],
                    "legal_actions": request["legal_actions"],
                },
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 240,
                    "success": True,
                    "action_id": 3,
                    "reason": "Answer using public history.",
                    "confidence": 0.8,
                    "opponent_board_assessment": "Public board noted.",
                    "opponent_action_assessment": (
                        "Opponent prior turn compacted; event 10 is my detailed public summon."
                    ),
                    "history_event_ids_used": [10, 5, 2, 99, 11],
                    "request_json": request_str,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        report = renderer.render_report([path])
        lowered = report.lower()
        self.assertIn("duel timeline", lowered)
        # Ordered detailed events before the decision.
        idx10 = report.find("event_id=10")
        idx12 = report.find("event_id=12")
        self.assertTrue(idx10 >= 0 and idx12 > idx10, report)
        # Valid citation highlighted.
        self.assertRegex(report, r"(?i)(cited|highlight).*event_id=10|event_id=10.*(cited|valid)")
        # Distinct classification labels for invalid families.
        for label in (
            "stale_history_event_id",
            "compacted_away_history_event_id",
            "newer_history_event_id",
            "unknown_history_event_id",
        ):
            self.assertIn(label, lowered, "missing classification %s in:\n%s" % (label, report))
        # Positive control: legitimate public face-up / grave identities render.
        self.assertIn(self.PUBLIC_FACEUP, report)
        self.assertIn(self.PUBLIC_GRAVE, report)

    def test_slice5_marks_missing_assessment_for_compacted_opponent_only(self):
        request = {
            "schema_version": 4,
            "run_effect_seq": 250,
            "legal_actions": [{"action_id": 1, "kind": "move_phase", "phase": "End"}],
            "duel_history": {
                "history_version": 1,
                "duel_generation": 1,
                "last_event_id": 5,
                "first_detailed_event_id": 5,
                "history_compacted": True,
                "events": [
                    {
                        "event_id": 5,
                        "actor_player": 1,
                        "kind": "normal_summon",
                        "card_name": "Own Public",
                    }
                ],
                "prior_turn_summaries": [
                    {
                        "turn": 1,
                        "first_event_id": 1,
                        "last_event_id": 2,
                        "actor_players": [0],
                    }
                ],
                "revealed_card_context": [],
            },
        }
        request_str = json.dumps(request, separators=(",", ":"), ensure_ascii=False)
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 250,
                    "duel_history": request["duel_history"],
                    "legal_actions": request["legal_actions"],
                },
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 250,
                    "success": True,
                    "action_id": 1,
                    "reason": "End phase with public history available.",
                    "opponent_action_assessment": "",
                    "history_event_ids_used": [1, 5],
                    "request_json": request_str,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        report = renderer.render_report([path])
        lowered = report.lower()
        self.assertTrue(
            "missing_opponent_action_assessment" in lowered
            or "missing opponent_action_assessment" in lowered
            or "assessment missing" in lowered,
            report,
        )
        self.assertIn("compacted_away_history_event_id", lowered)

    def test_slice5_schema_v3_windows_do_not_show_v4_history_failure_labels(self):
        v3_request = {
            "kind": "decision_request",
            "schema_version": 3,
            "run_effect_seq": 30,
            "legal_actions": [
                {
                    "action_id": 1,
                    "kind": "command",
                    "command": "Summon",
                    "card": {"name": "Legacy Card", "text": "Legacy."},
                }
            ],
        }
        v3_str = json.dumps(v3_request, separators=(",", ":"))
        v4_request = {
            "kind": "decision_request",
            "schema_version": 4,
            "run_effect_seq": 31,
            "controlled_player": 1,
            "legal_actions": [{"action_id": 1, "kind": "move_phase", "phase": "End"}],
            "duel_history": {
                "history_version": 1,
                "duel_generation": 1,
                "last_event_id": 5,
                "first_detailed_event_id": 5,
                "history_compacted": True,
                "events": [
                    {
                        "event_id": 5,
                        "actor_player": 1,
                        "kind": "normal_summon",
                        "card_name": "Own Public",
                    }
                ],
                "prior_turn_summaries": [
                    {
                        "turn": 1,
                        "first_event_id": 1,
                        "last_event_id": 2,
                        "actor_players": [0],
                    }
                ],
                "revealed_card_context": [],
            },
        }
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 30,
                    "legal_actions": v3_request["legal_actions"],
                },
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 30,
                    "success": True,
                    "action_id": 1,
                    "reason": "Legacy summon without history fields.",
                    "request_json": v3_str,
                },
                {
                    "kind": "decision_window",
                    "run_effect_seq": 31,
                    "duel_history": v4_request["duel_history"],
                    "legal_actions": v4_request["legal_actions"],
                },
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 31,
                    "success": True,
                    "action_id": 1,
                    "reason": "End phase with public history available.",
                    "opponent_action_assessment": "",
                    "history_event_ids_used": [1],
                    "request_json": json.dumps(v4_request, separators=(",", ":")),
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        report = renderer.render_report([path])
        lowered = report.lower()

        # Window 1 (schema 3): neutral marker, no v4 failure labels.
        self.assertIn("history grounding unavailable for legacy schema 3", lowered)
        # Ensure the schema-3 section does not carry v4 violation labels by checking
        # that missing_history_event_ids_used only appears (if at all) near schema 4.
        # Schema-3 response has no history_event_ids_used — must not emit that issue.
        # Split report by windows and assert per window.
        parts = report.split("## Window ")
        schema3_part = next(p for p in parts if p.startswith("1\n") or p.startswith("1\r"))
        schema4_part = next(p for p in parts if p.startswith("2\n") or p.startswith("2\r"))
        self.assertIn(
            "history grounding unavailable for legacy schema 3",
            schema3_part.lower(),
        )
        self.assertNotIn("missing_history_event_ids_used", schema3_part.lower())
        self.assertNotIn("missing_opponent_action_assessment", schema3_part.lower())
        self.assertNotIn("citation issue:", schema3_part.lower())

        # Schema 4 behavior unchanged.
        self.assertIn("compacted_away_history_event_id", schema4_part.lower())
        self.assertTrue(
            "missing_opponent_action_assessment" in schema4_part.lower()
            or "assessment missing" in schema4_part.lower(),
            schema4_part,
        )

    def test_slice5_never_renders_sentinel_set_but_renders_public_identities(self):
        request = {
            "schema_version": 4,
            "run_effect_seq": 260,
            "legal_actions": [{"action_id": 0, "kind": "command", "command": "Summon"}],
            "duel_history": {
                "history_version": 1,
                "duel_generation": 1,
                "last_event_id": 2,
                "first_detailed_event_id": 1,
                "history_compacted": False,
                "events": [
                    {
                        "event_id": 1,
                        "kind": "set_monster",
                        "destination_zone": "monster_zone",
                        "actor_player": 0,
                        "card_name": self.SENTINEL_SET,
                        "card_id": 99502991,
                    },
                    {
                        "event_id": 2,
                        "kind": "card_moved",
                        "destination_zone": "graveyard",
                        "actor_player": 0,
                        "card_name": self.PUBLIC_GRAVE,
                        "card_id": 5678,
                        "evidence": "public_state_delta",
                    },
                ],
                "prior_turn_summaries": [],
                "revealed_card_context": [
                    {"card_id": 5678, "name": self.PUBLIC_GRAVE, "text": "Public GY text."}
                ],
            },
        }
        request_str = json.dumps(request, separators=(",", ":"), ensure_ascii=False)
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 260,
                    "duel_history": request["duel_history"],
                    "legal_actions": request["legal_actions"],
                },
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 260,
                    "success": True,
                    "action_id": 0,
                    "reason": "Develop using public graveyard context.",
                    "opponent_action_assessment": "Set card noted without identity; GY is public.",
                    "history_event_ids_used": [2],
                    "request_json": request_str,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        report = renderer.render_report([path])
        self.assertIn("duel timeline", report.lower())
        self.assertTrue(
            "set_monster" in report.lower() or "set monster" in report.lower(),
            report,
        )
        self.assertNotIn(self.SENTINEL_SET, report)
        self.assertNotIn("99502991", report)
        # Positive control: public graveyard identity is rendered.
        self.assertIn(self.PUBLIC_GRAVE, report)


if __name__ == "__main__":
    unittest.main()

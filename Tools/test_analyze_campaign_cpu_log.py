#!/usr/bin/env python3
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("analyze_campaign_cpu_log.py")
spec = importlib.util.spec_from_file_location("analyze_campaign_cpu_log", MODULE_PATH)
analyzer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyzer)


class CampaignCpuLogAnalyzerTests(unittest.TestCase):
    def write_log(self, events):
        temp_dir = tempfile.TemporaryDirectory()
        path = Path(temp_dir.name) / "CampaignCpuAuditLog.jsonl"
        with path.open("w", encoding="utf-8") as writer:
            for event in events:
                writer.write(json.dumps(event, separators=(",", ":")) + "\n")
        return temp_dir, path

    def write_pack(self, pack):
        temp_dir = tempfile.TemporaryDirectory()
        path = Path(temp_dir.name) / "11010078.json"
        path.write_text(json.dumps(pack), encoding="utf-8")
        return temp_dir, path

    def test_segments_pack_loaded_and_counts_decisions_commits(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "ts": "t0",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                },
                {
                    "event": "seat_owned",
                    "confirmed": True,
                },
                {
                    "event": "campaign_cpu_decision",
                    "rule_id": "g1-opening-special-or-action",
                    "action_identity": "Command|SummonSp|12485|13|1|",
                    "route": "RuleCommit",
                    "shadow_only": False,
                    "acting_player": 1,
                    "owned_seat": 1,
                    "my_id": 0,
                },
                {
                    "event": "commit_applied",
                    "rule_id": "g1-opening-special-or-action",
                    "action": "Command|SummonSp|12485|13|1|",
                },
                {
                    "event": "commit_applied",
                    "rule_id": "mechanical_location_default",
                    "action": "Command|Decide|12485|0|0|default_location",
                },
                {
                    "event": "temporary_cpu_begin",
                    "reason": "dual_human_myid_response_hold",
                },
                {
                    "event": "temporary_cpu_restore",
                    "reason": "owned_main_capture_boundary",
                },
                {
                    "event": "duel_end_summary",
                    "decisions": 1,
                    "state": "NativeLease",
                    "scripting_disabled": False,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(1, summary["duel_count"])
        duel = summary["duels"][0]
        self.assertEqual(False, duel["log_only"])
        self.assertEqual("pr4b_scripted", duel["mode"])
        self.assertEqual(1, duel["live_decisions"])
        self.assertEqual(2, duel["commits"])
        self.assertEqual(
            {"g1-opening-special-or-action": 1},
            duel["decision_rule_ids"],
        )
        self.assertEqual(
            {
                "g1-opening-special-or-action": 1,
                "mechanical_location_default": 1,
            },
            duel["commit_rule_ids"],
        )
        self.assertEqual(
            {"mechanical_location_default": 1},
            duel["mechanical_hits"],
        )
        self.assertEqual(
            {"dual_human_myid_response_hold": 1},
            duel["lease_begin_reasons"],
        )
        self.assertEqual(1, summary["aggregate"]["completed_duels"])

    def test_s10_safety_and_human_seat_commit_detection(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                },
                {
                    "event": "campaign_cpu_decision",
                    "rule_id": "prefer-summon",
                    "route": "RuleCommit",
                    "shadow_only": False,
                    "acting_player": 0,
                    "owned_seat": 1,
                    "my_id": 0,
                    "action_identity": "Command|Summon|1|13|0|",
                },
                {"event": "post_commit_stall", "view_seq": 10, "timeout_ms": 2000},
                {"event": "location_mask_empty", "mask": 0},
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(1, summary["aggregate"]["post_commit_stall"])
        self.assertEqual(1, summary["aggregate"]["human_seat_rule_commits"])
        self.assertEqual(
            1,
            (summary["aggregate"]["safety"] or {}).get("location_mask_empty"),
        )

        errors = analyzer.validate_requirements(
            summary,
            require_zero_stalls=True,
            require_zero_human_seat_commits=True,
            require_zero_location_mask_empty=True,
        )
        self.assertTrue(any("post_commit_stall" in e for e in errors))
        self.assertTrue(any("human_seat_rule_commits" in e for e in errors))
        self.assertTrue(any("location_mask_empty" in e for e in errors))

    def test_pack_required_for_slice_and_never_fired(self):
        pack = {
            "chapter_id": 11010078,
            "deck_hash": analyzer.DEFAULT_DECK_PIN,
            "priority": [
                {
                    "id": "g1-opening-special-or-action",
                    "required_for_slice": True,
                },
                {
                    "id": "g2-next-preferred-without-top",
                    "required_for_slice": True,
                },
            ],
            "fallback_scoring": [
                {"id": "prefer-action", "required_for_slice": True},
                {"id": "prefer-set"},
            ],
        }
        pack_dir, pack_path = self.write_pack(pack)
        self.addCleanup(pack_dir.cleanup)

        log_dir, log_path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr3_capture_shadow",
                    "log_only": True,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                },
                {
                    "event": "campaign_cpu_decision",
                    "rule_id": "g2-next-preferred-without-top",
                    "route": "RuleCommit",
                    "shadow_only": True,
                    "action_identity": "Command|Summon|12292|13|2|",
                },
                {
                    "event": "campaign_cpu_decision",
                    "rule_id": "prefer-action",
                    "route": "RuleCommit",
                    "shadow_only": True,
                    "action_identity": "Command|Action|8344|13|0|",
                },
            ]
        )
        self.addCleanup(log_dir.cleanup)

        summary = analyzer.analyze_paths(
            [str(log_path)],
            pack_path=str(pack_path),
        )
        pack_info = summary["pack"]
        self.assertIn("g1-opening-special-or-action", pack_info["required_missing"])
        self.assertNotIn("g2-next-preferred-without-top", pack_info["required_missing"])
        self.assertIn("prefer-set", pack_info["never_fired_rules"])

        errors = analyzer.validate_requirements(
            summary,
            require_required_for_slice=True,
        )
        self.assertTrue(any("required_for_slice" in e for e in errors))

        # After G1 appears, gate passes.
        log_dir2, log_path2 = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr3_capture_shadow",
                    "log_only": True,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                },
                {
                    "event": "campaign_cpu_decision",
                    "rule_id": "g1-opening-special-or-action",
                    "route": "RuleCommit",
                    "shadow_only": True,
                },
                {
                    "event": "campaign_cpu_decision",
                    "rule_id": "g2-next-preferred-without-top",
                    "route": "RuleCommit",
                    "shadow_only": True,
                },
                {
                    "event": "campaign_cpu_decision",
                    "rule_id": "prefer-action",
                    "route": "RuleCommit",
                    "shadow_only": True,
                },
            ]
        )
        self.addCleanup(log_dir2.cleanup)
        summary2 = analyzer.analyze_paths(
            [str(log_path2)],
            pack_path=str(pack_path),
        )
        errors2 = analyzer.validate_requirements(
            summary2,
            require_required_for_slice=True,
            require_deck_hash=analyzer.DEFAULT_DECK_PIN,
            min_shadow_decisions=3,
        )
        self.assertEqual([], errors2)

    def test_filters_last_n_and_mode_and_cli_main(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr3_capture_shadow",
                    "log_only": True,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                },
                {
                    "event": "campaign_cpu_decision",
                    "rule_id": "prefer-set",
                    "shadow_only": True,
                    "route": "RuleCommit",
                },
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                },
                {
                    "event": "commit_applied",
                    "rule_id": "prefer-summon",
                    "action": "Command|Summon|1|13|0|",
                },
                {
                    "event": "duel_end_summary",
                    "decisions": 1,
                    "scripting_disabled": False,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths(
            [str(path)],
            mode="pr4b_scripted",
            last_n_duels=1,
        )
        self.assertEqual(1, summary["duel_count"])
        self.assertEqual("pr4b_scripted", summary["duels"][0]["mode"])
        self.assertEqual(1, summary["aggregate"]["commits"])

        rc = analyzer.main(
            [
                str(path),
                "--mode",
                "pr4b_scripted",
                "--min-commits",
                "1",
                "--require-zero-stalls",
                "--require-deck-hash",
                "pin",
                "--compact",
            ]
        )
        self.assertEqual(0, rc)

        rc_fail = analyzer.main(
            [
                str(path),
                "--mode",
                "pr4b_scripted",
                "--min-commits",
                "5",
            ]
        )
        self.assertEqual(1, rc_fail)

    def test_mechanical_acting_myid_is_not_human_seat_rule_commit(self):
        """MechanicalAuto may sample acting=MyId; only RuleCommit is S10-sensitive."""
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 3,
                },
                {
                    "event": "campaign_cpu_decision",
                    "rule_id": "mechanical_sel_stand",
                    "route": "MechanicalAuto",
                    "shadow_only": False,
                    "acting_player": 0,
                    "owned_seat": 1,
                    "my_id": 0,
                    "duel_generation": 3,
                    "action_identity": "DialogResult|Attack|0|65536|65536|sel_stand",
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(0, summary["aggregate"]["human_seat_rule_commits"])
        errors = analyzer.validate_requirements(
            summary,
            require_zero_human_seat_commits=True,
        )
        self.assertEqual([], errors)

    def production_decision_row(
        self,
        *,
        rule_id="prefer-summon",
        route="RuleCommit",
        shadow_only=False,
        acting_player=1,
        owned_seat=1,
        my_id=0,
        duel_generation=2,
        turn=3,
        turn_player=1,
        phase=4,
        action_identity="Command|Summon|7850|13|3|",
        include_my_id=True,
        include_generation=True,
    ):
        """Fields matching CampaignCpuAuditSerializer.SerializeDecision production schema.

        Intentionally excludes synthetic-only fields. my_id/duel_generation are part of
        the fixed production contract (PR4b handoff).
        """
        row = {
            "event": "campaign_cpu_decision",
            "ts": "2026-07-24T17:04:00Z",
            "view_seq": 29,
            "route": route,
            "reason": "rule",
            "rule_id": rule_id,
            "score": 10,
            "matched": True,
            "shadow_only": shadow_only,
            "chapter_id": 11010078,
            "window_class": "WaitInput_MainPhase",
            "acting_player": acting_player,
            "owned_seat": owned_seat,
            "turn": turn,
            "turn_player": turn_player,
            "phase": phase,
            "self_lp": 8000,
            "opp_lp": 8000,
            "is_main_phase_wait_input": True,
            "is_multi_select": False,
            "legal_count": 1,
            "legal_fingerprint": action_identity,
            "self_hand_card_ids": [7850],
            "self_field_face_up_card_ids": [],
            "opp_field_face_up_card_ids": [],
            "legal_actions": [
                {
                    "action_id": 1,
                    "identity": action_identity,
                    "kind": "Command",
                    "command": "Summon",
                    "phase": "Main1",
                    "card_id": 7850,
                    "position": 13,
                    "index": 3,
                    "dialog_result": 0,
                    "cancel_decide": 0,
                    "label": "",
                    "is_mechanical": False,
                    "target_scope": "",
                    "player": 1,
                }
            ],
            "action_id": 1,
            "action_identity": action_identity,
            "command": "Summon",
            "card_id": 7850,
            "chosen": {
                "action_id": 1,
                "identity": action_identity,
                "kind": "Command",
                "command": "Summon",
                "phase": "Main1",
                "card_id": 7850,
                "position": 13,
                "index": 3,
                "dialog_result": 0,
                "cancel_decide": 0,
                "label": "",
                "is_mechanical": False,
                "target_scope": "",
                "player": 1,
            },
        }
        if include_my_id:
            row["my_id"] = my_id
        if include_generation:
            row["duel_generation"] = duel_generation
        return row

    def test_production_serializer_schema_detects_human_seat_rule_commit(self):
        """S10 human-seat gate must fire on production-shaped rows (not synthetic supersets)."""
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 2,
                },
                self.production_decision_row(
                    acting_player=0,
                    owned_seat=1,
                    my_id=0,
                    duel_generation=2,
                ),
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(1, summary["aggregate"]["human_seat_rule_commits"])
        errors = analyzer.validate_requirements(
            summary, require_zero_human_seat_commits=True
        )
        self.assertTrue(any("human_seat_rule_commits" in e for e in errors))

    def test_production_serializer_owned_seat_rule_commit_is_clean(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 2,
                },
                self.production_decision_row(
                    acting_player=1,
                    owned_seat=1,
                    my_id=0,
                    duel_generation=2,
                ),
                {
                    "event": "commit_applied",
                    "rule_id": "prefer-summon",
                    "action": "Command|Summon|7850|13|3|",
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 2,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(0, summary["aggregate"]["human_seat_rule_commits"])
        self.assertEqual(
            0, summary["aggregate"]["ownership_context_missing_rule_commits"]
        )
        self.assertEqual([], analyzer.validate_requirements(
            summary, require_zero_human_seat_commits=True, min_commits=1
        ))

    def test_s10_rejects_non_owned_actor_even_when_not_my_id(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 2,
                },
                self.production_decision_row(
                    acting_player=2,
                    owned_seat=1,
                    my_id=0,
                    duel_generation=2,
                ),
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(
            1, summary["aggregate"].get("non_owned_rule_commits", 0)
        )
        errors = analyzer.validate_requirements(
            summary, require_zero_human_seat_commits=True
        )
        self.assertTrue(any("non_owned_rule_commits" in e for e in errors))

    def test_s10_rejects_collapsed_owned_and_my_id_seats(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 1,
                    "owned_seat": 1,
                    "duel_generation": 2,
                },
                self.production_decision_row(
                    acting_player=1,
                    owned_seat=1,
                    my_id=1,
                    duel_generation=2,
                ),
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(
            1,
            summary["aggregate"].get(
                "ownership_context_invalid_rule_commits", 0
            ),
        )
        errors = analyzer.validate_requirements(
            summary, require_zero_human_seat_commits=True
        )
        self.assertTrue(
            any("ownership_context_invalid_rule_commits" in e for e in errors)
        )

    def test_s10_rejects_row_ownership_that_contradicts_pack_segment(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 2,
                },
                self.production_decision_row(
                    acting_player=0,
                    owned_seat=0,
                    my_id=1,
                    duel_generation=2,
                ),
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(
            1,
            summary["aggregate"].get(
                "ownership_context_invalid_rule_commits", 0
            ),
        )
        errors = analyzer.validate_requirements(
            summary, require_zero_human_seat_commits=True
        )
        self.assertTrue(
            any("ownership_context_invalid_rule_commits" in e for e in errors)
        )

    def test_successive_main_lineage_counts_candidate_attempt_confirm(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 3,
                },
                {
                    "event": "owned_main_boundary_probe",
                    "candidate": True,
                    "candidate_reason": "eligible",
                    "lease_origin_classification": "ScriptedResponseContinuation",
                    "turn": 3,
                    "view_seq": 20,
                },
                {
                    "event": "same_main_recapture_attempt",
                    "boundary": "pre_sysact_stable_owned_main",
                    "origin_view_seq": 10,
                    "view_seq": 20,
                    "turn": 3,
                    "recapture_attempts": 1,
                    "lease_origin_classification": "ScriptedResponseContinuation",
                    "duel_generation": 3,
                    "my_id": 0,
                    "owned_seat": 1,
                },
                {
                    "event": "same_main_recapture_confirmed",
                    "origin_view_seq": 10,
                    "view_seq": 22,
                    "turn": 3,
                    "acting_player": 1,
                    "duel_generation": 3,
                    "my_id": 0,
                    "owned_seat": 1,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        agg = summary["aggregate"]
        self.assertEqual(1, agg["recapture_candidates"])
        self.assertEqual(1, agg["recapture_attempts"])
        self.assertEqual(1, agg["stable_main_boundary_attempts"])
        self.assertEqual(0, agg["legacy_cpu_thinking_recapture_attempts"])
        self.assertEqual(1, agg["recapture_confirmed"])
        self.assertEqual(1, agg["max_recaptures_per_turn"])
        self.assertEqual([], analyzer.validate_requirements(
            summary, require_successive_main_safety=True
        ))

    def test_stable_main_direct_commit_counts_without_human_handoff(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 4,
                },
                {
                    "event": "same_main_direct_commit_attempt",
                    "boundary": "pre_sysact_stable_owned_main_direct_commit",
                    "origin_view_seq": 10,
                    "view_seq": 20,
                    "turn": 3,
                    "recapture_attempts": 1,
                    "lease_origin_classification":
                        "ScriptedResponseContinuation",
                    "commit_outcome": "Applied",
                    "ownership_transition_attempted": False,
                    "owned_is_human_readback": 0,
                    "my_is_human_readback": 1,
                    "duel_generation": 4,
                    "my_id": 0,
                    "owned_seat": 1,
                },
                {
                    "event": "same_main_direct_commit_applied",
                    "view_seq": 20,
                    "turn": 3,
                    "action": "Command|Summon|4747|13|0|",
                    "rule_id": "prefer-summon",
                    "owned_is_human_readback": 0,
                    "duel_generation": 4,
                    "my_id": 0,
                    "owned_seat": 1,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        agg = summary["aggregate"]
        self.assertEqual(1, agg["recapture_attempts"])
        self.assertEqual(1, agg["stable_main_boundary_attempts"])
        self.assertEqual(1, agg["stable_main_direct_commit_attempts"])
        self.assertEqual(1, agg["stable_main_direct_commits_applied"])
        self.assertEqual(0, agg["legacy_cpu_thinking_recapture_attempts"])
        self.assertEqual(0, agg["recapture_confirmed"])
        self.assertEqual(1, agg["max_recaptures_per_turn"])
        self.assertEqual([], analyzer.validate_requirements(
            summary, require_successive_main_safety=True
        ))

    def test_stable_main_direct_commit_rejects_ownership_transition(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 5,
                },
                {
                    "event": "same_main_direct_commit_attempt",
                    "boundary": "pre_sysact_stable_owned_main_direct_commit",
                    "turn": 2,
                    "lease_origin_classification":
                        "ScriptedResponseContinuation",
                    "ownership_transition_attempted": True,
                    "owned_is_human_readback": 1,
                    "my_is_human_readback": 1,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(
            1,
            summary["aggregate"]["stable_main_direct_ownership_violations"],
        )
        errors = analyzer.validate_requirements(
            summary, require_successive_main_safety=True
        )
        self.assertTrue(
            any(
                "stable_main_direct_ownership_violations" in error
                for error in errors
            )
        )

    def test_successive_main_safety_rejects_unsafe_attempt_and_dominated_commit(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 3,
                },
                {
                    "event": "same_main_recapture_attempt",
                    "forward_order": "cpu_before_human_transition",
                    "origin_view_seq": 10,
                    "view_seq": 20,
                    "turn": 3,
                    "recapture_attempts": 1,
                    "lease_origin_classification": "UnsafeContinuation",
                    "duel_generation": 3,
                    "my_id": 0,
                    "owned_seat": 1,
                },
                {
                    "event": "player_type_transition_failed",
                    "path": "stable_main_pre_sysact_recapture",
                },
                {
                    "event": "campaign_cpu_decision",
                    "route": "RuleCommit",
                    "shadow_only": False,
                    "action_identity": "Command|TurnDef|4007|2|0|",
                    "tactical_filter_reason": "dominated_turn_defense",
                    "tactical_filtered_action_identities": [
                        "Command|TurnDef|4007|2|0|"
                    ],
                    "acting_player": 1,
                    "owned_seat": 1,
                    "my_id": 0,
                    "duel_generation": 3,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        agg = summary["aggregate"]
        self.assertEqual(1, agg["unsafe_lineage_recapture_attempts"])
        self.assertEqual(1, agg["legacy_cpu_thinking_recapture_attempts"])
        self.assertEqual(1, agg["recapture_transition_failures"])
        self.assertEqual(1, agg["dominated_position_commits"])
        errors = analyzer.validate_requirements(
            summary, require_successive_main_safety=True
        )
        self.assertTrue(
            any("unsafe_lineage_recapture_attempts" in error for error in errors)
        )
        self.assertTrue(
            any("dominated_position_commits" in error for error in errors)
        )
        self.assertTrue(
            any(
                "legacy_cpu_thinking_recapture_attempts" in error
                for error in errors
            )
        )

    def test_dominated_filter_counts_shadow_decisions_without_flagging_commit(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_shadow",
                    "log_only": True,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 3,
                },
                {
                    "event": "campaign_cpu_decision",
                    "route": "Shadow",
                    "shadow_only": True,
                    "action_identity": "Phase|Battle|0|0|0|",
                    "tactical_filter_reason": "dominated_turn_defense",
                    "tactical_filtered_action_identities": [
                        "Command|TurnDef|4007|2|0|"
                    ],
                    "acting_player": 1,
                    "owned_seat": 1,
                    "my_id": 0,
                    "duel_generation": 3,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        agg = summary["aggregate"]
        self.assertEqual(1, agg["dominated_position_filtered"])
        self.assertEqual(0, agg["dominated_position_commits"])

    def test_inherit_my_id_from_pack_loaded_when_decision_omits_it(self):
        """Legacy decision rows without my_id still evaluate via pack_loaded segment."""
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 5,
                },
                self.production_decision_row(
                    acting_player=0,
                    owned_seat=1,
                    include_my_id=False,
                    include_generation=False,
                ),
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(1, summary["aggregate"]["human_seat_rule_commits"])
        self.assertEqual(5, summary["duels"][0]["duel_generation"])
        self.assertEqual(0, summary["duels"][0]["my_id"])

    def test_fail_closed_when_require_human_seat_but_no_ownership_context(self):
        """Production-shaped RuleCommit without my_id/owned_seat must not green-wash S10."""
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    # deliberately no my_id / owned_seat
                },
                self.production_decision_row(
                    acting_player=1,
                    include_my_id=False,
                    include_generation=False,
                ),
            ]
        )
        # production_decision_row still sets owned_seat on the row; strip it to simulate
        # pre-fix production decision without ownership fields.
        # rewrite last line without owned_seat
        events = []
        with path.open(encoding="utf-8") as reader:
            for line in reader:
                events.append(json.loads(line))
        del events[1]["owned_seat"]
        with path.open("w", encoding="utf-8") as writer:
            for event in events:
                writer.write(json.dumps(event, separators=(",", ":")) + "\n")
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        self.assertEqual(
            1, summary["aggregate"]["ownership_context_missing_rule_commits"]
        )
        errors = analyzer.validate_requirements(
            summary, require_zero_human_seat_commits=True
        )
        self.assertTrue(
            any("ownership_context_missing_rule_commits" in e for e in errors)
        )

    def test_generation_change_opens_new_segment(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 1,
                },
                self.production_decision_row(duel_generation=1, acting_player=1),
                self.production_decision_row(
                    duel_generation=2,
                    acting_player=1,
                    rule_id="prefer-action",
                    action_identity="Command|Action|6782|13|0|",
                ),
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        summary = analyzer.analyze_paths([str(path)])
        self.assertGreaterEqual(summary["duel_count"], 2)
        gens = [d.get("duel_generation") for d in summary["duels"]]
        self.assertIn(1, gens)
        self.assertIn(2, gens)

    def test_offline_replay_report_is_deterministic_and_focuses_on_risks(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 9,
                },
                {
                    "event": "same_main_recapture_attempt",
                    "origin_view_seq": 10,
                    "view_seq": 20,
                    "turn": 3,
                    "recapture_attempts": 1,
                    "lease_origin_classification": "UnsafeContinuation",
                    "duel_generation": 9,
                    "my_id": 0,
                    "owned_seat": 1,
                },
                {
                    "event": "same_main_recapture_abandoned",
                    "origin_view_seq": 10,
                    "view_seq": 20,
                    "reason": "timeout",
                    "duel_generation": 9,
                },
                {
                    "event": "campaign_cpu_decision",
                    "route": "RuleCommit",
                    "shadow_only": False,
                    "action_identity": "Command|TurnDef|4007|2|0|",
                    "tactical_filtered_action_identities": [
                        "Command|TurnDef|4007|2|0|"
                    ],
                    "acting_player": 1,
                    "owned_seat": 1,
                    "my_id": 0,
                    "duel_generation": 9,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        report1 = analyzer.build_offline_replay_report(summary)
        report2 = analyzer.build_offline_replay_report(summary)

        self.assertEqual(report1, report2)
        self.assertEqual(
            [
                "recapture_timeouts",
                "unsafe_lineage_recapture_attempts",
                "dominated_position_commits",
            ],
            report1["tactical_issues"],
        )
        self.assertEqual(1, report1["recapture"]["attempts"])
        self.assertEqual(1, report1["safety"]["dominated_position_commits"])
        self.assertEqual(1, report1["duels"][0]["dominated_position_commits"])

        rc = analyzer.main(
            [
                str(path),
                "--offline-replay-report",
            ]
        )
        self.assertEqual(1, rc)

    def test_offline_replay_report_replays_safety_and_tactical_policy(self):
        temp_dir, path = self.write_log(
            [
                {
                    "event": "pack_loaded",
                    "chapter_id": 11010078,
                    "mode": "pr4b_scripted",
                    "log_only": False,
                    "deck_hash": analyzer.DEFAULT_DECK_PIN,
                    "my_id": 0,
                    "owned_seat": 1,
                    "duel_generation": 4,
                },
                {
                    "event": "campaign_cpu_decision",
                    "view_seq": 10,
                    "route": "RuleCommit",
                    "shadow_only": False,
                    "rule_id": "prefer-action",
                    "action_identity": "Command|Action|12489|3|0|effect_defined",
                    "command": "Action",
                    "card_id": 12489,
                    "acting_player": 1,
                    "owned_seat": 1,
                    "my_id": 0,
                    "duel_generation": 4,
                    "turn": 2,
                    "turn_player": 1,
                    "phase": 2,
                    "self_hand_card_ids": [12489, 4007],
                    "self_field_face_up_card_ids": [4007],
                    "opp_field_face_up_card_ids": [12522],
                    "legal_actions": [
                        {
                            "identity": "Command|Action|12489|3|0|effect_defined",
                            "kind": "Command",
                            "command": "Action",
                            "card_id": 12489,
                            "position": 3,
                            "index": 0,
                        },
                        {
                            "identity": "MovePhase|End|0|0|5|",
                            "kind": "MovePhase",
                            "command": "Attack",
                            "phase": "End",
                            "card_id": 0,
                            "position": 0,
                            "index": 0,
                        },
                    ],
                },
                {
                    "event": "campaign_cpu_decision",
                    "view_seq": 20,
                    "route": "RuleCommit",
                    "shadow_only": False,
                    "rule_id": "fallback",
                    "action_identity": "Command|TurnDef|4007|2|0|",
                    "command": "TurnDef",
                    "card_id": 4007,
                    "acting_player": 1,
                    "owned_seat": 1,
                    "my_id": 0,
                    "duel_generation": 4,
                    "turn": 4,
                    "turn_player": 1,
                    "phase": 2,
                    "self_hand_card_ids": [4007],
                    "self_field_face_up_card_ids": [4007],
                    "opp_field_face_up_card_ids": [12522],
                    "legal_actions": [
                        {
                            "identity": "Command|TurnDef|4007|2|0|",
                            "kind": "Command",
                            "command": "TurnDef",
                            "card_id": 4007,
                            "position": 2,
                            "index": 0,
                        },
                        {
                            "identity": "MovePhase|Battle|0|0|3|",
                            "kind": "MovePhase",
                            "command": "Attack",
                            "phase": "Battle",
                            "card_id": 0,
                            "position": 0,
                            "index": 0,
                        },
                        {
                            "identity": "MovePhase|End|0|0|5|",
                            "kind": "MovePhase",
                            "command": "Attack",
                            "phase": "End",
                            "card_id": 0,
                            "position": 0,
                            "index": 0,
                        },
                    ],
                    "tactical_monsters": [
                        {
                            "player": 1,
                            "position": 2,
                            "index": 0,
                            "card_id": 4007,
                            "face_known": True,
                            "face_up": True,
                            "turn_known": True,
                            "turn_raw": 0,
                            "is_attack": True,
                            "is_defense": False,
                            "atk": 3000,
                            "def": 2500,
                        },
                        {
                            "player": 0,
                            "position": 2,
                            "index": 0,
                            "card_id": 12522,
                            "face_known": True,
                            "face_up": True,
                            "turn_known": True,
                            "turn_raw": 0,
                            "is_attack": True,
                            "is_defense": False,
                            "atk": 3000,
                            "def": 2500,
                        },
                    ],
                },
                {
                    "event": "campaign_cpu_decision",
                    "view_seq": 30,
                    "route": "RuleCommit",
                    "shadow_only": False,
                    "rule_id": "prefer-summon",
                    "action_identity": "Command|Summon|4007|13|0|",
                    "command": "Summon",
                    "card_id": 4007,
                    "acting_player": 1,
                    "owned_seat": 1,
                    "my_id": 0,
                    "duel_generation": 4,
                    "turn": 6,
                    "turn_player": 1,
                    "phase": 2,
                    "self_hand_card_ids": [4007],
                    "legal_actions": [
                        {
                            "identity": "Command|Summon|4007|13|0|",
                            "kind": "Command",
                            "command": "Summon",
                            "card_id": 4007,
                            "position": 13,
                            "index": 0,
                        },
                        {
                            "identity": "MovePhase|End|0|0|5|",
                            "kind": "MovePhase",
                            "command": "Attack",
                            "phase": "End",
                            "card_id": 0,
                            "position": 0,
                            "index": 0,
                        },
                    ],
                },
                {
                    "event": "campaign_cpu_decision",
                    "view_seq": 31,
                    "route": "MechanicalAuto",
                    "shadow_only": False,
                    "rule_id": "mechanical_location_default",
                    "action_identity": "Command|Decide|4007|0|0|default_location",
                    "command": "Decide",
                    "legal_actions": [],
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([str(path)])
        report = analyzer.build_offline_replay_report(summary)

        self.assertEqual(4, report["corpus"]["decision_rows"])
        self.assertEqual(2, report["corpus"]["replayable_rows"])
        self.assertEqual(1, report["classification_counts"]["materially_changed"])
        self.assertEqual(1, report["classification_counts"]["safety_deferred"])
        self.assertEqual(2, report["classification_counts"]["unscorable"])

        action_row = report["decisions"][0]
        self.assertEqual("FallbackNative", action_row["candidate"]["route"])
        self.assertEqual(
            "unsupported_scripted_continuation",
            action_row["candidate"]["reason"],
        )
        self.assertEqual("safety_deferred", action_row["classification"])
        self.assertTrue(action_row["invariants"]["unsafe_continuation"])
        self.assertEqual([12489, 4007], action_row["controlled_hand"])

        position_row = report["decisions"][1]
        self.assertEqual(
            "MovePhase|Battle|0|0|3|",
            position_row["candidate"]["action_identity"],
        )
        self.assertEqual(
            "dominated_turn_defense",
            position_row["candidate"]["reason"],
        )
        self.assertIn(
            "Command|TurnDef|4007|2|0|",
            position_row["tactical"]["filtered_action_identities"],
        )
        self.assertTrue(position_row["invariants"]["legality"])

        unknown_level_row = report["decisions"][2]
        self.assertEqual(
            "Unscorable",
            unknown_level_row["candidate"]["route"],
        )
        self.assertEqual(
            "normal_summon_level_unknown",
            unknown_level_row["candidate"]["reason"],
        )
        self.assertFalse(unknown_level_row["replayable"])
        self.assertEqual("unscorable", unknown_level_row["classification"])
        self.assertTrue(report["admission"]["meaningful_decision_delta"])
        self.assertTrue(report["admission"]["offline_replay_gate_passed"])
        self.assertFalse(report["admission"]["ready_for_live_proof"])
        self.assertIn(
            "different_engine_boundary_not_proven",
            report["admission"]["blocking_reasons"],
        )
        self.assertTrue(report["invariants"]["all_passed"])

    def test_offline_replay_native_fallback_without_identity_is_unscorable(self):
        replay = analyzer._replay_decision_row(
            {},
            {
                "route": "FallbackNative",
                "legal_actions": [
                    {
                        "identity": "MovePhase|End|0|0|5|",
                        "kind": "MovePhase",
                        "command": "End",
                        "phase": "End",
                    }
                ],
            },
        )

        self.assertFalse(replay["replayable"])
        self.assertEqual("unscorable", replay["classification"])
        self.assertEqual("Unscorable", replay["candidate"]["route"])

    def test_offline_replay_candidate_commit_validates_ownership_and_generation(self):
        replay = analyzer._replay_decision_row(
            {},
            {
                "route": "FallbackNative",
                "action_identity": "Command|TurnDef|4007|2|0|",
                "acting_player": 0,
                "owned_seat": 1,
                "my_id": 0,
                "self_hand_card_ids": [4007],
                "legal_actions": [
                    {
                        "identity": "Command|TurnDef|4007|2|0|",
                        "kind": "Command",
                        "command": "TurnDef",
                        "card_id": 4007,
                        "position": 2,
                        "index": 0,
                    },
                    {
                        "identity": "MovePhase|End|0|0|5|",
                        "kind": "MovePhase",
                        "command": "End",
                        "phase": "End",
                    },
                ],
                "tactical_monsters": [
                    {
                        "player": 1,
                        "position": 2,
                        "index": 0,
                        "card_id": 4007,
                        "face_known": True,
                        "face_up": True,
                        "turn_known": True,
                        "is_attack": True,
                        "is_defense": False,
                        "has_atk": True,
                        "has_def": True,
                        "atk": 3000,
                        "def": 2500,
                    },
                    {
                        "player": 0,
                        "position": 2,
                        "index": 0,
                        "card_id": 12522,
                        "face_known": True,
                        "face_up": True,
                        "turn_known": True,
                        "is_attack": True,
                        "is_defense": False,
                        "has_atk": True,
                        "atk": 3000,
                        "def": 2500,
                    },
                ],
            },
        )

        self.assertEqual("RuleCommit", replay["candidate"]["route"])
        self.assertFalse(replay["invariants"]["ownership"])
        self.assertFalse(replay["invariants"]["generation_context"])

    def test_offline_replay_infers_stance_from_legal_turn_command_for_native_fallback(self):
        replay = analyzer._replay_decision_row(
            {},
            {
                "route": "FallbackNative",
                "acting_player": 1,
                "owned_seat": 1,
                "my_id": 0,
                "duel_generation": 4,
                "_segment_duel_generation": 4,
                "self_hand_card_ids": [4007],
                "legal_actions": [
                    {
                        "identity": "Command|TurnDef|12485|2|0|",
                        "kind": "Command",
                        "command": "TurnDef",
                        "card_id": 12485,
                        "position": 2,
                        "index": 0,
                    },
                    {
                        "identity": "MovePhase|Battle|0|0|3|",
                        "kind": "MovePhase",
                        "command": "Attack",
                        "phase": "Battle",
                    },
                    {
                        "identity": "MovePhase|End|0|0|5|",
                        "kind": "MovePhase",
                        "command": "End",
                        "phase": "End",
                    },
                ],
                "tactical_monsters": [
                    {
                        "player": 1,
                        "position": 2,
                        "index": 0,
                        "card_id": 12485,
                        "face_known": True,
                        "face_up": True,
                        "turn_known": False,
                        "atk": 1900,
                        "def": 1200,
                    },
                    {
                        "player": 0,
                        "position": 2,
                        "index": 0,
                        "card_id": 12483,
                        "face_known": True,
                        "face_up": True,
                        "turn_known": False,
                        "atk": 1600,
                        "def": 1600,
                    },
                ],
            },
        )

        self.assertTrue(replay["replayable"])
        self.assertEqual("materially_changed", replay["classification"])
        self.assertEqual("RuleCommit", replay["candidate"]["route"])
        self.assertEqual(
            "MovePhase|Battle|0|0|3|",
            replay["candidate"]["action_identity"],
        )
        self.assertEqual(
            "dominated_turn_defense",
            replay["candidate"]["reason"],
        )
        self.assertEqual(
            ["Command|TurnDef|12485|2|0|"],
            replay["tactical"]["filtered_action_identities"],
        )
        self.assertTrue(replay["invariants"]["legality"])

    def test_offline_replay_applies_allowlisted_opening_defensive_set(self):
        duel = {
            "_opening_set_safety_enabled": True,
            "_opening_set_safety_card_ids": [12293],
        }
        row = {
                "route": "RuleCommit",
                "reason": "fallback",
                "rule_id": "prefer-summon",
                "action_identity": "Command|Summon|12293|13|3|",
                "command": "Summon",
                "card_id": 12293,
                "selected_card_level": 1,
                "acting_player": 1,
                "owned_seat": 1,
                "my_id": 0,
                "duel_generation": 1,
                "_segment_duel_generation": 1,
                "turn": 0,
                "turn_player": 1,
                "phase": 2,
                "window_class": "WaitInput_MainPhase",
                "is_main_phase_wait_input": True,
                "self_field_face_up_card_ids": [],
                "opp_field_face_up_card_ids": [],
                "legal_actions": [
                    {
                        "identity": "Command|Summon|12293|13|3|",
                        "kind": "Command",
                        "command": "Summon",
                        "card_id": 12293,
                        "position": 13,
                        "index": 3,
                        "player": 1,
                        "basic_level": 1,
                        "basic_atk": 300,
                        "basic_def": 1200,
                    },
                    {
                        "identity": "Command|SetMonst|12293|13|3|",
                        "kind": "Command",
                        "command": "SetMonst",
                        "card_id": 12293,
                        "position": 13,
                        "index": 3,
                        "player": 1,
                        "basic_level": 1,
                        "basic_atk": 300,
                        "basic_def": 1200,
                    },
                    {
                        "identity": "MovePhase|End|0|0|5|",
                        "kind": "MovePhase",
                        "command": "Attack",
                        "phase": "End",
                    },
                ],
            }
        replay = analyzer._replay_decision_row(duel, row)

        self.assertTrue(replay["replayable"])
        self.assertEqual("materially_changed", replay["classification"])
        self.assertEqual("RuleCommit", replay["candidate"]["route"])
        self.assertEqual(
            "Command|SetMonst|12293|13|3|",
            replay["candidate"]["action_identity"],
        )
        self.assertEqual(
            "opening_position_safety",
            replay["candidate"]["reason"],
        )
        self.assertTrue(replay["invariants"]["legality"])
        self.assertTrue(replay["invariants"]["unsafe_continuation"])

        report = analyzer.build_offline_replay_report(
            {
                "aggregate": {"completed_duels": 1},
                "duel_count": 1,
                "duels": [{"decision_rows": [row]}],
                "pack": {
                    "opening_set_safety_enabled": True,
                    "opening_set_safety_card_ids": [12293],
                },
                "paths": ["fixture"],
            }
        )
        self.assertEqual(
            1,
            report["replay"].get("opening_set_safety_applied", 0),
        )

    def test_offline_replay_rejects_non_allowlisted_named_opening_set(self):
        replay = analyzer._replay_decision_row(
            {
                "_opening_set_safety_enabled": True,
                "_opening_set_safety_card_ids": [12293],
            },
            {
                "route": "RuleCommit",
                "reason": "opening_position_safety",
                "rule_id": "opening_defensive_set",
                "action_identity": "Command|SetMonst|99999|13|0|",
                "replaced_action_identity": "Command|Summon|99999|13|0|",
                "command": "SetMonst",
                "card_id": 99999,
                "selected_card_level": 1,
                "acting_player": 1,
                "owned_seat": 1,
                "my_id": 0,
                "duel_generation": 1,
                "_segment_duel_generation": 1,
                "turn": 0,
                "turn_player": 1,
                "phase": 2,
                "window_class": "WaitInput_MainPhase",
                "is_main_phase_wait_input": True,
                "legal_actions": [
                    {
                        "identity": "Command|Summon|99999|13|0|",
                        "kind": "Command",
                        "command": "Summon",
                        "card_id": 99999,
                        "position": 13,
                        "index": 0,
                        "player": 1,
                        "basic_level": 1,
                        "basic_atk": 100,
                        "basic_def": 1000,
                    },
                    {
                        "identity": "Command|SetMonst|99999|13|0|",
                        "kind": "Command",
                        "command": "SetMonst",
                        "card_id": 99999,
                        "position": 13,
                        "index": 0,
                        "player": 1,
                        "basic_level": 1,
                        "basic_atk": 100,
                        "basic_def": 1000,
                    },
                ],
            },
        )

        self.assertFalse(
            replay["invariants"].get("opening_set_policy", True)
        )

    def test_analyze_paths_projects_opening_set_policy_from_pack(self):
        temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(temp_dir.cleanup)
        root = Path(temp_dir.name)
        log_path = root / "CampaignCpuAuditLog.jsonl"
        pack_path = root / "11010078.json"
        log_path.write_text("", encoding="utf-8")
        pack_path.write_text(
            json.dumps(
                {
                    "chapter_id": 11010078,
                    "policy": {
                        "opening_set_safety_enabled": True,
                        "opening_set_safety_card_ids": [12293],
                    },
                }
            ),
            encoding="utf-8",
        )

        summary = analyzer.analyze_paths(
            [str(log_path)],
            pack_path=str(pack_path),
        )

        self.assertTrue(summary["pack"]["opening_set_safety_enabled"])
        self.assertEqual(
            [12293],
            summary["pack"]["opening_set_safety_card_ids"],
        )


if __name__ == "__main__":
    unittest.main()

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
                },
                {
                    "event": "campaign_cpu_decision",
                    "rule_id": "mechanical_sel_stand",
                    "route": "MechanicalAuto",
                    "shadow_only": False,
                    "acting_player": 0,
                    "owned_seat": 1,
                    "my_id": 0,
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


if __name__ == "__main__":
    unittest.main()

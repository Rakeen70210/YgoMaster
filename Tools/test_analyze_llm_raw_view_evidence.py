"""Unittest for Tools/analyze_llm_raw_view_evidence.py (LLM-004 Slice 3 remediation)."""

from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).with_name("analyze_llm_raw_view_evidence.py")
spec = importlib.util.spec_from_file_location("analyze_llm_raw_view_evidence", MODULE_PATH)
_analyzer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(_analyzer)
build_report = _analyzer.build_report
load_jsonl = _analyzer.load_jsonl


class AnalyzeLlmRawViewEvidenceTests(unittest.TestCase):
    def test_build_report_includes_raw_evidence_id_and_no_caused_by(self):
        rows = [
            {
                "kind": "llm_raw_duel_view",
                "raw_evidence_id": 3,
                "view_type": "BattleAttack",
                "view_type_id": 12,
                "param1": 0,
                "param2": 1,
                "param3": 0,
                "run_effect_seq": 9,
                "source_signature": "raw_view:id:3:seq:9:12:0:1:0",
                "audit_only": True,
                "broker_history": False,
            },
            {
                "kind": "llm_public_duel_event",
                "history_kind": "life_points_changed",
                "evidence": "public_state_delta",
                "event_id": 4,
                "actor_player": -1,
                "target_player": 1,
                "source_signature": "outcome:lp:seq:9:player:1:from:8000:to:7200:delta:-800",
            },
        ]
        report = build_report(rows)
        self.assertEqual(report["raw_view_count"], 1)
        self.assertEqual(report["normalized_outcome_count"], 1)
        raw0 = report["raw_views"][0]
        self.assertEqual(raw0["raw_evidence_id"], 3)
        self.assertIn("seq:9", raw0["source_signature"])
        outcome0 = report["normalized_outcomes"][0]
        self.assertNotIn("caused_by_event_id", outcome0)
        self.assertEqual(outcome0["source_signature"], rows[1]["source_signature"])

    def test_load_jsonl_and_deterministic_serialization(self):
        payload = [
            {
                "kind": "llm_raw_duel_view",
                "raw_evidence_id": 1,
                "view_type": "CardMove",
                "view_type_id": 5,
                "param1": 0,
                "param2": 0,
                "param3": 0,
                "run_effect_seq": 2,
                "source_signature": "raw_view:id:1:seq:2:5:0:0:0",
            }
        ]
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", delete=False, suffix=".jsonl") as fh:
            for row in payload:
                fh.write(json.dumps(row) + "\n")
            path = fh.name
        try:
            rows = load_jsonl(path)
            text_a = json.dumps(build_report(rows), sort_keys=False, separators=(",", ":"))
            text_b = json.dumps(build_report(rows), sort_keys=False, separators=(",", ":"))
            self.assertEqual(text_a, text_b)
        finally:
            Path(path).unlink(missing_ok=True)

    def test_face_probes_collected_with_coordinates_and_duplicate_ids(self):
        rows = [
            {
                "kind": "llm_face_probe",
                "probe_id": 1,
                "run_effect_seq": 12,
                "player": 0,
                "position": 2,
                "index": 0,
                "raw_face": 1,
                "audit_only": True,
                "broker_history": False,
            },
            {
                "kind": "llm_face_probe",
                "probe_id": 1,
                "run_effect_seq": 12,
                "player": 0,
                "position": 2,
                "index": 0,
                "raw_face": 1,
                "audit_only": True,
                "broker_history": False,
            },
            {
                "kind": "llm_face_probe",
                "probe_id": 2,
                "run_effect_seq": 13,
                "player": 1,
                "position": 0,
                "index": 0,
                "raw_face": 0,
                "audit_only": True,
                "broker_history": False,
            },
            {
                "kind": "llm_raw_duel_view",
                "raw_evidence_id": 9,
                "view_type": "CardFlipTurn",
                "view_type_id": 20,
                "param1": 0,
                "param2": 0,
                "param3": 0,
                "run_effect_seq": 12,
                "source_signature": "raw_view:id:9:seq:12:20:0:0:0",
            },
        ]
        report = build_report(rows)
        self.assertEqual(report["face_probe_count"], 3)
        self.assertEqual(report["duplicate_face_probe_ids"], 1)
        probes = report["face_probes"]
        self.assertEqual(len(probes), 3)
        self.assertEqual(probes[0]["probe_id"], 1)
        self.assertEqual(probes[0]["player"], 0)
        self.assertEqual(probes[0]["position"], 2)
        self.assertEqual(probes[0]["index"], 0)
        self.assertEqual(probes[0]["raw_face"], 1)
        self.assertEqual(probes[0]["run_effect_seq"], 12)
        # Live-validated field mapping + unvalidated banished.
        self.assertEqual(report["dll_face_mapping"]["field"]["dll_raw_face_face_up_public"], 1)
        self.assertEqual(report["dll_face_mapping"]["field"]["dll_raw_face_facedown_or_non_public"], 0)
        self.assertFalse(report["dll_face_mapping"]["banished"]["validated"])
        # No identity fields should be invented by the analyzer.
        for probe in probes:
            self.assertNotIn("card_id", probe)
            self.assertNotIn("card_name", probe)
            self.assertNotIn("card_unique_id", probe)


if __name__ == "__main__":
    unittest.main()
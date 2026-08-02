#!/usr/bin/env python3
import importlib.util
import hashlib
import json
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("analyze_native_cpu_trace.py")
FIXTURE_PATH = (
    Path(__file__).parent
    / "fixtures"
    / "native_cpu_advisor"
    / "ns2_20260801_production.fixture.jsonl"
)
SOURCE_PATH = Path(__file__).parent.parent / "Data" / "ClientData" / "CampaignCpuAuditLog.jsonl"
analyzer = None
if MODULE_PATH.exists():
    spec = importlib.util.spec_from_file_location(
        "analyze_native_cpu_trace", MODULE_PATH
    )
    analyzer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(analyzer)


class NativeCpuTraceAnalyzerTests(unittest.TestCase):
    def _candidate(self, **overrides):
        event = {
            "event": "native_cpu_candidate_trace",
            "duel_dll_sha256": analyzer.SUPPORTED_DUEL_DLL_SHA256,
            "duel_generation": 3,
            "my_id": 0,
            "owned_seat": 1,
            "player": 1,
            "turn": 2,
            "turn_player": 1,
            "phase": 2,
            "candidate_count": 3,
            "selected_index": 1,
            "native_chosen_raw": 0x55667788,
            "native_scores_available": False,
            "read_only": True,
            "candidates": [
                {
                    "index": 0,
                    "raw": 0x11223344,
                    "auxiliary_raw": 1,
                    "is_selected": False,
                },
                {
                    "index": 1,
                    "raw": 0x55667788,
                    "auxiliary_raw": 2,
                    "is_selected": True,
                },
                {
                    "index": 2,
                    "raw": 0x99AABBCC,
                    "auxiliary_raw": 3,
                    "is_selected": False,
                },
            ],
        }
        event.update(overrides)
        return event

    def _session_prefix(self):
        return [
            {
                "event": "native_cpu_trace_hook_ready",
                "ts": "ready",
                "duel_dll_sha256": analyzer.SUPPORTED_DUEL_DLL_SHA256,
                "read_only": True,
                "native_scores_available": False,
            },
            {
                "event": "native_cpu_trace_duel_observed",
                "duel_generation": 3,
                "my_id": 0,
                "owned_seat": 1,
                "game_mode": 0,
                "is_pvp_duel": False,
                "is_pvp_spectator": False,
                "active": True,
                "activation_reason": "active",
                "ts": "observed",
                "duel_dll_sha256": analyzer.SUPPORTED_DUEL_DLL_SHA256,
                "read_only": True,
            },
            {
                "event": "native_cpu_trace_duel_begin",
                "duel_generation": 3,
                "my_id": 0,
                "owned_seat": 1,
                "game_mode": 0,
                "ts": "begin",
                "duel_dll_sha256": analyzer.SUPPORTED_DUEL_DLL_SHA256,
                "read_only": True,
            },
            {
                "event": "native_cpu_trace_hook_invoked",
                "duel_generation": 3,
                "duel_active": True,
                "player": 0,
                "my_id": 0,
                "duel_dll_sha256": analyzer.SUPPORTED_DUEL_DLL_SHA256,
                "read_only": True,
            },
        ]

    def _analyze(self, path, events):
        path.write_text(
            "".join(json.dumps(event) + "\n" for event in events),
            encoding="utf-8",
        )
        return analyzer.analyze_paths([str(path)])

    def test_requires_one_fully_correlated_read_only_session(self):
        self.assertIsNotNone(analyzer, "native CPU trace analyzer must exist")
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "CampaignCpuAuditLog.jsonl"
            events = self._session_prefix() + [
                self._candidate(),
                {
                    "event": "native_cpu_candidate_trace_rejected",
                    "reason": "selected_pointer_mismatch",
                    "duel_generation": 3,
                    "read_only": True,
                },
            ]
            report = self._analyze(path, events)
            self.assertEqual(1, report["valid_trace_count"])
            self.assertIn("correlated_valid_trace_count", report)
            self.assertEqual(1, report["correlated_valid_trace_count"])
            self.assertEqual(1, report["correlated_valid_session_count"])
            self.assertEqual(0, report["invalid_trace_count"])
            self.assertEqual(1, report["rejection_count"])
            self.assertEqual(
                {"selected_pointer_mismatch": 1},
                report["rejection_reasons"],
            )
            self.assertEqual({"active": 1}, report["activation_reasons"])
            self.assertEqual({"0": 1}, report["hook_invocation_players"])
            self.assertTrue(report["ns2_trace_gate_passed"])
            self.assertFalse(report["rerank_authorized"])
            self.assertEqual([], analyzer.validate_requirements(report, True))
            traces = analyzer.collect_correlated_candidate_traces([str(path)])
            self.assertEqual(1, len(traces))
            self.assertEqual("observed", traces[0]["observed_ts"])
            self.assertEqual("begin", traces[0]["begin_ts"])
            self.assertEqual(0, traces[0]["trace_ordinal"])

            broken = dict(self._candidate())
            broken["native_chosen_raw"] = 123
            report = self._analyze(path, self._session_prefix() + [broken])
            self.assertEqual(0, report["valid_trace_count"])
            self.assertEqual(1, report["invalid_trace_count"])
            self.assertIn(
                "native_chosen_mismatch",
                report["invalid_reasons"],
            )
            self.assertFalse(report["ns2_trace_gate_passed"])

            uncorrelated_cases = {
                "missing observed and begin": [
                    self._session_prefix()[0],
                    self._candidate(),
                ],
                "generation mismatch": self._session_prefix()
                + [self._candidate(duel_generation=4)],
                "hash mismatch": self._session_prefix()
                + [
                    self._candidate(
                        duel_dll_sha256="0" * 64,
                    )
                ],
                "seat mismatch": self._session_prefix()
                + [
                    self._candidate(
                        my_id=1,
                        owned_seat=0,
                        player=0,
                    )
                ],
                "local candidate": self._session_prefix()
                + [self._candidate(player=0)],
            }
            for name, case_events in uncorrelated_cases.items():
                with self.subTest(name=name):
                    report = self._analyze(path, case_events)
                    self.assertEqual(
                        0,
                        report["correlated_valid_session_count"],
                    )
                    self.assertFalse(report["ns2_trace_gate_passed"])
                    self.assertNotEqual(
                        [], analyzer.validate_requirements(report, True)
                    )

    def test_correlated_collection_rejects_lifecycle_and_identity_mismatches(self):
        self.assertIsNotNone(analyzer)
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "trace.jsonl"
            valid = self._session_prefix() + [self._candidate()]
            self._analyze(path, valid)
            self.assertEqual(
                1,
                len(analyzer.collect_correlated_candidate_traces([str(path)])),
            )

            cases = {
                "missing begin": [
                    self._session_prefix()[0],
                    self._session_prefix()[1],
                    self._candidate(),
                ],
                "generation mismatch begin": self._session_prefix()[:2]
                + [dict(self._session_prefix()[2], duel_generation=4)]
                + [self._candidate()],
                "hash mismatch begin": self._session_prefix()[:2]
                + [dict(self._session_prefix()[2], duel_dll_sha256="0" * 64)]
                + [self._candidate()],
                "seat mismatch begin": self._session_prefix()[:2]
                + [dict(self._session_prefix()[2], owned_seat=0)]
                + [self._candidate()],
                "wrong ready epoch": [
                    self._session_prefix()[0],
                    dict(self._session_prefix()[0], duel_dll_sha256="0" * 64),
                    *self._session_prefix()[1:],
                    self._candidate(),
                ],
                "malformed selected marker": self._session_prefix()
                + [
                    self._candidate(
                        candidates=[
                            {
                                "index": 0,
                                "raw": 0x11223344,
                                "auxiliary_raw": 1,
                                "is_selected": True,
                            },
                            {
                                "index": 1,
                                "raw": 0x55667788,
                                "auxiliary_raw": 2,
                                "is_selected": True,
                            },
                            {
                                "index": 2,
                                "raw": 0x99AABBCC,
                                "auxiliary_raw": 3,
                                "is_selected": False,
                            },
                        ]
                    )
                ],
            }
            for name, events in cases.items():
                with self.subTest(name=name):
                    self._analyze(path, events)
                    self.assertEqual(
                        [], analyzer.collect_correlated_candidate_traces([str(path)])
                    )

    def test_frozen_ns2_fixture_hash_and_correlation_contract(self):
        self.assertTrue(FIXTURE_PATH.exists(), "NS2 fixture must be frozen")
        self.assertTrue(SOURCE_PATH.exists(), "NS2 source capture must exist")
        raw_source_slice = b"".join(
            SOURCE_PATH.read_bytes().splitlines(keepends=True)[34838:34848]
        )
        self.assertEqual(
            "839ab5ea756ebc6da4cb0ea40268d4231608a10b5cd5f0417bba1cbc556fc07b",
            hashlib.sha256(raw_source_slice).hexdigest(),
        )
        normalized = FIXTURE_PATH.read_bytes().replace(b"\r\n", b"\n")
        self.assertEqual(
            "f26422ce50c8891d30f0a0f789217601b714064d818f510ef51cab23a5065abb",
            hashlib.sha256(normalized).hexdigest(),
        )
        report = analyzer.analyze_paths([str(FIXTURE_PATH)])
        self.assertEqual(1, report["correlated_valid_session_count"])
        self.assertEqual(3, report["correlated_valid_trace_count"])
        self.assertEqual(0, report["invalid_trace_count"])
        self.assertEqual(
            {
                "candidate_count_out_of_range": 1,
                "selected_pointer_mismatch": 2,
            },
            report["rejection_reasons"],
        )
        traces = analyzer.collect_correlated_candidate_traces([str(FIXTURE_PATH)])
        self.assertEqual([0, 1, 2], [trace["trace_ordinal"] for trace in traces])
        self.assertEqual(
            [335622371, 352399587, 369176803],
            [trace["event"]["native_chosen_raw"] for trace in traces],
        )


if __name__ == "__main__":
    unittest.main()

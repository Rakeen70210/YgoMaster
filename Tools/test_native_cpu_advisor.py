#!/usr/bin/env python3
import copy
import contextlib
import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path


TOOLS_DIR = Path(__file__).parent
FIXTURE_DIR = TOOLS_DIR / "fixtures" / "native_cpu_advisor"
MANIFEST_PATH = FIXTURE_DIR / "manifest.json"
PRODUCTION_FIXTURE = FIXTURE_DIR / "ns2_20260801_production.fixture.jsonl"


def _load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


advisor = _load_module("native_cpu_advisor_tests_module", TOOLS_DIR / "native_cpu_advisor.py")
builder = _load_module(
    "build_native_cpu_advisor_corpus_tests_module",
    TOOLS_DIR / "build_native_cpu_advisor_corpus.py",
)
cli = _load_module(
    "analyze_native_cpu_advisor_tests_module",
    TOOLS_DIR / "analyze_native_cpu_advisor.py",
)


EXPECTED_ADVICE_FIELDS = (
    "status",
    "reason",
    "scoreable",
    "recommended_index",
    "native_visible_damage",
    "recommended_visible_damage",
)


class NativeCpuAdvisorTests(unittest.TestCase):
    def _json(self, path):
        return json.loads(path.read_text(encoding="utf-8"))

    def _manifest(self):
        return self._json(MANIFEST_PATH)

    def _wrapper(self, fixture_id):
        entry = next(
            item
            for item in self._manifest()["synthetic_fixtures"]
            if item["fixture_id"] == fixture_id
        )
        return self._json(FIXTURE_DIR / entry["file"])

    def test_all_synthetic_fixtures_match_exact_expectations(self):
        manifest = self._manifest()
        self.assertEqual(11, manifest["min_synthetic_fixture_count"])
        self.assertEqual(11, len(manifest["synthetic_fixtures"]))
        for entry in manifest["synthetic_fixtures"]:
            with self.subTest(fixture_id=entry["fixture_id"]):
                wrapper = self._json(FIXTURE_DIR / entry["file"])
                self.assertEqual(advisor.FIXTURE_SCHEMA, wrapper["schema"])
                self.assertEqual(entry["fixture_id"], wrapper["fixture_id"])
                corpus = wrapper["corpus"]
                self.assertEqual([], advisor.validate_corpus(corpus))
                self.assertEqual(1, len(corpus["decisions"]))
                advice = advisor.advise_decision(
                    corpus["decisions"][0], "synthetic_fixture"
                )
                for field in EXPECTED_ADVICE_FIELDS:
                    self.assertEqual(
                        wrapper["expectations"][field],
                        advice[field],
                        field,
                    )
                self.assertEqual(
                    [
                        "public visible state only",
                        "card effects and hidden responses are not modeled",
                    ],
                    advice["assumptions"],
                )
                self.assertFalse(advice["rerank_authorized"])

    def test_schema_rejects_malformed_types_consistency_and_forbidden_keys(self):
        corpus = copy.deepcopy(self._wrapper("visible_direct_lethal_alternative")["corpus"])
        self.assertEqual([], advisor.validate_corpus(corpus))

        cases = []
        malformed_count = copy.deepcopy(corpus)
        malformed_count["decisions"][0]["candidate_count"] = True
        cases.append(malformed_count)

        malformed_raw = copy.deepcopy(corpus)
        malformed_raw["decisions"][0]["candidates"][0]["raw"] += 1
        cases.append(malformed_raw)

        malformed_marker = copy.deepcopy(corpus)
        malformed_marker["decisions"][0]["candidates"][1]["is_native_selected"] = True
        cases.append(malformed_marker)

        forbidden_nested = copy.deepcopy(corpus)
        forbidden_nested["decisions"][0]["public_state"]["monsters"][0]["card_name"] = "forbidden"
        cases.append(forbidden_nested)

        for malformed in cases:
            with self.subTest(malformed=malformed):
                self.assertNotEqual([], advisor.validate_corpus(malformed))

    def test_advisor_does_not_mutate_decision_and_ignores_raw_auxiliary_for_math(self):
        corpus = self._wrapper("card_identity_invariance_a")["corpus"]
        original = copy.deepcopy(corpus["decisions"][0])
        changed = copy.deepcopy(original)
        for index, candidate in enumerate(changed["candidates"]):
            candidate["auxiliary_raw"] = 0xFFFFFFFF - index
            candidate["raw"] = 0xA0000000 + index
            candidate["word0"] = candidate["raw"] & 0xFFFF
            candidate["word1"] = candidate["raw"] >> 16
        changed["native_selected_raw"] = changed["candidates"][0]["raw"]
        before = copy.deepcopy(changed)
        first = advisor.advise_decision(original, "synthetic_fixture")
        second = advisor.advise_decision(changed, "synthetic_fixture")
        self.assertEqual(
            tuple(first[field] for field in EXPECTED_ADVICE_FIELDS),
            tuple(second[field] for field in EXPECTED_ADVICE_FIELDS),
        )
        self.assertEqual(original, corpus["decisions"][0])
        self.assertEqual(before, changed)

    def test_production_builder_preserves_trace_and_derives_stable_ids(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            output = Path(temp_dir) / "production.json"
            self.assertEqual(
                0,
                builder.main(
                    [str(PRODUCTION_FIXTURE), "--output", str(output)]
                ),
            )
            corpus = self._json(output)
        self.assertEqual([], advisor.validate_corpus(corpus))
        self.assertEqual("ns2_20260801_production_v1", corpus["corpus_id"])
        self.assertEqual(
            "f26422ce50c8891d30f0a0f789217601b714064d818f510ef51cab23a5065abb",
            corpus["source"]["normalized_input_sha256"],
        )
        self.assertEqual(1, corpus["source"]["correlated_session_count"])
        self.assertEqual(3, corpus["source"]["correlated_trace_count"])
        first = corpus["decisions"][0]
        session_id = hashlib.sha256(
            "|".join(
                [
                    "2026-08-01T18:55:59.4536201Z",
                    "2026-08-01T18:55:59.4834463Z",
                    advisor.SUPPORTED_DUEL_DLL_SHA256,
                    "1",
                    "0",
                    "1",
                ]
            ).encode("utf-8")
        ).hexdigest()[:16]
        self.assertEqual(
            [
                f"{session_id}:0000",
                f"{session_id}:0001",
                f"{session_id}:0002",
            ],
            [decision["decision_id"] for decision in corpus["decisions"]],
        )
        self.assertEqual(335622371, first["candidates"][0]["raw"])
        self.assertEqual(12515, first["candidates"][0]["word0"])
        self.assertEqual(5121, first["candidates"][0]["word1"])
        self.assertEqual(95, first["candidates"][0]["auxiliary_raw"])
        for decision in corpus["decisions"]:
            self.assertIsNone(decision["public_state"])
            self.assertFalse(decision["native_scores_available"])
            for candidate in decision["candidates"]:
                self.assertEqual(
                    {
                        "status": "unproven",
                        "action_family": None,
                        "attacker_instance_id": None,
                        "target_instance_id": None,
                        "proof_refs": [],
                    },
                    candidate["semantics"],
                )

    def test_report_baseline_and_strict_gate_exit_codes(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            production = Path(temp_dir) / "production.json"
            report_path = Path(temp_dir) / "report.json"
            decisions_path = Path(temp_dir) / "decisions.jsonl"
            self.assertEqual(
                0,
                builder.main(
                    [str(PRODUCTION_FIXTURE), "--output", str(production)]
                ),
            )
            args = [
                str(production),
                "--fixture-manifest",
                str(MANIFEST_PATH),
                "--decisions-jsonl",
                str(decisions_path),
                "--report",
                str(report_path),
            ]
            self.assertEqual(0, cli.main(args))
            report = self._json(report_path)
            self.assertEqual(
                {
                    "input_corpus_count": 12,
                    "production_decision_count": 3,
                    "production_multi_candidate_count": 2,
                    "production_scoreable_count": 0,
                    "production_recommendation_count": 0,
                    "production_distinct_state_count": 3,
                    "production_recommended_distinct_state_count": 0,
                    "production_status_counts": {
                        "keep_native": 1,
                        "unscorable": 2,
                    },
                    "production_reason_counts": {
                        "public_state_missing": 2,
                        "single_candidate": 1,
                    },
                    "synthetic_decision_count": 11,
                    "synthetic_required_fixture_count": 11,
                    "synthetic_exact_expectation_pass_count": 11,
                    "synthetic_exact_expectation_fail_count": 0,
                    "synthetic_fixture_coverage_passed": True,
                    "card_identity_invariance_passed": True,
                    "validation_error_count": 0,
                    "ns3_advisor_gate_passed": False,
                    "production_feasibility": False,
                    "rerank_authorized": False,
                    "duel_required_next": False,
                },
                {key: report[key] for key in report if key != "schema"},
            )
            self.assertEqual(14, len(decisions_path.read_text().splitlines()))
            self.assertEqual(1, cli.main(args + ["--require-ns3-gate"]))

            invalid = Path(temp_dir) / "invalid.json"
            invalid.write_text("{}", encoding="utf-8")
            with contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(2, cli.main([str(invalid)]))
                self.assertEqual(
                    2,
                    cli.main(
                        [str(FIXTURE_DIR / "visible_direct_lethal_alternative.fixture.json")]
                    ),
                )

    def test_forged_production_corpus_with_genuine_source_metadata_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            production = Path(temp_dir) / "production.json"
            self.assertEqual(
                0,
                builder.main(
                    [str(PRODUCTION_FIXTURE), "--output", str(production)]
                ),
            )
            genuine = self._json(production)

        forged = {
            "schema": genuine["schema"],
            "corpus_id": genuine["corpus_id"],
            "provenance_type": "production_trace",
            "source": copy.deepcopy(genuine["source"]),
            "decisions": [
                copy.deepcopy(
                    self._wrapper("visible_direct_lethal_alternative")["corpus"][
                        "decisions"
                    ][0]
                ),
                copy.deepcopy(
                    self._wrapper("visible_attack_position_lethal_alternative")[
                        "corpus"
                    ]["decisions"][0]
                ),
            ],
        }

        self.assertEqual(
            "f26422ce50c8891d30f0a0f789217601b714064d818f510ef51cab23a5065abb",
            forged["source"]["normalized_input_sha256"],
        )
        self.assertEqual(
            ["production_frozen_corpus_mismatch"], advisor.validate_corpus(forged)
        )
        synthetic_corpora = [
            self._wrapper(entry["fixture_id"])["corpus"]
            for entry in self._manifest()["synthetic_fixtures"]
        ]
        fixture_expectations = [
            {
                "fixture_id": entry["fixture_id"],
                **self._wrapper(entry["fixture_id"])["expectations"],
            }
            for entry in self._manifest()["synthetic_fixtures"]
        ]
        report = advisor.analyze_corpora(
            [forged, *synthetic_corpora], fixture_expectations
        )
        self.assertGreater(report["validation_error_count"], 0)
        self.assertFalse(report["ns3_advisor_gate_passed"])
        self.assertFalse(report["production_feasibility"])
        self.assertFalse(report["rerank_authorized"])
        self.assertFalse(report["duel_required_next"])

    def test_missing_public_state_and_unknown_semantics_follow_order(self):
        for fixture_id, reason in (
            ("missing_public_state", "public_state_missing"),
            ("unknown_native_semantics", "native_semantics_unproven"),
        ):
            with self.subTest(fixture_id=fixture_id):
                wrapper = self._wrapper(fixture_id)
                advice = advisor.advise_decision(
                    wrapper["corpus"]["decisions"][0], "synthetic_fixture"
                )
                self.assertEqual("unscorable", advice["status"])
                self.assertEqual(reason, advice["reason"])
                self.assertFalse(advice["scoreable"])
                self.assertFalse(advice["rerank_authorized"])


if __name__ == "__main__":
    unittest.main()

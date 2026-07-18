#!/usr/bin/env python3
"""YGOMASTER-LLM-005 Slice 4: offline decision-search corpus + evaluator contract.

Corpus lives under Tools/fixtures/llm_search/. Schema version:
  ygomaster.llm_decision_search_corpus.v1

Independent-review regressions (micro-totals, root-regret vs invalid, contradiction
gates, semantic selection, forbidden_hidden_facts surfaces, fixture_oracle labeling)
are locked here. Existing Tools/evaluate_llm_replay.py schema-v3 migration remains separate.
"""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS_DIR = Path(__file__).resolve().parent
FIXTURES_DIR = TOOLS_DIR / "fixtures" / "llm_search"
MANIFEST_PATH = FIXTURES_DIR / "manifest.json"
EVALUATOR_MODULE_PATH = TOOLS_DIR / "evaluate_llm_decision_search.py"
CORPUS_SCHEMA = "ygomaster.llm_decision_search_corpus.v1"

REQUIRED_FAMILIES = frozenset(
    {
        "xyz_setup",
        "destroy_vs_negate_activated_oneshot",
        "remain_face_up_control",
        "value_zero_primary_without_secondary",
        "value_zero_primary_with_secondary",
        "synchro_setup",
        "link_setup_unsupported",
        "lethal_vs_setup",
        "board_wipe_before_development",
        "bait_vs_direct_commitment",
        "preserve_once_per_turn_resource",
        "stop_unknown_opponent_response",
        "phase_sequencing",
        "no_viable_continuation",
        "timeout_incomplete_budget",
        "unknown_semantics_control",
    }
)

MANDATORY_FIXTURE_IDS = frozenset(
    {
        "xyz_girochin_cave_rank4",
        "destroy_vs_negate_dust_foolish_zero_primary",
        "negate_positive_grounded_activation",
        "remain_faceup_continuous_destroy",
        "value_zero_primary_with_secondary_benefit",
        "synchro_exact_tuner_nontuner_level_sum",
        "link_unsupported_linkrating_unavailable",
        "duplicate_extra_deck_identities_distinct",
        "unknown_opponent_response_boundary",
        "timeout_incomplete_budget",
        "no_viable_continuation",
    }
)

GIROCHIN_INTERNAL_ID = 5136
CAVE_DRAGON_INTERNAL_ID = 4103
DUST_TORNADO_INTERNAL_ID = 4988
FOOLISH_BURIAL_GOODS_INTERNAL_ID = 12800

PLAN_THRESHOLDS = {
    "root_coverage_min": 1.0,
    "required_line_recall_min": 0.95,
    "hidden_leaks_max": 0,
    "invalid_selections_max": 0,
    "root_regrets_max": 0,
    "grounded_outcome_contradictions_max": 0,
    "destroy_vs_negate_semantic_pass_min": 1.0,
    "remain_face_up_semantic_pass_min": 1.0,
}


def _load_json(path: Path):
    with path.open("r", encoding="utf-8") as reader:
        return json.load(reader)


def _fixture_paths():
    return sorted(FIXTURES_DIR.glob("*.fixture.json"))


def _load_all_fixtures():
    fixtures = []
    for path in _fixture_paths():
        data = _load_json(path)
        fixtures.append(data)
    return fixtures


def _json_blob(value) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def _hidden_sentinel_strings(fixture) -> list:
    sent = fixture.get("hidden_sentinels") or {}
    out = []
    for key, value in sent.items():
        out.append(str(value))
    return out


# ---------------------------------------------------------------------------
# Corpus artifact tests (must PASS without the evaluator module)
# ---------------------------------------------------------------------------


class CorpusArtifactContractTests(unittest.TestCase):
    """Lock frozen fixtures. These prove RED is not caused by malformed data."""

    def test_fixtures_directory_and_manifest_exist(self):
        self.assertTrue(FIXTURES_DIR.is_dir(), "Tools/fixtures/llm_search/ must exist")
        self.assertTrue(MANIFEST_PATH.is_file(), "manifest.json must exist")

    def test_manifest_schema_and_thresholds(self):
        manifest = _load_json(MANIFEST_PATH)
        self.assertEqual(CORPUS_SCHEMA, manifest.get("schema"))
        self.assertEqual("llm005_slice4_offline_v1", manifest.get("corpus_id"))
        self.assertGreaterEqual(int(manifest.get("min_fixture_count") or 0), 20)
        thresholds = manifest.get("thresholds") or {}
        for key, expected in PLAN_THRESHOLDS.items():
            self.assertEqual(
                expected,
                thresholds.get(key),
                "manifest threshold %s must encode plan gate" % key,
            )
        families = set(manifest.get("families_required") or [])
        self.assertTrue(
            REQUIRED_FAMILIES.issubset(families),
            "manifest missing families: %s" % sorted(REQUIRED_FAMILIES - families),
        )

    def test_minimum_twenty_fixtures_and_unique_ids(self):
        paths = _fixture_paths()
        self.assertGreaterEqual(len(paths), 20, "need at least 20 fixture files")
        fixtures = _load_all_fixtures()
        self.assertEqual(len(paths), len(fixtures))
        ids = [f.get("fixture_id") for f in fixtures]
        self.assertEqual(len(ids), len(set(ids)), "duplicate fixture_id values")
        self.assertTrue(
            MANDATORY_FIXTURE_IDS.issubset(set(ids)),
            "missing mandatory fixtures: %s" % sorted(MANDATORY_FIXTURE_IDS - set(ids)),
        )

    def test_every_fixture_has_required_top_level_fields(self):
        for fixture in _load_all_fixtures():
            fid = fixture.get("fixture_id")
            with self.subTest(fixture_id=fid):
                self.assertEqual(CORPUS_SCHEMA, fixture.get("schema"), fid)
                self.assertIsInstance(fixture.get("family"), str)
                self.assertTrue(fixture.get("family"))
                self.assertIsInstance(fixture.get("decision_snapshot"), dict)
                self.assertIsInstance(fixture.get("expectations"), dict)
                self.assertIsInstance(fixture.get("hidden_sentinels"), dict)
                self.assertIsInstance(fixture.get("provider_visible"), dict)
                has_proj = fixture.get("search_projection") is not None
                has_unsup = fixture.get("unsupported_reason") is not None
                self.assertTrue(
                    has_proj or has_unsup,
                    "%s needs search_projection and/or unsupported_reason" % fid,
                )

    def test_every_required_family_represented(self):
        families = {f.get("family") for f in _load_all_fixtures()}
        missing = REQUIRED_FAMILIES - families
        self.assertFalse(missing, "missing corpus families: %s" % sorted(missing))

    def test_expectations_shape(self):
        required_keys = {
            "required_root_action_ids",
            "required_candidate_lines",
            "forbidden_hidden_facts",
            "acceptable_selected_roots",
            "forbidden_rationale_claims",
            "required_outcome_properties",
            "required_score_rationale_properties",
            "coverage_budget_limits",
            "decline",
        }
        for fixture in _load_all_fixtures():
            exp = fixture["expectations"]
            with self.subTest(fixture_id=fixture["fixture_id"]):
                missing = required_keys - set(exp.keys())
                self.assertFalse(missing, "expectations missing keys: %s" % sorted(missing))
                self.assertIsInstance(exp["required_root_action_ids"], list)
                self.assertIsInstance(exp["acceptable_selected_roots"], list)
                self.assertGreaterEqual(len(exp["acceptable_selected_roots"]), 1)
                self.assertIsInstance(exp["decline"], dict)
                self.assertIn("acceptable", exp["decline"])
                self.assertIn("required", exp["decline"])

    def test_hidden_sentinels_isolated_from_provider_visible(self):
        for fixture in _load_all_fixtures():
            fid = fixture["fixture_id"]
            sentinels = _hidden_sentinel_strings(fixture)
            self.assertGreaterEqual(len(sentinels), 4, fid)
            visible = _json_blob(fixture["provider_visible"])
            snapshot = _json_blob(fixture["decision_snapshot"])
            projection = _json_blob(fixture.get("search_projection") or {})
            with self.subTest(fixture_id=fid):
                for token in sentinels:
                    self.assertNotIn(token, visible, "provider_visible leaks %s" % token)
                    self.assertNotIn(token, snapshot, "decision_snapshot leaks %s" % token)
                    self.assertNotIn(token, projection, "search_projection leaks %s" % token)
                # Forbidden list must include sentinels
                forbidden = set(fixture["expectations"].get("forbidden_hidden_facts") or [])
                for token in sentinels:
                    self.assertIn(token, forbidden, "forbidden_hidden_facts must list %s" % token)

    def test_future_steps_not_commit_eligible_in_frozen_projections(self):
        for fixture in _load_all_fixtures():
            proj = fixture.get("search_projection")
            if not proj:
                continue
            fid = fixture["fixture_id"]
            for line in proj.get("lines") or []:
                steps = line.get("steps") or []
                for index, step in enumerate(steps):
                    if index == 0:
                        continue
                    with self.subTest(fixture_id=fid, line_id=line.get("line_id"), step=index):
                        self.assertIs(step.get("commit_eligible"), False)
                        self.assertIs(step.get("current_legal"), False)

    def test_mandatory_girochin_cave_xyz_fixture(self):
        fixture = next(
            f for f in _load_all_fixtures() if f["fixture_id"] == "xyz_girochin_cave_rank4"
        )
        blob = _json_blob(fixture)
        self.assertIn(str(GIROCHIN_INTERNAL_ID), blob)
        self.assertIn(str(CAVE_DRAGON_INTERNAL_ID), blob)
        exp = fixture["expectations"]
        self.assertEqual([0, 1, 2], exp["required_root_action_ids"])
        self.assertEqual([0, 1, 2], exp["acceptable_selected_roots"])
        lines = exp["required_candidate_lines"]
        self.assertTrue(any("xyz" in (L.get("line_id") or "").lower() or
                            any("Xyz" in s for s in (L.get("must_contain_step_substrings") or []))
                            for L in lines))
        # Not a brittle always-pick single root
        self.assertGreater(len(exp["acceptable_selected_roots"]), 1)

    def test_mandatory_dust_foolish_zero_primary_fixture(self):
        fixture = next(
            f
            for f in _load_all_fixtures()
            if f["fixture_id"] == "destroy_vs_negate_dust_foolish_zero_primary"
        )
        blob = _json_blob(fixture["decision_snapshot"])
        self.assertIn(str(DUST_TORNADO_INTERNAL_ID), blob)
        self.assertIn(str(FOOLISH_BURIAL_GOODS_INTERNAL_ID), blob)
        exp = fixture["expectations"]
        self.assertTrue(exp["decline"]["required"])
        self.assertEqual([1], exp["acceptable_selected_roots"])
        by_root = (exp["required_outcome_properties"].get("by_root_action_id") or {}).get("0")
        self.assertIsNotNone(by_root)
        self.assertIs(by_root.get("primary_effect_stopped"), False)
        self.assertIs(by_root.get("target_effect_expected_to_resolve"), True)
        self.assertEqual(0, by_root.get("primary_disruption_value"))
        claims = " ".join(exp["forbidden_rationale_claims"]).lower()
        self.assertIn("negate", claims)

    def test_manifest_lists_every_fixture_file(self):
        manifest = _load_json(MANIFEST_PATH)
        listed = {entry["file"] for entry in manifest.get("fixtures") or []}
        on_disk = {path.name for path in _fixture_paths()}
        self.assertEqual(listed, on_disk)


# ---------------------------------------------------------------------------
# Evaluator module / API contract (intentional RED until GREEN implementation)
# ---------------------------------------------------------------------------


def _import_evaluator():
    """Load Tools/evaluate_llm_decision_search.py or raise a clear RED error."""
    if not EVALUATOR_MODULE_PATH.is_file():
        raise FileNotFoundError(
            "missing offline evaluator module: %s "
            "(YGOMASTER-LLM-005 Slice 4 RED expects evaluate_llm_decision_search.py)"
            % EVALUATOR_MODULE_PATH
        )
    spec = importlib.util.spec_from_file_location(
        "evaluate_llm_decision_search", EVALUATOR_MODULE_PATH
    )
    if spec is None or spec.loader is None:
        raise ImportError("cannot load evaluate_llm_decision_search from %s" % EVALUATOR_MODULE_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class EvaluatorModuleContractTests(unittest.TestCase):
    def test_evaluator_module_exists(self):
        self.assertTrue(
            EVALUATOR_MODULE_PATH.is_file(),
            "Tools/evaluate_llm_decision_search.py must exist (Slice 4 offline evaluator)",
        )

    def test_evaluator_exports_pure_callable_api(self):
        evaluator = _import_evaluator()
        required = [
            "CORPUS_SCHEMA",
            "DEFAULT_THRESHOLDS",
            "load_corpus",
            "validate_corpus",
            "validate_fixture",
            "evaluate_fixture",
            "evaluate_corpus",
            "compare_one_step_vs_lookahead",
            "aggregate_metrics",
            "gates_pass",
            "main",
        ]
        for name in required:
            self.assertTrue(
                hasattr(evaluator, name),
                "evaluate_llm_decision_search must export %s" % name,
            )
            if name not in ("CORPUS_SCHEMA", "DEFAULT_THRESHOLDS"):
                self.assertTrue(
                    callable(getattr(evaluator, name)),
                    "%s must be callable" % name,
                )
        self.assertEqual(CORPUS_SCHEMA, evaluator.CORPUS_SCHEMA)
        for key, expected in PLAN_THRESHOLDS.items():
            self.assertEqual(
                expected,
                evaluator.DEFAULT_THRESHOLDS.get(key),
                "DEFAULT_THRESHOLDS[%s]" % key,
            )


class CorpusValidationApiTests(unittest.TestCase):
    """Corpus/data validation is offline and independent of provider availability."""

    def setUp(self):
        self.evaluator = _import_evaluator()

    def test_load_and_validate_frozen_corpus_offline(self):
        fixtures = self.evaluator.load_corpus(FIXTURES_DIR)
        self.assertGreaterEqual(len(fixtures), 20)
        result = self.evaluator.validate_corpus(fixtures)
        # Accept either raising API or structured result with ok/errors.
        if isinstance(result, dict):
            self.assertTrue(
                result.get("ok") is True or result.get("valid") is True,
                "validate_corpus must accept the frozen corpus: %s" % result,
            )
            self.assertEqual([], result.get("errors") or [])
        else:
            self.assertTrue(result)

    def test_rejects_duplicate_fixture_ids(self):
        fixtures = self.evaluator.load_corpus(FIXTURES_DIR)
        self.assertGreaterEqual(len(fixtures), 1)
        dup = json.loads(json.dumps(fixtures[0]))
        fixtures_with_dup = list(fixtures) + [dup]
        errors = self.evaluator.validate_corpus(fixtures_with_dup)
        if isinstance(errors, dict):
            self.assertFalse(errors.get("ok") or errors.get("valid"))
            joined = _json_blob(errors).lower()
        else:
            joined = _json_blob(errors).lower() if errors else ""
            self.assertTrue(errors)
        self.assertIn("duplicate", joined)

    def test_rejects_hidden_sentinels_in_provider_visible(self):
        fixtures = self.evaluator.load_corpus(FIXTURES_DIR)
        bad = json.loads(json.dumps(fixtures[0]))
        sentinel = bad["hidden_sentinels"]["opponent_hand_name"]
        bad["provider_visible"]["leaked"] = sentinel
        errors = self.evaluator.validate_fixture(bad)
        if isinstance(errors, dict):
            self.assertTrue(errors.get("errors") or not (errors.get("ok") or errors.get("valid")))
            joined = _json_blob(errors).lower()
        else:
            self.assertTrue(errors)
            joined = _json_blob(errors).lower()
        self.assertTrue(
            "hidden" in joined or "sentinel" in joined or "leak" in joined or sentinel.lower() in joined,
            "must reject hidden sentinel in provider_visible: %s" % joined,
        )

    def test_rejects_missing_required_roots(self):
        fixtures = self.evaluator.load_corpus(FIXTURES_DIR)
        bad = json.loads(json.dumps(fixtures[0]))
        # Drop a required root from projection while expectations still require it.
        req = list(bad["expectations"]["required_root_action_ids"])
        self.assertTrue(req)
        if bad.get("search_projection") and bad["search_projection"].get("lines"):
            bad["search_projection"]["lines"] = [
                line
                for line in bad["search_projection"]["lines"]
                if not (line.get("is_root_shell") and line.get("root_action_id") == req[0])
            ]
        errors = self.evaluator.validate_fixture(bad)
        joined = _json_blob(errors).lower()
        self.assertTrue(errors)
        self.assertTrue(
            "root" in joined or "missing" in joined,
            "must reject missing required roots: %s" % joined,
        )

    def test_rejects_future_steps_mislabeled_commit_eligible(self):
        fixtures = self.evaluator.load_corpus(FIXTURES_DIR)
        target = None
        for fixture in fixtures:
            for line in (fixture.get("search_projection") or {}).get("lines") or []:
                if line.get("steps") and len(line["steps"]) > 1:
                    target = json.loads(json.dumps(fixture))
                    break
            if target:
                break
        self.assertIsNotNone(target, "corpus needs a multi-step line to mutate")
        for line in target["search_projection"]["lines"]:
            if line.get("steps") and len(line["steps"]) > 1:
                line["steps"][1]["commit_eligible"] = True
                line["steps"][1]["current_legal"] = True
                break
        errors = self.evaluator.validate_fixture(target)
        joined = _json_blob(errors).lower()
        self.assertTrue(errors)
        self.assertTrue(
            "commit" in joined or "future" in joined or "legal" in joined,
            "must reject future commit-eligible steps: %s" % joined,
        )

    def test_rejects_contradictory_grounded_outcomes(self):
        fixtures = self.evaluator.load_corpus(FIXTURES_DIR)
        dust = next(
            f
            for f in fixtures
            if f["fixture_id"] == "destroy_vs_negate_dust_foolish_zero_primary"
        )
        bad = json.loads(json.dumps(dust))
        for line in bad["search_projection"]["lines"]:
            if line.get("root_action_id") == 0 and line.get("immediate_outcome"):
                line["immediate_outcome"]["primary_effect_stopped"] = True
                line["immediate_outcome"]["target_effect_expected_to_resolve"] = True
                line["immediate_outcome"]["primary_disruption_value"] = 0
        errors = self.evaluator.validate_fixture(bad)
        joined = _json_blob(errors).lower()
        self.assertTrue(errors)
        self.assertTrue(
            "contradict" in joined or "outcome" in joined or "primary_effect" in joined,
            "must reject contradictory grounded outcomes: %s" % joined,
        )

    def test_rejects_unsupported_reason_mismatch(self):
        fixtures = self.evaluator.load_corpus(FIXTURES_DIR)
        link = next(
            f for f in fixtures if f["fixture_id"] == "link_unsupported_linkrating_unavailable"
        )
        bad = json.loads(json.dumps(link))
        bad["unsupported_reason"] = "totally_wrong_reason"
        errors = self.evaluator.validate_fixture(bad)
        joined = _json_blob(errors).lower()
        self.assertTrue(errors)
        self.assertTrue(
            "unsupported" in joined or "mismatch" in joined or "reason" in joined,
            "must reject unsupported_reason mismatch: %s" % joined,
        )


class EvaluationMetricsApiTests(unittest.TestCase):
    def setUp(self):
        self.evaluator = _import_evaluator()
        self.fixtures = self.evaluator.load_corpus(FIXTURES_DIR)

    def _oracle_callback(self, fixture):
        """Legal callback that always picks an acceptable root (no hidden facts)."""

        def _cb(_provider_visible):
            roots = fixture["expectations"]["acceptable_selected_roots"]
            action_id = roots[0]
            return {
                "action_id": action_id,
                "reason": "oracle acceptable root %s" % action_id,
                "selected_line_id": None,
                "latency_ms": 1,
                "token_cost": 10,
                "confidence": 0.5,
            }

        return _cb

    def test_evaluate_fixture_metrics_shape(self):
        fixture = next(f for f in self.fixtures if f["fixture_id"] == "xyz_girochin_cave_rank4")
        result = self.evaluator.evaluate_fixture(fixture, self._oracle_callback(fixture))
        self.assertIsInstance(result, dict)
        for key in (
            "fixture_id",
            "root_coverage",
            "required_line_recall",
            "hidden_information_violations",
            "invalid_selection",
            "grounded_outcome_contradictions",
            "latency_ms",
            "token_cost",
            "selected_action_id",
            "findings",
        ):
            self.assertIn(key, result, "evaluate_fixture missing %s" % key)

    def test_evaluate_corpus_aggregate_and_gates(self):
        def router(provider_visible):
            seq = provider_visible.get("run_effect_seq")
            fixture = next(
                f for f in self.fixtures if f["decision_snapshot"]["run_effect_seq"] == seq
            )
            return self._oracle_callback(fixture)(provider_visible)

        report = self.evaluator.evaluate_corpus(
            self.fixtures, router, thresholds=PLAN_THRESHOLDS
        )
        self.assertIsInstance(report, dict)
        self.assertIn("metrics", report)
        self.assertIn("fixtures", report)
        metrics = report["metrics"]
        for key in (
            "root_coverage",
            "required_line_recall",
            "hidden_leaks",
            "invalid_selections",
            "destroy_vs_negate_semantic_pass",
            "remain_face_up_semantic_pass",
        ):
            self.assertIn(key, metrics, "aggregate metrics missing %s" % key)
        # Determinism: second run identical JSON
        report2 = self.evaluator.evaluate_corpus(
            self.fixtures, router, thresholds=PLAN_THRESHOLDS
        )
        self.assertEqual(
            json.dumps(report, sort_keys=True, separators=(",", ":")),
            json.dumps(report2, sort_keys=True, separators=(",", ":")),
            "evaluate_corpus JSON must be deterministic",
        )
        self.assertTrue(self.evaluator.gates_pass(report))

    def test_root_regret_against_acceptable_set(self):
        fixture = next(
            f
            for f in self.fixtures
            if f["fixture_id"] == "destroy_vs_negate_dust_foolish_zero_primary"
        )

        def bad_cb(_pv):
            return {
                "action_id": 0,  # activate Dust — not acceptable when decline required
                "reason": "clear backrow before follow-ups resolve",
                "latency_ms": 2,
                "token_cost": 12,
            }

        result = self.evaluator.evaluate_fixture(fixture, bad_cb)
        self.assertTrue(
            result.get("invalid_selection")
            or result.get("root_regret")
            or (result.get("findings") and len(result["findings"]) > 0),
            "selecting non-acceptable root must produce regret/invalid finding",
        )

    def test_hidden_information_violation_detected(self):
        fixture = next(f for f in self.fixtures if f["fixture_id"] == "xyz_girochin_cave_rank4")
        sentinel = fixture["hidden_sentinels"]["opponent_hand_name"]

        def leaky_cb(_pv):
            return {
                "action_id": fixture["expectations"]["acceptable_selected_roots"][0],
                "reason": "I know opponent holds %s" % sentinel,
                "latency_ms": 1,
                "token_cost": 10,
            }

        result = self.evaluator.evaluate_fixture(fixture, leaky_cb)
        self.assertGreaterEqual(int(result.get("hidden_information_violations") or 0), 1)

    def test_grounded_outcome_contradiction_in_rationale(self):
        fixture = next(
            f
            for f in self.fixtures
            if f["fixture_id"] == "destroy_vs_negate_dust_foolish_zero_primary"
        )

        def contradict_cb(_pv):
            return {
                "action_id": 1,
                "reason": "Dust Tornado negates Foolish Burial Goods and stops the effect",
                "latency_ms": 1,
                "token_cost": 10,
            }

        result = self.evaluator.evaluate_fixture(fixture, contradict_cb)
        self.assertGreaterEqual(int(result.get("grounded_outcome_contradictions") or 0), 1)


class ComparisonCallbackTests(unittest.TestCase):
    def setUp(self):
        self.evaluator = _import_evaluator()
        self.fixtures = self.evaluator.load_corpus(FIXTURES_DIR)
        self.fixture = next(
            f
            for f in self.fixtures
            if f["fixture_id"] == "comparison_callback_does_not_force_choice"
        )

    def test_compare_one_step_vs_lookahead_records_deltas(self):
        def one_step(pv):
            return {
                "action_id": 0,
                "reason": "enter battle immediately",
                "selected_line_id": None,
                "latency_ms": 3,
                "token_cost": 20,
            }

        def lookahead(pv):
            return {
                "action_id": 2,
                "reason": "flip for Rank 4 Xyz line",
                "selected_line_id": "line:flip_xyz",
                "latency_ms": 8,
                "token_cost": 45,
            }

        # Same frozen snapshot/provider-visible payload for both callbacks.
        comparison = self.evaluator.compare_one_step_vs_lookahead(
            self.fixture, one_step, lookahead
        )
        self.assertIsInstance(comparison, dict)
        for key in (
            "fixture_id",
            "one_step",
            "lookahead",
            "deltas",
        ):
            self.assertIn(key, comparison)
        deltas = comparison["deltas"]
        for field in ("action_id", "reason", "line_id", "latency_ms", "token_cost"):
            self.assertIn(field, deltas, "comparison deltas missing %s" % field)
        self.assertNotEqual(
            comparison["one_step"].get("action_id"),
            comparison["lookahead"].get("action_id"),
            "fixture must allow callbacks to disagree so evaluator does not force choice",
        )

    def test_evaluator_does_not_force_expected_choice(self):
        """Both callbacks returning different acceptable roots must both be valid evaluations."""

        def pick(action_id):
            def _cb(_pv):
                return {
                    "action_id": action_id,
                    "reason": "callback chose %s" % action_id,
                    "latency_ms": 1,
                    "token_cost": 5,
                }

            return _cb

        acceptable = self.fixture["expectations"]["acceptable_selected_roots"]
        self.assertGreaterEqual(len(acceptable), 2)
        r0 = self.evaluator.evaluate_fixture(self.fixture, pick(acceptable[0]))
        r1 = self.evaluator.evaluate_fixture(self.fixture, pick(acceptable[1]))
        self.assertFalse(r0.get("invalid_selection"))
        self.assertFalse(r1.get("invalid_selection"))
        self.assertEqual(acceptable[0], r0.get("selected_action_id"))
        self.assertEqual(acceptable[1], r1.get("selected_action_id"))


class CliContractTests(unittest.TestCase):
    def test_cli_nonzero_when_gates_fail_and_json_deterministic(self):
        self.assertTrue(
            EVALUATOR_MODULE_PATH.is_file(),
            "CLI requires Tools/evaluate_llm_decision_search.py",
        )

        def run_cli(extra_args):
            cmd = [
                sys.executable,
                str(EVALUATOR_MODULE_PATH),
                "--corpus",
                str(FIXTURES_DIR),
                "--json",
            ] + extra_args
            return subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                check=False,
            )

        # Oracle-less / empty callback path should still produce deterministic JSON;
        # force a failing gate via a deliberate bad-callback flag if exposed, else
        # use --provider force_invalid if implemented. Fall back to calling main().
        evaluator = _import_evaluator()

        def always_invalid(_pv):
            return {
                "action_id": 999999,
                "reason": "illegal",
                "latency_ms": 1,
                "token_cost": 1,
            }

        fixtures = evaluator.load_corpus(FIXTURES_DIR)
        report = evaluator.evaluate_corpus(fixtures, always_invalid, thresholds=PLAN_THRESHOLDS)
        self.assertFalse(evaluator.gates_pass(report))

        # main() must return nonzero when gates fail
        with tempfile.TemporaryDirectory() as tmp:
            out_path = Path(tmp) / "report.json"
            # Prefer CLI argv contract:
            #   evaluate_llm_decision_search.py --corpus DIR --json [--out PATH]
            # Implementation may also accept a callback factory; for RED we only
            # require that main exists and failing gates => nonzero.
            rc = evaluator.main(
                [
                    "--corpus",
                    str(FIXTURES_DIR),
                    "--json",
                    "--out",
                    str(out_path),
                    "--callback",
                    "always_invalid",
                ]
            )
            self.assertNotEqual(0, rc, "CLI must return nonzero when gates fail")
            if out_path.is_file():
                text1 = out_path.read_text(encoding="utf-8")
                rc2 = evaluator.main(
                    [
                        "--corpus",
                        str(FIXTURES_DIR),
                        "--json",
                        "--out",
                        str(out_path),
                        "--callback",
                        "always_invalid",
                    ]
                )
                self.assertNotEqual(0, rc2)
                text2 = out_path.read_text(encoding="utf-8")
                self.assertEqual(text1, text2, "CLI JSON output must be deterministic")


class ReplayEvaluatorRemainsSeparateTests(unittest.TestCase):
    def test_evaluate_llm_replay_still_owns_schema_v3_migration(self):
        replay_path = TOOLS_DIR / "evaluate_llm_replay.py"
        self.assertTrue(replay_path.is_file())
        spec = importlib.util.spec_from_file_location("evaluate_llm_replay", replay_path)
        replay = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(replay)
        self.assertTrue(
            callable(getattr(replay, "migrate_schema_v3_request", None))
            or callable(getattr(replay, "accept_schema_v3_replay_fixture", None)),
            "evaluate_llm_replay keeps explicit schema-v3 migration; "
            "decision-search evaluator must remain a separate module",
        )
        # Decision-search module must not be the same file.
        self.assertNotEqual(EVALUATOR_MODULE_PATH.resolve(), replay_path.resolve())


# ---------------------------------------------------------------------------
# Independent-review regressions (must fail until evaluator micro-totals GREEN)
# ---------------------------------------------------------------------------


class IndependentReviewRegressionTests(unittest.TestCase):
    """Locks review findings that the first GREEN incorrectly averaged or conflated."""

    def setUp(self):
        self.evaluator = _import_evaluator()
        self.fixtures = self.evaluator.load_corpus(FIXTURES_DIR)

    def _oracle(self, fixture):
        def _cb(_pv):
            roots = fixture["expectations"]["acceptable_selected_roots"]
            return {
                "action_id": roots[0],
                "reason": "oracle acceptable root %s" % roots[0],
                "latency_ms": 1,
                "token_cost": 10,
            }

        return _cb

    def test_thresholds_include_root_regret_and_contradiction_max(self):
        for key in ("root_regrets_max", "grounded_outcome_contradictions_max"):
            self.assertIn(key, PLAN_THRESHOLDS)
            self.assertEqual(0, PLAN_THRESHOLDS[key])
            self.assertEqual(
                0,
                self.evaluator.DEFAULT_THRESHOLDS.get(key),
                "DEFAULT_THRESHOLDS must gate %s=0" % key,
            )
        manifest = _load_json(MANIFEST_PATH)
        thresholds = manifest.get("thresholds") or {}
        self.assertEqual(0, thresholds.get("root_regrets_max"))
        self.assertEqual(0, thresholds.get("grounded_outcome_contradictions_max"))

    def test_per_fixture_exposes_micro_totals_for_roots_and_lines(self):
        fixture = next(
            f for f in self.fixtures if f["fixture_id"] == "xyz_girochin_cave_rank4"
        )
        result = self.evaluator.evaluate_fixture(fixture, self._oracle(fixture))
        for key in (
            "root_required",
            "root_represented",
            "line_required",
            "line_matched",
        ):
            self.assertIn(key, result, "evaluate_fixture must expose micro total %s" % key)
        self.assertEqual(3, result["root_required"])
        self.assertEqual(3, result["root_represented"])
        self.assertGreaterEqual(result["line_required"], 1)
        self.assertEqual(result["line_required"], result["line_matched"])

    def test_aggregate_line_recall_is_micro_total_not_average(self):
        """Empty-requirement fixtures must not mask a single missed required line.

        Construct a corpus with many zero-requirement fixtures (each would score 1.0
        under naive averaging) plus one fixture whose required candidate line is
        removed from the projection. Micro-total recall must drop below 0.95 and
        fail gates even though the average of per-fixture ratios would stay high.
        """
        xyz = next(f for f in self.fixtures if f["fixture_id"] == "xyz_girochin_cave_rank4")
        empty_pool = [
            f
            for f in self.fixtures
            if not (f.get("expectations") or {}).get("required_candidate_lines")
        ]
        self.assertGreaterEqual(len(empty_pool), 5, "need empty-requirement fixtures")

        broken = json.loads(json.dumps(xyz))
        req_lines = list(broken["expectations"]["required_candidate_lines"])
        self.assertTrue(req_lines)
        victim_id = req_lines[0]["line_id"]
        broken["search_projection"]["lines"] = [
            line
            for line in broken["search_projection"]["lines"]
            if line.get("line_id") != victim_id
        ]

        corpus = empty_pool[:10] + [broken]

        def router(pv):
            seq = pv.get("run_effect_seq")
            for f in corpus:
                if f["decision_snapshot"].get("run_effect_seq") == seq:
                    return self._oracle(f)(pv)
            return {"action_id": 0, "reason": "fallback", "latency_ms": 0, "token_cost": 0}

        report = self.evaluator.evaluate_corpus(
            corpus, router, thresholds=PLAN_THRESHOLDS
        )
        metrics = report["metrics"]
        self.assertIn("line_required", metrics)
        self.assertIn("line_matched", metrics)
        self.assertIn("root_required", metrics)
        self.assertIn("root_represented", metrics)
        self.assertGreaterEqual(metrics["line_required"], 1)
        # Micro-total: 0 matched of >=1 required => recall 0 (or low), not ~0.9x from averaging.
        self.assertEqual(0, metrics["line_matched"])
        self.assertLess(metrics["required_line_recall"], 0.95)
        # Naive average would be ~10/11 >= 0.90; ensure we are not that soft.
        naive_avg_floor = float(len(empty_pool[:10])) / float(len(corpus))
        self.assertGreaterEqual(naive_avg_floor, 0.90)
        self.assertLess(
            metrics["required_line_recall"],
            naive_avg_floor,
            "micro-total recall must not equal/exceed naive average that masks the miss",
        )
        self.assertFalse(self.evaluator.gates_pass(report))

    def test_legal_outside_acceptable_is_root_regret_not_invalid_selection(self):
        fixture = next(
            f
            for f in self.fixtures
            if f["fixture_id"] == "destroy_vs_negate_dust_foolish_zero_primary"
        )

        def dust_cb(_pv):
            return {
                "action_id": 0,  # legal activate; not acceptable when decline required
                "reason": "clear the backrow",
                "latency_ms": 1,
                "token_cost": 5,
            }

        result = self.evaluator.evaluate_fixture(fixture, dust_cb)
        self.assertTrue(result.get("root_regret"), "Dust action 0 must be root regret")
        self.assertFalse(
            result.get("invalid_selection"),
            "legal current action outside acceptable set is regret, not invalid_selection",
        )
        self.assertEqual(0, result.get("selected_action_id"))

        report = self.evaluator.evaluate_corpus(
            [fixture], dust_cb, thresholds=PLAN_THRESHOLDS
        )
        self.assertIn("root_regrets", report["metrics"])
        self.assertEqual(1, report["metrics"]["root_regrets"])
        self.assertEqual(0, report["metrics"]["invalid_selections"])
        self.assertFalse(self.evaluator.gates_pass(report))
        # Semantic pass must also fail: selected root incompatible with decline required.
        self.assertFalse(result.get("destroy_vs_negate_semantic_pass"))

    def test_truly_illegal_action_is_invalid_selection(self):
        fixture = next(
            f
            for f in self.fixtures
            if f["fixture_id"] == "destroy_vs_negate_dust_foolish_zero_primary"
        )

        def illegal_cb(_pv):
            return {
                "action_id": 999999,
                "reason": "not a legal root",
                "latency_ms": 1,
                "token_cost": 1,
            }

        result = self.evaluator.evaluate_fixture(fixture, illegal_cb)
        self.assertTrue(result.get("invalid_selection"))
        self.assertFalse(
            result.get("root_regret"),
            "unparseable/illegal action is invalid_selection, not root_regret",
        )

    def test_contradictory_rationale_on_acceptable_root_fails_gates(self):
        fixture = next(
            f
            for f in self.fixtures
            if f["fixture_id"] == "destroy_vs_negate_dust_foolish_zero_primary"
        )

        phrases = [
            "Dust Tornado negates Foolish Burial Goods",
            "this response stops the effect",
            "the activation is interrupted",
            "Dust Tornado prevents resolution of the activated spell",
            "Foolish Burial Goods will not resolve",
        ]
        for phrase in phrases:
            with self.subTest(phrase=phrase):

                def cb(_pv, p=phrase):
                    return {
                        "action_id": 1,  # acceptable decline root
                        "reason": p,
                        "latency_ms": 1,
                        "token_cost": 8,
                    }

                result = self.evaluator.evaluate_fixture(fixture, cb)
                self.assertFalse(result.get("invalid_selection"))
                self.assertFalse(result.get("root_regret"))
                self.assertGreaterEqual(
                    int(result.get("grounded_outcome_contradictions") or 0),
                    1,
                    "must count grounded_outcome_contradictions for phrase %r" % phrase,
                )
                report = self.evaluator.evaluate_corpus(
                    [fixture], cb, thresholds=PLAN_THRESHOLDS
                )
                self.assertIn("grounded_outcome_contradictions", report["metrics"])
                self.assertGreaterEqual(
                    report["metrics"]["grounded_outcome_contradictions"], 1
                )
                self.assertFalse(
                    self.evaluator.gates_pass(report),
                    "gates must fail when contradictions > grounded_outcome_contradictions_max",
                )

    def test_forbidden_hidden_facts_not_only_sentinels_rejected_in_provider_visible(self):
        fixtures = self.evaluator.load_corpus(FIXTURES_DIR)
        bad = json.loads(json.dumps(fixtures[0]))
        synthetic = "SYNTHETIC_FORBIDDEN_HIDDEN_FACT_LLM005_S4_REVIEW"
        # Ensure synthetic is not already a sentinel value.
        for value in (bad.get("hidden_sentinels") or {}).values():
            self.assertNotEqual(synthetic, str(value))
        facts = list(bad["expectations"].get("forbidden_hidden_facts") or [])
        facts.append(synthetic)
        bad["expectations"]["forbidden_hidden_facts"] = facts
        bad["provider_visible"]["leaked_non_sentinel"] = synthetic
        errors = self.evaluator.validate_fixture(bad)
        joined = _json_blob(errors).lower()
        self.assertTrue(errors)
        self.assertTrue(
            "hidden" in joined
            or "forbidden" in joined
            or "leak" in joined
            or synthetic.lower() in joined,
            "must reject forbidden_hidden_facts leak in provider_visible: %s" % joined,
        )

    def test_fixture_oracle_is_labeled_non_authoritative(self):
        """CLI default must not present fixture_oracle output as provider-quality pass."""
        # Callback name and report metadata.
        self.assertTrue(
            hasattr(self.evaluator, "DEFAULT_THRESHOLDS"),
        )
        fixtures = self.fixtures
        # Pure API still accepts any callable; CLI default naming is fixture_oracle.
        with tempfile.TemporaryDirectory() as tmp:
            out_path = Path(tmp) / "oracle_report.json"
            rc = self.evaluator.main(
                [
                    "--corpus",
                    str(FIXTURES_DIR),
                    "--json",
                    "--out",
                    str(out_path),
                    "--callback",
                    "fixture_oracle",
                ]
            )
            # fixture_oracle may return 0 when structural gates pass, but must label mode.
            self.assertTrue(out_path.is_file())
            report = json.loads(out_path.read_text(encoding="utf-8"))
            self.assertEqual("fixture_oracle", report.get("evaluation_mode"))
            self.assertIs(
                report.get("quality_gate_authoritative"),
                False,
                "fixture_oracle reports must set quality_gate_authoritative=false",
            )
            # Always_invalid remains nonzero (locked).
            out2 = Path(tmp) / "invalid_report.json"
            rc2 = self.evaluator.main(
                [
                    "--corpus",
                    str(FIXTURES_DIR),
                    "--json",
                    "--out",
                    str(out2),
                    "--callback",
                    "always_invalid",
                ]
            )
            self.assertNotEqual(0, rc2)
            inv = json.loads(out2.read_text(encoding="utf-8"))
            # always_invalid is still a non-provider probe but must not claim authoritative green.
            if "quality_gate_authoritative" in inv:
                self.assertIs(inv.get("quality_gate_authoritative"), False)


if __name__ == "__main__":
    unittest.main()


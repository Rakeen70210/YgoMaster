#!/usr/bin/env python3
"""Analyze canonical NS3 corpora and enforce the offline feasibility gate."""
from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Sequence, Tuple


def _load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ImportError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


ADVISOR = _load_module(
    "analyze_native_cpu_advisor_module", Path(__file__).with_name("native_cpu_advisor.py")
)


EXPECTATION_FIELDS = (
    "status",
    "reason",
    "scoreable",
    "recommended_index",
    "native_visible_damage",
    "recommended_visible_damage",
)
MANIFEST_SCHEMA = "ygomaster.native_cpu_advisor_fixture_manifest.v1"


def _load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def _load_corpus(path: Path) -> Dict[str, Any]:
    corpus = _load_json(path)
    if not isinstance(corpus, dict) or corpus.get("schema") != ADVISOR.CORPUS_SCHEMA:
        raise ValueError(f"{path} is not a canonical corpus JSON file")
    return corpus


def _load_fixture_manifest(path: Path) -> Tuple[List[Dict[str, Any]], List[Dict[str, Any]]]:
    manifest = _load_json(path)
    if not isinstance(manifest, dict):
        raise ValueError("fixture manifest must be an object")
    if manifest.get("schema") != MANIFEST_SCHEMA:
        raise ValueError("fixture manifest schema mismatch")
    if manifest.get("corpus_schema") != ADVISOR.CORPUS_SCHEMA:
        raise ValueError("fixture manifest corpus schema mismatch")
    entries = manifest.get("synthetic_fixtures")
    if not isinstance(entries, list) or len(entries) != ADVISOR.SYNTHETIC_REQUIRED_FIXTURE_COUNT:
        raise ValueError("fixture manifest must list exactly eleven fixtures")
    base = path.parent
    corpora: List[Dict[str, Any]] = []
    expectations: List[Dict[str, Any]] = []
    seen_ids = set()
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("fixture manifest entry is not an object")
        fixture_id = entry.get("fixture_id")
        file_name = entry.get("file")
        if not isinstance(fixture_id, str) or fixture_id in seen_ids:
            raise ValueError("fixture manifest has duplicate or invalid fixture ID")
        if not isinstance(file_name, str) or Path(file_name).is_absolute() or ".." in Path(file_name).parts:
            raise ValueError("fixture manifest file must be relative")
        wrapper = _load_json(base / file_name)
        if not isinstance(wrapper, dict):
            raise ValueError(f"fixture {fixture_id} wrapper is not an object")
        if wrapper.get("schema") != ADVISOR.FIXTURE_SCHEMA:
            raise ValueError(f"fixture {fixture_id} wrapper schema mismatch")
        if wrapper.get("fixture_id") != fixture_id:
            raise ValueError(f"fixture {fixture_id} wrapper ID mismatch")
        expected = wrapper.get("expectations")
        if not isinstance(expected, dict) or set(expected) != set(EXPECTATION_FIELDS):
            raise ValueError(f"fixture {fixture_id} expectations mismatch")
        corpus = wrapper.get("corpus")
        if not isinstance(corpus, dict) or corpus.get("corpus_id") != fixture_id:
            raise ValueError(f"fixture {fixture_id} corpus ID mismatch")
        if len(corpus.get("decisions", [])) != 1:
            raise ValueError(f"fixture {fixture_id} must contain one decision")
        errors = ADVISOR.validate_corpus(corpus)
        if errors:
            raise ValueError(f"fixture {fixture_id} is invalid: {', '.join(errors)}")
        seen_ids.add(fixture_id)
        corpora.append(corpus)
        expectations.append({"fixture_id": fixture_id, **expected})
    return corpora, expectations


def _decision_rows(corpora: Sequence[Dict[str, Any]]) -> List[Dict[str, Any]]:
    rows: List[Dict[str, Any]] = []
    for corpus in corpora:
        provenance_type = corpus["provenance_type"]
        for decision in corpus["decisions"]:
            rows.append(ADVISOR.advise_decision(decision, provenance_type))
    return rows


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("corpora", nargs="+", help="canonical corpus JSON files")
    parser.add_argument("--fixture-manifest", help="manifest for synthetic wrappers")
    parser.add_argument("--decisions-jsonl", help="optional per-decision JSONL output")
    parser.add_argument("--report", help="optional aggregate report JSON output")
    parser.add_argument("--require-ns3-gate", action="store_true")
    args = parser.parse_args(argv)
    try:
        corpora = [_load_corpus(Path(path)) for path in args.corpora]
        fixture_expectations: List[Dict[str, Any]] = []
        if args.fixture_manifest:
            fixture_corpora, fixture_expectations = _load_fixture_manifest(
                Path(args.fixture_manifest)
            )
            corpora.extend(fixture_corpora)
        report = ADVISOR.analyze_corpora(corpora, fixture_expectations)
        if report["validation_error_count"] != 0:
            raise ValueError("corpus validation failed")
        payload = json.dumps(report, indent=2, sort_keys=True)
        print(payload)
        if args.report:
            Path(args.report).write_text(payload + "\n", encoding="utf-8")
        if args.decisions_jsonl:
            rows = _decision_rows(corpora)
            Path(args.decisions_jsonl).write_text(
                "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows),
                encoding="utf-8",
            )
    except (OSError, TypeError, ValueError, KeyError, json.JSONDecodeError) as exc:
        print(str(exc), file=sys.stderr)
        return 2
    if args.require_ns3_gate and not report["ns3_advisor_gate_passed"]:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

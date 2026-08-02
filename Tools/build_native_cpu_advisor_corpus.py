#!/usr/bin/env python3
"""Build the honest, canonical production corpus from a frozen NS2 trace."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path


def _load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ImportError(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TRACE = _load_module(
    "build_native_cpu_advisor_trace", Path(__file__).with_name("analyze_native_cpu_trace.py")
)
ADVISOR = _load_module(
    "build_native_cpu_advisor_schema", Path(__file__).with_name("native_cpu_advisor.py")
)


def _parse_all_lines(path: Path) -> None:
    with path.open("r", encoding="utf-8") as reader:
        for line_number, line in enumerate(reader, 1):
            if not line.strip():
                continue
            try:
                json.loads(line)
            except json.JSONDecodeError as exc:
                raise ValueError(f"{path}:{line_number} invalid JSON: {exc}") from exc


def _normalized_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()


def _session_id(trace: dict) -> str:
    event = trace["event"]
    values = (
        trace.get("observed_ts"),
        trace.get("begin_ts"),
        str(event["duel_dll_sha256"]).lower(),
        event["duel_generation"],
        event["my_id"],
        event["owned_seat"],
    )
    payload = "|".join(str(value) for value in values)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:16]


def _canonical_candidate(candidate: dict) -> dict:
    raw = candidate["raw"]
    return {
        "index": candidate["index"],
        "raw": raw,
        "word0": candidate.get("word0", raw & 0xFFFF),
        "word1": candidate.get("word1", (raw >> 16) & 0xFFFF),
        "auxiliary_raw": candidate.get("auxiliary_raw"),
        "is_native_selected": candidate.get("is_selected") is True,
        "semantics": {
            "status": "unproven",
            "action_family": None,
            "attacker_instance_id": None,
            "target_instance_id": None,
            "proof_refs": [],
        },
    }


def build_corpus(input_path: Path) -> dict:
    """Build one production corpus, raising ValueError on any admission failure."""
    _parse_all_lines(input_path)
    report = TRACE.analyze_paths([str(input_path)])
    if int(report.get("supported_hook_ready_count") or 0) < 1:
        raise ValueError("no supported native trace hook-ready row")
    if int(report.get("correlated_valid_session_count") or 0) < 1:
        raise ValueError("no correlated native trace session")
    traces = TRACE.collect_correlated_candidate_traces([str(input_path)])
    if not traces:
        raise ValueError("no correlated candidate traces")
    decisions = []
    for trace in traces:
        event = trace["event"]
        session_id = _session_id(trace)
        ordinal = trace.get("trace_ordinal")
        if not isinstance(ordinal, int) or ordinal < 0:
            raise ValueError("correlated trace has no valid session ordinal")
        decisions.append(
            {
                "decision_id": f"{session_id}:{ordinal:04d}",
                "duel_dll_sha256": str(event["duel_dll_sha256"]).lower(),
                "duel_generation": event["duel_generation"],
                "my_id": event["my_id"],
                "owned_seat": event["owned_seat"],
                "turn": event["turn"],
                "turn_player": event["turn_player"],
                "phase": event["phase"],
                "candidate_count": event["candidate_count"],
                "native_selected_index": event["selected_index"],
                "native_selected_raw": event["native_chosen_raw"],
                "native_scores_available": False,
                "candidates": [_canonical_candidate(candidate) for candidate in event["candidates"]],
                "public_state": None,
            }
        )
    corpus = {
        "schema": ADVISOR.CORPUS_SCHEMA,
        "corpus_id": "ns2_20260801_production_v1",
        "provenance_type": "production_trace",
        "source": {
            "duel_dll_sha256": TRACE.SUPPORTED_DUEL_DLL_SHA256,
            "normalized_input_sha256": _normalized_sha256(input_path),
            "correlated_session_count": report["correlated_valid_session_count"],
            "correlated_trace_count": len(decisions),
        },
        "decisions": decisions,
    }
    errors = ADVISOR.validate_corpus(corpus)
    if errors:
        raise ValueError("built production corpus is invalid: " + ", ".join(errors))
    return corpus


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", help="frozen NS2 audit JSONL")
    parser.add_argument("--output", required=True, help="canonical corpus JSON output")
    args = parser.parse_args(argv)
    try:
        corpus = build_corpus(Path(args.input))
        output = Path(args.output)
        output.write_text(
            json.dumps(corpus, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
    except (OSError, TypeError, ValueError, KeyError) as exc:
        print(str(exc), file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

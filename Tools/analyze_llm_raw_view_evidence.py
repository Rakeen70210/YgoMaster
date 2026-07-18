#!/usr/bin/env python3
"""Deterministic live-evidence analyzer for YGOMASTER-LLM-004 Slice 3.

Reads LlmDecisionLog.jsonl (or any JSONL of llm_raw_duel_view / llm_public_duel_event
lines) and prints a stable report mapping raw DuelView evidence for post-deploy UI
validation. Does not invent view-parameter semantics.

Usage:
  python3 Tools/analyze_llm_raw_view_evidence.py path/to/LlmDecisionLog.jsonl
  python3 Tools/analyze_llm_raw_view_evidence.py --self-test
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter, OrderedDict
from typing import Any, Dict, Iterable, List, Optional


def load_jsonl(path: str) -> List[Dict[str, Any]]:
    rows: List[Dict[str, Any]] = []
    with open(path, "r", encoding="utf-8") as fh:
        for line_no, line in enumerate(fh, 1):
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError as exc:
                raise SystemExit(f"invalid jsonl at line {line_no}: {exc}") from exc
            if isinstance(obj, dict):
                rows.append(obj)
    return rows


def collect_raw_views(rows: Iterable[Dict[str, Any]]) -> List[Dict[str, Any]]:
    out: List[Dict[str, Any]] = []
    for row in rows:
        if row.get("kind") == "llm_raw_duel_view":
            out.append(row)
    return out


def collect_outcomes(rows: Iterable[Dict[str, Any]]) -> List[Dict[str, Any]]:
    out: List[Dict[str, Any]] = []
    for row in rows:
        if row.get("kind") == "llm_public_duel_event" and row.get("evidence") == "public_state_delta":
            out.append(row)
        # Also accept history_kind from audit envelope after Slice 2 serializer.
        if row.get("kind") == "llm_public_duel_event" and row.get("history_kind") in {
            "turn_changed",
            "phase_changed",
            "life_points_changed",
            "card_moved",
            "hand_shuffled",
            "deck_shuffled",
        }:
            if row not in out:
                out.append(row)
    return out


def collect_face_probes(rows: Iterable[Dict[str, Any]]) -> List[Dict[str, Any]]:
    out: List[Dict[str, Any]] = []
    for row in rows:
        if row.get("kind") == "llm_face_probe":
            out.append(row)
    return out


def derive_dll_face_mapping(probes: List[Dict[str, Any]]) -> Dict[str, Any]:
    """Record live-validated DLL field face mapping; banished stays unvalidated.

    Evidence (2026-07-15 P2 duel face probes): field raw_face 0=facedown/non-public,
    raw_face 1=face-up/public. Fixture domain 8/4 is a different face system.
    """
    field_faces = Counter()
    for p in probes:
        pos = p.get("position")
        if isinstance(pos, int) and 0 <= pos <= 12:
            face = p.get("raw_face")
            if face is not None:
                field_faces[face] += 1
    return OrderedDict(
        [
            (
                "field",
                OrderedDict(
                    [
                        ("validated", True),
                        ("validated_from_live_evidence", True),
                        ("dll_raw_face_face_up_public", 1),
                        ("dll_raw_face_facedown_or_non_public", 0),
                        ("fixture_face_domain_not_applicable", OrderedDict(
                            [("face_up", 8), ("facedown", 4)]
                        )),
                        ("positions", "0..12"),
                        (
                            "evidence_note",
                            "Live P2 duel: set at p0 pos2 raw_face=0 until flip/battle; "
                            "normal summon p1 pos0 raw_face=1; flip to raw_face=1 at BattleRun.",
                        ),
                        ("observed_field_raw_face_counts", OrderedDict(
                            (str(k), field_faces[k]) for k in sorted(field_faces)
                        )),
                    ]
                ),
            ),
            (
                "banished",
                OrderedDict(
                    [
                        ("validated", False),
                        ("reason", "No face-up/facedown banished fixture in validating duel; identity remains fail-closed."),
                    ]
                ),
            ),
        ]
    )


def build_report(rows: List[Dict[str, Any]]) -> Dict[str, Any]:
    raw = collect_raw_views(rows)
    outcomes = collect_outcomes(rows)
    probes = collect_face_probes(rows)
    view_counts = Counter(r.get("view_type", "?") for r in raw)
    raw_ids = [r.get("raw_evidence_id") for r in raw if r.get("raw_evidence_id") is not None]
    dupes = len(raw_ids) - len(set(raw_ids))
    probe_ids = [p.get("probe_id") for p in probes if p.get("probe_id") is not None]
    probe_dupes = len(probe_ids) - len(set(probe_ids))
    families = OrderedDict(sorted(view_counts.items(), key=lambda kv: kv[0]))
    face_mapping = derive_dll_face_mapping(probes)
    return OrderedDict(
        [
            ("kind", "llm_live_view_evidence_report"),
            ("raw_view_count", len(raw)),
            ("normalized_outcome_count", len(outcomes)),
            ("face_probe_count", len(probes)),
            ("duplicate_raw_evidence_ids", dupes),
            ("duplicate_face_probe_ids", probe_dupes),
            ("dll_face_mapping", face_mapping),
            ("raw_view_type_counts", families),
            (
                "note",
                "Map raw view_type/params and face probes "
                "(player/position/index/raw_face/run_effect_seq) to visible UI. "
                "Field DLL face mapping is live-validated (0=facedown, 1=face-up); "
                "banished mapping is unvalidated. Do not promote unproven families.",
            ),
            (
                "raw_views",
                [
                    OrderedDict(
                        [
                            ("raw_evidence_id", r.get("raw_evidence_id")),
                            ("view_type", r.get("view_type")),
                            ("view_type_id", r.get("view_type_id")),
                            ("param1", r.get("param1")),
                            ("param2", r.get("param2")),
                            ("param3", r.get("param3")),
                            ("run_effect_seq", r.get("run_effect_seq")),
                            ("source_signature", r.get("source_signature")),
                        ]
                    )
                    for r in raw
                ],
            ),
            (
                "face_probes",
                [
                    OrderedDict(
                        [
                            ("probe_id", p.get("probe_id")),
                            ("run_effect_seq", p.get("run_effect_seq")),
                            ("player", p.get("player")),
                            ("position", p.get("position")),
                            ("index", p.get("index")),
                            ("raw_face", p.get("raw_face")),
                        ]
                    )
                    for p in probes
                ],
            ),
            (
                "normalized_outcomes",
                [
                    OrderedDict(
                        [
                            ("event_id", o.get("event_id")),
                            ("kind", o.get("history_kind") or o.get("kind")),
                            ("evidence", o.get("evidence")),
                            ("actor_player", o.get("actor_player")),
                            ("target_player", o.get("target_player")),
                            ("card_player", o.get("card_player")),
                            ("source_signature", o.get("source_signature")),
                        ]
                    )
                    for o in outcomes
                ],
            ),
        ]
    )


def self_test() -> None:
    sample = [
        {
            "kind": "llm_raw_duel_view",
            "raw_evidence_id": 1,
            "view_type": "BattleAttack",
            "view_type_id": 12,
            "param1": 0,
            "param2": 1,
            "param3": 0,
            "run_effect_seq": 9,
            "source_signature": "raw_view:id:1:seq:9:12:0:1:0",
            "audit_only": True,
            "broker_history": False,
        },
        {
            "kind": "llm_face_probe",
            "probe_id": 4,
            "run_effect_seq": 9,
            "player": 0,
            "position": 2,
            "index": 0,
            "raw_face": 1,
            "audit_only": True,
            "broker_history": False,
        },
        {
            "kind": "llm_public_duel_event",
            "history_kind": "life_points_changed",
            "evidence": "public_state_delta",
            "event_id": 3,
            "actor_player": -1,
            "target_player": 1,
            "source_signature": "outcome:lp:seq:9:player:1:from:8000:to:7200:delta:-800",
        },
    ]
    report = build_report(sample)
    assert report["raw_view_count"] == 1
    assert report["normalized_outcome_count"] == 1
    assert report["face_probe_count"] == 1
    assert report["duplicate_face_probe_ids"] == 0
    assert report["face_probes"][0]["raw_face"] == 1
    assert report["dll_face_mapping"]["field"]["dll_raw_face_face_up_public"] == 1
    assert report["dll_face_mapping"]["field"]["dll_raw_face_facedown_or_non_public"] == 0
    assert report["dll_face_mapping"]["banished"]["validated"] is False
    assert report["raw_view_type_counts"]["BattleAttack"] == 1
    text1 = json.dumps(report, sort_keys=False, separators=(",", ":"))
    text2 = json.dumps(build_report(sample), sort_keys=False, separators=(",", ":"))
    assert text1 == text2, "report must be deterministic"
    print("analyze_llm_raw_view_evidence: self-test ok")


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log", nargs="?", help="Path to LlmDecisionLog.jsonl")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args(argv)
    if args.self_test:
        self_test()
        return 0
    if not args.log:
        parser.error("log path required unless --self-test")
    rows = load_jsonl(args.log)
    report = build_report(rows)
    json.dump(report, sys.stdout, indent=2, sort_keys=False)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

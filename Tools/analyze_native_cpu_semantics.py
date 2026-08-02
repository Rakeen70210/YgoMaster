#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from native_cpu_semantic_evidence import analyze_readiness


def _load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def main(argv=None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--binary", required=True)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--vectors")
    parser.add_argument("--csharp-source")
    parser.add_argument("--require-static-evidence-gate", action="store_true")
    parser.add_argument("--require-ns4-gate", action="store_true")
    args = parser.parse_args(argv)
    try:
        binary = Path(args.binary).read_bytes()
        manifest = _load_json(Path(args.manifest))
        vectors = _load_json(Path(args.vectors)) if args.vectors else None
        csharp_source = Path(args.csharp_source).read_text(encoding="utf-8") if args.csharp_source else None
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(str(exc), file=sys.stderr)
        return 2
    report = analyze_readiness(manifest, binary, vectors, csharp_source)
    print(json.dumps(report, sort_keys=True))
    if args.require_static_evidence_gate and not report["static_evidence_gate_passed"]:
        return 1
    if args.require_ns4_gate and not report["ns4_semantic_gate_passed"]:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

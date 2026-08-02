#!/usr/bin/env python3
import importlib.util
import json
import unittest
from pathlib import Path


TOOLS_DIR = Path(__file__).parent
MODULE_PATH = TOOLS_DIR / "native_cpu_semantic_evidence.py"
MANIFEST_PATH = TOOLS_DIR / "fixtures" / "native_cpu_semantics" / "manifest.json"


def _load_module():
    spec = importlib.util.spec_from_file_location("native_cpu_semantic_evidence_tests", MODULE_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class NativeCpuSemanticEvidenceTests(unittest.TestCase):
    def _manifest_and_binary(self):
        manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
        binary = Path("../masterduel_Data/Plugins/x86_64/duel.dll").read_bytes()
        return manifest, binary

    def test_pinned_binary_and_exact_manifest_shape_are_required(self):
        evidence = _load_module()
        manifest, binary = self._manifest_and_binary()
        self.assertEqual([], evidence.validate_manifest(manifest, binary))
        self.assertEqual(evidence.EVIDENCE_SCHEMA, manifest["schema"])

        extra_key = dict(manifest)
        extra_key["extra"] = True
        self.assertIn("manifest_top_level_keys_invalid", evidence.validate_manifest(extra_key, binary))

        missing_key = dict(manifest)
        del missing_key["authority"]
        self.assertIn("manifest_top_level_keys_invalid", evidence.validate_manifest(missing_key, binary))

    def test_proven_requirement_needs_independent_group_a_and_b_proofs(self):
        evidence = _load_module()
        manifest, binary = self._manifest_and_binary()
        proven = manifest["requirements"][0]
        proven["status"] = "proven"
        proven["supported_values"] = ["attack_direct"]
        proven["proof_refs"] = ["producer"]
        proven["failure_reason"] = None
        self.assertIn("requirement_action_family_proof_count_invalid", evidence.validate_manifest(manifest, binary))

        proof = {
            "id": "producer",
            "requirement_ids": ["action_family"],
            "source_class": "candidate_materializer",
            "function_start_va": 0x18063A380,
            "window_start_va": 0x18063A380,
            "window_end_va_exclusive": 0x18063A384,
            "window_sha256": evidence.hash_pe_window(binary, 0x18063A380, 0x18063A384),
            "instruction_assertions": ["0x18063a380: mov qword ptr [rsp + 0x8], rbx"],
            "conclusion": "The materializer writes a native field to action work.",
            "independent_of": ["consumer"],
        }
        consumer = dict(proof)
        consumer.update({
            "id": "consumer",
            "source_class": "action_consumer",
            "function_start_va": 0x1805CC460,
            "window_start_va": 0x1805CC460,
            "window_end_va_exclusive": 0x1805CC464,
            "window_sha256": evidence.hash_pe_window(binary, 0x1805CC460, 0x1805CC464),
            "instruction_assertions": ["0x1805cc460: mov qword ptr [rsp + 0x8], rbx"],
            "conclusion": "A different native function reads action work.",
            "independent_of": ["producer"],
        })
        manifest["proofs"] = [proof, consumer]
        proven["proof_refs"] = ["consumer", "producer"]
        errors = evidence.validate_manifest(manifest, binary)
        self.assertNotIn("requirement_action_family_proof_count_invalid", errors)
        self.assertNotIn("proof_independence_invalid", errors)

        same_source = dict(consumer)
        same_source["id"] = "same-source"
        same_source["function_start_va"] = proof["function_start_va"]
        same_source["independent_of"] = ["producer"]
        manifest["proofs"] = [proof, same_source]
        proven["proof_refs"] = ["producer", "same-source"]
        self.assertIn("proof_independence_invalid", evidence.validate_manifest(manifest, binary))

    def test_raw_pe_window_hash_mismatch_is_rejected(self):
        evidence = _load_module()
        _, binary = self._manifest_and_binary()
        with self.assertRaises(ValueError):
            evidence.hash_pe_window(binary, evidence.SUPPORTED_IMAGE_BASE, evidence.SUPPORTED_IMAGE_BASE + 1)

        manifest, _ = self._manifest_and_binary()
        mutated = bytearray(binary)
        mutated[0x1000] ^= 0x01
        errors = evidence.validate_manifest(manifest, bytes(mutated))
        self.assertIn("binary_sha256_mismatch", errors)

        self.assertIn("binary_pe_invalid", evidence.validate_manifest(manifest, binary[:128]))

    def test_unproven_requirement_forces_infeasible_and_false_authority(self):
        evidence = _load_module()
        manifest, binary = self._manifest_and_binary()
        report = evidence.analyze_readiness(manifest, binary, None, None)
        self.assertEqual("NS4_INFEASIBLE", report["outcome"])
        self.assertFalse(report["trace_extension_ready"])
        self.assertFalse(report["rerank_authorized"])

        manifest["authority"]["deployment_authorized"] = True
        report = evidence.analyze_readiness(manifest, binary, None, None)
        self.assertEqual("NS4_INVALID", report["outcome"])
        self.assertFalse(report["deployment_authorized"])

    def test_static_or_synthetic_input_can_never_be_production_authority(self):
        evidence = _load_module()
        self.assertFalse(evidence.analyze_readiness({}, b"", None, None)["production_evidence"])

        manifest, binary = self._manifest_and_binary()
        self.assertIn(
            "vectors_not_allowed_when_manifest_unproven",
            evidence.validate_vectors(manifest, []),
        )
        self.assertIn(
            "csharp_source_not_allowed_in_ns4_infeasible",
            evidence.verify_csharp_constants(manifest, "class Unexpected {}"),
        )

    def test_malformed_json_shapes_are_rejected_without_exceptions(self):
        evidence = _load_module()
        manifest, binary = self._manifest_and_binary()
        malformed = json.loads(json.dumps(manifest))
        malformed["requirements"][0]["supported_values"] = [{}]
        malformed["requirements"][0]["proof_refs"] = [[]]
        malformed["proofs"] = [{"id": []}]
        errors = evidence.validate_manifest(malformed, binary)
        self.assertIn("requirement_action_family_supported_values_invalid", errors)
        self.assertIn("requirement_action_family_proof_refs_invalid", errors)
        self.assertIn("proof_keys_invalid", errors)
        report = evidence.analyze_readiness(malformed, binary, None, None)
        self.assertEqual("NS4_INVALID", report["outcome"])


if __name__ == "__main__":
    unittest.main()

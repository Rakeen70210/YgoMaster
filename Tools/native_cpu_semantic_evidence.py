#!/usr/bin/env python3
"""Pure, fail-closed validation for the NS4 native semantic boundary.

The current NS4 manifest intentionally contains no proven mappings.  This
module still validates the complete evidence-manifest shape so that an
accidental proof, binary mismatch, or authority claim cannot silently turn
the infeasible result into a usable decoder.
"""

from __future__ import annotations

import hashlib
import json
import re
import struct
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple


EVIDENCE_SCHEMA = "ygomaster.native_cpu_semantic_evidence.v1"
DECODER_SCHEMA = "ygomaster.native_cpu_candidate_decoder.v1"
VECTOR_SCHEMA = "ygomaster.native_cpu_candidate_decoder_vectors.v1"
REPORT_SCHEMA = "ygomaster.native_cpu_semantic_readiness_report.v1"
SUPPORTED_DUEL_DLL_SHA256 = "97bd4d136e39b0872e4a8a9171632f1f0bd9bb04d69af08e836c56e684d43c44"
SUPPORTED_DUEL_DLL_SIZE = 19443200
SUPPORTED_IMAGE_BASE = 0x180000000

REQUIRED_IDS = [
    "action_family",
    "attacker_instance_id",
    "battle_position",
    "boundary_timing",
    "public_state_apis",
    "target_or_direct",
]

_TOP_LEVEL_KEYS = [
    "schema",
    "manifest_id",
    "binary",
    "requirements",
    "proofs",
    "decoder_spec",
    "authority",
]
_BINARY_KEYS = ["path_hint", "sha256", "size", "image_base"]
_REQUIREMENT_KEYS = ["id", "status", "supported_values", "proof_refs", "failure_reason"]
_PROOF_KEYS = [
    "id",
    "requirement_ids",
    "source_class",
    "function_start_va",
    "window_start_va",
    "window_end_va_exclusive",
    "window_sha256",
    "instruction_assertions",
    "conclusion",
    "independent_of",
]
_AUTHORITY = {
    "production_evidence": False,
    "deployment_authorized": False,
    "trace_enablement_authorized": False,
    "duel_required_next": False,
    "rerank_authorized": False,
}
_GROUP_A = {"candidate_producer", "candidate_materializer"}
_GROUP_B = {"action_consumer", "named_duel_api", "production_boundary"}
_SOURCE_CLASSES = _GROUP_A | _GROUP_B
_UNCERTAINTY_WORDS = ("likely", "probably", "appears", "seems", "assume", "guess")
_HEX64 = re.compile(r"^[0-9a-f]{64}$")
_ASSERTION_ADDRESS = re.compile(r"^(?:0x)?([0-9a-fA-F]+|[0-9]+):")


def canonical_json_sha256(value: object) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _exact_keys(value: object, expected: Sequence[str], error: str) -> List[str]:
    if not isinstance(value, dict):
        return [error]
    if list(value.keys()) != list(expected):
        return [error]
    return []


def _is_sorted_unique_strings(value: object) -> bool:
    return (
        isinstance(value, list)
        and all(isinstance(item, str) for item in value)
        and value == sorted(value)
        and len(value) == len(set(value))
    )


def _parse_pe_sections(binary: bytes) -> List[Tuple[int, int, int, int]]:
    if not isinstance(binary, (bytes, bytearray)) or isinstance(binary, bool):
        raise ValueError("binary must be bytes")
    if len(binary) < 0x40 or binary[:2] != b"MZ":
        raise ValueError("not a PE image")
    pe_offset = struct.unpack_from("<I", binary, 0x3C)[0]
    if pe_offset + 4 + 20 > len(binary) or binary[pe_offset : pe_offset + 4] != b"PE\0\0":
        raise ValueError("invalid PE header")
    coff = pe_offset + 4
    number_of_sections = struct.unpack_from("<H", binary, coff + 2)[0]
    size_of_optional_header = struct.unpack_from("<H", binary, coff + 16)[0]
    optional = coff + 20
    section_table = optional + size_of_optional_header
    if section_table + number_of_sections * 40 > len(binary):
        raise ValueError("truncated PE section table")

    sections: List[Tuple[int, int, int, int]] = []
    for index in range(number_of_sections):
        base = section_table + index * 40
        virtual_size = struct.unpack_from("<I", binary, base + 8)[0]
        virtual_addr = struct.unpack_from("<I", binary, base + 12)[0]
        raw_size = struct.unpack_from("<I", binary, base + 16)[0]
        raw_ptr = struct.unpack_from("<I", binary, base + 20)[0]
        if raw_size == 0 or raw_ptr + raw_size > len(binary):
            raise ValueError("out-of-file PE section")
        if virtual_size == 0:
            raise ValueError("empty PE section")
        sections.append((virtual_addr, virtual_size, raw_ptr, raw_size))

    for index, (start, size, _, _) in enumerate(sections):
        end = start + size
        for other_start, other_size, _, _ in sections[index + 1 :]:
            if start < other_start + other_size and other_start < end:
                raise ValueError("overlapping PE sections")
    return sections


def pe_va_to_file_offset(binary: bytes, virtual_address: int) -> int:
    if not _is_int(virtual_address):
        raise TypeError("virtual_address must be int")
    sections = _parse_pe_sections(binary)
    if virtual_address < SUPPORTED_IMAGE_BASE:
        raise ValueError("unmapped virtual address")
    rva = virtual_address - SUPPORTED_IMAGE_BASE
    for virtual_addr, virtual_size, raw_ptr, raw_size in sections:
        if virtual_addr <= rva < virtual_addr + virtual_size:
            relative = rva - virtual_addr
            if relative >= raw_size:
                raise ValueError("virtual-only PE range")
            offset = raw_ptr + relative
            if offset >= len(binary):
                raise ValueError("out-of-file PE range")
            return offset
    raise ValueError("unmapped virtual address")


def hash_pe_window(binary: bytes, start_va: int, end_va_exclusive: int) -> str:
    if not _is_int(start_va) or not _is_int(end_va_exclusive):
        raise TypeError("window addresses must be ints")
    if end_va_exclusive <= start_va:
        raise ValueError("invalid window")
    start = pe_va_to_file_offset(binary, start_va)
    end = pe_va_to_file_offset(binary, end_va_exclusive - 1) + 1
    if end - start != end_va_exclusive - start_va:
        raise ValueError("non-contiguous PE window")
    return hashlib.sha256(binary[start:end]).hexdigest()


def _validate_proof_rows(manifest: dict, binary: bytes) -> Tuple[List[str], int, int]:
    errors: set[str] = set()
    independence_errors = 0
    binary_window_errors = 0
    proofs = manifest.get("proofs")
    if not isinstance(proofs, list):
        return ["proofs_shape_invalid"], 0, 0

    proof_ids = [proof.get("id") for proof in proofs if isinstance(proof, dict)]
    if not _is_sorted_unique_strings(proof_ids):
        errors.add("proof_ids_invalid")
    proof_map: Dict[str, dict] = {}
    for proof in proofs:
        if not isinstance(proof, dict):
            errors.add("proof_shape_invalid")
            continue
        errors.update(_exact_keys(proof, _PROOF_KEYS, "proof_keys_invalid"))
        proof_id = proof.get("id")
        if not isinstance(proof_id, str) or not proof_id or proof_id in proof_map:
            errors.add("proof_id_invalid")
        else:
            proof_map[proof_id] = proof
        requirement_ids = proof.get("requirement_ids")
        if (
            not isinstance(requirement_ids, list)
            or not requirement_ids
            or not _is_sorted_unique_strings(requirement_ids)
            or not all(isinstance(value, str) and value in REQUIRED_IDS for value in requirement_ids)
        ):
            errors.add("proof_requirement_ids_invalid")
        source_class = proof.get("source_class")
        if source_class not in _SOURCE_CLASSES:
            errors.add("proof_source_class_invalid")
        for field in ("function_start_va", "window_start_va", "window_end_va_exclusive"):
            if not _is_int(proof.get(field)):
                errors.add("proof_address_invalid")
        start = proof.get("window_start_va")
        end = proof.get("window_end_va_exclusive")
        if _is_int(start) and _is_int(end):
            if end <= start or end - start < 4 or end - start > 256:
                errors.add("proof_window_range_invalid")
            else:
                try:
                    actual_hash = hash_pe_window(binary, start, end)
                except (TypeError, ValueError, struct.error):
                    errors.add("binary_window_range_invalid")
                    binary_window_errors += 1
                else:
                    if proof.get("window_sha256") != actual_hash:
                        errors.add("binary_window_hash_mismatch")
                        binary_window_errors += 1
        if not isinstance(proof.get("window_sha256"), str) or not _HEX64.fullmatch(
            proof.get("window_sha256", "")
        ):
            errors.add("proof_window_sha256_invalid")
        assertions = proof.get("instruction_assertions")
        if not _is_sorted_unique_strings(assertions) or not assertions:
            errors.add("proof_instruction_assertions_invalid")
        else:
            for assertion in assertions:
                match = _ASSERTION_ADDRESS.match(assertion) if isinstance(assertion, str) else None
                if not match or not _is_int(start) or not _is_int(end):
                    errors.add("proof_instruction_assertion_invalid")
                    continue
                address = int(match.group(1), 16)
                if not start <= address < end:
                    errors.add("proof_instruction_assertion_out_of_window")
        conclusion = proof.get("conclusion")
        if not isinstance(conclusion, str) or not conclusion.strip():
            errors.add("proof_conclusion_invalid")
        elif any(word in conclusion.lower() for word in _UNCERTAINTY_WORDS):
            errors.add("proof_conclusion_uncertain")
        independent_of = proof.get("independent_of")
        if (
            not isinstance(independent_of, list)
            or not _is_sorted_unique_strings(independent_of)
            or not all(isinstance(value, str) and value != proof_id for value in independent_of)
        ):
            errors.add("proof_independence_invalid")
            independence_errors += 1

    for proof in proofs:
        if not isinstance(proof, dict):
            continue
        proof_id = proof.get("id")
        for other in proof.get("independent_of", []) if isinstance(proof.get("independent_of"), list) else []:
            if other not in proof_map:
                errors.add("proof_independence_invalid")
                independence_errors += 1

    requirements = manifest.get("requirements")
    if isinstance(requirements, list):
        for requirement in requirements:
            if not isinstance(requirement, dict) or requirement.get("status") != "proven":
                continue
            requirement_id = requirement.get("id")
            refs = requirement.get("proof_refs")
            if not _is_sorted_unique_strings(refs) or len(refs) < 2:
                errors.add(f"requirement_{requirement_id}_proof_count_invalid")
                continue
            selected = [proof_map.get(ref) for ref in refs]
            if any(proof is None for proof in selected):
                errors.add(f"requirement_{requirement_id}_proof_refs_invalid")
                continue
            source_classes = [proof["source_class"] for proof in selected]
            if not any(source in _GROUP_A for source in source_classes) or not any(
                source in _GROUP_B for source in source_classes
            ):
                errors.add("proof_independence_invalid")
                errors.add(f"requirement_{requirement_id}_proof_group_invalid")
                independence_errors += 1
            function_starts = [proof["function_start_va"] for proof in selected]
            if len(function_starts) != len(set(function_starts)):
                errors.add("proof_independence_invalid")
                independence_errors += 1
            for proof in selected:
                if any(other != proof["id"] and other not in proof.get("independent_of", []) for other in refs):
                    errors.add("proof_independence_invalid")
                    independence_errors += 1
    return sorted(errors), independence_errors, binary_window_errors


def _validate_manifest_details(manifest: object, binary: bytes) -> Tuple[List[str], int, int]:
    errors: set[str] = set()
    if not isinstance(manifest, dict):
        return ["manifest_not_object"], 0, 0
    errors.update(_exact_keys(manifest, _TOP_LEVEL_KEYS, "manifest_top_level_keys_invalid"))
    if manifest.get("schema") != EVIDENCE_SCHEMA:
        errors.add("manifest_schema_invalid")
    if manifest.get("manifest_id") != "ns4-pinned-duel-dll-20260802":
        errors.add("manifest_id_invalid")

    binary_obj = manifest.get("binary")
    errors.update(_exact_keys(binary_obj, _BINARY_KEYS, "binary_keys_invalid"))
    binary_matches = False
    if isinstance(binary_obj, dict):
        if binary_obj.get("path_hint") != "../masterduel_Data/Plugins/x86_64/duel.dll":
            errors.add("binary_path_hint_invalid")
        if binary_obj.get("sha256") != SUPPORTED_DUEL_DLL_SHA256:
            errors.add("binary_sha256_invalid")
        if binary_obj.get("size") != SUPPORTED_DUEL_DLL_SIZE:
            errors.add("binary_size_invalid")
        if binary_obj.get("image_base") != SUPPORTED_IMAGE_BASE:
            errors.add("binary_image_base_invalid")
        binary_matches = (
            len(binary) == binary_obj.get("size") == SUPPORTED_DUEL_DLL_SIZE
            and hashlib.sha256(binary).hexdigest() == binary_obj.get("sha256") == SUPPORTED_DUEL_DLL_SHA256
        )
    if isinstance(binary, (bytes, bytearray)):
        if len(binary) != SUPPORTED_DUEL_DLL_SIZE:
            errors.add("binary_size_mismatch")
        if hashlib.sha256(binary).hexdigest() != SUPPORTED_DUEL_DLL_SHA256:
            errors.add("binary_sha256_mismatch")
        try:
            _parse_pe_sections(binary)
        except (TypeError, ValueError, struct.error):
            errors.add("binary_pe_invalid")
    else:
        errors.add("binary_input_invalid")

    requirements = manifest.get("requirements")
    if not isinstance(requirements, list) or len(requirements) != len(REQUIRED_IDS):
        errors.add("requirements_shape_invalid")
        requirements = []
    else:
        ids = [req.get("id") if isinstance(req, dict) else None for req in requirements]
        if ids != REQUIRED_IDS:
            errors.add("requirements_ids_invalid")
    for requirement in requirements:
        if not isinstance(requirement, dict):
            errors.add("requirement_shape_invalid")
            continue
        requirement_id = requirement.get("id")
        errors.update(_exact_keys(requirement, _REQUIREMENT_KEYS, "requirement_keys_invalid"))
        status = requirement.get("status")
        supported_values = requirement.get("supported_values")
        proof_refs = requirement.get("proof_refs")
        failure_reason = requirement.get("failure_reason")
        if status not in {"proven", "unproven"}:
            errors.add(f"requirement_{requirement_id}_status_invalid")
        if not _is_sorted_unique_strings(supported_values) or not all(supported_values):
            errors.add(f"requirement_{requirement_id}_supported_values_invalid")
        if not _is_sorted_unique_strings(proof_refs):
            errors.add(f"requirement_{requirement_id}_proof_refs_invalid")
        if status == "unproven":
            if supported_values != []:
                errors.add(f"requirement_{requirement_id}_unproven_values_invalid")
            if proof_refs != []:
                errors.add(f"requirement_{requirement_id}_unproven_proofs_invalid")
            if not isinstance(failure_reason, str) or not failure_reason.strip():
                errors.add(f"requirement_{requirement_id}_failure_reason_invalid")
            elif any(word in failure_reason.lower() for word in ("tbd", "todo", "unknown", "placeholder")):
                errors.add(f"requirement_{requirement_id}_failure_reason_placeholder")
        elif status == "proven" and failure_reason is not None:
            errors.add(f"requirement_{requirement_id}_failure_reason_invalid")
        if requirement_id == "public_state_apis" and status == "proven":
            if supported_values != ["attack", "defense", "face_up", "instance_id", "opponent_lp", "stats"]:
                errors.add("requirement_public_state_apis_domain_invalid")
        if requirement_id == "boundary_timing" and status == "proven":
            if supported_values != ["duel_work_initialized", "pre_materialization", "read_only"]:
                errors.add("requirement_boundary_timing_domain_invalid")

    authority = manifest.get("authority")
    if authority != _AUTHORITY:
        errors.add("authority_invalid")

    proof_errors, independence_errors, binary_window_errors = _validate_proof_rows(manifest, binary)
    errors.update(proof_errors)
    proof_map = {
        proof.get("id"): proof
        for proof in manifest.get("proofs", [])
        if isinstance(proof, dict) and isinstance(proof.get("id"), str)
    }
    for requirement in requirements:
        if not isinstance(requirement, dict) or requirement.get("status") != "proven":
            continue
        refs = requirement.get("proof_refs", [])
        if isinstance(refs, list) and any(ref not in proof_map for ref in refs):
            errors.add(f"requirement_{requirement.get('id')}_proof_refs_invalid")

    core_ids = {"action_family", "attacker_instance_id", "target_or_direct", "battle_position"}
    statuses = {
        requirement.get("id"): requirement.get("status")
        for requirement in requirements
        if isinstance(requirement, dict) and isinstance(requirement.get("id"), str)
    }
    if manifest.get("decoder_spec") is not None and not core_ids.issubset(
        {key for key, value in statuses.items() if value == "proven"}
    ):
        errors.add("decoder_spec_not_allowed_while_unproven")
    return sorted(errors), independence_errors, binary_window_errors


def validate_manifest(manifest: object, binary: bytes) -> list[str]:
    return _validate_manifest_details(manifest, binary)[0]


def _empty_decode(status: str, reason: str) -> dict:
    return {
        "status": status,
        "reason": reason,
        "action_family": None,
        "attacker_instance_id": None,
        "target_instance_id": None,
        "attacker_battle_position": None,
        "target_battle_position": None,
        "proof_refs": [],
    }


def decode_candidate(decoder_spec: dict, candidate: dict, owned_seat: int, native_state: dict) -> dict:
    """Return a safe result while no decoder has been admitted by NS4."""
    if decoder_spec is None:
        return _empty_decode("unproven", "decoder_unproven")
    if not isinstance(decoder_spec, dict) or not isinstance(candidate, dict) or not isinstance(native_state, dict):
        return _empty_decode("invalid", "candidate_shape_invalid")
    return _empty_decode("unproven", "decoder_not_implemented")


def validate_vectors(manifest: dict, vectors: object) -> list[str]:
    if not isinstance(manifest, dict) or manifest.get("decoder_spec") is None:
        return ["vectors_not_allowed_when_manifest_unproven"]
    if not isinstance(vectors, dict):
        return ["vectors_shape_invalid"]
    errors = set(_exact_keys(vectors, ["schema", "manifest_canonical_sha256", "provenance_type", "vectors"], "vectors_keys_invalid"))
    if vectors.get("schema") != VECTOR_SCHEMA:
        errors.add("vectors_schema_invalid")
    if vectors.get("provenance_type") != "static_decoder_vector":
        errors.add("vectors_provenance_invalid")
    if vectors.get("manifest_canonical_sha256") != canonical_json_sha256(manifest):
        errors.add("vectors_manifest_hash_mismatch")
    rows = vectors.get("vectors")
    if not isinstance(rows, list) or len(rows) != 9:
        errors.add("vectors_count_invalid")
    errors.add("decoder_validation_not_available_in_ns4_infeasible_branch")
    return sorted(errors)


def verify_csharp_constants(manifest: dict, csharp_source_text: str) -> list[str]:
    if not isinstance(manifest, dict) or manifest.get("decoder_spec") is None:
        return ["csharp_source_not_allowed_in_ns4_infeasible"]
    if not isinstance(csharp_source_text, str):
        return ["csharp_source_invalid"]
    return ["csharp_constant_verification_not_available_in_ns4_infeasible_branch"]


def _requirement_status(manifest: object) -> Dict[str, str]:
    result = {requirement_id: "invalid" for requirement_id in REQUIRED_IDS}
    if isinstance(manifest, dict) and isinstance(manifest.get("requirements"), list):
        for requirement in manifest["requirements"]:
            if (
                isinstance(requirement, dict)
                and isinstance(requirement.get("id"), str)
                and requirement.get("id") in result
            ):
                result[requirement["id"]] = requirement.get("status", "invalid")
    return result


def analyze_readiness(
    manifest: object,
    binary: bytes,
    vectors: object | None,
    csharp_source_text: str | None,
) -> dict:
    manifest_errors, independence_errors, binary_window_errors = _validate_manifest_details(manifest, binary)
    status = _requirement_status(manifest)
    proven_count = sum(value == "proven" for value in status.values())
    unproven_count = sum(value == "unproven" for value in status.values())
    proof_count = len(manifest.get("proofs", [])) if isinstance(manifest, dict) and isinstance(manifest.get("proofs"), list) else 0

    vector_errors = []
    vector_count = 0
    vector_pass_count = 0
    if vectors is not None:
        vector_errors = validate_vectors(manifest if isinstance(manifest, dict) else {}, vectors)
        if isinstance(vectors, dict) and isinstance(vectors.get("vectors"), list):
            vector_count = len(vectors["vectors"])
        vector_pass_count = 0 if vector_errors else vector_count

    csharp_checked = csharp_source_text is not None
    csharp_errors = (
        verify_csharp_constants(manifest if isinstance(manifest, dict) else {}, csharp_source_text)
        if csharp_checked
        else []
    )

    binary_obj = manifest.get("binary") if isinstance(manifest, dict) else None
    binary_matches = bool(
        isinstance(binary_obj, dict)
        and isinstance(binary, (bytes, bytearray))
        and len(binary) == binary_obj.get("size")
        and hashlib.sha256(binary).hexdigest() == binary_obj.get("sha256")
    )
    all_proven = proven_count == len(REQUIRED_IDS)
    static_gate = bool(
        not manifest_errors
        and not vector_errors
        and all_proven
        and isinstance(manifest, dict)
        and manifest.get("decoder_spec") is not None
        and vectors is not None
        and vector_count == 9
        and vector_pass_count == 9
        and binary_matches
    )
    all_errors = manifest_errors or vector_errors or csharp_errors
    if all_errors:
        outcome = "NS4_INVALID"
    else:
        outcome = "NS4_READY_FOR_CAPTURE" if static_gate and csharp_checked and not csharp_errors else "NS4_INFEASIBLE"

    return {
        "schema": REPORT_SCHEMA,
        "outcome": outcome,
        "binary_sha256": hashlib.sha256(binary).hexdigest() if isinstance(binary, (bytes, bytearray)) else None,
        "binary_size": len(binary) if isinstance(binary, (bytes, bytearray)) else None,
        "binary_matches_manifest": binary_matches,
        "manifest_canonical_sha256": canonical_json_sha256(manifest) if isinstance(manifest, dict) else None,
        "requirement_status": status,
        "proven_requirement_count": proven_count,
        "unproven_requirement_count": unproven_count,
        "proof_count": proof_count,
        "independence_error_count": independence_errors,
        "binary_window_error_count": binary_window_errors,
        "manifest_error_count": len(manifest_errors),
        "manifest_errors": manifest_errors,
        "vector_count": vector_count,
        "vector_pass_count": vector_pass_count,
        "vector_fail_count": len(vector_errors),
        "static_evidence_gate_passed": static_gate,
        "csharp_constant_check_performed": csharp_checked,
        "csharp_constant_error_count": len(csharp_errors),
        "ns4_semantic_gate_passed": outcome == "NS4_READY_FOR_CAPTURE",
        "trace_extension_ready": outcome == "NS4_READY_FOR_CAPTURE",
        "successor_capture_plan_allowed": outcome == "NS4_READY_FOR_CAPTURE",
        "production_evidence": False,
        "deployment_authorized": False,
        "trace_enablement_authorized": False,
        "duel_required_next": False,
        "rerank_authorized": False,
    }

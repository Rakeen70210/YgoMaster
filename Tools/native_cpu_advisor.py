#!/usr/bin/env python3
"""Pure, offline visible-lethal advisor for the NS3 native CPU spike."""
from __future__ import annotations

import hashlib
import json
import re
from typing import Any, Dict, Iterable, List, Sequence, Set, Tuple


CORPUS_SCHEMA = "ygomaster.native_cpu_advisor_corpus.v1"
REPORT_SCHEMA = "ygomaster.native_cpu_advisor_report.v1"
FIXTURE_SCHEMA = "ygomaster.native_cpu_advisor_fixture.v1"
SUPPORTED_DUEL_DLL_SHA256 = (
    "97bd4d136e39b0872e4a8a9171632f1f0bd9bb04d69af08e836c56e684d43c44"
)
FROZEN_PRODUCTION_CORPUS_CANONICAL_SHA256 = (
    "e4e1ae7213a0204ae6f5c82a03704eff7850b8ba9f3fae451b3fbb0f7ff96892"
)
MAX_CANDIDATES = 299
MAX_U32 = 0xFFFFFFFF
MAX_U16 = 0xFFFF
ALLOWED_ACTION_FAMILIES = {
    "attack_direct",
    "attack_monster",
    "unsupported",
}
REQUIRED_ASSUMPTIONS = [
    "public visible state only",
    "card effects and hidden responses are not modeled",
]
SYNTHETIC_REQUIRED_FIXTURE_COUNT = 11
FORBIDDEN_KEYS = {
    "card_id",
    "card_name",
    "effect",
    "effect_text",
    "opponent_hand",
    "opponent_hand_card_ids",
    "opponent_deck",
    "opponent_extra_deck",
}
RATIONALES = {
    "input_invalid": "The decision is malformed and cannot be evaluated.",
    "single_candidate": (
        "Only one native candidate is available, so there is no alternative "
        "to recommend."
    ),
    "public_state_missing": (
        "The native trace has no public battle state for this decision."
    ),
    "opponent_lp_missing": "The opponent life-point value is unknown.",
    "native_semantics_unproven": (
        "The native selected candidate has no proven attack semantics."
    ),
    "native_action_family_unsupported": (
        "The native selected candidate is outside the supported attack-only policy."
    ),
    "attacker_missing": (
        "The candidate attacker is absent from the public monster state."
    ),
    "attacker_not_owned": (
        "The candidate attacker is not controlled by the owned CPU seat."
    ),
    "attacker_not_face_up": "The candidate attacker is not known face up.",
    "attacker_attack_unknown": (
        "The candidate attacker's current attack value is unknown."
    ),
    "direct_attack_has_target": (
        "The direct-attack candidate unexpectedly contains a target."
    ),
    "monster_attack_target_missing": (
        "The monster-attack candidate has no public target instance."
    ),
    "target_not_opponent": (
        "The candidate target is not controlled by the opponent seat."
    ),
    "target_not_face_up": "The candidate target is not known face up.",
    "target_position_unknown": (
        "The candidate target's battle position is unknown."
    ),
    "target_attack_unknown": (
        "The attack-position target's current attack value is unknown."
    ),
    "target_defense_unknown": (
        "The defense-position target's current defense value is unknown."
    ),
    "native_already_visible_lethal": (
        "The native choice already deals {native_visible_damage} visible battle "
        "damage against {opponent_lp} LP."
    ),
    "no_visible_lethal_alternative": (
        "No proven alternative produces visible lethal under the supplied public "
        "battle state."
    ),
    "visible_lethal_alternative": (
        "Candidate {recommended_index} deals {recommended_visible_damage} visible "
        "battle damage against {opponent_lp} LP while the native choice deals "
        "{native_visible_damage}."
    ),
}
DECISION_REQUIRED_KEYS = {
    "decision_id",
    "duel_dll_sha256",
    "duel_generation",
    "my_id",
    "owned_seat",
    "turn",
    "turn_player",
    "phase",
    "candidate_count",
    "native_selected_index",
    "native_selected_raw",
    "native_scores_available",
    "candidates",
    "public_state",
}
CANDIDATE_REQUIRED_KEYS = {
    "index",
    "raw",
    "word0",
    "word1",
    "auxiliary_raw",
    "is_native_selected",
    "semantics",
}
SEMANTICS_REQUIRED_KEYS = {
    "status",
    "action_family",
    "attacker_instance_id",
    "target_instance_id",
    "proof_refs",
}
PUBLIC_STATE_REQUIRED_KEYS = {"visibility", "opponent_lp", "monsters"}
MONSTER_REQUIRED_KEYS = {
    "instance_id",
    "controller",
    "face_up",
    "battle_position",
    "attack",
    "defense",
}


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _is_hex64(value: Any) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def _validate_range(
    errors: List[str], value: Any, low: int, high: int, code: str
) -> None:
    if not _is_int(value) or value < low or value > high:
        errors.append(code)


def _missing_keys(errors: List[str], value: Dict[str, Any], required: Set[str], prefix: str) -> None:
    for key in sorted(required - set(value)):
        errors.append(f"{prefix}_missing_{key}")


def _collect_forbidden(value: Any, found: Set[str]) -> None:
    if isinstance(value, dict):
        for key, nested in value.items():
            if key in FORBIDDEN_KEYS:
                found.add(key)
            _collect_forbidden(nested, found)
    elif isinstance(value, list):
        for nested in value:
            _collect_forbidden(nested, found)


def _canonical_json_sha256(value: Any) -> str | None:
    """Hash a JSON value without accepting a non-canonical Python object."""
    try:
        encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    except (TypeError, ValueError):
        return None
    return hashlib.sha256(encoded).hexdigest()


def _validate_public_state(public_state: Any, prefix: str) -> List[str]:
    errors: List[str] = []
    if public_state is None:
        return errors
    if not isinstance(public_state, dict):
        return [f"{prefix}_not_object"]
    _missing_keys(errors, public_state, PUBLIC_STATE_REQUIRED_KEYS, prefix)
    if public_state.get("visibility") != "public_only":
        errors.append(f"{prefix}_visibility_invalid")
    lp = public_state.get("opponent_lp")
    if lp is not None:
        _validate_range(errors, lp, 0, 99999, f"{prefix}_opponent_lp_invalid")
    monsters = public_state.get("monsters")
    if not isinstance(monsters, list):
        errors.append(f"{prefix}_monsters_not_list")
        return errors
    seen_ids: Set[int] = set()
    for index, monster in enumerate(monsters):
        item_prefix = f"{prefix}_monster_{index}"
        if not isinstance(monster, dict):
            errors.append(f"{item_prefix}_not_object")
            continue
        _missing_keys(errors, monster, MONSTER_REQUIRED_KEYS, item_prefix)
        instance_id = monster.get("instance_id")
        if not _is_int(instance_id) or instance_id < 0:
            errors.append(f"{item_prefix}_instance_id_invalid")
        elif instance_id in seen_ids:
            errors.append(f"{item_prefix}_instance_id_duplicate")
        else:
            seen_ids.add(instance_id)
        if monster.get("controller") not in (0, 1):
            errors.append(f"{item_prefix}_controller_invalid")
        if not isinstance(monster.get("face_up"), bool):
            errors.append(f"{item_prefix}_face_up_invalid")
        if monster.get("battle_position") not in {"attack", "defense", "unknown"}:
            errors.append(f"{item_prefix}_battle_position_invalid")
        for stat in ("attack", "defense"):
            value = monster.get(stat)
            if value is not None:
                _validate_range(errors, value, 0, 99999, f"{item_prefix}_{stat}_invalid")
    return errors


def _validate_semantics(semantics: Any, prefix: str) -> List[str]:
    errors: List[str] = []
    if not isinstance(semantics, dict):
        return [f"{prefix}_not_object"]
    _missing_keys(errors, semantics, SEMANTICS_REQUIRED_KEYS, prefix)
    status = semantics.get("status")
    if status not in {"proven", "unproven"}:
        errors.append(f"{prefix}_status_invalid")
    family = semantics.get("action_family")
    if status == "proven" and family not in ALLOWED_ACTION_FAMILIES:
        errors.append(f"{prefix}_action_family_invalid")
    for field in ("attacker_instance_id", "target_instance_id"):
        value = semantics.get(field)
        if value is not None and (not _is_int(value) or value < 0):
            errors.append(f"{prefix}_{field}_invalid")
    proof_refs = semantics.get("proof_refs")
    if not isinstance(proof_refs, list) or not all(isinstance(ref, str) for ref in proof_refs):
        errors.append(f"{prefix}_proof_refs_invalid")
    if status == "unproven" and (
        family is not None
        or semantics.get("attacker_instance_id") is not None
        or semantics.get("target_instance_id") is not None
        or proof_refs != []
    ):
        errors.append(f"{prefix}_unproven_fields_not_null")
    return errors


def _validate_decision(decision: Any, prefix: str = "decision") -> List[str]:
    errors: List[str] = []
    if not isinstance(decision, dict):
        return [f"{prefix}_not_object"]
    _missing_keys(errors, decision, DECISION_REQUIRED_KEYS, prefix)
    if not isinstance(decision.get("decision_id"), str) or not decision.get("decision_id"):
        errors.append(f"{prefix}_id_invalid")
    if decision.get("duel_dll_sha256") != SUPPORTED_DUEL_DLL_SHA256:
        errors.append(f"{prefix}_duel_dll_hash_invalid")
    _validate_range(errors, decision.get("duel_generation"), 0, 0x7FFFFFFF, f"{prefix}_generation_invalid")
    if decision.get("my_id") not in (0, 1):
        errors.append(f"{prefix}_my_id_invalid")
    if decision.get("owned_seat") not in (0, 1):
        errors.append(f"{prefix}_owned_seat_invalid")
    elif decision.get("my_id") == decision.get("owned_seat"):
        errors.append(f"{prefix}_owned_seat_equals_my_id")
    _validate_range(errors, decision.get("turn"), 0, 0x7FFFFFFF, f"{prefix}_turn_invalid")
    if decision.get("turn_player") not in (0, 1):
        errors.append(f"{prefix}_turn_player_invalid")
    _validate_range(errors, decision.get("phase"), 0, 0x7FFFFFFF, f"{prefix}_phase_invalid")
    count = decision.get("candidate_count")
    _validate_range(errors, count, 1, MAX_CANDIDATES, f"{prefix}_candidate_count_invalid")
    candidates = decision.get("candidates")
    if not isinstance(candidates, list):
        errors.append(f"{prefix}_candidates_not_list")
        candidates = []
    elif _is_int(count) and len(candidates) != count:
        errors.append(f"{prefix}_candidate_count_mismatch")
    selected_index = decision.get("native_selected_index")
    if not _is_int(selected_index) or not _is_int(count) or selected_index < 0 or selected_index >= count:
        errors.append(f"{prefix}_selected_index_invalid")
    _validate_range(errors, decision.get("native_selected_raw"), 0, MAX_U32, f"{prefix}_selected_raw_invalid")
    if decision.get("native_scores_available") is not False:
        errors.append(f"{prefix}_native_scores_must_be_false")

    selected_markers: List[Dict[str, Any]] = []
    for index, candidate in enumerate(candidates):
        candidate_prefix = f"{prefix}_candidate_{index}"
        if not isinstance(candidate, dict):
            errors.append(f"{candidate_prefix}_not_object")
            continue
        _missing_keys(errors, candidate, CANDIDATE_REQUIRED_KEYS, candidate_prefix)
        if candidate.get("index") != index:
            errors.append(f"{candidate_prefix}_index_invalid")
        _validate_range(errors, candidate.get("raw"), 0, MAX_U32, f"{candidate_prefix}_raw_invalid")
        _validate_range(errors, candidate.get("word0"), 0, MAX_U16, f"{candidate_prefix}_word0_invalid")
        _validate_range(errors, candidate.get("word1"), 0, MAX_U16, f"{candidate_prefix}_word1_invalid")
        if (
            _is_int(candidate.get("raw"))
            and _is_int(candidate.get("word0"))
            and _is_int(candidate.get("word1"))
            and candidate.get("raw") != candidate.get("word0") | (candidate.get("word1") << 16)
        ):
            errors.append(f"{candidate_prefix}_raw_word_mismatch")
        _validate_range(
            errors,
            candidate.get("auxiliary_raw"),
            0,
            MAX_U32,
            f"{candidate_prefix}_auxiliary_raw_invalid",
        )
        if not isinstance(candidate.get("is_native_selected"), bool):
            errors.append(f"{candidate_prefix}_selected_marker_invalid")
        elif candidate.get("is_native_selected"):
            selected_markers.append(candidate)
        errors.extend(_validate_semantics(candidate.get("semantics"), f"{candidate_prefix}_semantics"))
    if len(selected_markers) != 1:
        errors.append(f"{prefix}_selected_marker_count")
    elif _is_int(selected_index):
        selected = selected_markers[0]
        if selected.get("index") != selected_index:
            errors.append(f"{prefix}_selected_marker_index_mismatch")
        if selected.get("raw") != decision.get("native_selected_raw"):
            errors.append(f"{prefix}_selected_marker_raw_mismatch")
    errors.extend(_validate_public_state(decision.get("public_state"), f"{prefix}_public_state"))
    return errors


def validate_corpus(corpus: Dict[str, Any]) -> List[str]:
    """Return deterministic structural/schema errors without mutating input."""
    errors: List[str] = []
    if not isinstance(corpus, dict):
        return ["corpus_not_object"]
    forbidden: Set[str] = set()
    _collect_forbidden(corpus, forbidden)
    errors.extend(f"forbidden_field_{key}" for key in sorted(forbidden))
    if corpus.get("schema") != CORPUS_SCHEMA:
        errors.append("schema_mismatch")
    corpus_id = corpus.get("corpus_id")
    if not isinstance(corpus_id, str) or not corpus_id:
        errors.append("corpus_id_invalid")
    provenance_type = corpus.get("provenance_type")
    if provenance_type not in {"production_trace", "synthetic_fixture"}:
        errors.append("provenance_type_invalid")
    source = corpus.get("source")
    if not isinstance(source, dict):
        errors.append("source_not_object")
    elif provenance_type == "production_trace":
        if set(source) != {
            "duel_dll_sha256",
            "normalized_input_sha256",
            "correlated_session_count",
            "correlated_trace_count",
        }:
            errors.append("production_source_keys_invalid")
        if source.get("duel_dll_sha256") != SUPPORTED_DUEL_DLL_SHA256:
            errors.append("production_source_duel_hash_invalid")
        if not _is_hex64(source.get("normalized_input_sha256")):
            errors.append("production_source_normalized_hash_invalid")
        for field in ("correlated_session_count", "correlated_trace_count"):
            _validate_range(errors, source.get(field), 0, 0x7FFFFFFF, f"production_source_{field}_invalid")
        # NS3 has exactly one admissible production artifact: the canonical
        # corpus emitted from the frozen, correlation-derived NS2 fixture.
        # Source metadata alone is not evidence because it can be copied onto
        # manual or synthetic decision rows.
        if _canonical_json_sha256(corpus) != FROZEN_PRODUCTION_CORPUS_CANONICAL_SHA256:
            errors.append("production_frozen_corpus_mismatch")
    elif provenance_type == "synthetic_fixture":
        if set(source) != {"fixture"} or source.get("fixture") is not True:
            errors.append("synthetic_source_invalid")
    decisions = corpus.get("decisions")
    if not isinstance(decisions, list):
        errors.append("decisions_not_list")
        return sorted(set(errors))
    for index, decision in enumerate(decisions):
        errors.extend(_validate_decision(decision, f"decision_{index}"))
    return sorted(set(errors))


def evaluate_candidate(
    decision: Dict[str, Any], candidate: Dict[str, Any]
) -> Dict[str, Any]:
    """Evaluate one candidate using only explicit public battle facts."""
    result = {
        "candidate_index": candidate.get("index") if isinstance(candidate, dict) else None,
        "visible_damage": None,
        "visible_lethal": False,
        "scoreable": False,
        "reason": "input_invalid",
    }
    if not isinstance(decision, dict) or not isinstance(candidate, dict):
        return result
    semantics = candidate.get("semantics")
    if not isinstance(semantics, dict) or semantics.get("status") != "proven":
        result["reason"] = "native_semantics_unproven"
        return result
    public_state = decision.get("public_state")
    if public_state is None:
        result["reason"] = "public_state_missing"
        return result
    if not isinstance(public_state, dict):
        return result
    opponent_lp = public_state.get("opponent_lp")
    if not _is_int(opponent_lp) or opponent_lp < 0 or opponent_lp > 99999:
        result["reason"] = "opponent_lp_missing"
        return result
    monsters = public_state.get("monsters")
    if not isinstance(monsters, list):
        return result
    owned_seat = decision.get("owned_seat")
    attacker = next(
        (
            monster
            for monster in monsters
            if isinstance(monster, dict)
            and monster.get("instance_id") == semantics.get("attacker_instance_id")
        ),
        None,
    )
    if attacker is None:
        result["reason"] = "attacker_missing"
        return result
    if attacker.get("controller") != owned_seat:
        result["reason"] = "attacker_not_owned"
        return result
    if attacker.get("face_up") is not True:
        result["reason"] = "attacker_not_face_up"
        return result
    if not _is_int(attacker.get("attack")):
        result["reason"] = "attacker_attack_unknown"
        return result
    family = semantics.get("action_family")
    if family == "attack_direct":
        if semantics.get("target_instance_id") is not None:
            result["reason"] = "direct_attack_has_target"
            return result
        visible_damage = attacker["attack"]
    elif family == "attack_monster":
        target_id = semantics.get("target_instance_id")
        if target_id is None:
            result["reason"] = "monster_attack_target_missing"
            return result
        target = next(
            (
                monster
                for monster in monsters
                if isinstance(monster, dict)
                and monster.get("instance_id") == target_id
            ),
            None,
        )
        if target is None or target.get("controller") == owned_seat:
            result["reason"] = "target_not_opponent"
            return result
        if target.get("face_up") is not True:
            result["reason"] = "target_not_face_up"
            return result
        position = target.get("battle_position")
        if position == "attack":
            if not _is_int(target.get("attack")):
                result["reason"] = "target_attack_unknown"
                return result
            visible_damage = max(attacker["attack"] - target["attack"], 0)
        elif position == "defense":
            if not _is_int(target.get("defense")):
                result["reason"] = "target_defense_unknown"
                return result
            visible_damage = 0
        else:
            result["reason"] = "target_position_unknown"
            return result
    else:
        result["reason"] = "native_action_family_unsupported"
        return result
    result["visible_damage"] = visible_damage
    result["visible_lethal"] = visible_damage >= opponent_lp
    result["scoreable"] = True
    result["reason"] = None
    return result


def _rationale(reason: str, **values: Any) -> str:
    template = RATIONALES.get(reason, RATIONALES["input_invalid"])
    return template.format(**values)


def _invalid_advice(decision: Any, provenance_type: str) -> Dict[str, Any]:
    return {
        "decision_id": decision.get("decision_id") if isinstance(decision, dict) else None,
        "provenance_type": provenance_type,
        "status": "unscorable",
        "reason": "input_invalid",
        "scoreable": False,
        "native_index": None,
        "recommended_index": None,
        "native_visible_damage": None,
        "recommended_visible_damage": None,
        "excluded_alternative_reasons": {},
        "rationale": RATIONALES["input_invalid"],
        "assumptions": list(REQUIRED_ASSUMPTIONS),
        "rerank_authorized": False,
    }


def advise_decision(decision: Dict[str, Any], provenance_type: str) -> Dict[str, Any]:
    """Return one fixed-contract advice result without mutating the decision."""
    result = _invalid_advice(decision, provenance_type)
    if not isinstance(decision, dict) or provenance_type not in {
        "production_trace",
        "synthetic_fixture",
    }:
        return result
    if _validate_decision(decision):
        return result
    candidates = decision["candidates"]
    native_index = decision["native_selected_index"]
    result["native_index"] = native_index
    if decision["candidate_count"] < 2:
        result.update(
            status="keep_native",
            reason="single_candidate",
            scoreable=False,
            rationale=RATIONALES["single_candidate"],
        )
        return result
    if decision["public_state"] is None:
        result.update(
            status="unscorable",
            reason="public_state_missing",
            rationale=RATIONALES["public_state_missing"],
        )
        return result
    native_candidate = candidates[native_index]
    native_semantics = native_candidate["semantics"]
    if native_semantics["status"] == "unproven":
        result.update(
            status="unscorable",
            reason="native_semantics_unproven",
            rationale=RATIONALES["native_semantics_unproven"],
        )
        return result
    if native_semantics["action_family"] not in {"attack_direct", "attack_monster"}:
        result.update(
            status="unscorable",
            reason="native_action_family_unsupported",
            rationale=RATIONALES["native_action_family_unsupported"],
        )
        return result
    native_eval = evaluate_candidate(decision, native_candidate)
    if not native_eval["scoreable"]:
        reason = native_eval["reason"]
        result.update(
            status="unscorable",
            reason=reason,
            rationale=_rationale(reason),
        )
        return result
    result["native_visible_damage"] = native_eval["visible_damage"]
    opponent_lp = decision["public_state"]["opponent_lp"]
    if native_eval["visible_lethal"]:
        result.update(
            status="keep_native",
            reason="native_already_visible_lethal",
            scoreable=True,
            rationale=_rationale(
                "native_already_visible_lethal",
                native_visible_damage=native_eval["visible_damage"],
                opponent_lp=opponent_lp,
            ),
        )
        return result
    excluded: Dict[str, int] = {}
    best: Dict[str, Any] | None = None
    for candidate in candidates:
        if candidate["index"] == native_index:
            continue
        evaluated = evaluate_candidate(decision, candidate)
        if not evaluated["scoreable"]:
            reason = evaluated["reason"]
            excluded[reason] = excluded.get(reason, 0) + 1
            continue
        if not evaluated["visible_lethal"]:
            continue
        candidate_index = candidate["index"]
        if best is None or (
            evaluated["visible_damage"] > best["visible_damage"]
            or (
                evaluated["visible_damage"] == best["visible_damage"]
                and candidate_index < best["index"]
            )
        ):
            best = {
                "index": candidate_index,
                "visible_damage": evaluated["visible_damage"],
            }
    result["excluded_alternative_reasons"] = dict(sorted(excluded.items()))
    if best is None:
        result.update(
            status="keep_native",
            reason="no_visible_lethal_alternative",
            scoreable=True,
            rationale=RATIONALES["no_visible_lethal_alternative"],
        )
        return result
    result.update(
        status="recommend_candidate",
        reason="visible_lethal_alternative",
        scoreable=True,
        recommended_index=best["index"],
        recommended_visible_damage=best["visible_damage"],
        rationale=_rationale(
            "visible_lethal_alternative",
            recommended_index=best["index"],
            recommended_visible_damage=best["visible_damage"],
            opponent_lp=opponent_lp,
            native_visible_damage=native_eval["visible_damage"],
        ),
    )
    return result


def canonical_state_identity(decision: Dict[str, Any]) -> str:
    canonical = {
        "duel_generation": decision.get("duel_generation"),
        "turn": decision.get("turn"),
        "turn_player": decision.get("turn_player"),
        "phase": decision.get("phase"),
        "candidate_raws": [
            candidate.get("raw") for candidate in decision.get("candidates", [])
        ],
        "native_selected_index": decision.get("native_selected_index"),
        "public_state": decision.get("public_state"),
    }
    encoded = json.dumps(canonical, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def _advice_rows(corpora: Sequence[Dict[str, Any]]) -> Iterable[Tuple[Dict[str, Any], Dict[str, Any]]]:
    for corpus in corpora:
        if not isinstance(corpus, dict) or validate_corpus(corpus):
            continue
        provenance_type = corpus["provenance_type"]
        for decision in corpus["decisions"]:
            yield decision, advise_decision(decision, provenance_type)


def analyze_corpora(
    corpora: Sequence[Dict[str, Any]],
    fixture_expectations: Sequence[Dict[str, Any]],
) -> Dict[str, Any]:
    """Aggregate production and synthetic results with strict provenance separation."""
    report: Dict[str, Any] = {
        "schema": REPORT_SCHEMA,
        "input_corpus_count": len(corpora),
        "production_decision_count": 0,
        "production_multi_candidate_count": 0,
        "production_scoreable_count": 0,
        "production_recommendation_count": 0,
        "production_distinct_state_count": 0,
        "production_recommended_distinct_state_count": 0,
        "production_status_counts": {},
        "production_reason_counts": {},
        "synthetic_decision_count": 0,
        "synthetic_required_fixture_count": SYNTHETIC_REQUIRED_FIXTURE_COUNT,
        "synthetic_exact_expectation_pass_count": 0,
        "synthetic_exact_expectation_fail_count": 0,
        "synthetic_fixture_coverage_passed": False,
        "card_identity_invariance_passed": False,
        "validation_error_count": 0,
        "ns3_advisor_gate_passed": False,
        "production_feasibility": False,
        "rerank_authorized": False,
        "duel_required_next": False,
    }
    expected_map: Dict[str, Dict[str, Any]] = {}
    duplicate_expected = False
    for expectation in fixture_expectations:
        fixture_id = expectation.get("fixture_id") if isinstance(expectation, dict) else None
        if not isinstance(fixture_id, str) or fixture_id in expected_map:
            duplicate_expected = True
        elif isinstance(expectation, dict):
            expected_map[fixture_id] = expectation
    seen_fixture_ids: List[str] = []
    fixture_advice: Dict[str, Dict[str, Any]] = {}
    production_states: Set[str] = set()
    recommended_states: Set[str] = set()
    for corpus in corpora:
        errors = validate_corpus(corpus)
        report["validation_error_count"] += len(errors)
        if errors or not isinstance(corpus, dict):
            continue
        provenance_type = corpus["provenance_type"]
        if provenance_type == "production_trace":
            for decision in corpus["decisions"]:
                advice = advise_decision(decision, provenance_type)
                report["production_decision_count"] += 1
                if decision["candidate_count"] >= 2:
                    report["production_multi_candidate_count"] += 1
                if advice["scoreable"]:
                    report["production_scoreable_count"] += 1
                if advice["status"] == "recommend_candidate":
                    report["production_recommendation_count"] += 1
                    recommended_states.add(canonical_state_identity(decision))
                production_states.add(canonical_state_identity(decision))
                status_counts = report["production_status_counts"]
                status_counts[advice["status"]] = status_counts.get(advice["status"], 0) + 1
                reason_counts = report["production_reason_counts"]
                reason_counts[advice["reason"]] = reason_counts.get(advice["reason"], 0) + 1
        else:
            report["synthetic_decision_count"] += len(corpus["decisions"])
            fixture_id = corpus["corpus_id"]
            seen_fixture_ids.append(fixture_id)
            if fixture_id in fixture_advice:
                continue
            if fixture_id not in expected_map or len(corpus["decisions"]) != 1:
                continue
            advice = advise_decision(corpus["decisions"][0], provenance_type)
            fixture_advice[fixture_id] = advice
            expected = expected_map[fixture_id]
            if all(advice.get(field) == expected.get(field) for field in EXPECTED_ADVICE_FIELDS):
                report["synthetic_exact_expectation_pass_count"] += 1
            else:
                report["synthetic_exact_expectation_fail_count"] += 1
    report["production_distinct_state_count"] = len(production_states)
    report["production_recommended_distinct_state_count"] = len(recommended_states)
    report["production_status_counts"] = dict(sorted(report["production_status_counts"].items()))
    report["production_reason_counts"] = dict(sorted(report["production_reason_counts"].items()))
    report["synthetic_fixture_coverage_passed"] = (
        not duplicate_expected
        and len(seen_fixture_ids) == len(set(seen_fixture_ids))
        and set(seen_fixture_ids) == set(expected_map)
        and len(expected_map) == SYNTHETIC_REQUIRED_FIXTURE_COUNT
    )
    a = fixture_advice.get("card_identity_invariance_a")
    b = fixture_advice.get("card_identity_invariance_b")
    if a is not None and b is not None:
        report["card_identity_invariance_passed"] = all(
            a.get(field) == b.get(field) for field in EXPECTED_ADVICE_FIELDS
        )
    report["ns3_advisor_gate_passed"] = (
        report["validation_error_count"] == 0
        and report["production_multi_candidate_count"] >= 2
        and report["production_scoreable_count"] >= 2
        and report["production_recommendation_count"] >= 2
        and report["production_recommended_distinct_state_count"] >= 2
        and report["synthetic_fixture_coverage_passed"]
        and report["synthetic_exact_expectation_pass_count"] == SYNTHETIC_REQUIRED_FIXTURE_COUNT
        and report["synthetic_exact_expectation_fail_count"] == 0
        and report["card_identity_invariance_passed"]
    )
    report["production_feasibility"] = report["ns3_advisor_gate_passed"]
    return report


EXPECTED_ADVICE_FIELDS = (
    "status",
    "reason",
    "scoreable",
    "recommended_index",
    "native_visible_damage",
    "recommended_visible_damage",
)

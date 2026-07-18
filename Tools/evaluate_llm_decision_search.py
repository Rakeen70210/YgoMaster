#!/usr/bin/env python3
"""Offline decision-search corpus evaluator (YGOMASTER-LLM-005 Slice 4).

Pure callable API + CLI for frozen fixtures under Tools/fixtures/llm_search/.
Does not touch runtime schema, broker request shape, or providers.

Schema: ygomaster.llm_decision_search_corpus.v1

Hidden sentinels are evaluator-only and never passed to decision callbacks.

Aggregation uses corpus micro-totals (sum hits / sum requirements), not averages of
per-fixture ratios. Legal-but-unacceptable roots are root_regret, not invalid_selection.
CLI fixture_oracle is explicitly non-authoritative for provider quality claims.
"""

from __future__ import annotations

import argparse
import copy
import json
import sys
from pathlib import Path


CORPUS_SCHEMA = "ygomaster.llm_decision_search_corpus.v1"

DEFAULT_THRESHOLDS = {
    "root_coverage_min": 1.0,
    "required_line_recall_min": 0.95,
    "hidden_leaks_max": 0,
    "invalid_selections_max": 0,
    "root_regrets_max": 0,
    "grounded_outcome_contradictions_max": 0,
    "destroy_vs_negate_semantic_pass_min": 1.0,
    "remain_face_up_semantic_pass_min": 1.0,
}

# Phrases that contradict a grounded primary_effect_stopped=false outcome.
_GROUNDED_STOP_CLAIM_PHRASES = (
    "negate",
    "negates",
    "negated",
    "negation",
    "stops the effect",
    "stop the effect",
    "stopped the effect",
    "interrupted",
    "interrupts",
    "prevents resolution",
    "prevent resolution",
    "will not resolve",
    "won't resolve",
)

_REQUIRED_TOP_LEVEL = (
    "schema",
    "fixture_id",
    "family",
    "decision_snapshot",
    "expectations",
    "hidden_sentinels",
    "provider_visible",
)

_REQUIRED_EXPECTATION_KEYS = (
    "required_root_action_ids",
    "required_candidate_lines",
    "forbidden_hidden_facts",
    "acceptable_selected_roots",
    "forbidden_rationale_claims",
    "required_outcome_properties",
    "required_score_rationale_properties",
    "coverage_budget_limits",
    "decline",
)


def _json_blob(value) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def _deep_copy(value):
    return copy.deepcopy(value)


def _as_path(path):
    return Path(path)


def _hidden_sentinel_values(fixture) -> list:
    tokens = []
    sent = fixture.get("hidden_sentinels") or {}
    for _key, value in sent.items():
        if value is None:
            continue
        tokens.append(str(value))
    return tokens


def _forbidden_hidden_fact_tokens(fixture) -> list:
    tokens = []
    for token in (fixture.get("expectations") or {}).get("forbidden_hidden_facts") or []:
        if token is not None:
            tokens.append(str(token))
    return tokens


def _all_hidden_check_tokens(fixture) -> list:
    """Union of private sentinels and forbidden_hidden_facts for surface checks."""
    out = []
    seen = set()
    for token in _hidden_sentinel_values(fixture) + _forbidden_hidden_fact_tokens(fixture):
        if token and token not in seen:
            seen.add(token)
            out.append(token)
    return out


def _contains_any_token(blob: str, tokens) -> list:
    hits = []
    for token in tokens:
        if token and token in blob:
            hits.append(token)
    return hits


def _root_shell_action_ids(fixture) -> set:
    ids = set()
    proj = fixture.get("search_projection") or {}
    for line in proj.get("lines") or []:
        if not isinstance(line, dict):
            continue
        if line.get("is_root_shell"):
            try:
                ids.add(int(line.get("root_action_id")))
            except (TypeError, ValueError):
                pass
    if not ids and not proj:
        snap = fixture.get("decision_snapshot") or {}
        for action in snap.get("legal_actions") or []:
            if isinstance(action, dict) and "action_id" in action:
                try:
                    ids.add(int(action["action_id"]))
                except (TypeError, ValueError):
                    pass
    return ids


def _legal_action_ids(fixture) -> set:
    ids = set()
    for action in (fixture.get("decision_snapshot") or {}).get("legal_actions") or []:
        if isinstance(action, dict) and "action_id" in action:
            try:
                ids.add(int(action["action_id"]))
            except (TypeError, ValueError):
                pass
    return ids


def _projection_lines(fixture) -> list:
    proj = fixture.get("search_projection") or {}
    lines = proj.get("lines") or []
    return [line for line in lines if isinstance(line, dict)]


def _find_line(fixture, line_id):
    for line in _projection_lines(fixture):
        if line.get("line_id") == line_id:
            return line
    return None


def _step_text(line) -> str:
    parts = []
    for step in line.get("steps") or []:
        if isinstance(step, dict):
            parts.append(str(step.get("label") or ""))
    for unlock in line.get("unlocks") or []:
        parts.append(str(unlock))
    return " ".join(parts)


def _outcome_for_root(fixture, root_action_id):
    for line in _projection_lines(fixture):
        try:
            rid = int(line.get("root_action_id"))
        except (TypeError, ValueError):
            continue
        if rid == int(root_action_id) and line.get("immediate_outcome") is not None:
            return line.get("immediate_outcome")
    return None


def _provider_visible_payload(fixture) -> dict:
    """Build callback payload: provider_visible only (never hidden_sentinels)."""
    if isinstance(fixture.get("provider_visible"), dict):
        return _deep_copy(fixture["provider_visible"])
    return {}


def _normalize_errors(errors):
    if not errors:
        return {"ok": True, "valid": True, "errors": []}
    return {"ok": False, "valid": False, "errors": list(errors)}


def validate_fixture(fixture) -> dict:
    """Validate one fixture. Returns {ok, valid, errors}."""
    errors = []
    if not isinstance(fixture, dict):
        return _normalize_errors(["fixture is not an object"])

    fid = fixture.get("fixture_id") or "<missing_id>"

    for key in _REQUIRED_TOP_LEVEL:
        if key not in fixture:
            errors.append("%s: missing top-level field %s" % (fid, key))

    if fixture.get("schema") != CORPUS_SCHEMA:
        errors.append("%s: schema mismatch (expected %s)" % (fid, CORPUS_SCHEMA))

    if not fixture.get("search_projection") and not fixture.get("unsupported_reason"):
        errors.append("%s: needs search_projection and/or unsupported_reason" % fid)

    exp = fixture.get("expectations")
    if not isinstance(exp, dict):
        errors.append("%s: expectations must be an object" % fid)
        exp = {}
    else:
        for key in _REQUIRED_EXPECTATION_KEYS:
            if key not in exp:
                errors.append("%s: expectations missing %s" % (fid, key))

    # Callback-visible / frozen surfaces must not contain sentinels OR forbidden_hidden_facts.
    tokens = _all_hidden_check_tokens(fixture)
    for surface_name in ("provider_visible", "decision_snapshot", "search_projection"):
        surface = fixture.get(surface_name)
        if surface is None:
            continue
        blob = _json_blob(surface)
        hits = _contains_any_token(blob, tokens)
        if hits:
            errors.append(
                "%s: hidden/forbidden leak in %s: %s"
                % (fid, surface_name, ",".join(hits))
            )

    required_roots = list(exp.get("required_root_action_ids") or [])
    represented = _root_shell_action_ids(fixture)
    legal = _legal_action_ids(fixture)
    for root_id in required_roots:
        try:
            rid = int(root_id)
        except (TypeError, ValueError):
            errors.append("%s: invalid required root id %r" % (fid, root_id))
            continue
        if rid not in represented and rid not in legal:
            errors.append("%s: missing required root action_id=%s" % (fid, rid))
        elif fixture.get("search_projection") is not None and rid not in represented:
            errors.append(
                "%s: missing required root shell action_id=%s in search_projection"
                % (fid, rid)
            )

    for line in _projection_lines(fixture):
        steps = line.get("steps") or []
        for index, step in enumerate(steps):
            if not isinstance(step, dict) or index == 0:
                continue
            if step.get("commit_eligible") is True:
                errors.append(
                    "%s: future step commit-eligible on line %s step %s"
                    % (fid, line.get("line_id"), index)
                )
            if step.get("current_legal") is True:
                errors.append(
                    "%s: future step current_legal on line %s step %s"
                    % (fid, line.get("line_id"), index)
                )

    for line in _projection_lines(fixture):
        outcome = line.get("immediate_outcome")
        if not isinstance(outcome, dict):
            continue
        stopped = outcome.get("primary_effect_stopped")
        expected_resolve = outcome.get("target_effect_expected_to_resolve")
        disruption = outcome.get("primary_disruption_value")
        caps = outcome.get("response_capabilities") or []
        if not isinstance(caps, list):
            caps = []
        caps_l = [str(c).lower() for c in caps]
        has_negate = any("negate" in c for c in caps_l)
        has_destroy = any(c == "destroy" or c.endswith(":destroy") for c in caps_l)

        if stopped is True and expected_resolve is True:
            errors.append(
                "%s: contradictory grounded outcome on line %s "
                "(primary_effect_stopped true while target_effect_expected_to_resolve true)"
                % (fid, line.get("line_id"))
            )

        if (
            has_destroy
            and not has_negate
            and stopped is True
            and (disruption is None or int(disruption) == 0)
        ):
            errors.append(
                "%s: contradictory outcome primary_effect_stopped on destroy-only root %s"
                % (fid, line.get("line_id"))
            )

        by_root = (exp.get("required_outcome_properties") or {}).get("by_root_action_id") or {}
        try:
            rid_key = str(int(line.get("root_action_id")))
        except (TypeError, ValueError):
            rid_key = None
        if rid_key and rid_key in by_root and isinstance(by_root[rid_key], dict):
            req = by_root[rid_key]
            if (
                req.get("primary_effect_stopped") is False
                and outcome.get("primary_effect_stopped") is True
            ):
                errors.append(
                    "%s: outcome contradiction primary_effect_stopped for root %s"
                    % (fid, rid_key)
                )
            if (
                req.get("target_effect_expected_to_resolve") is True
                and outcome.get("primary_effect_stopped") is True
            ):
                errors.append(
                    "%s: contradictory outcome primary_effect vs resolve for root %s"
                    % (fid, rid_key)
                )

    fixture_unsup = fixture.get("unsupported_reason")
    exact_unsup = exp.get("exact_unsupported_reason")
    if exact_unsup is not None and fixture_unsup is not None and fixture_unsup != exact_unsup:
        errors.append(
            "%s: unsupported_reason mismatch: fixture=%r expected=%r"
            % (fid, fixture_unsup, exact_unsup)
        )

    if fixture_unsup is not None and fixture.get("search_projection"):
        cov = (fixture.get("search_projection") or {}).get("coverage") or {}
        reasons = set()
        for key in (
            "no_candidate_reasons",
            "non_expansion_reasons",
            "exclusion_reasons",
        ):
            for reason in cov.get(key) or []:
                reasons.add(str(reason))
        for line in _projection_lines(fixture):
            if line.get("no_candidate_reason"):
                reasons.add(str(line.get("no_candidate_reason")))
        if reasons and fixture_unsup not in reasons:
            errors.append(
                "%s: unsupported_reason mismatch with projection reasons %s"
                % (fid, sorted(reasons))
            )

    for req_line in exp.get("required_candidate_lines") or []:
        if not isinstance(req_line, dict):
            continue
        line_id = req_line.get("line_id")
        line = _find_line(fixture, line_id) if line_id else None
        if line is None and line_id:
            errors.append("%s: missing required candidate line %s" % (fid, line_id))
            continue
        if line is None:
            continue
        text = _step_text(line)
        for sub in req_line.get("must_contain_step_substrings") or []:
            if sub not in text:
                errors.append(
                    "%s: line %s missing substring %r" % (fid, line_id, sub)
                )
        if req_line.get("future_steps_not_commit_eligible"):
            for index, step in enumerate(line.get("steps") or []):
                if index == 0 or not isinstance(step, dict):
                    continue
                if step.get("commit_eligible") is True or step.get("current_legal") is True:
                    errors.append(
                        "%s: required line %s has future commit/legal step" % (fid, line_id)
                    )
        if req_line.get("boundary") is not None:
            if line.get("boundary") != req_line.get("boundary"):
                errors.append("%s: line %s boundary mismatch" % (fid, line_id))

    return _normalize_errors(errors)


def validate_corpus(fixtures) -> dict:
    """Validate a list of fixtures. Rejects duplicate IDs and per-fixture errors."""
    errors = []
    if not isinstance(fixtures, list):
        return _normalize_errors(["corpus must be a list of fixtures"])

    seen = {}
    for index, fixture in enumerate(fixtures):
        if not isinstance(fixture, dict):
            errors.append("fixture[%s] is not an object" % index)
            continue
        fid = fixture.get("fixture_id")
        if not fid:
            errors.append("fixture[%s] missing fixture_id" % index)
        elif fid in seen:
            errors.append(
                "duplicate fixture_id %r (indices %s and %s)" % (fid, seen[fid], index)
            )
        else:
            seen[fid] = index
        result = validate_fixture(fixture)
        for err in result.get("errors") or []:
            errors.append(err)

    return _normalize_errors(errors)


def load_corpus(path) -> list:
    """Load all *.fixture.json files from a directory (or a single fixture file)."""
    root = _as_path(path)
    fixtures = []
    if root.is_file():
        with root.open("r", encoding="utf-8") as reader:
            data = json.load(reader)
        if isinstance(data, list):
            fixtures.extend(data)
        elif isinstance(data, dict) and isinstance(data.get("fixtures"), list):
            fixtures.extend(data["fixtures"])
        elif isinstance(data, dict):
            fixtures.append(data)
        return fixtures

    if not root.is_dir():
        raise FileNotFoundError("corpus path not found: %s" % root)

    for fixture_path in sorted(root.glob("*.fixture.json")):
        with fixture_path.open("r", encoding="utf-8") as reader:
            data = json.load(reader)
        if isinstance(data, dict):
            fixtures.append(data)
    return fixtures


def _line_matches_requirement(line, req) -> bool:
    if line is None:
        return False
    if req.get("line_id") and line.get("line_id") != req.get("line_id"):
        return False
    if req.get("root_action_id") is not None:
        try:
            if int(line.get("root_action_id")) != int(req["root_action_id"]):
                return False
        except (TypeError, ValueError):
            return False
    text = _step_text(line)
    for sub in req.get("must_contain_step_substrings") or []:
        if sub not in text:
            return False
    if req.get("boundary") is not None and line.get("boundary") != req.get("boundary"):
        return False
    if req.get("future_steps_not_commit_eligible"):
        for index, step in enumerate(line.get("steps") or []):
            if index == 0 or not isinstance(step, dict):
                continue
            if step.get("commit_eligible") is True or step.get("current_legal") is True:
                return False
    if req.get("provenance_in"):
        allowed = set(req["provenance_in"])
        if line.get("provenance") not in allowed:
            step_ok = any(
                isinstance(s, dict) and s.get("provenance") in allowed
                for s in (line.get("steps") or [])
            )
            if not step_ok and line.get("provenance") not in allowed:
                return False
    return True


def _root_micro_totals(fixture):
    """Return (root_required, root_represented) counts for micro aggregation."""
    required = list((fixture.get("expectations") or {}).get("required_root_action_ids") or [])
    if not required:
        return 0, 0
    represented = _root_shell_action_ids(fixture)
    if fixture.get("search_projection") is None:
        represented = _legal_action_ids(fixture)
    hit = 0
    total = 0
    for root_id in required:
        try:
            rid = int(root_id)
        except (TypeError, ValueError):
            continue
        total += 1
        if rid in represented:
            hit += 1
    return total, hit


def _line_micro_totals(fixture):
    """Return (line_required, line_matched) counts for micro aggregation."""
    required = list((fixture.get("expectations") or {}).get("required_candidate_lines") or [])
    if not required:
        return 0, 0
    lines = _projection_lines(fixture)
    hit = 0
    total = 0
    for req in required:
        if not isinstance(req, dict):
            continue
        total += 1
        matched = False
        if req.get("line_id"):
            line = _find_line(fixture, req["line_id"])
            matched = _line_matches_requirement(line, req)
        else:
            for line in lines:
                if _line_matches_requirement(line, req):
                    matched = True
                    break
        if matched:
            hit += 1
    return total, hit


def _ratio(hits, required, empty_is_one=True) -> float:
    if required <= 0:
        return 1.0 if empty_is_one else 0.0
    return float(hits) / float(required)


def _reason_text(response) -> str:
    if not isinstance(response, dict):
        return ""
    parts = [str(response.get("reason") or "")]
    for key in ("plan", "why_now", "rationale", "selected_line_id"):
        if response.get(key) is not None:
            parts.append(str(response.get(key)))
    return " ".join(parts)


def _count_hidden_violations(fixture, response) -> int:
    tokens = _all_hidden_check_tokens(fixture)
    blob = _json_blob(response)
    hits = _contains_any_token(blob, tokens)
    reason = _reason_text(response)
    for token in tokens:
        if token and token in reason and token not in hits:
            hits.append(token)
    return len(hits)


def _count_rationale_contradictions(fixture, response) -> int:
    exp = fixture.get("expectations") or {}
    reason = _reason_text(response)
    reason_l = reason.lower()
    count = 0
    seen_phrases = set()

    for claim in exp.get("forbidden_rationale_claims") or []:
        claim_s = str(claim)
        if not claim_s:
            continue
        key = claim_s.lower()
        if key in reason_l or claim_s in reason:
            if key not in seen_phrases:
                seen_phrases.add(key)
                count += 1

    # Grounded outcome contradiction for zero-primary destroy semantics.
    by_root = (exp.get("required_outcome_properties") or {}).get("by_root_action_id") or {}
    for _root_key, req in by_root.items():
        if not isinstance(req, dict):
            continue
        if req.get("primary_effect_stopped") is False:
            for phrase in _GROUNDED_STOP_CLAIM_PHRASES:
                if phrase in reason_l and phrase not in seen_phrases:
                    # Count each distinct forbidden claim family once per response
                    # for gate purposes: at least one contradiction is enough; still
                    # increment for any phrase match so multi-claim tests see >= 1.
                    seen_phrases.add(phrase)
                    count += 1
            break
    return count


def _selected_compatible_with_expectations(fixture, selected) -> bool:
    exp = fixture.get("expectations") or {}
    if selected is None:
        return False
    try:
        selected = int(selected)
    except (TypeError, ValueError):
        return False

    acceptable = []
    for root_id in exp.get("acceptable_selected_roots") or []:
        try:
            acceptable.append(int(root_id))
        except (TypeError, ValueError):
            pass
    if acceptable and selected not in acceptable:
        return False

    decline = exp.get("decline") or {}
    if decline.get("required"):
        decline_id = decline.get("root_action_id")
        try:
            decline_id = int(decline_id) if decline_id is not None else None
        except (TypeError, ValueError):
            decline_id = None
        if decline_id is not None and selected != decline_id:
            return False
    return True


def _semantic_pass_destroy_vs_negate(fixture, response, selected=None) -> bool:
    """Generic destroy-vs-negate semantic check (no named-card blacklist).

    Includes selected-root compatibility with decline/acceptable expectations and
    all forbidden rationale claims — not only structural projection validation.
    """
    exp = fixture.get("expectations") or {}
    gates = exp.get("semantic_gates") or []
    if "destroy_vs_negate" not in gates and "destroy_vs_negate_negative" not in gates:
        return True

    if selected is None and isinstance(response, dict):
        try:
            selected = int(response.get("action_id"))
        except (TypeError, ValueError):
            selected = None

    if not _selected_compatible_with_expectations(fixture, selected):
        return False

    if _count_rationale_contradictions(fixture, response) > 0:
        return False

    by_root = (exp.get("required_outcome_properties") or {}).get("by_root_action_id") or {}
    for root_key, req in by_root.items():
        if not isinstance(req, dict):
            continue
        outcome = _outcome_for_root(fixture, root_key)
        if outcome is None:
            if any(
                k in req
                for k in (
                    "primary_effect_stopped",
                    "target_effect_expected_to_resolve",
                    "primary_disruption_value",
                )
            ):
                return False
            continue
        if "primary_effect_stopped" in req:
            if outcome.get("primary_effect_stopped") != req["primary_effect_stopped"]:
                return False
        if "target_effect_expected_to_resolve" in req:
            if outcome.get("target_effect_expected_to_resolve") != req[
                "target_effect_expected_to_resolve"
            ]:
                return False
        if "primary_disruption_value" in req:
            try:
                if int(outcome.get("primary_disruption_value")) != int(
                    req["primary_disruption_value"]
                ):
                    return False
            except (TypeError, ValueError):
                return False
        if req.get("primary_disruption_value_min") is not None:
            try:
                if int(outcome.get("primary_disruption_value") or 0) < int(
                    req["primary_disruption_value_min"]
                ):
                    return False
            except (TypeError, ValueError):
                return False
        if req.get("secondary_benefits_include"):
            secondary = outcome.get("secondary_benefits") or []
            for need in req["secondary_benefits_include"]:
                if need not in secondary:
                    return False
    return True


def _semantic_pass_remain_face_up(fixture, response, selected=None) -> bool:
    exp = fixture.get("expectations") or {}
    gates = exp.get("semantic_gates") or []
    if "remain_face_up" not in gates:
        return True

    if selected is None and isinstance(response, dict):
        try:
            selected = int(response.get("action_id"))
        except (TypeError, ValueError):
            selected = None

    if not _selected_compatible_with_expectations(fixture, selected):
        # Remain-face-up fixtures usually accept both roots; only fail if acceptable set set.
        acceptable = exp.get("acceptable_selected_roots") or []
        if acceptable:
            return False

    if _count_rationale_contradictions(fixture, response) > 0:
        return False

    by_root = (exp.get("required_outcome_properties") or {}).get("by_root_action_id") or {}
    for root_key, req in by_root.items():
        if not isinstance(req, dict):
            continue
        outcome = _outcome_for_root(fixture, root_key)
        if outcome is None:
            return False
        if req.get("target_resolution_requires_remain_face_up") is True:
            if not outcome.get("target_resolution_requires_remain_face_up"):
                return False
        if req.get("applied_one_shot_destroy_does_not_negate_rule") is False:
            if outcome.get("applied_one_shot_destroy_does_not_negate_rule") is True:
                return False
        if req.get("must_not_confident_one_shot_still_resolves"):
            if (
                outcome.get("target_effect_expected_to_resolve") is True
                and outcome.get("primary_effect_stopped") is False
                and outcome.get("target_is_one_shot_spell_or_trap") is True
            ):
                return False
    return True


def evaluate_fixture(fixture, decision_callback) -> dict:
    """Evaluate one fixture with a decision callback(provider_visible) -> response dict."""
    if not callable(decision_callback):
        raise TypeError("decision_callback must be callable")

    fid = fixture.get("fixture_id")
    exp = fixture.get("expectations") or {}
    findings = []

    validation = validate_fixture(fixture)
    if not validation.get("ok"):
        findings.append({"kind": "fixture_invalid", "errors": validation.get("errors")})

    root_required, root_represented = _root_micro_totals(fixture)
    line_required, line_matched = _line_micro_totals(fixture)
    root_coverage = _ratio(root_represented, root_required)
    line_recall = _ratio(line_matched, line_required)

    payload = _provider_visible_payload(fixture)
    payload_blob = _json_blob(payload)
    for token in _all_hidden_check_tokens(fixture):
        # Only flag private sentinel values that must never reach callbacks.
        # forbidden_hidden_facts that are also expected public card names are
        # checked on response, not on legal public state.
        pass
    for token in _hidden_sentinel_values(fixture):
        if token and token in payload_blob:
            findings.append(
                {
                    "kind": "hidden_leak_in_callback_payload",
                    "token": token,
                }
            )

    response = decision_callback(payload)
    if not isinstance(response, dict):
        response = {"action_id": None, "reason": str(response)}

    try:
        selected = int(response.get("action_id"))
    except (TypeError, ValueError):
        selected = None

    legal = _legal_action_ids(fixture)
    acceptable = []
    for root_id in exp.get("acceptable_selected_roots") or []:
        try:
            acceptable.append(int(root_id))
        except (TypeError, ValueError):
            pass

    # invalid_selection: unparseable or not a current legal action only.
    # root_regret: legal current action outside acceptable / decline-required set.
    invalid_selection = False
    root_regret = False
    if selected is None or selected not in legal:
        invalid_selection = True
        findings.append(
            {
                "kind": "invalid_selection",
                "selected_action_id": selected,
                "legal_action_ids": sorted(legal),
            }
        )
    else:
        if acceptable and selected not in acceptable:
            root_regret = True
            findings.append(
                {
                    "kind": "root_regret",
                    "selected_action_id": selected,
                    "acceptable_selected_roots": acceptable,
                }
            )
        decline = exp.get("decline") or {}
        if decline.get("required"):
            decline_id = decline.get("root_action_id")
            try:
                decline_id = int(decline_id) if decline_id is not None else None
            except (TypeError, ValueError):
                decline_id = None
            if decline_id is not None and selected != decline_id:
                root_regret = True
                findings.append(
                    {
                        "kind": "decline_required",
                        "selected_action_id": selected,
                        "required_decline_root": decline_id,
                    }
                )

    hidden_violations = _count_hidden_violations(fixture, response)
    if hidden_violations:
        findings.append(
            {
                "kind": "hidden_information_violation",
                "count": hidden_violations,
            }
        )

    outcome_contradictions = _count_rationale_contradictions(fixture, response)
    if outcome_contradictions:
        findings.append(
            {
                "kind": "grounded_outcome_contradiction",
                "count": outcome_contradictions,
            }
        )

    try:
        latency_ms = float(response.get("latency_ms") or 0)
    except (TypeError, ValueError):
        latency_ms = 0.0
    try:
        token_cost = int(response.get("token_cost") or 0)
    except (TypeError, ValueError):
        token_cost = 0

    destroy_pass = _semantic_pass_destroy_vs_negate(fixture, response, selected=selected)
    remain_pass = _semantic_pass_remain_face_up(fixture, response, selected=selected)
    if not destroy_pass:
        findings.append({"kind": "destroy_vs_negate_semantic_fail"})
    if not remain_pass:
        findings.append({"kind": "remain_face_up_semantic_fail"})

    return {
        "fixture_id": fid,
        "family": fixture.get("family"),
        "root_required": int(root_required),
        "root_represented": int(root_represented),
        "line_required": int(line_required),
        "line_matched": int(line_matched),
        "root_coverage": root_coverage,
        "required_line_recall": line_recall,
        "hidden_information_violations": hidden_violations,
        "invalid_selection": bool(invalid_selection),
        "root_regret": bool(root_regret),
        "grounded_outcome_contradictions": int(outcome_contradictions),
        "latency_ms": latency_ms,
        "token_cost": token_cost,
        "selected_action_id": selected,
        "selected_line_id": response.get("selected_line_id"),
        "reason": response.get("reason"),
        "destroy_vs_negate_semantic_pass": destroy_pass,
        "remain_face_up_semantic_pass": remain_pass,
        "findings": findings,
    }


def aggregate_metrics(results, thresholds=None) -> dict:
    """Deterministic aggregate metrics using corpus micro-totals."""
    thresholds = dict(DEFAULT_THRESHOLDS if thresholds is None else thresholds)
    results = list(results or [])
    n = len(results)

    root_required = int(sum(int(r.get("root_required") or 0) for r in results))
    root_represented = int(sum(int(r.get("root_represented") or 0) for r in results))
    line_required = int(sum(int(r.get("line_required") or 0) for r in results))
    line_matched = int(sum(int(r.get("line_matched") or 0) for r in results))

    # Micro-total ratios; 1.0 only when the entire corpus denominator is zero.
    root_coverage = _ratio(root_represented, root_required)
    line_recall = _ratio(line_matched, line_required)

    hidden_leaks = int(sum(int(r.get("hidden_information_violations") or 0) for r in results))
    invalid_selections = int(sum(1 for r in results if r.get("invalid_selection")))
    root_regrets = int(sum(1 for r in results if r.get("root_regret")))
    grounded_contradictions = int(
        sum(int(r.get("grounded_outcome_contradictions") or 0) for r in results)
    )

    destroy_scored = [
        r
        for r in results
        if str(r.get("family") or "")
        in (
            "destroy_vs_negate_activated_oneshot",
            "value_zero_primary_without_secondary",
            "value_zero_primary_with_secondary",
        )
    ]
    if not destroy_scored:
        destroy_scored = [
            r for r in results if "destroy_vs_negate_semantic_pass" in r and r.get("family")
        ]
    if destroy_scored:
        destroy_pass = float(
            sum(1 for r in destroy_scored if r.get("destroy_vs_negate_semantic_pass"))
        ) / float(len(destroy_scored))
    else:
        destroy_pass = 1.0

    remain_scored = [
        r for r in results if str(r.get("family") or "") == "remain_face_up_control"
    ]
    if remain_scored:
        remain_pass = float(
            sum(1 for r in remain_scored if r.get("remain_face_up_semantic_pass"))
        ) / float(len(remain_scored))
    else:
        remain_pass = 1.0

    return {
        "fixtures": n,
        "root_required": root_required,
        "root_represented": root_represented,
        "line_required": line_required,
        "line_matched": line_matched,
        "root_coverage": root_coverage,
        "required_line_recall": line_recall,
        "hidden_leaks": hidden_leaks,
        "invalid_selections": invalid_selections,
        "root_regrets": root_regrets,
        "grounded_outcome_contradictions": grounded_contradictions,
        "destroy_vs_negate_semantic_pass": destroy_pass,
        "remain_face_up_semantic_pass": remain_pass,
        "latency_ms_total": float(sum(float(r.get("latency_ms") or 0) for r in results)),
        "token_cost_total": int(sum(int(r.get("token_cost") or 0) for r in results)),
        "thresholds": thresholds,
    }


def gates_pass(report) -> bool:
    """Return True when aggregate metrics meet plan thresholds."""
    if not isinstance(report, dict):
        return False
    metrics = report.get("metrics") or {}
    thresholds = metrics.get("thresholds") or report.get("thresholds") or DEFAULT_THRESHOLDS
    try:
        if float(metrics.get("root_coverage") or 0) < float(
            thresholds.get("root_coverage_min", 1.0)
        ):
            return False
        if float(metrics.get("required_line_recall") or 0) < float(
            thresholds.get("required_line_recall_min", 0.95)
        ):
            return False
        if int(metrics.get("hidden_leaks") or 0) > int(thresholds.get("hidden_leaks_max", 0)):
            return False
        if int(metrics.get("invalid_selections") or 0) > int(
            thresholds.get("invalid_selections_max", 0)
        ):
            return False
        if int(metrics.get("root_regrets") or 0) > int(thresholds.get("root_regrets_max", 0)):
            return False
        if int(metrics.get("grounded_outcome_contradictions") or 0) > int(
            thresholds.get("grounded_outcome_contradictions_max", 0)
        ):
            return False
        if float(metrics.get("destroy_vs_negate_semantic_pass") or 0) < float(
            thresholds.get("destroy_vs_negate_semantic_pass_min", 1.0)
        ):
            return False
        if float(metrics.get("remain_face_up_semantic_pass") or 0) < float(
            thresholds.get("remain_face_up_semantic_pass_min", 1.0)
        ):
            return False
    except (TypeError, ValueError):
        return False
    return True


def evaluate_corpus(fixtures, decision_callback, thresholds=None, evaluation_mode=None) -> dict:
    """Evaluate every fixture; return deterministic report with micro-total metrics."""
    thresholds = dict(DEFAULT_THRESHOLDS if thresholds is None else thresholds)
    results = []
    ordered = sorted(
        list(fixtures or []),
        key=lambda f: str((f or {}).get("fixture_id") or ""),
    )
    for fixture in ordered:
        results.append(evaluate_fixture(fixture, decision_callback))
    metrics = aggregate_metrics(results, thresholds=thresholds)
    report = {
        "schema": CORPUS_SCHEMA,
        "metrics": metrics,
        "fixtures": results,
        "gates_pass": gates_pass({"metrics": metrics}),
    }
    if evaluation_mode is not None:
        report["evaluation_mode"] = evaluation_mode
        # Only real provider callbacks are quality-gate authoritative.
        report["quality_gate_authoritative"] = evaluation_mode not in (
            "fixture_oracle",
            "always_invalid",
            "oracle",  # legacy alias of fixture_oracle
        )
    return report


def compare_one_step_vs_lookahead(fixture, one_step_callback, lookahead_callback) -> dict:
    """Invoke each callback once on independent deep copies of provider_visible.

    Does not force expected choices. Records paired action/reason/line/latency/token deltas.
    Pure callback API for real one-step/lookahead provider comparisons.
    """
    if not callable(one_step_callback) or not callable(lookahead_callback):
        raise TypeError("both callbacks must be callable")

    base = _provider_visible_payload(fixture)
    payload_a = _deep_copy(base)
    payload_b = _deep_copy(base)
    assert _json_blob(payload_a) == _json_blob(payload_b)

    one_step_response = one_step_callback(payload_a)
    lookahead_response = lookahead_callback(payload_b)

    if not isinstance(one_step_response, dict):
        one_step_response = {"action_id": None, "reason": str(one_step_response)}
    if not isinstance(lookahead_response, dict):
        lookahead_response = {"action_id": None, "reason": str(lookahead_response)}

    def _norm(resp):
        try:
            action_id = int(resp.get("action_id"))
        except (TypeError, ValueError):
            action_id = resp.get("action_id")
        try:
            latency = float(resp.get("latency_ms") or 0)
        except (TypeError, ValueError):
            latency = 0.0
        try:
            tokens = int(resp.get("token_cost") or 0)
        except (TypeError, ValueError):
            tokens = 0
        return {
            "action_id": action_id,
            "reason": resp.get("reason"),
            "line_id": resp.get("selected_line_id"),
            "selected_line_id": resp.get("selected_line_id"),
            "latency_ms": latency,
            "token_cost": tokens,
        }

    one = _norm(one_step_response)
    look = _norm(lookahead_response)

    def _delta(a, b):
        if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(
            a, bool
        ) and not isinstance(b, bool):
            return b - a
        if a == b:
            return 0
        return {"from": a, "to": b}

    deltas = {
        "action_id": _delta(one["action_id"], look["action_id"]),
        "reason": _delta(one["reason"], look["reason"]),
        "line_id": _delta(one["line_id"], look["line_id"]),
        "latency_ms": _delta(one["latency_ms"], look["latency_ms"]),
        "token_cost": _delta(one["token_cost"], look["token_cost"]),
    }

    return {
        "fixture_id": fixture.get("fixture_id"),
        "one_step": one,
        "lookahead": look,
        "deltas": deltas,
        "evaluation_mode": "provider_callback_comparison",
        "quality_gate_authoritative": True,
    }


def _fixture_oracle_callback_factory(fixtures_by_seq):
    def _cb(provider_visible):
        seq = provider_visible.get("run_effect_seq")
        fixture = fixtures_by_seq.get(seq)
        if fixture is None:
            return {
                "action_id": 0,
                "reason": "fixture_oracle fallback",
                "latency_ms": 0,
                "token_cost": 0,
            }
        roots = (fixture.get("expectations") or {}).get("acceptable_selected_roots") or [0]
        action_id = int(roots[0])
        return {
            "action_id": action_id,
            "reason": "fixture_oracle acceptable root %s" % action_id,
            "selected_line_id": None,
            "latency_ms": 1,
            "token_cost": 10,
            "confidence": 0.5,
        }

    return _cb


def _always_invalid_callback(_provider_visible):
    return {
        "action_id": 999999,
        "reason": "illegal",
        "latency_ms": 1,
        "token_cost": 1,
    }


def _build_callback(name, fixtures):
    name = (name or "fixture_oracle").strip()
    by_seq = {}
    for fixture in fixtures:
        snap = fixture.get("decision_snapshot") or {}
        seq = snap.get("run_effect_seq")
        if seq is not None:
            by_seq[seq] = fixture
        pv = fixture.get("provider_visible") or {}
        if pv.get("run_effect_seq") is not None:
            by_seq[pv.get("run_effect_seq")] = fixture

    if name in ("always_invalid", "force_invalid", "invalid"):
        return _always_invalid_callback, "always_invalid"
    if name in ("fixture_oracle", "oracle", "acceptable", "default"):
        # "oracle" remains a deprecated alias of fixture_oracle.
        return _fixture_oracle_callback_factory(by_seq), "fixture_oracle"
    raise SystemExit(
        "unknown --callback %r (use fixture_oracle|always_invalid)" % name
    )


def main(argv=None) -> int:
    """CLI entry. Returns nonzero when gates fail.

    Default callback is fixture_oracle (picks acceptable roots from fixtures).
    Oracle reports set evaluation_mode=fixture_oracle and
    quality_gate_authoritative=false so they are not mistaken for provider-quality passes.
    """
    argv = list(sys.argv[1:] if argv is None else argv)
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate offline LLM decision-search corpus (YGOMASTER-LLM-005 Slice 4). "
            "Default callback fixture_oracle is non-authoritative for provider quality."
        )
    )
    parser.add_argument(
        "--corpus",
        required=True,
        help="Directory of *.fixture.json or a single fixture/corpus JSON file",
    )
    parser.add_argument("--json", action="store_true", help="Emit report as JSON")
    parser.add_argument("--out", default=None, help="Write report JSON to this path")
    parser.add_argument(
        "--callback",
        default="fixture_oracle",
        help="Decision callback: fixture_oracle (default, non-authoritative) or always_invalid",
    )
    parser.add_argument(
        "--validate-only",
        action="store_true",
        help="Only validate corpus schema (no decision callbacks)",
    )
    args = parser.parse_args(argv)

    fixtures = load_corpus(args.corpus)
    validation = validate_corpus(fixtures)
    if args.validate_only:
        report = {
            "schema": CORPUS_SCHEMA,
            "validation": validation,
            "evaluation_mode": "validate_only",
            "quality_gate_authoritative": False,
            "metrics": aggregate_metrics([], thresholds=DEFAULT_THRESHOLDS),
            "fixtures": [],
            "gates_pass": bool(validation.get("ok")),
        }
        text = json.dumps(report, sort_keys=True, indent=2, ensure_ascii=True) + "\n"
        if args.out:
            Path(args.out).write_text(text, encoding="utf-8")
        if args.json and not args.out:
            sys.stdout.write(text)
        return 0 if validation.get("ok") else 2

    callback, mode = _build_callback(args.callback, fixtures)
    report = evaluate_corpus(
        fixtures,
        callback,
        thresholds=DEFAULT_THRESHOLDS,
        evaluation_mode=mode,
    )
    report["validation"] = validation
    if not validation.get("ok"):
        report["gates_pass"] = False

    text = json.dumps(report, sort_keys=True, indent=2, ensure_ascii=True) + "\n"
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
    if args.json and not args.out:
        sys.stdout.write(text)
    elif not args.out and not args.json:
        sys.stdout.write(text)

    if not gates_pass(report) or not validation.get("ok"):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

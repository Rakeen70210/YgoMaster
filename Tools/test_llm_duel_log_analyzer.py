import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("analyze_llm_duel_log.py")
spec = importlib.util.spec_from_file_location("analyze_llm_duel_log", MODULE_PATH)
analyzer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyzer)


class LlmDuelLogAnalyzerTests(unittest.TestCase):
    def write_log(self, events):
        temp_dir = tempfile.TemporaryDirectory()
        path = Path(temp_dir.name) / "LlmDecisionLog.jsonl"
        with path.open("w", encoding="utf-8") as writer:
            for event in events:
                writer.write(json.dumps(event, separators=(",", ":")) + "\n")
        return temp_dir, path

    def test_counts_broker_owned_attack_targets_and_explicit_divergence(self):
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "llm_broker_window_routed",
                    "run_effect_seq": 539,
                    "route": "Broker",
                    "prompt_family": "WaitInput",
                    "reason": "attack_target",
                },
                {
                    "kind": "llm_attack_target_divergence",
                    "run_effect_seq": 539,
                    "origin_run_effect_seq": 534,
                    "reason": "provider_error",
                    "fallback": "temporary_cpu",
                    "legal_target_count": 2,
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([path])
        self.assertEqual(1, summary["attack_targets"]["broker_owned_windows"])
        self.assertEqual(1, summary["attack_targets"]["fallback_divergences"])
        self.assertEqual(
            {"provider_error": 1},
            summary["attack_targets"]["divergence_reasons"],
        )

    def test_counts_grounded_blocks_and_promised_followup_results(self):
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 843,
                    "legal_actions": [
                        {
                            "action_id": 3,
                            "effect_applicability": {
                                "is_grounded": True,
                                "effect_expected_to_apply": False,
                                "reason": "blocked_by_activated_monster_effect_immunity",
                            },
                        }
                    ],
                },
                {
                    "kind": "llm_broker_rejected",
                    "error": "effect_applicability_contradiction",
                },
                {
                    "kind": "intended_followup_matched",
                    "origin_run_effect_seq": 881,
                    "current_run_effect_seq": 920,
                    "action_family": "effect_activation",
                },
                {
                    "kind": "intended_followup_unavailable",
                    "origin_run_effect_seq": 881,
                    "current_run_effect_seq": 929,
                    "reason": "effect_not_legal",
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([path])
        self.assertEqual(1, summary["effect_applicability"]["grounded_blocked_actions"])
        self.assertEqual(1, summary["effect_applicability"]["contradiction_rejections"])
        self.assertEqual(1, summary["promised_followups"]["matched"])
        self.assertEqual(1, summary["promised_followups"]["unavailable"])
        self.assertEqual(
            {"effect_not_legal": 1},
            summary["promised_followups"]["unavailable_reasons"],
        )

    def test_analyzes_broker_commit_coverage(self):
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 30,
                    "turn": 0,
                    "turn_player": 1,
                    "acting_player": 1,
                    "current_phase": 2,
                    "is_strategic_window": True,
                    "legal_actions": [{"kind": "command"}, {"kind": "move_phase"}],
                },
                {
                    "kind": "llm_broker_window_routed",
                    "run_effect_seq": 30,
                    "route": "Broker",
                    "prompt_family": "WaitInput",
                    "reason": "strategic_choices",
                },
                {"kind": "llm_broker_request_started", "request_run_effect_seq": 30},
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 30,
                    "success": True,
                    "action_id": 1,
                    "reason": "first option",
                    "confidence": 0.22,
                    "latency_ms": 1400,
                    "request_json": json.dumps(
                        {
                            "legal_actions": [
                                {"action_id": 1, "kind": "command", "command": "Summon"}
                            ]
                        },
                        separators=(",", ":"),
                    ),
                },
                {
                    "kind": "llm_broker_committed",
                    "request_run_effect_seq": 30,
                    "commit_run_effect_seq": 30,
                    "action_id": 1,
                    "action_type": "command",
                },
                {
                    "kind": "llm_broker_committed",
                    "request_run_effect_seq": 31,
                    "commit_run_effect_seq": 31,
                    "action_id": 4,
                    "action_type": "command",
                    "action": {
                        "kind": "command",
                        "command": "Decide",
                        "player": 0,
                        "position": 2,
                        "index": 0,
                    },
                },
                {
                    "kind": "llm_broker_committed",
                    "request_run_effect_seq": 32,
                    "commit_run_effect_seq": 32,
                    "action_id": 4,
                    "action_type": "command",
                    "action": {
                        "kind": "command",
                        "command": "Decide",
                        "player": 0,
                        "position": 2,
                        "index": 0,
                    },
                },
                {
                    "kind": "llm_broker_automatic_action",
                    "run_effect_seq": 40,
                    "reason": "forced_draw",
                    "action_type": "command",
                },
                {
                    "kind": "llm_broker_skipped_window",
                    "run_effect_seq": 45,
                    "reason": "mechanical_only",
                    "is_strategic_window": False,
                    "strategic_action_count": 0,
                    "mechanical_action_count": 2,
                },
                {
                    "kind": "llm_broker_window_routed",
                    "run_effect_seq": 45,
                    "route": "CpuFallback",
                    "prompt_family": "RunList",
                    "reason": "mechanical_only",
                },
                {
                    "kind": "llm_broker_unsupported_window",
                    "run_effect_seq": 45,
                    "prompt_family": "RunList",
                    "reason": "mechanical_only",
                },
                {
                    "kind": "llm_broker_rejected",
                    "request_run_effect_seq": 41,
                    "current_run_effect_seq": 42,
                    "error": "stale_run_effect_seq",
                },
                {
                    "kind": "llm_broker_response",
                    "request_run_effect_seq": 50,
                    "success": False,
                    "error": "provider_error",
                    "error_detail": "provider timed out after 20 seconds",
                    "latency_ms": 20000,
                },
                {
                    "kind": "decision_window",
                    "run_effect_seq": 61,
                    "turn": 1,
                    "turn_player": 0,
                    "acting_player": 0,
                    "current_phase": 2,
                    "is_strategic_window": False,
                    "legal_actions": [{"kind": "move_phase"}],
                },
            ]
        )
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([path])

        self.assertEqual(summary["events"]["decision_window"], 2)
        self.assertEqual(summary["events"]["llm_broker_committed"], 3)
        self.assertEqual(summary["broker"]["commits"], 3)
        self.assertEqual(summary["broker"]["responses"], 2)
        self.assertEqual(summary["broker"]["successful_responses"], 1)
        self.assertEqual(summary["broker"]["failed_responses"], 1)
        self.assertEqual(summary["broker"]["automatic_actions"], 1)
        self.assertEqual(summary["broker"]["skipped_windows"], 1)
        self.assertEqual(summary["broker"]["rejected"], 1)
        self.assertEqual(summary["broker"]["stale_rejects"], 1)
        self.assertEqual(summary["broker"]["provider_timeouts"], 1)
        self.assertEqual(summary["routes"]["total"], 2)
        self.assertEqual(summary["routes"]["by_route"]["Broker"], 1)
        self.assertEqual(summary["routes"]["by_route"]["CpuFallback"], 1)
        self.assertEqual(summary["routes"]["by_prompt_family"]["WaitInput"], 1)
        self.assertEqual(summary["routes"]["by_prompt_family"]["RunList"], 1)
        self.assertEqual(summary["routes"]["unsupported_windows"], 1)
        self.assertEqual(summary["coverage"]["turns"], [0, 1])
        self.assertEqual(summary["coverage"]["broker_action_types"], ["command"])
        self.assertEqual(summary["coverage"]["decision_action_types"], ["command", "move_phase"])
        self.assertEqual(summary["quality"]["generic_reasons"], 1)
        self.assertEqual(summary["quality"]["low_confidence"], 1)
        self.assertEqual(summary["quality"]["cardless_command_choices"], 1)
        self.assertEqual(summary["quality"]["missing_opponent_board_assessments"], 1)
        self.assertEqual(summary["quality"]["meaningful_model_calls"], 1)
        self.assertEqual(summary["quality"]["mechanical_skipped"], 1)
        self.assertEqual(summary["quality"]["strategic_windows"], 1)
        self.assertEqual(summary["quality"]["mechanical_windows"], 2)
        self.assertEqual(summary["quality"]["strategic_window_rate"], 1.0 / 3.0)
        self.assertEqual(summary["quality"]["duplicate_decide_commits"], 1)
        self.assertEqual(summary["quality"]["cpu_fallbacks"], 2)
        self.assertEqual(summary["quality"]["latency_ms_p50"], 1400)
        self.assertEqual(summary["quality"]["latency_ms_p95"], 20000)

    def test_requirement_check_reports_missing_coverage(self):
        temp_dir, path = self.write_log(
            [
                {
                    "kind": "decision_window",
                    "run_effect_seq": 30,
                    "turn": 0,
                    "turn_player": 1,
                    "acting_player": 1,
                    "current_phase": 2,
                    "legal_actions": [{"kind": "command"}],
                }
            ]
        )
        self.addCleanup(temp_dir.cleanup)
        summary = analyzer.analyze_paths([path])

        errors = analyzer.validate_requirements(
            summary,
            min_broker_commits=1,
            min_turns=2,
            required_broker_action_types=["command"],
        )

        self.assertIn("expected at least 1 broker commits, got 0", errors)
        self.assertIn("expected at least 2 turns, got 1", errors)
        self.assertIn("missing broker action types: command", errors)

    def test_quality_thresholds_report_failures(self):
        summary = {
            "broker": {"commits": 3},
            "coverage": {"turns": [0, 1], "broker_action_types": ["command"]},
            "quality": {
                "generic_reasons": 2,
                "cardless_command_choices": 1,
                "duplicate_decide_commits": 1,
                "strategic_window_rate": 0.25,
                "meaningful_model_calls": 4,
            },
        }

        errors = analyzer.validate_requirements(
            summary,
            max_generic_reason_rate=0.25,
            max_cardless_command_rate=0.0,
            max_duplicate_decide_commits=0,
            require_strategic_window_rate=0.5,
        )

        self.assertIn("generic reason rate 0.500 exceeds 0.250", errors)
        self.assertIn("cardless command rate 0.250 exceeds 0.000", errors)
        self.assertIn("duplicate Decide commits 1 exceeds 0", errors)
        self.assertIn("strategic window rate 0.250 below 0.500", errors)

    # ------------------------------------------------------------------
    # YGOMASTER-LLM-004 Slice 5 RED: duel_history audit metrics (hardened)
    # ------------------------------------------------------------------

    SENTINEL_HAND = "SENTINEL_OPP_HAND_LEAK_99501"
    SENTINEL_SET = "SENTINEL_FACEDOWN_SET_LEAK_99502"
    SENTINEL_DECK = "SENTINEL_DECK_LEAK_99503"
    SENTINEL_EXTRA = "SENTINEL_EXTRA_LEAK_99504"

    def _history_summary(self, path):
        summary = analyzer.analyze_paths([path])
        self.assertIn("duel_history", summary)
        return summary["duel_history"]

    @staticmethod
    def _nearest_rank_percentile(sorted_values, percentile):
        """
        Deterministic nearest-rank percentile (inclusive):
        index = ceil(p/100 * n) - 1, clamped to [0, n-1].
        """
        import math

        if not sorted_values:
            return 0
        n = len(sorted_values)
        idx = int(math.ceil((percentile / 100.0) * n) - 1)
        if idx < 0:
            idx = 0
        if idx >= n:
            idx = n - 1
        return sorted_values[idx]

    def _history_request(
        self,
        run_effect_seq=240,
        events=None,
        summaries=None,
        compacted=False,
        first_detailed=1,
        last_event_id=21,
        duel_generation=1,
        extra_fields=None,
    ):
        req = {
            "kind": "decision_request",
            "schema_version": 4,
            "run_effect_seq": run_effect_seq,
            "controlled_player": 1,
            "legal_actions": [
                {"action_id": 0, "kind": "command", "command": "Summon"},
            ],
            "duel_history": {
                "history_version": 1,
                "duel_generation": duel_generation,
                "last_event_id": last_event_id,
                "first_detailed_event_id": first_detailed,
                "history_compacted": compacted,
                "budget_status": "ok",
                "events": events
                if events is not None
                else [
                    {
                        "event_id": 18,
                        "actor_player": 0,
                        "kind": "normal_summon",
                        "evidence": "accepted_command",
                        "card_name": "Public Monster",
                    },
                    {
                        "event_id": 20,
                        "actor_player": 0,
                        "kind": "activate_effect",
                        "evidence": "accepted_command",
                        "card_name": "Public Monster",
                    },
                    {
                        "event_id": 21,
                        "actor_player": 0,
                        "kind": "card_moved",
                        "evidence": "public_state_delta",
                        "card_name": "Public Grave Card",
                    },
                ],
                "prior_turn_summaries": summaries if summaries is not None else [],
                "revealed_card_context": [],
            },
        }
        if extra_fields:
            req.update(extra_fields)
        return req

    def _compacted_request(self, run_effect_seq, duel_generation=1, extra_fields=None):
        """Detailed own events only; opponent history only in prior_turn_summaries."""
        return self._history_request(
            run_effect_seq=run_effect_seq,
            duel_generation=duel_generation,
            events=[
                {
                    "event_id": 10,
                    "actor_player": 1,
                    "kind": "normal_summon",
                    "evidence": "accepted_command",
                    "card_name": "Own Public",
                },
                {
                    "event_id": 12,
                    "actor_player": 1,
                    "kind": "activate_effect",
                    "evidence": "accepted_command",
                    "card_name": "Own Public",
                },
            ],
            summaries=[
                {
                    "turn": 1,
                    "first_event_id": 1,
                    "last_event_id": 3,
                    "actor_players": [0],
                    "event_count": 3,
                }
            ],
            compacted=True,
            first_detailed=10,
            last_event_id=12,
            extra_fields=extra_fields,
        )

    def test_slice5_analyzes_public_event_counts_gaps_duplicates_and_evidence(self):
        # request_json as runtime JSON *string* (not only a dict).
        request_obj = self._history_request(100, duel_generation=7)
        request_str = json.dumps(request_obj, separators=(",", ":"), ensure_ascii=False)
        events = [
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 7,
                "event_id": 1,
                "actor_player": 0,
                "evidence": "accepted_command",
                "public_event_kind": "normal_summon",
            },
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 7,
                "event_id": 2,
                "actor_player": 0,
                "evidence": "accepted_command",
                "public_event_kind": "activate_effect",
            },
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 7,
                "event_id": 2,
                "actor_player": 0,
                "evidence": "accepted_command",
                "public_event_kind": "activate_effect",
            },
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 7,
                "event_id": 5,
                "actor_player": 1,
                "evidence": "public_state_delta",
                "public_event_kind": "card_moved",
            },
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 100,
                "latency_ms": 50,
                "request_json": request_str,
                "opponent_action_assessment": "Events 18/20 used.",
                "history_event_ids_used": [18, 20],
            },
        ]
        temp_dir, path = self.write_log(events)
        self.addCleanup(temp_dir.cleanup)

        summary = analyzer.analyze_paths([path])
        self.assertIn("duel_history", summary)
        hist = summary["duel_history"]
        self.assertEqual(4, hist["public_event_count"])
        self.assertEqual(
            {"accepted_command": 3, "public_state_delta": 1},
            hist["evidence_counts"],
        )
        # Kind/actor coverage is generation-independent aggregate counts.
        self.assertEqual(
            {
                "normal_summon": 1,
                "activate_effect": 2,
                "card_moved": 1,
            },
            hist["public_event_kind_counts"],
        )
        self.assertEqual({"0": 3, "1": 1}, hist["actor_player_counts"])
        # Duplicates/gaps scoped by duel_generation.
        self.assertEqual(
            [{"duel_generation": 7, "event_id": 2}],
            hist["event_id_duplicates"],
        )
        self.assertEqual(
            [{"duel_generation": 7, "after": 2, "before": 5}],
            hist["event_id_gaps"],
        )

    def test_slice5_string_and_object_request_json_produce_identical_history_metrics(self):
        request_obj = self._compacted_request(310)
        request_str = json.dumps(request_obj, separators=(",", ":"), ensure_ascii=False)
        events_object = [
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 310,
                "request_json": request_obj,
                "opponent_action_assessment": "ok",
                "history_event_ids_used": [10],
            }
        ]
        events_string = [
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 310,
                "request_json": request_str,
                "opponent_action_assessment": "ok",
                "history_event_ids_used": [10],
            }
        ]
        temp_a, path_a = self.write_log(events_object)
        temp_b, path_b = self.write_log(events_string)
        self.addCleanup(temp_a.cleanup)
        self.addCleanup(temp_b.cleanup)

        hist_a = self._history_summary(path_a)
        hist_b = self._history_summary(path_b)
        # Payload bytes for string form are the original serialized UTF-8 length.
        self.assertEqual(
            [len(request_str.encode("utf-8"))],
            hist_b["request_payload_bytes"],
        )
        # Semantic metrics must match for object vs string forms.
        for key in (
            "compacted_request_count",
            "citation_issues",
            "assessment_issues",
            "hidden_info_leaks",
        ):
            self.assertEqual(hist_a[key], hist_b[key], key)

    def test_slice5_payload_bytes_are_exact_utf8_with_deterministic_percentiles(self):
        # Three distinct original serialized request_json strings, including non-ASCII.
        payloads = [
            json.dumps(
                self._compacted_request(401, extra_fields={"note": "ascii-only"}),
                separators=(",", ":"),
                ensure_ascii=False,
            ),
            json.dumps(
                self._compacted_request(
                    402,
                    # Multi-byte UTF-8: "カード" is 9 bytes, 3 chars — cannot pass as char count.
                    extra_fields={"note": "カード"},
                ),
                separators=(",", ":"),
                ensure_ascii=False,
            ),
            json.dumps(
                self._compacted_request(
                    403,
                    extra_fields={"note": "café-長さ"},
                ),
                separators=(",", ":"),
                ensure_ascii=False,
            ),
        ]
        byte_counts = [len(p.encode("utf-8")) for p in payloads]
        # Sanity: non-ASCII fixture has more UTF-8 bytes than Unicode characters.
        self.assertGreater(byte_counts[1], len(payloads[1]))
        self.assertGreater(byte_counts[2], len(payloads[2]))

        events = []
        for idx, payload in enumerate(payloads):
            events.append(
                {
                    "kind": "llm_broker_response",
                    "success": True,
                    "request_run_effect_seq": 401 + idx,
                    "request_json": payload,  # original string form required
                    "opponent_action_assessment": "ok",
                    "history_event_ids_used": [10],
                }
            )
        temp_dir, path = self.write_log(events)
        self.addCleanup(temp_dir.cleanup)

        hist = self._history_summary(path)
        expected_sorted = sorted(byte_counts)
        self.assertEqual(expected_sorted, hist["request_payload_bytes"])
        expected_p50 = self._nearest_rank_percentile(expected_sorted, 50)
        expected_p95 = self._nearest_rank_percentile(expected_sorted, 95)
        self.assertEqual(expected_p50, hist["request_payload_bytes_p50"])
        self.assertEqual(expected_p95, hist["request_payload_bytes_p95"])
        # Absolute non-trivial values (not just >= 0).
        self.assertGreater(expected_p50, 0)
        self.assertGreaterEqual(expected_p95, expected_p50)
        self.assertEqual(expected_sorted, sorted(byte_counts))

    def test_slice5_citation_issue_families_are_distinct_and_exact(self):
        """
        Detailed events: 10, 12 (first_detailed=10, last=12).
        Compacted summary range: [1,3].
        Families:
          stale: positive id < first_detailed and outside summary ranges (e.g. 5)
          compacted_away: id inside summary range (e.g. 2)
          newer: id > last_event_id (e.g. 99)
          unknown: positive id in detailed range but absent from events (e.g. 11)
          duplicate / nonpositive / missing
        """
        base = self._compacted_request(500)
        base_str = json.dumps(base, separators=(",", ":"), ensure_ascii=False)

        cases = [
            (501, [5], "stale_history_event_id"),
            (502, [2], "compacted_away_history_event_id"),
            (503, [99], "newer_history_event_id"),
            (504, [11], "unknown_history_event_id"),
            (505, [10, 10], "duplicate_history_event_id"),
            (506, [0], "invalid_history_event_id"),
            (507, [-3], "invalid_history_event_id"),
            (508, None, "missing_history_event_ids_used"),  # field absent
        ]
        events = []
        for seq, citations, _kind in cases:
            entry = {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": seq,
                "request_json": base_str,
                "opponent_action_assessment": "Opponent prior turn compacted.",
            }
            if citations is not None:
                entry["history_event_ids_used"] = citations
            events.append(entry)

        temp_dir, path = self.write_log(events)
        self.addCleanup(temp_dir.cleanup)
        hist = self._history_summary(path)
        by_seq = {}
        for issue in hist["citation_issues"]:
            by_seq.setdefault(issue["request_run_effect_seq"], set()).add(issue["kind"])

        self.assertIn("stale_history_event_id", by_seq[501])
        self.assertIn("compacted_away_history_event_id", by_seq[502])
        self.assertIn("newer_history_event_id", by_seq[503])
        self.assertIn("unknown_history_event_id", by_seq[504])
        self.assertIn("duplicate_history_event_id", by_seq[505])
        self.assertIn("invalid_history_event_id", by_seq[506])
        self.assertIn("invalid_history_event_id", by_seq[507])
        self.assertIn("missing_history_event_ids_used", by_seq[508])
        # Families must remain distinct — compacted_away is not unknown/stale.
        self.assertNotIn("unknown_history_event_id", by_seq[502])
        self.assertNotIn("stale_history_event_id", by_seq[502])
        self.assertNotIn("compacted_away_history_event_id", by_seq[501])

    def test_slice5_assessment_required_for_detailed_and_compacted_opponent_history(self):
        # Detailed opponent events.
        detailed = self._history_request(
            600,
            events=[
                {
                    "event_id": 1,
                    "actor_player": 0,
                    "kind": "normal_summon",
                    "evidence": "accepted_command",
                    "card_name": "Opp Public",
                }
            ],
            first_detailed=1,
            last_event_id=1,
        )
        # Opponent only in compacted summary.
        compacted = self._compacted_request(601)

        events = [
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 600,
                "request_json": json.dumps(detailed, separators=(",", ":")),
                "opponent_action_assessment": "",
                "history_event_ids_used": [],
            },
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 601,
                "request_json": json.dumps(compacted, separators=(",", ":")),
                "opponent_action_assessment": "   ",
                "history_event_ids_used": [],
            },
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 602,
                "request_json": json.dumps(
                    self._history_request(
                        602,
                        events=[
                            {
                                "event_id": 1,
                                "actor_player": 1,
                                "kind": "normal_summon",
                                "evidence": "accepted_command",
                            }
                        ],
                        first_detailed=1,
                        last_event_id=1,
                    ),
                    separators=(",", ":"),
                ),
                "opponent_action_assessment": "",
                "history_event_ids_used": [],
            },
        ]
        temp_dir, path = self.write_log(events)
        self.addCleanup(temp_dir.cleanup)
        hist = self._history_summary(path)
        by_seq = {
            issue["request_run_effect_seq"]: issue["kind"]
            for issue in hist["assessment_issues"]
        }
        self.assertEqual("missing_opponent_action_assessment", by_seq[600])
        self.assertEqual("missing_opponent_action_assessment", by_seq[601])
        # Own-only history: blank assessment is not an issue.
        self.assertNotIn(602, by_seq)

    def test_slice5_event_id_analysis_scoped_by_duel_generation(self):
        """
        After lifecycle reset, event ids restart at 1. Without generation scoping,
        gen2 id=1 would look like a duplicate of gen1 id=1. Analyzer must require
        explicit duel_generation and not treat cross-generation reuse as duplicate.
        """
        events = [
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 1,
                "event_id": 1,
                "evidence": "accepted_command",
                "public_event_kind": "normal_summon",
            },
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 1,
                "event_id": 2,
                "evidence": "accepted_command",
                "public_event_kind": "activate_effect",
            },
            # Lifecycle reset — new duel generation, ids restart.
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 2,
                "event_id": 1,
                "evidence": "accepted_command",
                "public_event_kind": "normal_summon",
            },
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 2,
                "event_id": 1,
                "evidence": "accepted_command",
                "public_event_kind": "normal_summon",
            },
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 2,
                "event_id": 4,
                "evidence": "public_state_delta",
                "public_event_kind": "card_moved",
            },
        ]
        temp_dir, path = self.write_log(events)
        self.addCleanup(temp_dir.cleanup)
        hist = self._history_summary(path)

        # Cross-generation id reuse is NOT a duplicate.
        self.assertEqual(
            [{"duel_generation": 2, "event_id": 1}],
            hist["event_id_duplicates"],
        )
        # Within-generation gap only (gen2: 1 -> 4).
        self.assertEqual(
            [{"duel_generation": 2, "after": 1, "before": 4}],
            hist["event_id_gaps"],
        )
        self.assertEqual(5, hist["public_event_count"])
        # Kind/actor totals aggregate across generations (not generation-scoped).
        self.assertEqual(
            {"normal_summon": 3, "activate_effect": 1, "card_moved": 1},
            hist["public_event_kind_counts"],
        )
        # actor_player may be absent on some fixtures; when present counts are global.
        self.assertIsInstance(hist["actor_player_counts"], dict)

    def test_slice5_public_event_kind_and_actor_counts_are_generation_independent(self):
        """Coverage counters sum across gens; gaps/duplicates stay gen-scoped."""
        events = [
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 1,
                "event_id": 1,
                "actor_player": 0,
                "evidence": "accepted_command",
                "public_event_kind": "normal_summon",
            },
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 1,
                "event_id": 2,
                "actor_player": 1,
                "evidence": "public_state_delta",
                "public_event_kind": "card_moved",
            },
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 2,
                "event_id": 1,
                "actor_player": 0,
                "evidence": "accepted_command",
                "public_event_kind": "normal_summon",
            },
            {
                "kind": "llm_public_duel_event",
                "duel_generation": 2,
                "event_id": 2,
                "actor_player": 0,
                "evidence": "accepted_command",
                "public_event_kind": "activate_effect",
            },
        ]
        temp_dir, path = self.write_log(events)
        self.addCleanup(temp_dir.cleanup)
        hist = self._history_summary(path)
        self.assertEqual(
            {"normal_summon": 2, "card_moved": 1, "activate_effect": 1},
            hist["public_event_kind_counts"],
        )
        self.assertEqual({"0": 3, "1": 1}, hist["actor_player_counts"])
        # No within-gen duplicates/gaps in this fixture.
        self.assertEqual([], hist["event_id_duplicates"])
        self.assertEqual([], hist["event_id_gaps"])

    def test_slice5_schema_v3_legacy_requests_do_not_emit_v4_citation_or_assessment_issues(self):
        """
        Pre-v4 deployment logs lack history_event_ids_used / opponent_action_assessment.
        Enforcing v4 on those records is a false positive.
        """
        v3_obj = {
            "kind": "decision_request",
            "schema_version": 3,
            "run_effect_seq": 10,
            "controlled_player": 1,
            "legal_actions": [{"action_id": 0, "kind": "command", "command": "Summon"}],
            # No duel_history — pre-schema-v4.
        }
        v3_str = json.dumps(v3_obj, separators=(",", ":"))
        v4_obj = self._compacted_request(20)
        v4_str = json.dumps(v4_obj, separators=(",", ":"), ensure_ascii=False)

        events = [
            # schema 3 as object form — missing v4 fields must NOT be issues
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 10,
                "request_json": v3_obj,
                "action_id": 0,
                "reason": "legacy summon",
            },
            # schema 3 as runtime string form
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 11,
                "request_json": v3_str,
                "action_id": 0,
                "reason": "legacy summon string",
            },
            # schema 4 still enforced (string form)
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 20,
                "request_json": v4_str,
                "opponent_action_assessment": "   ",
                "history_event_ids_used": [1],  # compacted-away
            },
            # schema 4 still enforced (object form) — missing citations
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 21,
                "request_json": v4_obj,
                "opponent_action_assessment": "ok",
                # history_event_ids_used absent
            },
            # unparseable request_json — not a schema4 history violation
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 99,
                "request_json": "not-json{{{",
                "reason": "broken payload",
            },
        ]
        temp_dir, path = self.write_log(events)
        self.addCleanup(temp_dir.cleanup)
        hist = self._history_summary(path)

        # Explicit schema metadata — skipped enforcement must be visible.
        self.assertIn("request_schema_counts", hist)
        self.assertEqual(2, hist["request_schema_counts"].get("3", 0))
        self.assertEqual(2, hist["request_schema_counts"].get("4", 0))
        self.assertEqual(2, hist["legacy_schema3_request_count"])

        citation_seqs = {
            issue["request_run_effect_seq"] for issue in hist["citation_issues"]
        }
        assessment_seqs = {
            issue["request_run_effect_seq"] for issue in hist["assessment_issues"]
        }
        # Legacy schema 3 must not appear.
        self.assertNotIn(10, citation_seqs)
        self.assertNotIn(11, citation_seqs)
        self.assertNotIn(10, assessment_seqs)
        self.assertNotIn(11, assessment_seqs)
        # Schema 4 still enforced.
        self.assertIn(20, citation_seqs)
        self.assertIn(21, citation_seqs)
        self.assertIn(20, assessment_seqs)
        # Unparseable is not a missing_history_event_ids_used false positive.
        self.assertNotIn(99, citation_seqs)
        self.assertNotIn(99, assessment_seqs)
        self.assertIn("unparseable_request_count", hist)
        self.assertEqual(1, hist["unparseable_request_count"])

    def test_slice5_hidden_leaks_cover_all_sentinel_classes_with_path_metadata(self):
        leaky_request = self._history_request(
            700,
            events=[
                {
                    "event_id": 1,
                    "kind": "set_monster",
                    "actor_player": 0,
                    "card_name": self.SENTINEL_SET,
                    "card_id": 99502991,
                }
            ],
            first_detailed=1,
            last_event_id=1,
            extra_fields={
                "opponent_hand_probe": self.SENTINEL_HAND,
                "deck_probe": self.SENTINEL_DECK,
                "extra_probe": self.SENTINEL_EXTRA,
            },
        )
        request_str = json.dumps(leaky_request, separators=(",", ":"), ensure_ascii=False)
        window = {
            "kind": "decision_window",
            "run_effect_seq": 701,
            "schema_version": 4,
            "duel_generation": 1,
            "duel_history": {
                "history_version": 1,
                "duel_generation": 1,
                "last_event_id": 1,
                "first_detailed_event_id": 1,
                "history_compacted": False,
                "events": [
                    {
                        "event_id": 1,
                        "kind": "set_monster",
                        "card_name": self.SENTINEL_SET,
                        "actor_player": 0,
                    }
                ],
                "prior_turn_summaries": [],
                "revealed_card_context": [
                    {"name": self.SENTINEL_DECK},
                    {"name": self.SENTINEL_EXTRA},
                ],
            },
            "legal_actions": [],
            "hand_note": self.SENTINEL_HAND,
        }
        events = [
            window,
            {
                "kind": "llm_broker_response",
                "success": True,
                "request_run_effect_seq": 700,
                "request_json": request_str,
                "opponent_action_assessment": "Leak fixture.",
                "history_event_ids_used": [],
            },
        ]
        temp_dir, path = self.write_log(events)
        self.addCleanup(temp_dir.cleanup)
        hist = self._history_summary(path)
        leaks = hist["hidden_info_leaks"]
        by_token = {leak["token"]: leak for leak in leaks}
        for token in (
            self.SENTINEL_HAND,
            self.SENTINEL_SET,
            self.SENTINEL_DECK,
            self.SENTINEL_EXTRA,
        ):
            self.assertIn(token, by_token, leaks)
            leak = by_token[token]
            self.assertIn(leak["sink"], ("request_json", "decision_window"), leak)
            self.assertTrue(
                isinstance(leak.get("path"), str) and leak["path"],
                "leak must include JSON path/location: %s" % leak,
            )

        # Both sinks appear.
        sinks = {leak["sink"] for leak in leaks}
        self.assertIn("request_json", sinks)
        self.assertIn("decision_window", sinks)


if __name__ == "__main__":
    unittest.main()

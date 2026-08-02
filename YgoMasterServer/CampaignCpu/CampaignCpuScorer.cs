using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Deterministic pure scorer. Consumes CampaignCpuObservation only.
    /// </summary>
    static class CampaignCpuScorer
    {
        public static CampaignCpuDecision Decide(
            CampaignCpuObservation observation,
            CampaignCpuRulePack pack)
        {
            return Decide(observation, pack, predicateEvalFailed: false, appliedDecisionCount: 0);
        }

        public static CampaignCpuDecision Decide(
            CampaignCpuObservation observation,
            CampaignCpuRulePack pack,
            bool predicateEvalFailed)
        {
            return Decide(observation, pack, predicateEvalFailed, appliedDecisionCount: 0);
        }

        public static CampaignCpuDecision Decide(
            CampaignCpuObservation observation,
            CampaignCpuRulePack pack,
            bool predicateEvalFailed,
            int appliedDecisionCount)
        {
            if (observation == null)
            {
                return CampaignCpuDecision.Native("null_observation");
            }
            if (pack == null)
            {
                return CampaignCpuDecision.Native("null_pack");
            }

            if (IsDecisionCapReached(pack.Policy, appliedDecisionCount))
            {
                return CampaignCpuDecision.Native("max_decisions_per_duel");
            }

            if (observation.PredicateQueryFailed)
            {
                predicateEvalFailed = true;
            }

            List<CampaignCpuLegalAction> legal = observation.LegalActions;
            if (legal == null || legal.Count == 0)
            {
                return CampaignCpuDecision.Native("zero_legal");
            }

            // Multi-select lists always native in v1.
            if (observation.IsMultiSelectList)
            {
                return CampaignCpuDecision.Native("multi_select");
            }

            // Apply never-rules first, then deterministic position safety.
            List<CampaignCpuLegalAction> filtered = new List<CampaignCpuLegalAction>(legal.Count);
            for (int i = 0; i < legal.Count; i++)
            {
                CampaignCpuLegalAction action = legal[i];
                if (action == null)
                {
                    continue;
                }
                bool banned = false;
                if (pack.Never != null)
                {
                    for (int n = 0; n < pack.Never.Count; n++)
                    {
                        CampaignCpuNeverRule never = pack.Never[n];
                        if (never != null
                            && CampaignCpuMatchers.MatchesAction(never.Match, action))
                        {
                            banned = true;
                            break;
                        }
                    }
                }
                if (!banned)
                {
                    filtered.Add(action);
                }
            }

            bool neverExhausted = false;
            if (filtered.Count == 0)
            {
                neverExhausted = true;
                filtered = new List<CampaignCpuLegalAction>(legal.Count);
                for (int i = 0; i < legal.Count; i++)
                {
                    if (legal[i] != null)
                    {
                        filtered.Add(legal[i]);
                    }
                }
            }

            CampaignCpuPositionSafetyResult positionSafety;
            if (pack.Policy != null && pack.Policy.PositionSafetyEnabled)
            {
                positionSafety =
                    CampaignCpuPositionSafety.FilterDominatedPositionChanges(
                        observation,
                        filtered,
                        pack.Policy.PositionSafetyExceptionCardIds);
            }
            else
            {
                positionSafety = new CampaignCpuPositionSafetyResult
                {
                    FilteredActions = filtered,
                    RemovedActionIdentities = new List<string>(),
                    Reason = "policy_disabled",
                };
            }
            filtered = positionSafety.FilteredActions;
            if (filtered.Count == 0)
            {
                return AttachTactical(
                    CampaignCpuDecision.Native(
                        positionSafety.RemovedAnyAction
                            ? positionSafety.Reason
                            : "zero_legal_after_position_safety"),
                    positionSafety,
                    filtered);
            }

            // Priority rules: sort by priority DESC, list_index ASC
            if (pack.Priority != null && pack.Priority.Count > 0)
            {
                List<CampaignCpuPriorityRule> ordered =
                    new List<CampaignCpuPriorityRule>(pack.Priority);
                ordered.Sort(ComparePriorityRules);

                for (int r = 0; r < ordered.Count; r++)
                {
                    CampaignCpuPriorityRule rule = ordered[r];
                    if (rule == null)
                    {
                        continue;
                    }
                    if (!CampaignCpuMatchers.WhenHolds(rule.When, observation, predicateEvalFailed))
                    {
                        continue;
                    }
                    if (rule.Prefer == null || rule.Prefer.Count == 0)
                    {
                        continue;
                    }

                    // Prefer list order is the tie-break among matches
                    for (int p = 0; p < rule.Prefer.Count; p++)
                    {
                        CampaignCpuMatchSpec prefer = rule.Prefer[p];
                        CampaignCpuLegalAction best = null;
                        for (int a = 0; a < filtered.Count; a++)
                        {
                            CampaignCpuLegalAction candidate = filtered[a];
                            if (!CampaignCpuMatchers.MatchesAction(prefer, candidate))
                            {
                                continue;
                            }
                            if (best == null || candidate.ActionId < best.ActionId)
                            {
                                best = candidate;
                            }
                        }
                        if (best != null)
                        {
                            CampaignCpuDecision decision = CampaignCpuDecision.Commit(
                                CampaignCpuRoute.RuleCommit,
                                best,
                                neverExhausted ? "priority_after_never_exhausted" : "priority",
                                rule.Id,
                                rule.ScoreBonus,
                                matched: true);
                            return AttachTactical(decision, positionSafety, filtered);
                        }
                    }
                }
            }

            // Fallback scoring
            int[] scores = new int[filtered.Count];
            string[] hitRules = new string[filtered.Count];
            bool anyNonZero = false;
            bool anyPositive = false;
            if (pack.FallbackScoring != null)
            {
                for (int a = 0; a < filtered.Count; a++)
                {
                    CampaignCpuLegalAction action = filtered[a];
                    int score = 0;
                    string lastRule = null;
                    for (int f = 0; f < pack.FallbackScoring.Count; f++)
                    {
                        CampaignCpuFallbackRule fr = pack.FallbackScoring[f];
                        if (fr == null)
                        {
                            continue;
                        }
                        if (CampaignCpuMatchers.MatchesAction(fr.Match, action))
                        {
                            score += fr.Score;
                            lastRule = fr.Id;
                        }
                    }
                    scores[a] = score;
                    hitRules[a] = lastRule;
                    if (score != 0)
                    {
                        anyNonZero = true;
                    }
                    if (score > 0)
                    {
                        anyPositive = true;
                    }
                }
            }

            string onNoMatch = pack.Policy != null ? pack.Policy.OnNoMatch : "native_cpu";
            if (!anyPositive
                && pack.Policy != null
                && pack.Policy.PositionSafetyEnabled
                && string.Equals(
                    pack.Policy.PhaseExitPolicy,
                    "battle_then_end",
                    StringComparison.OrdinalIgnoreCase))
            {
                CampaignCpuLegalAction phaseAction = null;
                string phaseRule = null;
                if (CampaignCpuPositionSafety.HasKnownFaceUpAttacker(observation))
                {
                    phaseAction = FindPhaseAction(filtered, DuelPhase.Battle);
                    phaseRule = "phase_exit_battle";
                }
                if (phaseAction == null)
                {
                    phaseAction = FindPhaseAction(filtered, DuelPhase.End);
                    phaseRule = "phase_exit_end";
                }
                if (phaseAction != null)
                {
                    return AttachTactical(
                        CampaignCpuDecision.Commit(
                            CampaignCpuRoute.RuleCommit,
                            phaseAction,
                            "explicit_phase_exit",
                            phaseRule,
                            0,
                            matched: true),
                        positionSafety,
                        filtered);
                }
            }
            if (string.Equals(onNoMatch, "native_cpu", StringComparison.OrdinalIgnoreCase)
                && !anyNonZero)
            {
                return AttachTactical(
                    CampaignCpuDecision.Native("no_match"),
                    positionSafety,
                    filtered);
            }

            int bestIdx = 0;
            for (int a = 1; a < filtered.Count; a++)
            {
                if (scores[a] > scores[bestIdx])
                {
                    bestIdx = a;
                }
                else if (scores[a] == scores[bestIdx]
                    && filtered[a].ActionId < filtered[bestIdx].ActionId)
                {
                    bestIdx = a;
                }
            }

            CampaignCpuLegalAction selected = filtered[bestIdx];
            CampaignCpuLegalAction openingSet;
            if (CampaignCpuOpeningSetSafety.TryFindReplacement(
                observation,
                pack.Policy,
                filtered,
                selected,
                out openingSet))
            {
                CampaignCpuDecision openingDecision =
                    CampaignCpuDecision.Commit(
                        CampaignCpuRoute.RuleCommit,
                        openingSet,
                        "opening_position_safety",
                        "opening_defensive_set",
                        0,
                        matched: true);
                openingDecision.ReplacedActionIdentity =
                    selected.CanonicalIdentity;
                return AttachTactical(
                    openingDecision,
                    positionSafety,
                    filtered);
            }

            return AttachTactical(
                CampaignCpuDecision.Commit(
                    CampaignCpuRoute.RuleCommit,
                    selected,
                    neverExhausted ? "fallback_after_never_exhausted" : "fallback",
                    hitRules[bestIdx] ?? "fallback",
                    scores[bestIdx],
                    matched: anyNonZero),
                positionSafety,
                filtered);
        }

        /// <summary>
        /// max_decisions_per_duel &lt;= 0 means unlimited. When positive and already applied
        /// count is at/above the cap, scripted commits fail closed to native.
        /// </summary>
        public static bool IsDecisionCapReached(CampaignCpuPackPolicy policy, int appliedDecisionCount)
        {
            if (policy == null || policy.MaxDecisionsPerDuel <= 0)
            {
                return false;
            }
            return appliedDecisionCount >= policy.MaxDecisionsPerDuel;
        }

        static int ComparePriorityRules(CampaignCpuPriorityRule a, CampaignCpuPriorityRule b)
        {
            if (a == null && b == null)
            {
                return 0;
            }
            if (a == null)
            {
                return 1;
            }
            if (b == null)
            {
                return -1;
            }
            // priority DESC
            int cmp = b.Priority.CompareTo(a.Priority);
            if (cmp != 0)
            {
                return cmp;
            }
            // list_index ASC
            return a.ListIndex.CompareTo(b.ListIndex);
        }

        static CampaignCpuLegalAction FindPhaseAction(
            IList<CampaignCpuLegalAction> actions,
            DuelPhase phase)
        {
            CampaignCpuLegalAction best = null;
            if (actions == null)
            {
                return null;
            }
            for (int i = 0; i < actions.Count; i++)
            {
                CampaignCpuLegalAction action = actions[i];
                if (action == null
                    || action.Kind != LegalActionKind.MovePhase
                    || action.Phase != phase)
                {
                    continue;
                }
                if (best == null || action.ActionId < best.ActionId)
                {
                    best = action;
                }
            }
            return best;
        }

        static CampaignCpuDecision AttachTactical(
            CampaignCpuDecision decision,
            CampaignCpuPositionSafetyResult positionSafety,
            IList<CampaignCpuLegalAction> filtered)
        {
            if (decision == null)
            {
                return null;
            }
            decision.TacticalFilterReason = positionSafety != null
                ? positionSafety.Reason
                : "not_evaluated";
            decision.TacticalFilteredActionIdentities =
                positionSafety != null
                    ? positionSafety.RemovedActionIdentities
                    : new List<string>();
            decision.LegalAfterTacticalFingerprint =
                CampaignCpuObservation.FingerprintLegalActions(filtered);
            return decision;
        }
    }
}

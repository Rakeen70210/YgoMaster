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
            return Decide(observation, pack, predicateEvalFailed: false);
        }

        public static CampaignCpuDecision Decide(
            CampaignCpuObservation observation,
            CampaignCpuRulePack pack,
            bool predicateEvalFailed)
        {
            if (observation == null)
            {
                return CampaignCpuDecision.Native("null_observation");
            }
            if (pack == null)
            {
                return CampaignCpuDecision.Native("null_pack");
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

            // Apply never-rules
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
                            return decision;
                        }
                    }
                }
            }

            // Fallback scoring
            int[] scores = new int[filtered.Count];
            string[] hitRules = new string[filtered.Count];
            bool anyNonZero = false;
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
                }
            }

            string onNoMatch = pack.Policy != null ? pack.Policy.OnNoMatch : "native_cpu";
            if (string.Equals(onNoMatch, "native_cpu", StringComparison.OrdinalIgnoreCase)
                && !anyNonZero)
            {
                return CampaignCpuDecision.Native("no_match");
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

            return CampaignCpuDecision.Commit(
                CampaignCpuRoute.RuleCommit,
                filtered[bestIdx],
                neverExhausted ? "fallback_after_never_exhausted" : "fallback",
                hitRules[bestIdx] ?? "fallback",
                scores[bestIdx],
                matched: anyNonZero);
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
    }
}

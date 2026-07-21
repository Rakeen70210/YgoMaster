using System;
using System.Collections.Generic;

namespace YgoMaster
{
    static class CampaignCpuMatchers
    {
        public static bool MatchesAction(CampaignCpuMatchSpec spec, CampaignCpuLegalAction action)
        {
            if (spec == null || action == null)
            {
                return false;
            }
            if (spec.HasKind && action.Kind != spec.Kind)
            {
                return false;
            }
            if (spec.HasCommand && action.Command != spec.Command)
            {
                return false;
            }
            if (spec.HasPhase && action.Phase != spec.Phase)
            {
                return false;
            }
            if (spec.HasCardId && action.CardId != spec.CardId)
            {
                return false;
            }
            if (spec.HasPosition && !spec.PositionWildcard && action.Position != spec.Position)
            {
                return false;
            }
            if (spec.HasIsMechanical && action.IsMechanical != spec.IsMechanical)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(spec.TargetScope))
            {
                if (string.IsNullOrEmpty(action.TargetScope)
                    || action.TargetScope.IndexOf(spec.TargetScope, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }
            if (!string.IsNullOrEmpty(spec.ActionLabelContains))
            {
                if (string.IsNullOrEmpty(action.Label)
                    || action.Label.IndexOf(
                        spec.ActionLabelContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Fail-closed: if self_has_card_id cannot be evaluated, treat when as false.
        /// Caller should set predicateEvalFailed when observation hand list is unusable.
        /// </summary>
        public static bool WhenHolds(
            CampaignCpuWhenSpec when,
            CampaignCpuObservation observation,
            bool predicateEvalFailed)
        {
            if (when == null)
            {
                return true;
            }
            if (predicateEvalFailed
                && when.SelfHasCardId != null
                && when.SelfHasCardId.Count > 0)
            {
                return false;
            }
            if (observation == null)
            {
                return false;
            }
            if (when.PhaseIn != null && when.PhaseIn.Count > 0)
            {
                bool phaseOk = false;
                for (int i = 0; i < when.PhaseIn.Count; i++)
                {
                    if ((int)when.PhaseIn[i] == observation.Phase)
                    {
                        phaseOk = true;
                        break;
                    }
                }
                if (!phaseOk)
                {
                    return false;
                }
            }
            if (when.TurnLte.HasValue && observation.Turn > when.TurnLte.Value)
            {
                return false;
            }
            if (when.TurnGte.HasValue && observation.Turn < when.TurnGte.Value)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(when.WindowClass)
                && !string.Equals(
                    when.WindowClass,
                    observation.WindowClass,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (when.SelfHasCardId != null && when.SelfHasCardId.Count > 0)
            {
                if (!ContainsAny(observation.SelfHandCardIds, when.SelfHasCardId)
                    && !ContainsAny(observation.SelfFieldFaceUpCardIds, when.SelfHasCardId))
                {
                    return false;
                }
            }
            if (when.SelfHasFieldCardId != null && when.SelfHasFieldCardId.Count > 0)
            {
                if (!ContainsAny(observation.SelfFieldFaceUpCardIds, when.SelfHasFieldCardId))
                {
                    return false;
                }
            }
            return true;
        }

        static bool ContainsAny(IList<int> haystack, IList<int> needles)
        {
            if (haystack == null || needles == null)
            {
                return false;
            }
            for (int n = 0; n < needles.Count; n++)
            {
                int needle = needles[n];
                for (int h = 0; h < haystack.Count; h++)
                {
                    if (haystack[h] == needle)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}

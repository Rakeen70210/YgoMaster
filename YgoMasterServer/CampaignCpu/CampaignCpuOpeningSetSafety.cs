using System.Collections.Generic;

namespace YgoMaster
{
    interface ICampaignCpuCardBasicStatsQuery
    {
        bool TryReadCardBasicStats(
            int player,
            int position,
            int index,
            out int level,
            out int atk,
            out int def);
    }

    /// <summary>
    /// Duel-thread projection of optional BasicVal onto legal normal Summon/Set actions.
    /// Per-action failures remain unknown and preserve legacy scorer behavior.
    /// </summary>
    static class CampaignCpuLegalActionBasicStatsBuilder
    {
        public static void Populate(
            ICampaignCpuCardBasicStatsQuery query,
            CampaignCpuObservation observation)
        {
            if (query == null
                || observation == null
                || observation.LegalActions == null)
            {
                return;
            }

            for (int i = 0; i < observation.LegalActions.Count; i++)
            {
                CampaignCpuLegalAction action = observation.LegalActions[i];
                if (!CampaignCpuSummonSafety.IsNormalSummonOrSet(action))
                {
                    continue;
                }

                int level;
                int atk;
                int def;
                bool read;
                try
                {
                    read = query.TryReadCardBasicStats(
                        action.Player,
                        action.Position,
                        action.Index,
                        out level,
                        out atk,
                        out def);
                }
                catch
                {
                    read = false;
                    level = atk = def = 0;
                }
                if (!read)
                {
                    continue;
                }

                action.BasicLevelKnown = level > 0;
                action.BasicLevel = level;
                action.BasicAtkKnown = atk >= 0;
                action.BasicAtk = atk;
                action.BasicDefKnown = def >= 0;
                action.BasicDef = def;
            }
        }
    }

    /// <summary>
    /// Narrow opening fallback override. Explicit priority rules return before this
    /// helper is consulted, and unknown/effect-sensitive cards are opt-in by pack id.
    /// </summary>
    static class CampaignCpuOpeningSetSafety
    {
        public static bool TryFindReplacement(
            CampaignCpuObservation observation,
            CampaignCpuPackPolicy policy,
            IList<CampaignCpuLegalAction> legalActions,
            CampaignCpuLegalAction selected,
            out CampaignCpuLegalAction replacement)
        {
            replacement = null;
            if (observation == null
                || policy == null
                || !policy.OpeningSetSafetyEnabled
                || selected == null
                || selected.Kind != LegalActionKind.Command
                || selected.Command != DuelCommandType.Summon
                || selected.Player != observation.OwnedSeat
                || observation.ActingPlayer != observation.OwnedSeat
                || observation.TurnPlayer != observation.OwnedSeat
                || !observation.IsMainPhaseWaitInput
                || !string.Equals(
                    observation.WindowClass,
                    "WaitInput_MainPhase",
                    System.StringComparison.OrdinalIgnoreCase)
                || observation.Turn != 0
                || observation.SelfMonsterCount != 0
                || observation.OppMonsterCount != 0
                || !Contains(policy.OpeningSetSafetyCardIds, selected.CardId)
                || !selected.BasicLevelKnown
                || selected.BasicLevel < 1
                || selected.BasicLevel > 4
                || !selected.BasicAtkKnown
                || !selected.BasicDefKnown
                || selected.BasicDef <= selected.BasicAtk
                || legalActions == null)
            {
                return false;
            }

            for (int i = 0; i < legalActions.Count; i++)
            {
                CampaignCpuLegalAction action = legalActions[i];
                if (action != null
                    && action.Kind == LegalActionKind.MovePhase
                    && action.Phase == DuelPhase.Battle)
                {
                    return false;
                }
            }

            for (int i = 0; i < legalActions.Count; i++)
            {
                CampaignCpuLegalAction action = legalActions[i];
                if (action == null
                    || action.Kind != LegalActionKind.Command
                    || action.Command != DuelCommandType.SetMonst
                    || action.CardId != selected.CardId
                    || action.Player != selected.Player
                    || action.Position != selected.Position
                    || action.Index != selected.Index)
                {
                    continue;
                }
                replacement = action;
                return true;
            }
            return false;
        }

        static bool Contains(IList<int> values, int value)
        {
            if (values == null)
            {
                return false;
            }
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

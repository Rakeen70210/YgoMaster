using System;
using System.Collections.Generic;

namespace YgoMaster
{
    sealed class CampaignCpuPositionSafetyResult
    {
        public bool HasKnownTacticalState;
        public bool RemovedAnyAction;
        public string Reason;
        public List<CampaignCpuLegalAction> FilteredActions;
        public List<string> RemovedActionIdentities;
    }

    static class CampaignCpuPositionSafety
    {
        public static CampaignCpuPositionSafetyResult FilterDominatedPositionChanges(
            CampaignCpuObservation observation,
            IList<CampaignCpuLegalAction> legalActions)
        {
            return FilterDominatedPositionChanges(
                observation,
                legalActions,
                new List<int>());
        }

        public static CampaignCpuPositionSafetyResult FilterDominatedPositionChanges(
            CampaignCpuObservation observation,
            IList<CampaignCpuLegalAction> legalActions,
            IList<int> exceptionCardIds)
        {
            var result = new CampaignCpuPositionSafetyResult
            {
                FilteredActions = new List<CampaignCpuLegalAction>(),
                RemovedActionIdentities = new List<string>(),
                Reason = "unknown_tactical_state",
            };

            if (legalActions == null || legalActions.Count == 0)
            {
                result.Reason = "empty";
                return result;
            }

            int opposingMaxAtk;
            bool opposingThreatKnown = TryGetOpposingMaxFaceUpAtk(
                observation,
                out opposingMaxAtk);

            for (int i = 0; i < legalActions.Count; i++)
            {
                CampaignCpuLegalAction action = legalActions[i];
                if (action == null)
                {
                    continue;
                }

                string dominatedReason;
                bool evaluated;
                bool dominated = IsDominatedPositionChange(
                    observation,
                    action,
                    opposingThreatKnown,
                    opposingMaxAtk,
                    exceptionCardIds,
                    out evaluated,
                    out dominatedReason);
                result.HasKnownTacticalState |= evaluated;
                if (dominated)
                {
                    result.RemovedAnyAction = true;
                    result.RemovedActionIdentities.Add(action.CanonicalIdentity);
                    if (string.IsNullOrEmpty(result.Reason)
                        || string.Equals(
                            result.Reason,
                            "unknown_tactical_state",
                            StringComparison.Ordinal))
                    {
                        result.Reason = dominatedReason;
                    }
                    continue;
                }
                result.FilteredActions.Add(action);
            }

            if (!result.RemovedAnyAction)
            {
                result.Reason = result.HasKnownTacticalState
                    ? "not_dominated"
                    : "unknown_tactical_state";
            }
            return result;
        }

        public static bool HasKnownFaceUpAttacker(CampaignCpuObservation observation)
        {
            if (observation == null || observation.TacticalMonsters == null)
            {
                return false;
            }
            for (int i = 0; i < observation.TacticalMonsters.Count; i++)
            {
                CampaignCpuMonsterTacticalState monster =
                    observation.TacticalMonsters[i];
                if (monster != null
                    && monster.Player == observation.OwnedSeat
                    && monster.FaceKnown
                    && monster.FaceUp
                    && monster.TurnKnown
                    && monster.IsAttack
                    && monster.HasAtk
                    && monster.Atk > 0)
                {
                    return true;
                }
            }
            return false;
        }

        static bool IsDominatedPositionChange(
            CampaignCpuObservation observation,
            CampaignCpuLegalAction action,
            bool opposingThreatKnown,
            int opposingMaxFaceUpAtk,
            IList<int> exceptionCardIds,
            out bool evaluated,
            out string reason)
        {
            evaluated = false;
            reason = null;
            if (action.Command != DuelCommandType.TurnDef
                && action.Command != DuelCommandType.TurnAtk)
            {
                return false;
            }
            if (Contains(exceptionCardIds, action.CardId))
            {
                return false;
            }
            if (!opposingThreatKnown)
            {
                return false;
            }

            CampaignCpuMonsterTacticalState own = FindOwnMonster(
                observation,
                action);
            if (own == null
                || !own.FaceKnown
                || !own.FaceUp
                || !own.TurnKnown
                || !own.HasAtk
                || !own.HasDef)
            {
                return false;
            }

            evaluated = true;
            if (action.Command == DuelCommandType.TurnDef)
            {
                reason = "dominated_turn_defense";
                return own.IsAttack
                    && own.Atk >= opposingMaxFaceUpAtk
                    && own.Def < opposingMaxFaceUpAtk;
            }

            reason = "dominated_turn_attack";
            return own.IsDefense
                && own.Def >= opposingMaxFaceUpAtk
                && own.Atk < opposingMaxFaceUpAtk;
        }

        static CampaignCpuMonsterTacticalState FindOwnMonster(
            CampaignCpuObservation observation,
            CampaignCpuLegalAction action)
        {
            if (observation == null || observation.TacticalMonsters == null)
            {
                return null;
            }
            for (int i = 0; i < observation.TacticalMonsters.Count; i++)
            {
                CampaignCpuMonsterTacticalState monster =
                    observation.TacticalMonsters[i];
                if (monster == null || monster.Player != observation.OwnedSeat)
                {
                    continue;
                }
                if (monster.Position == action.Position
                    && monster.Index == action.Index
                    && (action.CardId <= 0 || monster.CardId == action.CardId))
                {
                    return monster;
                }
            }
            return null;
        }

        static bool TryGetOpposingMaxFaceUpAtk(
            CampaignCpuObservation observation,
            out int maximum)
        {
            maximum = 0;
            bool known = false;
            if (observation == null || observation.TacticalMonsters == null)
            {
                return false;
            }
            for (int i = 0; i < observation.TacticalMonsters.Count; i++)
            {
                CampaignCpuMonsterTacticalState monster =
                    observation.TacticalMonsters[i];
                if (monster == null
                    || monster.Player == observation.OwnedSeat
                    || !monster.FaceKnown
                    || !monster.FaceUp
                    || !monster.HasAtk)
                {
                    continue;
                }
                if (!known || monster.Atk > maximum)
                {
                    maximum = monster.Atk;
                }
                known = true;
            }
            return known;
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

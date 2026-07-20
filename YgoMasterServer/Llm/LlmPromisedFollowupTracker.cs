using System;
using System.Collections.Generic;

namespace YgoMaster
{
    sealed class LlmPromisedFollowupEvaluation
    {
        public string EventKind { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public ulong OriginRunEffectSeq { get; set; }
        public ulong CurrentRunEffectSeq { get; set; }
        public int DuelGeneration { get; set; }
        public int OriginActionId { get; set; }
        public int OriginCardId { get; set; }
        public string OriginActionLabel { get; set; }
        public string ActionFamily { get; set; }
        public int ExpectedCardId { get; set; }
        public string ExpectedCardName { get; set; }
        public string Description { get; set; }
        public int ActionId { get; set; }
        public List<string> CurrentLegalActionFamilies { get; private set; }

        public LlmPromisedFollowupEvaluation()
        {
            ActionId = -1;
            CurrentLegalActionFamilies = new List<string>();
        }
    }

    sealed class LlmPromisedFollowupTracker
    {
        sealed class Pending
        {
            public ulong OriginRunEffectSeq;
            public int OriginTurn;
            public int DuelGeneration;
            public int OriginActionId;
            public int OriginCardId;
            public string OriginActionLabel;
            public LlmBrokerIntendedFollowup Followup;
        }

        Pending pending;

        public void Reset()
        {
            pending = null;
        }

        public void RecordCommittedAction(
            DecisionSnapshot snapshot,
            LegalAction action,
            LlmBrokerDecisionResponse response,
            int duelGeneration)
        {
            pending = null;
            if (snapshot == null || action == null || response == null
                || action.Kind != LegalActionKind.Command
                || action.Command != DuelCommandType.SummonSp
                || response.ActionId != action.ActionId
                || response.IntendedFollowups == null
                || response.IntendedFollowups.Count == 0)
            {
                return;
            }
            LlmBrokerIntendedFollowup followup = response.IntendedFollowups[0];
            if (followup == null || !IsSupportedFamily(followup.ActionFamily))
            {
                return;
            }
            pending = new Pending()
            {
                OriginRunEffectSeq = snapshot.RunEffectSeq,
                OriginTurn = snapshot.Turn,
                DuelGeneration = duelGeneration,
                OriginActionId = action.ActionId,
                OriginCardId = action.CardId,
                OriginActionLabel = action.ActionLabel,
                Followup = followup,
            };
        }

        public LlmPromisedFollowupEvaluation Evaluate(
            DecisionSnapshot snapshot,
            int duelGeneration)
        {
            if (pending == null || snapshot == null)
            {
                return null;
            }
            if (duelGeneration != pending.DuelGeneration || snapshot.Turn != pending.OriginTurn)
            {
                LlmPromisedFollowupEvaluation expired = Create(snapshot, "unavailable", "expired");
                pending = null;
                return expired;
            }
            if (snapshot.RunEffectSeq <= pending.OriginRunEffectSeq
                || !snapshot.IsStrategicWindow
                || snapshot.ViewType != DuelViewType.WaitInput)
            {
                return null;
            }

            int matchingActionId = FindMatchingAction(snapshot, pending.Followup);
            if (matchingActionId >= 0)
            {
                LlmPromisedFollowupEvaluation matched = Create(snapshot, "matched", null);
                matched.ActionId = matchingActionId;
                pending = null;
                return matched;
            }
            if (!ExpectedCardIsOnField(snapshot, pending))
            {
                return null;
            }

            string reason = pending.Followup.ActionFamily == "attack"
                ? "attack_not_legal"
                : "effect_not_legal";
            LlmPromisedFollowupEvaluation unavailable = Create(snapshot, "unavailable", reason);
            pending = null;
            return unavailable;
        }

        LlmPromisedFollowupEvaluation Create(
            DecisionSnapshot snapshot,
            string status,
            string reason)
        {
            LlmPromisedFollowupEvaluation result = new LlmPromisedFollowupEvaluation()
            {
                EventKind = status == "matched"
                    ? "intended_followup_matched"
                    : "intended_followup_unavailable",
                Status = status,
                Reason = reason,
                OriginRunEffectSeq = pending.OriginRunEffectSeq,
                CurrentRunEffectSeq = snapshot.RunEffectSeq,
                DuelGeneration = pending.DuelGeneration,
                OriginActionId = pending.OriginActionId,
                OriginCardId = pending.OriginCardId,
                OriginActionLabel = pending.OriginActionLabel,
                ActionFamily = pending.Followup.ActionFamily,
                ExpectedCardId = pending.Followup.CardId,
                ExpectedCardName = pending.Followup.CardName,
                Description = pending.Followup.Description,
            };
            foreach (LegalAction action in snapshot.LegalActions)
            {
                string family = ActionFamily(action);
                if (!result.CurrentLegalActionFamilies.Contains(family))
                {
                    result.CurrentLegalActionFamilies.Add(family);
                }
            }
            return result;
        }

        static int FindMatchingAction(
            DecisionSnapshot snapshot,
            LlmBrokerIntendedFollowup followup)
        {
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action == null || action.IsMechanical)
                {
                    continue;
                }
                int cardId = action.CardId > 0
                    ? action.CardId
                    : (action.Card != null ? action.Card.CardId : 0);
                if (followup.CardId > 0 && cardId != followup.CardId)
                {
                    continue;
                }
                if (ActionFamily(action) == followup.ActionFamily)
                {
                    return action.ActionId;
                }
            }
            return -1;
        }

        static bool ExpectedCardIsOnField(DecisionSnapshot snapshot, Pending value)
        {
            if (snapshot.SelfResources == null || snapshot.SelfResources.Field == null)
            {
                return false;
            }
            int expected = value.Followup.CardId > 0
                ? value.Followup.CardId
                : value.OriginCardId;
            foreach (LlmSelfResourceCard card in snapshot.SelfResources.Field)
            {
                if (card != null && card.CardId == expected)
                {
                    return true;
                }
            }
            return false;
        }

        static string ActionFamily(LegalAction action)
        {
            if (action == null)
            {
                return "unknown";
            }
            if (action.Kind == LegalActionKind.MovePhase)
            {
                return "move_phase";
            }
            if (action.Kind == LegalActionKind.Command)
            {
                if (action.Command == DuelCommandType.Action) return "effect_activation";
                if (action.Command == DuelCommandType.Attack) return "attack";
                if (action.Command == DuelCommandType.SummonSp) return "special_summon";
            }
            return action.Kind.ToString().ToLowerInvariant();
        }

        static bool IsSupportedFamily(string family)
        {
            return family == "effect_activation"
                || family == "attack"
                || family == "move_phase";
        }
    }
}

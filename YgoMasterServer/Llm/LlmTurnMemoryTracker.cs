using System.Collections.Generic;

namespace YgoMaster
{
    class LlmTurnMemoryTracker
    {
        readonly int maxRecentActions;
        readonly List<LlmRecentActionMemory> recentActions;
        readonly List<LlmUsedCardMemory> cardsUsedThisTurn;
        int currentTurn;
        bool normalSummonUsed;
        LlmIntendedFollowupMemory intendedFollowup;

        public LlmTurnMemoryTracker(int maxRecentActions)
        {
            this.maxRecentActions = maxRecentActions <= 0 ? 4 : maxRecentActions;
            recentActions = new List<LlmRecentActionMemory>();
            cardsUsedThisTurn = new List<LlmUsedCardMemory>();
            currentTurn = -1;
        }

        public void Reset()
        {
            recentActions.Clear();
            cardsUsedThisTurn.Clear();
            currentTurn = -1;
            normalSummonUsed = false;
            intendedFollowup = null;
        }

        public LlmTurnMemoryState CreateSnapshot(DecisionSnapshot snapshot)
        {
            return CreateSnapshot(snapshot, null);
        }

        /// <summary>
        /// Slice 6B: re-evaluate intended followup against a fresh engine window graph.
        /// </summary>
        public LlmTurnMemoryState CreateSnapshot(DecisionSnapshot snapshot, LlmSearchGraph graph)
        {
            if (snapshot == null)
            {
                return new LlmTurnMemoryState();
            }

            EnsureTurn(snapshot.Turn);
            if (intendedFollowup != null && snapshot.Turn != intendedFollowup.OriginTurn)
            {
                intendedFollowup = null;
            }
            if (intendedFollowup != null && graph != null)
            {
                LlmIntendedFollowupEvaluator.Evaluate(intendedFollowup, snapshot, graph);
            }

            LlmTurnMemoryState memory = new LlmTurnMemoryState()
            {
                NormalSummonUsed = normalSummonUsed,
                PhasePlan = PhasePlanFor(snapshot.CurrentPhase),
                IntendedFollowup = intendedFollowup,
            };
            foreach (LlmRecentActionMemory action in recentActions)
            {
                memory.RecentActions.Add(Clone(action));
            }
            foreach (LlmUsedCardMemory card in cardsUsedThisTurn)
            {
                memory.CardsUsedThisTurn.Add(new LlmUsedCardMemory()
                {
                    CardId = card.CardId,
                    Name = card.Name,
                });
            }
            return memory;
        }

        public void RecordCommittedAction(
            DecisionSnapshot snapshot,
            LlmBrokerDecisionResponse response,
            LegalAction action)
        {
            RecordCommittedAction(snapshot, response, action, null);
        }

        /// <summary>
        /// Slice 6B: optional selected candidate line persists intended followup intent.
        /// Existing 3-arg callers pass null and never invent intent.
        /// </summary>
        public void RecordCommittedAction(
            DecisionSnapshot snapshot,
            LlmBrokerDecisionResponse response,
            LegalAction action,
            LlmSearchLine selectedLine)
        {
            if (snapshot == null || action == null)
            {
                return;
            }

            EnsureTurn(snapshot.Turn);
            LlmRecentActionMemory memory = new LlmRecentActionMemory()
            {
                Turn = snapshot.Turn,
                Phase = snapshot.CurrentPhase,
                ActionType = KindName(action.Kind),
                ActionLabel = ActionLabel(action),
                CardId = action.CardId,
                CardName = action.Card == null ? null : action.Card.Name,
                Reason = response == null ? null : response.Reason,
                Plan = response == null ? null : response.Plan,
            };
            recentActions.Add(memory);
            while (recentActions.Count > maxRecentActions)
            {
                recentActions.RemoveAt(0);
            }

            if (action.CardId > 0 && !HasUsedCard(action.CardId))
            {
                cardsUsedThisTurn.Add(new LlmUsedCardMemory()
                {
                    CardId = action.CardId,
                    Name = action.Card == null ? null : action.Card.Name,
                });
            }

            if (action.Kind == LegalActionKind.Command &&
                action.Command == DuelCommandType.Summon)
            {
                normalSummonUsed = true;
            }

            // Never leave prior intent stale: every tracker commit clears first.
            // 3-arg (selectedLine null) and invalid/mismatched/root-shell 4-arg leave intent absent.
            // Only a valid matching-root non-shell template replaces intent.
            // Mechanical automatic paths that never call the tracker are unchanged.
            intendedFollowup = null;
            if (selectedLine != null
                && !selectedLine.IsRootShell
                && selectedLine.RootActionId == action.ActionId)
            {
                intendedFollowup = LlmIntendedFollowupMemory.FromSelectedLine(snapshot, selectedLine);
            }
        }

        void EnsureTurn(int turn)
        {
            if (currentTurn == turn)
            {
                return;
            }
            currentTurn = turn;
            cardsUsedThisTurn.Clear();
            normalSummonUsed = false;
            // Turn change expires prior intent (CreateSnapshot also clears on OriginTurn mismatch).
            if (intendedFollowup != null && intendedFollowup.OriginTurn != turn)
            {
                intendedFollowup = null;
            }
        }

        bool HasUsedCard(int cardId)
        {
            foreach (LlmUsedCardMemory card in cardsUsedThisTurn)
            {
                if (card.CardId == cardId)
                {
                    return true;
                }
            }
            return false;
        }

        static LlmRecentActionMemory Clone(LlmRecentActionMemory action)
        {
            return new LlmRecentActionMemory()
            {
                Turn = action.Turn,
                Phase = action.Phase,
                ActionType = action.ActionType,
                ActionLabel = action.ActionLabel,
                CardId = action.CardId,
                CardName = action.CardName,
                Reason = action.Reason,
                Plan = action.Plan,
            };
        }

        static string PhasePlanFor(int phase)
        {
            if (phase == (int)DuelPhase.Main1)
            {
                return "develop_board";
            }
            if (phase == (int)DuelPhase.Battle)
            {
                return "attack_or_remove_threats";
            }
            if (phase == (int)DuelPhase.Main2)
            {
                return "preserve_resources_or_pass";
            }
            if (phase == (int)DuelPhase.End)
            {
                return "pass";
            }
            return "respond_defensively";
        }

        static string KindName(LegalActionKind kind)
        {
            switch (kind)
            {
                case LegalActionKind.MovePhase:
                    return "move_phase";
                case LegalActionKind.Command:
                    return "command";
                case LegalActionKind.DialogResult:
                    return "dialog_result";
                case LegalActionKind.ListIndex:
                    return "list_index";
                case LegalActionKind.Cancel:
                    return "cancel";
                default:
                    return kind.ToString();
            }
        }

        static string ActionLabel(LegalAction action)
        {
            if (action.Kind == LegalActionKind.MovePhase)
            {
                return "Move to " + action.Phase;
            }
            if (action.Kind == LegalActionKind.Command)
            {
                string label = action.Command.ToString();
                if (action.Card != null && !string.IsNullOrEmpty(action.Card.Name))
                {
                    label += " " + action.Card.Name;
                }
                return label;
            }
            if (action.Kind == LegalActionKind.DialogResult)
            {
                return "Dialog result " + action.DialogResult;
            }
            if (action.Kind == LegalActionKind.ListIndex)
            {
                return "List index " + action.Index;
            }
            if (action.Kind == LegalActionKind.Cancel)
            {
                return "Pass empty dialog";
            }
            return action.Kind.ToString();
        }
    }
}

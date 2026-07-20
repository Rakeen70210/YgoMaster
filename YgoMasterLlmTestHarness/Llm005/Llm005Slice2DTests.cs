using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-005 Slice 2D: transactional Extra Deck summon completion.
    /// </summary>
    static class Llm005Slice2DTests
    {
        const int ControlledPlayer = 1;
        const int SummonCardId = 11263;

        public static void RunAll()
        {
            PlacementGuardEscalatesAfterBoundedRetry();
            BrokerSummonStartsInteractionBeforePlacement();
            RawLocationPromptUsesActiveSummonIdentity();
            ActiveSummonPromotesUnannotatedDecide();
            InteractionSignatureKeepsOriginButIgnoresSequenceForRetry();
            CompletionRequiresAuthoritativeStateEvidence();
            WrongSeatAndGenerationCannotReuseInteraction();
            TransitionAndCancellationClearOwnershipSafely();
        }

        static void BrokerSummonStartsInteractionBeforePlacement()
        {
            LlmAutomaticActionLoopGuard guard = new LlmAutomaticActionLoopGuard();
            DecisionSnapshot summonSnapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 1369,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = ControlledPlayer,
            };
            LegalAction summonAction = new LegalAction()
            {
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = ControlledPlayer,
                CardUniqueId = SummonCardId,
                CardId = SummonCardId,
            };

            LlmSummonInteractionDecision started = guard.BeginSummonInteraction(
                summonSnapshot,
                summonAction,
                7);
            AssertEqual(LlmSummonInteractionDisposition.Started, started.Disposition,
                "broker-selected summon starts the interaction before placement");
            AssertEqual((ulong)1369, started.OriginatingRunEffectSeq,
                "broker summon sequence is the interaction origin");

            DecisionSnapshot placementSnapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 1371,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = ControlledPlayer,
            };
            LegalAction placementAction = new LegalAction()
            {
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Decide,
                Player = ControlledPlayer,
                CardUniqueId = SummonCardId,
                CardId = SummonCardId,
                TargetScope = "summon_placement",
            };

            LlmSummonInteractionDecision placement = guard.TryAcquirePlacement(
                placementSnapshot,
                placementAction,
                7);
            AssertEqual(LlmSummonInteractionDisposition.Retry, placement.Disposition,
                "the first engine placement follows the broker summon without reopening identity");
            AssertEqual(started.StableSignature, placement.StableSignature,
                "broker summon and placement share a stable interaction identity");
        }

        static void RawLocationPromptUsesActiveSummonIdentity()
        {
            LlmAutomaticActionLoopGuard guard = new LlmAutomaticActionLoopGuard();
            DecisionSnapshot summonSnapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 50,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = ControlledPlayer,
            };
            LegalAction summonAction = new LegalAction()
            {
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = ControlledPlayer,
                CardUniqueId = SummonCardId,
                CardId = SummonCardId,
            };
            guard.BeginSummonInteraction(summonSnapshot, summonAction, 12);

            DecisionSnapshot rawLocationSnapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 51,
                ViewType = DuelViewType.WaitInput,
                ViewParam1 = (int)DuelMenuActType.Location,
                ViewParam2 = SummonCardId,
                ActingPlayer = ControlledPlayer,
            };
            LegalAction rawPlacementAction = new LegalAction()
            {
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Decide,
                Player = ControlledPlayer,
            };

            AssertTrue(guard.PreparePlacementAction(rawLocationSnapshot, rawPlacementAction),
                "raw location prompt must be promoted to a summon placement action");
            AssertEqual(SummonCardId, rawPlacementAction.CardUniqueId,
                "raw location prompt inherits the active summon UID");
            AssertEqual(SummonCardId, rawPlacementAction.CardId,
                "raw location prompt inherits the active summon card ID");
            LlmSummonInteractionDecision placement = guard.TryAcquirePlacement(
                rawLocationSnapshot,
                rawPlacementAction,
                12);
            AssertEqual(LlmSummonInteractionDisposition.Retry, placement.Disposition,
                "raw location prompt is a placement retry, not cancellation");
        }

        static void ActiveSummonPromotesUnannotatedDecide()
        {
            LlmAutomaticActionLoopGuard guard = new LlmAutomaticActionLoopGuard();
            guard.BeginSummonInteraction(
                new DecisionSnapshot()
                {
                    RunEffectSeq = 60,
                    ViewType = DuelViewType.WaitInput,
                    ActingPlayer = ControlledPlayer,
                },
                new LegalAction()
                {
                    Kind = LegalActionKind.Command,
                    Command = DuelCommandType.Summon,
                    Player = ControlledPlayer,
                    CardUniqueId = SummonCardId,
                    CardId = SummonCardId,
                },
                13);

            LegalAction unannotated = new LegalAction()
            {
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Decide,
                Player = ControlledPlayer,
                TargetScope = "battle_target",
            };
            AssertTrue(guard.PreparePlacementAction(
                    new DecisionSnapshot()
                    {
                        RunEffectSeq = 61,
                        ViewType = DuelViewType.WaitInput,
                        ActingPlayer = ControlledPlayer,
                    },
                    unannotated),
                "active summon ownership promotes an unannotated Decide follow-up");
            AssertEqual(SummonCardId, unannotated.CardUniqueId,
                "unannotated follow-up inherits the active summon UID");
            AssertEqual("summon_placement", unannotated.TargetScope,
                "unannotated follow-up is not treated as a battle target");
        }

        static void PlacementGuardEscalatesAfterBoundedRetry()
        {
            LlmAutomaticActionLoopGuard guard = new LlmAutomaticActionLoopGuard();
            DecisionSnapshot placementSnapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 1390,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = ControlledPlayer,
            };
            LegalAction placementAction = new LegalAction()
            {
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Decide,
                Player = ControlledPlayer,
                CardUniqueId = SummonCardId,
                Position = 18,
                TargetScope = "summon_placement",
            };

            AssertTrue(guard.TryAcquire(placementSnapshot, placementAction),
                "first placement attempt must be accepted");
            AssertTrue(guard.TryAcquire(placementSnapshot, placementAction),
                "repeat placement prompt must keep the interaction open");
            AssertFalse(guard.TryAcquire(placementSnapshot, placementAction),
                "third identical placement prompt must escalate to temporary CPU");

            DecisionSnapshot completedSnapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 1392,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = ControlledPlayer,
            };
            LegalAction nextPlacementAction = new LegalAction()
            {
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Decide,
                Player = ControlledPlayer,
                CardUniqueId = SummonCardId + 1,
                Position = 18,
                TargetScope = "summon_placement",
            };

            AssertTrue(guard.TryAcquire(completedSnapshot, nextPlacementAction),
                "different placement interaction must reset the guard");

            DecisionSnapshot nonPlacementSnapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 1393,
                ViewType = DuelViewType.RunDialog,
                ActingPlayer = ControlledPlayer,
            };
            LegalAction nonPlacementAction = new LegalAction()
            {
                Kind = LegalActionKind.Cancel,
                Player = ControlledPlayer,
                TargetScope = "empty_check_chain_decline",
            };
            AssertTrue(guard.TryAcquire(nonPlacementSnapshot, nonPlacementAction),
                "non-placement actions must reset the guard state");
        }

        static void InteractionSignatureKeepsOriginButIgnoresSequenceForRetry()
        {
            LlmSummonInteractionTracker tracker = new LlmSummonInteractionTracker(2);
            LlmSummonResourceFacts before = Facts(
                extraDeckCount: 1,
                summonedFieldCount: 0,
                isExtraDeck: true,
                requiresMaterials: true,
                fieldCards: new Dictionary<int, int>()
                {
                    { 4771, 1 },
                    { 4927, 1 },
                });

            LlmSummonInteractionDecision started = tracker.Observe(Placement(
                generation: 7,
                seq: 1369,
                player: ControlledPlayer,
                cardUniqueId: SummonCardId,
                cardId: SummonCardId,
                sourceFamily: "xyz",
                facts: before));
            AssertEqual(LlmSummonInteractionDisposition.Started, started.Disposition,
                "first placement starts interaction");
            AssertEqual((ulong)1369, started.OriginatingRunEffectSeq,
                "interaction keeps originating sequence");
            AssertEqual(1, started.Attempt, "first placement attempt number");

            LlmSummonInteractionDecision retry = tracker.Observe(Placement(
                generation: 7,
                seq: 1391,
                player: ControlledPlayer,
                cardUniqueId: SummonCardId,
                cardId: SummonCardId,
                sourceFamily: "xyz",
                facts: before));
            AssertEqual(LlmSummonInteractionDisposition.Retry, retry.Disposition,
                "same interaction may retry at a later engine sequence");
            AssertEqual((ulong)1369, retry.OriginatingRunEffectSeq,
                "retry retains original interaction sequence");
            AssertEqual(2, retry.Attempt, "retry attempt number");
            AssertEqual(started.StableSignature, retry.StableSignature,
                "sequence changes do not change stable interaction signature");

            LlmSummonInteractionDecision repeated = tracker.Observe(Placement(
                generation: 7,
                seq: 1392,
                player: ControlledPlayer,
                cardUniqueId: SummonCardId,
                cardId: SummonCardId,
                sourceFamily: "xyz",
                facts: before));
            AssertEqual(LlmSummonInteractionDisposition.TemporaryCpu, repeated.Disposition,
                "repeated placement prompts escalate after the bounded retry");
            AssertEqual(true, repeated.ShouldUseTemporaryCpu,
                "repeated placement prompts request temporary CPU recovery");
        }

        static void CompletionRequiresAuthoritativeStateEvidence()
        {
            LlmSummonCompletionEvidence incomplete = new LlmSummonCompletionEvidence()
            {
                ExtraDeckCardRemoved = true,
                SummonedCardAppearsOnField = true,
                MaterialsResolved = false,
                ReachedNextDecisionBoundary = true,
            };
            AssertFalse(incomplete.IsAuthoritative,
                "cut-in plus field appearance without material resolution is not completion");

            LlmSummonCompletionEvidence complete = new LlmSummonCompletionEvidence()
            {
                ExtraDeckCardRemoved = true,
                SummonedCardAppearsOnField = true,
                MaterialsResolved = true,
                ReachedNextDecisionBoundary = true,
            };
            AssertTrue(complete.IsAuthoritative,
                "completion requires all authoritative state transitions");

            LlmSummonInteractionTracker tracker = new LlmSummonInteractionTracker(2);
            tracker.Observe(Placement(
                generation: 8,
                seq: 20,
                player: ControlledPlayer,
                cardUniqueId: SummonCardId,
                cardId: SummonCardId,
                sourceFamily: "xyz",
                facts: Facts(1, 0, true, true, new Dictionary<int, int>() {{ 4771, 1 }, { 4927, 1 }})));
            LlmSummonInteractionDecision notDone = tracker.Observe(new LlmSummonInteractionObservation()
            {
                DuelGeneration = 8,
                RunEffectSeq = 22,
                ActingPlayer = ControlledPlayer,
                CardUniqueId = SummonCardId,
                CardId = SummonCardId,
                SourceFamily = "xyz",
                ExpectedFollowupFamily = "summon_placement",
                IsPlacementPrompt = false,
                CompletionEvidence = incomplete,
            });
            AssertEqual(LlmSummonInteractionDisposition.Preserved, notDone.Disposition,
                "incomplete state evidence preserves the interaction for retry");
            AssertTrue(tracker.IsActive, "incomplete evidence must not clear interaction");

            LlmSummonInteractionDecision done = tracker.Observe(new LlmSummonInteractionObservation()
            {
                DuelGeneration = 8,
                RunEffectSeq = 23,
                ActingPlayer = ControlledPlayer,
                CardUniqueId = SummonCardId,
                CardId = SummonCardId,
                SourceFamily = "xyz",
                ExpectedFollowupFamily = "summon_placement",
                IsPlacementPrompt = false,
                CompletionEvidence = complete,
            });
            AssertEqual(LlmSummonInteractionDisposition.Completed, done.Disposition,
                "authoritative completion clears the interaction");
            AssertFalse(tracker.IsActive, "completed interaction is no longer active");
        }

        static void WrongSeatAndGenerationCannotReuseInteraction()
        {
            LlmSummonInteractionTracker tracker = new LlmSummonInteractionTracker(2);
            LlmSummonResourceFacts facts = Facts(1, 0, true, true, new Dictionary<int, int>() {{ 4771, 1 }});
            tracker.Observe(Placement(9, 30, ControlledPlayer, SummonCardId, SummonCardId, "xyz", facts));

            LlmSummonInteractionDecision wrongSeat = tracker.Observe(
                Placement(9, 31, 0, SummonCardId, SummonCardId, "xyz", facts));
            AssertEqual(LlmSummonInteractionDisposition.Rejected, wrongSeat.Disposition,
                "wrong seat cannot retry active summon interaction");
            AssertTrue(tracker.IsActive, "wrong-seat rejection preserves active interaction");

            LlmSummonInteractionDecision staleGeneration = tracker.Observe(
                Placement(10, 32, ControlledPlayer, SummonCardId, SummonCardId, "xyz", facts));
            AssertEqual(LlmSummonInteractionDisposition.Rejected, staleGeneration.Disposition,
                "cross-generation retry is rejected");
            AssertTrue(tracker.IsActive, "cross-generation rejection does not mutate active state");
        }

        static void TransitionAndCancellationClearOwnershipSafely()
        {
            LlmSummonInteractionTracker tracker = new LlmSummonInteractionTracker(2);
            LlmSummonResourceFacts facts = Facts(1, 0, true, true, new Dictionary<int, int>() {{ 4771, 1 }});
            tracker.Observe(Placement(11, 40, ControlledPlayer, SummonCardId, SummonCardId, "xyz", facts));

            LlmSummonInteractionDecision transition = tracker.Observe(new LlmSummonInteractionObservation()
            {
                DuelGeneration = 11,
                RunEffectSeq = 41,
                ActingPlayer = ControlledPlayer,
                CardUniqueId = SummonCardId,
                CardId = SummonCardId,
                SourceFamily = "xyz",
                ExpectedFollowupFamily = "summon_placement",
                IsSummonTransition = true,
            });
            AssertEqual(LlmSummonInteractionDisposition.Preserved, transition.Disposition,
                "overlay/cutin transition preserves summon ownership");
            AssertTrue(tracker.IsActive, "transition does not clear active interaction");

            LlmSummonInteractionDecision cancelled = tracker.Observe(new LlmSummonInteractionObservation()
            {
                DuelGeneration = 11,
                RunEffectSeq = 42,
                ActingPlayer = ControlledPlayer,
                CardUniqueId = SummonCardId,
                CardId = SummonCardId,
                SourceFamily = "xyz",
                ExpectedFollowupFamily = "summon_placement",
                IsCancellation = true,
            });
            AssertEqual(LlmSummonInteractionDisposition.Reset, cancelled.Disposition,
                "cancellation resets summon ownership");
            AssertFalse(tracker.IsActive, "cancelled interaction is cleared");
        }

        static LlmSummonInteractionObservation Placement(
            int generation,
            ulong seq,
            int player,
            int cardUniqueId,
            int cardId,
            string sourceFamily,
            LlmSummonResourceFacts facts)
        {
            return new LlmSummonInteractionObservation()
            {
                DuelGeneration = generation,
                RunEffectSeq = seq,
                ActingPlayer = player,
                CardUniqueId = cardUniqueId,
                CardId = cardId,
                SourceFamily = sourceFamily,
                ExpectedFollowupFamily = "summon_placement",
                IsPlacementPrompt = true,
                ResourceFacts = facts,
            };
        }

        static LlmSummonResourceFacts Facts(
            int extraDeckCount,
            int summonedFieldCount,
            bool isExtraDeck,
            bool requiresMaterials,
            Dictionary<int, int> fieldCards)
        {
            return new LlmSummonResourceFacts()
            {
                ExtraDeckCount = extraDeckCount,
                SummonedFieldCount = summonedFieldCount,
                IsExtraDeck = isExtraDeck,
                RequiresMaterialConsumption = requiresMaterials,
                FieldCardCounts = fieldCards,
            };
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value)
            {
                throw new Exception(message);
            }
        }

        static void AssertFalse(bool value, string message)
        {
            if (value)
            {
                throw new Exception(message);
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + " (expected " + expected + ", actual " + actual + ")");
            }
        }
    }
}

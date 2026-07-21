using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace YgoMaster
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                Llm004Slice0Tests.RunAll();
                // YGOMASTER-LLM-003 Milestone 3B + LLM-005 Slice 2G strategic prompt lease.
                Llm003Milestone3BTests.RunAll();
                // YGOMASTER-LLM-003 Milestones 3C/3D semantic recurrence + safe recovery.
                Llm003Milestone3CDTests.RunAll();
                ExtractsCommandActionsFromCommandMask();
                ExtractsIndexZeroCommandWhenCardNumIsZero();
                ExtractsCommandsAcrossAllIndexesUpToCardNum();
                ExtractsMovePhaseActionsFromPhaseMaskBeforeCommands();
                IgnoresNullPhaseBit();
                PreservesDecisionSnapshotMetadata();
                ExtractsPublicStateCountsAndKnownCards();
                ExtractsPublicFaceUpFieldCardsAndRedactsFaceDown();
                DoesNotExposeHiddenPublicStateCards();
                ExtractsControlledPlayerPrivateHandCardMetadata();
                DoesNotExposeOpponentHiddenHandCardMetadata();
                FiltersDebugAndSurrenderCommandsFromBrokerActions();
                ExtractsAutomaticDrawCommandWithoutBrokerExposure();
                FiltersSingleDecideCommandFromBrokerActions();
                ExtractsAutomaticSingleDecideCommandWithoutBrokerExposure();
                RoutesSelectionMenuToTemporaryCpuWithoutPrematureDecide();
                RoutesUnsupportedRunListToTemporaryCpu();
                TemporaryCpuSelectionCoordinatorDeduplicatesAndRestoresAtBoundary();
                DoesNotExtractAutomaticMain2PhaseByDefault();
                ExtractsAutomaticMain2PhaseForEmptyBattleWaitInput();
                DoesNotAutoCommitEmptyRunDialog();
                ExtractsForcedAcknowledgementForEmptyRunDialog();
                DoesNotAutoPassSelectableRunDialog();
                DoesNotTreatSelStandDialogAsNoChoice();
                TreatsConfirmDialogAsBinaryChoiceWhenEngineFlagIsMissing();
                LeavesEffectDialogWithoutEngineChoicesOnNativeDefaultPath();
                AddsDeclineActionToCheckChainWaitInput();
                AddsDeclineActionToCheckTimingWaitInput();
                DoesNotAddDeclineToForcedCheckChainWaitInput();
                EmptyCancellableCheckChainWaitInputAddsMechanicalDeclineAndAutoCommits();
                EmptyCancellableCheckTimingWaitInputAddsMechanicalDeclineAndAutoCommits();
                EmptyCheckChainDeclineCommitPlanUsesCancelNotDecide();
                ExtractsSummonPlacementActionsFromPositionMask();
                IgnoresStaleSummonPlacementMetadataOutsideLocationPrompt();
                ExtractsAttackTargetActionsFromTargetMask();
                ExtractsSingleAttackTargetAsAutomaticAction();
                RoutesStrategicAttackTargetsThroughBrokerGate();
                SerializesPublicSafeAttackTargetContinuationContext();
                HidesFaceDownAttackTargetIdentityFromRequestBytes();
                RejectsAttackTargetTokenFromDifferentInteractionLease();
                SerializesAttackTargetFallbackDivergence();
                DoesNotHeuristicallySubstituteAttackTargetDuringRecovery();
                ExtractsOpponentEffectTargetsAsStrategicActions();
                ExtractsSingleOpponentEffectTargetAsAutomaticAction();
                DoesNotExtractAttackTargetsWithoutPendingAttackContext();
                DoesNotExtractAttackTargetsWhenStandardBattleActionsExist();
                CardCatalogLoadsLazilyAndNormalizesText();
                CardCatalogReturnsNullWhenLoadingFails();
                CardDataResolverSkipsMissingBaseDirAndUsesValidCandidate();
                IgnoresEmptyCommandAndPhaseMasks();
                SerializesDecisionWindowAsJsonLine();
                SerializesCommittedCommandAsJsonLine();
                SerializesCommittedPhaseAsJsonLine();
                SerializesBrokerCommittedActionAsJsonLine();
                SerializesBrokerRequestStartedAsJsonLine();
                SerializesBrokerResponseAsJsonLine();
                SerializesBrokerFailureAsJsonLine();
                SerializesBrokerCommitSkippedAsJsonLine();
                SerializesBrokerRejectedActionAsJsonLine();
                SerializesBrokerAutomaticActionAsJsonLine();
                SerializesWindowRoutedAsJsonLine();
                SerializesUnsupportedWindowAsJsonLine();
                SerializesStuckWindowRecoveryAsJsonLine();
                SerializesBrokerDecisionRequest();
                SerializesBrokerRequestStartedWithSnapshotDetails();
                SerializesPublicStateInBrokerDecisionRequest();
                SerializesSchemaV3CardMetadataInBrokerDecisionRequest();
                SerializesSafeBoardContextInBrokerDecisionRequest();
                SerializesOpponentContextInBrokerDecisionRequest();
                SerializesTurnMemoryInBrokerDecisionRequest();
                ClassifiesStrategicWindowAndSerializesActionSemantics();
                ClassifiesMechanicalDecideWindowBeforeBrokerDispatch();
                ParsesBrokerDecisionResponse();
                ParsesBrokerDecisionResponseWithConfidenceAndPlan();
                ParsesBrokerDecisionResponseWithTacticalAuditFields();
                ParsesBrokerDecisionResponseWithOpponentBoardAssessment();
                ParsesBrokerErrorResponse();
                AcceptsBrokerResponseWithLegalActionId();
                RejectsBrokerResponseWithLowConfidence();
                RejectsBrokerResponseWithGenericReason();
                RejectsBrokerResponseForMechanicalAction();
                RejectsBrokerResponseForCardlessStrategicAction();
                RejectsBrokerResponseForEarlyEndPhaseWithPlayableCommand();
                SelectsPreferredActionForEarlyEndPhaseRecovery();
                SelectsQualityPassingActionWhenPreferredActionChanged();
                RecoverableQualityErrorExcludesStaleSeq();
                SerializesBrokerRecoveredActionAsJsonLine();
                BrokerRecoverySelectsPreferredActionForEarlyEndPhase();
                RejectsBrokerResponseWhenProviderLatencyExceedsQualityBudget();
                RejectsBrokerResponseWithStaleRunEffectSeq();
                RejectsBrokerResponseWithUnknownActionId();
                AcceptsBrokerResponseWhenExpectedActionStillMatches();
                RejectsBrokerResponseWhenActionIdChangedMeaning();
                RejectsBrokerResponseWhenPhaseActionChangedMeaning();
                BrokerControlPolicyOnlyControlsLocalConfiguredPlayer();
                BrokerControlPolicySuppressesLocalControlledPendingRequest();
                BrokerControlPolicyRunsCpuThinkingForLocalControlledWhenNoRequestStarts();
                BrokerControlPolicyFallbackRunsCpuThinkingForLocalControlledPlayer();
                BrokerControlPolicyRunsCpuThinkingForUncontrolledRemotePlayer();
                BrokerControlPolicyRunsDefaultForLocalUncontrolledEmptyWaitInput();
                BrokerControlPolicyRunsDefaultForControlledEmptyWaitInputWithoutActions();
                BrokerControlPolicyStillSuppressesControlledStrategicPendingRequest();
                RoutesControlledStrategicWindowToBroker();
                RoutesControlledMechanicalWindowToAutomatic();
                RoutesControlledEmptyWindowToCpuFallback();
                RoutesUncontrolledInfoDialogToDefault();
                RoutesPendingBrokerRequestToSuppressed();
                RoutesPendingBrokerRequestSuppressesBeforeAutomatic();
                RoutesGateFallbackToCpuFallback();
                RoutesGateFallbackDoesNotBlockAutomatic();
                RoutesMechanicalOnlyWithoutUnsupportedFlag();
                RoutesKnownSummonPlacementWindowToAutomatic();
                ResolvesPlannerSelectedAutomaticActionForCommit();
                Llm005Slice2DTests.RunAll();
                RoutesUncontrolledRemotePromptToCpuFallback();
                RoutesUncontrolledLocalPromptToDefault();
                BrokerClientPostsDecisionRequestAndReturnsLegalAction();
                BrokerClientRecordsLatencyMillis();
                BrokerClientRejectsInvalidJson();
                BrokerClientPreservesBrokerErrorResponseBody();
                HttpBrokerTransportPreservesErrorResponseBody();
                CreatesCommandCommitPlan();
                CreatesPhaseCommitPlan();
                CreatesSummonPlacementCommitPlan();
                BlocksRepeatedAutomaticSummonPlacementPrompt();
                StuckWindowWatchdogDetectsCrossSequenceLogicalCycle();
                StuckWindowWatchdogDetectsAutomaticCheckTimingCycle();
                StuckWindowWatchdogIgnoresPendingAndUncontrolledWindows();
                TemporaryCpuCoordinatorAllowsExplicitWatchdogRecovery();
                CreatesCancelCommitPlan();
                ExtractsEnabledDialogResultActions();
                ExtractsYesNoEffectDialogAsStrategicActions();
                ExtractsListIndexActions();
                DoesNotExtractUnsupportedListSelectionWindows();
                DoesNotOfferWaitInputActionsForRunDialog();
                SerializesDialogAndListActions();
                RejectsBrokerResponseWhenDialogActionChangedMeaning();
                RejectsBrokerResponseWhenListActionChangedMeaning();
                CreatesDialogResultCommitPlan();
                CreatesListIndexCommitPlan();
                BrokerGateStartsFirstSeqAndKeepsSameSeqPending();
                BrokerGateFallsBackForNewSeqWhileOldSeqIsPending();
                BrokerGateFallsBackAfterFailedSeq();
                BrokerGateSuppressesCompletedSeqOnceThenFallsBack();
                BrokerGateKeepsFallingBackAfterCompletedSeqSuppressIsConsumed();
                BrokerGateAllowsZeroSeq();
                BrokerGateResetClearsPendingRequest();
                TurnMemoryTrackerRecordsRecentActionsAndResets();
                BrokerPlayerResolverPrefersTurnPlayerForWaitInput();
                BrokerPlayerResolverUsesReportedUserForResponseWaitInput();
                BrokerPlayerResolverUsesReportedUserForLockOnWaitInput();
                BrokerPlayerResolverFallsBackToRivalTurnForDialog();
                BrokerPlayerResolverTrustsReportedDialogUserEvenWhenLocal();
                BrokerPlayerResolverTrustsReportedDialogUserIdenticallyForBothSeats();
                ResettingNonReadyPlayerKeepsOpponentReadyAndPvpSession();
                ResettingMatchedPlayerKeepsOpponentReadyAndPvpSession();
                // YGOMASTER-LLM-005: 1A+1B+2A + live-gate fixes; Slice 0 planning; Slice 0B outcome RED.
                Llm005Slice1ATests.RunAll();
                Llm005Slice1BTests.RunAll();
                Llm005LiveGateFixTests.RunAll();
                Llm005Slice2ATests.RunAll();
                Llm005Slice0Tests.RunAll();
                Llm005Slice0BTests.RunAll();
                Llm005Slice2BIntegrationTests.RunAll();
                Llm005ApplicabilityFollowupTests.RunAll();
                // YGOMASTER-LLM-005 Slice 2C: Synchro candidacy TDD RED (tests only).
                Llm005Slice2CTests.RunAll();
                // YGOMASTER-LLM-005 Slice 5: out-of-process replay worker contract.
                Llm005Slice5Tests.RunAll();
                Llm005Slice5ReviewRegressionTests.RunAll();
                // Slice 5 Pvp authoritative transcript + independent-review remediation.
                Llm005Slice5PvpAuthorityRegressionTests.RunAll();
                Llm005Slice5PvpAuthorityRemediationTests.RunAll();
                // YGOMASTER-LLM-005 Slice 6B: receding-horizon semantic planning (tests-only RED).
                Llm005Slice6BTests.RunAll();
                Llm005Slice2HTests.RunAll();
                Console.WriteLine("YgoMasterLlmTestHarness: all tests passed");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(e.StackTrace);
                return 1;
            }
        }

        static void ExtractsCommandActionsFromCommandMask()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(0, 13)] = 1;
            query.CommandMasks[Key(0, 13, 0)] =
                (uint)((1 << (int)DuelCommandType.Attack) | (1 << (int)DuelCommandType.Action));
            query.CardUniqueIds[Key(0, 13, 0)] = 12031;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 42, DuelViewType.WaitInput, 0);

            AssertEqual(2, snapshot.LegalActions.Count, "expected two command actions");
            AssertCommand(snapshot.LegalActions[0], 0, 0, 13, 0, DuelCommandType.Attack, 12031);
            AssertCommand(snapshot.LegalActions[1], 1, 0, 13, 0, DuelCommandType.Action, 12031);
        }

        static void ExtractsMovePhaseActionsFromPhaseMaskBeforeCommands()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.MovablePhaseMask =
                (uint)((1 << (int)DuelPhase.Battle) | (1 << (int)DuelPhase.End));
            query.CardNums[Key(1, 4)] = 1;
            query.CommandMasks[Key(1, 4, 0)] = (uint)(1 << (int)DuelCommandType.SummonSp);
            query.CardUniqueIds[Key(1, 4, 0)] = 22019;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 7, DuelViewType.WaitInput, 1);

            AssertEqual(3, snapshot.LegalActions.Count, "expected two phase actions and one command action");
            AssertPhase(snapshot.LegalActions[0], 0, DuelPhase.Battle);
            AssertPhase(snapshot.LegalActions[1], 1, DuelPhase.End);
            AssertCommand(snapshot.LegalActions[2], 2, 1, 4, 0, DuelCommandType.SummonSp, 22019);
        }

        static void ExtractsCommandsAcrossAllIndexesUpToCardNum()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(0, 13)] = 2;
            query.CommandMasks[Key(0, 13, 1)] = (uint)(1 << (int)DuelCommandType.Set);
            query.CommandMasks[Key(0, 13, 2)] = (uint)(1 << (int)DuelCommandType.Pendulum);
            query.CardUniqueIds[Key(0, 13, 1)] = 101;
            query.CardUniqueIds[Key(0, 13, 2)] = 102;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 5, DuelViewType.WaitInput, 0);

            AssertEqual(2, snapshot.LegalActions.Count, "expected commands on indexes 1 and 2");
            AssertCommand(snapshot.LegalActions[0], 0, 0, 13, 1, DuelCommandType.Set, 101);
            AssertCommand(snapshot.LegalActions[1], 1, 0, 13, 2, DuelCommandType.Pendulum, 102);
        }

        static void IgnoresNullPhaseBit()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.MovablePhaseMask =
                (uint)((1 << (int)DuelPhase.Main1) | (1 << (int)DuelPhase.Null));

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 2, DuelViewType.WaitInput, 0);

            AssertEqual(1, snapshot.LegalActions.Count, "expected null phase to be ignored");
            AssertPhase(snapshot.LegalActions[0], 0, DuelPhase.Main1);
        }

        static void PreservesDecisionSnapshotMetadata()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.TurnNum = 12;
            query.TurnPlayer = 1;
            query.CurrentPhase = (int)DuelPhase.Battle;
            query.CurrentStep = (int)DuelStepType.Damage;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 99, DuelViewType.WaitInput, 1);

            AssertEqual((ulong)99, snapshot.RunEffectSeq, "run_effect_seq");
            AssertEqual(DuelViewType.WaitInput, snapshot.ViewType, "view_type");
            AssertEqual(1, snapshot.ActingPlayer, "acting_player");
            AssertEqual(12, snapshot.Turn, "turn");
            AssertEqual(1, snapshot.TurnPlayer, "turn_player");
            AssertEqual((int)DuelPhase.Battle, snapshot.CurrentPhase, "current_phase");
            AssertEqual((int)DuelStepType.Damage, snapshot.CurrentStep, "current_step");
        }

        static void ExtractsPublicStateCountsAndKnownCards()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.LifePoints[0] = 8000;
            query.LifePoints[1] = 6200;
            query.CardNums[Key(0, 13)] = 2;
            query.CardNums[Key(1, 16)] = 1;
            query.CardUniqueIds[Key(0, 13, 0)] = 101;
            query.CardUniqueIds[Key(0, 13, 1)] = 102;
            query.CardUniqueIds[Key(1, 16, 0)] = 201;
            query.HandCardOpen[Key(0, 0)] = 1;
            query.CardIdsByUniqueId[101] = 1111;
            query.CardIdsByUniqueId[102] = 2222;
            query.CardIdsByUniqueId[201] = 3333;
            query.CardFaces[Key(0, 13, 0)] = 7;
            query.CardFaces[Key(1, 16, 0)] = 8;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 12, DuelViewType.WaitInput, 0);

            AssertEqual(2, snapshot.PublicState.Players.Count, "player count");
            AssertEqual(8000, snapshot.PublicState.Players[0].LifePoints, "p0 lp");
            AssertEqual(6200, snapshot.PublicState.Players[1].LifePoints, "p1 lp");
            AssertEqual(2, snapshot.PublicState.Players[0].Positions[13].Count, "p0 hand count");
            AssertEqual(1, snapshot.PublicState.Players[1].Positions[16].Count, "p1 grave count");
            AssertEqual(2, snapshot.PublicState.Players[0].KnownCards.Count, "p0 known cards");
            AssertEqual(1, snapshot.PublicState.Players[1].KnownCards.Count, "p1 known cards");
            AssertEqual(1111, snapshot.PublicState.Players[0].KnownCards[0].CardId, "open hand card id");
            AssertEqual(2222, snapshot.PublicState.Players[0].KnownCards[1].CardId, "controlled hidden hand card id");
            AssertEqual(3333, snapshot.PublicState.Players[1].KnownCards[0].CardId, "grave card id");
            AssertEqual(8, snapshot.PublicState.Players[1].KnownCards[0].Face, "grave face");
        }

        static void DoesNotExposeHiddenPublicStateCards()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 0)] = 0;
            query.CardNums[Key(1, 13)] = 1;
            query.CardNums[Key(1, 14)] = 1;
            query.CardNums[Key(1, 15)] = 1;
            query.CardNums[Key(1, 17)] = 1;
            query.CardNums[Key(1, 18)] = 1;
            query.CardUniqueIds[Key(1, 0, 0)] = 301;
            query.CardUniqueIds[Key(1, 13, 0)] = 302;
            query.CardUniqueIds[Key(1, 14, 0)] = 303;
            query.CardUniqueIds[Key(1, 15, 0)] = 304;
            query.CardUniqueIds[Key(1, 17, 0)] = 305;
            query.CardUniqueIds[Key(1, 18, 0)] = 306;
            query.CardIdsByUniqueId[301] = 4301;
            query.CardIdsByUniqueId[302] = 4302;
            query.CardIdsByUniqueId[303] = 4303;
            query.CardIdsByUniqueId[304] = 4304;
            query.CardIdsByUniqueId[305] = 4305;
            query.CardIdsByUniqueId[306] = 4306;
            query.HandCardOpen[Key(1, 0)] = 0;
            query.CardFaces[Key(1, 0, 0)] = 0;
            query.CardFaces[Key(1, 17, 0)] = 1;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 13, DuelViewType.WaitInput, 0);

            AssertEqual(0, snapshot.PublicState.Players[1].KnownCards.Count, "hidden known cards");
        }

        static void ExtractsPublicFaceUpFieldCardsAndRedactsFaceDown()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 2)] = 1;
            query.CardUniqueIds[Key(1, 2, 0)] = 401;
            query.CardUniqueIds[Key(1, 2, 1)] = 402;
            query.CardIdsByUniqueId[401] = 5401;
            query.CardIdsByUniqueId[402] = 5402;
            query.CardFaces[Key(1, 2, 0)] = 1;
            query.CardFaces[Key(1, 2, 1)] = 0;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 14, DuelViewType.WaitInput, 0);

            AssertEqual(1, snapshot.PublicState.Players[1].KnownCards.Count,
                "only public face-up field identity");
            AssertEqual(5401, snapshot.PublicState.Players[1].KnownCards[0].CardId,
                "face-up opponent field card id");
        }

        static void ExtractsControlledPlayerPrivateHandCardMetadata()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 13)] = 1;
            query.CardUniqueIds[Key(1, 13, 0)] = 901;
            query.CardIdsByUniqueId[901] = 4900;
            query.CommandMasks[Key(1, 13, 0)] = (uint)(1 << (int)DuelCommandType.Summon);
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[4900] = CreateCard(4900, "Cubic Seed", "Starts the Cubic line.");

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 66, DuelViewType.WaitInput, 1, catalog);

            AssertEqual(1, snapshot.ControlledPlayer, "controlled_player");
            AssertEqual(1, snapshot.PublicState.Players[1].KnownCards.Count, "known card count");
            AssertEqual(4900, snapshot.PublicState.Players[1].KnownCards[0].CardId, "known card id");
            AssertEqual("Cubic Seed", snapshot.PublicState.Players[1].KnownCards[0].Card.Name, "known card name");
            AssertEqual(1, snapshot.LegalActions.Count, "legal action count");
            AssertEqual(4900, snapshot.LegalActions[0].CardId, "action card id");
            AssertEqual("Cubic Seed", snapshot.LegalActions[0].Card.Name, "action card name");
        }

        static void DoesNotExposeOpponentHiddenHandCardMetadata()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(0, 13)] = 1;
            query.CardUniqueIds[Key(0, 13, 0)] = 902;
            query.CardIdsByUniqueId[902] = 4901;
            query.CardNums[Key(1, 13)] = 1;
            query.CardUniqueIds[Key(1, 13, 0)] = 903;
            query.CardIdsByUniqueId[903] = 4902;
            query.CommandMasks[Key(1, 13, 0)] = (uint)(1 << (int)DuelCommandType.Summon);
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[4901] = CreateCard(4901, "Opponent Secret", "This must stay hidden.");
            catalog.Cards[4902] = CreateCard(4902, "Controlled Card", "This can be shown.");

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 67, DuelViewType.WaitInput, 1, catalog);
            string requestJson = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);

            AssertEqual(0, snapshot.PublicState.Players[0].KnownCards.Count, "opponent hidden known cards");
            AssertEqual(false, requestJson.Contains("Opponent Secret"), "opponent hidden card not serialized");
            AssertEqual(true, requestJson.Contains("Controlled Card"), "controlled card serialized");
        }

        static void FiltersDebugAndSurrenderCommandsFromBrokerActions()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 13)] = 1;
            query.CardUniqueIds[Key(1, 13, 0)] = 904;
            query.CommandMasks[Key(1, 13, 0)] =
                (uint)((1 << (int)DuelCommandType.Look) |
                    (1 << (int)DuelCommandType.Surrender) |
                    (1 << (int)DuelCommandType.Summon));

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 68, DuelViewType.WaitInput, 1);

            AssertEqual(1, snapshot.LegalActions.Count, "filtered legal action count");
            AssertCommand(snapshot.LegalActions[0], 0, 1, 13, 0, DuelCommandType.Summon, 904);
        }

        static void ExtractsAutomaticDrawCommandWithoutBrokerExposure()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CommandMasks[Key(1, 0, 0)] = (uint)(1 << (int)DuelCommandType.Draw);

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 70, DuelViewType.WaitInput, 1);
            LegalAction automaticAction;
            bool extracted = LegalActionExtractor.TryExtractAutomaticAction(
                query, DuelViewType.WaitInput, 1, out automaticAction);

            AssertEqual(0, snapshot.LegalActions.Count, "draw action is hidden from broker");
            AssertEqual(true, extracted, "automatic draw extracted");
            AssertCommand(automaticAction, 0, 1, 0, 0, DuelCommandType.Draw, 0);
        }

        static void FiltersSingleDecideCommandFromBrokerActions()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 13)] = 1;
            query.CardUniqueIds[Key(1, 13, 0)] = 905;
            query.CommandMasks[Key(1, 13, 0)] = (uint)(1 << (int)DuelCommandType.Decide);

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 69, DuelViewType.WaitInput, 1);

            AssertEqual(0, snapshot.LegalActions.Count, "single decide action count");
        }

        static void ExtractsAutomaticSingleDecideCommandWithoutBrokerExposure()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CommandMasks[Key(1, 2, 0)] = (uint)(1 << (int)DuelCommandType.Decide);

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 71, DuelViewType.WaitInput, 1);
            LegalAction automaticAction;
            bool extracted = LegalActionExtractor.TryExtractAutomaticAction(
                query, DuelViewType.WaitInput, 1, out automaticAction);

            AssertEqual(0, snapshot.LegalActions.Count, "single decide action is hidden from broker");
            AssertEqual(true, extracted, "automatic decide extracted");
            AssertCommand(automaticAction, 0, 1, 2, 0, DuelCommandType.Decide, 0);
        }

        static void RoutesSelectionMenuToTemporaryCpuWithoutPrematureDecide()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CommandMasks[Key(1, 18, 0)] =
                (uint)(1 << (int)DuelCommandType.Decide);
            query.CardUniqueIds[Key(1, 18, 0)] = 901;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 276, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.Selection,
                9760,
                (int)DuelMenuParamType.Decide);

            LegalAction automaticAction = null;
            if (snapshot.LegalActions.Count == 0)
            {
                LegalActionExtractor.TryExtractAutomaticAction(
                    query,
                    DuelViewType.WaitInput,
                    1,
                    out automaticAction);
            }
            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                1,
                true,
                LlmBrokerRequestGateDecision.SuppressForPendingRequest,
                false,
                true,
                automaticAction);
            AssertEqual(LlmDecisionWindowRoute.TemporaryCpu, plan.Route,
                "multi-select material window routes to temporary CPU");
            AssertEqual(null, plan.AutomaticAction,
                "selection window has no commit-eligible automatic action");
        }

        static void TemporaryCpuSelectionCoordinatorDeduplicatesAndRestoresAtBoundary()
        {
            LlmTemporaryCpuSelectionCoordinator coordinator =
                new LlmTemporaryCpuSelectionCoordinator();

            AssertEqual(true, coordinator.TryBegin(58, 1),
                "first temporary CPU request starts");
            AssertEqual(false, coordinator.TryBegin(58, 1),
                "same selection request is deduplicated");
            AssertEqual(false, coordinator.ShouldRestore(
                DuelViewType.CpuThinking, 0),
                "CPU thinking presentation remains delegated");
            AssertEqual(false, coordinator.ShouldRestore(
                DuelViewType.WaitInput, (int)DuelMenuActType.Selection),
                "selection follow-up remains delegated");
            AssertEqual(false, coordinator.ShouldRestore(
                DuelViewType.RunDialog, 0),
                "selection dialog follow-up remains delegated");
            AssertEqual(false, coordinator.ShouldRestore(
                DuelViewType.RunList, 0),
                "selection list follow-up remains delegated");
            AssertEqual(true, coordinator.ShouldSuppressDecisionView(
                DuelViewType.WaitInput, (int)DuelMenuActType.Selection),
                "selection wait input is suppressed while CPU owns the seat");
            AssertEqual(true, coordinator.ShouldSuppressDecisionView(
                DuelViewType.RunDialog, 0),
                "dialog follow-up is suppressed while CPU owns the seat");
            AssertEqual(true, coordinator.ShouldSuppressDecisionView(
                DuelViewType.RunList, 0),
                "list follow-up is suppressed while CPU owns the seat");
            AssertEqual(true, coordinator.ShouldRestore(
                DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "ordinary wait input restores human control");
            AssertEqual(true, coordinator.ShouldRestore(
                DuelViewType.CardMove, 0),
                "resolution animation restores human control");

            coordinator.MarkRestored();
            AssertEqual(true, coordinator.TryBegin(59, 1),
                "later selection can start after restoration");

            AssertEqual(false, coordinator.ShouldRestore(
                DuelViewType.WaitInput,
                (int)DuelMenuActType.Location,
                true),
                "summon placement remains delegated while temporary CPU owns the seat");
            AssertEqual(true, coordinator.ShouldSuppressDecisionView(
                DuelViewType.WaitInput,
                (int)DuelMenuActType.Location,
                true),
                "summon placement wait input is suppressed while temporary CPU owns the seat");
            AssertEqual(false, coordinator.ShouldRestore(
                DuelViewType.WaitInput,
                (int)DuelMenuActType.Location,
                false),
                "location recovery remains delegated after native summon metadata disappears");
            AssertEqual(true, coordinator.ShouldSuppressDecisionView(
                DuelViewType.WaitInput,
                (int)DuelMenuActType.Location,
                false),
                "metadata-poor location recovery remains suppressed while CPU owns the seat");

            AssertEqual(true, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                59, 59, DuelViewType.WaitInput, (int)DuelMenuActType.Selection,
                1, 1, 1),
                "matching selection request is accepted");
            AssertEqual(true, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                60, 60, DuelViewType.RunList, 1,
                0, 1, 1),
                "matching RunList owner is accepted despite stale command user");
            AssertEqual(false, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                58, 59, DuelViewType.WaitInput, (int)DuelMenuActType.Selection,
                1, 1, 1),
                "stale selection request is rejected");
            AssertEqual(false, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                59, 59, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase,
                1, 1, 1),
                "non-selection request is rejected");
            AssertEqual(false, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                59, 59, DuelViewType.WaitInput, (int)DuelMenuActType.Selection,
                0, 1, 1),
                "wrong acting seat is rejected");
            AssertEqual(false, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                59, 59, DuelViewType.WaitInput, (int)DuelMenuActType.Selection,
                1, 0, 1),
                "spoofed actor seat is rejected");
            AssertEqual(true, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                61, 61, DuelViewType.WaitInput, (int)DuelMenuActType.Location,
                1, 1, 1, true),
                "matching summon placement request is accepted");
            AssertEqual(true, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                61, 61, DuelViewType.WaitInput, (int)DuelMenuActType.Location,
                1, 1, 1, false),
                "owned location recovery survives transient native summon metadata loss");
        }

        static void RoutesUnsupportedRunListToTemporaryCpu()
        {
            FakeLegalActionQuery unsupportedQuery = new FakeLegalActionQuery();
            unsupportedQuery.ListItemMax = 5;
            unsupportedQuery.ListSelectMin = 2;
            unsupportedQuery.ListSelectMax = 2;
            unsupportedQuery.ListIsMultiMode = 1;
            DecisionSnapshot unsupported = LegalActionExtractor.Extract(
                unsupportedQuery, 32, DuelViewType.RunList, 1);
            LegalActionExtractor.ApplyViewContext(unsupported, unsupportedQuery, 1, 5, 0);

            LlmDecisionWindowPlan unsupportedPlan = LlmDecisionWindowPlanner.Plan(
                unsupported,
                true,
                1,
                1,
                true,
                null,
                false,
                true,
                null);
            AssertEqual(LlmDecisionWindowRoute.TemporaryCpu, unsupportedPlan.Route,
                "unsupported controlled RunList routes to temporary CPU");
            AssertEqual("selection_multi_step", unsupportedPlan.Reason,
                "unsupported RunList route reason");
        }

        static void ExtractsAutomaticMain2PhaseForEmptyBattleWaitInput()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CurrentPhase = (int)DuelPhase.Battle;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 72, DuelViewType.WaitInput, 1);
            LegalAction automaticAction;
            bool extracted = LegalActionExtractor.TryExtractAutomaticAction(
                query, DuelViewType.WaitInput, 1, true, out automaticAction);

            AssertEqual(0, snapshot.LegalActions.Count, "empty battle action count");
            AssertEqual(true, extracted, "automatic main2 extracted");
            AssertEqual(LegalActionKind.MovePhase, automaticAction.Kind, "automatic kind");
            AssertEqual(DuelPhase.Main2, automaticAction.Phase, "automatic phase");
        }

        static void DoesNotExtractAutomaticMain2PhaseByDefault()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CurrentPhase = (int)DuelPhase.Battle;

            LegalAction automaticAction;
            bool extracted = LegalActionExtractor.TryExtractAutomaticAction(
                query, DuelViewType.WaitInput, 1, out automaticAction);

            AssertEqual(false, extracted, "automatic main2 requires follow-up permission");
            AssertEqual(null, automaticAction, "automatic action");
        }

        static void DoesNotAutoCommitEmptyRunDialog()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();

            LegalAction automaticAction;
            bool extracted = LegalActionExtractor.TryExtractAutomaticAction(
                query, DuelViewType.RunDialog, 1, out automaticAction);

            AssertEqual(true, LegalActionExtractor.IsDialogWithoutChoice(query),
                "empty dialog uses native default path");
            AssertEqual(false, extracted, "empty dialog is left to the native default path");
            AssertEqual(null, automaticAction, "automatic action");
        }

        static void DoesNotAutoPassSelectableRunDialog()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.DialogSelectItemNum = 1;
            query.DialogSelectItemEnabled[0] = 1;

            LegalAction automaticAction;
            bool extracted = LegalActionExtractor.TryExtractAutomaticAction(
                query, DuelViewType.RunDialog, 1, out automaticAction);

            AssertEqual(false, LegalActionExtractor.IsDialogWithoutChoice(query),
                "selectable dialog does not use native default path");
            AssertEqual(false, extracted, "selectable dialog is not auto-passed");
            AssertEqual(null, automaticAction, "automatic action");
        }

        static void ExtractsForcedAcknowledgementForEmptyRunDialog()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            LegalAction action;

            AssertEqual(true,
                LegalActionExtractor.TryExtractForcedDialogAcknowledgement(
                    query, 0, 0, out action),
                "empty default dialog has forced acknowledgement");
            AssertEqual(LegalActionKind.DialogResult, action.Kind,
                "acknowledgement action kind");
            AssertEqual(0, action.DialogResult,
                "acknowledgement uses engine default result");
            AssertEqual(true, action.IsMechanical,
                "acknowledgement is mechanical");
            AssertEqual("dialog_acknowledgement", action.TargetScope,
                "acknowledgement target scope");

            query.DialogSelectItemNum = 1;
            query.DialogSelectItemEnabled[0] = 1;
            AssertEqual(false,
                LegalActionExtractor.TryExtractForcedDialogAcknowledgement(
                    query, 0, 0, out action),
                "selectable dialog is never acknowledged automatically");

            query.DialogSelectItemNum = 0;
            query.DialogCanYesNoSkip = 1;
            AssertEqual(false,
                LegalActionExtractor.TryExtractForcedDialogAcknowledgement(
                    query, 0, 0, out action),
                "yes-no dialog is never acknowledged automatically");

            query.DialogCanYesNoSkip = 0;
            AssertEqual(false,
                LegalActionExtractor.TryExtractForcedDialogAcknowledgement(
                    query, (int)DuelDialogType.SelStand, 0, out action),
                "summon-position dialog remains interactive");
            AssertEqual(false,
                LegalActionExtractor.TryExtractForcedDialogAcknowledgement(
                    query, (int)DuelDialogType.Confirm, 0, out action),
                "confirm dialog remains interactive");
        }

        static void DoesNotTreatSelStandDialogAsNoChoice()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();

            AssertEqual(false, LegalActionExtractor.IsDialogWithoutChoice(
                query,
                (int)DuelDialogType.SelStand),
                "summon position dialog is interactive without select-item rows");
            AssertEqual(
                LlmBrokerViewHandling.RunDefault,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 0, 0, false, false, false),
                "owning client renders summon position dialog");
            AssertEqual(
                LlmBrokerViewHandling.RunCpuThinking,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 0, 1, false, false, false),
                "remote client suppresses summon position dialog");
        }

        static void TreatsConfirmDialogAsBinaryChoiceWhenEngineFlagIsMissing()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 184, DuelViewType.RunDialog, 1);

            LegalActionExtractor.ApplyViewContext(
                snapshot, (int)DuelDialogType.Confirm, 1, 40);

            AssertEqual(false, LegalActionExtractor.IsDialogWithoutChoice(
                query, (int)DuelDialogType.Confirm),
                "confirm dialog is an actionable binary prompt");
            AssertEqual(2, snapshot.LegalActions.Count, "confirm action count");
            AssertEqual(1, snapshot.LegalActions[0].DialogResult, "confirm result");
            AssertEqual(0, snapshot.LegalActions[1].DialogResult, "cancel result");
            AssertEqual(true, snapshot.IsStrategicWindow, "confirm strategic window");
            AssertEqual((int)DuelDialogType.Confirm, snapshot.ViewParam1, "confirm view type");
            AssertEqual(1, snapshot.ViewParam2, "confirm view param2");
            AssertEqual(40, snapshot.ViewParam3, "confirm view param3");

            LlmDecisionWindowPlan controlledPlan = LlmDecisionWindowPlanner.Plan(
                snapshot, true, 1, 1, true,
                LlmBrokerRequestGateDecision.StartRequest, false, true, null);
            AssertEqual(LlmDecisionWindowRoute.Broker, controlledPlan.Route,
                "controlled confirm routes to broker");

            LlmDecisionWindowPlan remotePlan = LlmDecisionWindowPlanner.Plan(
                snapshot, true, 1, 0, false, null, false, true, null);
            AssertEqual(LlmDecisionWindowRoute.CpuFallback, remotePlan.Route,
                "remote confirm routes to CPU");

            DecisionSnapshot yesNoSnapshot = LegalActionExtractor.Extract(
                query, 186, DuelViewType.RunDialog, 1);
            LegalActionExtractor.ApplyViewContext(
                yesNoSnapshot, (int)DuelDialogType.YesNo, 0, 0);
            AssertEqual(false, LegalActionExtractor.IsDialogWithoutChoice(
                query, (int)DuelDialogType.YesNo),
                "yes/no dialog is an actionable binary prompt");
            AssertEqual(2, yesNoSnapshot.LegalActions.Count, "flagless yes/no action count");
        }

        static void LeavesEffectDialogWithoutEngineChoicesOnNativeDefaultPath()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 185, DuelViewType.RunDialog, 1);

            LegalActionExtractor.ApplyViewContext(
                snapshot, (int)DuelDialogType.Effect, 1, 40);

            AssertEqual(true, LegalActionExtractor.IsDialogWithoutChoice(
                query, (int)DuelDialogType.Effect),
                "effect presentation without choices uses native default");
            AssertEqual(0, snapshot.LegalActions.Count, "effect action count");
        }

        static void AddsDeclineActionToCheckChainWaitInput()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 7)] = 1;
            query.CommandMasks[Key(1, 7, 0)] =
                (uint)(1 << (int)DuelCommandType.Action);
            query.CardUniqueIds[Key(1, 7, 0)] = 701;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 187, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.CheckChain,
                0,
                (int)DuelMenuParamType.TrueCancel);

            AssertEqual(2, snapshot.LegalActions.Count, "check-chain action count");
            AssertEqual(LegalActionKind.Command, snapshot.LegalActions[0].Kind,
                "activation kind");
            AssertEqual(LegalActionKind.Cancel, snapshot.LegalActions[1].Kind,
                "decline kind");
            AssertEqual(false, snapshot.LegalActions[1].CancelDecide,
                "decline response is not decide");
            AssertEqual("Decline response", snapshot.LegalActions[1].ActionLabel,
                "decline label");
            AssertEqual(true, snapshot.IsStrategicWindow, "check-chain strategic window");
        }

        static void AddsDeclineActionToCheckTimingWaitInput()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 7)] = 1;
            query.CommandMasks[Key(1, 7, 0)] =
                (uint)(1 << (int)DuelCommandType.Action);
            query.CardUniqueIds[Key(1, 7, 0)] = 702;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 189, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.CheckTiming,
                0,
                (int)DuelMenuParamType.TrueCancel);

            AssertEqual(2, snapshot.LegalActions.Count, "check-timing action count");
            AssertEqual(LegalActionKind.Command, snapshot.LegalActions[0].Kind,
                "timing activation kind");
            AssertEqual(LegalActionKind.Cancel, snapshot.LegalActions[1].Kind,
                "timing decline kind");
            AssertEqual(false, snapshot.LegalActions[1].CancelDecide,
                "timing decline is not decide");
            AssertEqual("Decline response", snapshot.LegalActions[1].ActionLabel,
                "timing decline label");
            AssertEqual(true, snapshot.IsStrategicWindow, "check-timing strategic window");
        }

        static void DoesNotAddDeclineToForcedCheckChainWaitInput()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 7)] = 1;
            query.CommandMasks[Key(1, 7, 0)] =
                (uint)(1 << (int)DuelCommandType.Action);

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 188, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.CheckChain,
                0,
                (int)DuelMenuParamType.Force);

            AssertEqual(1, snapshot.LegalActions.Count, "forced check-chain action count");
            AssertEqual(LegalActionKind.Command, snapshot.LegalActions[0].Kind,
                "forced activation kind");
        }

        static void EmptyCancellableCheckChainWaitInputAddsMechanicalDeclineAndAutoCommits()
        {
            // Live hang (seq 650+): WaitInput params (5,0,2) =
            // MenuActType.CheckChain + MenuParamType.TrueCancel, zero extractable
            // activations. NativeDefault loops; must expose a sole mechanical decline
            // and automatic-route Commit CancelCommand2(false).
            AssertEqual(5, (int)DuelMenuActType.CheckChain, "CheckChain ordinal");
            AssertEqual(2, (int)DuelMenuParamType.TrueCancel, "TrueCancel ordinal");

            FakeLegalActionQuery query = new FakeLegalActionQuery();
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 650, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.CheckChain,
                0,
                (int)DuelMenuParamType.TrueCancel);

            AssertEqual(1, snapshot.LegalActions.Count, "empty check-chain sole decline");
            AssertEqual(LegalActionKind.Cancel, snapshot.LegalActions[0].Kind, "decline kind");
            AssertEqual(false, snapshot.LegalActions[0].CancelDecide, "CancelCommand2(false)");
            AssertEqual(true, snapshot.LegalActions[0].IsMechanical, "empty decline is mechanical");
            AssertEqual(false, snapshot.IsStrategicWindow, "not a broker strategic window");
            AssertEqual("mechanical_only", snapshot.StrategicWindowReason, "mechanical reason");

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                1,
                true,
                null,
                false,
                true,
                null);

            AssertEqual(LlmDecisionWindowRoute.Automatic, plan.Route,
                "empty check-chain routes automatic");
            LegalAction commit = LlmDecisionWindowPlanner.ResolveAutomaticActionForCommit(
                plan, null);
            AssertEqual(false, commit == null, "automatic decline selected");
            AssertEqual(LegalActionKind.Cancel, commit.Kind, "commit cancel kind");
            AssertEqual(false, commit.CancelDecide, "commit cancel decide false");

            // Also TrueCancel-equivalent cancel families with empty chain.
            DecisionSnapshot onlyCancel = LegalActionExtractor.Extract(
                query, 651, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                onlyCancel, query,
                (int)DuelMenuActType.CheckChain, 0, (int)DuelMenuParamType.OnlyCancel);
            AssertEqual(1, onlyCancel.LegalActions.Count, "OnlyCancel empty decline");
            AssertEqual(true, onlyCancel.LegalActions[0].IsMechanical, "OnlyCancel mechanical");
        }

        static void EmptyCheckChainDeclineCommitPlanUsesCancelNotDecide()
        {
            LegalAction decline = new LegalAction()
            {
                Kind = LegalActionKind.Cancel,
                CancelDecide = false,
                IsMechanical = true,
                ActionLabel = "Decline empty response window",
            };
            LlmActionCommitPlan plan = LlmActionCommitPlan.FromLegalAction(decline);
            AssertEqual(LlmActionCommitKind.Cancel, plan.Kind, "commit kind cancel");
            AssertEqual(false, plan.CancelDecide, "does not force decide");
        }

        static void EmptyCancellableCheckTimingWaitInputAddsMechanicalDeclineAndAutoCommits()
        {
            AssertEqual(4, (int)DuelMenuActType.CheckTiming, "CheckTiming ordinal");
            AssertEqual(2, (int)DuelMenuParamType.TrueCancel, "TrueCancel ordinal");

            FakeLegalActionQuery query = new FakeLegalActionQuery();
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 994, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.CheckTiming,
                0,
                (int)DuelMenuParamType.TrueCancel);

            AssertEqual(1, snapshot.LegalActions.Count, "empty check-timing sole decline");
            AssertEqual(LegalActionKind.Cancel, snapshot.LegalActions[0].Kind, "decline kind");
            AssertEqual(false, snapshot.LegalActions[0].CancelDecide, "CancelCommand2(false)");
            AssertEqual(true, snapshot.LegalActions[0].IsMechanical, "empty decline is mechanical");
            AssertEqual("empty_check_timing_decline", snapshot.LegalActions[0].TargetScope,
                "check-timing scope");

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot, true, 1, 1, true, null, false, true, null);
            AssertEqual(LlmDecisionWindowRoute.Automatic, plan.Route,
                "empty check-timing routes automatic");
            AssertEqual("empty_check_timing_decline", plan.Reason,
                "check-timing route reason");
        }

        static void SerializesStuckWindowRecoveryAsJsonLine()
        {
            DecisionSnapshot snapshot = CreateCheckTimingSnapshot(1000);
            LlmDecisionWindowPlan plan = new LlmDecisionWindowPlan()
            {
                Route = LlmDecisionWindowRoute.CpuFallback,
                PromptFamily = LlmPromptFamily.WaitInput,
                Reason = "no_actions",
            };
            LlmStuckWindowObservation observation = new LlmStuckWindowObservation()
            {
                ShouldRecover = true,
                RepeatCount = 3,
                Fingerprint = "WaitInput|4|0|1|CpuFallback|no_actions",
                RouteHistory = new List<string>() { "994:a", "997:a", "1000:a" },
            };

            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeStuckWindowRecovered(
                    snapshot, plan, observation));
            AssertEqual("llm_broker_stuck_window_recovered", data["kind"], "event kind");
            AssertEqual((long)1000, data["run_effect_seq"], "recovery seq");
            AssertEqual((long)3, data["repeat_count"], "repeat count");
            AssertEqual("WaitInput|4|0|1|CpuFallback|no_actions", data["fingerprint"],
                "logical fingerprint");
            AssertEqual(3, ((List<object>)data["route_history"]).Count, "route history count");
        }

        static void ExtractsOpponentEffectTargetsAsStrategicActions()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 0)] = 1;
            query.CardNums[Key(0, 2)] = 1;
            query.CommandMasks[Key(1, 0, 0)] = (uint)(1 << (int)DuelCommandType.Decide);
            query.CommandMasks[Key(0, 2, 0)] = (uint)(1 << (int)DuelCommandType.Decide);
            query.CardUniqueIds[Key(1, 0, 0)] = 301;
            query.CardUniqueIds[Key(0, 2, 0)] = 302;
            query.CardIdsByUniqueId[301] = 6413;
            query.CardIdsByUniqueId[302] = 12483;
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[6413] = new LlmCardMetadata() { CardId = 6413, Name = "Jerry Beans Man" };
            catalog.Cards[12483] = new LlmCardMetadata() { CardId = 12483, Name = "Duza" };

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 183, DuelViewType.WaitInput, 1, catalog);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.LockOn,
                12483,
                0,
                catalog);

            AssertEqual(2, snapshot.LegalActions.Count, "opponent effect target count");
            AssertEqual(0, snapshot.LegalActions[0].Player, "first target player");
            AssertEqual(1, snapshot.LegalActions[1].Player, "second target player");
            AssertEqual("effect_target", snapshot.LegalActions[0].TargetScope,
                "first target scope");
            AssertEqual("Duza", snapshot.LegalActions[0].Card.Name, "first target card");
            AssertEqual("Jerry Beans Man", snapshot.LegalActions[1].Card.Name,
                "second target card");
            AssertEqual(false, snapshot.LegalActions[0].IsMechanical,
                "multiple effect targets are strategic");
            AssertEqual(true, snapshot.IsStrategicWindow, "effect target window is strategic");
        }

        static void ExtractsSingleOpponentEffectTargetAsAutomaticAction()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(0, 2)] = 1;
            query.CommandMasks[Key(0, 2, 0)] = (uint)(1 << (int)DuelCommandType.Decide);
            query.CardUniqueIds[Key(0, 2, 0)] = 302;

            LegalAction automaticAction;
            bool extracted = LegalActionExtractor.TryExtractAutomaticAction(
                query, DuelViewType.WaitInput, 1, out automaticAction);

            AssertEqual(true, extracted, "single opponent effect target extracted");
            AssertEqual(0, automaticAction.Player, "target player");
            AssertEqual(2, automaticAction.Position, "target position");
            AssertEqual(DuelCommandType.Decide, automaticAction.Command, "target command");
            AssertEqual("effect_target", automaticAction.TargetScope, "target scope");
        }

        static void ExtractsSummonPlacementActionsFromPositionMask()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.SummoningMonsterUniqueId = 33;
            query.SummonPositionMask = (1 << 1) | (1 << 2);
            query.CardIdsByUniqueId[33] = 4927;
            query.MovablePhaseMask = (uint)(1 << (int)DuelPhase.End);

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 132, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.Location,
                33,
                (int)DuelMenuParamType.Cancel);

            AssertEqual(2, snapshot.LegalActions.Count, "summon placement action count");
            AssertCommand(snapshot.LegalActions[0], 0, 1, 1, 0, DuelCommandType.Decide, 33);
            AssertCommand(snapshot.LegalActions[1], 1, 1, 2, 0, DuelCommandType.Decide, 33);
            AssertEqual(4927, snapshot.LegalActions[0].CardId, "first placement card id");
            AssertEqual(4927, snapshot.LegalActions[1].CardId, "second placement card id");
        }

        static void IgnoresStaleSummonPlacementMetadataOutsideLocationPrompt()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.SummoningMonsterUniqueId = 83;
            query.SummonPositionMask = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3);
            query.CardIdsByUniqueId[83] = 9760;
            query.MovablePhaseMask = (uint)(1 << (int)DuelPhase.End);

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 964, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.MainPhase,
                0,
                (int)DuelMenuParamType.TrueCancel);

            AssertEqual(1, snapshot.LegalActions.Count, "ordinary main phase action count");
            AssertEqual(LegalActionKind.MovePhase, snapshot.LegalActions[0].Kind,
                "stale summon metadata does not replace phase actions");
            AssertEqual(DuelPhase.End, snapshot.LegalActions[0].Phase,
                "end phase remains available");
            AssertEqual(false,
                LlmAutomaticActionLoopGuard.IsSummonPlacementAction(snapshot.LegalActions[0]),
                "ordinary main phase action is not summon placement");
        }

        static void ExtractsAttackTargetActionsFromTargetMask()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CurrentPhase = (int)DuelPhase.Battle;
            query.CardNums[Key(0, 2)] = 1;
            query.CardNums[Key(0, 4)] = 1;
            query.CardUniqueIds[Key(0, 2, 0)] = 701;
            query.CardUniqueIds[Key(0, 4, 0)] = 702;
            query.AttackTargetMasks[Key(1, 0)] = (1 << 2) | (1 << 4);

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query,
                131,
                DuelViewType.WaitInput,
                1,
                new AttackTargetContext() { AttackingPlayer = 1, AttackerPosition = 0 });

            AssertEqual(2, snapshot.LegalActions.Count, "attack target action count");
            AssertCommand(snapshot.LegalActions[0], 0, 0, 2, 0, DuelCommandType.Decide, 0);
            AssertCommand(snapshot.LegalActions[1], 1, 0, 4, 0, DuelCommandType.Decide, 0);
        }

        static void ExtractsSingleAttackTargetAsAutomaticAction()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CurrentPhase = (int)DuelPhase.Battle;
            query.CardNums[Key(0, 2)] = 1;
            query.CardUniqueIds[Key(0, 2, 0)] = 701;
            query.AttackTargetMasks[Key(1, 0)] = 1 << 2;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query,
                131,
                DuelViewType.WaitInput,
                1,
                new AttackTargetContext() { AttackingPlayer = 1, AttackerPosition = 0 });
            LegalAction automaticAction;
            bool extracted = LegalActionExtractor.TryExtractAutomaticAction(
                query,
                DuelViewType.WaitInput,
                1,
                new AttackTargetContext() { AttackingPlayer = 1, AttackerPosition = 0 },
                out automaticAction);

            AssertEqual(0, snapshot.LegalActions.Count, "single attack target hidden from broker");
            AssertEqual(true, extracted, "automatic attack target extracted");
            AssertCommand(automaticAction, 0, 0, 2, 0, DuelCommandType.Decide, 0);
        }

        static void RoutesStrategicAttackTargetsThroughBrokerGate()
        {
            DecisionSnapshot snapshot = CreateAttackTargetSnapshot(
                539, 534, 7, 701, 702, 1, 0);

            AssertEqual(false, LlmDecisionWindowPlanner.RequiresCpuFallback(snapshot),
                "strategic attack target is not generic selection fallback");
            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                1,
                true,
                LlmBrokerRequestGateDecision.StartRequest,
                false,
                false,
                null);
            AssertEqual(LlmDecisionWindowRoute.Broker, plan.Route,
                "multiple attack targets remain broker-owned");

            DecisionSnapshot unrelated = new DecisionSnapshot()
            {
                RunEffectSeq = 540,
                ViewType = DuelViewType.WaitInput,
                ViewParam1 = (int)DuelMenuActType.Selection,
                ActingPlayer = 1,
                ControlledPlayer = 1,
            };
            AssertEqual(true, LlmDecisionWindowPlanner.RequiresCpuFallback(unrelated),
                "unrelated selection remains temporary CPU");
        }

        static void SerializesPublicSafeAttackTargetContinuationContext()
        {
            DecisionSnapshot snapshot = CreateAttackTargetSnapshot(
                539, 534, 7, 701, 702, 1, 0);

            AssertEqual(2, snapshot.LegalActions.Count, "attack target count");
            LegalAction faceUp = snapshot.LegalActions[0];
            LegalAction faceDown = snapshot.LegalActions[1];
            AssertEqual("attack_target", faceUp.TargetScope, "face-up target scope");
            AssertEqual("Attack target: Duza the Meteor Cubic Vessel", faceUp.ActionLabel,
                "face-up target label");
            AssertEqual(12801, faceUp.CardId, "face-up target card id");
            AssertEqual("Attack target: face-down monster in zone 4", faceDown.ActionLabel,
                "face-down target label");
            AssertEqual(0, faceDown.CardId, "face-down target card id redacted");
            AssertEqual(0, faceDown.CardUniqueId, "face-down target unique id redacted");

            string json = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            Dictionary<string, object> root = DeserializeObject(json);
            Dictionary<string, object> origin =
                root["interaction_origin"] as Dictionary<string, object>;
            AssertEqual("attack", origin["kind"], "interaction origin kind");
            AssertNumber(534, origin["run_effect_seq"], "origin run_effect_seq");
            AssertEqual("Attack: Chaosrider Gustaph", origin["action_label"],
                "origin action label");
            AssertEqual("Attack Duza to remove the public threat.", origin["reason"],
                "origin provider reason");
            AssertEqual(false, json.Contains("702"),
                "face-down target unique id absent from request");
        }

        static void HidesFaceDownAttackTargetIdentityFromRequestBytes()
        {
            DecisionSnapshot first = CreateAttackTargetSnapshot(
                539, 534, 7, 701, 702, 1, 0);
            DecisionSnapshot second = CreateAttackTargetSnapshot(
                539, 534, 7, 701, 9902, 1, 0);

            string firstJson = LlmBrokerProtocol.SerializeDecisionRequest(first);
            string secondJson = LlmBrokerProtocol.SerializeDecisionRequest(second);
            AssertEqual(firstJson, secondJson,
                "hidden target pair produces byte-identical broker request");
        }

        static void RejectsAttackTargetTokenFromDifferentInteractionLease()
        {
            DecisionSnapshot expected = CreateAttackTargetSnapshot(
                539, 534, 7, 701, 702, 1, 0);
            DecisionSnapshot fresh = CreateAttackTargetSnapshot(
                539, 535, 7, 701, 702, 1, 0);

            AssertEqual(false,
                LlmBrokerProtocol.IsSameAction(fresh.LegalActions[0], expected.LegalActions[0]),
                "target token binds action to originating attack lease");
        }

        static void SerializesAttackTargetFallbackDivergence()
        {
            DecisionSnapshot snapshot = CreateAttackTargetSnapshot(
                539, 534, 7, 701, 702, 1, 0);
            string json = LlmDecisionLogSerializer.SerializeAttackTargetDivergence(
                snapshot,
                "provider_error",
                "temporary_cpu");
            Dictionary<string, object> data = DeserializeObject(json);

            AssertEqual("llm_attack_target_divergence", data["kind"], "kind");
            AssertNumber(539, data["run_effect_seq"], "run_effect_seq");
            AssertNumber(534, data["origin_run_effect_seq"], "origin_run_effect_seq");
            AssertEqual("provider_error", data["reason"], "reason");
            AssertEqual("temporary_cpu", data["fallback"], "fallback");
            AssertNumber(2, data["legal_target_count"], "legal_target_count");
        }

        static void DoesNotHeuristicallySubstituteAttackTargetDuringRecovery()
        {
            DecisionSnapshot snapshot = CreateAttackTargetSnapshot(
                539, 534, 7, 701, 702, 1, 0);
            LegalAction selected;
            string policy;
            bool recovered = LlmBrokerRecovery.TrySelectAction(
                snapshot,
                "provider_error",
                null,
                out selected,
                out policy);

            AssertEqual(false, recovered,
                "provider failure cannot silently choose the first attack target");
            AssertEqual(null, selected, "no substituted attack target");
        }

        static DecisionSnapshot CreateAttackTargetSnapshot(
            ulong currentSeq,
            ulong originSeq,
            int duelGeneration,
            int faceUpUniqueId,
            int faceDownUniqueId,
            int faceUpFace,
            int faceDownFace)
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CurrentPhase = (int)DuelPhase.Battle;
            query.CardNums[Key(0, 2)] = 1;
            query.CardNums[Key(0, 4)] = 1;
            query.CardUniqueIds[Key(0, 2, 0)] = faceUpUniqueId;
            query.CardUniqueIds[Key(0, 4, 0)] = faceDownUniqueId;
            query.CardFaces[Key(0, 2, 0)] = faceUpFace;
            query.CardFaces[Key(0, 4, 0)] = faceDownFace;
            query.CardIdsByUniqueId[faceUpUniqueId] = 12801;
            query.CardIdsByUniqueId[faceDownUniqueId] = 99999;
            query.AttackTargetMasks[Key(1, 0)] = (1 << 2) | (1 << 4);
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[12801] = new LlmCardMetadata()
            {
                CardId = 12801,
                Name = "Duza the Meteor Cubic Vessel",
            };
            catalog.Cards[99999] = new LlmCardMetadata()
            {
                CardId = 99999,
                Name = "Hidden Sentinel",
            };

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query,
                currentSeq,
                DuelViewType.WaitInput,
                1,
                LegalActionExtractor.DefaultPosNum,
                catalog,
                new AttackTargetContext()
                {
                    AttackingPlayer = 1,
                    AttackerPosition = 0,
                    OriginRunEffectSeq = originSeq,
                    OriginDuelGeneration = duelGeneration,
                    AttackerCardId = 5825,
                    AttackerUniqueId = 501,
                    OriginActionLabel = "Attack: Chaosrider Gustaph",
                    OriginReason = "Attack Duza to remove the public threat.",
                    OriginPlan = "Clear Duza before Main Phase 2.",
                },
                LlmPublicVisibilityMode.RuntimeDllField);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.Selection,
                0,
                0,
                catalog);
            return snapshot;
        }

        static void DoesNotExtractAttackTargetsWithoutPendingAttackContext()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CurrentPhase = (int)DuelPhase.Battle;
            query.CardNums[Key(0, 2)] = 1;
            query.CardUniqueIds[Key(0, 2, 0)] = 701;
            query.AttackTargetMasks[Key(1, 0)] = 1 << 2;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 208, DuelViewType.WaitInput, 1);
            LegalAction automaticAction;
            bool extracted = LegalActionExtractor.TryExtractAutomaticAction(
                query, DuelViewType.WaitInput, 1, out automaticAction);

            AssertEqual(0, snapshot.LegalActions.Count, "stale attack target action count");
            AssertEqual(false, extracted, "stale attack target automatic action");
        }

        static void DoesNotExtractAttackTargetsWhenStandardBattleActionsExist()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CurrentPhase = (int)DuelPhase.Battle;
            query.MovablePhaseMask = (uint)(1 << (int)DuelPhase.Main2);
            query.CardNums[Key(1, 0)] = 1;
            query.CardUniqueIds[Key(1, 0, 0)] = 901;
            query.CommandMasks[Key(1, 0, 0)] = (uint)(1 << (int)DuelCommandType.Attack);
            query.CardNums[Key(0, 2)] = 1;
            query.CardNums[Key(0, 4)] = 1;
            query.AttackTargetMasks[Key(1, 0)] = (1 << 2) | (1 << 4);

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 126, DuelViewType.WaitInput, 1);

            AssertEqual(2, snapshot.LegalActions.Count, "standard battle action count");
            AssertPhase(snapshot.LegalActions[0], 0, DuelPhase.Main2);
            AssertCommand(snapshot.LegalActions[1], 1, 1, 0, 0, DuelCommandType.Attack, 901);
        }

        static void CardCatalogLoadsLazilyAndNormalizesText()
        {
            int loadCount = 0;
            LlmCardCatalog catalog = new LlmCardCatalog(
                () =>
                {
                    loadCount++;
                    return new Dictionary<int, LlmCardMetadata>()
                    {
                        {
                            4900,
                            new LlmCardMetadata()
                            {
                                CardId = 4900,
                                Name = "Cubic Seed",
                                Text = "Line one\r\n   line two   line three",
                            }
                        }
                    };
                },
                18);

            LlmCardMetadata first = catalog.GetCard(4900);
            LlmCardMetadata second = catalog.GetCard(4900);

            AssertEqual(1, loadCount, "catalog load count");
            AssertEqual("Cubic Seed", first.Name, "card name");
            AssertEqual("Line one line two...", first.Text, "normalized text");
            AssertEqual("Line one line two...", second.Text, "cached normalized text");
        }

        static void CardCatalogReturnsNullWhenLoadingFails()
        {
            LlmCardCatalog catalog = new LlmCardCatalog(
                () =>
                {
                    throw new InvalidOperationException("missing files");
                },
                80);

            AssertEqual(null, catalog.GetCard(4900), "missing catalog card");
        }

        static void CardDataResolverSkipsMissingBaseDirAndUsesValidCandidate()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ygomaster-llm-carddata-" + Guid.NewGuid().ToString("N"));
            try
            {
                string missingBaseDir = Path.Combine(tempRoot, "missing");
                string validBaseDir = Path.Combine(tempRoot, "valid");
                string validDataDir = Path.Combine(validBaseDir, "Data");
                CreateRequiredCardDataFiles(validDataDir);

                string resolvedDataDir = LlmCardDataDirectoryResolver.ResolveClientDataDirectory(
                    new string[] { missingBaseDir, validBaseDir });

                AssertEqual(
                    Path.GetFullPath(validDataDir),
                    Path.GetFullPath(resolvedDataDir),
                    "resolved card data directory");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, true);
                    }
                }
                catch
                {
                }
            }
        }

        static void CreateRequiredCardDataFiles(string dataDir)
        {
            string cardDataDir = Path.Combine(dataDir, "CardData");
            string hashDir = Path.Combine(cardDataDir, "#");
            string textDir = Path.Combine(cardDataDir, "en-US");
            Directory.CreateDirectory(hashDir);
            Directory.CreateDirectory(textDir);
            File.WriteAllBytes(Path.Combine(hashDir, "CARD_Prop.bytes"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(textDir, "CARD_Indx.bytes"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(textDir, "CARD_Name.bytes"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(textDir, "CARD_Desc.bytes"), new byte[] { 0 });
        }

        static void ExtractsIndexZeroCommandWhenCardNumIsZero()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(0, 0)] = 0;
            query.CommandMasks[Key(0, 0, 0)] = (uint)(1 << (int)DuelCommandType.Attack);
            query.CardUniqueIds[Key(0, 0, 0)] = 991;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 11, DuelViewType.WaitInput, 0);

            AssertEqual(1, snapshot.LegalActions.Count, "expected index zero command action");
            AssertCommand(snapshot.LegalActions[0], 0, 0, 0, 0, DuelCommandType.Attack, 991);
        }

        static void IgnoresEmptyCommandAndPhaseMasks()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(0, 13)] = 2;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 1, DuelViewType.WaitInput, 0);

            AssertEqual(0, snapshot.LegalActions.Count, "expected no actions for empty masks");
        }

        static void SerializesDecisionWindowAsJsonLine()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.MovablePhaseMask = (uint)(1 << (int)DuelPhase.Battle);
            query.CardNums[Key(0, 13)] = 1;
            query.CommandMasks[Key(0, 13, 0)] = (uint)(1 << (int)DuelCommandType.Attack);
            query.CardUniqueIds[Key(0, 13, 0)] = 12031;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 42, DuelViewType.WaitInput, 0);
            snapshot.ViewParam1 = 7;
            snapshot.ViewParam2 = 8;
            snapshot.ViewParam3 = 9;
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot));
            List<object> actions = (List<object>)data["legal_actions"];

            AssertEqual("decision_window", data["kind"], "kind");
            AssertEqual((long)4, data["schema_version"], "schema_version");
            AssertEqual((long)42, data["run_effect_seq"], "run_effect_seq");
            AssertEqual("WaitInput", data["view_type"], "view_type");
            AssertEqual((long)7, data["view_param1"], "view_param1");
            AssertEqual((long)8, data["view_param2"], "view_param2");
            AssertEqual((long)9, data["view_param3"], "view_param3");
            AssertEqual((long)0, data["acting_player"], "acting_player");
            AssertEqual((long)0, data["controlled_player"], "controlled_player");
            AssertEqual(true, data.ContainsKey("public_state"), "public_state present");
            AssertEqual(2, actions.Count, "legal_actions count");
            AssertEqual("move_phase", ((Dictionary<string, object>)actions[0])["kind"], "first action kind");
            AssertEqual("command", ((Dictionary<string, object>)actions[1])["kind"], "second action kind");
        }

        static void SerializesCommittedCommandAsJsonLine()
        {
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeCommittedCommand(
                    9, 1, 13, 2, (int)DuelCommandType.Set));

            AssertEqual("committed_action", data["kind"], "kind");
            AssertEqual("command", data["action_type"], "action_type");
            AssertEqual((long)9, data["run_effect_seq"], "run_effect_seq");
            AssertEqual((long)1, data["player"], "player");
            AssertEqual((long)13, data["position"], "position");
            AssertEqual((long)2, data["index"], "index");
            AssertEqual("Set", data["command"], "command");
            AssertEqual((long)(int)DuelCommandType.Set, data["command_id"], "command_id");
        }

        static void SerializesCommittedPhaseAsJsonLine()
        {
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeCommittedPhase(8, (int)DuelPhase.End));

            AssertEqual("committed_action", data["kind"], "kind");
            AssertEqual("move_phase", data["action_type"], "action_type");
            AssertEqual((long)8, data["run_effect_seq"], "run_effect_seq");
            AssertEqual("End", data["phase"], "phase");
            AssertEqual((long)(int)DuelPhase.End, data["phase_id"], "phase_id");
        }

        static void SerializesBrokerCommittedActionAsJsonLine()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            LegalAction action = snapshot.LegalActions[1];
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerCommittedAction(
                    42,
                    43,
                    new LlmBrokerDecisionResponse()
                    {
                        RunEffectSeq = 42,
                        ActionId = 1,
                        Reason = "summon attacker",
                        Confidence = 0.75,
                        Plan = "develop the board",
                        WhyNow = "normal summon before ending main phase",
                        AlternativesConsidered = new List<string>()
                        {
                            "attack is unavailable before summoning",
                            "ending now gives up pressure",
                        },
                        Risk = "summoned monster may be removed",
                    },
                    action));
            Dictionary<string, object> serializedAction =
                (Dictionary<string, object>)data["action"];

            AssertEqual("llm_broker_committed", data["kind"], "kind");
            AssertEqual((long)42, data["request_run_effect_seq"], "request_run_effect_seq");
            AssertEqual((long)43, data["commit_run_effect_seq"], "commit_run_effect_seq");
            AssertEqual((long)1, data["action_id"], "action_id");
            AssertEqual("summon attacker", data["reason"], "reason");
            AssertEqual(0.75, Convert.ToDouble(data["confidence"]), "confidence");
            AssertEqual("develop the board", data["plan"], "plan");
            AssertEqual("normal summon before ending main phase", data["why_now"], "why_now");
            AssertEqual(
                "attack is unavailable before summoning",
                ((List<object>)data["alternatives_considered"])[0],
                "alternatives_considered[0]");
            AssertEqual("summoned monster may be removed", data["risk"], "risk");
            AssertEqual("command", data["action_type"], "action_type");
            AssertEqual("command", serializedAction["kind"], "action kind");
            AssertEqual((long)1, serializedAction["action_id"], "serialized action_id");
            AssertEqual("Attack", serializedAction["command"], "serialized command");
        }

        static void SerializesBrokerRequestStartedAsJsonLine()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerRequestStarted(snapshot));

            AssertEqual("llm_broker_request_started", data["kind"], "kind");
            AssertEqual((long)42, data["request_run_effect_seq"], "request_run_effect_seq");
            AssertEqual("WaitInput", data["view_type"], "view_type");
            AssertEqual((long)0, data["acting_player"], "acting_player");
            AssertEqual((long)0, data["turn_player"], "turn_player");
            AssertEqual((long)2, data["legal_action_count"], "legal_action_count");
        }

        static void SerializesBrokerResponseAsJsonLine()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            LlmBrokerDecisionResponse response = new LlmBrokerDecisionResponse()
            {
                RunEffectSeq = 42,
                ActionId = 1,
                Reason = "attack",
                Confidence = 0.61,
                Plan = "pressure life points",
                WhyNow = "battle phase is open and the opponent has no blockers",
                AlternativesConsidered = new List<string>() { "End Phase misses damage" },
                Risk = "attack trigger could punish direct pressure",
            };
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerResponse(
                    42,
                    42,
                    LlmBrokerDecisionResult.Success("{}", "{\"action_id\":1}", response, snapshot.LegalActions[1])));

            AssertEqual("llm_broker_response", data["kind"], "kind");
            AssertEqual((long)42, data["request_run_effect_seq"], "request_run_effect_seq");
            AssertEqual((long)42, data["current_run_effect_seq"], "current_run_effect_seq");
            AssertEqual(true, data["success"], "success");
            AssertEqual((long)1, data["action_id"], "action_id");
            AssertEqual("attack", data["reason"], "reason");
            AssertEqual(0.61, Convert.ToDouble(data["confidence"]), "confidence");
            AssertEqual("pressure life points", data["plan"], "plan");
            AssertEqual(
                "battle phase is open and the opponent has no blockers",
                data["why_now"],
                "why_now");
            AssertEqual(
                "End Phase misses damage",
                ((List<object>)data["alternatives_considered"])[0],
                "alternatives_considered[0]");
            AssertEqual("attack trigger could punish direct pressure", data["risk"], "risk");
            AssertEqual("{}", data["request_json"], "request_json");
            AssertEqual("{\"action_id\":1}", data["response_json"], "response_json");
        }

        static void SerializesBrokerFailureAsJsonLine()
        {
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerResponse(
                    42,
                    44,
                    LlmBrokerDecisionResult.Failure(
                        "provider_error",
                        "{}",
                        "{\"error\":\"provider_error\"}",
                        "timeout")));

            AssertEqual("llm_broker_response", data["kind"], "kind");
            AssertEqual(false, data["success"], "success");
            AssertEqual("provider_error", data["error"], "error");
            AssertEqual("timeout", data["error_detail"], "error_detail");
            AssertEqual((long)44, data["current_run_effect_seq"], "current_run_effect_seq");
            AssertEqual("{}", data["request_json"], "request_json");
            AssertEqual("{\"error\":\"provider_error\"}", data["response_json"], "response_json");
        }

        static void SerializesBrokerCommitSkippedAsJsonLine()
        {
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerCommitSkipped(42, 44, "view changed"));

            AssertEqual("llm_broker_commit_skipped", data["kind"], "kind");
            AssertEqual((long)42, data["request_run_effect_seq"], "request_run_effect_seq");
            AssertEqual((long)44, data["current_run_effect_seq"], "current_run_effect_seq");
            AssertEqual("view changed", data["reason"], "reason");
        }

        static void SerializesBrokerRejectedActionAsJsonLine()
        {
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerRejectedAction(42, 44, "stale_run_effect_seq"));

            AssertEqual("llm_broker_rejected", data["kind"], "kind");
            AssertEqual((long)42, data["request_run_effect_seq"], "request_run_effect_seq");
            AssertEqual((long)44, data["current_run_effect_seq"], "current_run_effect_seq");
            AssertEqual("stale_run_effect_seq", data["error"], "error");
        }

        static void SerializesBrokerAutomaticActionAsJsonLine()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerAutomaticAction(
                    42,
                    "forced_draw",
                    snapshot.LegalActions[1]));
            Dictionary<string, object> action = (Dictionary<string, object>)data["action"];

            AssertEqual("llm_broker_automatic_action", data["kind"], "kind");
            AssertEqual((long)42, data["run_effect_seq"], "run_effect_seq");
            AssertEqual("forced_draw", data["reason"], "reason");
            AssertEqual("command", data["action_type"], "action_type");
            AssertEqual("command", action["kind"], "action kind");
        }

        static void SerializesWindowRoutedAsJsonLine()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            LegalAction automaticAction = new LegalAction() { Kind = LegalActionKind.Command };
            LlmDecisionWindowPlan plan = new LlmDecisionWindowPlan()
            {
                Route = LlmDecisionWindowRoute.Automatic,
                PromptFamily = LlmPromptFamily.WaitInput,
                Reason = "mechanical_window",
                Snapshot = snapshot,
                MyId = 1,
                AutomaticAction = automaticAction,
                HasActingPlayer = true,
                BrokerControlsActingPlayer = true,
            };

            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeWindowRouted(7, plan));

            AssertEqual("llm_broker_window_routed", data["kind"], "kind");
            AssertEqual("Automatic", data["route"], "route");
            AssertEqual("WaitInput", data["prompt_family"], "prompt_family");
            AssertEqual("mechanical_window", data["reason"], "reason");
            AssertEqual((long)7, data["run_effect_seq"], "run_effect_seq");
            AssertEqual((long)1, data["my_id"], "my_id");
            AssertEqual(true, data["has_automatic_action"], "has_automatic_action");
            AssertEqual((long)0, data["acting_player"], "acting_player");
            AssertEqual((long)0, data["controlled_player"], "controlled_player");
            AssertEqual((long)2, data["legal_action_count"], "legal_action_count");
            AssertEqual((long)2, data["strategic_action_count"], "strategic_action_count");
            AssertEqual((long)0, data["mechanical_action_count"], "mechanical_action_count");
        }

        static void SerializesUnsupportedWindowAsJsonLine()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 8,
                ViewType = DuelViewType.RunList,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                StrategicWindowReason = "unsupported_window",
            };
            LlmDecisionWindowPlan plan = new LlmDecisionWindowPlan()
            {
                Route = LlmDecisionWindowRoute.CpuFallback,
                PromptFamily = LlmPromptFamily.RunList,
                Reason = "unsupported_window",
                Snapshot = snapshot,
                MyId = 1,
                HasActingPlayer = true,
                BrokerControlsActingPlayer = true,
                IsUnsupportedWindow = true,
            };

            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerUnsupportedWindow(8, plan));

            AssertEqual("llm_broker_unsupported_window", data["kind"], "kind");
            AssertEqual((long)8, data["run_effect_seq"], "run_effect_seq");
            AssertEqual("RunList", data["prompt_family"], "prompt_family");
            AssertEqual("unsupported_window", data["reason"], "reason");
            AssertEqual((long)1, data["my_id"], "my_id");
            AssertEqual((long)1, data["acting_player"], "acting_player");
            AssertEqual((long)1, data["controlled_player"], "controlled_player");
        }

        static void SerializesBrokerDecisionRequest()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            Dictionary<string, object> data = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));

            AssertEqual("decision_request", data["kind"], "kind");
            AssertEqual((long)4, data["schema_version"], "schema_version");
            AssertEqual((long)42, data["run_effect_seq"], "run_effect_seq");
            AssertEqual("WaitInput", data["view_type"], "view_type");
            AssertEqual((long)0, data["acting_player"], "acting_player");
            AssertEqual((long)0, data["controlled_player"], "controlled_player");
            AssertEqual(2, ((List<object>)data["legal_actions"]).Count, "legal_actions count");
            AssertEqual(true, data.ContainsKey("duel_history"), "schema v4 duel_history present");
        }

        static void SerializesBrokerRequestStartedWithSnapshotDetails()
        {
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerRequestStarted(CreateSnapshotWithTwoActions()));

            AssertEqual("llm_broker_request_started", data["kind"], "kind");
            AssertEqual((long)42, data["request_run_effect_seq"], "request_run_effect_seq");
            AssertEqual("WaitInput", data["view_type"], "view_type");
            AssertEqual((long)2, data["legal_action_count"], "legal_action_count");
        }

        static void SerializesPublicStateInBrokerDecisionRequest()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.LifePoints[0] = 8000;
            query.LifePoints[1] = 6200;
            query.CardNums[Key(0, 13)] = 1;
            query.CardUniqueIds[Key(0, 13, 0)] = 101;
            query.HandCardOpen[Key(0, 0)] = 1;
            query.CardIdsByUniqueId[101] = 1111;
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 44, DuelViewType.WaitInput, 0);

            Dictionary<string, object> data = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));
            Dictionary<string, object> publicState = (Dictionary<string, object>)data["public_state"];
            List<object> players = (List<object>)publicState["players"];
            Dictionary<string, object> player0 = (Dictionary<string, object>)players[0];
            List<object> knownCards = (List<object>)player0["known_cards"];
            Dictionary<string, object> knownCard = (Dictionary<string, object>)knownCards[0];

            AssertEqual((long)4, data["schema_version"], "schema_version");
            AssertEqual((long)8000, player0["life_points"], "life_points");
            AssertEqual((long)1111, knownCard["card_id"], "known card id");
        }

        static void SerializesSchemaV3CardMetadataInBrokerDecisionRequest()
        {
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();

            Dictionary<string, object> data = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));
            List<object> actions = (List<object>)data["legal_actions"];
            Dictionary<string, object> action = (Dictionary<string, object>)actions[0];
            Dictionary<string, object> actionCard = (Dictionary<string, object>)action["card"];
            Dictionary<string, object> publicState = (Dictionary<string, object>)data["public_state"];
            Dictionary<string, object> player1 = (Dictionary<string, object>)((List<object>)publicState["players"])[1];
            Dictionary<string, object> knownCard =
                (Dictionary<string, object>)((List<object>)player1["known_cards"])[0];
            Dictionary<string, object> knownCardMetadata = (Dictionary<string, object>)knownCard["card"];

            AssertEqual((long)4, data["schema_version"], "schema_version");
            AssertEqual((long)1, data["controlled_player"], "controlled_player");
            AssertNumber(4900, action["card_id"], "action card id");
            AssertEqual("Cubic Seed", actionCard["name"], "action card name");
            AssertEqual("Starts the Cubic line.", actionCard["text"], "action card text");
            AssertNumber(4900, knownCardMetadata["card_id"], "known card metadata id");
            AssertEqual("Cubic Seed", knownCardMetadata["name"], "known card name");
        }

        static void SerializesSafeBoardContextInBrokerDecisionRequest()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.LifePoints[0] = 8000;
            query.LifePoints[1] = 6200;
            query.CardNums[Key(0, 0)] = 1;
            query.CardNums[Key(0, 16)] = 1;
            query.CardNums[Key(1, 0)] = 2;
            query.CardNums[Key(1, 16)] = 1;
            query.CardUniqueIds[Key(0, 0, 0)] = 701;
            query.CardUniqueIds[Key(0, 16, 0)] = 702;
            query.CardUniqueIds[Key(1, 16, 0)] = 703;
            query.CardIdsByUniqueId[701] = 5701;
            query.CardIdsByUniqueId[702] = 5702;
            query.CardIdsByUniqueId[703] = 5703;
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[5701] = CreateCard(5701, "Hidden Field Card", "Must not be exposed.");
            catalog.Cards[5702] = CreateCard(5702, "Known Grave Threat", "Public graveyard card.");
            catalog.Cards[5703] = CreateCard(5703, "Opponent Grave Card", "Also public.");

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 82, DuelViewType.WaitInput, 0, catalog);
            Dictionary<string, object> data = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));
            Dictionary<string, object> boardContext = (Dictionary<string, object>)data["board_context"];
            List<object> players = (List<object>)boardContext["players"];
            Dictionary<string, object> player0 = (Dictionary<string, object>)players[0];
            Dictionary<string, object> player1 = (Dictionary<string, object>)players[1];
            List<object> p0KnownGrave = (List<object>)player0["known_graveyard_cards"];
            Dictionary<string, object> p0KnownGraveCard = (Dictionary<string, object>)p0KnownGrave[0];

            AssertNumber(1, player0["field_count"], "p0 field count");
            AssertNumber(2, player1["field_count"], "p1 field count");
            AssertNumber(1, player0["graveyard_count"], "p0 grave count");
            AssertNumber(1, player1["graveyard_count"], "p1 grave count");
            AssertNumber(1, player0["known_graveyard_count"], "p0 known grave count");
            AssertEqual("Known Grave Threat", p0KnownGraveCard["name"], "known grave name");
            AssertEqual(false, MiniJSON.Json.Serialize(boardContext).Contains("Hidden Field Card"), "hidden field not exposed");
        }

        static void SerializesOpponentContextInBrokerDecisionRequest()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.LifePoints[0] = 8000;
            query.LifePoints[1] = 6200;
            query.CardNums[Key(0, 0)] = 1;
            query.CardNums[Key(0, 16)] = 1;
            query.CardNums[Key(1, 0)] = 2;
            query.CardUniqueIds[Key(0, 0, 0)] = 701;
            query.CardUniqueIds[Key(0, 16, 0)] = 702;
            query.CardIdsByUniqueId[701] = 5701;
            query.CardIdsByUniqueId[702] = 5702;
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[5701] = CreateCard(5701, "Hidden Field Card", "Must not be exposed.");
            catalog.Cards[5702] = CreateCard(5702, "Known Grave Threat", "Public graveyard card.");

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 83, DuelViewType.WaitInput, 1, catalog);
            Dictionary<string, object> data = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));
            Dictionary<string, object> opponentContext =
                (Dictionary<string, object>)data["opponent_context"];
            List<object> knownThreats = (List<object>)opponentContext["known_public_threats"];
            Dictionary<string, object> knownThreat =
                (Dictionary<string, object>)knownThreats[0];

            AssertNumber(0, opponentContext["opponent_player"], "opponent player");
            AssertNumber(1, opponentContext["field_count"], "opponent field count");
            AssertNumber(1, opponentContext["graveyard_count"], "opponent grave count");
            AssertEqual("Known Grave Threat", knownThreat["name"], "known public threat name");
            AssertEqual("opponent has 1 field card(s); identities unavailable", opponentContext["summary"], "opponent summary");
            AssertEqual(false, MiniJSON.Json.Serialize(opponentContext).Contains("Hidden Field Card"), "hidden field not exposed");
        }

        static void SerializesTurnMemoryInBrokerDecisionRequest()
        {
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();
            snapshot.TurnMemory.RecentActions.Add(new LlmRecentActionMemory()
            {
                Turn = 3,
                Phase = (int)DuelPhase.Main1,
                ActionType = "command",
                ActionLabel = "Summon Cubic Seed",
                CardId = 4900,
                CardName = "Cubic Seed",
                Reason = "Normal Summon Cubic Seed.",
                Plan = "Develop a monster.",
            });
            snapshot.TurnMemory.CardsUsedThisTurn.Add(new LlmUsedCardMemory()
            {
                CardId = 4900,
                Name = "Cubic Seed",
            });
            snapshot.TurnMemory.NormalSummonUsed = true;
            snapshot.TurnMemory.PhasePlan = "develop_board";

            Dictionary<string, object> data = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));
            Dictionary<string, object> memory = (Dictionary<string, object>)data["turn_memory"];
            List<object> recentActions = (List<object>)memory["recent_actions"];
            Dictionary<string, object> recentAction = (Dictionary<string, object>)recentActions[0];
            List<object> cardsUsed = (List<object>)memory["cards_used_this_turn"];
            Dictionary<string, object> usedCard = (Dictionary<string, object>)cardsUsed[0];

            AssertEqual("develop_board", memory["phase_plan"], "phase plan");
            AssertEqual(true, memory["normal_summon_used"], "normal summon used");
            AssertEqual("Summon Cubic Seed", recentAction["action_label"], "recent action label");
            AssertEqual("Cubic Seed", usedCard["name"], "used card name");
        }

        static void ClassifiesStrategicWindowAndSerializesActionSemantics()
        {
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();
            LegalAction action = snapshot.LegalActions[0];

            AssertEqual(true, snapshot.IsStrategicWindow, "strategic window");
            AssertEqual("strategic_choices", snapshot.StrategicWindowReason, "strategic reason");
            AssertEqual(1, snapshot.StrategicActionCount, "strategic action count");
            AssertEqual(0, snapshot.MechanicalActionCount, "mechanical action count");
            AssertEqual("Summon Cubic Seed", action.ActionLabel, "action label");
            AssertEqual("summon", action.ActionGroup, "action group");
            AssertEqual(false, action.IsMechanical, "is mechanical");
            AssertEqual("board_development", action.StrategicRole, "strategic role");
            AssertEqual(false, action.RequiresTarget, "requires target");
            AssertEqual("normal_summon_consumes_turn_summon", action.ConsequenceHint, "consequence hint");

            Dictionary<string, object> data = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));
            List<object> actions = (List<object>)data["legal_actions"];
            Dictionary<string, object> serializedAction = (Dictionary<string, object>)actions[0];

            AssertEqual(true, data["is_strategic_window"], "serialized strategic window");
            AssertEqual("strategic_choices", data["strategic_window_reason"], "serialized strategic reason");
            AssertNumber(1, data["strategic_action_count"], "serialized strategic count");
            AssertEqual("Summon Cubic Seed", serializedAction["action_label"], "serialized action label");
            AssertEqual("summon", serializedAction["action_group"], "serialized action group");
            AssertEqual(false, serializedAction["is_mechanical"], "serialized mechanical");
            AssertEqual("board_development", serializedAction["strategic_role"], "serialized strategic role");
            AssertEqual(false, serializedAction["requires_target"], "serialized requires target");
            AssertEqual(
                "normal_summon_consumes_turn_summon",
                serializedAction["consequence_hint"],
                "serialized consequence hint");
        }

        static void ClassifiesMechanicalDecideWindowBeforeBrokerDispatch()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.SummoningMonsterUniqueId = 33;
            query.CardIdsByUniqueId[33] = 5033;
            query.SummonPositionMask = (1 << 0) | (1 << 1);
            FakeCardCatalog catalog = new FakeCardCatalog();

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query,
                132,
                DuelViewType.WaitInput,
                1,
                catalog);
            LegalActionExtractor.ApplyViewContext(
                snapshot, query, (int)DuelMenuActType.Location, 33,
                (int)DuelMenuParamType.Cancel, catalog);

            AssertEqual(2, snapshot.LegalActions.Count, "placement actions");
            AssertEqual(false, snapshot.IsStrategicWindow, "mechanical window");
            AssertEqual("mechanical_only", snapshot.StrategicWindowReason, "mechanical reason");
            AssertEqual(0, snapshot.StrategicActionCount, "strategic action count");
            AssertEqual(2, snapshot.MechanicalActionCount, "mechanical action count");
            AssertEqual(true, snapshot.LegalActions[0].IsMechanical, "first placement mechanical");
            AssertEqual("placement", snapshot.LegalActions[0].StrategicRole, "placement role");
            AssertEqual("summon_placement", snapshot.LegalActions[0].TargetScope, "placement target scope");
        }

        static void RoutesControlledStrategicWindowToBroker()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                0,
                0,
                true,
                LlmBrokerRequestGateDecision.StartRequest,
                false,
                true,
                null);

            AssertEqual(LlmDecisionWindowRoute.Broker, plan.Route, "broker route");
            AssertEqual("strategic_choices", plan.Reason, "broker reason");
        }

        static void RoutesControlledMechanicalWindowToAutomatic()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                ViewType = DuelViewType.WaitInput,
                IsStrategicWindow = false,
                StrategicWindowReason = "mechanical_only",
                StrategicActionCount = 0,
                MechanicalActionCount = 1,
            };
            LegalAction action = new LegalAction() { Kind = LegalActionKind.Command };

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                1,
                true,
                null,
                false,
                true,
                action);

            AssertEqual(LlmDecisionWindowRoute.Automatic, plan.Route, "automatic route");
            AssertEqual(action, plan.AutomaticAction, "automatic action");
        }

        static void RoutesControlledEmptyWindowToCpuFallback()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                ViewType = DuelViewType.WaitInput,
                StrategicWindowReason = "no_actions",
                IsStrategicWindow = false,
            };

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                1,
                true,
                null,
                false,
                true,
                null);

            AssertEqual(LlmDecisionWindowRoute.CpuFallback, plan.Route, "cpu route");
            AssertEqual("no_actions", plan.Reason, "cpu reason");
            AssertEqual(true, plan.IsUnsupportedWindow, "unsupported window");
        }

        static void RoutesUncontrolledInfoDialogToDefault()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                ViewType = DuelViewType.RunDialog,
            };

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                0,
                0,
                false,
                null,
                true,
                true,
                null);

            AssertEqual(LlmDecisionWindowRoute.Default, plan.Route, "default route");
            AssertEqual("info_only", plan.Reason, "default reason");
        }

        static void RoutesPendingBrokerRequestToSuppressed()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                0,
                0,
                true,
                LlmBrokerRequestGateDecision.SuppressForPendingRequest,
                false,
                true,
                null);

            AssertEqual(LlmDecisionWindowRoute.Suppressed, plan.Route, "suppressed route");
            AssertEqual("pending_broker_request", plan.Reason, "suppressed reason");
        }

        static void RoutesPendingBrokerRequestSuppressesBeforeAutomatic()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                ViewType = DuelViewType.WaitInput,
                IsStrategicWindow = false,
                StrategicWindowReason = "mechanical_only",
                StrategicActionCount = 0,
                MechanicalActionCount = 1,
            };
            LegalAction action = new LegalAction() { Kind = LegalActionKind.Command };

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                1,
                true,
                LlmBrokerRequestGateDecision.SuppressForPendingRequest,
                false,
                true,
                action);

            AssertEqual(LlmDecisionWindowRoute.Suppressed, plan.Route, "suppressed route");
            AssertEqual("pending_broker_request", plan.Reason, "suppressed reason");
        }

        static void RoutesGateFallbackToCpuFallback()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                0,
                0,
                true,
                LlmBrokerRequestGateDecision.FallbackToDefault,
                false,
                true,
                null);

            AssertEqual(LlmDecisionWindowRoute.CpuFallback, plan.Route, "gate fallback route");
            AssertEqual("gate_fallback", plan.Reason, "gate fallback reason");
            AssertEqual(false, plan.IsUnsupportedWindow, "gate fallback is not unsupported");
        }

        static void RoutesGateFallbackDoesNotBlockAutomatic()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                ViewType = DuelViewType.WaitInput,
                IsStrategicWindow = false,
                StrategicWindowReason = "mechanical_only",
                StrategicActionCount = 0,
                MechanicalActionCount = 1,
            };
            LegalAction action = new LegalAction() { Kind = LegalActionKind.Command };

            // Different-seq in-flight maps to FallbackToDefault; automatic mechanical
            // windows must still auto-commit instead of falling through to CPU.
            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                1,
                true,
                LlmBrokerRequestGateDecision.FallbackToDefault,
                false,
                true,
                action);

            AssertEqual(LlmDecisionWindowRoute.Automatic, plan.Route, "automatic wins over gate fallback");
            AssertEqual(action, plan.AutomaticAction, "automatic action preserved");
            AssertEqual(false, plan.IsUnsupportedWindow, "automatic is not unsupported");
        }

        static void RoutesMechanicalOnlyWithoutUnsupportedFlag()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                ViewType = DuelViewType.WaitInput,
                IsStrategicWindow = false,
                StrategicWindowReason = "mechanical_only",
                StrategicActionCount = 0,
                MechanicalActionCount = 2,
            };
            snapshot.LegalActions.Add(new LegalAction() { Kind = LegalActionKind.Command });

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                1,
                true,
                null,
                false,
                true,
                null);

            AssertEqual(LlmDecisionWindowRoute.CpuFallback, plan.Route, "mechanical cpu route");
            AssertEqual("mechanical_only", plan.Reason, "mechanical reason");
            AssertEqual(false, plan.IsUnsupportedWindow, "mechanical is intentional skip not unsupported");
        }

        static void RoutesKnownSummonPlacementWindowToAutomatic()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.SummoningMonsterUniqueId = 33;
            query.SummonPositionMask = (1 << 1) | (1 << 2);
            query.CardIdsByUniqueId[33] = 4927;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 192, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot, query, (int)DuelMenuActType.Location, 33,
                (int)DuelMenuParamType.Cancel);

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                1,
                true,
                null,
                false,
                true,
                null);

            AssertEqual(LlmDecisionWindowRoute.Automatic, plan.Route, "summon placement route");
            AssertEqual("summon_placement", plan.Reason, "summon placement reason");
            AssertEqual(snapshot.LegalActions[0], plan.AutomaticAction, "summon placement action");
            AssertEqual(false, plan.IsUnsupportedWindow, "summon placement is not unsupported");
        }

        static void ResolvesPlannerSelectedAutomaticActionForCommit()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.SummoningMonsterUniqueId = 33;
            query.SummonPositionMask = (1 << 1) | (1 << 2);
            query.CardIdsByUniqueId[33] = 4927;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 192, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot, query, (int)DuelMenuActType.Location, 33,
                (int)DuelMenuParamType.Cancel);

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                1,
                true,
                null,
                false,
                true,
                null);

            LegalAction action = LlmDecisionWindowPlanner.ResolveAutomaticActionForCommit(
                plan,
                null);

            AssertEqual(snapshot.LegalActions[0], action, "planner selected automatic action");
        }

        static void RoutesUncontrolledRemotePromptToCpuFallback()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            snapshot.ActingPlayer = 1;

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                1,
                0,
                false,
                null,
                false,
                true,
                null);

            AssertEqual(LlmDecisionWindowRoute.CpuFallback, plan.Route, "remote route");
            AssertEqual("uncontrolled_remote_player", plan.Reason, "remote reason");
            AssertEqual(false, plan.IsUnsupportedWindow, "remote is not unsupported");
        }

        static void RoutesUncontrolledLocalPromptToDefault()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();

            LlmDecisionWindowPlan plan = LlmDecisionWindowPlanner.Plan(
                snapshot,
                true,
                0,
                0,
                false,
                null,
                false,
                false,
                null);

            AssertEqual(LlmDecisionWindowRoute.Default, plan.Route, "local default route");
            AssertEqual("uncontrolled_local_player", plan.Reason, "local default reason");
        }

        static void ParsesBrokerDecisionResponse()
        {
            LlmBrokerDecisionResponse response;
            string error;
            bool parsed = LlmBrokerProtocol.TryParseDecisionResponse(
                "{\"run_effect_seq\":42,\"action_id\":1,\"reason\":\"test\",\"history_event_ids_used\":[]}",
                out response,
                out error);

            AssertEqual(true, parsed, "parsed");
            AssertEqual(null, error, "error");
            AssertEqual((ulong)42, response.RunEffectSeq, "run_effect_seq");
            AssertEqual(1, response.ActionId, "action_id");
            AssertEqual("test", response.Reason, "reason");
        }

        static void ParsesBrokerDecisionResponseWithConfidenceAndPlan()
        {
            LlmBrokerDecisionResponse response;
            string error;
            bool parsed = LlmBrokerProtocol.TryParseDecisionResponse(
                "{\"run_effect_seq\":42,\"action_id\":1,\"reason\":\"summon Duza\",\"confidence\":0.74,\"plan\":\"develop first\",\"history_event_ids_used\":[]}",
                out response,
                out error);

            AssertEqual(true, parsed, "parsed");
            AssertEqual(null, error, "error");
            AssertEqual((ulong)42, response.RunEffectSeq, "run_effect_seq");
            AssertEqual(1, response.ActionId, "action_id");
            AssertEqual("summon Duza", response.Reason, "reason");
            AssertEqual(0.74, response.Confidence.Value, "confidence");
            AssertEqual("develop first", response.Plan, "plan");
        }

        static void ParsesBrokerDecisionResponseWithTacticalAuditFields()
        {
            LlmBrokerDecisionResponse response;
            string error;
            bool parsed = LlmBrokerProtocol.TryParseDecisionResponse(
                "{\"run_effect_seq\":42,\"action_id\":1,\"reason\":\"summon Duza\",\"confidence\":0.74,\"plan\":\"develop first\",\"why_now\":\"before moving phases\",\"alternatives_considered\":[\"set Duza loses its effect\",\"end phase gives up tempo\"],\"risk\":\"opponent may remove it\",\"history_event_ids_used\":[]}",
                out response,
                out error);

            AssertEqual(true, parsed, "parsed");
            AssertEqual(null, error, "error");
            AssertEqual((ulong)42, response.RunEffectSeq, "run_effect_seq");
            AssertEqual(1, response.ActionId, "action_id");
            AssertEqual("before moving phases", response.WhyNow, "why_now");
            AssertEqual(2, response.AlternativesConsidered.Count, "alternatives_considered count");
            AssertEqual("set Duza loses its effect", response.AlternativesConsidered[0], "alternative[0]");
            AssertEqual("opponent may remove it", response.Risk, "risk");
        }

        static void ParsesBrokerDecisionResponseWithOpponentBoardAssessment()
        {
            LlmBrokerDecisionResponse response;
            string error;
            bool parsed = LlmBrokerProtocol.TryParseDecisionResponse(
                "{\"run_effect_seq\":42,\"action_id\":1,\"reason\":\"summon Duza\",\"opponent_board_assessment\":\"Opponent has one known grave threat and one unknown field card.\",\"history_event_ids_used\":[]}",
                out response,
                out error);

            AssertEqual(true, parsed, "parsed");
            AssertEqual(null, error, "error");
            AssertEqual(
                "Opponent has one known grave threat and one unknown field card.",
                response.OpponentBoardAssessment,
                "opponent board assessment");
        }

        static void ParsesBrokerErrorResponse()
        {
            string error;
            string detail;
            AssertEqual(
                true,
                LlmBrokerProtocol.TryParseErrorResponse(
                    "{\"error\":\"provider_error\",\"detail\":\"timed out\"}",
                    out error,
                    out detail),
                "parsed provider error");
            AssertEqual("provider_error", error, "provider error");
            AssertEqual("timed out", detail, "provider detail");
            AssertEqual(false, LlmBrokerProtocol.TryParseErrorResponse("not json", out error), "invalid json");
            AssertEqual(false, LlmBrokerProtocol.TryParseErrorResponse("{\"error\":\"\"}", out error), "empty error");
            AssertEqual(false, LlmBrokerProtocol.TryParseErrorResponse("{\"detail\":\"timed out\"}", out error), "missing error");
        }

        static void AcceptsBrokerResponseWithLegalActionId()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 42,
                    ActionId = 1,
                });

            AssertEqual(true, result.IsValid, "is_valid");
            AssertEqual(null, result.Error, "error");
            AssertEqual(DuelCommandType.Attack, result.Action.Command, "command");
        }

        static void RejectsBrokerResponseWithLowConfidence()
        {
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();
            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 42,
                    ActionId = 0,
                    Reason = "Normal Summon Cubic Seed to start the line.",
                    Confidence = 0.22,
                });

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("low_confidence", result.Error, "error");
            AssertEqual(null, result.Action, "action");
        }

        static void RejectsBrokerResponseWithGenericReason()
        {
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();
            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 42,
                    ActionId = 0,
                    Reason = "first option",
                    Confidence = 0.75,
                });

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("generic_reason", result.Error, "error");
            AssertEqual(null, result.Action, "action");
        }

        static void RejectsBrokerResponseForMechanicalAction()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.SummoningMonsterUniqueId = 33;
            query.CardIdsByUniqueId[33] = 5033;
            query.SummonPositionMask = (1 << 0) | (1 << 1);
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 132, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot, query, (int)DuelMenuActType.Location, 33,
                (int)DuelMenuParamType.Cancel);

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 132,
                    ActionId = 0,
                    Reason = "Place the monster in the first available zone.",
                    Confidence = 0.8,
                });

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("mechanical_action", result.Error, "error");
        }

        static void RejectsBrokerResponseForCardlessStrategicAction()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 13)] = 1;
            query.CardUniqueIds[Key(1, 13, 0)] = 901;
            query.CommandMasks[Key(1, 13, 0)] = (uint)(1 << (int)DuelCommandType.Summon);
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 42, DuelViewType.WaitInput, 1);

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 42,
                    ActionId = 0,
                    Reason = "Normal Summon this monster to develop the board.",
                    Confidence = 0.8,
                });

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("cardless_strategic_action", result.Error, "error");
        }

        static void RejectsBrokerResponseForEarlyEndPhaseWithPlayableCommand()
        {
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();
            snapshot.LegalActions.Insert(
                0,
                new LegalAction()
                {
                    ActionId = 0,
                    Kind = LegalActionKind.MovePhase,
                    Phase = DuelPhase.End,
                });
            snapshot.LegalActions[1].ActionId = 1;
            LegalActionExtractor.ClassifySnapshot(snapshot);

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 42,
                    ActionId = 0,
                    Reason = "End now to preserve resources after reviewing available plays.",
                    Confidence = 0.8,
                });

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("early_end_phase", result.Error, "error");
            AssertEqual(DuelPhase.End, result.Action.Phase, "rejected action phase");
        }

        static void SelectsPreferredActionForEarlyEndPhaseRecovery()
        {
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();
            snapshot.LegalActions.Insert(
                0,
                new LegalAction()
                {
                    ActionId = 0,
                    Kind = LegalActionKind.MovePhase,
                    Phase = DuelPhase.End,
                });
            snapshot.LegalActions[1].ActionId = 1;
            LegalActionExtractor.ClassifySnapshot(snapshot);

            LegalAction recoveryAction;
            string policyBranch;
            AssertEqual(true, LlmBrokerRecovery.TrySelectAction(
                snapshot,
                "early_end_phase",
                snapshot.LegalActions[0],
                out recoveryAction,
                out policyBranch), "recovery available");
            AssertEqual(LlmBrokerRecovery.PolicyPreferredAction, policyBranch, "policy");
            AssertEqual(LegalActionKind.MovePhase, recoveryAction.Kind, "recovery kind");
            AssertEqual(DuelPhase.End, recoveryAction.Phase, "recovery phase");
        }

        static void SelectsQualityPassingActionWhenPreferredActionChanged()
        {
            DecisionSnapshot requestSnapshot = CreateCardAwareSnapshot();
            requestSnapshot.LegalActions.Insert(
                0,
                new LegalAction()
                {
                    ActionId = 0,
                    Kind = LegalActionKind.MovePhase,
                    Phase = DuelPhase.End,
                });
            requestSnapshot.LegalActions[1].ActionId = 1;
            LegalActionExtractor.ClassifySnapshot(requestSnapshot);

            DecisionSnapshot currentSnapshot = CreateCardAwareSnapshot();
            currentSnapshot.LegalActions[0].ActionId = 0;
            LegalActionExtractor.ClassifySnapshot(currentSnapshot);

            LegalAction recoveryAction;
            string policyBranch;
            AssertEqual(true, LlmBrokerRecovery.TrySelectAction(
                currentSnapshot,
                "early_end_phase",
                requestSnapshot.LegalActions[0],
                out recoveryAction,
                out policyBranch), "recovery available");
            // Milestone 3D: grounded positive replaces list-order quality_passer when
            // preferred intent is no longer legal and no optional decline exists.
            AssertEqual(
                true,
                policyBranch == LlmBrokerRecovery.PolicyGroundedPositive ||
                    policyBranch == LlmBrokerRecovery.PolicyQualityPasser,
                "policy");
            AssertEqual(DuelCommandType.Summon, recoveryAction.Command, "recovery command");
            AssertEqual("Cubic Seed", recoveryAction.Card.Name, "recovery card");
        }

        static void RecoverableQualityErrorExcludesStaleSeq()
        {
            AssertEqual(true, LlmBrokerProtocol.IsRecoverableQualityError("early_end_phase"), "early_end");
            AssertEqual(false, LlmBrokerProtocol.IsRecoverableQualityError("stale_run_effect_seq"), "stale");
            AssertEqual(false, LlmBrokerProtocol.IsRecoverableQualityError("action_changed"), "action_changed");
        }

        static void SerializesBrokerRecoveredActionAsJsonLine()
        {
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerRecoveredAction(
                    42,
                    44,
                    "early_end_phase",
                    LlmBrokerRecovery.PolicyPreferredAction,
                    snapshot.LegalActions[0],
                    new LlmBrokerDecisionResponse()
                    {
                        ActionId = 0,
                        Reason = "End Phase now.",
                    }));
            Dictionary<string, object> action = (Dictionary<string, object>)data["action"];

            AssertEqual("llm_broker_recovered", data["kind"], "kind");
            AssertEqual((long)42, data["request_run_effect_seq"], "request_run_effect_seq");
            AssertEqual((long)44, data["commit_run_effect_seq"], "commit_run_effect_seq");
            AssertEqual("early_end_phase", data["error"], "error");
            AssertEqual(LlmBrokerRecovery.PolicyPreferredAction, data["policy_branch"], "policy_branch");
            AssertEqual("command", data["action_type"], "action_type");
            AssertEqual("command", action["kind"], "action kind");
        }

        static void BrokerRecoverySelectsPreferredActionForEarlyEndPhase()
        {
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();
            snapshot.LegalActions.Insert(
                0,
                new LegalAction()
                {
                    ActionId = 0,
                    Kind = LegalActionKind.MovePhase,
                    Phase = DuelPhase.End,
                });
            snapshot.LegalActions[1].ActionId = 1;
            LegalActionExtractor.ClassifySnapshot(snapshot);

            LegalAction rejected = snapshot.LegalActions[0];
            LegalAction recoveryAction;
            string policyBranch;
            AssertEqual(true, LlmBrokerRecovery.TrySelectAction(
                snapshot,
                "early_end_phase",
                rejected,
                out recoveryAction,
                out policyBranch), "recovery available");
            AssertEqual(LlmBrokerRecovery.PolicyPreferredAction, policyBranch, "policy");
            AssertEqual(LegalActionKind.MovePhase, recoveryAction.Kind, "recovery kind");
            AssertEqual(DuelPhase.End, recoveryAction.Phase, "recovery phase");
        }

        static void RejectsBrokerResponseWhenProviderLatencyExceedsQualityBudget()
        {
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();
            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 42,
                    ActionId = 0,
                    Reason = "Normal Summon Cubic Seed to start the Cubic line.",
                    Confidence = 0.8,
                },
                null,
                46000);

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("provider_latency_exceeded", result.Error, "error");
        }

        static void RejectsBrokerResponseWithStaleRunEffectSeq()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 41,
                    ActionId = 1,
                });

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("stale_run_effect_seq", result.Error, "error");
            AssertEqual(null, result.Action, "action");
        }

        static void RejectsBrokerResponseWithUnknownActionId()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 42,
                    ActionId = 9,
                });

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("unknown_action_id", result.Error, "error");
            AssertEqual(null, result.Action, "action");
        }

        static void AcceptsBrokerResponseWhenExpectedActionStillMatches()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            LegalAction expectedAction = snapshot.LegalActions[1];
            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 42,
                    ActionId = 1,
                },
                expectedAction);

            AssertEqual(true, result.IsValid, "is_valid");
            AssertEqual(DuelCommandType.Attack, result.Action.Command, "command");
        }

        static void RejectsBrokerResponseWhenActionIdChangedMeaning()
        {
            DecisionSnapshot originalSnapshot = CreateSnapshotWithTwoActions();
            LegalAction expectedAction = originalSnapshot.LegalActions[1];
            DecisionSnapshot currentSnapshot = CreateSnapshotWithTwoActions();
            currentSnapshot.LegalActions[1].Command = DuelCommandType.Set;

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                currentSnapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 42,
                    ActionId = 1,
                },
                expectedAction);

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("action_changed", result.Error, "error");
            AssertEqual(null, result.Action, "action");
        }

        static void RejectsBrokerResponseWhenPhaseActionChangedMeaning()
        {
            DecisionSnapshot originalSnapshot = CreateSnapshotWithTwoActions();
            LegalAction expectedAction = originalSnapshot.LegalActions[0];
            DecisionSnapshot currentSnapshot = CreateSnapshotWithTwoActions();
            currentSnapshot.LegalActions[0].Phase = DuelPhase.End;

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                currentSnapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 42,
                    ActionId = 0,
                },
                expectedAction);

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("action_changed", result.Error, "error");
            AssertEqual(null, result.Action, "action");
        }

        static void BrokerClientPostsDecisionRequestAndReturnsLegalAction()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            FakeBrokerTransport transport = new FakeBrokerTransport(
                "{\"run_effect_seq\":42,\"action_id\":1,\"history_event_ids_used\":[]}");

            LlmBrokerDecisionResult result = LlmBrokerClient.RequestDecision(snapshot, transport, 2000);

            AssertEqual(true, result.IsSuccess, "is_success");
            AssertEqual(null, result.Error, "error");
            AssertEqual(null, result.ErrorDetail, "error_detail");
            AssertEqual(1, transport.RequestCount, "request_count");
            AssertEqual(true, transport.LastRequestJson.Contains("\"kind\":\"decision_request\""), "request kind");
            AssertEqual(true, transport.LastRequestJson.Contains("\"public_state\""), "public state");
            AssertEqual(true, transport.LastRequestJson.Contains("\"duel_history\""), "duel_history");
            AssertEqual(DuelCommandType.Attack, result.Action.Command, "command");
        }

        static void BrokerClientRecordsLatencyMillis()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            FakeBrokerTransport transport = new FakeBrokerTransport(
                "{\"run_effect_seq\":42,\"action_id\":1,\"reason\":\"Attack with the available monster.\",\"confidence\":0.8,\"history_event_ids_used\":[]}");
            transport.DelayMs = 10;

            LlmBrokerDecisionResult result = LlmBrokerClient.RequestDecision(snapshot, transport, 2000);

            AssertEqual(true, result.IsSuccess, "is_success");
            if (!result.LatencyMs.HasValue || result.LatencyMs.Value < 0)
            {
                throw new Exception("latency_ms should be recorded");
            }
        }

        static void BrokerClientRejectsInvalidJson()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            FakeBrokerTransport transport = new FakeBrokerTransport("not json");

            LlmBrokerDecisionResult result = LlmBrokerClient.RequestDecision(snapshot, transport, 2000);

            AssertEqual(false, result.IsSuccess, "is_success");
            AssertEqual("invalid_json", result.Error, "error");
            AssertEqual(null, result.ErrorDetail, "error_detail");
            AssertEqual(null, result.Action, "action");
        }

        static void BrokerClientPreservesBrokerErrorResponseBody()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            string errorJson = "{\"error\":\"provider_error\",\"detail\":\"timed out\"}";
            FakeBrokerTransport transport = new FakeBrokerTransport(null);
            transport.ExceptionToThrow = new LlmBrokerTransportException("bad gateway", errorJson);

            LlmBrokerDecisionResult result = LlmBrokerClient.RequestDecision(snapshot, transport, 2000);

            AssertEqual(false, result.IsSuccess, "is_success");
            AssertEqual("provider_error", result.Error, "error");
            AssertEqual("timed out", result.ErrorDetail, "error_detail");
            AssertEqual(errorJson, result.ResponseJson, "response_json");
            AssertEqual(null, result.Action, "action");
        }

        static void HttpBrokerTransportPreservesErrorResponseBody()
        {
            string errorJson = "{\"error\":\"provider_error\",\"detail\":\"timed out\"}";
            using (SingleResponseHttpServer server = new SingleResponseHttpServer(502, "Bad Gateway", errorJson))
            {
                server.Start();
                HttpLlmBrokerTransport transport = new HttpLlmBrokerTransport(server.Url);
                try
                {
                    transport.PostDecisionRequest("{}", 2000);
                    throw new Exception("expected transport exception");
                }
                catch (LlmBrokerTransportException e)
                {
                    AssertEqual(errorJson, e.ResponseJson, "response_json");
                }
            }
        }

        static void CreatesCommandCommitPlan()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            LlmActionCommitPlan plan = LlmActionCommitPlan.FromLegalAction(snapshot.LegalActions[1]);

            AssertEqual(LlmActionCommitKind.Command, plan.Kind, "kind");
            AssertEqual(0, plan.Player, "player");
            AssertEqual(13, plan.Position, "position");
            AssertEqual(0, plan.Index, "index");
            AssertEqual((int)DuelCommandType.Attack, plan.CommandId, "command_id");
        }

        static void CreatesPhaseCommitPlan()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            LlmActionCommitPlan plan = LlmActionCommitPlan.FromLegalAction(snapshot.LegalActions[0]);

            AssertEqual(LlmActionCommitKind.MovePhase, plan.Kind, "kind");
            AssertEqual((int)DuelPhase.Battle, plan.PhaseId, "phase_id");
        }

        static void CreatesSummonPlacementCommitPlan()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.SummoningMonsterUniqueId = 33;
            query.SummonPositionMask = 1 << 2;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 132, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                snapshot, query, (int)DuelMenuActType.Location, 33,
                (int)DuelMenuParamType.Cancel);
            LlmActionCommitPlan plan = LlmActionCommitPlan.FromLegalAction(snapshot.LegalActions[0]);

            AssertEqual(LlmActionCommitKind.Command, plan.Kind, "kind");
            AssertEqual(1, plan.Player, "player");
            AssertEqual(18, plan.Position, "selection pseudo-position");
            AssertEqual(2, plan.Index, "selected monster zone");
            AssertEqual((int)DuelCommandType.Decide, plan.CommandId, "command_id");
        }

        static void BlocksRepeatedAutomaticSummonPlacementPrompt()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.SummoningMonsterUniqueId = 33;
            query.SummonPositionMask = (1 << 1) | (1 << 2);

            DecisionSnapshot first = LegalActionExtractor.Extract(
                query, 301, DuelViewType.WaitInput, 1);
            DecisionSnapshot repeated = LegalActionExtractor.Extract(
                query, 302, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                first, query, (int)DuelMenuActType.Location, 33,
                (int)DuelMenuParamType.Cancel);
            LegalActionExtractor.ApplyViewContext(
                repeated, query, (int)DuelMenuActType.Location, 33,
                (int)DuelMenuParamType.Cancel);
            LlmAutomaticActionLoopGuard guard = new LlmAutomaticActionLoopGuard();

            AssertEqual(true, guard.TryAcquire(first, first.LegalActions[0]), "first placement attempt");
            AssertEqual(true, guard.TryAcquire(repeated, repeated.LegalActions[0]), "repeated prompt retry");
            AssertEqual(false, guard.TryAcquire(repeated, repeated.LegalActions[0]), "third repeated prompt blocked");

            query.SummoningMonsterUniqueId = 34;
            DecisionSnapshot different = LegalActionExtractor.Extract(
                query, 303, DuelViewType.WaitInput, 1);
            LegalActionExtractor.ApplyViewContext(
                different, query, (int)DuelMenuActType.Location, 34,
                (int)DuelMenuParamType.Cancel);
            AssertEqual(true, guard.TryAcquire(different, different.LegalActions[0]), "different summon accepted");

            guard.Reset();
            AssertEqual(true, guard.TryAcquire(repeated, repeated.LegalActions[0]), "reset accepts prompt");
        }

        static void StuckWindowWatchdogDetectsCrossSequenceLogicalCycle()
        {
            LlmStuckWindowWatchdog watchdog = new LlmStuckWindowWatchdog(3);
            LlmDecisionWindowPlan plan = new LlmDecisionWindowPlan()
            {
                Route = LlmDecisionWindowRoute.CpuFallback,
                Reason = "no_actions",
                HasActingPlayer = true,
                BrokerControlsActingPlayer = true,
                IsUnsupportedWindow = true,
            };

            LlmStuckWindowObservation first = watchdog.Observe(
                CreateCheckTimingSnapshot(994), plan, 7, false);
            LlmStuckWindowObservation second = watchdog.Observe(
                CreateCheckTimingSnapshot(997), plan, 7, false);
            LlmStuckWindowObservation third = watchdog.Observe(
                CreateCheckTimingSnapshot(1000), plan, 7, false);

            AssertEqual(false, first.ShouldRecover, "first cycle does not recover");
            AssertEqual(false, second.ShouldRecover, "second cycle does not recover");
            AssertEqual(true, third.ShouldRecover, "third cross-seq cycle recovers");
            AssertEqual(3, third.RepeatCount, "logical repeat count ignores run_effect_seq");
            AssertEqual(true, third.Fingerprint.Contains("WaitInput|4|0|1|CpuFallback|no_actions"),
                "stable logical fingerprint");

            DecisionSnapshot newPhase = CreateCheckTimingSnapshot(1003);
            newPhase.CurrentPhase = (int)DuelPhase.Battle;
            AssertEqual(false, watchdog.Observe(newPhase, plan, 7, false).ShouldRecover,
                "phase change resets watchdog");
        }

        static void StuckWindowWatchdogIgnoresPendingAndUncontrolledWindows()
        {
            LlmStuckWindowWatchdog watchdog = new LlmStuckWindowWatchdog(2);
            DecisionSnapshot snapshot = CreateCheckTimingSnapshot(1100);
            LlmDecisionWindowPlan pending = new LlmDecisionWindowPlan()
            {
                Route = LlmDecisionWindowRoute.Suppressed,
                Reason = "pending_broker_request",
                HasActingPlayer = true,
                BrokerControlsActingPlayer = true,
            };
            AssertEqual(false, watchdog.Observe(snapshot, pending, 8, true).ShouldRecover,
                "pending request ignored");
            AssertEqual(false, watchdog.Observe(CreateCheckTimingSnapshot(1103), pending, 8, true).ShouldRecover,
                "repeated pending request ignored");

            LlmDecisionWindowPlan uncontrolled = new LlmDecisionWindowPlan()
            {
                Route = LlmDecisionWindowRoute.Default,
                Reason = "uncontrolled_local_player",
                HasActingPlayer = true,
                BrokerControlsActingPlayer = false,
            };
            AssertEqual(false, watchdog.Observe(snapshot, uncontrolled, 8, false).ShouldRecover,
                "uncontrolled local prompt ignored");
            AssertEqual(false, watchdog.Observe(CreateCheckTimingSnapshot(1106), uncontrolled, 8, false).ShouldRecover,
                "repeated uncontrolled prompt ignored");
        }

        static void StuckWindowWatchdogDetectsAutomaticCheckTimingCycle()
        {
            LlmStuckWindowWatchdog watchdog = new LlmStuckWindowWatchdog(3);
            LegalAction decline = new LegalAction()
            {
                Kind = LegalActionKind.Cancel,
                IsMechanical = true,
                CancelDecide = false,
                TargetScope = "empty_check_timing_decline",
            };
            LlmDecisionWindowPlan automatic = new LlmDecisionWindowPlan()
            {
                Route = LlmDecisionWindowRoute.Automatic,
                Reason = "empty_check_timing_decline",
                AutomaticAction = decline,
                HasActingPlayer = true,
                BrokerControlsActingPlayer = true,
            };
            LlmDecisionWindowPlan dialog = new LlmDecisionWindowPlan()
            {
                Route = LlmDecisionWindowRoute.CpuFallback,
                Reason = "mechanical_only",
                HasActingPlayer = true,
                BrokerControlsActingPlayer = true,
            };

            AssertEqual(false, watchdog.Observe(
                CreateCheckTimingSnapshot(640), automatic, 9, false).ShouldRecover,
                "first automatic decline does not recover");
            AssertEqual(false, watchdog.Observe(
                CreateMechanicalDialogSnapshot(641, 4, 1, 4), dialog, 9, false).ShouldRecover,
                "intervening dialog does not reset or recover");
            AssertEqual(false, watchdog.Observe(
                CreateMechanicalDialogSnapshot(642, 0, 273, 3), dialog, 9, false).ShouldRecover,
                "second intervening dialog does not reset or recover");
            AssertEqual(false, watchdog.Observe(
                CreateCheckTimingSnapshot(643), automatic, 9, false).ShouldRecover,
                "second automatic decline does not recover");
            LlmStuckWindowObservation third = watchdog.Observe(
                CreateCheckTimingSnapshot(646), automatic, 9, false);

            AssertEqual(true, third.ShouldRecover,
                "third automatic CheckTiming cycle recovers");
            AssertEqual(3, third.RepeatCount,
                "automatic decline repeats survive mechanical dialogs");
            AssertEqual(true, third.Fingerprint.Contains(
                "WaitInput|4|0|1|Automatic|empty_check_timing_decline"),
                "automatic CheckTiming fingerprint is stable");
            AssertEqual(true,
                LlmDecisionWindowPlanner.ShouldPreserveWatchdogAfterAutomaticAction(decline),
                "empty response decline preserves watchdog history after commit");
        }

        static DecisionSnapshot CreateMechanicalDialogSnapshot(
            ulong runEffectSeq,
            int param1,
            int param2,
            int param3)
        {
            return new DecisionSnapshot()
            {
                RunEffectSeq = runEffectSeq,
                ViewType = DuelViewType.RunDialog,
                ViewParam1 = param1,
                ViewParam2 = param2,
                ViewParam3 = param3,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
        }

        static void TemporaryCpuCoordinatorAllowsExplicitWatchdogRecovery()
        {
            AssertEqual(false, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                1200, 1200, DuelViewType.WaitInput, (int)DuelMenuActType.CheckTiming,
                1, 1, 1, false, false),
                "ordinary temporary CPU request cannot broaden beyond selections");
            AssertEqual(true, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                1200, 1200, DuelViewType.WaitInput, (int)DuelMenuActType.CheckTiming,
                1, 1, 1, false, true),
                "explicit watchdog recovery can hand owned WaitInput to CPU");
            AssertEqual(false, LlmTemporaryCpuSelectionCoordinator.CanBeginRequest(
                1200, 1200, DuelViewType.WaitInput, (int)DuelMenuActType.CheckTiming,
                1, 0, 1, false, true),
                "watchdog recovery rejects spoofed actor seat");
        }

        static DecisionSnapshot CreateCheckTimingSnapshot(ulong runEffectSeq)
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = runEffectSeq,
                ViewType = DuelViewType.WaitInput,
                ViewParam1 = (int)DuelMenuActType.CheckTiming,
                ViewParam2 = 0,
                ViewParam3 = (int)DuelMenuParamType.TrueCancel,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            return snapshot;
        }

        static void CreatesCancelCommitPlan()
        {
            LegalAction action = new LegalAction()
            {
                Kind = LegalActionKind.Cancel,
                CancelDecide = true,
            };

            LlmActionCommitPlan plan = LlmActionCommitPlan.FromLegalAction(action);

            AssertEqual(LlmActionCommitKind.Cancel, plan.Kind, "kind");
            AssertEqual(true, plan.CancelDecide, "cancel decide");
        }

        static void ExtractsEnabledDialogResultActions()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.DialogSelectItemNum = 3;
            query.DialogSelectItemEnabled[0] = 1;
            query.DialogSelectItemEnabled[2] = 1;
            query.DialogSelectItemTextIds[0] = 1101;
            query.DialogSelectItemTextIds[2] = 1103;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 55, DuelViewType.RunDialog, 1);

            AssertEqual(2, snapshot.LegalActions.Count, "dialog action count");
            AssertEqual(LegalActionKind.DialogResult, snapshot.LegalActions[0].Kind, "first kind");
            AssertEqual(0, snapshot.LegalActions[0].DialogResult, "first result");
            AssertEqual(1101, snapshot.LegalActions[0].DialogTextId, "first text id");
            AssertEqual(LegalActionKind.DialogResult, snapshot.LegalActions[1].Kind, "second kind");
            AssertEqual(2, snapshot.LegalActions[1].DialogResult, "second result");
            AssertEqual(1103, snapshot.LegalActions[1].DialogTextId, "second text id");
        }

        static void ExtractsYesNoEffectDialogAsStrategicActions()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.DialogCanYesNoSkip = 1;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 136, DuelViewType.RunDialog, 1);

            AssertEqual(2, snapshot.LegalActions.Count, "yes/no action count");
            AssertEqual(true, snapshot.IsStrategicWindow, "yes/no strategic window");
            AssertEqual("strategic_choices", snapshot.StrategicWindowReason, "yes/no reason");
            AssertEqual(2, snapshot.StrategicActionCount, "yes/no strategic count");
            AssertEqual(0, snapshot.MechanicalActionCount, "yes/no mechanical count");
            AssertEqual(LegalActionKind.DialogResult, snapshot.LegalActions[0].Kind, "yes kind");
            AssertEqual(1, snapshot.LegalActions[0].DialogResult, "yes result");
            AssertEqual("Activate optional effect", snapshot.LegalActions[0].ActionLabel, "yes label");
            AssertEqual(false, snapshot.LegalActions[0].IsMechanical, "yes mechanical");
            AssertEqual(LegalActionKind.DialogResult, snapshot.LegalActions[1].Kind, "no kind");
            AssertEqual(0, snapshot.LegalActions[1].DialogResult, "no result");
            AssertEqual("Decline optional effect", snapshot.LegalActions[1].ActionLabel, "no label");
        }

        static void ExtractsListIndexActions()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.ListItemMax = 2;
            query.ListSelectMin = 1;
            query.ListSelectMax = 1;
            query.ListIsMultiMode = 0;
            query.ListItemIds[0] = 501;
            query.ListItemUniqueIds[0] = 9001;
            query.ListItemTargetUniqueIds[0] = 9101;
            query.ListItemMsgs[0] = 2101;
            query.ListItemAttributes[0] = 7;
            query.ListItemFroms[0] = 13;
            query.ListItemIds[1] = 502;
            query.ListItemUniqueIds[1] = 9002;
            query.ListItemTargetUniqueIds[1] = 9102;
            query.ListItemMsgs[1] = 2102;
            query.ListItemAttributes[1] = 8;
            query.ListItemFroms[1] = 16;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 56, DuelViewType.RunList, 1);

            AssertEqual(2, snapshot.LegalActions.Count, "list action count");
            AssertEqual(LegalActionKind.ListIndex, snapshot.LegalActions[0].Kind, "first kind");
            AssertEqual(0, snapshot.LegalActions[0].Index, "first index");
            AssertEqual(501, snapshot.LegalActions[0].ListItemId, "first item id");
            AssertEqual(9001, snapshot.LegalActions[0].ListItemUniqueId, "first unique id");
            AssertEqual(9101, snapshot.LegalActions[0].ListItemTargetUniqueId, "first target unique id");
            AssertEqual(2101, snapshot.LegalActions[0].ListItemMsg, "first msg");
            AssertEqual(7, snapshot.LegalActions[0].ListItemAttribute, "first attr");
            AssertEqual(13, snapshot.LegalActions[0].ListItemFrom, "first from");
            AssertEqual(1, snapshot.LegalActions[0].ListSelectMin, "first min");
            AssertEqual(1, snapshot.LegalActions[0].ListSelectMax, "first max");
            AssertEqual(0, snapshot.LegalActions[0].ListIsMultiMode, "first multi");
            AssertEqual(1, snapshot.LegalActions[1].Index, "second index");
            AssertEqual(502, snapshot.LegalActions[1].ListItemId, "second item id");
        }

        static void DoesNotExtractUnsupportedListSelectionWindows()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.ListItemMax = 2;
            query.ListSelectMin = 2;
            query.ListSelectMax = 2;
            query.ListIsMultiMode = 1;
            query.ListItemIds[0] = 501;
            query.ListItemIds[1] = 502;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 58, DuelViewType.RunList, 1);

            AssertEqual(0, snapshot.LegalActions.Count, "unsupported list action count");
        }

        static void DoesNotOfferWaitInputActionsForRunDialog()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.MovablePhaseMask = (uint)(1 << (int)DuelPhase.Battle);
            query.CardNums[Key(1, 13)] = 1;
            query.CommandMasks[Key(1, 13, 0)] = (uint)(1 << (int)DuelCommandType.Attack);
            query.DialogSelectItemNum = 1;
            query.DialogSelectItemEnabled[0] = 1;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 57, DuelViewType.RunDialog, 1);

            AssertEqual(1, snapshot.LegalActions.Count, "dialog-only action count");
            AssertEqual(LegalActionKind.DialogResult, snapshot.LegalActions[0].Kind, "kind");
        }

        static void SerializesDialogAndListActions()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot();
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.DialogResult,
                DialogResult = 2,
                DialogTextId = 1103,
            });
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.ListIndex,
                Index = 4,
                ListItemId = 502,
                ListItemUniqueId = 9002,
                ListItemTargetUniqueId = 9102,
                ListItemMsg = 2102,
                ListItemAttribute = 8,
                ListItemFrom = 16,
                ListSelectMin = 1,
                ListSelectMax = 1,
                ListIsMultiMode = 0,
            });

            List<object> data = LlmDecisionLogSerializer.SerializeLegalActions(snapshot.LegalActions);
            Dictionary<string, object> dialog = (Dictionary<string, object>)data[0];
            Dictionary<string, object> list = (Dictionary<string, object>)data[1];

            AssertEqual("dialog_result", dialog["kind"], "dialog kind");
            AssertNumber(2, dialog["result"], "dialog result");
            AssertNumber(1103, dialog["text_id"], "dialog text id");
            AssertEqual("list_index", list["kind"], "list kind");
            AssertNumber(4, list["index"], "list index");
            AssertNumber(502, list["item_id"], "list item id");
            AssertNumber(9002, list["item_unique_id"], "list item unique id");
            AssertNumber(9102, list["target_unique_id"], "list target unique id");
            AssertNumber(2102, list["msg"], "list msg");
            AssertNumber(8, list["attribute"], "list attr");
            AssertNumber(16, list["from"], "list from");
            AssertNumber(1, list["select_min"], "list min");
            AssertNumber(1, list["select_max"], "list max");
            AssertNumber(0, list["is_multi_mode"], "list multi");
        }

        static void RejectsBrokerResponseWhenDialogActionChangedMeaning()
        {
            DecisionSnapshot originalSnapshot = CreateDialogSnapshot();
            LegalAction expectedAction = originalSnapshot.LegalActions[0];
            DecisionSnapshot currentSnapshot = CreateDialogSnapshot();
            currentSnapshot.LegalActions[0].DialogResult = 1;

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                currentSnapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 77,
                    ActionId = 0,
                },
                expectedAction);

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("action_changed", result.Error, "error");
        }

        static void RejectsBrokerResponseWhenListActionChangedMeaning()
        {
            DecisionSnapshot originalSnapshot = CreateListSnapshot();
            LegalAction expectedAction = originalSnapshot.LegalActions[0];
            DecisionSnapshot currentSnapshot = CreateListSnapshot();
            currentSnapshot.LegalActions[0].ListItemUniqueId = 9900;

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                currentSnapshot,
                new LlmBrokerDecisionResponse()
                {
                    RunEffectSeq = 78,
                    ActionId = 0,
                },
                expectedAction);

            AssertEqual(false, result.IsValid, "is_valid");
            AssertEqual("action_changed", result.Error, "error");
        }

        static void CreatesDialogResultCommitPlan()
        {
            LlmActionCommitPlan plan = LlmActionCommitPlan.FromLegalAction(CreateDialogSnapshot().LegalActions[0]);

            AssertEqual(LlmActionCommitKind.DialogResult, plan.Kind, "kind");
            AssertEqual((uint)2, plan.DialogResult, "dialog result");
        }

        static void CreatesListIndexCommitPlan()
        {
            LlmActionCommitPlan plan = LlmActionCommitPlan.FromLegalAction(CreateListSnapshot().LegalActions[0]);

            AssertEqual(LlmActionCommitKind.ListIndex, plan.Kind, "kind");
            AssertEqual(0, plan.Index, "index");
        }

        static void BrokerGateStartsFirstSeqAndKeepsSameSeqPending()
        {
            LlmBrokerRequestGate gate = new LlmBrokerRequestGate();

            AssertEqual(
                LlmBrokerRequestGateDecision.StartRequest,
                gate.Evaluate(10),
                "first decision");
            AssertEqual(
                LlmBrokerRequestGateDecision.SuppressForPendingRequest,
                gate.Evaluate(10),
                "pending same seq");
        }

        static void BrokerGateFallsBackForNewSeqWhileOldSeqIsPending()
        {
            LlmBrokerRequestGate gate = new LlmBrokerRequestGate();

            AssertEqual(
                LlmBrokerRequestGateDecision.StartRequest,
                gate.Evaluate(10),
                "first decision");
            AssertEqual(
                LlmBrokerRequestGateDecision.FallbackToDefault,
                gate.Evaluate(11),
                "new seq while old pending");
        }

        static void BrokerGateFallsBackAfterFailedSeq()
        {
            LlmBrokerRequestGate gate = new LlmBrokerRequestGate();

            AssertEqual(
                LlmBrokerRequestGateDecision.StartRequest,
                gate.Evaluate(10),
                "first decision");
            gate.MarkFailed(10);
            gate.Finish(10);

            AssertEqual(
                LlmBrokerRequestGateDecision.FallbackToDefault,
                gate.Evaluate(10),
                "failed seq");
        }

        static void BrokerGateSuppressesCompletedSeqOnceThenFallsBack()
        {
            LlmBrokerRequestGate gate = new LlmBrokerRequestGate();

            AssertEqual(
                LlmBrokerRequestGateDecision.StartRequest,
                gate.Evaluate(10),
                "first decision");
            gate.MarkCompleted(10);
            gate.Finish(10);

            AssertEqual(
                LlmBrokerRequestGateDecision.SuppressForCompletedRequest,
                gate.Evaluate(10),
                "completed seq first repeat");
            AssertEqual(
                LlmBrokerRequestGateDecision.FallbackToDefault,
                gate.Evaluate(10),
                "completed seq second repeat");
        }

        static void BrokerGateKeepsFallingBackAfterCompletedSeqSuppressIsConsumed()
        {
            LlmBrokerRequestGate gate = new LlmBrokerRequestGate();

            AssertEqual(
                LlmBrokerRequestGateDecision.StartRequest,
                gate.Evaluate(10),
                "first decision");
            gate.MarkCompleted(10);
            gate.Finish(10);

            AssertEqual(
                LlmBrokerRequestGateDecision.SuppressForCompletedRequest,
                gate.Evaluate(10),
                "completed seq first repeat");
            AssertEqual(
                LlmBrokerRequestGateDecision.FallbackToDefault,
                gate.Evaluate(10),
                "completed seq second repeat");
            AssertEqual(
                LlmBrokerRequestGateDecision.FallbackToDefault,
                gate.Evaluate(10),
                "completed seq third repeat");
        }

        static void BrokerGateAllowsZeroSeq()
        {
            LlmBrokerRequestGate gate = new LlmBrokerRequestGate();

            AssertEqual(
                LlmBrokerRequestGateDecision.StartRequest,
                gate.Evaluate(0),
                "zero seq");
        }

        static void BrokerGateResetClearsPendingRequest()
        {
            LlmBrokerRequestGate gate = new LlmBrokerRequestGate();

            AssertEqual(
                LlmBrokerRequestGateDecision.StartRequest,
                gate.Evaluate(10),
                "first decision");
            gate.Reset();
            AssertEqual(
                LlmBrokerRequestGateDecision.StartRequest,
                gate.Evaluate(10),
                "after reset");
        }

        static void TurnMemoryTrackerRecordsRecentActionsAndResets()
        {
            LlmTurnMemoryTracker tracker = new LlmTurnMemoryTracker(2);
            DecisionSnapshot snapshot = CreateCardAwareSnapshot();
            snapshot.Turn = 3;
            snapshot.CurrentPhase = (int)DuelPhase.Main1;
            LegalAction action = snapshot.LegalActions[0];
            tracker.RecordCommittedAction(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    Reason = "Normal Summon Cubic Seed to start the Cubic line.",
                    Plan = "Develop before battle.",
                },
                action);

            LlmTurnMemoryState memory = tracker.CreateSnapshot(snapshot);
            AssertEqual(1, memory.RecentActions.Count, "recent actions");
            AssertEqual("Summon Cubic Seed", memory.RecentActions[0].ActionLabel, "action label");
            AssertEqual(1, memory.CardsUsedThisTurn.Count, "cards used");
            AssertEqual(true, memory.NormalSummonUsed, "normal summon used");
            AssertEqual("develop_board", memory.PhasePlan, "phase plan");

            tracker.Reset();
            memory = tracker.CreateSnapshot(snapshot);
            AssertEqual(0, memory.RecentActions.Count, "recent actions after reset");
            AssertEqual(false, memory.NormalSummonUsed, "normal summon after reset");
        }

        static void BrokerControlPolicyOnlyControlsLocalConfiguredPlayer()
        {
            AssertEqual(
                true,
                LlmBrokerControlPolicy.ShouldControlPlayer(
                    true, true, "http://127.0.0.1:4991/decide", 1, 1, 1),
                "p2 controls p2");
            AssertEqual(
                false,
                LlmBrokerControlPolicy.ShouldControlPlayer(
                    true, true, "http://127.0.0.1:4991/decide", 1, 0, 1),
                "root does not control p2");
            AssertEqual(
                false,
                LlmBrokerControlPolicy.ShouldControlPlayer(
                    true, true, "http://127.0.0.1:4991/decide", 1, 1, 0),
                "p2 does not control p1");
        }

        static void BrokerControlPolicySuppressesLocalControlledPendingRequest()
        {
            AssertEqual(
                LlmBrokerViewHandling.SuppressForBroker,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 1, 1, true, true, false),
                "local broker pending view handling");
        }

        static void BrokerControlPolicyRunsCpuThinkingForLocalControlledWhenNoRequestStarts()
        {
            AssertEqual(
                LlmBrokerViewHandling.RunCpuThinking,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 1, 1, true, false, false),
                "local broker no request view handling");
        }

        static void BrokerControlPolicyFallbackRunsCpuThinkingForLocalControlledPlayer()
        {
            AssertEqual(
                LlmBrokerViewHandling.RunCpuThinking,
                LlmBrokerControlPolicy.DecideFallbackHandling(
                    true, 1, 1, true, false),
                "local broker fallback view handling");
        }

        static void BrokerControlPolicyRunsCpuThinkingForUncontrolledRemotePlayer()
        {
            AssertEqual(
                LlmBrokerViewHandling.RunCpuThinking,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 1, 0, false, false, false),
                "remote uncontrolled view handling");
        }

        static void BrokerControlPolicyRunsDefaultForLocalUncontrolledEmptyWaitInput()
        {
            AssertEqual(
                LlmBrokerViewHandling.RunDefault,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 0, 0, false, false, false, false),
                "local empty wait input handling");
        }

        static void BrokerControlPolicyRunsDefaultForControlledEmptyWaitInputWithoutActions()
        {
            // Live hang (seq 417): controlled WaitInput with no legal/automatic actions.
            // hasLocalDefaultInteraction=false means the engine surface has nothing to commit;
            // CpuThinking would never send a duel command and freezes PvP.
            // Native RunDefault (original RunEffect) matches the empty-RunDialog fix.
            AssertEqual(
                LlmBrokerViewHandling.RunDefault,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 1, 1, true, false, false, false),
                "controlled empty WaitInput uses native default, not CpuThinking");
            // With extractable interaction but no request, CpuThinking fallback remains.
            AssertEqual(
                LlmBrokerViewHandling.RunCpuThinking,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 1, 1, true, false, false, true),
                "controlled non-empty WaitInput without request still CpuThinking");
        }

        static void BrokerControlPolicyStillSuppressesControlledStrategicPendingRequest()
        {
            AssertEqual(
                LlmBrokerViewHandling.SuppressForBroker,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 1, 1, true, true, false, true),
                "pending broker request still suppresses native/default");
            // Even if probe says no local actions, an in-flight request must not be dropped
            // onto native default while the broker is still pending.
            AssertEqual(
                LlmBrokerViewHandling.SuppressForBroker,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 1, 1, true, true, false, false),
                "pending request suppresses even when local probe is empty");
        }

        static void BrokerPlayerResolverPrefersTurnPlayerForWaitInput()
        {
            int player;
            bool resolved = LlmBrokerPlayerResolver.TryResolve(
                DuelViewType.WaitInput,
                0,
                0,
                0,
                0,
                1,
                out player);

            AssertEqual(true, resolved, "resolved");
            AssertEqual(1, player, "player");
        }

        static void BrokerPlayerResolverUsesReportedUserForResponseWaitInput()
        {
            int playerFromSeat0;
            int playerFromSeat1;
            bool resolved0 = LlmBrokerPlayerResolver.TryResolve(
                DuelViewType.WaitInput,
                1,
                0,
                (int)DuelMenuActType.CheckChain,
                0,
                0,
                out playerFromSeat0);
            bool resolved1 = LlmBrokerPlayerResolver.TryResolve(
                DuelViewType.WaitInput,
                1,
                0,
                (int)DuelMenuActType.CheckChain,
                1,
                0,
                out playerFromSeat1);

            AssertEqual(true, resolved0, "response input resolved from seat0");
            AssertEqual(true, resolved1, "response input resolved from seat1");
            AssertEqual(1, playerFromSeat0, "response input player from seat0");
            AssertEqual(1, playerFromSeat1, "response input player from seat1");
        }

        static void BrokerPlayerResolverUsesReportedUserForLockOnWaitInput()
        {
            int player;
            bool resolved = LlmBrokerPlayerResolver.TryResolve(
                DuelViewType.WaitInput,
                1,
                0,
                (int)DuelMenuActType.LockOn,
                1,
                0,
                out player);

            AssertEqual(true, resolved, "lock-on input resolved");
            AssertEqual(1, player, "lock-on input player");
        }

        static void BrokerPlayerResolverFallsBackToRivalTurnForDialog()
        {
            int player;
            // Invalid reported dialog user (-1) falls back to turn player.
            bool resolved = LlmBrokerPlayerResolver.TryResolve(
                DuelViewType.RunDialog,
                0,
                -1,
                0,
                0,
                1,
                out player);

            AssertEqual(true, resolved, "resolved");
            AssertEqual(1, player, "player");
        }

        static void BrokerPlayerResolverTrustsReportedDialogUserEvenWhenLocal()
        {
            int player;
            // Absolute seat ids must resolve the same on both clients. A local
            // reported user must not be rewritten to the rival turn player.
            bool resolved = LlmBrokerPlayerResolver.TryResolve(
                DuelViewType.RunDialog,
                0,
                1,
                0,
                1,
                0,
                out player);

            AssertEqual(true, resolved, "resolved");
            AssertEqual(1, player, "player");
        }

        static void BrokerPlayerResolverTrustsReportedDialogUserIdenticallyForBothSeats()
        {
            int playerFromSeat0;
            int playerFromSeat1;
            bool resolved0 = LlmBrokerPlayerResolver.TryResolve(
                DuelViewType.RunDialog,
                0,
                1,
                0,
                0,
                0,
                out playerFromSeat0);
            bool resolved1 = LlmBrokerPlayerResolver.TryResolve(
                DuelViewType.RunDialog,
                0,
                1,
                0,
                1,
                0,
                out playerFromSeat1);

            AssertEqual(true, resolved0, "resolved0");
            AssertEqual(true, resolved1, "resolved1");
            AssertEqual(1, playerFromSeat0, "playerFromSeat0");
            AssertEqual(1, playerFromSeat1, "playerFromSeat1");
        }

        static void ResettingNonReadyPlayerKeepsOpponentReadyAndPvpSession()
        {
            DuelRoom room;
            Player p1;
            Player p2;
            DuelRoomTable table = CreateReadyRoom(
                DuelRoomTableState.P1StandingBy,
                true,
                false,
                out room,
                out p1,
                out p2);

            room.ResetTableStateIfMatchingOrDueling(p2);

            AssertEqual(true, table.Entries[0].IsMatchingOrInDuel, "p1 remains ready");
            AssertEqual(false, table.Entries[1].IsMatchingOrInDuel, "p2 remains not ready");
            AssertEqual(DuelRoomTableState.P1StandingBy, table.State, "state stays p1 standing by");
            AssertEqual("secret", table.SecretKeyForPvpServer, "secret key preserved");
            AssertEqual("hash", table.TableHash, "table hash preserved");
            AssertEqual("ticket", table.TableTicket, "table ticket preserved");
        }

        static void ResettingMatchedPlayerKeepsOpponentReadyAndPvpSession()
        {
            DuelRoom room;
            Player p1;
            Player p2;
            DuelRoomTable table = CreateReadyRoom(
                DuelRoomTableState.Matched,
                true,
                true,
                out room,
                out p1,
                out p2);
            table.MatchedTime = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);

            room.ResetTableStateIfMatchingOrDueling(p2);

            AssertEqual(true, table.Entries[0].IsMatchingOrInDuel, "p1 remains ready after matched reset");
            AssertEqual(false, table.Entries[1].IsMatchingOrInDuel, "p2 ready state cleared");
            AssertEqual(DuelRoomTableState.P1StandingBy, table.State, "state returns to p1 standing by");
            AssertEqual(default(DateTime), table.MatchedTime, "matched time cleared");
            AssertEqual("secret", table.SecretKeyForPvpServer, "secret key preserved after matched reset");
        }

        static DuelRoomTable CreateReadyRoom(
            DuelRoomTableState state,
            bool p1Ready,
            bool p2Ready,
            out DuelRoom room,
            out Player p1,
            out Player p2)
        {
            room = new DuelRoom();
            room.Id = 123;
            room.MemberLimit = 2;
            room.InitTables();

            p1 = new Player() { Code = 1001, Name = "P1", DuelRoom = room };
            p2 = new Player() { Code = 1002, Name = "P2", DuelRoom = room };

            DuelRoomTable table = room.Tables[0];
            table.Player1 = p1;
            table.Player2 = p2;
            table.State = state;
            table.SecretKeyForPvpServer = "secret";
            table.TableHash = "hash";
            table.TableTicket = "ticket";
            table.FirstPlayer = 1;
            table.Entries[0].IsMatchingOrInDuel = p1Ready;
            table.Entries[1].IsMatchingOrInDuel = p2Ready;
            return table;
        }

        static void AssertCommand(
            LegalAction action,
            int actionId,
            int player,
            int position,
            int index,
            DuelCommandType command,
            int uniqueId)
        {
            AssertEqual(actionId, action.ActionId, "action_id");
            AssertEqual(LegalActionKind.Command, action.Kind, "kind");
            AssertEqual(player, action.Player, "player");
            AssertEqual(position, action.Position, "position");
            AssertEqual(index, action.Index, "index");
            AssertEqual(command, action.Command, "command");
            AssertEqual(uniqueId, action.CardUniqueId, "card_unique_id");
        }

        static void AssertPhase(LegalAction action, int actionId, DuelPhase phase)
        {
            AssertEqual(actionId, action.ActionId, "action_id");
            AssertEqual(LegalActionKind.MovePhase, action.Kind, "kind");
            AssertEqual(phase, action.Phase, "phase");
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + ": expected " + expected + ", got " + actual);
            }
        }

        static void AssertNumber(long expected, object actual, string message)
        {
            long actualNumber = Convert.ToInt64(actual);
            if (expected != actualNumber)
            {
                throw new Exception(message + ": expected " + expected + ", got " + actual);
            }
        }

        static string Key(params int[] args)
        {
            return string.Join(",", args.Select(x => x.ToString()).ToArray());
        }

        static DecisionSnapshot CreateSnapshotWithTwoActions()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.MovablePhaseMask = (uint)(1 << (int)DuelPhase.Battle);
            query.CardNums[Key(0, 13)] = 1;
            query.CommandMasks[Key(0, 13, 0)] = (uint)(1 << (int)DuelCommandType.Attack);
            query.CardUniqueIds[Key(0, 13, 0)] = 12031;
            return LegalActionExtractor.Extract(query, 42, DuelViewType.WaitInput, 0);
        }

        static DecisionSnapshot CreateCardAwareSnapshot()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 13)] = 1;
            query.CardUniqueIds[Key(1, 13, 0)] = 901;
            query.CardIdsByUniqueId[901] = 4900;
            query.CommandMasks[Key(1, 13, 0)] = (uint)(1 << (int)DuelCommandType.Summon);
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[4900] = CreateCard(4900, "Cubic Seed", "Starts the Cubic line.");
            return LegalActionExtractor.Extract(query, 42, DuelViewType.WaitInput, 1, catalog);
        }

        static LlmCardMetadata CreateCard(int cardId, string name, string text)
        {
            return new LlmCardMetadata()
            {
                CardId = cardId,
                Name = name,
                Text = text,
                Kind = "Effect",
                Attribute = "Dark",
                Level = 4,
                Atk = 1600,
                Def = 1200,
                Scale = 0,
            };
        }

        static DecisionSnapshot CreateDialogSnapshot()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.DialogSelectItemNum = 1;
            query.DialogSelectItemEnabled[0] = 1;
            query.DialogSelectItemTextIds[0] = 1103;
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(query, 77, DuelViewType.RunDialog, 1);
            snapshot.LegalActions[0].DialogResult = 2;
            return snapshot;
        }

        static DecisionSnapshot CreateListSnapshot()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.ListItemMax = 1;
            query.ListItemIds[0] = 502;
            query.ListItemUniqueIds[0] = 9002;
            query.ListItemTargetUniqueIds[0] = 9102;
            query.ListItemMsgs[0] = 2102;
            query.ListItemAttributes[0] = 8;
            query.ListItemFroms[0] = 16;
            query.ListSelectMin = 1;
            query.ListSelectMax = 1;
            return LegalActionExtractor.Extract(query, 78, DuelViewType.RunList, 1);
        }

        static Dictionary<string, object> DeserializeObject(string json)
        {
            Dictionary<string, object> data = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
            if (data == null)
            {
                throw new Exception("expected serialized JSON object");
            }
            return data;
        }

        class FakeLegalActionQuery : ILegalActionQuery
        {
            public readonly Dictionary<string, int> CardNums = new Dictionary<string, int>();
            public readonly Dictionary<string, uint> CommandMasks = new Dictionary<string, uint>();
            public readonly Dictionary<string, int> AttackTargetMasks = new Dictionary<string, int>();
            public readonly Dictionary<string, int> CardUniqueIds = new Dictionary<string, int>();
            public readonly Dictionary<string, int> CardFaces = new Dictionary<string, int>();
            public readonly Dictionary<string, int> HandCardOpen = new Dictionary<string, int>();
            public readonly Dictionary<int, int> CardIdsByUniqueId = new Dictionary<int, int>();
            public readonly Dictionary<int, int> DialogSelectItemEnabled = new Dictionary<int, int>();
            public readonly Dictionary<int, int> DialogSelectItemTextIds = new Dictionary<int, int>();
            public readonly Dictionary<int, int> ListItemAttributes = new Dictionary<int, int>();
            public readonly Dictionary<int, int> ListItemFroms = new Dictionary<int, int>();
            public readonly Dictionary<int, int> ListItemIds = new Dictionary<int, int>();
            public readonly Dictionary<int, int> ListItemMsgs = new Dictionary<int, int>();
            public readonly Dictionary<int, int> ListItemTargetUniqueIds = new Dictionary<int, int>();
            public readonly Dictionary<int, int> ListItemUniqueIds = new Dictionary<int, int>();
            public uint MovablePhaseMask;
            public int CurrentPhase = (int)DuelPhase.Main1;
            public int CurrentStep;
            public int TurnNum = 3;
            public int TurnPlayer;
            public int DialogSelectItemNum;
            public int DialogCanYesNoSkip;
            public int ListItemMax;
            public int ListSelectMin;
            public int ListSelectMax;
            public int ListIsMultiMode;
            public int SummoningMonsterUniqueId;
            public int SummonPositionMask;
            public readonly Dictionary<int, int> LifePoints = new Dictionary<int, int>();

            public int GetCardNum(int player, int position)
            {
                return Get(CardNums, Key(player, position));
            }

            public uint GetCommandMask(int player, int position, int index)
            {
                return Get(CommandMasks, Key(player, position, index));
            }

            public int GetCardUniqueId(int player, int position, int index)
            {
                return Get(CardUniqueIds, Key(player, position, index));
            }

            public int GetCardFace(int player, int position, int index)
            {
                return Get(CardFaces, Key(player, position, index));
            }

            public int GetCardIdByUniqueId(int uniqueId)
            {
                return Get(CardIdsByUniqueId, uniqueId);
            }

            public int GetHandCardOpen(int player, int index)
            {
                return Get(HandCardOpen, Key(player, index));
            }

            public int GetLifePoints(int player)
            {
                return Get(LifePoints, player);
            }

            public uint GetMovablePhase()
            {
                return MovablePhaseMask;
            }

            public int GetCurrentPhase()
            {
                return CurrentPhase;
            }

            public int GetCurrentStep()
            {
                return CurrentStep;
            }

            public int GetTurnNum()
            {
                return TurnNum;
            }

            public int GetTurnPlayer()
            {
                return TurnPlayer;
            }

            public int GetAttackTargetMask(int player, int locate)
            {
                return Get(AttackTargetMasks, Key(player, locate));
            }

            public int GetDialogCanYesNoSkip()
            {
                return DialogCanYesNoSkip;
            }

            public int GetDialogSelectItemEnable(int index)
            {
                return Get(DialogSelectItemEnabled, index);
            }

            public int GetDialogSelectItemNum()
            {
                return DialogSelectItemNum;
            }

            public int GetDialogSelectItemTextId(int index)
            {
                return Get(DialogSelectItemTextIds, index);
            }

            public int GetListItemAttribute(int index)
            {
                return Get(ListItemAttributes, index);
            }

            public int GetListItemFrom(int index)
            {
                return Get(ListItemFroms, index);
            }

            public int GetListItemId(int index)
            {
                return Get(ListItemIds, index);
            }

            public int GetListItemMax()
            {
                return ListItemMax;
            }

            public int GetListItemMsg(int index)
            {
                return Get(ListItemMsgs, index);
            }

            public int GetListItemTargetUniqueId(int index)
            {
                return Get(ListItemTargetUniqueIds, index);
            }

            public int GetListItemUniqueId(int index)
            {
                return Get(ListItemUniqueIds, index);
            }

            public int GetListSelectMax()
            {
                return ListSelectMax;
            }

            public int GetListSelectMin()
            {
                return ListSelectMin;
            }

            public int GetListIsMultiMode()
            {
                return ListIsMultiMode;
            }

            public int GetSummoningMonsterUniqueId()
            {
                return SummoningMonsterUniqueId;
            }

            public int GetSummonPositionMask()
            {
                return SummonPositionMask;
            }

            static T Get<T>(Dictionary<int, T> values, int key)
            {
                T value;
                return values.TryGetValue(key, out value) ? value : default(T);
            }

            static T Get<T>(Dictionary<string, T> values, string key)
            {
                T value;
                return values.TryGetValue(key, out value) ? value : default(T);
            }
        }

        class FakeCardCatalog : ILlmCardCatalog
        {
            public readonly Dictionary<int, LlmCardMetadata> Cards = new Dictionary<int, LlmCardMetadata>();

            public LlmCardMetadata GetCard(int cardId)
            {
                LlmCardMetadata card;
                Cards.TryGetValue(cardId, out card);
                return card;
            }
        }

        class FakeBrokerTransport : ILlmBrokerTransport
        {
            readonly string responseJson;

            public int RequestCount { get; private set; }
            public string LastRequestJson { get; private set; }
            public int LastTimeoutMs { get; private set; }
            public Exception ExceptionToThrow { get; set; }
            public int DelayMs { get; set; }

            public FakeBrokerTransport(string responseJson)
            {
                this.responseJson = responseJson;
            }

            public string PostDecisionRequest(string requestJson, int timeoutMs)
            {
                RequestCount++;
                LastRequestJson = requestJson;
                LastTimeoutMs = timeoutMs;
                if (DelayMs > 0)
                {
                    Thread.Sleep(DelayMs);
                }
                if (ExceptionToThrow != null)
                {
                    throw ExceptionToThrow;
                }
                return responseJson;
            }
        }

        class SingleResponseHttpServer : IDisposable
        {
            readonly int statusCode;
            readonly string reasonPhrase;
            readonly string responseBody;
            TcpListener listener;
            Thread thread;
            Exception threadError;

            public string Url { get; private set; }

            public SingleResponseHttpServer(int statusCode, string reasonPhrase, string responseBody)
            {
                this.statusCode = statusCode;
                this.reasonPhrase = reasonPhrase;
                this.responseBody = responseBody;
            }

            public void Start()
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Url = "http://127.0.0.1:" + port + "/decide";
                thread = new Thread(ServeOnce);
                thread.Start();
            }

            public void Dispose()
            {
                if (listener != null)
                {
                    listener.Stop();
                }
                if (thread != null)
                {
                    thread.Join(2000);
                }
                if (threadError != null)
                {
                    throw new Exception("test HTTP server failed", threadError);
                }
            }

            void ServeOnce()
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = 2000;
                        byte[] requestBuffer = new byte[4096];
                        stream.Read(requestBuffer, 0, requestBuffer.Length);

                        byte[] bodyBytes = Encoding.UTF8.GetBytes(responseBody);
                        string header =
                            "HTTP/1.1 " + statusCode + " " + reasonPhrase + "\r\n" +
                            "Content-Type: application/json\r\n" +
                            "Content-Length: " + bodyBytes.Length + "\r\n" +
                            "Connection: close\r\n" +
                            "\r\n";
                        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                        stream.Write(headerBytes, 0, headerBytes.Length);
                        stream.Write(bodyBytes, 0, bodyBytes.Length);
                    }
                }
                catch (Exception e)
                {
                    threadError = e;
                }
            }
        }
    }
}

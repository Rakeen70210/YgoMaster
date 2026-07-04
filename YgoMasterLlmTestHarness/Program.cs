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
                ExtractsCommandActionsFromCommandMask();
                ExtractsIndexZeroCommandWhenCardNumIsZero();
                ExtractsCommandsAcrossAllIndexesUpToCardNum();
                ExtractsMovePhaseActionsFromPhaseMaskBeforeCommands();
                IgnoresNullPhaseBit();
                PreservesDecisionSnapshotMetadata();
                ExtractsPublicStateCountsAndKnownCards();
                DoesNotExposeHiddenPublicStateCards();
                ExtractsControlledPlayerPrivateHandCardMetadata();
                DoesNotExposeOpponentHiddenHandCardMetadata();
                FiltersDebugAndSurrenderCommandsFromBrokerActions();
                FiltersSingleDecideCommandFromBrokerActions();
                ExtractsSummonPlacementActionsFromPositionMask();
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
                SerializesBrokerDecisionRequest();
                SerializesBrokerRequestStartedWithSnapshotDetails();
                SerializesPublicStateInBrokerDecisionRequest();
                SerializesSchemaV3CardMetadataInBrokerDecisionRequest();
                ParsesBrokerDecisionResponse();
                ParsesBrokerDecisionResponseWithConfidenceAndPlan();
                ParsesBrokerErrorResponse();
                AcceptsBrokerResponseWithLegalActionId();
                RejectsBrokerResponseWithStaleRunEffectSeq();
                RejectsBrokerResponseWithUnknownActionId();
                AcceptsBrokerResponseWhenExpectedActionStillMatches();
                RejectsBrokerResponseWhenActionIdChangedMeaning();
                RejectsBrokerResponseWhenPhaseActionChangedMeaning();
                BrokerControlPolicyOnlyControlsLocalConfiguredPlayer();
                BrokerControlPolicySuppressesLocalControlledPendingRequest();
                BrokerControlPolicyRunsCpuThinkingForLocalControlledWhenNoRequestStarts();
                BrokerControlPolicyRunsCpuThinkingForUncontrolledRemotePlayer();
                BrokerClientPostsDecisionRequestAndReturnsLegalAction();
                BrokerClientRejectsInvalidJson();
                BrokerClientPreservesBrokerErrorResponseBody();
                HttpBrokerTransportPreservesErrorResponseBody();
                CreatesCommandCommitPlan();
                CreatesPhaseCommitPlan();
                CreatesSummonPlacementCommitPlan();
                ExtractsEnabledDialogResultActions();
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
                BrokerPlayerResolverPrefersTurnPlayerForWaitInput();
                BrokerPlayerResolverFallsBackToRivalTurnForDialog();
                ResettingNonReadyPlayerKeepsOpponentReadyAndPvpSession();
                ResettingMatchedPlayerKeepsOpponentReadyAndPvpSession();
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
            query.CardFaces[Key(1, 0, 0)] = 1;
            query.CardFaces[Key(1, 17, 0)] = 1;

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 13, DuelViewType.WaitInput, 0);

            AssertEqual(0, snapshot.PublicState.Players[1].KnownCards.Count, "hidden known cards");
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

        static void ExtractsSummonPlacementActionsFromPositionMask()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.SummoningMonsterUniqueId = 33;
            query.SummonPositionMask = (1 << 1) | (1 << 2);
            query.CardIdsByUniqueId[33] = 4927;
            query.MovablePhaseMask = (uint)(1 << (int)DuelPhase.End);

            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 132, DuelViewType.WaitInput, 1);

            AssertEqual(2, snapshot.LegalActions.Count, "summon placement action count");
            AssertCommand(snapshot.LegalActions[0], 0, 1, 1, 0, DuelCommandType.Decide, 33);
            AssertCommand(snapshot.LegalActions[1], 1, 1, 2, 0, DuelCommandType.Decide, 33);
            AssertEqual(4927, snapshot.LegalActions[0].CardId, "first placement card id");
            AssertEqual(4927, snapshot.LegalActions[1].CardId, "second placement card id");
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
            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot));
            List<object> actions = (List<object>)data["legal_actions"];

            AssertEqual("decision_window", data["kind"], "kind");
            AssertEqual((long)3, data["schema_version"], "schema_version");
            AssertEqual((long)42, data["run_effect_seq"], "run_effect_seq");
            AssertEqual("WaitInput", data["view_type"], "view_type");
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

        static void SerializesBrokerDecisionRequest()
        {
            DecisionSnapshot snapshot = CreateSnapshotWithTwoActions();
            Dictionary<string, object> data = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));

            AssertEqual("decision_request", data["kind"], "kind");
            AssertEqual((long)3, data["schema_version"], "schema_version");
            AssertEqual((long)42, data["run_effect_seq"], "run_effect_seq");
            AssertEqual("WaitInput", data["view_type"], "view_type");
            AssertEqual((long)0, data["acting_player"], "acting_player");
            AssertEqual((long)0, data["controlled_player"], "controlled_player");
            AssertEqual(2, ((List<object>)data["legal_actions"]).Count, "legal_actions count");
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

            AssertEqual((long)3, data["schema_version"], "schema_version");
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

            AssertEqual((long)3, data["schema_version"], "schema_version");
            AssertEqual((long)1, data["controlled_player"], "controlled_player");
            AssertNumber(4900, action["card_id"], "action card id");
            AssertEqual("Cubic Seed", actionCard["name"], "action card name");
            AssertEqual("Starts the Cubic line.", actionCard["text"], "action card text");
            AssertNumber(4900, knownCardMetadata["card_id"], "known card metadata id");
            AssertEqual("Cubic Seed", knownCardMetadata["name"], "known card name");
        }

        static void ParsesBrokerDecisionResponse()
        {
            LlmBrokerDecisionResponse response;
            string error;
            bool parsed = LlmBrokerProtocol.TryParseDecisionResponse(
                "{\"run_effect_seq\":42,\"action_id\":1,\"reason\":\"test\"}",
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
                "{\"run_effect_seq\":42,\"action_id\":1,\"reason\":\"summon Duza\",\"confidence\":0.74,\"plan\":\"develop first\"}",
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
                "{\"run_effect_seq\":42,\"action_id\":1}");

            LlmBrokerDecisionResult result = LlmBrokerClient.RequestDecision(snapshot, transport, 2000);

            AssertEqual(true, result.IsSuccess, "is_success");
            AssertEqual(null, result.Error, "error");
            AssertEqual(null, result.ErrorDetail, "error_detail");
            AssertEqual(1, transport.RequestCount, "request_count");
            AssertEqual(true, transport.LastRequestJson.Contains("\"kind\":\"decision_request\""), "request kind");
            AssertEqual(true, transport.LastRequestJson.Contains("\"public_state\""), "public state");
            AssertEqual(DuelCommandType.Attack, result.Action.Command, "command");
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
            LlmActionCommitPlan plan = LlmActionCommitPlan.FromLegalAction(snapshot.LegalActions[0]);

            AssertEqual(LlmActionCommitKind.Command, plan.Kind, "kind");
            AssertEqual(1, plan.Player, "player");
            AssertEqual(2, plan.Position, "position");
            AssertEqual(0, plan.Index, "index");
            AssertEqual((int)DuelCommandType.Decide, plan.CommandId, "command_id");
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

        static void BrokerControlPolicyRunsCpuThinkingForUncontrolledRemotePlayer()
        {
            AssertEqual(
                LlmBrokerViewHandling.RunCpuThinking,
                LlmBrokerControlPolicy.DecideViewHandling(
                    true, 1, 0, false, false, false),
                "remote uncontrolled view handling");
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

        static void BrokerPlayerResolverFallsBackToRivalTurnForDialog()
        {
            int player;
            bool resolved = LlmBrokerPlayerResolver.TryResolve(
                DuelViewType.RunDialog,
                0,
                0,
                0,
                0,
                1,
                out player);

            AssertEqual(true, resolved, "resolved");
            AssertEqual(1, player, "player");
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

            public FakeBrokerTransport(string responseJson)
            {
                this.responseJson = responseJson;
            }

            public string PostDecisionRequest(string requestJson, int timeoutMs)
            {
                RequestCount++;
                LastRequestJson = requestJson;
                LastTimeoutMs = timeoutMs;
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

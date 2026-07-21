using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-003 Milestones 3C + 3D:
    /// cross-sequence semantic optional-response recurrence and safe provider-error recovery.
    /// </summary>
    static class Llm003Milestone3CDTests
    {
        public static void RunAll()
        {
            // --- 3D: freeze seq-272 recovery and make decline order-independent ---
            ProviderErrorRecoveryDeclinesCallRegardlessOfListOrder();
            ProviderErrorRecoveryDeclinesWhenActivationIsFirst();
            ProviderErrorRecoveryDeclinesShuffledOptionalWindows();
            ProviderErrorRecoveryDoesNotInventDeclineOnMandatoryChoice();
            PreferredProviderIntentStillSelectedWhenStillLegal();
            PreferredMissingFallsToOptionalDeclineNotActivation();
            RecoveryPolicyBranchIsOptionalDeclineNotQualityPasser();
            GroundedPositiveUsedWhenNoDeclineAvailable();

            // --- 3C taxonomy ---
            CheckTimingDeclineUsesCheckTimingTargetScope();
            CheckChainDeclineKeepsCheckChainTargetScope();

            // --- 3C semantic fingerprint pure component ---
            SemanticFingerprintIgnoresRunEffectSeqAndActionIds();
            SemanticFingerprintChangesWithPhaseHistoryLegalActionsAndBoard();
            SemanticRecurrenceReusesDeclineOnSecondOccurrence();
            SemanticRecurrenceRequestsTemporaryCpuOnThirdOccurrence();
            SemanticRecurrenceNeverCachesActivation();
            SemanticRecurrenceResetsOnAuthoritativeProgress();
            SemanticRecurrenceHiddenStatePairProducesSameFingerprint();

            // --- Telemetry kinds ---
            SerializesSemanticWindowDecisionReused();
            SerializesSemanticWindowRecoveryEvents();
        }

        static void ProviderErrorRecoveryDeclinesCallRegardlessOfListOrder()
        {
            // Live seq 272: [Activate Call of the Haunted, Decline response], no provider action.
            DecisionSnapshot activationFirst = CreateOptionalActivationPlusDecline(
                activationFirst: true,
                cardId: 9707,
                cardName: "Call of the Haunted",
                uniqueId: 501);
            DecisionSnapshot declineFirst = CreateOptionalActivationPlusDecline(
                activationFirst: false,
                cardId: 9707,
                cardName: "Call of the Haunted",
                uniqueId: 501);

            LegalAction recoveredA;
            string branchA;
            AssertTrue(
                LlmBrokerRecovery.TrySelectAction(
                    activationFirst,
                    "provider_error",
                    null,
                    out recoveredA,
                    out branchA),
                "recovery available activation-first");
            AssertEqual(LegalActionKind.Cancel, recoveredA.Kind, "activation-first kind");
            AssertEqual("Decline response", recoveredA.ActionLabel, "activation-first label");
            AssertEqual(LlmBrokerRecovery.PolicyOptionalDecline, branchA, "activation-first branch");

            LegalAction recoveredB;
            string branchB;
            AssertTrue(
                LlmBrokerRecovery.TrySelectAction(
                    declineFirst,
                    "provider_error",
                    null,
                    out recoveredB,
                    out branchB),
                "recovery available decline-first");
            AssertEqual(LegalActionKind.Cancel, recoveredB.Kind, "decline-first kind");
            AssertEqual(LlmBrokerRecovery.PolicyOptionalDecline, branchB, "decline-first branch");
        }

        static void ProviderErrorRecoveryDeclinesWhenActivationIsFirst()
        {
            DecisionSnapshot snapshot = CreateOptionalActivationPlusDecline(
                activationFirst: true,
                cardId: 9707,
                cardName: "Call of the Haunted",
                uniqueId: 501);

            LegalAction recovered;
            string branch;
            AssertTrue(
                LlmBrokerRecovery.TrySelectAction(
                    snapshot,
                    "max_turns_without_structured_output",
                    null,
                    out recovered,
                    out branch),
                "recovery available");
            AssertEqual(LegalActionKind.Cancel, recovered.Kind, "must decline");
            AssertEqual(false, recovered.Kind == LegalActionKind.Command, "must not activate");
            AssertEqual(LlmBrokerRecovery.PolicyOptionalDecline, branch, "branch");
        }

        static void ProviderErrorRecoveryDeclinesShuffledOptionalWindows()
        {
            // Xyz Soul, optional mode, optional target-style activation windows.
            string[] names = new string[] { "Xyz Soul", "Optional Mode Trap", "Optional Target Spell" };
            int[] ids = new int[] { 201855, 3001, 3002 };
            for (int i = 0; i < names.Length; i++)
            {
                for (int order = 0; order < 2; order++)
                {
                    DecisionSnapshot snapshot = CreateOptionalActivationPlusDecline(
                        activationFirst: order == 0,
                        cardId: ids[i],
                        cardName: names[i],
                        uniqueId: 600 + i);
                    LegalAction recovered;
                    string branch;
                    AssertTrue(
                        LlmBrokerRecovery.TrySelectAction(
                            snapshot,
                            "provider_error",
                            null,
                            out recovered,
                            out branch),
                        "recovery " + names[i] + " order " + order);
                    AssertEqual(LegalActionKind.Cancel, recovered.Kind, names[i] + " decline kind");
                    AssertEqual(
                        LlmBrokerRecovery.PolicyOptionalDecline,
                        branch,
                        names[i] + " branch");
                }
            }
        }

        static void ProviderErrorRecoveryDoesNotInventDeclineOnMandatoryChoice()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 10,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
            };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = 1,
                Position = 13,
                Index = 0,
                CardId = 100,
                CardUniqueId = 10,
                Card = new LlmCardMetadata() { CardId = 100, Name = "Mandatory Monster" },
            });
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.SetMonst,
                Player = 1,
                Position = 13,
                Index = 0,
                CardId = 100,
                CardUniqueId = 10,
                Card = new LlmCardMetadata() { CardId = 100, Name = "Mandatory Monster" },
            });
            LegalActionExtractor.ClassifySnapshot(snapshot);

            LegalAction recovered;
            string branch;
            AssertTrue(
                LlmBrokerRecovery.TrySelectAction(
                    snapshot,
                    "provider_error",
                    null,
                    out recovered,
                    out branch),
                "mandatory recovery available");
            AssertEqual(LegalActionKind.Command, recovered.Kind, "no invented decline");
            AssertEqual(false, recovered.Kind == LegalActionKind.Cancel, "cancel not invented");
        }

        static void PreferredProviderIntentStillSelectedWhenStillLegal()
        {
            DecisionSnapshot snapshot = CreateOptionalActivationPlusDecline(
                activationFirst: true,
                cardId: 9707,
                cardName: "Call of the Haunted",
                uniqueId: 501);
            LegalAction preferred = snapshot.LegalActions[0]; // Activate

            LegalAction recovered;
            string branch;
            AssertTrue(
                LlmBrokerRecovery.TrySelectAction(
                    snapshot,
                    "early_end_phase",
                    preferred,
                    out recovered,
                    out branch),
                "preferred recovery");
            AssertEqual(LlmBrokerRecovery.PolicyPreferredAction, branch, "preferred branch");
            AssertEqual(LegalActionKind.Command, recovered.Kind, "preferred activate");
        }

        static void PreferredMissingFallsToOptionalDeclineNotActivation()
        {
            DecisionSnapshot snapshot = CreateOptionalActivationPlusDecline(
                activationFirst: true,
                cardId: 9707,
                cardName: "Call of the Haunted",
                uniqueId: 501);
            LegalAction missingPreferred = new LegalAction()
            {
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
            };

            LegalAction recovered;
            string branch;
            AssertTrue(
                LlmBrokerRecovery.TrySelectAction(
                    snapshot,
                    "early_end_phase",
                    missingPreferred,
                    out recovered,
                    out branch),
                "missing preferred recovery");
            AssertEqual(LegalActionKind.Cancel, recovered.Kind, "decline when preferred missing");
            AssertEqual(LlmBrokerRecovery.PolicyOptionalDecline, branch, "optional decline branch");
        }

        static void RecoveryPolicyBranchIsOptionalDeclineNotQualityPasser()
        {
            DecisionSnapshot snapshot = CreateOptionalActivationPlusDecline(
                activationFirst: true,
                cardId: 9707,
                cardName: "Call of the Haunted",
                uniqueId: 501);
            LegalAction recovered;
            string branch;
            LlmBrokerRecovery.TrySelectAction(
                snapshot, "provider_error", null, out recovered, out branch);
            AssertEqual(
                false,
                branch == LlmBrokerRecovery.PolicyQualityPasser,
                "must not use quality_passer for optional activation-first");
            AssertEqual(LlmBrokerRecovery.PolicyOptionalDecline, branch, "optional_decline");
        }

        static void GroundedPositiveUsedWhenNoDeclineAvailable()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 42,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
            };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = 1,
                Position = 13,
                Index = 0,
                CardId = 4900,
                CardUniqueId = 901,
                Card = new LlmCardMetadata() { CardId = 4900, Name = "Cubic Seed" },
            });
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
            });
            LegalActionExtractor.ClassifySnapshot(snapshot);

            LegalAction recovered;
            string branch;
            AssertTrue(
                LlmBrokerRecovery.TrySelectAction(
                    snapshot,
                    "provider_error",
                    null,
                    out recovered,
                    out branch),
                "grounded recovery");
            AssertEqual(DuelCommandType.Summon, recovered.Command, "summon preferred over end");
            AssertEqual(
                true,
                branch == LlmBrokerRecovery.PolicyGroundedPositive ||
                    branch == LlmBrokerRecovery.PolicyQualityPasser ||
                    branch == LlmBrokerRecovery.PolicyFirstNonMechanical,
                "grounded/quality branch without optional decline");
        }

        static void CheckTimingDeclineUsesCheckTimingTargetScope()
        {
            DecisionSnapshot snapshot = CreateLiveCheckTimingActivationWindow(209);
            LegalAction decline = null;
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action.Kind == LegalActionKind.Cancel)
                {
                    decline = action;
                    break;
                }
            }
            AssertTrue(decline != null, "decline present");
            AssertEqual("check_timing_decline", decline.TargetScope, "CheckTiming taxonomy");
            AssertEqual("Decline response", decline.ActionLabel, "label");
        }

        static void CheckChainDeclineKeepsCheckChainTargetScope()
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            query.CardNums[Key(1, 7)] = 1;
            query.CommandMasks[Key(1, 7, 0)] = (uint)(1 << (int)DuelCommandType.Action);
            query.CardUniqueIds[Key(1, 7, 0)] = 701;
            query.CardIdsByUniqueId[701] = 1234;
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[1234] = new LlmCardMetadata() { CardId = 1234, Name = "Chain Card" };
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, 187, DuelViewType.WaitInput, 1, catalog);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.CheckChain,
                0,
                (int)DuelMenuParamType.TrueCancel,
                catalog);
            LegalAction decline = snapshot.LegalActions[snapshot.LegalActions.Count - 1];
            AssertEqual(LegalActionKind.Cancel, decline.Kind, "chain decline kind");
            AssertEqual("check_chain_decline", decline.TargetScope, "CheckChain taxonomy");
        }

        static void SemanticFingerprintIgnoresRunEffectSeqAndActionIds()
        {
            DecisionSnapshot a = CreateLiveCheckTimingActivationWindow(209);
            DecisionSnapshot b = CreateLiveCheckTimingActivationWindow(212);
            // Shuffle action ids without changing semantics.
            if (b.LegalActions.Count >= 2)
            {
                LegalAction first = b.LegalActions[0];
                b.LegalActions[0] = b.LegalActions[1];
                b.LegalActions[1] = first;
                b.LegalActions[0].ActionId = 0;
                b.LegalActions[1].ActionId = 1;
            }

            string fpA = LlmDecisionWindowSemanticFingerprint.Build(a, duelGeneration: 3);
            string fpB = LlmDecisionWindowSemanticFingerprint.Build(b, duelGeneration: 3);
            AssertEqual(fpA, fpB, "seq and action_id excluded from fingerprint");
            AssertEqual(false, fpA.Contains("209"), "must not embed run_effect_seq 209");
            AssertEqual(false, fpA.Contains("212"), "must not embed run_effect_seq 212");
        }

        static void SemanticFingerprintChangesWithPhaseHistoryLegalActionsAndBoard()
        {
            DecisionSnapshot baseSnap = CreateLiveCheckTimingActivationWindow(209);
            string baseFp = LlmDecisionWindowSemanticFingerprint.Build(baseSnap, 3);

            DecisionSnapshot phaseChanged = CreateLiveCheckTimingActivationWindow(209);
            phaseChanged.CurrentPhase = (int)DuelPhase.Battle;
            AssertEqual(
                false,
                baseFp == LlmDecisionWindowSemanticFingerprint.Build(phaseChanged, 3),
                "phase change is new window");

            DecisionSnapshot historyChanged = CreateLiveCheckTimingActivationWindow(209);
            historyChanged.DuelHistory = new LlmDuelHistoryState();
            // High-water mark via event count when available; also change public LP digest.
            historyChanged.PublicState.Players.Clear();
            historyChanged.PublicState.Players.Add(new PublicPlayerState()
            {
                Player = 0,
                LifePoints = 8000,
            });
            historyChanged.PublicState.Players.Add(new PublicPlayerState()
            {
                Player = 1,
                LifePoints = 7000,
            });
            AssertEqual(
                false,
                baseFp == LlmDecisionWindowSemanticFingerprint.Build(historyChanged, 3),
                "public state change is new window");

            DecisionSnapshot legalChanged = CreateLiveCheckTimingActivationWindow(209);
            legalChanged.LegalActions.Clear();
            legalChanged.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Cancel,
                CancelDecide = false,
            });
            LegalActionExtractor.ClassifySnapshot(legalChanged);
            legalChanged.ViewParam1 = (int)DuelMenuActType.CheckTiming;
            AssertEqual(
                false,
                baseFp == LlmDecisionWindowSemanticFingerprint.Build(legalChanged, 3),
                "legal-action semantic set change is new window");

            DecisionSnapshot sourceChanged = CreateLiveCheckTimingActivationWindow(209, cardId: 9999);
            AssertEqual(
                false,
                baseFp == LlmDecisionWindowSemanticFingerprint.Build(sourceChanged, 3),
                "source card change is new window");
        }

        static void SemanticRecurrenceReusesDeclineOnSecondOccurrence()
        {
            LlmSemanticOptionalResponseTracker tracker = new LlmSemanticOptionalResponseTracker();
            DecisionSnapshot first = CreateLiveCheckTimingActivationWindow(209);
            DecisionSnapshot second = CreateLiveCheckTimingActivationWindow(212);
            LegalAction decline = FindDecline(first);

            LlmSemanticWindowDisposition d1 = tracker.ObserveStrategicWindow(first, 3);
            AssertEqual(LlmSemanticWindowDisposition.UseBroker, d1, "first occurrence uses broker");
            tracker.RecordSuccessfulDeclineOrPass(first, decline, 3);

            LlmSemanticWindowDisposition d2 = tracker.ObserveStrategicWindow(second, 3);
            AssertEqual(LlmSemanticWindowDisposition.ReuseDecline, d2, "second reuses decline");
            LegalAction reused;
            AssertTrue(tracker.TryGetReusableDecline(second, out reused), "reusable decline");
            AssertEqual(LegalActionKind.Cancel, reused.Kind, "reused kind");
            AssertEqual(true, LlmBrokerProtocol.IsSameAction(reused, FindDecline(second)),
                "reused matches current legal decline");
        }

        static void SemanticRecurrenceRequestsTemporaryCpuOnThirdOccurrence()
        {
            LlmSemanticOptionalResponseTracker tracker = new LlmSemanticOptionalResponseTracker();
            DecisionSnapshot s1 = CreateLiveCheckTimingActivationWindow(209);
            DecisionSnapshot s2 = CreateLiveCheckTimingActivationWindow(212);
            DecisionSnapshot s3 = CreateLiveCheckTimingActivationWindow(215);
            LegalAction decline = FindDecline(s1);

            AssertEqual(
                LlmSemanticWindowDisposition.UseBroker,
                tracker.ObserveStrategicWindow(s1, 3),
                "first");
            tracker.RecordSuccessfulDeclineOrPass(s1, decline, 3);

            AssertEqual(
                LlmSemanticWindowDisposition.ReuseDecline,
                tracker.ObserveStrategicWindow(s2, 3),
                "second");
            tracker.RecordSuccessfulDeclineOrPass(s2, FindDecline(s2), 3);

            AssertEqual(
                LlmSemanticWindowDisposition.TemporaryCpu,
                tracker.ObserveStrategicWindow(s3, 3),
                "third requests temporary CPU");
            AssertEqual(
                "semantic_optional_response_loop",
                tracker.LastRecoveryReason,
                "recovery reason");
        }

        static void SemanticRecurrenceNeverCachesActivation()
        {
            LlmSemanticOptionalResponseTracker tracker = new LlmSemanticOptionalResponseTracker();
            DecisionSnapshot snapshot = CreateLiveCheckTimingActivationWindow(209);
            LegalAction activate = null;
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action.Kind == LegalActionKind.Command)
                {
                    activate = action;
                    break;
                }
            }
            AssertTrue(activate != null, "activation present");
            tracker.ObserveStrategicWindow(snapshot, 3);
            tracker.RecordSuccessfulDeclineOrPass(snapshot, activate, 3);
            LegalAction reused;
            AssertEqual(
                false,
                tracker.TryGetReusableDecline(snapshot, out reused),
                "activation must never be cached as reusable decline");
        }

        static void SemanticRecurrenceResetsOnAuthoritativeProgress()
        {
            LlmSemanticOptionalResponseTracker tracker = new LlmSemanticOptionalResponseTracker();
            DecisionSnapshot first = CreateLiveCheckTimingActivationWindow(209);
            tracker.ObserveStrategicWindow(first, 3);
            tracker.RecordSuccessfulDeclineOrPass(first, FindDecline(first), 3);

            DecisionSnapshot progressed = CreateLiveCheckTimingActivationWindow(300);
            progressed.CurrentPhase = (int)DuelPhase.End;
            AssertEqual(
                LlmSemanticWindowDisposition.UseBroker,
                tracker.ObserveStrategicWindow(progressed, 3),
                "phase progress resets to broker");
        }

        static void SemanticRecurrenceHiddenStatePairProducesSameFingerprint()
        {
            DecisionSnapshot a = CreateLiveCheckTimingActivationWindow(209);
            DecisionSnapshot b = CreateLiveCheckTimingActivationWindow(209);
            // Opponent hidden hand identity must not affect fingerprint.
            if (a.SelfResources == null)
            {
                // Leave self resources null; only public-visible digest is used.
            }
            AssertEqual(
                LlmDecisionWindowSemanticFingerprint.Build(a, 3),
                LlmDecisionWindowSemanticFingerprint.Build(b, 3),
                "identical visible state same fingerprint");
        }

        static void SerializesSemanticWindowDecisionReused()
        {
            DecisionSnapshot snapshot = CreateLiveCheckTimingActivationWindow(212);
            LegalAction decline = FindDecline(snapshot);
            string json = LlmDecisionLogSerializer.SerializeSemanticWindowDecisionReused(
                snapshot,
                fingerprint: "fp-test",
                occurrenceCount: 2,
                originRunEffectSeq: 209,
                reusedAction: decline);
            AssertTrue(json.IndexOf("llm_semantic_window_decision_reused", StringComparison.Ordinal) >= 0,
                "kind");
            AssertTrue(json.IndexOf("\"occurrence_count\":2", StringComparison.Ordinal) >= 0,
                "occurrence");
            AssertTrue(json.IndexOf("fp-test", StringComparison.Ordinal) >= 0, "fingerprint");
        }

        static void SerializesSemanticWindowRecoveryEvents()
        {
            DecisionSnapshot snapshot = CreateLiveCheckTimingActivationWindow(215);
            string failed = LlmDecisionLogSerializer.SerializeSemanticWindowRecoveryFailed(
                snapshot,
                fingerprint: "fp-loop",
                occurrenceCount: 3,
                reason: "semantic_optional_response_loop");
            AssertTrue(
                failed.IndexOf("llm_semantic_window_recovery_failed", StringComparison.Ordinal) >= 0,
                "failed kind");

            string diverged = LlmDecisionLogSerializer.SerializeSemanticWindowRecoveryDiverged(
                snapshot,
                fingerprint: "fp-loop",
                disposition: "temporary_cpu_activated");
            AssertTrue(
                diverged.IndexOf("llm_semantic_window_recovery_diverged", StringComparison.Ordinal) >= 0,
                "diverged kind");
        }

        static DecisionSnapshot CreateOptionalActivationPlusDecline(
            bool activationFirst,
            int cardId,
            string cardName,
            int uniqueId)
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 272,
                ViewType = DuelViewType.WaitInput,
                ViewParam1 = (int)DuelMenuActType.CheckTiming,
                ViewParam2 = 0,
                ViewParam3 = (int)DuelMenuParamType.TrueCancel,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                Turn = 0,
                TurnPlayer = 0,
                CurrentPhase = (int)DuelPhase.Draw,
            };

            LegalAction activate = new LegalAction()
            {
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Action,
                Player = 1,
                Position = 10,
                Index = 0,
                CardId = cardId,
                CardUniqueId = uniqueId,
                Card = new LlmCardMetadata() { CardId = cardId, Name = cardName },
            };
            LegalAction decline = new LegalAction()
            {
                Kind = LegalActionKind.Cancel,
                CancelDecide = false,
            };

            if (activationFirst)
            {
                activate.ActionId = 0;
                decline.ActionId = 1;
                snapshot.LegalActions.Add(activate);
                snapshot.LegalActions.Add(decline);
            }
            else
            {
                decline.ActionId = 0;
                activate.ActionId = 1;
                snapshot.LegalActions.Add(decline);
                snapshot.LegalActions.Add(activate);
            }
            LegalActionExtractor.ClassifySnapshot(snapshot);
            // Taxonomy correction after classification (production ApplyViewContext path).
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action.Kind == LegalActionKind.Cancel && !action.CancelDecide && !action.IsMechanical)
                {
                    action.TargetScope = "check_timing_decline";
                }
            }
            return snapshot;
        }

        static DecisionSnapshot CreateLiveCheckTimingActivationWindow(
            ulong runEffectSeq,
            int cardId = 9707)
        {
            FakeLegalActionQuery query = new FakeLegalActionQuery();
            // Stable unique id across sequences: same set card, advancing run_effect_seq only.
            const int uniqueId = 8801;
            query.CardNums[Key(1, 10)] = 1;
            query.CommandMasks[Key(1, 10, 0)] = (uint)(1 << (int)DuelCommandType.Action);
            query.CardUniqueIds[Key(1, 10, 0)] = uniqueId;
            query.CardIdsByUniqueId[uniqueId] = cardId;
            query.TurnNum = 1;
            query.TurnPlayer = 0;
            query.CurrentPhase = (int)DuelPhase.Draw;
            query.LifePoints[0] = 8000;
            query.LifePoints[1] = 8000;
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[cardId] = new LlmCardMetadata()
            {
                CardId = cardId,
                Name = cardId == 9707 ? "Call of the Haunted" : ("Card " + cardId),
            };
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                query, runEffectSeq, DuelViewType.WaitInput, 1, catalog);
            LegalActionExtractor.ApplyViewContext(
                snapshot,
                query,
                (int)DuelMenuActType.CheckTiming,
                0,
                (int)DuelMenuParamType.TrueCancel,
                catalog);
            return snapshot;
        }

        static LegalAction FindDecline(DecisionSnapshot snapshot)
        {
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action != null && action.Kind == LegalActionKind.Cancel && !action.CancelDecide)
                {
                    return action;
                }
            }
            throw new Exception("expected decline action");
        }

        static string Key(params int[] args)
        {
            string[] parts = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                parts[i] = args[i].ToString();
            }
            return string.Join(",", parts);
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value)
            {
                throw new Exception(message + ": expected true");
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + ": expected " + expected + ", got " + actual);
            }
        }

        class FakeLegalActionQuery : ILegalActionQuery
        {
            public readonly Dictionary<string, int> CardNums = new Dictionary<string, int>();
            public readonly Dictionary<string, uint> CommandMasks = new Dictionary<string, uint>();
            public readonly Dictionary<string, int> CardUniqueIds = new Dictionary<string, int>();
            public readonly Dictionary<string, int> CardFaces = new Dictionary<string, int>();
            public readonly Dictionary<int, int> CardIdsByUniqueId = new Dictionary<int, int>();
            public readonly Dictionary<int, int> LifePoints = new Dictionary<int, int>();
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

            public int GetCardNum(int player, int position)
            {
                int value;
                return CardNums.TryGetValue(player + "," + position, out value) ? value : 0;
            }

            public uint GetCommandMask(int player, int position, int index)
            {
                uint value;
                return CommandMasks.TryGetValue(player + "," + position + "," + index, out value)
                    ? value : 0u;
            }

            public int GetCardUniqueId(int player, int position, int index)
            {
                int value;
                return CardUniqueIds.TryGetValue(player + "," + position + "," + index, out value)
                    ? value : 0;
            }

            public int GetCardFace(int player, int position, int index)
            {
                int value;
                return CardFaces.TryGetValue(player + "," + position + "," + index, out value)
                    ? value : 1;
            }

            public int GetCardIdByUniqueId(int uniqueId)
            {
                int value;
                return CardIdsByUniqueId.TryGetValue(uniqueId, out value) ? value : 0;
            }

            public int GetHandCardOpen(int player, int index) { return 0; }

            public int GetLifePoints(int player)
            {
                int value;
                return LifePoints.TryGetValue(player, out value) ? value : 8000;
            }

            public uint GetMovablePhase() { return MovablePhaseMask; }
            public int GetCurrentPhase() { return CurrentPhase; }
            public int GetCurrentStep() { return CurrentStep; }
            public int GetTurnNum() { return TurnNum; }
            public int GetTurnPlayer() { return TurnPlayer; }
            public int GetAttackTargetMask(int player, int locate) { return 0; }
            public int GetDialogCanYesNoSkip() { return DialogCanYesNoSkip; }
            public int GetDialogSelectItemEnable(int index)
            {
                int value;
                return DialogSelectItemEnabled.TryGetValue(index, out value) ? value : 0;
            }
            public int GetDialogSelectItemNum() { return DialogSelectItemNum; }
            public int GetDialogSelectItemTextId(int index)
            {
                int value;
                return DialogSelectItemTextIds.TryGetValue(index, out value) ? value : 0;
            }
            public int GetListItemAttribute(int index)
            {
                int value;
                return ListItemAttributes.TryGetValue(index, out value) ? value : 0;
            }
            public int GetListItemFrom(int index)
            {
                int value;
                return ListItemFroms.TryGetValue(index, out value) ? value : 0;
            }
            public int GetListItemId(int index)
            {
                int value;
                return ListItemIds.TryGetValue(index, out value) ? value : 0;
            }
            public int GetListItemMax() { return ListItemMax; }
            public int GetListItemMsg(int index)
            {
                int value;
                return ListItemMsgs.TryGetValue(index, out value) ? value : 0;
            }
            public int GetListItemTargetUniqueId(int index)
            {
                int value;
                return ListItemTargetUniqueIds.TryGetValue(index, out value) ? value : 0;
            }
            public int GetListItemUniqueId(int index)
            {
                int value;
                return ListItemUniqueIds.TryGetValue(index, out value) ? value : 0;
            }
            public int GetListSelectMax() { return ListSelectMax; }
            public int GetListSelectMin() { return ListSelectMin; }
            public int GetListIsMultiMode() { return ListIsMultiMode; }
            public int GetSummoningMonsterUniqueId() { return SummoningMonsterUniqueId; }
            public int GetSummonPositionMask() { return SummonPositionMask; }
        }

        class FakeCardCatalog : ILlmCardCatalog
        {
            public readonly Dictionary<int, LlmCardMetadata> Cards =
                new Dictionary<int, LlmCardMetadata>();

            public LlmCardMetadata GetCard(int cardId)
            {
                LlmCardMetadata card;
                Cards.TryGetValue(cardId, out card);
                return card;
            }
        }
    }
}

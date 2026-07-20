using System;
using System.Collections.Generic;

namespace YgoMaster
{
    static class Llm005ApplicabilityFollowupTests
    {
        const int SourceCardId = 910001;
        const int ImmuneTargetCardId = 910002;
        const int UnknownTargetCardId = 910003;
        const int CastelCardId = 11263;

        public static void RunAll()
        {
            BlockedEffectIsProjectedAndRejected();
            LiveBeeNovaCatalogRegressionIsBlocked();
            ApplicabilityPositiveAndUnknownControlsFailOpenCorrectly();
            RecoveryNeverReusesGroundedBlockedAction();
            ParsesStructuredIntendedFollowups();
            ReconcilesPromisedFollowupAtPostSummonBoundary();
        }

        static void BlockedEffectIsProjectedAndRejected()
        {
            RegisterApplicabilityFacts();
            DecisionSnapshot snapshot = BuildApplicabilitySnapshot(1600, ImmuneTargetCardId);
            LegalAction blocked = snapshot.LegalActions[0];

            AssertNotNull(blocked.EffectApplicability, "blocked applicability annotation");
            AssertTrue(blocked.EffectApplicability.IsGrounded, "blocked applicability grounded");
            AssertEqual(false, blocked.EffectApplicability.EffectExpectedToApply,
                "matching immunity blocks activated monster effect");
            AssertEqual("blocked_by_activated_monster_effect_immunity",
                blocked.EffectApplicability.Reason, "blocked reason");

            string request = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            AssertContains(request, "effect_applicability", "request applicability projection");
            AssertContains(request, "effect_expected_to_apply\":false",
                "request blocked applicability value");
            AssertContains(request, "source_original_atk\":1600", "request source ATK fact");
            AssertContains(request, "source_original_atk_at_most\":3000",
                "request immunity threshold");

            LlmBrokerValidationResult validation = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                Response(snapshot.RunEffectSeq, blocked.ActionId));
            AssertFalse(validation.IsValid, "blocked action must be rejected");
            AssertEqual("effect_applicability_contradiction", validation.Error,
                "blocked action rejection code");
            AssertTrue(validation.Action == blocked, "rejection preserves matched action for audit");
        }

        static void ApplicabilityPositiveAndUnknownControlsFailOpenCorrectly()
        {
            RegisterApplicabilityFacts();

            DecisionSnapshot thresholdMiss = BuildApplicabilitySnapshot(3200, ImmuneTargetCardId);
            AssertEqual(true,
                thresholdMiss.LegalActions[0].EffectApplicability.EffectExpectedToApply,
                "source above immunity threshold applies");
            AssertTrue(LlmBrokerProtocol.ValidateResponse(
                thresholdMiss,
                Response(thresholdMiss.RunEffectSeq, 0)).IsValid,
                "positive applicability control remains legal");

            DecisionSnapshot unknown = BuildApplicabilitySnapshot(1600, UnknownTargetCardId);
            AssertNotNull(unknown.LegalActions[0].EffectApplicability,
                "unknown applicability annotation");
            AssertFalse(unknown.LegalActions[0].EffectApplicability.IsGrounded,
                "unknown semantics not grounded");
            AssertEqual(null, unknown.LegalActions[0].EffectApplicability.EffectExpectedToApply,
                "unknown semantics has nullable outcome");
            AssertTrue(LlmBrokerProtocol.ValidateResponse(
                unknown,
                Response(unknown.RunEffectSeq, 0)).IsValid,
                "unknown applicability fails open");

            DecisionSnapshot spellSource = BuildApplicabilitySnapshot(1600, ImmuneTargetCardId);
            spellSource.LegalActions[0].Card.Kind = "Magic";
            spellSource.LegalActions[0].Card.SummonFamily = "spell";
            LegalActionExtractor.ClassifySnapshot(spellSource);
            AssertEqual(true,
                spellSource.LegalActions[0].EffectApplicability.EffectExpectedToApply,
                "monster-effect immunity does not block a Spell source");
        }

        static void LiveBeeNovaCatalogRegressionIsBlocked()
        {
            DecisionSnapshot snapshot = BuildApplicabilitySnapshot(1600, 12522);
            LegalAction action = snapshot.LegalActions[0];
            action.CardId = 8742;
            action.Card.CardId = 8742;
            action.Card.Name = "Armored Bee";
            LegalActionExtractor.ClassifySnapshot(snapshot);
            AssertNotNull(action.EffectApplicability, "live Bee/Nova applicability");
            AssertEqual(false, action.EffectApplicability.EffectExpectedToApply,
                "live Bee/Nova structured catalog regression");
        }

        static void RecoveryNeverReusesGroundedBlockedAction()
        {
            RegisterApplicabilityFacts();
            DecisionSnapshot snapshot = BuildApplicabilitySnapshot(1600, ImmuneTargetCardId);
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
            });
            LegalActionExtractor.ClassifySnapshot(snapshot);

            LegalAction selected;
            string branch;
            bool recovered = LlmBrokerRecovery.TrySelectAction(
                snapshot,
                "effect_applicability_contradiction",
                snapshot.LegalActions[0],
                out selected,
                out branch);
            AssertTrue(recovered, "recovery finds non-blocked action");
            AssertEqual(1, selected.ActionId, "recovery must not reuse blocked preferred action");
        }

        static void ParsesStructuredIntendedFollowups()
        {
            string json = "{"
                + "\"run_effect_seq\":44,\"action_id\":2,\"reason\":\"Summon Castel for removal\","
                + "\"confidence\":0.9,\"plan\":\"Activate Castel after summon\","
                + "\"opponent_board_assessment\":\"public board\","
                + "\"opponent_action_assessment\":\"\",\"history_event_ids_used\":[],"
                + "\"why_now\":\"remove threat\",\"alternatives_considered\":[],\"risk\":\"effect may be unavailable\","
                + "\"intended_followups\":[{\"action_family\":\"effect_activation\","
                + "\"card_id\":11263,\"card_name\":\"Castel, the Skyblaster Musketeer\","
                + "\"description\":\"detach two to shuffle\"}]}";
            LlmBrokerDecisionResponse response;
            string error;
            AssertTrue(LlmBrokerProtocol.TryParseDecisionResponse(json, out response, out error),
                "parse intended followups: " + error);
            AssertEqual(1, response.IntendedFollowups.Count, "intended followup count");
            AssertEqual("effect_activation", response.IntendedFollowups[0].ActionFamily,
                "intended followup family");
            AssertEqual(CastelCardId, response.IntendedFollowups[0].CardId,
                "intended followup card id");
        }

        static void ReconcilesPromisedFollowupAtPostSummonBoundary()
        {
            LlmPromisedFollowupTracker tracker = new LlmPromisedFollowupTracker();
            DecisionSnapshot origin = new DecisionSnapshot()
            {
                RunEffectSeq = 90,
                Turn = 5,
                ActingPlayer = 1,
                ControlledPlayer = 1,
            };
            LegalAction summon = new LegalAction()
            {
                ActionId = 3,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.SummonSp,
                CardId = CastelCardId,
                Card = Card(CastelCardId, "Castel, the Skyblaster Musketeer", 2000, "Xyz"),
            };
            LlmBrokerDecisionResponse response = Response(origin.RunEffectSeq, summon.ActionId);
            response.IntendedFollowups.Add(new LlmBrokerIntendedFollowup()
            {
                ActionFamily = "effect_activation",
                CardId = CastelCardId,
                CardName = "Castel, the Skyblaster Musketeer",
                Description = "detach two to shuffle",
            });
            tracker.RecordCommittedAction(origin, summon, response, 7);

            DecisionSnapshot available = PostSummonSnapshot(100, includeEffectAction: true);
            LlmPromisedFollowupEvaluation matched = tracker.Evaluate(available, 7);
            AssertNotNull(matched, "matched followup evaluation");
            AssertEqual("matched", matched.Status, "matching effect action status");
            AssertEqual(0, matched.ActionId, "matching effect action id");
            AssertEqual(null, tracker.Evaluate(available, 7), "terminal match logs only once");

            tracker.Reset();
            tracker.RecordCommittedAction(origin, summon, response, 7);
            DecisionSnapshot unavailable = PostSummonSnapshot(101, includeEffectAction: false);
            LlmPromisedFollowupEvaluation miss = tracker.Evaluate(unavailable, 7);
            AssertNotNull(miss, "unavailable followup evaluation");
            AssertEqual("unavailable", miss.Status, "unavailable effect status");
            AssertEqual("effect_not_legal", miss.Reason, "unavailable effect reason");
            string audit = LlmDecisionLogSerializer.SerializeIntendedFollowupEvaluation(miss);
            AssertContains(audit, "intended_followup_unavailable", "unavailable audit kind");
            AssertContains(audit, "effect_not_legal", "unavailable audit reason");
        }

        static DecisionSnapshot BuildApplicabilitySnapshot(int sourceAtk, int targetCardId)
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 77,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                Turn = 5,
                TurnPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            PublicPlayerState self = new PublicPlayerState() { Player = 1, LifePoints = 8000 };
            PublicPlayerState opponent = new PublicPlayerState() { Player = 0, LifePoints = 8000 };
            opponent.KnownCards.Add(new PublicKnownCard()
            {
                Player = 0,
                Position = 2,
                Index = 0,
                CardUniqueId = 22,
                CardId = targetCardId,
                Face = 1,
                Card = Card(targetCardId, "Public target", 3000, "Effect"),
            });
            snapshot.PublicState.Players.Add(opponent);
            snapshot.PublicState.Players.Add(self);
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Player = 1,
                Position = 2,
                Index = 0,
                Command = DuelCommandType.Action,
                CardUniqueId = 11,
                CardId = SourceCardId,
                Card = Card(SourceCardId, "ATK modifier monster", sourceAtk, "Effect"),
            });
            LegalActionExtractor.ClassifySnapshot(snapshot);
            return snapshot;
        }

        static DecisionSnapshot PostSummonSnapshot(ulong seq, bool includeEffectAction)
        {
            List<LlmSelfResourceCard> field = new List<LlmSelfResourceCard>();
            field.Add(LlmSelfResourceCard.Create(
                CastelCardId,
                "Castel, the Skyblaster Musketeer",
                2,
                0,
                1,
                true,
                4,
                4,
                true,
                false,
                null,
                "Xyz",
                "xyz",
                false,
                "Xyz",
                2000,
                1500,
                string.Empty,
                0));
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = seq,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                Turn = 5,
                TurnPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
                SelfResources = LlmSelfResources.Create(
                    1,
                    LlmPublicVisibilityMode.RuntimeDllField,
                    5,
                    (int)DuelPhase.Main1,
                    0,
                    null,
                    field,
                    null,
                    null,
                    null),
            };
            if (includeEffectAction)
            {
                snapshot.LegalActions.Add(new LegalAction()
                {
                    ActionId = 0,
                    Kind = LegalActionKind.Command,
                    Command = DuelCommandType.Action,
                    CardId = CastelCardId,
                    CardUniqueId = 99,
                    Card = Card(CastelCardId, "Castel, the Skyblaster Musketeer", 2000, "Xyz"),
                });
            }
            else
            {
                snapshot.LegalActions.Add(new LegalAction()
                {
                    ActionId = 0,
                    Kind = LegalActionKind.MovePhase,
                    Phase = DuelPhase.Battle,
                });
            }
            LegalActionExtractor.ClassifySnapshot(snapshot);
            return snapshot;
        }

        static void RegisterApplicabilityFacts()
        {
            LlmCardEffectCapabilityCatalog.Register(new LlmCardEffectCapabilityFacts()
            {
                CardId = SourceCardId,
                TargetsOpponentMonster = true,
                PrimaryEffectCapability = "modify_atk",
                ApplicabilityGrounded = true,
                CapabilitiesGrounded = true,
            });
            LlmCardEffectCapabilityCatalog.Register(new LlmCardEffectCapabilityFacts()
            {
                CardId = ImmuneTargetCardId,
                UnaffectedByActivatedMonsterEffectsFromSourceOriginalAtkAtMost = 3000,
                ApplicabilityGrounded = true,
            });
        }

        static LlmBrokerDecisionResponse Response(ulong seq, int actionId)
        {
            return new LlmBrokerDecisionResponse()
            {
                RunEffectSeq = seq,
                ActionId = actionId,
                Reason = "Choose the named legal action for the grounded line.",
                Confidence = 0.9,
                OpponentActionAssessment = string.Empty,
                HistoryEventIdsUsed = new List<long>(),
                IntendedFollowups = new List<LlmBrokerIntendedFollowup>(),
            };
        }

        static LlmCardMetadata Card(int id, string name, int atk, string kind)
        {
            return new LlmCardMetadata()
            {
                CardId = id,
                Name = name,
                Atk = atk,
                Kind = kind,
                SummonFamily = kind == "Xyz" ? "xyz" : "main_deck_monster",
            };
        }

        static void AssertContains(string value, string expected, string message)
        {
            if (value == null || value.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new Exception(message + ": missing " + expected);
            }
        }

        static void AssertNotNull(object value, string message)
        {
            if (value == null) throw new Exception(message + ": expected non-null");
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value) throw new Exception(message + ": expected true");
        }

        static void AssertFalse(bool value, string message)
        {
            if (value) throw new Exception(message + ": expected false");
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + ": expected=" + expected + " actual=" + actual);
            }
        }
    }
}

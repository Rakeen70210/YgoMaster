using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-005 Slice 0B: immediate-outcome contract fixtures (TDD RED).
    ///
    /// Tests bind production APIs by name via reflection so the harness compiles before
    /// types exist. Do not implement production code here (Slice 2B owns
    /// LlmImmediateOutcomeAnalyzer and related types).
    ///
    /// Contract surface (Slice 2B):
    ///   LlmImmediateOutcomeAnalyzer
    ///     .Analyze / .Annotate(DecisionSnapshot, LegalAction, LlmImmediateOutcomeInput)
    ///     .ValidateProviderResponse(annotation, providerResponse) → outcome_contradiction
    ///   LlmImmediateOutcomeInput  — grounded chain + capability + secondary facts
    ///   LlmImmediateOutcomeAnnotation — primary_effect_stopped, target_effect_expected_to_resolve,
    ///     primary_disruption_value, response capabilities, secondary benefits, provenance,
    ///     uncertainty / unknown_card_semantics boundary
    ///
    /// Named cards appear only as fixture identities; rules must be generic (no blacklist).
    /// </summary>
    static class Llm005Slice0BTests
    {
        // Master Duel internal CardIds (authoritative). Source: YgoMaster/Data/YdkIds.txt
        // Passcode→internal: 60082869→4988, 35726888→12800, 41420027→4861, 44656491→4938,
        // 23434538→9455, 22046459→4663. Do not use YGOPro passcodes as CardId.
        const int DustTornadoInternalCardId = 4988;
        const int FoolishBurialGoodsInternalCardId = 12800;
        const int SolemnJudgmentInternalCardId = 4861;
        const int ContinuousSpellInternalCardId = 4938; // Messenger of Peace
        const int FieldSpellInternalCardId = 9455; // Field Spell control (Terraforming target shape)
        const int EquipSpellInternalCardId = 4663; // United We Stand
        // Backward-compatible aliases used in this fixture file.
        const int DustTornadoCardId = DustTornadoInternalCardId;
        const int FoolishBurialGoodsCardId = FoolishBurialGoodsInternalCardId;
        const int SolemnJudgmentCardId = SolemnJudgmentInternalCardId;
        const int ContinuousSpellCardId = ContinuousSpellInternalCardId;
        const int FieldSpellCardId = FieldSpellInternalCardId;
        const int EquipSpellCardId = EquipSpellInternalCardId;

        const string DustTornadoName = "Dust Tornado";
        const string FoolishBurialGoodsName = "Foolish Burial Goods";
        const string SolemnJudgmentName = "Solemn Judgment";
        const string ContinuousSpellName = "Messenger of Peace";
        const string FieldSpellName = "FIELD_SPELL_REMAIN_FACEUP_CTRL";
        const string EquipSpellName = "EQUIP_SPELL_REMAIN_FACEUP_CTRL";

        const int SentinelOpponentHandId = 99105051;
        const string SentinelOpponentHandName = "SENTINEL_OPP_HAND_LEAK_LLM005_0B";
        const int SentinelOpponentSetId = 99105052;
        const string SentinelOpponentSetName = "SENTINEL_OPP_SET_LEAK_LLM005_0B";
        const int SentinelOpponentExtraId = 99105053;
        const string SentinelOpponentExtraName = "SENTINEL_OPP_EXTRA_LEAK_LLM005_0B";

        const int ControlledPlayer = 1;
        const int OpponentPlayer = 0;
        const ulong FixtureRunEffectSeq = 710;
        const int OwnSpellTrapZone = 5;
        const int OpponentSpellTrapZone = 5;
        const int RuntimeFaceDown = 0;
        const int RuntimeFaceUp = 1;

        const int DustTornadoRootActionId = 0;
        const int DeclineRootActionId = 1;
        const int TargetFoolishActionId = 2;

        public static void RunAll()
        {
            // Probe the Slice 2B production surface first so RED names the intended
            // missing outcome contract (not a later fixture typo).
            RequireType("LlmImmediateOutcomeAnalyzer");
            RequireType("LlmImmediateOutcomeInput");
            RequireType("LlmImmediateOutcomeAnnotation");

            DustTornadoFoolishBurialGoodsRegression();
            DestroyIsNotNegate();
            ActualNegationControl();
            RemainFaceUpControl();
            SecondaryBenefitAccounting();
            UnknownSemanticsFailOpen();
            ProviderResponseOutcomeConsistency();
            ProjectionAndAuditHiddenInformationSafety();
        }

        // ---------------------------------------------------------------------
        // 1. Dust Tornado / Foolish Burial Goods regression
        // ---------------------------------------------------------------------
        static void DustTornadoFoolishBurialGoodsRegression()
        {
            ChainResponseScenario scenario = CreateDustTornadoVsActivatedOneShotScenario();
            object annotation = AnalyzeRoot(scenario, scenario.DestroyOnlyRoot);

            AssertRootRepresented(scenario, scenario.DestroyOnlyRoot.ActionId,
                "Dust Tornado root must remain legal/represented");
            AssertEqual(true, GetBoolPropFlexible(annotation,
                    "TargetAlreadyActivated", "TargetIsAlreadyActivated", "TargetActivatedOnChain"),
                "target must be marked already activated on the chain");
            AssertEqual(true, GetBoolPropFlexible(annotation,
                    "TargetEffectExpectedToResolve", "PrimaryEffectExpectedToResolve"),
                "one-shot Normal Spell effect expected to resolve after destroy-only response");
            AssertEqual(false, GetBoolPropFlexible(annotation,
                    "PrimaryEffectStopped", "PrimaryEffectPrevented"),
                "primary_effect_stopped must be false for destroy-only vs activated one-shot");
            AssertEqual(0, GetNumericPropFlexible(annotation,
                    "PrimaryDisruptionValue", "PrimaryDisruptionScore", "NegationValue"),
                "primary_disruption_value must be 0");
            AssertTrue(CapabilitiesInclude(annotation, "destroy"),
                "response capabilities must include destroy");
            AssertFalse(CapabilitiesInclude(annotation, "negate_activation")
                    || CapabilitiesInclude(annotation, "negate_effect"),
                "destroy-only Dust Tornado must not claim negate_* capability");
            AssertFalse(AnnotationClaimsNegationOrInterruption(annotation),
                "annotation must not claim negation/interruption of Foolish Burial Goods");
        }

        // ---------------------------------------------------------------------
        // 2. Destroy is not negate
        // ---------------------------------------------------------------------
        static void DestroyIsNotNegate()
        {
            ChainResponseScenario scenario = CreateDustTornadoVsActivatedOneShotScenario();
            object annotation = AnalyzeRoot(scenario, scenario.DestroyOnlyRoot);

            AssertEqual(false, GetBoolPropFlexible(annotation,
                    "PrimaryEffectStopped", "PrimaryEffectPrevented"),
                "destroy-only response cannot receive primary_effect_stopped: true");
            AssertEqual(0, GetNumericPropFlexible(annotation,
                    "PrimaryDisruptionValue", "PrimaryDisruptionScore", "NegationValue"),
                "destroy-only cannot receive negation/prevention value");
            AssertFalse(AnnotationClaimsNegationOrInterruption(annotation),
                "destroy-only cannot claim the chain effect was interrupted");

            // Provider-facing projection of the annotation must also refuse interruption language.
            string projected = SerializeAnnotation(annotation);
            AssertFalse(
                projected.IndexOf("negate", StringComparison.OrdinalIgnoreCase) >= 0
                && (projected.IndexOf("primary_effect_stopped\":true", StringComparison.OrdinalIgnoreCase) >= 0
                    || projected.IndexOf("\"primary_effect_stopped\": true", StringComparison.OrdinalIgnoreCase) >= 0
                    || projected.IndexOf("PrimaryEffectStopped\":true", StringComparison.OrdinalIgnoreCase) >= 0),
                "serialized annotation must not couple destroy with stopped/negated primary effect");
            AssertFalse(
                ContainsPhrase(projected, "interrupted")
                || ContainsPhrase(projected, "prevents resolution")
                || ContainsPhrase(projected, "stops the effect"),
                "serialized annotation must not claim the activated effect was interrupted");
        }

        // ---------------------------------------------------------------------
        // 3. Actual negation control
        // ---------------------------------------------------------------------
        static void ActualNegationControl()
        {
            ChainResponseScenario scenario = CreateNegationVsActivatedOneShotScenario();
            object annotation = AnalyzeRoot(scenario, scenario.NegateRoot);

            AssertTrue(CapabilitiesInclude(annotation, "negate_activation")
                    || CapabilitiesInclude(annotation, "negate_effect"),
                "negation control must carry grounded negate_activation or negate_effect");
            AssertEqual(true, GetBoolPropFlexible(annotation,
                    "PrimaryEffectStopped", "PrimaryEffectPrevented"),
                "grounded negation is allowed to receive primary_effect_stopped: true");
            AssertTrue(
                GetNumericPropFlexible(annotation,
                    "PrimaryDisruptionValue", "PrimaryDisruptionScore", "NegationValue") > 0,
                "grounded negation may receive positive prevention/disruption value");
            AssertEqual(false, GetBoolPropFlexible(annotation,
                    "TargetEffectExpectedToResolve", "PrimaryEffectExpectedToResolve"),
                "negated one-shot must not be expected to resolve unchanged");
        }

        // ---------------------------------------------------------------------
        // 4. Remain-face-up control (Continuous / Field / Equip)
        // ---------------------------------------------------------------------
        static void RemainFaceUpControl()
        {
            foreach (ChainResponseScenario scenario in new[]
            {
                CreateDestroyVsRemainFaceUpScenario("continuous", ContinuousSpellCardId, ContinuousSpellName),
                CreateDestroyVsRemainFaceUpScenario("field", FieldSpellCardId, FieldSpellName),
                CreateDestroyVsRemainFaceUpScenario("equip", EquipSpellCardId, EquipSpellName),
            })
            {
                object annotation = AnalyzeRoot(scenario, scenario.DestroyOnlyRoot);
                string kind = scenario.TargetResolutionKind;

                AssertEqual(true, GetBoolPropFlexible(annotation,
                        "TargetResolutionRequiresRemainFaceUp",
                        "ResolutionRequiresRemainFaceUp",
                        "TargetMustRemainFaceUp"),
                    kind + ": remain-face-up dependency must be grounded");

                // Must NOT apply the one-shot Normal Spell/Trap destroy-still-resolves rule.
                bool usedOneShotRule = GetBoolPropOptional(annotation,
                    "AppliedOneShotDestroyDoesNotNegateRule",
                    "UsedOneShotSpellTrapRule",
                    "ClassifiedAsOneShotDestroyResolve");
                AssertFalse(usedOneShotRule,
                    kind + ": must not classify via one-shot Normal Spell/Trap rule");

                // Either a non-zero prevention path or explicit non-one-shot uncertainty —
                // but never a confident false "still resolves as one-shot" package.
                bool stopped = GetBoolPropOptional(annotation,
                    "PrimaryEffectStopped", "PrimaryEffectPrevented");
                bool expectedResolve = GetBoolPropOptional(annotation,
                    "TargetEffectExpectedToResolve", "PrimaryEffectExpectedToResolve");
                string uncertainty = CollectUncertaintyText(annotation);
                bool openOrRemain =
                    stopped
                    || uncertainty.IndexOf("remain", StringComparison.OrdinalIgnoreCase) >= 0
                    || uncertainty.IndexOf("continuous", StringComparison.OrdinalIgnoreCase) >= 0
                    || uncertainty.IndexOf("face-up", StringComparison.OrdinalIgnoreCase) >= 0
                    || uncertainty.IndexOf("face_up", StringComparison.OrdinalIgnoreCase) >= 0
                    || uncertainty.IndexOf("ongoing", StringComparison.OrdinalIgnoreCase) >= 0;

                AssertFalse(expectedResolve && !stopped && !openOrRemain,
                    kind + ": destroy of remain-face-up card must not be confidently treated as "
                    + "value-zero one-shot that still fully resolves");
                AssertTrue(openOrRemain || stopped,
                    kind + ": remain-face-up control must stop, open uncertainty, or mark prevention");
            }
        }

        // ---------------------------------------------------------------------
        // 5. Secondary-benefit accounting
        // ---------------------------------------------------------------------
        static void SecondaryBenefitAccounting()
        {
            ChainResponseScenario scenario = CreateDustTornadoVsActivatedOneShotScenario(
                includeSecondarySetFromHand: true);
            object annotation = AnalyzeRoot(scenario, scenario.DestroyOnlyRoot);

            AssertEqual(0, GetNumericPropFlexible(annotation,
                    "PrimaryDisruptionValue", "PrimaryDisruptionScore", "NegationValue"),
                "secondary benefits must not convert zero primary disruption into negation value");
            AssertEqual(false, GetBoolPropFlexible(annotation,
                    "PrimaryEffectStopped", "PrimaryEffectPrevented"),
                "secondary Set-from-hand cannot flip primary_effect_stopped to true");

            IEnumerable secondary = GetEnumerablePropFlexible(annotation,
                "SecondaryBenefits", "SecondaryEffects", "SecondaryValueItems");
            AssertNotNull(secondary, "secondary benefits must be enumerated separately");
            int count = 0;
            bool sawSetFromHand = false;
            foreach (object item in secondary)
            {
                count++;
                string blob = item == null
                    ? string.Empty
                    : (item is string
                        ? (string)item
                        : MiniJSON.Json.Serialize(ToSerializable(item)));
                if (blob.IndexOf("set", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("optional", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sawSetFromHand = true;
                }
                // Secondary item must not claim it negates/interrupts the primary chain effect.
                AssertFalse(
                    blob.IndexOf("negate", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("interrupt", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("primary_effect_stopped", StringComparison.OrdinalIgnoreCase) >= 0,
                    "secondary benefit must not be described as negating the activated effect");
            }
            AssertTrue(count > 0, "at least one secondary benefit must be listed when structured");
            AssertTrue(sawSetFromHand,
                "optional Set-from-hand clause must appear as secondary, not as primary disruption");
        }

        // ---------------------------------------------------------------------
        // 6. Unknown-semantics fail-open
        // ---------------------------------------------------------------------
        static void UnknownSemanticsFailOpen()
        {
            ChainResponseScenario scenario = CreateUnknownSemanticsScenario();
            object annotation = AnalyzeRoot(scenario, scenario.DestroyOnlyRoot);

            string uncertainty = CollectUncertaintyText(annotation);
            string boundary = GetStringPropFlexible(annotation,
                "Boundary", "UncertaintyBoundary", "FailOpenBoundary", "SemanticsBoundary")
                ?? string.Empty;
            string provenance = GetStringPropFlexible(annotation, "Provenance", "OutcomeProvenance")
                ?? string.Empty;

            bool reportsUnknown =
                uncertainty.IndexOf("unknown_card_semantics", StringComparison.OrdinalIgnoreCase) >= 0
                || boundary.IndexOf("unknown_card_semantics", StringComparison.OrdinalIgnoreCase) >= 0
                || provenance.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0
                || GetBoolPropOptional(annotation, "IsUnknownCardSemantics", "UnknownSemantics");

            AssertTrue(reportsUnknown,
                "insufficient structured data must produce unknown_card_semantics (fail-open)");

            // Must not invent a confident value-zero or false-negation conclusion.
            object disruptionObj = GetPropFlexible(annotation,
                "PrimaryDisruptionValue", "PrimaryDisruptionScore", "NegationValue");
            if (disruptionObj != null)
            {
                // If a number is present under unknown semantics it must not be treated as
                // grounded zero-value authority — require uncertainty/boundary still present
                // and primary_effect_stopped must not be hard true/false without confidence cap.
                AssertTrue(reportsUnknown,
                    "any disruption number under missing semantics must still fail-open");
            }

            // Hard false-negation is forbidden when semantics are unknown.
            object stoppedObj = GetPropFlexible(annotation,
                "PrimaryEffectStopped", "PrimaryEffectPrevented");
            if (stoppedObj != null && Convert.ToBoolean(stoppedObj))
            {
                throw new Exception(
                    "unknown_card_semantics must not assert primary_effect_stopped: true");
            }

            // Hard confident value-zero package is forbidden: either omit grounded zero or
            // keep confidence below a deterministic threshold / mark unknown.
            object confidenceObj = GetPropFlexible(annotation, "Confidence", "OutcomeConfidence");
            if (disruptionObj != null
                && Convert.ToDouble(disruptionObj) == 0.0
                && stoppedObj != null
                && !Convert.ToBoolean(stoppedObj)
                && GetBoolPropOptional(annotation,
                    "TargetEffectExpectedToResolve", "PrimaryEffectExpectedToResolve"))
            {
                bool lowConfidence = confidenceObj != null && Convert.ToDouble(confidenceObj) < 0.5;
                AssertTrue(lowConfidence || reportsUnknown,
                    "unknown semantics must not silently emit a confident value-zero one-shot package");
            }
        }

        // ---------------------------------------------------------------------
        // 7. Provider response outcome consistency (outcome_contradiction)
        // ---------------------------------------------------------------------
        static void ProviderResponseOutcomeConsistency()
        {
            ChainResponseScenario scenario = CreateDustTornadoVsActivatedOneShotScenario(
                includeSecondarySetFromHand: true);
            object annotation = AnalyzeRoot(scenario, scenario.DestroyOnlyRoot);

            // Contradictory provider claim: destroy stopped the activated effect.
            object badResponse = CreateProviderResponse(
                scenario.Snapshot.RunEffectSeq,
                scenario.DestroyOnlyRoot.ActionId,
                reason: "Dust Tornado destroys Foolish Burial Goods and stops it from resolving "
                    + "before Cubic follow-ups",
                primaryEffectStopped: true,
                valueBasis: "negation of activated spell");
            object badResult = ValidateProviderResponse(annotation, badResponse, scenario);
            AssertEqual(false, GetBoolPropFlexible(badResult, "IsValid", "Valid", "Success", "Ok"),
                "provider claim that destroy stopped the effect must be rejected");
            string badError = GetStringPropFlexible(badResult, "Error", "Reason", "FailureReason", "Code")
                ?? string.Empty;
            AssertEqual("outcome_contradiction", badError,
                "rejection reason must be outcome_contradiction");

            // Consistent low-value selection stating concrete secondary basis is allowed.
            object goodResponse = CreateProviderResponse(
                scenario.Snapshot.RunEffectSeq,
                scenario.DestroyOnlyRoot.ActionId,
                reason: "Primary disruption is zero because the activated one-shot still resolves; "
                    + "selecting for the concrete secondary Set-from-hand benefit only",
                primaryEffectStopped: false,
                valueBasis: "secondary_set_from_hand");
            object goodResult = ValidateProviderResponse(annotation, goodResponse, scenario);
            AssertEqual(true, GetBoolPropFlexible(goodResult, "IsValid", "Valid", "Success", "Ok"),
                "consistent low-value selection with concrete secondary-value basis must pass");
        }

        // ---------------------------------------------------------------------
        // 8. Projection and audit hidden-information safety
        // ---------------------------------------------------------------------
        static void ProjectionAndAuditHiddenInformationSafety()
        {
            ChainResponseScenario scenario = CreateDustTornadoVsActivatedOneShotScenario(
                includeSecondarySetFromHand: true,
                opponentHiddenHandId: SentinelOpponentHandId,
                opponentHiddenSetId: SentinelOpponentSetId,
                opponentHiddenExtraId: SentinelOpponentExtraId);
            object annotation = AnalyzeRoot(scenario, scenario.DestroyOnlyRoot);

            string annotationJson = SerializeAnnotation(annotation);
            AssertNoLeak(annotationJson,
                SentinelOpponentHandName, SentinelOpponentHandId,
                SentinelOpponentSetName, SentinelOpponentSetId,
                SentinelOpponentExtraName, SentinelOpponentExtraId);

            // Request / decision-window sinks stay free of hidden sentinels and still
            // carry outcome provenance when projected for audit.
            string requestJson = LlmBrokerProtocol.SerializeDecisionRequest(scenario.Snapshot);
            string decisionWindowJson = LlmDecisionLogSerializer.SerializeDecisionWindow(scenario.Snapshot);
            AssertNoLeak(requestJson,
                SentinelOpponentHandName, SentinelOpponentHandId,
                SentinelOpponentSetName, SentinelOpponentSetId,
                SentinelOpponentExtraName, SentinelOpponentExtraId);
            AssertNoLeak(decisionWindowJson,
                SentinelOpponentHandName, SentinelOpponentHandId,
                SentinelOpponentSetName, SentinelOpponentSetId,
                SentinelOpponentExtraName, SentinelOpponentExtraId);

            object auditPayload = ProjectOutcomeAudit(scenario, annotation);
            AssertNotNull(auditPayload, "outcome audit projection required");
            string auditJson = MiniJSON.Json.Serialize(ToSerializable(auditPayload));
            AssertNoLeak(auditJson,
                SentinelOpponentHandName, SentinelOpponentHandId,
                SentinelOpponentSetName, SentinelOpponentSetId,
                SentinelOpponentExtraName, SentinelOpponentExtraId);

            // Provenance of the grounded outcome must survive audit serialization.
            AssertTrue(
                auditJson.IndexOf("rules_inferred", StringComparison.OrdinalIgnoreCase) >= 0
                || auditJson.IndexOf("engine_current", StringComparison.OrdinalIgnoreCase) >= 0
                || auditJson.IndexOf("primary_effect_stopped", StringComparison.OrdinalIgnoreCase) >= 0
                || auditJson.IndexOf("PrimaryEffectStopped", StringComparison.OrdinalIgnoreCase) >= 0
                || auditJson.IndexOf("destroy", StringComparison.OrdinalIgnoreCase) >= 0,
                "audit projection must preserve outcome provenance / capability facts");
            AssertFalse(
                auditJson.IndexOf(SentinelOpponentHandName, StringComparison.Ordinal) >= 0,
                "audit must not expose opponent-hidden hand identity");
        }

        // =====================================================================
        // Scenarios
        // =====================================================================

        class ChainResponseScenario
        {
            public DecisionSnapshot Snapshot;
            public LegalAction DestroyOnlyRoot;
            public LegalAction NegateRoot;
            public LegalAction DeclineRoot;
            public object OutcomeInput;
            public string TargetResolutionKind;
            public LlmCardMetadata ResponseCard;
            public LlmCardMetadata TargetCard;
        }

        static ChainResponseScenario CreateDustTornadoVsActivatedOneShotScenario(
            bool includeSecondarySetFromHand = false,
            int opponentHiddenHandId = SentinelOpponentHandId,
            int opponentHiddenSetId = SentinelOpponentSetId,
            int opponentHiddenExtraId = SentinelOpponentExtraId)
        {
            LlmCardMetadata dust = Meta(
                DustTornadoCardId,
                DustTornadoName,
                kind: "Trap",
                frame: "Normal",
                summonFamily: "trap",
                text: "Target 1 Spell/Trap your opponent controls; destroy that target. "
                    + "Then you can Set 1 Spell/Trap from your hand.");
            LlmCardMetadata goods = Meta(
                FoolishBurialGoodsCardId,
                FoolishBurialGoodsName,
                kind: "Spell",
                frame: "Normal",
                summonFamily: "spell",
                text: "Send 1 Spell/Trap from your Deck to the GY.");

            DecisionSnapshot snapshot = BaseChainSnapshot();
            PlacePublicKnown(snapshot, ControlledPlayer, OwnSpellTrapZone, 0,
                uniqueId: 501, cardId: DustTornadoCardId, face: RuntimeFaceDown, card: dust);
            PlacePublicKnown(snapshot, OpponentPlayer, OpponentSpellTrapZone, 0,
                uniqueId: 601, cardId: FoolishBurialGoodsCardId, face: RuntimeFaceUp, card: goods);
            // Opponent-hidden sentinels: must never appear in outcome/request/audit sinks.
            PlacePublicKnown(snapshot, OpponentPlayer, 13, 0,
                uniqueId: 901, cardId: opponentHiddenHandId, face: RuntimeFaceDown, card: Meta(
                    opponentHiddenHandId, SentinelOpponentHandName, "Monster", "Effect", "main_deck_monster"));
            PlacePublicKnown(snapshot, OpponentPlayer, 6, 0,
                uniqueId: 902, cardId: opponentHiddenSetId, face: RuntimeFaceDown, card: Meta(
                    opponentHiddenSetId, SentinelOpponentSetName, "Trap", "Normal", "trap"));
            PlacePublicKnown(snapshot, OpponentPlayer, 14, 0,
                uniqueId: 903, cardId: opponentHiddenExtraId, face: RuntimeFaceUp, card: Meta(
                    opponentHiddenExtraId, SentinelOpponentExtraName, "Monster", "Xyz", "xyz"));

            LegalAction activateDust = new LegalAction()
            {
                ActionId = DustTornadoRootActionId,
                Kind = LegalActionKind.Command,
                Player = ControlledPlayer,
                Position = OwnSpellTrapZone,
                Index = 0,
                Command = DuelCommandType.Action,
                CardUniqueId = 501,
                CardId = DustTornadoCardId,
                Card = dust,
                ActionLabel = "Activate Dust Tornado",
                IsMechanical = false,
                StrategicRole = "chain_response",
                RequiresTarget = true,
                TargetScope = "opponent_spell_trap",
            };
            LegalAction decline = new LegalAction()
            {
                ActionId = DeclineRootActionId,
                Kind = LegalActionKind.Cancel,
                Player = ControlledPlayer,
                ActionLabel = "Decline response",
                IsMechanical = false,
                StrategicRole = "decline",
                CancelDecide = true,
            };
            LegalAction targetGoods = new LegalAction()
            {
                ActionId = TargetFoolishActionId,
                Kind = LegalActionKind.Command,
                Player = OpponentPlayer,
                Position = OpponentSpellTrapZone,
                Index = 0,
                Command = DuelCommandType.Action,
                CardUniqueId = 601,
                CardId = FoolishBurialGoodsCardId,
                Card = goods,
                ActionLabel = "Target Foolish Burial Goods",
                IsMechanical = false,
                StrategicRole = "effect_target",
                IsEffectTargetSelection = true,
            };
            snapshot.LegalActions.Add(activateDust);
            snapshot.LegalActions.Add(decline);
            snapshot.LegalActions.Add(targetGoods);
            snapshot.StrategicActionCount = 3;
            snapshot.MechanicalActionCount = 0;
            snapshot.IsStrategicWindow = true;
            snapshot.StrategicWindowReason = "chain_response";

            object input = CreateOutcomeInput(
                rootActionId: activateDust.ActionId,
                targetCardId: FoolishBurialGoodsCardId,
                targetName: FoolishBurialGoodsName,
                targetAlreadyActivated: true,
                targetIsOneShotSpellOrTrap: true,
                targetResolutionRequiresRemainFaceUp: false,
                targetCardKind: "spell",
                targetFrame: "Normal",
                responseCapabilities: new[] { "destroy" },
                secondaryBenefits: includeSecondarySetFromHand
                    ? new[] { "optional_set_spell_trap_from_hand" }
                    : new string[0],
                capabilitiesGrounded: true,
                resolutionDependencyGrounded: true);

            return new ChainResponseScenario()
            {
                Snapshot = snapshot,
                DestroyOnlyRoot = activateDust,
                DeclineRoot = decline,
                OutcomeInput = input,
                TargetResolutionKind = "one_shot",
                ResponseCard = dust,
                TargetCard = goods,
            };
        }

        static ChainResponseScenario CreateNegationVsActivatedOneShotScenario()
        {
            LlmCardMetadata solemn = Meta(
                SolemnJudgmentCardId,
                SolemnJudgmentName,
                kind: "Trap",
                frame: "Counter",
                summonFamily: "trap",
                text: "When a monster would be Summoned, OR a Spell/Trap Card is activated: "
                    + "Pay half your LP; negate the Summon or activation, and if you do, destroy that card.");
            LlmCardMetadata goods = Meta(
                FoolishBurialGoodsCardId,
                FoolishBurialGoodsName,
                kind: "Spell",
                frame: "Normal",
                summonFamily: "spell",
                text: "Send 1 Spell/Trap from your Deck to the GY.");

            DecisionSnapshot snapshot = BaseChainSnapshot();
            PlacePublicKnown(snapshot, ControlledPlayer, OwnSpellTrapZone, 0,
                uniqueId: 511, cardId: SolemnJudgmentCardId, face: RuntimeFaceDown, card: solemn);
            PlacePublicKnown(snapshot, OpponentPlayer, OpponentSpellTrapZone, 0,
                uniqueId: 611, cardId: FoolishBurialGoodsCardId, face: RuntimeFaceUp, card: goods);

            LegalAction activateNegate = new LegalAction()
            {
                ActionId = DustTornadoRootActionId,
                Kind = LegalActionKind.Command,
                Player = ControlledPlayer,
                Position = OwnSpellTrapZone,
                Index = 0,
                Command = DuelCommandType.Action,
                CardUniqueId = 511,
                CardId = SolemnJudgmentCardId,
                Card = solemn,
                ActionLabel = "Activate Solemn Judgment",
                IsMechanical = false,
                StrategicRole = "chain_response",
            };
            LegalAction decline = new LegalAction()
            {
                ActionId = DeclineRootActionId,
                Kind = LegalActionKind.Cancel,
                Player = ControlledPlayer,
                ActionLabel = "Decline response",
                IsMechanical = false,
                StrategicRole = "decline",
                CancelDecide = true,
            };
            snapshot.LegalActions.Add(activateNegate);
            snapshot.LegalActions.Add(decline);
            snapshot.StrategicActionCount = 2;
            snapshot.IsStrategicWindow = true;
            snapshot.StrategicWindowReason = "chain_response";

            object input = CreateOutcomeInput(
                rootActionId: activateNegate.ActionId,
                targetCardId: FoolishBurialGoodsCardId,
                targetName: FoolishBurialGoodsName,
                targetAlreadyActivated: true,
                targetIsOneShotSpellOrTrap: true,
                targetResolutionRequiresRemainFaceUp: false,
                targetCardKind: "spell",
                targetFrame: "Normal",
                responseCapabilities: new[] { "negate_activation", "destroy" },
                secondaryBenefits: new string[0],
                capabilitiesGrounded: true,
                resolutionDependencyGrounded: true);

            return new ChainResponseScenario()
            {
                Snapshot = snapshot,
                NegateRoot = activateNegate,
                DestroyOnlyRoot = activateNegate,
                DeclineRoot = decline,
                OutcomeInput = input,
                TargetResolutionKind = "one_shot",
                ResponseCard = solemn,
                TargetCard = goods,
            };
        }

        static ChainResponseScenario CreateDestroyVsRemainFaceUpScenario(
            string resolutionKind,
            int targetCardId,
            string targetName)
        {
            LlmCardMetadata dust = Meta(
                DustTornadoCardId,
                DustTornadoName,
                kind: "Trap",
                frame: "Normal",
                summonFamily: "trap",
                text: "Target 1 Spell/Trap; destroy that target.");
            string frame = resolutionKind == "continuous"
                ? "Continuous"
                : (resolutionKind == "field" ? "Field" : "Equip");
            LlmCardMetadata target = Meta(
                targetCardId,
                targetName,
                kind: "Spell",
                frame: frame,
                summonFamily: "spell",
                text: "Ongoing effect while face-up on the field.");

            DecisionSnapshot snapshot = BaseChainSnapshot();
            PlacePublicKnown(snapshot, ControlledPlayer, OwnSpellTrapZone, 0,
                uniqueId: 521, cardId: DustTornadoCardId, face: RuntimeFaceDown, card: dust);
            PlacePublicKnown(snapshot, OpponentPlayer, OpponentSpellTrapZone, 0,
                uniqueId: 621, cardId: targetCardId, face: RuntimeFaceUp, card: target);

            LegalAction activateDust = new LegalAction()
            {
                ActionId = DustTornadoRootActionId,
                Kind = LegalActionKind.Command,
                Player = ControlledPlayer,
                Position = OwnSpellTrapZone,
                Index = 0,
                Command = DuelCommandType.Action,
                CardUniqueId = 521,
                CardId = DustTornadoCardId,
                Card = dust,
                ActionLabel = "Activate Dust Tornado",
                IsMechanical = false,
                StrategicRole = "chain_response",
            };
            LegalAction decline = new LegalAction()
            {
                ActionId = DeclineRootActionId,
                Kind = LegalActionKind.Cancel,
                Player = ControlledPlayer,
                ActionLabel = "Decline response",
                IsMechanical = false,
                StrategicRole = "decline",
                CancelDecide = true,
            };
            snapshot.LegalActions.Add(activateDust);
            snapshot.LegalActions.Add(decline);
            snapshot.StrategicActionCount = 2;
            snapshot.IsStrategicWindow = true;
            snapshot.StrategicWindowReason = "chain_response";

            object input = CreateOutcomeInput(
                rootActionId: activateDust.ActionId,
                targetCardId: targetCardId,
                targetName: targetName,
                targetAlreadyActivated: true,
                targetIsOneShotSpellOrTrap: false,
                targetResolutionRequiresRemainFaceUp: true,
                targetCardKind: "spell",
                targetFrame: frame,
                responseCapabilities: new[] { "destroy" },
                secondaryBenefits: new string[0],
                capabilitiesGrounded: true,
                resolutionDependencyGrounded: true);

            return new ChainResponseScenario()
            {
                Snapshot = snapshot,
                DestroyOnlyRoot = activateDust,
                DeclineRoot = decline,
                OutcomeInput = input,
                TargetResolutionKind = resolutionKind,
                ResponseCard = dust,
                TargetCard = target,
            };
        }

        static ChainResponseScenario CreateUnknownSemanticsScenario()
        {
            // Response card with no grounded capabilities / resolution dependency.
            LlmCardMetadata unknown = Meta(
                99990001,
                "UNKNOWN_RESPONSE_CARD",
                kind: "Trap",
                frame: "Normal",
                summonFamily: "trap",
                text: "Effect text present but no structured capability table entry.");
            LlmCardMetadata goods = Meta(
                FoolishBurialGoodsCardId,
                FoolishBurialGoodsName,
                kind: "Spell",
                frame: "Normal",
                summonFamily: "spell",
                text: "Send 1 Spell/Trap from your Deck to the GY.");

            DecisionSnapshot snapshot = BaseChainSnapshot();
            PlacePublicKnown(snapshot, ControlledPlayer, OwnSpellTrapZone, 0,
                uniqueId: 531, cardId: 99990001, face: RuntimeFaceDown, card: unknown);
            PlacePublicKnown(snapshot, OpponentPlayer, OpponentSpellTrapZone, 0,
                uniqueId: 631, cardId: FoolishBurialGoodsCardId, face: RuntimeFaceUp, card: goods);

            LegalAction activateUnknown = new LegalAction()
            {
                ActionId = DustTornadoRootActionId,
                Kind = LegalActionKind.Command,
                Player = ControlledPlayer,
                Position = OwnSpellTrapZone,
                Index = 0,
                Command = DuelCommandType.Action,
                CardUniqueId = 531,
                CardId = 99990001,
                Card = unknown,
                ActionLabel = "Activate UNKNOWN_RESPONSE_CARD",
                IsMechanical = false,
                StrategicRole = "chain_response",
            };
            LegalAction decline = new LegalAction()
            {
                ActionId = DeclineRootActionId,
                Kind = LegalActionKind.Cancel,
                Player = ControlledPlayer,
                ActionLabel = "Decline response",
                IsMechanical = false,
                StrategicRole = "decline",
                CancelDecide = true,
            };
            snapshot.LegalActions.Add(activateUnknown);
            snapshot.LegalActions.Add(decline);
            snapshot.StrategicActionCount = 2;
            snapshot.IsStrategicWindow = true;
            snapshot.StrategicWindowReason = "chain_response";

            object input = CreateOutcomeInput(
                rootActionId: activateUnknown.ActionId,
                targetCardId: FoolishBurialGoodsCardId,
                targetName: FoolishBurialGoodsName,
                targetAlreadyActivated: true,
                targetIsOneShotSpellOrTrap: true,
                targetResolutionRequiresRemainFaceUp: false,
                targetCardKind: "spell",
                targetFrame: "Normal",
                responseCapabilities: new string[0], // insufficient structured capability data
                secondaryBenefits: new string[0],
                capabilitiesGrounded: false,
                resolutionDependencyGrounded: false);

            return new ChainResponseScenario()
            {
                Snapshot = snapshot,
                DestroyOnlyRoot = activateUnknown,
                DeclineRoot = decline,
                OutcomeInput = input,
                TargetResolutionKind = "unknown",
                ResponseCard = unknown,
                TargetCard = goods,
            };
        }

        static DecisionSnapshot BaseChainSnapshot()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = FixtureRunEffectSeq,
                ViewType = DuelViewType.WaitInput,
                ViewParam1 = (int)DuelMenuActType.CheckChain,
                ActingPlayer = ControlledPlayer,
                ControlledPlayer = ControlledPlayer,
                Turn = 4,
                TurnPlayer = OpponentPlayer,
                CurrentPhase = (int)DuelPhase.Main1,
                CurrentStep = 0,
            };
            EnsurePlayer(snapshot.PublicState, ControlledPlayer).LifePoints = 7000;
            EnsurePlayer(snapshot.PublicState, OpponentPlayer).LifePoints = 8000;
            snapshot.TurnMemory.PhasePlan = "respond_to_chain_without_hidden_leak";
            return snapshot;
        }

        static PublicPlayerState EnsurePlayer(PublicDuelState state, int player)
        {
            foreach (PublicPlayerState existing in state.Players)
            {
                if (existing.Player == player)
                {
                    return existing;
                }
            }
            PublicPlayerState created = new PublicPlayerState() { Player = player };
            state.Players.Add(created);
            return created;
        }

        static void PlacePublicKnown(
            DecisionSnapshot snapshot,
            int player,
            int position,
            int index,
            int uniqueId,
            int cardId,
            int face,
            LlmCardMetadata card)
        {
            PublicPlayerState ps = EnsurePlayer(snapshot.PublicState, player);
            PublicPositionState pos = ps.Positions.FirstOrDefault(p => p.Position == position);
            if (pos == null)
            {
                pos = new PublicPositionState() { Position = position, Count = 0 };
                ps.Positions.Add(pos);
            }
            if (index >= pos.Count)
            {
                pos.Count = index + 1;
            }
            ps.KnownCards.Add(new PublicKnownCard()
            {
                Player = player,
                Position = position,
                Index = index,
                CardUniqueId = uniqueId,
                CardId = cardId,
                Face = face,
                Card = card,
            });
        }

        static LlmCardMetadata Meta(
            int cardId,
            string name,
            string kind,
            string frame,
            string summonFamily,
            string text = null)
        {
            return new LlmCardMetadata()
            {
                CardId = cardId,
                Name = name,
                Kind = kind,
                Frame = frame,
                SummonFamily = summonFamily,
                Text = text ?? name,
            };
        }

        // =====================================================================
        // Production API binding (reflection)
        // =====================================================================

        static object AnalyzeRoot(ChainResponseScenario scenario, LegalAction root)
        {
            AssertNotNull(root, "root action");
            Type analyzerType = RequireType("LlmImmediateOutcomeAnalyzer");
            MethodInfo analyze = FindAnalyzeMethod(analyzerType);
            AssertNotNull(analyze,
                "LlmImmediateOutcomeAnalyzer.Analyze/Annotate/Evaluate static method");

            object input = scenario.OutcomeInput;
            if (input == null)
            {
                throw new Exception(
                    "YGOMASTER-LLM-005 Slice 0B fixture missing LlmImmediateOutcomeInput");
            }

            try
            {
                object result = InvokeAnalyze(analyze, scenario.Snapshot, root, input);
                AssertNotNull(result, "LlmImmediateOutcomeAnalyzer returned null annotation");
                return result;
            }
            catch (TargetInvocationException ex)
            {
                throw new Exception(
                    "LlmImmediateOutcomeAnalyzer failed: "
                    + (ex.InnerException != null ? ex.InnerException.Message : ex.Message),
                    ex.InnerException ?? ex);
            }
        }

        static MethodInfo FindAnalyzeMethod(Type analyzerType)
        {
            string[] names = { "Analyze", "Annotate", "Evaluate", "AnalyzeRoot", "AnnotateRoot" };
            MethodInfo[] methods = analyzerType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (string name in names)
            {
                foreach (MethodInfo m in methods)
                {
                    if (m.Name != name)
                    {
                        continue;
                    }
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length >= 2)
                    {
                        return m;
                    }
                }
            }
            return methods.FirstOrDefault(m =>
                names.Any(n => string.Equals(n, m.Name, StringComparison.Ordinal)));
        }

        static object InvokeAnalyze(
            MethodInfo analyze,
            DecisionSnapshot snapshot,
            LegalAction root,
            object input)
        {
            ParameterInfo[] ps = analyze.GetParameters();
            object[] args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                string pname = (ps[i].Name ?? string.Empty).ToLowerInvariant();
                if (pt == typeof(DecisionSnapshot) || pt.IsAssignableFrom(typeof(DecisionSnapshot)))
                {
                    args[i] = snapshot;
                }
                else if (pt == typeof(LegalAction) || pt.IsAssignableFrom(typeof(LegalAction)))
                {
                    args[i] = root;
                }
                else if (pt == typeof(int))
                {
                    args[i] = root.ActionId;
                }
                else if (pt == typeof(IEnumerable<LegalAction>)
                    || pt == typeof(List<LegalAction>)
                    || pt == typeof(IList<LegalAction>))
                {
                    args[i] = snapshot.LegalActions;
                }
                else if (input != null && pt.IsInstanceOfType(input))
                {
                    args[i] = input;
                }
                else if (input != null && (pt.IsAssignableFrom(input.GetType())
                    || pt == typeof(object)
                    || pname.Contains("input")
                    || pname.Contains("fact")
                    || pname.Contains("context")
                    || pname.Contains("chain")))
                {
                    args[i] = input;
                }
                else if (pt == typeof(bool))
                {
                    args[i] = false;
                }
                else
                {
                    args[i] = null;
                }
            }
            return analyze.Invoke(null, args);
        }

        static object CreateOutcomeInput(
            int rootActionId,
            int targetCardId,
            string targetName,
            bool targetAlreadyActivated,
            bool targetIsOneShotSpellOrTrap,
            bool targetResolutionRequiresRemainFaceUp,
            string targetCardKind,
            string targetFrame,
            string[] responseCapabilities,
            string[] secondaryBenefits,
            bool capabilitiesGrounded,
            bool resolutionDependencyGrounded)
        {
            Type inputType = RequireType("LlmImmediateOutcomeInput");
            object input = Activator.CreateInstance(inputType);

            TrySetProp(input, "RootActionId", rootActionId);
            TrySetProp(input, "TargetCardId", targetCardId);
            TrySetProp(input, "TargetName", targetName);
            TrySetProp(input, "TargetAlreadyActivated", targetAlreadyActivated);
            TrySetProp(input, "TargetIsAlreadyActivated", targetAlreadyActivated);
            TrySetProp(input, "TargetActivatedOnChain", targetAlreadyActivated);
            TrySetProp(input, "TargetIsOneShotSpellOrTrap", targetIsOneShotSpellOrTrap);
            TrySetProp(input, "IsOneShotSpellOrTrap", targetIsOneShotSpellOrTrap);
            TrySetProp(input, "TargetResolutionRequiresRemainFaceUp", targetResolutionRequiresRemainFaceUp);
            TrySetProp(input, "ResolutionRequiresRemainFaceUp", targetResolutionRequiresRemainFaceUp);
            TrySetProp(input, "TargetMustRemainFaceUp", targetResolutionRequiresRemainFaceUp);
            TrySetProp(input, "TargetCardKind", targetCardKind);
            TrySetProp(input, "TargetKind", targetCardKind);
            TrySetProp(input, "TargetFrame", targetFrame);
            TrySetProp(input, "CapabilitiesGrounded", capabilitiesGrounded);
            TrySetProp(input, "ResponseCapabilitiesGrounded", capabilitiesGrounded);
            TrySetProp(input, "ResolutionDependencyGrounded", resolutionDependencyGrounded);
            TrySetProp(input, "SemanticsGrounded", capabilitiesGrounded && resolutionDependencyGrounded);

            SetStringListProp(input,
                new[] { "ResponseCapabilities", "Capabilities", "EffectCapabilities" },
                responseCapabilities ?? new string[0]);
            SetStringListProp(input,
                new[] { "SecondaryBenefits", "SecondaryEffects", "SecondaryValueItems" },
                secondaryBenefits ?? new string[0]);

            // Provenance for structured fixture facts (production may overwrite).
            TrySetProp(input, "Provenance", "rules_inferred");
            TrySetProp(input, "CapabilityProvenance",
                capabilitiesGrounded ? "rules_inferred" : "unknown_card_semantics");

            return input;
        }

        static object ValidateProviderResponse(
            object annotation,
            object providerResponse,
            ChainResponseScenario scenario)
        {
            Type analyzerType = RequireType("LlmImmediateOutcomeAnalyzer");
            MethodInfo validate = FindValidateMethod(analyzerType);
            if (validate == null)
            {
                // Alternate production surface: dedicated consistency type.
                Type consistencyType = TryGetType("LlmImmediateOutcomeConsistency")
                    ?? TryGetType("LlmOutcomeConsistencyValidator");
                if (consistencyType != null)
                {
                    validate = FindValidateMethod(consistencyType);
                }
            }
            AssertNotNull(validate,
                "LlmImmediateOutcomeAnalyzer.ValidateProviderResponse (or LlmImmediateOutcomeConsistency.Validate) "
                + "must exist for outcome_contradiction");

            try
            {
                ParameterInfo[] ps = validate.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    Type pt = ps[i].ParameterType;
                    string pname = (ps[i].Name ?? string.Empty).ToLowerInvariant();
                    if (annotation != null && (pt.IsInstanceOfType(annotation)
                        || pname.Contains("annotation")
                        || pname.Contains("outcome")))
                    {
                        args[i] = annotation;
                    }
                    else if (providerResponse != null && (pt.IsInstanceOfType(providerResponse)
                        || pt == typeof(LlmBrokerDecisionResponse)
                        || pname.Contains("response")
                        || pname.Contains("provider")))
                    {
                        args[i] = providerResponse;
                    }
                    else if (pt == typeof(DecisionSnapshot))
                    {
                        args[i] = scenario.Snapshot;
                    }
                    else if (pt == typeof(LegalAction))
                    {
                        args[i] = scenario.DestroyOnlyRoot;
                    }
                    else
                    {
                        args[i] = null;
                    }
                }
                object result = validate.Invoke(null, args);
                AssertNotNull(result, "ValidateProviderResponse returned null");
                return result;
            }
            catch (TargetInvocationException ex)
            {
                throw new Exception(
                    "ValidateProviderResponse failed: "
                    + (ex.InnerException != null ? ex.InnerException.Message : ex.Message),
                    ex.InnerException ?? ex);
            }
        }

        static MethodInfo FindValidateMethod(Type type)
        {
            string[] names =
            {
                "ValidateProviderResponse",
                "ValidateResponseConsistency",
                "ValidateConsistency",
                "Validate",
                "CheckProviderResponse",
            };
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (string name in names)
            {
                MethodInfo m = methods.FirstOrDefault(x => x.Name == name && x.GetParameters().Length >= 2);
                if (m != null)
                {
                    return m;
                }
            }
            return null;
        }

        static object CreateProviderResponse(
            ulong runEffectSeq,
            int actionId,
            string reason,
            bool primaryEffectStopped,
            string valueBasis)
        {
            // Prefer production response DTO if it already carries outcome fields.
            Type responseType = TryGetType("LlmBrokerDecisionResponse");
            if (responseType != null)
            {
                object response = Activator.CreateInstance(responseType);
                TrySetProp(response, "RunEffectSeq", runEffectSeq);
                TrySetProp(response, "ActionId", actionId);
                TrySetProp(response, "Reason", reason);
                TrySetProp(response, "Confidence", 0.9);
                TrySetProp(response, "PrimaryEffectStopped", primaryEffectStopped);
                TrySetProp(response, "PredictedResolution",
                    primaryEffectStopped ? "stopped" : "resolves");
                TrySetProp(response, "ValueBasis", valueBasis);
                TrySetProp(response, "NetTacticalValue", primaryEffectStopped ? 1.0 : 0.2);
                // Also try nested predicted_outcome bag if present.
                TrySetProp(response, "PrimaryDisruptionClaimed", primaryEffectStopped);
                return response;
            }

            return new Dictionary<string, object>()
            {
                { "run_effect_seq", (long)runEffectSeq },
                { "action_id", actionId },
                { "reason", reason },
                { "confidence", 0.9 },
                { "primary_effect_stopped", primaryEffectStopped },
                { "value_basis", valueBasis },
            };
        }

        static object ProjectOutcomeAudit(ChainResponseScenario scenario, object annotation)
        {
            Type analyzerType = RequireType("LlmImmediateOutcomeAnalyzer");
            MethodInfo project = analyzerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                    m.Name == "ProjectAudit"
                    || m.Name == "SerializeAudit"
                    || m.Name == "ToAuditDictionary"
                    || m.Name == "Project"
                    || m.Name == "Serialize");
            if (project != null)
            {
                try
                {
                    ParameterInfo[] ps = project.GetParameters();
                    if (ps.Length == 0)
                    {
                        return project.Invoke(null, null);
                    }
                    if (ps.Length == 1)
                    {
                        return project.Invoke(null, new object[] { annotation });
                    }
                    return project.Invoke(null, new object[] { scenario.Snapshot, annotation });
                }
                catch (TargetInvocationException ex)
                {
                    throw new Exception(
                        "outcome audit projection failed: "
                        + (ex.InnerException != null ? ex.InnerException.Message : ex.Message),
                        ex.InnerException ?? ex);
                }
            }

            // Alternate: decision-log serializer helper.
            Type logType = TryGetType("LlmDecisionLogSerializer");
            if (logType != null)
            {
                MethodInfo logMethod = logType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                        m.Name.IndexOf("Outcome", StringComparison.OrdinalIgnoreCase) >= 0
                        || m.Name.IndexOf("Immediate", StringComparison.OrdinalIgnoreCase) >= 0);
                if (logMethod != null)
                {
                    ParameterInfo[] ps = logMethod.GetParameters();
                    object[] args = new object[ps.Length];
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (ps[i].ParameterType == typeof(DecisionSnapshot))
                        {
                            args[i] = scenario.Snapshot;
                        }
                        else if (annotation != null
                            && ps[i].ParameterType.IsInstanceOfType(annotation))
                        {
                            args[i] = annotation;
                        }
                        else
                        {
                            args[i] = annotation;
                        }
                    }
                    return logMethod.Invoke(null, args);
                }
            }

            throw new Exception(
                "YGOMASTER-LLM-005 missing outcome audit projection "
                + "(LlmImmediateOutcomeAnalyzer.ProjectAudit / SerializeAudit or "
                + "LlmDecisionLogSerializer outcome helper)");
        }

        static string SerializeAnnotation(object annotation)
        {
            if (annotation == null)
            {
                return "null";
            }
            if (annotation is string)
            {
                return (string)annotation;
            }
            try
            {
                return MiniJSON.Json.Serialize(ToSerializable(annotation));
            }
            catch
            {
                return annotation.ToString() ?? string.Empty;
            }
        }

        // =====================================================================
        // Annotation helpers
        // =====================================================================

        static void AssertRootRepresented(
            ChainResponseScenario scenario,
            int rootActionId,
            string message)
        {
            bool found = scenario.Snapshot.LegalActions.Any(a =>
                a != null && !a.IsMechanical && a.ActionId == rootActionId);
            AssertTrue(found, message + " (legal root action_id " + rootActionId + ")");
        }

        static bool CapabilitiesInclude(object annotation, string capability)
        {
            IEnumerable caps = GetEnumerablePropFlexible(annotation,
                "ResponseCapabilities", "Capabilities", "EffectCapabilities");
            if (caps != null)
            {
                foreach (object c in caps)
                {
                    if (c != null
                        && string.Equals(c.ToString(), capability, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            string blob = SerializeAnnotation(annotation);
            // Accept either JSON array membership or structured field text.
            return blob.IndexOf("\"" + capability + "\"", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf(capability, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool AnnotationClaimsNegationOrInterruption(object annotation)
        {
            string blob = SerializeAnnotation(annotation);
            if (GetBoolPropOptional(annotation, "PrimaryEffectStopped", "PrimaryEffectPrevented"))
            {
                return true;
            }
            if (ContainsPhrase(blob, "negates the activation")
                || ContainsPhrase(blob, "negates the effect")
                || ContainsPhrase(blob, "interrupted")
                || ContainsPhrase(blob, "stops the effect")
                || ContainsPhrase(blob, "prevents resolution"))
            {
                return true;
            }
            // primary_effect_stopped true in JSON
            if (blob.IndexOf("primary_effect_stopped", StringComparison.OrdinalIgnoreCase) >= 0
                && (blob.IndexOf("primary_effect_stopped\":true", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("primary_effect_stopped\": true", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("PrimaryEffectStopped\":true", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("PrimaryEffectStopped\": true", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
            return false;
        }

        static string CollectUncertaintyText(object annotation)
        {
            StringBuilder sb = new StringBuilder();
            IEnumerable list = GetEnumerablePropFlexible(annotation,
                "Uncertainty", "Uncertainties", "UncertaintyNotes", "OpenQuestions");
            if (list != null)
            {
                foreach (object item in list)
                {
                    if (item != null)
                    {
                        sb.Append(item.ToString()).Append(' ');
                    }
                }
            }
            string single = GetStringPropFlexible(annotation,
                "Uncertainty", "UncertaintyNote", "Boundary", "SemanticsBoundary");
            if (!string.IsNullOrEmpty(single))
            {
                sb.Append(single).Append(' ');
            }
            sb.Append(SerializeAnnotation(annotation));
            return sb.ToString();
        }

        static bool ContainsPhrase(string text, string phrase)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // =====================================================================
        // Reflection helpers
        // =====================================================================

        static Type RequireType(string simpleName)
        {
            Type type = TryGetType(simpleName);
            if (type == null)
            {
                throw new Exception(
                    "YGOMASTER-LLM-005 missing type " + simpleName
                    + " (Slice 0B immediate-outcome contract; implement LlmImmediateOutcomeAnalyzer "
                    + "and related types in YgoMasterServer/Llm/ as Slice 2B — tests only in Slice 0B)");
            }
            return type;
        }

        static Type TryGetType(string simpleName)
        {
            Assembly asm = typeof(Program).Assembly;
            Type direct = asm.GetType("YgoMaster." + simpleName, false);
            if (direct != null)
            {
                return direct;
            }
            foreach (Type t in asm.GetTypes())
            {
                if (t.Name == simpleName)
                {
                    return t;
                }
            }
            return null;
        }

        static object GetProp(object target, string name)
        {
            if (target == null)
            {
                return null;
            }
            if (target is IDictionary)
            {
                IDictionary dict = (IDictionary)target;
                if (dict.Contains(name))
                {
                    return dict[name];
                }
                string snake = ToSnakeCase(name);
                if (dict.Contains(snake))
                {
                    return dict[snake];
                }
                return null;
            }
            PropertyInfo prop = target.GetType().GetProperty(name);
            if (prop == null)
            {
                FieldInfo field = target.GetType().GetField(name);
                if (field != null)
                {
                    return field.GetValue(target);
                }
                return null;
            }
            return prop.GetValue(target, null);
        }

        static object GetPropFlexible(object target, params string[] names)
        {
            foreach (string name in names)
            {
                object value = GetProp(target, name);
                if (value != null)
                {
                    return value;
                }
            }
            return null;
        }

        static string GetStringPropFlexible(object target, params string[] names)
        {
            object value = GetPropFlexible(target, names);
            return value != null ? Convert.ToString(value) : null;
        }

        static bool GetBoolPropFlexible(object target, params string[] names)
        {
            object value = GetPropFlexible(target, names);
            if (value == null)
            {
                throw new Exception(
                    "missing bool property among [" + string.Join(",", names) + "] on "
                    + (target != null ? target.GetType().Name : "null")
                    + " (Slice 0B outcome annotation contract)");
            }
            return Convert.ToBoolean(value);
        }

        static bool GetBoolPropOptional(object target, params string[] names)
        {
            object value = GetPropFlexible(target, names);
            if (value == null)
            {
                return false;
            }
            return Convert.ToBoolean(value);
        }

        static int GetNumericPropFlexible(object target, params string[] names)
        {
            object value = GetPropFlexible(target, names);
            if (value == null)
            {
                throw new Exception(
                    "missing numeric property among [" + string.Join(",", names) + "] on "
                    + (target != null ? target.GetType().Name : "null")
                    + " (Slice 0B outcome annotation contract)");
            }
            return Convert.ToInt32(Convert.ToDouble(value));
        }

        static IEnumerable GetEnumerablePropFlexible(object target, params string[] names)
        {
            foreach (string name in names)
            {
                object value = GetProp(target, name);
                if (value is IEnumerable && !(value is string))
                {
                    return (IEnumerable)value;
                }
            }
            return null;
        }

        static void TrySetProp(object target, string name, object value)
        {
            if (target == null)
            {
                return;
            }
            if (target is IDictionary)
            {
                IDictionary dict = (IDictionary)target;
                dict[name] = value;
                dict[ToSnakeCase(name)] = value;
                return;
            }
            PropertyInfo prop = target.GetType().GetProperty(name);
            if (prop == null || !prop.CanWrite)
            {
                return;
            }
            object coerced = value;
            try
            {
                if (value != null && prop.PropertyType != value.GetType())
                {
                    if (prop.PropertyType.IsEnum && value is string)
                    {
                        coerced = Enum.Parse(prop.PropertyType, (string)value, true);
                    }
                    else if (typeof(IConvertible).IsAssignableFrom(prop.PropertyType)
                        && value is IConvertible)
                    {
                        coerced = Convert.ChangeType(value, prop.PropertyType);
                    }
                }
                prop.SetValue(target, coerced, null);
            }
            catch
            {
                // Flexible: production property type may differ; other props still set.
            }
        }

        static void SetStringListProp(object target, string[] names, string[] values)
        {
            foreach (string name in names)
            {
                PropertyInfo prop = target.GetType().GetProperty(name);
                if (prop == null || !prop.CanWrite)
                {
                    continue;
                }
                Type pt = prop.PropertyType;
                if (pt == typeof(string[]))
                {
                    prop.SetValue(target, values, null);
                    return;
                }
                if (pt == typeof(List<string>) || pt == typeof(IList<string>))
                {
                    prop.SetValue(target, new List<string>(values), null);
                    return;
                }
                if (pt == typeof(IList) || pt == typeof(ArrayList))
                {
                    ArrayList list = new ArrayList();
                    foreach (string v in values)
                    {
                        list.Add(v);
                    }
                    prop.SetValue(target, list, null);
                    return;
                }
                // IEnumerable<string> / IReadOnlyList etc. — try List<string>.
                if (typeof(IEnumerable).IsAssignableFrom(pt) && pt != typeof(string))
                {
                    List<string> concrete = new List<string>(values);
                    if (pt.IsAssignableFrom(typeof(List<string>)))
                    {
                        prop.SetValue(target, concrete, null);
                        return;
                    }
                }
            }
            // Dictionary-style bag fallback.
            if (target is IDictionary)
            {
                IDictionary dict = (IDictionary)target;
                dict[names[0]] = values;
                dict[ToSnakeCase(names[0])] = values;
            }
        }

        static object ToSerializable(object value)
        {
            if (value == null || value is string || value is ValueType)
            {
                return value;
            }
            if (value is IDictionary)
            {
                Dictionary<string, object> copy = new Dictionary<string, object>();
                foreach (DictionaryEntry entry in (IDictionary)value)
                {
                    copy[Convert.ToString(entry.Key)] = ToSerializable(entry.Value);
                }
                return copy;
            }
            if (value is IEnumerable && !(value is string))
            {
                List<object> list = new List<object>();
                foreach (object item in (IEnumerable)value)
                {
                    list.Add(ToSerializable(item));
                }
                return list;
            }
            Dictionary<string, object> dict = new Dictionary<string, object>();
            foreach (PropertyInfo prop in value.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                object raw;
                try
                {
                    raw = prop.GetValue(value, null);
                }
                catch
                {
                    continue;
                }
                dict[prop.Name] = ToSerializable(raw);
            }
            return dict;
        }

        static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0)
                    {
                        sb.Append('_');
                    }
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        static void AssertNoLeak(string json, params object[] forbidden)
        {
            foreach (object item in forbidden)
            {
                if (item == null)
                {
                    continue;
                }
                string token = item.ToString();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }
                AssertFalse(
                    json != null && json.Contains(token),
                    "outcome / projection / audit leaks " + token);
            }
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

        static void AssertNotNull(object value, string message)
        {
            if (value == null)
            {
                throw new Exception(message + ": expected non-null");
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + ": expected " + expected + ", got " + actual);
            }
        }
    }
}

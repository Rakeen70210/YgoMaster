using System;
using System.Collections.Generic;
using System.Linq;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-005 Slice 2B integration: production path must derive outcomes from
    /// DecisionSnapshot + legal roots + capability/resolution facts, attach them to Layer A
    /// root shells, consume ValueZeroPrimaryPenalty in deterministic scores, and emit
    /// default-off seat-gated audit through the planning-search log path.
    ///
    /// Dust Tornado / Foolish Burial Goods must be representable without a test-only
    /// manually populated LlmImmediateOutcomeInput.
    /// </summary>
    static class Llm005Slice2BIntegrationTests
    {
        const int ControlledPlayer = 1;
        const int OpponentPlayer = 0;
        const int OwnStZone = 5;
        const int OppStZone = 5;
        const int PosHand = 13;
        const int PosExtra = 14;
        const int PosGrave = 16;
        const int PosBanished = 17;
        const int RuntimeFaceDown = 0;
        const int RuntimeFaceUp = 1;

        // Master Duel internal CardIds (authoritative for LlmCardMetadata.CardId / live logs).
        // Conversion source: YgoMaster/Data/YdkIds.txt (YGOPro passcode → internal id).
        // Passcodes (DO NOT use as CardId): Dust 60082869, Goods 35726888, Solemn 41420027, MoP 44656491.
        const int DustTornadoInternalCardId = 4988;          // YdkIds: 60082869 4988
        const int FoolishBurialGoodsInternalCardId = 12800;  // YdkIds: 35726888 12800
        const int SolemnJudgmentInternalCardId = 4861;       // YdkIds: 41420027 4861
        const int MessengerOfPeaceInternalCardId = 4938;     // YdkIds: 44656491 4938
        // Known passcodes — must never be registered as catalog keys or live CardIds.
        const int DustTornadoYdkPasscode = 60082869;
        const int FoolishBurialGoodsYdkPasscode = 35726888;

        const int SentinelOppHandId = 99105091;
        const string SentinelOppHandName = "SENTINEL_OPP_HAND_2B_INT";
        const int SentinelOppSetId = 99105092;
        const string SentinelOppSetName = "SENTINEL_OPP_SET_2B_INT";
        const int SentinelOppExtraId = 99105093;
        const string SentinelOppExtraName = "SENTINEL_OPP_EXTRA_2B_INT";

        public static void RunAll()
        {
            ProductionDeriveDustTornadoWithoutManualInput();
            GraphRootShellCarriesImmediateOutcome();
            ValueZeroPrimaryPenaltyConsumedInRootScore();
            PlanningAuditEmitsImmediateOutcomesWhenEnabled();
            PlanningAuditDefaultOffDoesNotRequireOutcomePayload();
            UnknownCardSemanticsWhenCapabilityMissing();
            OutcomeAuditNeverLeaksOpponentHidden();
            PublicKnownCardRedactionRegression();
            CatalogUsesInternalIdsNotYdkPasscodes();
            AmbiguousTwoFaceUpSpellTrapsFailOpenWithoutTargetAction();
        }

        // ---------------------------------------------------------------------
        // 1. Production fact derivation (no manual LlmImmediateOutcomeInput)
        // ---------------------------------------------------------------------
        static void ProductionDeriveDustTornadoWithoutManualInput()
        {
            DecisionSnapshot snap = CreateDustTornadoChainSnapshot(includeSecondaryCatalog: true);
            LegalAction dust = FindRoot(snap, DustTornadoInternalCardId);
            AssertNotNull(dust, "Dust Tornado root");
            AssertEqual(DustTornadoInternalCardId, dust.CardId,
                "live fixture must use Master Duel internal CardId 4988 (not Ydk passcode)");

            LlmImmediateOutcomeAnnotation ann =
                LlmImmediateOutcomeAnalyzer.AnalyzeRoot(snap, dust);
            AssertNotNull(ann, "production AnalyzeRoot annotation");
            AssertTrue(ann.TargetAlreadyActivated, "target already activated");
            AssertEqual(FoolishBurialGoodsInternalCardId, ann.TargetCardId,
                "target CardId must be internal 12800 (captured duel id)");
            AssertEqual(false, ann.PrimaryEffectStopped, "destroy-only does not stop");
            AssertEqual(true, ann.TargetEffectExpectedToResolve, "one-shot still resolves");
            AssertEqual(0, ann.PrimaryDisruptionValue, "primary disruption zero");
            AssertFalse(ann.IsUnknownCardSemantics,
                "internal-id Dust/Goods regression must be grounded, not unknown_card_semantics");
            AssertTrue(
                ann.ResponseCapabilities != null
                && ann.ResponseCapabilities.Any(c =>
                    string.Equals(c, "destroy", StringComparison.OrdinalIgnoreCase)),
                "capabilities include destroy from production catalog");
            AssertFalse(
                ann.ResponseCapabilities.Any(c =>
                    c != null && c.IndexOf("negate", StringComparison.OrdinalIgnoreCase) >= 0),
                "no negate capability for destroy-only");
            AssertTrue(ann.ValueZeroPrimaryPenalty > 0, "value-zero penalty set");
            AssertTrue(
                ann.SecondaryBenefits != null && ann.SecondaryBenefits.Count > 0,
                "secondary set-from-hand from capability facts");
        }

        // ---------------------------------------------------------------------
        // 2. Layer A graph attaches outcome to root shells
        // ---------------------------------------------------------------------
        static void GraphRootShellCarriesImmediateOutcome()
        {
            DecisionSnapshot snap = CreateDustTornadoChainSnapshot(includeSecondaryCatalog: true);
            LlmSelfResources self = ProjectSelf(snap);
            LlmSearchGraph graph = LlmTacticalAffordanceGraph.Build(
                snap, self, LlmSearchLimits.CreateDefault());
            graph = LlmBoundedLineSearch.Search(graph, LlmSearchLimits.CreateDefault());

            LlmSearchLine dustLine = graph.Lines.FirstOrDefault(l =>
                l != null && l.IsRootShell && l.RootActionId == 0);
            AssertNotNull(dustLine, "Dust Tornado root shell");
            AssertNotNull(dustLine.ImmediateOutcome, "root shell ImmediateOutcome attached");
            AssertEqual(false, dustLine.ImmediateOutcome.PrimaryEffectStopped,
                "graph annotation: not stopped");
            AssertEqual(0, dustLine.ImmediateOutcome.PrimaryDisruptionValue,
                "graph annotation: zero disruption");
        }

        // ---------------------------------------------------------------------
        // 3. ValueZeroPrimaryPenalty consumed in deterministic score
        // ---------------------------------------------------------------------
        static void ValueZeroPrimaryPenaltyConsumedInRootScore()
        {
            DecisionSnapshot snap = CreateDustTornadoChainSnapshot(includeSecondaryCatalog: true);
            LlmSelfResources self = ProjectSelf(snap);
            LlmSearchGraph graph = LlmTacticalAffordanceGraph.Build(
                snap, self, LlmSearchLimits.CreateDefault());

            LlmSearchLine dustLine = graph.Lines.First(l => l.IsRootShell && l.RootActionId == 0);
            LlmSearchLine declineLine = graph.Lines.First(l => l.IsRootShell && l.RootActionId == 1);
            AssertNotNull(dustLine.ImmediateOutcome, "dust outcome");
            AssertTrue(dustLine.ImmediateOutcome.ValueZeroPrimaryPenalty > 0, "penalty > 0");
            // Decline has no destroy-vs-one-shot value-zero package; dust must score strictly lower
            // after penalty application than an unpenalized shell of the same base class, or at
            // least lower than decline when both start from ScoreRootShell defaults.
            AssertTrue(
                dustLine.Score < declineLine.Score
                || dustLine.Score <= Math.Max(0, 1 - dustLine.ImmediateOutcome.ValueZeroPrimaryPenalty),
                "ValueZeroPrimaryPenalty must reduce dust root score (got dust="
                + dustLine.Score + " decline=" + declineLine.Score + " penalty="
                + dustLine.ImmediateOutcome.ValueZeroPrimaryPenalty + ")");
            AssertTrue(
                dustLine.ScoreFeatures != null
                && dustLine.ScoreFeatures.ContainsKey("value_zero_primary_penalty"),
                "score features must record value_zero_primary_penalty");
        }

        // ---------------------------------------------------------------------
        // 4. Planning audit path emits outcomes when enabled
        // ---------------------------------------------------------------------
        static void PlanningAuditEmitsImmediateOutcomesWhenEnabled()
        {
            DecisionSnapshot snap = CreateDustTornadoChainSnapshot(includeSecondaryCatalog: true);
            LlmSelfResources self = ProjectSelf(snap);
            LlmPlanningSearchAuditResult result = LlmPlanningSearchAudit.TryBuild(
                snap, self, LlmSearchLimits.CreateDefault());
            AssertTrue(result.Success, "audit TryBuild success");

            string completed = LlmDecisionLogSerializer.SerializeSearchCompleted(result);
            AssertTrue(
                completed.IndexOf("immediate_outcome", StringComparison.OrdinalIgnoreCase) >= 0
                || completed.IndexOf("primary_effect_stopped", StringComparison.OrdinalIgnoreCase) >= 0
                || completed.IndexOf("value_zero_primary_penalty", StringComparison.OrdinalIgnoreCase) >= 0,
                "SerializeSearchCompleted must include immediate-outcome audit fields");

            string outcomeAudit = LlmDecisionLogSerializer.SerializeImmediateOutcomeAudit(
                snap, result.Graph, auditEnabled: true, configuredControlPlayer: ControlledPlayer);
            AssertTrue(
                outcomeAudit.IndexOf("llm_immediate_outcome", StringComparison.OrdinalIgnoreCase) >= 0
                || outcomeAudit.IndexOf("primary_effect_stopped", StringComparison.OrdinalIgnoreCase) >= 0,
                "SerializeImmediateOutcomeAudit emits payload when enabled");
            AssertTrue(
                outcomeAudit.IndexOf("\"audit_emitted\":true", StringComparison.OrdinalIgnoreCase) >= 0
                || outcomeAudit.IndexOf("audit_emitted\": true", StringComparison.OrdinalIgnoreCase) >= 0
                || outcomeAudit.IndexOf("primary_effect_stopped", StringComparison.OrdinalIgnoreCase) >= 0,
                "enabled audit emits outcome content");
        }

        // ---------------------------------------------------------------------
        // 5. Default-off: disabled audit does not require / leak full outcome bag
        // ---------------------------------------------------------------------
        static void PlanningAuditDefaultOffDoesNotRequireOutcomePayload()
        {
            DecisionSnapshot snap = CreateDustTornadoChainSnapshot(includeSecondaryCatalog: true);
            LlmSelfResources self = ProjectSelf(snap);
            LlmSearchGraph graph = LlmTacticalAffordanceGraph.Build(
                snap, self, LlmSearchLimits.CreateDefault());

            string disabled = LlmDecisionLogSerializer.SerializeImmediateOutcomeAudit(
                snap, graph, auditEnabled: false, configuredControlPlayer: ControlledPlayer);
            AssertTrue(
                disabled.IndexOf("audit_emitted\":false", StringComparison.OrdinalIgnoreCase) >= 0
                || disabled.IndexOf("audit_emitted\": false", StringComparison.OrdinalIgnoreCase) >= 0
                || disabled.IndexOf("\"audit_enabled\":false", StringComparison.OrdinalIgnoreCase) >= 0,
                "default-off audit marks not emitted");
            // Seat mismatch also blocks.
            string mismatch = LlmDecisionLogSerializer.SerializeImmediateOutcomeAudit(
                snap, graph, auditEnabled: true, configuredControlPlayer: OpponentPlayer);
            AssertTrue(
                mismatch.IndexOf("audit_emitted\":false", StringComparison.OrdinalIgnoreCase) >= 0
                || mismatch.IndexOf("audit_emitted\": false", StringComparison.OrdinalIgnoreCase) >= 0,
                "seat-gated audit blocks wrong control seat");
        }

        // ---------------------------------------------------------------------
        // 6. Unknown capability → unknown_card_semantics
        // ---------------------------------------------------------------------
        static void UnknownCardSemanticsWhenCapabilityMissing()
        {
            DecisionSnapshot snap = CreateUnknownResponseChainSnapshot();
            LegalAction unknown = snap.LegalActions.First(a => a.CardId == 99990011);
            LlmImmediateOutcomeAnnotation ann =
                LlmImmediateOutcomeAnalyzer.AnalyzeRoot(snap, unknown);
            AssertNotNull(ann, "unknown annotation");
            AssertTrue(
                ann.IsUnknownCardSemantics
                || string.Equals(ann.Boundary, "unknown_card_semantics", StringComparison.OrdinalIgnoreCase)
                || (ann.Uncertainty != null && ann.Uncertainty.Any(u =>
                    u != null && u.IndexOf("unknown_card_semantics", StringComparison.OrdinalIgnoreCase) >= 0)),
                "missing capability facts fail open as unknown_card_semantics");
            AssertEqual(false, ann.PrimaryEffectStopped, "unknown must not claim stopped");
        }

        // ---------------------------------------------------------------------
        // 7. Outcome audit redaction
        // ---------------------------------------------------------------------
        static void OutcomeAuditNeverLeaksOpponentHidden()
        {
            DecisionSnapshot snap = CreateDustTornadoChainSnapshot(
                includeSecondaryCatalog: true,
                plantHiddenSentinels: true);
            LlmSelfResources self = ProjectSelf(snap);
            LlmSearchGraph graph = LlmTacticalAffordanceGraph.Build(
                snap, self, LlmSearchLimits.CreateDefault());
            string audit = LlmDecisionLogSerializer.SerializeImmediateOutcomeAudit(
                snap, graph, auditEnabled: true, configuredControlPlayer: ControlledPlayer);
            AssertFalse(audit.Contains(SentinelOppHandName), "no opp hand name");
            AssertFalse(audit.Contains(SentinelOppHandId.ToString()), "no opp hand id");
            AssertFalse(audit.Contains(SentinelOppSetName), "no opp set name");
            AssertFalse(audit.Contains(SentinelOppSetId.ToString()), "no opp set id");
            AssertFalse(audit.Contains(SentinelOppExtraName), "no opp extra name");
            AssertFalse(audit.Contains(SentinelOppExtraId.ToString()), "no opp extra id");
        }

        // ---------------------------------------------------------------------
        // 8. Public known-card redaction regression (if retained)
        // ---------------------------------------------------------------------
        static void PublicKnownCardRedactionRegression()
        {
            DecisionSnapshot snap = new DecisionSnapshot()
            {
                RunEffectSeq = 880,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = ControlledPlayer,
                ControlledPlayer = ControlledPlayer,
                Turn = 2,
                TurnPlayer = ControlledPlayer,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            PublicPlayerState self = EnsurePlayer(snap, ControlledPlayer);
            self.LifePoints = 8000;
            // Controlled open hand (face-up) may appear in public_state.
            self.KnownCards.Add(new PublicKnownCard()
            {
                Player = ControlledPlayer,
                Position = PosHand,
                Index = 0,
                CardUniqueId = 1,
                CardId = 46986414,
                Face = RuntimeFaceUp,
                Card = Meta(46986414, "Dark Magician", "Monster", "Normal", "main_deck_monster"),
            });
            // Own face-down ST must not expose identity in public_state sinks.
            self.KnownCards.Add(new PublicKnownCard()
            {
                Player = ControlledPlayer,
                Position = OwnStZone,
                Index = 0,
                CardUniqueId = 2,
                CardId = DustTornadoInternalCardId,
                Face = RuntimeFaceDown,
                Card = Meta(DustTornadoInternalCardId, "Dust Tornado", "Trap", "Normal", "trap"),
            });
            PublicPlayerState opp = EnsurePlayer(snap, OpponentPlayer);
            opp.LifePoints = 8000;
            // Revealed opponent hand (face-up) may appear.
            // Monster Reborn internal id from YdkIds: 83764718 5014 (hand reveal control only).
            const int MonsterRebornInternalCardId = 5014;
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = PosHand,
                Index = 0,
                CardUniqueId = 3,
                CardId = MonsterRebornInternalCardId,
                Face = RuntimeFaceUp,
                Card = Meta(MonsterRebornInternalCardId, "Monster Reborn", "Spell", "Normal", "spell"),
            });
            // Face-up field threat.
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = OppStZone,
                Index = 0,
                CardUniqueId = 4,
                CardId = FoolishBurialGoodsInternalCardId,
                Face = RuntimeFaceUp,
                Card = Meta(FoolishBurialGoodsInternalCardId, "Foolish Burial Goods", "Spell", "Normal", "spell"),
            });
            // GY public.
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = PosGrave,
                Index = 0,
                CardUniqueId = 5,
                CardId = 81439173,
                Face = RuntimeFaceUp,
                Card = Meta(81439173, "Foolish Burial", "Spell", "Normal", "spell"),
            });
            // Face-up banished public.
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = PosBanished,
                Index = 0,
                CardUniqueId = 6,
                CardId = 5318639,
                Face = RuntimeFaceUp,
                Card = Meta(5318639, "Mystical Space Typhoon", "Spell", "Normal", "spell"),
            });
            // Opponent-hidden face-down set + extra must not leak.
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = 6,
                Index = 0,
                CardUniqueId = 7,
                CardId = SentinelOppSetId,
                Face = RuntimeFaceDown,
                Card = Meta(SentinelOppSetId, SentinelOppSetName, "Trap", "Normal", "trap"),
            });
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = PosExtra,
                Index = 0,
                CardUniqueId = 8,
                CardId = SentinelOppExtraId,
                Face = RuntimeFaceUp,
                Card = Meta(SentinelOppExtraId, SentinelOppExtraName, "Monster", "Xyz", "xyz"),
            });
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = PosHand,
                Index = 1,
                CardUniqueId = 9,
                CardId = SentinelOppHandId,
                Face = RuntimeFaceDown,
                Card = Meta(SentinelOppHandId, SentinelOppHandName, "Monster", "Effect", "main_deck_monster"),
            });

            string publicJson = MiniJSON.Json.Serialize(
                LlmDecisionLogSerializer.SerializePublicState(snap.PublicState));
            string request = LlmBrokerProtocol.SerializeDecisionRequest(snap);

            AssertTrue(publicJson.Contains("Dark Magician") || publicJson.Contains("46986414"),
                "controlled face-up hand identity may appear");
            AssertTrue(publicJson.Contains("Monster Reborn") || publicJson.Contains("5014"),
                "revealed opponent hand identity may appear");
            AssertTrue(
                publicJson.Contains("Foolish Burial Goods")
                || publicJson.Contains(FoolishBurialGoodsInternalCardId.ToString()),
                "face-up field threat may appear");
            AssertTrue(publicJson.Contains("Foolish Burial") || publicJson.Contains("81439173"),
                "GY identity may appear");
            AssertTrue(publicJson.Contains("Mystical Space Typhoon") || publicJson.Contains("5318639"),
                "face-up banished may appear");

            AssertFalse(publicJson.Contains("Dust Tornado") && publicJson.Contains("\"face\":0"),
                "own face-down identity must not serialize as public known");
            // Hard: face-down own dust name should not appear at all in public_state after redaction.
            AssertFalse(publicJson.Contains("\"name\":\"Dust Tornado\""),
                "own face-down Dust Tornado name redacted from public_state");
            AssertFalse(publicJson.Contains(SentinelOppSetName), "opp face-down set redacted");
            AssertFalse(publicJson.Contains(SentinelOppHandName), "opp face-down hand redacted");
            AssertFalse(publicJson.Contains(SentinelOppExtraName), "opp extra redacted");
            AssertFalse(request.Contains(SentinelOppSetName), "request: no set leak");
            AssertFalse(request.Contains(SentinelOppHandName), "request: no hand leak");
            AssertFalse(request.Contains(SentinelOppExtraName), "request: no extra leak");

            // Own Extra Deck identity remains a self_resources concern, not public_state.
            // Build a minimal self projection expectation: Extra may appear in self_resources only.
            LlmSelfResources selfRes = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                2,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    // Use production entry construction if available via analyzer-safe path.
                });
            // Soft check: public_state must not contain a synthetic Extra Deck private name.
            AssertFalse(publicJson.Contains(SentinelOppExtraName), "extra never in public");
        }

        // =====================================================================
        // Fixtures — production-shaped DecisionSnapshot only
        // =====================================================================

        // ---------------------------------------------------------------------
        // 9. Catalog / live CardId must be Master Duel internal ids (YdkIds.txt)
        // ---------------------------------------------------------------------
        static void CatalogUsesInternalIdsNotYdkPasscodes()
        {
            LlmCardEffectCapabilityFacts dustFacts;
            AssertTrue(
                LlmCardEffectCapabilityCatalog.TryGet(DustTornadoInternalCardId, out dustFacts)
                && dustFacts != null
                && dustFacts.CapabilitiesGrounded,
                "catalog must resolve Dust Tornado internal id 4988");
            AssertTrue(
                dustFacts.ResponseCapabilities != null
                && dustFacts.ResponseCapabilities.Any(c =>
                    string.Equals(c, "destroy", StringComparison.OrdinalIgnoreCase)),
                "internal Dust entry carries destroy");

            LlmCardEffectCapabilityFacts passcodeFacts;
            AssertFalse(
                LlmCardEffectCapabilityCatalog.TryGet(DustTornadoYdkPasscode, out passcodeFacts)
                && passcodeFacts != null
                && passcodeFacts.CapabilitiesGrounded,
                "catalog must NOT key Dust Tornado by YGOPro passcode 60082869 "
                + "(passcode/internal confusion regression)");

            LlmCardEffectCapabilityFacts goodsFacts;
            AssertTrue(
                LlmCardEffectCapabilityCatalog.TryGet(FoolishBurialGoodsInternalCardId, out goodsFacts)
                && goodsFacts != null
                && goodsFacts.ResolutionDependencyGrounded,
                "catalog must resolve Foolish Burial Goods internal id 12800");
            AssertFalse(
                LlmCardEffectCapabilityCatalog.TryGet(FoolishBurialGoodsYdkPasscode, out goodsFacts)
                && goodsFacts != null
                && goodsFacts.ResolutionDependencyGrounded,
                "catalog must NOT key Foolish Burial Goods by passcode 35726888");

            // Live path with passcode CardId must fail open (unknown), proving catalog is internal-only.
            DecisionSnapshot passcodeSnap = CreateDustTornadoChainSnapshot(
                includeSecondaryCatalog: true,
                plantHiddenSentinels: false,
                useYdkPasscodesAsCardIds: true);
            LegalAction passcodeDust = FindRoot(passcodeSnap, DustTornadoYdkPasscode);
            AssertNotNull(passcodeDust, "passcode fixture root");
            LlmImmediateOutcomeAnnotation passcodeAnn =
                LlmImmediateOutcomeAnalyzer.AnalyzeRoot(passcodeSnap, passcodeDust);
            AssertTrue(
                passcodeAnn.IsUnknownCardSemantics
                || string.Equals(
                    passcodeAnn.Boundary,
                    "unknown_card_semantics",
                    StringComparison.OrdinalIgnoreCase),
                "passcode CardId must not silently hit internal-id catalog entries");
        }

        // ---------------------------------------------------------------------
        // 10. Two face-up S/T without explicit target → unknown (no arbitrary pick)
        // ---------------------------------------------------------------------
        static void AmbiguousTwoFaceUpSpellTrapsFailOpenWithoutTargetAction()
        {
            DecisionSnapshot snap = CreateAmbiguousTwoFaceUpSpellTrapChainSnapshot();
            LegalAction dust = FindRoot(snap, DustTornadoInternalCardId);
            AssertNotNull(dust, "dust root");
            // No IsEffectTargetSelection actions — only two public face-up S/T candidates.
            AssertFalse(
                snap.LegalActions.Any(a => a != null && a.IsEffectTargetSelection),
                "fixture has no explicit target action");

            LlmImmediateOutcomeAnnotation ann =
                LlmImmediateOutcomeAnalyzer.AnalyzeRoot(snap, dust);
            AssertNotNull(ann, "annotation");
            AssertTrue(
                ann.IsUnknownCardSemantics
                || string.Equals(
                    ann.Boundary,
                    "unknown_card_semantics",
                    StringComparison.OrdinalIgnoreCase)
                || (ann.Uncertainty != null && ann.Uncertainty.Any(u =>
                    u != null
                    && u.IndexOf("unknown_card_semantics", StringComparison.OrdinalIgnoreCase) >= 0)),
                "ambiguous multi face-up S/T without explicit chain target must fail open");
            AssertEqual(false, ann.PrimaryEffectStopped,
                "ambiguity must not invent primary_effect_stopped");
            // Must not arbitrarily prefer a Normal-frame card as the grounded target.
            AssertFalse(
                ann.TargetCardId == FoolishBurialGoodsInternalCardId
                && !ann.IsUnknownCardSemantics
                && ann.TargetEffectExpectedToResolve
                && ann.ValueZeroPrimaryPenalty > 0,
                "must not pick Foolish Burial Goods among multiple face-up candidates without explicit target");
        }

        static DecisionSnapshot CreateDustTornadoChainSnapshot(
            bool includeSecondaryCatalog,
            bool plantHiddenSentinels = false,
            bool useYdkPasscodesAsCardIds = false)
        {
            // Catalog registration is production default; secondary flag reserved for future.
            _ = includeSecondaryCatalog;

            int dustId = useYdkPasscodesAsCardIds
                ? DustTornadoYdkPasscode
                : DustTornadoInternalCardId;
            int goodsId = useYdkPasscodesAsCardIds
                ? FoolishBurialGoodsYdkPasscode
                : FoolishBurialGoodsInternalCardId;

            LlmCardMetadata dust = Meta(
                dustId, "Dust Tornado", "Trap", "Normal", "trap",
                "Target 1 Spell/Trap; destroy that target. Then you can Set 1 Spell/Trap from your hand.");
            LlmCardMetadata goods = Meta(
                goodsId, "Foolish Burial Goods", "Spell", "Normal", "spell",
                "Send 1 Spell/Trap from your Deck to the GY.");

            DecisionSnapshot snap = new DecisionSnapshot()
            {
                RunEffectSeq = 710,
                ViewType = DuelViewType.WaitInput,
                ViewParam1 = (int)DuelMenuActType.CheckChain,
                ActingPlayer = ControlledPlayer,
                ControlledPlayer = ControlledPlayer,
                Turn = 4,
                TurnPlayer = OpponentPlayer,
                CurrentPhase = (int)DuelPhase.Main1,
                IsStrategicWindow = true,
                StrategicWindowReason = "chain_response",
            };
            EnsurePlayer(snap, ControlledPlayer).LifePoints = 7000;
            EnsurePlayer(snap, OpponentPlayer).LifePoints = 8000;

            // Own face-down Dust Tornado (response card).
            PlaceKnown(snap, ControlledPlayer, OwnStZone, 0, 501, dustId, RuntimeFaceDown, dust);
            // Opponent face-up already-activated one-shot (chain head).
            PlaceKnown(snap, OpponentPlayer, OppStZone, 0, 601, goodsId, RuntimeFaceUp, goods);

            if (plantHiddenSentinels)
            {
                PlaceKnown(snap, OpponentPlayer, PosHand, 0, 901, SentinelOppHandId, RuntimeFaceDown,
                    Meta(SentinelOppHandId, SentinelOppHandName, "Monster", "Effect", "main_deck_monster"));
                PlaceKnown(snap, OpponentPlayer, 6, 0, 902, SentinelOppSetId, RuntimeFaceDown,
                    Meta(SentinelOppSetId, SentinelOppSetName, "Trap", "Normal", "trap"));
                PlaceKnown(snap, OpponentPlayer, PosExtra, 0, 903, SentinelOppExtraId, RuntimeFaceUp,
                    Meta(SentinelOppExtraId, SentinelOppExtraName, "Monster", "Xyz", "xyz"));
            }

            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Player = ControlledPlayer,
                Position = OwnStZone,
                Index = 0,
                Command = DuelCommandType.Action,
                CardUniqueId = 501,
                CardId = dustId,
                Card = dust,
                ActionLabel = "Activate Dust Tornado",
                IsMechanical = false,
                StrategicRole = "chain_response",
                RequiresTarget = true,
                TargetScope = "opponent_spell_trap",
            });
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.Cancel,
                Player = ControlledPlayer,
                ActionLabel = "Decline response",
                IsMechanical = false,
                StrategicRole = "decline",
                CancelDecide = true,
            });
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 2,
                Kind = LegalActionKind.Command,
                Player = OpponentPlayer,
                Position = OppStZone,
                Index = 0,
                Command = DuelCommandType.Decide,
                CardUniqueId = 601,
                CardId = goodsId,
                Card = goods,
                ActionLabel = "Target Foolish Burial Goods",
                IsMechanical = false,
                StrategicRole = "effect_target",
                IsEffectTargetSelection = true,
            });
            snap.StrategicActionCount = 3;
            return snap;
        }

        static DecisionSnapshot CreateAmbiguousTwoFaceUpSpellTrapChainSnapshot()
        {
            LlmCardMetadata dust = Meta(
                DustTornadoInternalCardId, "Dust Tornado", "Trap", "Normal", "trap",
                "Target 1 Spell/Trap; destroy.");
            LlmCardMetadata goods = Meta(
                FoolishBurialGoodsInternalCardId, "Foolish Burial Goods", "Spell", "Normal", "spell",
                "Send 1 Spell/Trap from your Deck to the GY.");
            // Second face-up Normal S/T (e.g. another public backrow) — not the unique candidate.
            LlmCardMetadata other = Meta(
                5014, "Monster Reborn", "Spell", "Normal", "spell",
                "Special Summon from GY.");

            DecisionSnapshot snap = new DecisionSnapshot()
            {
                RunEffectSeq = 712,
                ViewType = DuelViewType.WaitInput,
                ViewParam1 = (int)DuelMenuActType.CheckChain,
                ActingPlayer = ControlledPlayer,
                ControlledPlayer = ControlledPlayer,
                Turn = 5,
                TurnPlayer = OpponentPlayer,
                CurrentPhase = (int)DuelPhase.Main1,
                IsStrategicWindow = true,
                StrategicWindowReason = "chain_response",
            };
            EnsurePlayer(snap, ControlledPlayer).LifePoints = 7000;
            EnsurePlayer(snap, OpponentPlayer).LifePoints = 8000;
            PlaceKnown(snap, ControlledPlayer, OwnStZone, 0, 501, DustTornadoInternalCardId, RuntimeFaceDown, dust);
            PlaceKnown(snap, OpponentPlayer, OppStZone, 0, 601, FoolishBurialGoodsInternalCardId, RuntimeFaceUp, goods);
            PlaceKnown(snap, OpponentPlayer, 6, 0, 602, 5014, RuntimeFaceUp, other);

            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Player = ControlledPlayer,
                Position = OwnStZone,
                Index = 0,
                Command = DuelCommandType.Action,
                CardUniqueId = 501,
                CardId = DustTornadoInternalCardId,
                Card = dust,
                ActionLabel = "Activate Dust Tornado",
                IsMechanical = false,
                StrategicRole = "chain_response",
                RequiresTarget = true,
                TargetScope = "opponent_spell_trap",
            });
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.Cancel,
                Player = ControlledPlayer,
                ActionLabel = "Decline response",
                IsMechanical = false,
                StrategicRole = "decline",
                CancelDecide = true,
            });
            // Intentionally no IsEffectTargetSelection — ambiguity over two face-up S/T.
            snap.StrategicActionCount = 2;
            return snap;
        }

        static DecisionSnapshot CreateUnknownResponseChainSnapshot()
        {
            LlmCardMetadata unknown = Meta(
                99990011, "UNKNOWN_CHAIN_RESPONSE", "Trap", "Normal", "trap",
                "No grounded capability table entry.");
            LlmCardMetadata goods = Meta(
                FoolishBurialGoodsInternalCardId, "Foolish Burial Goods", "Spell", "Normal", "spell",
                "Send 1 Spell/Trap from your Deck to the GY.");

            DecisionSnapshot snap = new DecisionSnapshot()
            {
                RunEffectSeq = 711,
                ViewType = DuelViewType.WaitInput,
                ViewParam1 = (int)DuelMenuActType.CheckChain,
                ActingPlayer = ControlledPlayer,
                ControlledPlayer = ControlledPlayer,
                Turn = 4,
                TurnPlayer = OpponentPlayer,
                CurrentPhase = (int)DuelPhase.Main1,
                IsStrategicWindow = true,
                StrategicWindowReason = "chain_response",
            };
            EnsurePlayer(snap, ControlledPlayer).LifePoints = 7000;
            EnsurePlayer(snap, OpponentPlayer).LifePoints = 8000;
            PlaceKnown(snap, ControlledPlayer, OwnStZone, 0, 531, 99990011, RuntimeFaceDown, unknown);
            PlaceKnown(snap, OpponentPlayer, OppStZone, 0, 631, FoolishBurialGoodsInternalCardId, RuntimeFaceUp, goods);

            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Player = ControlledPlayer,
                Position = OwnStZone,
                Index = 0,
                Command = DuelCommandType.Action,
                CardUniqueId = 531,
                CardId = 99990011,
                Card = unknown,
                ActionLabel = "Activate UNKNOWN_CHAIN_RESPONSE",
                IsMechanical = false,
                StrategicRole = "chain_response",
            });
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.Cancel,
                Player = ControlledPlayer,
                ActionLabel = "Decline response",
                IsMechanical = false,
                StrategicRole = "decline",
                CancelDecide = true,
            });
            snap.StrategicActionCount = 2;
            return snap;
        }

        static LegalAction FindRoot(DecisionSnapshot snap, int cardId)
        {
            return snap.LegalActions.FirstOrDefault(a =>
                a != null && !a.IsMechanical && a.CardId == cardId);
        }

        static LlmSelfResources ProjectSelf(DecisionSnapshot snap)
        {
            // Minimal empty self resources for graph build (chain response needs no materials).
            return LlmSelfResources.Create(
                snap.ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                snap.Turn,
                snap.CurrentPhase,
                snap.CurrentStep,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>());
        }

        static PublicPlayerState EnsurePlayer(DecisionSnapshot snap, int player)
        {
            foreach (PublicPlayerState p in snap.PublicState.Players)
            {
                if (p.Player == player)
                {
                    return p;
                }
            }
            PublicPlayerState created = new PublicPlayerState() { Player = player };
            snap.PublicState.Players.Add(created);
            return created;
        }

        static void PlaceKnown(
            DecisionSnapshot snap,
            int player,
            int position,
            int index,
            int uniqueId,
            int cardId,
            int face,
            LlmCardMetadata card)
        {
            PublicPlayerState ps = EnsurePlayer(snap, player);
            PublicPositionState pos = ps.Positions.FirstOrDefault(p => p.Position == position);
            if (pos == null)
            {
                pos = new PublicPositionState() { Position = position, Count = index + 1 };
                ps.Positions.Add(pos);
            }
            else if (index >= pos.Count)
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
            int id,
            string name,
            string kind,
            string frame,
            string family,
            string text = null)
        {
            return new LlmCardMetadata()
            {
                CardId = id,
                Name = name,
                Kind = kind,
                Frame = frame,
                SummonFamily = family,
                Text = text ?? name,
            };
        }

        static void AssertTrue(bool v, string m)
        {
            if (!v)
            {
                throw new Exception(m);
            }
        }

        static void AssertFalse(bool v, string m)
        {
            if (v)
            {
                throw new Exception(m);
            }
        }

        static void AssertNotNull(object v, string m)
        {
            if (v == null)
            {
                throw new Exception(m + ": expected non-null");
            }
        }

        static void AssertEqual<T>(T expected, T actual, string m)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(m + ": expected " + expected + ", got " + actual);
            }
        }
    }
}

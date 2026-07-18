using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-005 Slice 6B: receding-horizon semantic planning (TESTS-ONLY RED).
    /// Hardened independent-review contract: exact public APIs only (no name aliases / soft fallbacks).
    /// Reflection is used only so the suite compiles before production types exist.
    /// First failure remains missing LlmComboTemplateCatalog until GREEN implements the surface.
    /// </summary>
    static class Llm005Slice6BTests
    {
        const int ControlledPlayer = 1;
        const int OpponentPlayer = 0;

        const int GirochinInternalCardId = 5136;
        const int CaveDragonInternalCardId = 4103;
        const int UtopiaInternalCardId = 9575;
        const int JunkSynchronInternalCardId = 7687;
        const int StardustDragonInternalCardId = 7734;
        const int SynthNonTunerLevel5Id = 99105201;
        /// <summary>Post-root legal Utopia SummonSp action id (distinct from setup root 0).</summary>
        const int PostRootUtopiaSummonSpActionId = 7;

        const string GirochinName = "Girochin Kuwagata";
        const string CaveDragonName = "The Dragon Dwelling in the Cave";
        const string UtopiaName = "Number 39: Utopia";
        const string JunkSynchronName = "Junk Synchron";
        const string StardustName = "Stardust Dragon";

        const string ExpectedXyzTemplateId = "cubic_girochin_5136_cave_4103_rank4_utopia_9575";
        const string ExpectedSynchroTemplateId = "junk_synchron_stardust_level_sum";
        const string ProvenanceRulesInferred = "rules_inferred";
        const string ProvenanceModelHypothesis = "model_hypothesis";

        // Exact production type names — no aliases.
        const string CatalogTypeName = "LlmComboTemplateCatalog";
        const string MatcherTypeName = "LlmComboTemplateMatcher";
        const string HypothesisAdmissionTypeName = "LlmModelHypothesisLineAdmission";
        const string IntendedFollowupTypeName = "LlmIntendedFollowupMemory";
        const string IntendedFollowupEvaluatorTypeName = "LlmIntendedFollowupEvaluator";
        const string CorpusManifestTypeName = "LlmSlice6BCorpusCoverageManifest";

        // Multiple arbitrary sentinel identities (not one string prefix).
        static readonly int[] SentinelOppIds = new int[]
        {
            99106001, 99106017, 77112233, 42424201, 88001122,
        };
        static readonly string[] SentinelOppNames = new string[]
        {
            "SENTINEL_OPP_HAND_6B_ALPHA",
            "SENTINEL_OPP_SET_6B_BETA",
            "SENTINEL_OPP_EXTRA_6B_GAMMA",
            "HIDDEN_RIVAL_CARD_ZULU",
            "OPP_PRIVATE_IDENTITY_OMEGA",
        };

        public static void RunAll()
        {
            // (1) First missing production type remains the intentional RED.
            Type catalogType = RequireExactType(
                CatalogTypeName,
                "LlmComboTemplateCatalog.CreateDefault() with Templates — offline-validated combo catalog "
                + "(Girochin 5136 + Cave 4103 -> Rank4/Utopia 9575 and grounded Synchro control)");

            Type matcherType = RequireExactType(
                MatcherTypeName,
                "LlmComboTemplateMatcher.ExpandToGraph(DecisionSnapshot, LlmSelfResources, LlmSearchLimits)");

            MethodInfo createDefault = RequireExactPublicStaticMethod(
                catalogType, "CreateDefault", Type.EmptyTypes);
            object catalog = createDefault.Invoke(null, null);
            AssertNotNull(catalog, "CreateDefault() catalog instance");
            IEnumerable templates = RequireExactEnumerableProperty(catalog, "Templates");

            ComboTemplateCatalogExactIdentities(templates);
            LlmSearchGraph preRootGraph = ExpandExact(matcherType, MakeXyzSetupSnapshot(), MakeXyzReadySelf());
            TemplateExpandToGraphRootCompletenessAndStructuredRequirements(preRootGraph);
            MalformedDuplicateSourceAndAbsentEdControls(matcherType);
            HiddenOpponentOnlyNeverMatches(matcherType);
            SynchroMatcherPositiveAndNegativeControls(matcherType);

            Type admitType = RequireExactType(
                HypothesisAdmissionTypeName,
                "LlmModelHypothesisLineAdmission.TryAdmit(DecisionSnapshot, LlmSearchGraph, LlmSearchLine, "
                + "out LlmSearchLine, out string)");
            ModelHypothesisAdmissionExactContract(admitType, preRootGraph);

            Type memoryType = RequireExactType(
                IntendedFollowupTypeName,
                "LlmIntendedFollowupMemory.FromSelectedLine(DecisionSnapshot, LlmSearchLine)");
            Type evalType = RequireExactType(
                IntendedFollowupEvaluatorTypeName,
                "LlmIntendedFollowupEvaluator.Evaluate(LlmIntendedFollowupMemory, DecisionSnapshot, LlmSearchGraph)");
            // FromSelectedLine uses pre-commit flip-Cave line; all Evaluate assertions use POST-ROOT windows.
            IntendedFollowupExactFieldsAndPostRootStatusSemantics(memoryType, evalType, preRootGraph);
            TurnMemoryTrackerIntegrationExactOverloads(memoryType, evalType, preRootGraph);

            ProjectionAndSerializeTurnMemoryPairedHidden(preRootGraph, memoryType);
            CorpusCoverageManifestExact();
            // Default production audit path must use combo matcher (not disconnected types alone).
            PlanningSearchAuditDefaultFactoryEmitsXyzTemplateAndHiddenInvariance();

            // Independent GREEN-review remediation (test-first): budgets, grounding, intent, multiset, hypothesis, projection.
            IndependentReviewRemediationContracts(matcherType, admitType, memoryType, evalType, preRootGraph);
        }

        // =====================================================================
        // Independent GREEN-review remediation contracts (budget / grounding / intent)
        // =====================================================================
        static void IndependentReviewRemediationContracts(
            Type matcherType,
            Type admitType,
            Type memoryType,
            Type evalType,
            LlmSearchGraph defaultPreRootGraph)
        {
            TemplateExpansionObeysBoundedSearchContracts(matcherType);
            XyzAndSynchroRequireGroundedMetadata(matcherType);
            FromSelectedLineAndTrackerRejectInvalidLines(memoryType, defaultPreRootGraph);
            EvaluatorMultisetMaterialGrounding(memoryType, evalType, defaultPreRootGraph);
            HypothesisAdmissionStripsScoreFeaturesAndCapsScore(admitType, defaultPreRootGraph);
            ProjectionUsesSingleLowercaseStructuredKeys(defaultPreRootGraph);

            // Final independent remediation (intent lifecycle / IsInternallyValid / budget telemetry).
            IntentLifecycleClearsStaleOnEveryStrategicCommit(memoryType, defaultPreRootGraph);
            SummonRequirementIsInternallyValidStrictContract();
            TemplateBudgetTelemetryReasonsAndBoundaryNodes(matcherType);
        }

        static void TemplateExpansionObeysBoundedSearchContracts(Type matcherType)
        {
            DecisionSnapshot snap = MakeXyzSetupSnapshot();
            LlmSelfResources self = MakeXyzReadySelf();

            // (1a) MaxStrategicDepth=0: no template continuations; root shells complete; no charged expansions.
            LlmSearchLimits depth0 = LlmSearchLimits.CreateDefault();
            depth0.MaxStrategicDepth = 0;
            LlmSearchGraph gDepth0 = ExpandWithLimits(matcherType, snap, self, depth0);
            AssertRootShellsComplete(gDepth0, 0, 1, 2);
            AssertTrue(FindXyzTemplateLine(gDepth0) == null,
                "MaxStrategicDepth=0 must emit no Xyz template continuation");
            AssertEqual(0, CountTemplateLines(gDepth0),
                "MaxStrategicDepth=0 must emit zero TemplateId lines");
            AssertEqual(0, gDepth0.Coverage.ContinuationExpansionCount,
                "depth0 ContinuationExpansionCount must be 0");
            AssertEqual(0, gDepth0.Coverage.ExpandedNodes,
                "depth0 ExpandedNodes must be 0");
            AssertEqual("budget_incomplete", gDepth0.Status,
                "depth0 must surface budget_incomplete when a structural template is depth-blocked");
            AssertCoverageReasonContains(gDepth0, "template_depth_budget",
                "depth0 must record template_depth_budget for structurally matchable Xyz");

            // (1b) MaxNodes=1 hard cap includes generic + template continuations.
            LlmSearchLimits nodes1 = LlmSearchLimits.CreateDefault();
            nodes1.MaxNodes = 1;
            nodes1.MaxStrategicDepth = 4;
            nodes1.BeamWidth = 12;
            LlmSearchGraph gN1 = ExpandWithLimits(matcherType, snap, self, nodes1);
            AssertRootShellsComplete(gN1, 0, 1, 2);
            AssertTrue(gN1.Coverage.ContinuationExpansionCount <= 1,
                "MaxNodes=1: ContinuationExpansionCount must be <= 1 (got "
                + gN1.Coverage.ContinuationExpansionCount + ")");
            AssertEqual(gN1.Coverage.ContinuationExpansionCount, gN1.Coverage.ExpandedNodes,
                "MaxNodes=1: ExpandedNodes must equal ContinuationExpansionCount");
            AssertTrue(CountNonRootShellLines(gN1) <= 1,
                "MaxNodes=1: non-shell lines (generic+template) must be <= 1");
            if (FindXyzTemplateLine(gN1) != null)
            {
                AssertTrue(gN1.Coverage.ContinuationExpansionCount >= 1,
                    "emitted template must charge ContinuationExpansionCount");
            }

            // (1c) MaxNodes=2 hard cap.
            LlmSearchLimits nodes2 = LlmSearchLimits.CreateDefault();
            nodes2.MaxNodes = 2;
            nodes2.MaxStrategicDepth = 4;
            LlmSearchGraph gN2 = ExpandWithLimits(matcherType, snap, self, nodes2);
            AssertRootShellsComplete(gN2, 0, 1, 2);
            AssertTrue(gN2.Coverage.ContinuationExpansionCount <= 2,
                "MaxNodes=2: ContinuationExpansionCount <= 2 (got "
                + gN2.Coverage.ContinuationExpansionCount + ")");
            AssertEqual(gN2.Coverage.ContinuationExpansionCount, gN2.Coverage.ExpandedNodes,
                "MaxNodes=2: ExpandedNodes == ContinuationExpansionCount");
            AssertTrue(CountNonRootShellLines(gN2) <= 2,
                "MaxNodes=2: non-shell lines <= 2");

            // (1d) BeamWidth=1 caps template admission per root (generic Layer A fairness may
            // already place one fairness continuation per root + remainder; templates must not
            // push a root that already sits at BeamWidth).
            LlmSearchLimits beam1 = LlmSearchLimits.CreateDefault();
            beam1.BeamWidth = 1;
            beam1.MaxNodes = 96;
            beam1.MaxStrategicDepth = 4;
            LlmSearchGraph gBeam = ExpandWithLimits(matcherType, snap, self, beam1);
            AssertRootShellsComplete(gBeam, 0, 1, 2);
            Dictionary<int, int> contPerRoot = CountContinuationsPerRoot(gBeam);
            // Any root that carries a TemplateId line must respect BeamWidth on that root.
            if (gBeam.Lines != null)
            {
                foreach (LlmSearchLine line in gBeam.Lines)
                {
                    if (line == null || string.IsNullOrEmpty(line.TemplateId))
                    {
                        continue;
                    }
                    int c;
                    contPerRoot.TryGetValue(line.RootActionId, out c);
                    AssertTrue(c <= 1,
                        "BeamWidth=1: template-bearing root " + line.RootActionId
                        + " must have <= 1 continuation total (got " + c + ")");
                }
            }
            // If flip root already had a generic continuation, template must not also appear.
            int root0Cont;
            contPerRoot.TryGetValue(0, out root0Cont);
            if (root0Cont > 1)
            {
                AssertTrue(FindXyzTemplateLine(gBeam) == null,
                    "BeamWidth=1: must not emit template when root already exceeds beam");
            }
            AssertEqual(gBeam.Coverage.ContinuationExpansionCount, gBeam.Coverage.ExpandedNodes,
                "beam1 ExpandedNodes == ContinuationExpansionCount");

            // (1e) Fake clock / wall budget cannot be bypassed by template emission.
            LlmSearchFakeBudgetClock clock = new LlmSearchFakeBudgetClock();
            clock.AdvanceMilliseconds(10_000);
            LlmSearchLimits timed = LlmSearchLimits.CreateDefault();
            timed.MaxWallMs = 1;
            timed.MaxStrategicDepth = 4;
            timed.MaxNodes = 96;
            timed.Clock = clock;
            LlmSearchGraph gTime = ExpandWithLimits(matcherType, snap, self, timed);
            AssertRootShellsComplete(gTime, 0, 1, 2);
            AssertEqual("budget_incomplete", gTime.Status,
                "elapsed wall budget must yield budget_incomplete (cannot bypass via templates)");
            AssertTrue(FindXyzTemplateLine(gTime) == null,
                "time-exhausted ExpandToGraph must not emit template continuations");
            AssertTrue(gTime.Coverage.ContinuationExpansionCount == 0,
                "time-exhausted ContinuationExpansionCount must stay 0 when no expansions ran");
            AssertEqual(gTime.Coverage.ContinuationExpansionCount, gTime.Coverage.ExpandedNodes,
                "time-budget ExpandedNodes consistent with ContinuationExpansionCount");
            AssertCoverageReasonContains(gTime, "template_time_budget",
                "time-exhausted must record template_time_budget for structural Xyz match");

            // (1f) Happy-path telemetry: default limits emit template and charge coverage.
            LlmSearchGraph gOk = ExpandWithLimits(matcherType, snap, self, LlmSearchLimits.CreateDefault());
            LlmSearchLine tmpl = FindXyzTemplateLine(gOk);
            AssertNotNull(tmpl, "default limits still emit Xyz template");
            AssertTrue(gOk.Coverage.ContinuationExpansionCount >= 1,
                "emitted template(s) must increment ContinuationExpansionCount");
            AssertEqual(gOk.Coverage.ContinuationExpansionCount, gOk.Coverage.ExpandedNodes,
                "default path ExpandedNodes == ContinuationExpansionCount");
            AssertTrue(gOk.Coverage.ContinuationExpansionCount >= CountNonRootShellLines(gOk)
                || gOk.Coverage.ContinuationExpansionCount == CountNonRootShellLines(gOk),
                "coverage continuation count must account for non-shell lines");
            int templateCount = CountTemplateLines(gOk);
            AssertTrue(gOk.Coverage.BoundaryNodes >= templateCount,
                "each emitted fresh-window template boundary must increment BoundaryNodes "
                + "(BoundaryNodes=" + gOk.Coverage.BoundaryNodes + " templates=" + templateCount + ")");
        }

        // =====================================================================
        // Final remediation: intent lifecycle / IsInternallyValid / budget telemetry
        // =====================================================================
        static void IntentLifecycleClearsStaleOnEveryStrategicCommit(
            Type memoryType, LlmSearchGraph preRootGraph)
        {
            MethodInfo record4 = typeof(LlmTurnMemoryTracker).GetMethod(
                "RecordCommittedAction",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[]
                {
                    typeof(DecisionSnapshot),
                    typeof(LlmBrokerDecisionResponse),
                    typeof(LegalAction),
                    typeof(LlmSearchLine),
                },
                null);
            MethodInfo record3 = typeof(LlmTurnMemoryTracker).GetMethod(
                "RecordCommittedAction",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[]
                {
                    typeof(DecisionSnapshot),
                    typeof(LlmBrokerDecisionResponse),
                    typeof(LegalAction),
                },
                null);
            MethodInfo createWithGraph = typeof(LlmTurnMemoryTracker).GetMethod(
                "CreateSnapshot",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(DecisionSnapshot), typeof(LlmSearchGraph) },
                null);
            PropertyInfo intentProp = typeof(LlmTurnMemoryState).GetProperty(
                "IntendedFollowup", BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(record4, "4-arg RecordCommittedAction");
            AssertNotNull(record3, "3-arg RecordCommittedAction");
            AssertNotNull(createWithGraph, "CreateSnapshot(snap, graph)");
            AssertNotNull(intentProp, "IntendedFollowup");

            LlmTurnMemoryTracker tracker = new LlmTurnMemoryTracker(8);
            DecisionSnapshot pre = MakeXyzSetupSnapshot();
            pre.Turn = 5;
            pre.RunEffectSeq = 100UL;
            LlmSearchLine xyzLine = FindXyzTemplateLine(preRootGraph);
            AssertNotNull(xyzLine, "xyz template for intent lifecycle");
            LegalAction setupAction = pre.LegalActions[0];
            LlmBrokerDecisionResponse resp = new LlmBrokerDecisionResponse()
            {
                ActionId = 0,
                Reason = "setup",
                Plan = ExpectedXyzTemplateId,
            };

            // Seed intent.
            record4.Invoke(tracker, new object[] { pre, resp, setupAction, xyzLine });
            LlmTurnMemoryState seeded = (LlmTurnMemoryState)createWithGraph.Invoke(
                tracker, new object[] { pre, preRootGraph });
            AssertNotNull(intentProp.GetValue(seeded, null), "seed: intent present");

            // set -> 3-arg recovery clears
            record3.Invoke(tracker, new object[] { pre, resp, setupAction });
            LlmTurnMemoryState after3 = (LlmTurnMemoryState)createWithGraph.Invoke(
                tracker, new object[] { pre, preRootGraph });
            AssertTrue(intentProp.GetValue(after3, null) == null,
                "3-arg recovery/normal commit must clear any prior intent");

            // Re-seed
            record4.Invoke(tracker, new object[] { pre, resp, setupAction, xyzLine });
            AssertNotNull(
                intentProp.GetValue(
                    createWithGraph.Invoke(tracker, new object[] { pre, preRootGraph }), null),
                "re-seed intent");

            // set -> mismatched 4-arg clears
            LlmSearchLine wrongRoot = CloneLine(xyzLine);
            wrongRoot.RootActionId = 1;
            record4.Invoke(tracker, new object[] { pre, resp, setupAction, wrongRoot });
            AssertTrue(
                intentProp.GetValue(
                    createWithGraph.Invoke(tracker, new object[] { pre, preRootGraph }), null) == null,
                "4-arg mismatched root must clear prior intent (not leave stale)");

            // Re-seed
            record4.Invoke(tracker, new object[] { pre, resp, setupAction, xyzLine });
            AssertNotNull(
                intentProp.GetValue(
                    createWithGraph.Invoke(tracker, new object[] { pre, preRootGraph }), null),
                "re-seed after mismatch clear");

            // set -> actual Utopia engine-current root clears/completes (strategic commit, no template)
            DecisionSnapshot postUtopia = MakePostRootMaterialsGroundedSnapshot(turn: 5, seq: 101UL);
            postUtopia.LegalActions.Add(MakeUtopiaSummonSp(PostRootUtopiaSummonSpActionId));
            postUtopia.LegalActions.Add(MakeEndRoot(1));
            postUtopia.StrategicActionCount = 2;
            LegalAction utopiaAction = postUtopia.LegalActions[0];
            LlmBrokerDecisionResponse utopiaResp = new LlmBrokerDecisionResponse()
            {
                ActionId = PostRootUtopiaSummonSpActionId,
                Reason = "engine_current_utopia",
                Plan = "complete",
            };
            // 4-arg with engine-current root shell (non-template) clears.
            record4.Invoke(tracker, new object[]
            {
                postUtopia, utopiaResp, utopiaAction, RootShell(PostRootUtopiaSummonSpActionId),
            });
            LlmSearchGraph postGraph = MakePostRootGraphWithUtopiaLegal(
                postUtopia, MakePostRootBothMaterialsFaceUpSelf(), PostRootUtopiaSummonSpActionId);
            AssertTrue(
                intentProp.GetValue(
                    createWithGraph.Invoke(tracker, new object[] { postUtopia, postGraph }), null) == null,
                "Utopia engine-current root shell commit must clear/complete prior setup intent");

            // Re-seed on setup window
            record4.Invoke(tracker, new object[] { pre, resp, setupAction, xyzLine });
            object afterReseed = intentProp.GetValue(
                createWithGraph.Invoke(tracker, new object[] { pre, preRootGraph }), null);
            AssertNotNull(afterReseed, "re-seed before replace");
            AssertEqual(ExpectedXyzTemplateId, RequireStringProp(afterReseed, "TemplateId"),
                "seeded TemplateId is Xyz");

            // set -> different valid template replaces (Synchro)
            DecisionSnapshot synSnap = MakeBaseSnapshot();
            synSnap.Turn = 5;
            synSnap.RunEffectSeq = 110UL;
            synSnap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = ControlledPlayer,
                Position = 13,
                Index = 0,
                CardId = SynthNonTunerLevel5Id,
                Card = new LlmCardMetadata()
                {
                    CardId = SynthNonTunerLevel5Id,
                    Name = "SYNTH_L5_NONTUNER",
                    Level = 5,
                    Kind = "Effect",
                    Frame = "Effect",
                    SummonFamily = "main_deck_monster",
                    IsTuner = false,
                },
                ActionLabel = "Summon SYNTH_L5_NONTUNER",
            });
            synSnap.LegalActions.Add(MakeEndRoot(1));
            synSnap.StrategicActionCount = 2;
            LlmSelfResources synSelf = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(JunkSynchronInternalCardId, JunkSynchronName, 0, true, 3, true, 0),
                },
                hand: new List<LlmSelfResourceCard>()
                {
                    FieldCard(SynthNonTunerLevel5Id, "SYNTH_L5_NONTUNER", 13, true, 5, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, 8),
                });
            Type matcherType = RequireExactType(MatcherTypeName, "matcher for synchro replace");
            LlmSearchGraph synGraph = ExpandExact(matcherType, synSnap, synSelf);
            LlmSearchLine synLine = FindSynchroTemplateLine(synGraph);
            AssertNotNull(synLine, "synchro template for replace");
            LegalAction synAction = synSnap.LegalActions[0];
            LlmBrokerDecisionResponse synResp = new LlmBrokerDecisionResponse()
            {
                ActionId = 0,
                Reason = "synchro_setup",
                Plan = ExpectedSynchroTemplateId,
            };
            record4.Invoke(tracker, new object[] { synSnap, synResp, synAction, synLine });
            object replaced = intentProp.GetValue(
                createWithGraph.Invoke(tracker, new object[] { synSnap, synGraph }), null);
            AssertNotNull(replaced, "valid new template must replace prior intent");
            AssertEqual(ExpectedSynchroTemplateId, RequireStringProp(replaced, "TemplateId"),
                "replaced intent TemplateId must be Synchro (not stale Xyz)");
        }

        static void SummonRequirementIsInternallyValidStrictContract()
        {
            // Positive control
            LlmSummonRequirement ok = new LlmSummonRequirement()
            {
                SummonFamily = "xyz",
                Rank = 4,
                MaterialCount = 2,
                ExtraDeckTargetCardId = UtopiaInternalCardId,
                OrderedMaterialCardIds = new List<int>() { CaveDragonInternalCardId, GirochinInternalCardId },
                MaterialSources = new List<LlmMaterialSource>()
                {
                    new LlmMaterialSource() { Zone = 1, Index = 0, CardId = CaveDragonInternalCardId },
                    new LlmMaterialSource() { Zone = 0, Index = 0, CardId = GirochinInternalCardId },
                },
            };
            AssertTrue(ok.IsInternallyValid(), "positive IsInternallyValid");

            // MaterialCount must be > 0
            LlmSummonRequirement zeroCount = CloneReq(ok);
            zeroCount.MaterialCount = 0;
            AssertFalse(zeroCount.IsInternallyValid(), "MaterialCount=0 must be invalid");

            // Non-positive ordered card id
            LlmSummonRequirement nonPos = CloneReq(ok);
            nonPos.OrderedMaterialCardIds = new List<int>() { CaveDragonInternalCardId, 0 };
            nonPos.MaterialSources = new List<LlmMaterialSource>()
            {
                new LlmMaterialSource() { Zone = 1, Index = 0, CardId = CaveDragonInternalCardId },
                new LlmMaterialSource() { Zone = 0, Index = 0, CardId = 0 },
            };
            AssertFalse(nonPos.IsInternallyValid(), "non-positive ordered card id must be invalid");

            // Source CardId mismatch vs ordered
            LlmSummonRequirement mismatch = CloneReq(ok);
            mismatch.MaterialSources = new List<LlmMaterialSource>()
            {
                new LlmMaterialSource() { Zone = 1, Index = 0, CardId = GirochinInternalCardId }, // wrong
                new LlmMaterialSource() { Zone = 0, Index = 0, CardId = GirochinInternalCardId },
            };
            AssertFalse(mismatch.IsInternallyValid(),
                "source CardId must exactly match corresponding ordered card id");

            // Source count != MaterialCount / ordered
            LlmSummonRequirement countMismatch = CloneReq(ok);
            countMismatch.MaterialSources = new List<LlmMaterialSource>()
            {
                new LlmMaterialSource() { Zone = 1, Index = 0, CardId = CaveDragonInternalCardId },
            };
            AssertFalse(countMismatch.IsInternallyValid(),
                "source count must equal MaterialCount/ordered count");

            // Duplicate source identities
            LlmSummonRequirement dup = CloneReq(ok);
            dup.MaterialSources = new List<LlmMaterialSource>()
            {
                new LlmMaterialSource() { Zone = 0, Index = 0, CardId = CaveDragonInternalCardId },
                new LlmMaterialSource() { Zone = 0, Index = 0, CardId = GirochinInternalCardId },
            };
            AssertFalse(dup.IsInternallyValid(), "duplicate source identities must be invalid");
        }

        static LlmSummonRequirement CloneReq(LlmSummonRequirement src)
        {
            LlmSummonRequirement r = new LlmSummonRequirement()
            {
                SummonFamily = src.SummonFamily,
                Rank = src.Rank,
                Level = src.Level,
                MaterialCount = src.MaterialCount,
                ExtraDeckTargetCardId = src.ExtraDeckTargetCardId,
                OrderedMaterialCardIds = new List<int>(src.OrderedMaterialCardIds),
            };
            List<LlmMaterialSource> srcs = new List<LlmMaterialSource>();
            foreach (LlmMaterialSource s in src.MaterialSources)
            {
                srcs.Add(new LlmMaterialSource()
                {
                    Zone = s.Zone,
                    Index = s.Index,
                    CardId = s.CardId,
                    IsFaceUp = s.IsFaceUp,
                });
            }
            r.MaterialSources = srcs;
            return r;
        }

        static void TemplateBudgetTelemetryReasonsAndBoundaryNodes(Type matcherType)
        {
            DecisionSnapshot snap = MakeXyzSetupSnapshot();
            LlmSelfResources self = MakeXyzReadySelf();

            // depth0: structural Xyz match blocked → template_depth_budget + PrunedNodes + incomplete
            LlmSearchLimits depth0 = LlmSearchLimits.CreateDefault();
            depth0.MaxStrategicDepth = 0;
            LlmSearchGraph g0 = ExpandWithLimits(matcherType, snap, self, depth0);
            AssertCoverageReasonContains(g0, "template_depth_budget",
                "depth0 template prune reason");
            AssertTrue(g0.Coverage.PrunedNodes >= 1,
                "depth0 PrunedNodes must increment for template depth prune");
            AssertEqual("budget_incomplete", g0.Status, "depth0 status budget_incomplete");
            // No spam for non-matching templates alone: synchro should not force a reason when
            // materials do not match — only structural matches. Xyz matches here.
            AssertFalse(CoverageReasonBlob(g0).IndexOf("template_spam", StringComparison.Ordinal) >= 0,
                "no spam sentinel");

            // MaxNodes hard cap: if template omitted due to nodes, record template_node_budget
            LlmSearchLimits nodes1 = LlmSearchLimits.CreateDefault();
            nodes1.MaxNodes = 1;
            nodes1.MaxStrategicDepth = 4;
            LlmSearchGraph gN = ExpandWithLimits(matcherType, snap, self, nodes1);
            if (FindXyzTemplateLine(gN) == null && gN.Coverage.ContinuationExpansionCount >= 1)
            {
                AssertCoverageReasonContains(gN, "template_node_budget",
                    "MaxNodes=1 structural Xyz omitted must record template_node_budget");
                AssertEqual("budget_incomplete", gN.Status,
                    "node-exhausted template omit must set budget_incomplete");
                AssertTrue(gN.Coverage.PrunedNodes >= 1, "node prune increments PrunedNodes");
            }

            // Beam: when template omitted by beam, record template_beam_width (not incomplete required)
            LlmSearchLimits beam1 = LlmSearchLimits.CreateDefault();
            beam1.BeamWidth = 1;
            beam1.MaxNodes = 96;
            beam1.MaxStrategicDepth = 4;
            LlmSearchGraph gB = ExpandWithLimits(matcherType, snap, self, beam1);
            if (FindXyzTemplateLine(gB) == null)
            {
                AssertCoverageReasonContains(gB, "template_beam_width",
                    "beam-omitted structural Xyz must record template_beam_width");
                AssertTrue(gB.Coverage.PrunedNodes >= 1, "beam prune increments PrunedNodes");
            }

            // Fake clock: template_time_budget
            LlmSearchFakeBudgetClock clock = new LlmSearchFakeBudgetClock();
            clock.AdvanceMilliseconds(10_000);
            LlmSearchLimits timed = LlmSearchLimits.CreateDefault();
            timed.MaxWallMs = 1;
            timed.Clock = clock;
            LlmSearchGraph gT = ExpandWithLimits(matcherType, snap, self, timed);
            AssertCoverageReasonContains(gT, "template_time_budget",
                "time-exhausted structural Xyz must record template_time_budget");
            AssertTrue(gT.Coverage.PrunedNodes >= 1, "time prune increments PrunedNodes");
            AssertEqual("budget_incomplete", gT.Status, "time prune budget_incomplete");

            // Happy path BoundaryNodes delta: Layer A Search baseline vs Expand with templates
            LlmSearchLimits def = LlmSearchLimits.CreateDefault();
            LlmSearchGraph baseGraph = LlmTacticalAffordanceGraph.Build(snap, self, def);
            baseGraph = LlmBoundedLineSearch.Search(baseGraph, def);
            int baseBoundary = baseGraph.Coverage != null ? baseGraph.Coverage.BoundaryNodes : 0;
            LlmSearchGraph withTmpl = ExpandWithLimits(matcherType, snap, self, def);
            int templateLines = CountTemplateLines(withTmpl);
            AssertTrue(templateLines >= 1, "happy path emits at least one template");
            AssertTrue(withTmpl.Coverage.BoundaryNodes >= baseBoundary + templateLines,
                "BoundaryNodes must increase by >= emitted templates "
                + "(base=" + baseBoundary + " after=" + withTmpl.Coverage.BoundaryNodes
                + " templates=" + templateLines + ")");

            // Non-matching snapshot: opponent-only must not invent template prune reasons
            DecisionSnapshot oppOnly = MakeBaseSnapshot();
            oppOnly.LegalActions.Add(MakeEndRoot(0));
            oppOnly.StrategicActionCount = 1;
            LlmSelfResources empty = MakeSelf(
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>());
            LlmSearchLimits depth0b = LlmSearchLimits.CreateDefault();
            depth0b.MaxStrategicDepth = 0;
            LlmSearchGraph gNoMatch = ExpandWithLimits(matcherType, oppOnly, empty, depth0b);
            AssertFalse(CoverageReasonBlob(gNoMatch).IndexOf("template_depth_budget", StringComparison.Ordinal) >= 0,
                "non-matching snapshot must not spam template_depth_budget");
        }

        static void AssertCoverageReasonContains(LlmSearchGraph graph, string token, string msg)
        {
            string blob = CoverageReasonBlob(graph);
            AssertTrue(blob.IndexOf(token, StringComparison.Ordinal) >= 0,
                msg + " (blob missing '" + token + "': " + blob + ")");
        }

        static string CoverageReasonBlob(LlmSearchGraph graph)
        {
            if (graph == null || graph.Coverage == null)
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder();
            AppendReasons(sb, graph.Coverage.PruningReasons);
            AppendReasons(sb, graph.Coverage.NonExpansionReasons);
            AppendReasons(sb, graph.Coverage.PerRootBudgetTelemetry);
            AppendReasons(sb, graph.Coverage.ExclusionReasons);
            return sb.ToString();
        }

        static void AppendReasons(StringBuilder sb, IList<string> list)
        {
            if (list == null)
            {
                return;
            }
            foreach (string s in list)
            {
                if (!string.IsNullOrEmpty(s))
                {
                    sb.Append('|').Append(s);
                }
            }
        }

        static void XyzAndSynchroRequireGroundedMetadata(Type matcherType)
        {
            DecisionSnapshot snap = MakeXyzSetupSnapshot();

            // Wrong material levels (not Level 4).
            LlmSelfResources wrongLvl = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 3, false, 0),
                    FieldCard(CaveDragonInternalCardId, CaveDragonName, 1, false, 3, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 4),
                });
            AssertTrue(FindXyzTemplateLine(ExpandExact(matcherType, snap, wrongLvl)) == null,
                "Xyz: wrong material Level must not emit template");

            // Missing Level metadata on materials.
            LlmSelfResources noLvl = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 0, false, 0),
                    FieldCard(CaveDragonInternalCardId, CaveDragonName, 1, false, 0, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 4),
                });
            AssertTrue(FindXyzTemplateLine(ExpandExact(matcherType, snap, noLvl)) == null,
                "Xyz: missing Level metadata must not emit template");

            // ED id-only (no xyz family / UsesRank / rank).
            LlmSelfResources idOnlyEd = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                    FieldCard(CaveDragonInternalCardId, CaveDragonName, 1, false, 4, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    LlmSelfResourceExtraDeckEntry.Create(
                        UtopiaInternalCardId, UtopiaName, "Effect", "main_deck_monster",
                        4, null, false, null, false, "Effect",
                        2500, 2000, null, 1),
                });
            AssertTrue(FindXyzTemplateLine(ExpandExact(matcherType, snap, idOnlyEd)) == null,
                "Xyz: ED id-only without xyz/rank4/UsesRank must not emit template");

            // Wrong rank on ED.
            LlmSelfResources wrongRank = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                    FieldCard(CaveDragonInternalCardId, CaveDragonName, 1, false, 4, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 5),
                });
            AssertTrue(FindXyzTemplateLine(ExpandExact(matcherType, snap, wrongRank)) == null,
                "Xyz: wrong ED rank must not emit template");

            // Synchro: wrong non-tuner level (sum != 8).
            DecisionSnapshot synSnap = MakeBaseSnapshot();
            synSnap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = ControlledPlayer,
                Position = 13,
                Index = 0,
                CardId = SynthNonTunerLevel5Id,
                Card = new LlmCardMetadata()
                {
                    CardId = SynthNonTunerLevel5Id,
                    Name = "SYNTH_L4_NONTUNER",
                    Level = 4,
                    Kind = "Effect",
                    Frame = "Effect",
                    SummonFamily = "main_deck_monster",
                    IsTuner = false,
                },
                ActionLabel = "Summon SYNTH_L4_NONTUNER",
            });
            synSnap.LegalActions.Add(MakeEndRoot(1));
            synSnap.StrategicActionCount = 2;
            LlmSelfResources synWrongLvl = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(JunkSynchronInternalCardId, JunkSynchronName, 0, true, 3, true, 0),
                },
                hand: new List<LlmSelfResourceCard>()
                {
                    FieldCard(SynthNonTunerLevel5Id, "SYNTH_L4_NONTUNER", 13, true, 4, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, 8),
                });
            AssertTrue(FindSynchroTemplateLine(ExpandExact(matcherType, synSnap, synWrongLvl)) == null,
                "Synchro: wrong tuner/non-tuner level sum must not emit template");

            // Synchro ED id-only without synchro/level8.
            DecisionSnapshot synOkSnap = MakeBaseSnapshot();
            synOkSnap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = ControlledPlayer,
                Position = 13,
                Index = 0,
                CardId = SynthNonTunerLevel5Id,
                Card = new LlmCardMetadata()
                {
                    CardId = SynthNonTunerLevel5Id,
                    Name = "SYNTH_L5_NONTUNER",
                    Level = 5,
                    Kind = "Effect",
                    Frame = "Effect",
                    SummonFamily = "main_deck_monster",
                    IsTuner = false,
                },
                ActionLabel = "Summon SYNTH_L5_NONTUNER",
            });
            synOkSnap.LegalActions.Add(MakeEndRoot(1));
            synOkSnap.StrategicActionCount = 2;
            LlmSelfResources synIdOnlyEd = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(JunkSynchronInternalCardId, JunkSynchronName, 0, true, 3, true, 0),
                },
                hand: new List<LlmSelfResourceCard>()
                {
                    FieldCard(SynthNonTunerLevel5Id, "SYNTH_L5_NONTUNER", 13, true, 5, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    LlmSelfResourceExtraDeckEntry.Create(
                        StardustDragonInternalCardId, StardustName, "Effect", "main_deck_monster",
                        null, null, false, null, false, "Effect",
                        2500, 2000, null, 1),
                });
            AssertTrue(FindSynchroTemplateLine(ExpandExact(matcherType, synOkSnap, synIdOnlyEd)) == null,
                "Synchro: ED id-only without synchro/level8 must not emit template (no ID-only fallback)");
        }

        static void FromSelectedLineAndTrackerRejectInvalidLines(
            Type memoryType, LlmSearchGraph preRootGraph)
        {
            MethodInfo fromSelected = RequireExactPublicStaticMethod(
                memoryType, "FromSelectedLine", typeof(DecisionSnapshot), typeof(LlmSearchLine));
            DecisionSnapshot snap = MakeXyzSetupSnapshot();
            snap.Turn = 5;
            snap.RunEffectSeq = 77UL;
            LlmSearchLine good = FindXyzTemplateLine(preRootGraph);
            AssertNotNull(good, "valid template line for FromSelectedLine controls");

            // Empty TemplateId
            LlmSearchLine noTid = CloneLine(good);
            noTid.TemplateId = string.Empty;
            AssertTrue(fromSelected.Invoke(null, new object[] { snap, noTid }) == null,
                "FromSelectedLine: empty TemplateId => null");

            // Missing SummonRequirement
            LlmSearchLine noReq = CloneLine(good);
            noReq.SummonRequirement = null;
            AssertTrue(fromSelected.Invoke(null, new object[] { snap, noReq }) == null,
                "FromSelectedLine: missing SummonRequirement => null");

            // Invalid SummonRequirement (bad target / count)
            LlmSearchLine badReq = CloneLine(good);
            badReq.SummonRequirement = new LlmSummonRequirement()
            {
                SummonFamily = "xyz",
                Rank = 4,
                MaterialCount = 2,
                ExtraDeckTargetCardId = 0,
                OrderedMaterialCardIds = new List<int>() { 4103, 5136 },
                MaterialSources = new List<LlmMaterialSource>()
                {
                    new LlmMaterialSource() { Zone = 1, Index = 0, CardId = 4103, IsFaceUp = false },
                    new LlmMaterialSource() { Zone = 0, Index = 0, CardId = 5136, IsFaceUp = true },
                },
            };
            AssertTrue(fromSelected.Invoke(null, new object[] { snap, badReq }) == null,
                "FromSelectedLine: SummonRequirement with ExtraDeckTargetCardId=0 => null");

            // Duplicate material source identities
            LlmSearchLine dupSrc = CloneLine(good);
            dupSrc.SummonRequirement = new LlmSummonRequirement()
            {
                SummonFamily = "xyz",
                Rank = 4,
                MaterialCount = 2,
                ExtraDeckTargetCardId = UtopiaInternalCardId,
                OrderedMaterialCardIds = new List<int>() { 4103, 5136 },
                MaterialSources = new List<LlmMaterialSource>()
                {
                    new LlmMaterialSource() { Zone = 0, Index = 0, CardId = 4103, IsFaceUp = false },
                    new LlmMaterialSource() { Zone = 0, Index = 0, CardId = 5136, IsFaceUp = true },
                },
            };
            AssertTrue(fromSelected.Invoke(null, new object[] { snap, dupSrc }) == null,
                "FromSelectedLine: duplicate material source identities => null");

            // Tracker 4-arg: reject selected line for another root
            MethodInfo record4 = typeof(LlmTurnMemoryTracker).GetMethod(
                "RecordCommittedAction",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[]
                {
                    typeof(DecisionSnapshot),
                    typeof(LlmBrokerDecisionResponse),
                    typeof(LegalAction),
                    typeof(LlmSearchLine),
                },
                null);
            AssertNotNull(record4, "4-arg RecordCommittedAction");
            PropertyInfo intentProp = typeof(LlmTurnMemoryState).GetProperty(
                "IntendedFollowup", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo createWithGraph = typeof(LlmTurnMemoryTracker).GetMethod(
                "CreateSnapshot",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(DecisionSnapshot), typeof(LlmSearchGraph) },
                null);

            LegalAction setupAction = snap.LegalActions[0]; // root 0
            LlmBrokerDecisionResponse response = new LlmBrokerDecisionResponse()
            {
                ActionId = 0,
                Reason = "test",
                Plan = "test",
            };
            LlmSearchLine otherRoot = CloneLine(good);
            otherRoot.RootActionId = 1; // battle root — not committed action 0
            LlmTurnMemoryTracker trWrongRoot = new LlmTurnMemoryTracker(8);
            record4.Invoke(trWrongRoot, new object[] { snap, response, setupAction, otherRoot });
            LlmTurnMemoryState memWrong = (LlmTurnMemoryState)createWithGraph.Invoke(
                trWrongRoot, new object[] { snap, preRootGraph });
            AssertTrue(intentProp.GetValue(memWrong, null) == null,
                "tracker 4-arg must reject selected line for another root");

            // Tracker: root-shell / non-template
            LlmSearchLine shell = RootShell(0);
            LlmTurnMemoryTracker trShell = new LlmTurnMemoryTracker(8);
            record4.Invoke(trShell, new object[] { snap, response, setupAction, shell });
            LlmTurnMemoryState memShell = (LlmTurnMemoryState)createWithGraph.Invoke(
                trShell, new object[] { snap, preRootGraph });
            AssertTrue(intentProp.GetValue(memShell, null) == null,
                "tracker 4-arg must reject root-shell / non-template selected line");

            // Tracker: matching root + template still accepted
            LlmTurnMemoryTracker trOk = new LlmTurnMemoryTracker(8);
            record4.Invoke(trOk, new object[] { snap, response, setupAction, good });
            LlmTurnMemoryState memOk = (LlmTurnMemoryState)createWithGraph.Invoke(
                trOk, new object[] { snap, preRootGraph });
            AssertNotNull(intentProp.GetValue(memOk, null),
                "tracker 4-arg must accept matching-root template selected line");
        }

        static void EvaluatorMultisetMaterialGrounding(
            Type memoryType, Type evalType, LlmSearchGraph preRootGraph)
        {
            MethodInfo fromSelected = RequireExactPublicStaticMethod(
                memoryType, "FromSelectedLine", typeof(DecisionSnapshot), typeof(LlmSearchLine));
            MethodInfo evaluate = RequireExactPublicStaticMethod(
                evalType, "Evaluate", memoryType, typeof(DecisionSnapshot), typeof(LlmSearchGraph));

            DecisionSnapshot pre = MakeXyzSetupSnapshot();
            pre.Turn = 3;
            pre.RunEffectSeq = 42UL;
            LlmSearchLine selected = FindXyzTemplateLine(preRootGraph);
            AssertNotNull(selected, "template for multiset fixture");
            object memory = fromSelected.Invoke(null, new object[] { pre, selected });
            AssertNotNull(memory, "memory for multiset");

            // Setup movement/flip allowed: materials moved zones between pre- and post-root.
            DecisionSnapshot postMoved = MakePostRootMaterialsGroundedSnapshot(turn: 3, seq: 50UL);
            postMoved.LegalActions.Add(MakeUtopiaSummonSp(PostRootUtopiaSummonSpActionId));
            postMoved.StrategicActionCount = 1;
            LlmSelfResources movedSelf = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    // Zones flipped vs pre-root (0/1 -> 2/3) after setup movement/flip.
                    FieldCard(GirochinInternalCardId, GirochinName, 2, true, 4, false, 0),
                    FieldCard(CaveDragonInternalCardId, CaveDragonName, 3, true, 4, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 4),
                });
            LlmSearchGraph gMoved = MakePostRootGraphWithUtopiaLegal(
                postMoved, movedSelf, PostRootUtopiaSummonSpActionId);
            object stMoved = evaluate.Invoke(null, new object[] { memory, postMoved, gMoved });
            AssertStatus(stMoved, "available");

            // Duplicate-card-id count fixture: expect two copies of 5136; only one on field.
            LlmIntendedFollowupMemory dupMem = new LlmIntendedFollowupMemory()
            {
                TemplateId = ExpectedXyzTemplateId,
                LineId = "dup_count",
                RootActionId = 0,
                OriginRunEffectSeq = 42UL,
                OriginTurn = 3,
                ExpectedSummonFamily = "xyz",
                ExpectedTargetCardId = UtopiaInternalCardId,
                ExpectedMaterialCardIds = new List<int>()
                {
                    GirochinInternalCardId, GirochinInternalCardId,
                },
                ExpectedMaterialSources = new List<LlmMaterialSource>()
                {
                    new LlmMaterialSource() { Zone = 0, Index = 0, CardId = GirochinInternalCardId, IsFaceUp = true },
                    new LlmMaterialSource() { Zone = 1, Index = 0, CardId = GirochinInternalCardId, IsFaceUp = true },
                },
            };
            DecisionSnapshot postDup = MakePostRootMaterialsGroundedSnapshot(turn: 3, seq: 51UL);
            postDup.LegalActions.Add(MakeEndRoot(0));
            postDup.StrategicActionCount = 1;
            LlmSelfResources oneCopy = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 4),
                });
            LlmSearchGraph gOne = new LlmSearchGraph();
            gOne.SourceSnapshot = postDup;
            gOne.SourceSelfResources = oneCopy;
            gOne.Lines.Add(RootShell(0));
            object stOne = evaluate.Invoke(null, new object[] { dupMem, postDup, gOne });
            AssertStatus(stOne, "invalidated");
            AssertFalse(StatusName(stOne) == "available" || StatusName(stOne) == "not_yet_available",
                "one copy of card id must not satisfy two expected material slots");

            // Two distinct copies of same card id: multiset satisfied => not_yet (no Utopia legal).
            LlmSelfResources twoCopies = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                    FieldCard(GirochinInternalCardId, GirochinName, 1, true, 4, false, 1),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 4),
                });
            LlmSearchGraph gTwo = new LlmSearchGraph();
            gTwo.SourceSnapshot = postDup;
            gTwo.SourceSelfResources = twoCopies;
            gTwo.Lines.Add(RootShell(0));
            object stTwo = evaluate.Invoke(null, new object[] { dupMem, postDup, gTwo });
            AssertStatus(stTwo, "not_yet_available");
        }

        static void HypothesisAdmissionStripsScoreFeaturesAndCapsScore(
            Type admitType, LlmSearchGraph groundedGraph)
        {
            MethodInfo tryAdmit = RequireExactPublicStaticMethod(
                admitType,
                "TryAdmit",
                typeof(DecisionSnapshot),
                typeof(LlmSearchGraph),
                typeof(LlmSearchLine),
                typeof(LlmSearchLine).MakeByRefType(),
                typeof(string).MakeByRefType());
            DecisionSnapshot snap = MakeXyzSetupSnapshot();
            LlmSearchLine grounded = FindXyzTemplateLine(groundedGraph);
            AssertNotNull(grounded, "grounded line for hypothesis score strip");
            // Grounded authority = max non-hypothesis Score for the same root (shells + templates).
            int maxGroundedOnRoot = 0;
            foreach (LlmSearchLine line in groundedGraph.Lines)
            {
                if (line == null || line.RootActionId != 0)
                {
                    continue;
                }
                if (string.Equals(line.Provenance, ProvenanceModelHypothesis, StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.Score > maxGroundedOnRoot)
                {
                    maxGroundedOnRoot = line.Score;
                }
            }
            AssertTrue(maxGroundedOnRoot >= 0, "grounded max for root 0");

            LlmSearchLine hyp = MakeHypothesisLine(rootId: 0, uncertain: true, leakLabel: null);
            hyp.Score = maxGroundedOnRoot + 999;
            hyp.ScoreFeatures["template_setup_unlock"] = 99999;
            hyp.ScoreFeatures["unknown_candidate_feature_xyz"] = 88888;
            hyp.ScoreFeatures["bogus_authority"] = 77777;

            object[] args = new object[] { snap, groundedGraph, hyp, null, null };
            bool ok = Convert.ToBoolean(tryAdmit.Invoke(null, args));
            AssertTrue(ok, "hypothesis with huge ScoreFeatures must still admit when otherwise valid");
            LlmSearchLine admitted = args[3] as LlmSearchLine;
            AssertNotNull(admitted, "admitted hypothesis");
            AssertTrue(admitted.Score <= maxGroundedOnRoot,
                "admitted Score must be capped to grounded authority max="
                + maxGroundedOnRoot + " (got " + admitted.Score + ")");
            AssertTrue(admitted.Score < hyp.Score || maxGroundedOnRoot >= hyp.Score,
                "huge candidate Score must not survive uncapped");
            AssertTrue(
                admitted.ScoreFeatures == null || admitted.ScoreFeatures.Count == 0,
                "admission must strip all candidate ScoreFeatures (unknown keys cannot retain huge values)");
            AssertFalse(
                admitted.ScoreFeatures != null
                && admitted.ScoreFeatures.ContainsKey("unknown_candidate_feature_xyz"),
                "unknown candidate feature key must not be retained");
        }

        static void ProjectionUsesSingleLowercaseStructuredKeys(LlmSearchGraph graph)
        {
            Dictionary<string, object> proj = LlmSearchProjection.Project(graph);
            string json = MiniJSON.Json.Serialize(proj) ?? string.Empty;
            AssertTrue(json.IndexOf(ExpectedXyzTemplateId, StringComparison.Ordinal) >= 0,
                "projection includes template id value");
            // Prefer single lowercase keys (no duplicate PascalCase + snake_case pairs).
            bool hasLowerTid = json.IndexOf("\"template_id\"", StringComparison.Ordinal) >= 0;
            bool hasPascalTid = json.IndexOf("\"TemplateId\"", StringComparison.Ordinal) >= 0;
            AssertTrue(hasLowerTid, "projection must use lowercase template_id key");
            AssertFalse(hasPascalTid && hasLowerTid,
                "projection must not emit both TemplateId and template_id (16 KiB budget)");

            bool hasLowerReq = json.IndexOf("\"summon_requirement\"", StringComparison.Ordinal) >= 0;
            bool hasPascalReq = json.IndexOf("\"SummonRequirement\"", StringComparison.Ordinal) >= 0;
            AssertTrue(hasLowerReq, "projection must use lowercase summon_requirement key");
            AssertFalse(hasPascalReq && hasLowerReq,
                "projection must not emit both SummonRequirement and summon_requirement");
        }

        static LlmSearchGraph ExpandWithLimits(
            Type matcherType, DecisionSnapshot snap, LlmSelfResources self, LlmSearchLimits limits)
        {
            MethodInfo expand = RequireExactPublicStaticMethod(
                matcherType,
                "ExpandToGraph",
                typeof(DecisionSnapshot),
                typeof(LlmSelfResources),
                typeof(LlmSearchLimits));
            object result = expand.Invoke(null, new object[] { snap, self, limits });
            LlmSearchGraph graph = result as LlmSearchGraph;
            AssertNotNull(graph, "ExpandWithLimits result");
            return graph;
        }

        static void AssertRootShellsComplete(LlmSearchGraph graph, params int[] rootIds)
        {
            HashSet<int> shells = new HashSet<int>();
            if (graph != null && graph.Lines != null)
            {
                foreach (LlmSearchLine line in graph.Lines)
                {
                    if (line != null && line.IsRootShell)
                    {
                        shells.Add(line.RootActionId);
                    }
                }
            }
            foreach (int id in rootIds)
            {
                AssertTrue(shells.Contains(id),
                    "root shell for action " + id + " must remain present under budget");
            }
        }

        static int CountTemplateLines(LlmSearchGraph graph)
        {
            int n = 0;
            if (graph == null || graph.Lines == null)
            {
                return 0;
            }
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line != null && !string.IsNullOrEmpty(line.TemplateId))
                {
                    n++;
                }
            }
            return n;
        }

        static int CountNonRootShellLines(LlmSearchGraph graph)
        {
            int n = 0;
            if (graph == null || graph.Lines == null)
            {
                return 0;
            }
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line != null && !line.IsRootShell)
                {
                    n++;
                }
            }
            return n;
        }

        static Dictionary<int, int> CountContinuationsPerRoot(LlmSearchGraph graph)
        {
            Dictionary<int, int> d = new Dictionary<int, int>();
            if (graph == null || graph.Lines == null)
            {
                return d;
            }
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line == null || line.IsRootShell)
                {
                    continue;
                }
                int c;
                d.TryGetValue(line.RootActionId, out c);
                d[line.RootActionId] = c + 1;
            }
            return d;
        }

        static LlmSearchLine CloneLine(LlmSearchLine src)
        {
            LlmSearchLine copy = new LlmSearchLine()
            {
                LineId = src.LineId,
                RootActionId = src.RootActionId,
                Provenance = src.Provenance,
                CommitEligible = src.CommitEligible,
                StrategicDepth = src.StrategicDepth,
                Score = src.Score,
                Fingerprint = src.Fingerprint,
                IsRootShell = src.IsRootShell,
                Boundary = src.Boundary,
                TemplateId = src.TemplateId,
                SummonRequirement = src.SummonRequirement,
            };
            if (src.Steps != null)
            {
                foreach (LlmSearchStep s in src.Steps)
                {
                    if (s == null)
                    {
                        continue;
                    }
                    copy.Steps.Add(new LlmSearchStep()
                    {
                        Label = s.Label,
                        CurrentLegal = s.CurrentLegal,
                        CommitEligible = s.CommitEligible,
                        Provenance = s.Provenance,
                        Fingerprint = s.Fingerprint,
                    });
                }
            }
            if (src.Unlocks != null)
            {
                foreach (string u in src.Unlocks)
                {
                    copy.Unlocks.Add(u);
                }
            }
            if (src.Uncertainty != null)
            {
                foreach (string u in src.Uncertainty)
                {
                    copy.Uncertainty.Add(u);
                }
            }
            if (src.ScoreFeatures != null)
            {
                foreach (KeyValuePair<string, int> kv in src.ScoreFeatures)
                {
                    copy.ScoreFeatures[kv.Key] = kv.Value;
                }
            }
            if (src.SummonRequirement != null)
            {
                LlmSummonRequirement r = src.SummonRequirement;
                LlmSummonRequirement nr = new LlmSummonRequirement()
                {
                    SummonFamily = r.SummonFamily,
                    Rank = r.Rank,
                    Level = r.Level,
                    MaterialCount = r.MaterialCount,
                    ExtraDeckTargetCardId = r.ExtraDeckTargetCardId,
                };
                if (r.OrderedMaterialCardIds != null)
                {
                    nr.OrderedMaterialCardIds = new List<int>(r.OrderedMaterialCardIds);
                }
                if (r.MaterialSources != null)
                {
                    List<LlmMaterialSource> srcs = new List<LlmMaterialSource>();
                    foreach (LlmMaterialSource s in r.MaterialSources)
                    {
                        if (s == null)
                        {
                            continue;
                        }
                        srcs.Add(new LlmMaterialSource()
                        {
                            Zone = s.Zone,
                            Index = s.Index,
                            CardId = s.CardId,
                            IsFaceUp = s.IsFaceUp,
                        });
                    }
                    nr.MaterialSources = srcs;
                }
                copy.SummonRequirement = nr;
            }
            return copy;
        }

        /// <summary>
        /// Integration: LlmPlanningSearchAudit.TryBuild(default factory) must emit exact Xyz template
        /// line and preserve paired-hidden projection invariance.
        /// </summary>
        static void PlanningSearchAuditDefaultFactoryEmitsXyzTemplateAndHiddenInvariance()
        {
            DecisionSnapshot a = MakeXyzSetupSnapshot();
            DecisionSnapshot b = MakeXyzSetupSnapshot();
            for (int i = 0; i < SentinelOppIds.Length; i++)
            {
                PlantOpponentHidden(a, SentinelOppIds[i], SentinelOppNames[i] + "_AUDIT_A");
                PlantOpponentHidden(b, SentinelOppIds[i] + 5000, SentinelOppNames[i] + "_AUDIT_B");
            }
            LlmSelfResources self = MakeXyzReadySelf();
            LlmSearchLimits limits = LlmSearchLimits.CreateDefault();

            LlmPlanningSearchAuditResult ra = LlmPlanningSearchAudit.TryBuild(a, self, limits);
            LlmPlanningSearchAuditResult rb = LlmPlanningSearchAudit.TryBuild(b, self, limits);
            AssertTrue(ra != null && ra.Success && ra.Graph != null,
                "Slice 6B RED/GREEN: TryBuild(default) must succeed with graph "
                + "(requires LlmComboTemplateMatcher as default factory path)");
            AssertTrue(rb != null && rb.Success && rb.Graph != null, "TryBuild paired-B success");

            LlmSearchLine xyz = FindXyzTemplateLine(ra.Graph);
            AssertNotNull(xyz,
                "TryBuild(default) graph must include exact Xyz template line "
                + ExpectedXyzTemplateId);
            AssertEqual(ExpectedXyzTemplateId, RequireStringProp(xyz, "TemplateId"),
                "TryBuild Xyz TemplateId");

            string pa = MiniJSON.Json.Serialize(ra.Projection) ?? string.Empty;
            string pb = MiniJSON.Json.Serialize(rb.Projection) ?? string.Empty;
            AssertEqual(pa, pb,
                "TryBuild(default) paired-hidden projections must be byte-identical");
            foreach (string n in SentinelOppNames)
            {
                AssertTrue(pa.IndexOf(n, StringComparison.Ordinal) < 0,
                    "TryBuild projection must not leak opponent sentinel " + n);
            }
        }

        // =====================================================================
        // Catalog identities
        // =====================================================================
        static void ComboTemplateCatalogExactIdentities(IEnumerable templates)
        {
            object xyz = null;
            object synchro = null;
            foreach (object t in templates)
            {
                if (t == null)
                {
                    continue;
                }
                string id = RequireStringProp(t, "TemplateId");
                if (id == ExpectedXyzTemplateId)
                {
                    xyz = t;
                }
                if (id == ExpectedSynchroTemplateId)
                {
                    synchro = t;
                }
            }
            AssertNotNull(xyz,
                "catalog.Templates must include exact TemplateId '" + ExpectedXyzTemplateId + "'");
            AssertNotNull(synchro,
                "catalog.Templates must include exact TemplateId '" + ExpectedSynchroTemplateId + "'");

            // Structured Xyz template fields (not free-form labels).
            AssertEqual(UtopiaInternalCardId, Convert.ToInt32(RequireProp(xyz, "ExtraDeckTargetCardId")),
                "Xyz template ExtraDeckTargetCardId == 9575");
            AssertEqual("xyz", NormalizeFamily(RequireStringProp(xyz, "SummonFamily")),
                "Xyz template SummonFamily");
            object mats = RequireProp(xyz, "MaterialCardIds");
            AssertTrue(IsNonStringEnumerable(mats), "MaterialCardIds must be non-string IEnumerable");
            List<int> matIds = ToIntList(mats);
            AssertTrue(matIds.Contains(GirochinInternalCardId) && matIds.Contains(CaveDragonInternalCardId),
                "Xyz template MaterialCardIds must include 5136 and 4103");
        }

        // =====================================================================
        // ExpandToGraph — exact signature
        // =====================================================================
        static LlmSearchGraph ExpandExact(Type matcherType, DecisionSnapshot snap, LlmSelfResources self)
        {
            MethodInfo expand = RequireExactPublicStaticMethod(
                matcherType,
                "ExpandToGraph",
                typeof(DecisionSnapshot),
                typeof(LlmSelfResources),
                typeof(LlmSearchLimits));
            AssertTrue(typeof(LlmSearchGraph).IsAssignableFrom(expand.ReturnType),
                "ExpandToGraph must return LlmSearchGraph");
            LlmSearchLimits limits = LlmSearchLimits.CreateDefault();
            object result = expand.Invoke(null, new object[] { snap, self, limits });
            LlmSearchGraph graph = result as LlmSearchGraph;
            AssertNotNull(graph, "ExpandToGraph result");
            return graph;
        }

        static void TemplateExpandToGraphRootCompletenessAndStructuredRequirements(LlmSearchGraph graph)
        {
            AssertNotNull(graph.Lines, "graph.Lines");
            HashSet<int> roots = new HashSet<int>();
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line != null)
                {
                    roots.Add(line.RootActionId);
                }
            }
            AssertTrue(roots.Contains(0) && roots.Contains(1) && roots.Contains(2),
                "ExpandToGraph must retain root completeness for all three strategic legal actions");

            LlmSearchLine tmpl = FindXyzTemplateLine(graph);
            AssertNotNull(tmpl,
                "ExpandToGraph must emit Xyz template continuation for flip-Cave root "
                + "(TemplateId=" + ExpectedXyzTemplateId + ")");
            AssertEqual(ProvenanceRulesInferred, tmpl.Provenance, "template line provenance");
            AssertEqual(ExpectedXyzTemplateId, RequireStringProp(tmpl, "TemplateId"),
                "emitted line structured TemplateId");

            object req = RequireProp(tmpl, "SummonRequirement");
            AssertEqual("xyz", NormalizeFamily(RequireStringProp(req, "SummonFamily")),
                "SummonRequirement.SummonFamily");
            AssertEqual(4, Convert.ToInt32(RequireProp(req, "Rank")), "SummonRequirement.Rank");
            AssertEqual(2, Convert.ToInt32(RequireProp(req, "MaterialCount")),
                "SummonRequirement.MaterialCount");
            AssertEqual(UtopiaInternalCardId, Convert.ToInt32(RequireProp(req, "ExtraDeckTargetCardId")),
                "SummonRequirement.ExtraDeckTargetCardId");
            List<int> orderedMats = ToIntList(RequireProp(req, "OrderedMaterialCardIds"));
            AssertEqual(2, orderedMats.Count, "ordered material count");
            // Exact ordered IDs as required by review (4103, 5136).
            AssertEqual(CaveDragonInternalCardId, orderedMats[0], "OrderedMaterialCardIds[0]=4103");
            AssertEqual(GirochinInternalCardId, orderedMats[1], "OrderedMaterialCardIds[1]=5136");

            IEnumerable sources = RequireExactEnumerableProperty(req, "MaterialSources");
            List<object> sourceList = sources.Cast<object>().Where(x => x != null).ToList();
            AssertEqual(2, sourceList.Count, "MaterialSources count");
            AssertDistinctSourceIdentities(sourceList);

            AssertTrue(tmpl.Steps != null && tmpl.Steps.Count >= 2, "root + future step(s)");
            AssertTrue(tmpl.Steps[0].CurrentLegal && tmpl.Steps[0].CommitEligible,
                "root step current legal + commit eligible");
            for (int i = 1; i < tmpl.Steps.Count; i++)
            {
                AssertFalse(tmpl.Steps[i].CurrentLegal, "future step CurrentLegal=false");
                AssertFalse(tmpl.Steps[i].CommitEligible, "future step CommitEligible=false");
                AssertEqual(ProvenanceRulesInferred, tmpl.Steps[i].Provenance, "future provenance");
            }
            AssertTrue(!string.IsNullOrEmpty(tmpl.Fingerprint) || !string.IsNullOrEmpty(tmpl.LineId),
                "stable Fingerprint/LineId on template line");

            // Projection must carry structured template fields (not only labels).
            Dictionary<string, object> proj = LlmSearchProjection.Project(graph);
            string json = MiniJSON.Json.Serialize(proj) ?? string.Empty;
            AssertTrue(json.IndexOf(ExpectedXyzTemplateId, StringComparison.Ordinal) >= 0,
                "projection must include TemplateId string");
            AssertTrue(
                json.IndexOf("SummonRequirement", StringComparison.OrdinalIgnoreCase) >= 0
                || json.IndexOf("summon_requirement", StringComparison.OrdinalIgnoreCase) >= 0
                || json.IndexOf("ordered_material", StringComparison.OrdinalIgnoreCase) >= 0
                || json.IndexOf("ExtraDeckTargetCardId", StringComparison.Ordinal) >= 0
                || json.IndexOf("extra_deck_target", StringComparison.OrdinalIgnoreCase) >= 0,
                "projection must include structured SummonRequirement / material / ED target fields");
            AssertTrue(Encoding.UTF8.GetByteCount(json) <= LlmSearchLimits.DefaultMaxSerializedBytes,
                "projection within default byte budget");
        }

        // =====================================================================
        // Malformed / duplicate-source / absent ED controls
        // =====================================================================
        static void MalformedDuplicateSourceAndAbsentEdControls(Type matcherType)
        {
            DecisionSnapshot snap = MakeXyzSetupSnapshot();

            // (a) Missing self-resources
            LlmSearchGraph gNull = ExpandExact(matcherType, snap, null);
            AssertTrue(FindXyzTemplateLine(gNull) == null,
                "no template line when LlmSelfResources is null");

            // (b) Absent ED target
            LlmSelfResources noEd = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                    FieldCard(CaveDragonInternalCardId, CaveDragonName, 1, false, 4, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>());
            AssertTrue(FindXyzTemplateLine(ExpandExact(matcherType, snap, noEd)) == null,
                "no template line when Extra Deck target absent");

            // (c) Root location mismatch — legal root points at wrong zone/index vs Cave material
            DecisionSnapshot badRootLoc = MakeBaseSnapshot();
            badRootLoc.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Reverse,
                Player = ControlledPlayer,
                Position = 7, // not where Cave lives
                Index = 3,
                CardId = CaveDragonInternalCardId,
                Card = Meta(CaveDragonInternalCardId, CaveDragonName),
                ActionLabel = "Flip Summon " + CaveDragonName,
            });
            badRootLoc.LegalActions.Add(MakeBattleRoot(1));
            badRootLoc.LegalActions.Add(MakeEndRoot(2));
            badRootLoc.StrategicActionCount = 3;
            AssertTrue(
                FindXyzTemplateLine(ExpandExact(matcherType, badRootLoc, MakeXyzReadySelf())) == null,
                "no template line when root location mismatches material source");

            // (d) Duplicate source identities — two materials claim identical zone/index
            // Force malformed self where both materials share zone=0 index=0 (invalid distinct sources).
            LlmSelfResources dupSrc = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                    FieldCard(CaveDragonInternalCardId, CaveDragonName, 0, false, 4, false, 0), // same zone+index
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 4),
                });
            // Legal root still references position 0 index 0 — both claim that slot.
            DecisionSnapshot dupSnap = MakeBaseSnapshot();
            dupSnap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Reverse,
                Player = ControlledPlayer,
                Position = 0,
                Index = 0,
                CardId = CaveDragonInternalCardId,
                Card = Meta(CaveDragonInternalCardId, CaveDragonName),
                ActionLabel = "Flip Summon " + CaveDragonName,
            });
            dupSnap.LegalActions.Add(MakeBattleRoot(1));
            dupSnap.LegalActions.Add(MakeEndRoot(2));
            dupSnap.StrategicActionCount = 3;
            AssertTrue(FindXyzTemplateLine(ExpandExact(matcherType, dupSnap, dupSrc)) == null,
                "no template line when two materials claim identical zone/index");

            // (e) Non-enumerable / missing material metadata on graph path is covered by absent field cards:
            LlmSelfResources onlyOne = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 4),
                });
            AssertTrue(FindXyzTemplateLine(ExpandExact(matcherType, snap, onlyOne)) == null,
                "no template line with missing second material (malformed material set)");
        }

        static void HiddenOpponentOnlyNeverMatches(Type matcherType)
        {
            DecisionSnapshot oppOnly = MakeBaseSnapshot();
            oppOnly.LegalActions.Add(MakeEndRoot(0));
            oppOnly.StrategicActionCount = 1;
            for (int i = 0; i < SentinelOppIds.Length; i++)
            {
                PlantOpponentHidden(oppOnly, SentinelOppIds[i], SentinelOppNames[i]);
            }
            LlmSelfResources empty = MakeSelf(
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>());
            LlmSearchGraph g = ExpandExact(matcherType, oppOnly, empty);
            AssertTrue(FindXyzTemplateLine(g) == null,
                "hidden-opponent-only facts must never produce a template line");
            string proj = MiniJSON.Json.Serialize(LlmSearchProjection.Project(g)) ?? string.Empty;
            foreach (string name in SentinelOppNames)
            {
                AssertTrue(proj.IndexOf(name, StringComparison.Ordinal) < 0,
                    "projection must not leak opponent sentinel name " + name);
            }
            foreach (int id in SentinelOppIds)
            {
                AssertTrue(proj.IndexOf(id.ToString(), StringComparison.Ordinal) < 0,
                    "projection must not leak opponent sentinel id " + id);
            }
        }

        // =====================================================================
        // Hypothesis admission — exact signature
        // =====================================================================
        static void ModelHypothesisAdmissionExactContract(Type admitType, LlmSearchGraph groundedGraph)
        {
            MethodInfo tryAdmit = RequireExactPublicStaticMethod(
                admitType,
                "TryAdmit",
                typeof(DecisionSnapshot),
                typeof(LlmSearchGraph),
                typeof(LlmSearchLine),
                typeof(LlmSearchLine).MakeByRefType(),
                typeof(string).MakeByRefType());
            AssertEqual(typeof(bool), tryAdmit.ReturnType, "TryAdmit returns bool");

            DecisionSnapshot snap = MakeXyzSetupSnapshot();
            LlmSearchLine grounded = FindXyzTemplateLine(groundedGraph);
            AssertNotNull(grounded, "grounded template line required for score-authority check");
            int groundedScore = grounded.Score;
            Dictionary<string, int> groundedFeatures = CloneFeatures(grounded.ScoreFeatures);

            // Valid hypothesis
            LlmSearchLine good = MakeHypothesisLine(rootId: 0, uncertain: true, leakLabel: null);
            object[] goodArgs = new object[] { snap, groundedGraph, good, null, null };
            bool ok = Convert.ToBoolean(tryAdmit.Invoke(null, goodArgs));
            AssertTrue(ok, "valid model_hypothesis must TryAdmit=true");
            LlmSearchLine admitted = goodArgs[3] as LlmSearchLine;
            AssertNotNull(admitted, "out admitted line");
            AssertEqual(ProvenanceModelHypothesis, admitted.Provenance, "admitted provenance");
            AssertTrue(admitted.Uncertainty != null && admitted.Uncertainty.Count > 0,
                "admitted uncertainty non-empty");
            for (int i = 1; i < admitted.Steps.Count; i++)
            {
                AssertFalse(admitted.Steps[i].CommitEligible, "admitted future non-commit");
                AssertFalse(admitted.Steps[i].CurrentLegal, "admitted future not current_legal");
            }
            // Cannot increase/override deterministic Score / ScoreFeatures of grounded line.
            AssertEqual(groundedScore, grounded.Score,
                "admission must not mutate grounded line Score");
            AssertFeatureMapsEqual(groundedFeatures, grounded.ScoreFeatures,
                "admission must not mutate grounded ScoreFeatures");
            if (admitted.ScoreFeatures != null && grounded.ScoreFeatures != null)
            {
                foreach (KeyValuePair<string, int> kv in grounded.ScoreFeatures)
                {
                    if (admitted.ScoreFeatures.ContainsKey(kv.Key))
                    {
                        AssertTrue(admitted.ScoreFeatures[kv.Key] <= kv.Value
                            || admitted.Score <= grounded.Score,
                            "admitted hypothesis must not gain score authority over grounded features");
                    }
                }
            }
            AssertTrue(admitted.Score <= grounded.Score,
                "admitted hypothesis Score must not exceed grounded deterministic Score");

            // Invalid root
            AssertRejectReason(tryAdmit, snap, groundedGraph,
                MakeHypothesisLine(rootId: 99, uncertain: true, leakLabel: null),
                "missing_legal_root");

            // Invalid provenance
            LlmSearchLine badProv = MakeHypothesisLine(0, true, null);
            badProv.Provenance = ProvenanceRulesInferred;
            AssertRejectReason(tryAdmit, snap, groundedGraph, badProv, "invalid_provenance");

            // Missing uncertainty
            LlmSearchLine noUnc = MakeHypothesisLine(0, uncertain: false, leakLabel: null);
            AssertRejectReason(tryAdmit, snap, groundedGraph, noUnc, "missing_uncertainty");

            // Hidden sentinels — multiple arbitrary ids/names
            for (int i = 0; i < SentinelOppNames.Length; i++)
            {
                LlmSearchLine leak = MakeHypothesisLine(0, true, SentinelOppNames[i] + " id=" + SentinelOppIds[i]);
                AssertRejectReason(tryAdmit, snap, groundedGraph, leak, "opponent_hidden_identity");
            }
        }

        static void AssertRejectReason(
            MethodInfo tryAdmit,
            DecisionSnapshot snap,
            LlmSearchGraph graph,
            LlmSearchLine line,
            string expectedReason)
        {
            object[] args = new object[] { snap, graph, line, null, null };
            bool ok = Convert.ToBoolean(tryAdmit.Invoke(null, args));
            AssertFalse(ok, "TryAdmit must be false for " + expectedReason);
            string reason = Convert.ToString(args[4] ?? string.Empty);
            AssertEqual(expectedReason, reason,
                "stable reject reason for " + expectedReason);
            AssertTrue(args[3] == null, "out admitted line must be null on reject");
        }

        // =====================================================================
        // Synchro matcher — positive + negative (not catalog-only)
        // =====================================================================
        static void SynchroMatcherPositiveAndNegativeControls(Type matcherType)
        {
            // Positive: face-up Junk Synchron + legal Normal Summon L5 non-tuner + Stardust ED.
            DecisionSnapshot synSnap = MakeBaseSnapshot();
            synSnap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = ControlledPlayer,
                Position = 13,
                Index = 0,
                CardId = SynthNonTunerLevel5Id,
                Card = new LlmCardMetadata()
                {
                    CardId = SynthNonTunerLevel5Id,
                    Name = "SYNTH_L5_NONTUNER",
                    Level = 5,
                    Kind = "Effect",
                    Frame = "Effect",
                    SummonFamily = "main_deck_monster",
                    IsTuner = false,
                },
                ActionLabel = "Summon SYNTH_L5_NONTUNER",
            });
            synSnap.LegalActions.Add(MakeEndRoot(1));
            synSnap.StrategicActionCount = 2;

            LlmSelfResources synSelf = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(JunkSynchronInternalCardId, JunkSynchronName, 0, true, 3, true, 0),
                },
                hand: new List<LlmSelfResourceCard>()
                {
                    FieldCard(SynthNonTunerLevel5Id, "SYNTH_L5_NONTUNER", 13, true, 5, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, 8),
                });
            LlmSearchGraph synGraph = ExpandExact(matcherType, synSnap, synSelf);
            LlmSearchLine synLine = FindSynchroTemplateLine(synGraph);
            AssertNotNull(synLine,
                "positive grounded Synchro matcher: ExpandToGraph must emit Synchro template line "
                + "(TemplateId=" + ExpectedSynchroTemplateId + ")");
            AssertEqual(ExpectedSynchroTemplateId, RequireStringProp(synLine, "TemplateId"),
                "Synchro emitted TemplateId");
            AssertEqual(ProvenanceRulesInferred, synLine.Provenance, "Synchro line provenance");
            object synReq = RequireProp(synLine, "SummonRequirement");
            AssertEqual("synchro", NormalizeFamily(RequireStringProp(synReq, "SummonFamily")),
                "Synchro SummonRequirement.SummonFamily");
            AssertEqual(StardustDragonInternalCardId,
                Convert.ToInt32(RequireProp(synReq, "ExtraDeckTargetCardId")),
                "Synchro ExtraDeckTargetCardId=7734");

            // Negative: missing tuner
            LlmSelfResources noTuner = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(SynthNonTunerLevel5Id, "SYNTH_L5_NONTUNER", 0, true, 5, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, 8),
                });
            AssertTrue(FindSynchroTemplateLine(ExpandExact(matcherType, synSnap, noTuner)) == null,
                "Synchro matcher: missing tuner => no template line");

            // Negative: missing Synchro ED
            LlmSelfResources noEd = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(JunkSynchronInternalCardId, JunkSynchronName, 0, true, 3, true, 0),
                },
                hand: new List<LlmSelfResourceCard>()
                {
                    FieldCard(SynthNonTunerLevel5Id, "SYNTH_L5_NONTUNER", 13, true, 5, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>());
            AssertTrue(FindSynchroTemplateLine(ExpandExact(matcherType, synSnap, noEd)) == null,
                "Synchro matcher: missing ED target => no template line");
        }

        // =====================================================================
        // Intended followup memory — FromSelectedLine pre-commit; Evaluate POST-ROOT only
        // =====================================================================
        static void IntendedFollowupExactFieldsAndPostRootStatusSemantics(
            Type memoryType, Type evalType, LlmSearchGraph preRootGraph)
        {
            MethodInfo fromSelected = RequireExactPublicStaticMethod(
                memoryType,
                "FromSelectedLine",
                typeof(DecisionSnapshot),
                typeof(LlmSearchLine));
            AssertTrue(memoryType.IsAssignableFrom(fromSelected.ReturnType),
                "FromSelectedLine returns LlmIntendedFollowupMemory");

            MethodInfo evaluate = RequireExactPublicStaticMethod(
                evalType,
                "Evaluate",
                memoryType,
                typeof(DecisionSnapshot),
                typeof(LlmSearchGraph));

            // Pre-commit: flip-Cave selected template line (setup root, not Utopia SummonSp).
            DecisionSnapshot preCommitSnap = MakeXyzSetupSnapshot();
            preCommitSnap.Turn = 3;
            preCommitSnap.RunEffectSeq = 42UL;
            LlmSearchLine selected = FindXyzTemplateLine(preRootGraph);
            AssertNotNull(selected, "pre-commit selected flip-Cave template line");

            object memory = fromSelected.Invoke(null, new object[] { preCommitSnap, selected });
            AssertNotNull(memory, "FromSelectedLine result");

            // Exact structured fields — do not infer from labels.
            AssertEqual(ExpectedXyzTemplateId, RequireStringProp(memory, "TemplateId"), "TemplateId");
            AssertEqual(selected.LineId ?? RequireStringProp(selected, "LineId"),
                RequireStringProp(memory, "LineId"), "LineId");
            AssertEqual(0, Convert.ToInt32(RequireProp(memory, "RootActionId")), "RootActionId");
            AssertEqual(42UL, Convert.ToUInt64(RequireProp(memory, "OriginRunEffectSeq")),
                "OriginRunEffectSeq");
            AssertEqual(3, Convert.ToInt32(RequireProp(memory, "OriginTurn")), "OriginTurn");
            AssertEqual("xyz", NormalizeFamily(RequireStringProp(memory, "ExpectedSummonFamily")),
                "ExpectedSummonFamily");
            AssertEqual(UtopiaInternalCardId, Convert.ToInt32(RequireProp(memory, "ExpectedTargetCardId")),
                "ExpectedTargetCardId=9575");
            List<int> expectedMats = ToIntList(RequireProp(memory, "ExpectedMaterialCardIds"));
            AssertEqual(2, expectedMats.Count, "ExpectedMaterialCardIds count");
            AssertEqual(CaveDragonInternalCardId, expectedMats[0], "ExpectedMaterialCardIds[0]=4103");
            AssertEqual(GirochinInternalCardId, expectedMats[1], "ExpectedMaterialCardIds[1]=5136");
            IEnumerable srcs = RequireExactEnumerableProperty(memory, "ExpectedMaterialSources");
            AssertDistinctSourceIdentities(srcs.Cast<object>().Where(x => x != null).ToList());

            // ---- POST-ROOT windows only for Evaluate ----

            // (1) available: engine-current legal actions include SummonSp Utopia 9575 with ActionId=7
            DecisionSnapshot postAvail = MakePostRootMaterialsGroundedSnapshot(turn: 3, seq: 50UL);
            postAvail.LegalActions.Add(MakeUtopiaSummonSp(PostRootUtopiaSummonSpActionId));
            postAvail.LegalActions.Add(MakeEndRoot(1));
            postAvail.StrategicActionCount = 2;
            LlmSearchGraph postAvailGraph = MakePostRootGraphWithUtopiaLegal(
                postAvail, MakePostRootBothMaterialsFaceUpSelf(), PostRootUtopiaSummonSpActionId);
            object statusAvail = evaluate.Invoke(null, new object[] { memory, postAvail, postAvailGraph });
            AssertStatus(statusAvail, "available");
            AssertEqual(PostRootUtopiaSummonSpActionId, Convert.ToInt32(RequireProp(statusAvail, "ActionId")),
                "available ActionId must be exact Utopia SummonSp id 7 (not setup root 0)");
            AssertFalse(Convert.ToBoolean(RequireProp(statusAvail, "AutoCommit")),
                "available => AutoCommit=false");

            // (2) not_yet_available: materials + Utopia ED grounded; NO legal Utopia SummonSp
            DecisionSnapshot postNotYet = MakePostRootMaterialsGroundedSnapshot(turn: 3, seq: 51UL);
            postNotYet.LegalActions.Add(MakeEndRoot(0));
            postNotYet.LegalActions.Add(MakeBattleRoot(1));
            postNotYet.StrategicActionCount = 2;
            LlmSearchGraph postNotYetGraph = MakePostRootGraphNoUtopiaLegal(
                postNotYet, MakePostRootBothMaterialsFaceUpSelf());
            object statusNotYet = evaluate.Invoke(null, new object[] { memory, postNotYet, postNotYetGraph });
            AssertStatus(statusNotYet, "not_yet_available");
            AssertFalse(
                string.Equals(StatusName(statusNotYet), "available", StringComparison.Ordinal),
                "not_yet_available must never be classified as available");
            AssertFalse(Convert.ToBoolean(RequireProp(statusNotYet, "AutoCommit")),
                "not_yet_available AutoCommit=false");

            // (3a) invalidated: missing Utopia ED target (materials still present)
            DecisionSnapshot postNoEd = MakePostRootMaterialsGroundedSnapshot(turn: 3, seq: 52UL);
            postNoEd.LegalActions.Add(MakeEndRoot(0));
            postNoEd.StrategicActionCount = 1;
            LlmSearchGraph gNoEd = new LlmSearchGraph();
            gNoEd.SourceSnapshot = postNoEd;
            gNoEd.SourceSelfResources = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                    FieldCard(CaveDragonInternalCardId, CaveDragonName, 1, true, 4, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>());
            gNoEd.Lines.Add(RootShell(0));
            object stNoEd = evaluate.Invoke(null, new object[] { memory, postNoEd, gNoEd });
            AssertStatus(stNoEd, "invalidated");
            AssertFalse(StatusName(stNoEd) == "not_yet_available",
                "missing Utopia ED must not be not_yet_available");

            // (3b) invalidated: missing material (Cave gone)
            DecisionSnapshot postMissMat = MakePostRootMaterialsGroundedSnapshot(turn: 3, seq: 53UL);
            postMissMat.LegalActions.Add(MakeEndRoot(0));
            postMissMat.StrategicActionCount = 1;
            LlmSearchGraph gMissMat = new LlmSearchGraph();
            gMissMat.SourceSnapshot = postMissMat;
            gMissMat.SourceSelfResources = MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 4),
                });
            gMissMat.Lines.Add(RootShell(0));
            object stMissMat = evaluate.Invoke(null, new object[] { memory, postMissMat, gMissMat });
            AssertStatus(stMissMat, "invalidated");
            AssertFalse(StatusName(stMissMat) == "not_yet_available",
                "missing material must not be not_yet_available");

            // (3c) expired/invalidated: changed turn
            DecisionSnapshot postTurn = MakePostRootMaterialsGroundedSnapshot(turn: 99, seq: 100UL);
            postTurn.LegalActions.Add(MakeUtopiaSummonSp(PostRootUtopiaSummonSpActionId));
            postTurn.StrategicActionCount = 1;
            LlmSearchGraph gTurn = MakePostRootGraphWithUtopiaLegal(
                postTurn, MakePostRootBothMaterialsFaceUpSelf(), PostRootUtopiaSummonSpActionId);
            object stTurn = evaluate.Invoke(null, new object[] { memory, postTurn, gTurn });
            AssertTrue(
                StatusName(stTurn) == "expired" || StatusName(stTurn) == "invalidated",
                "changed turn must expire/invalidate (got " + StatusName(stTurn) + ")");
            AssertFalse(StatusName(stTurn) == "not_yet_available",
                "changed turn must not be not_yet_available");
        }

        static void TurnMemoryTrackerIntegrationExactOverloads(
            Type memoryType, Type evalType, LlmSearchGraph preRootGraph)
        {
            Type trackerType = typeof(LlmTurnMemoryTracker);
            MethodInfo record4 = trackerType.GetMethod(
                "RecordCommittedAction",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[]
                {
                    typeof(DecisionSnapshot),
                    typeof(LlmBrokerDecisionResponse),
                    typeof(LegalAction),
                    typeof(LlmSearchLine),
                },
                null);
            AssertNotNull(record4,
                "LlmTurnMemoryTracker.RecordCommittedAction(snapshot, response, action, selectedLine) "
                + "exact 4-arg overload required");

            // Pin ONLY exact CreateSnapshot(DecisionSnapshot, LlmSearchGraph) — no setters/properties.
            MethodInfo createWithGraph = trackerType.GetMethod(
                "CreateSnapshot",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(DecisionSnapshot), typeof(LlmSearchGraph) },
                null);
            AssertNotNull(createWithGraph,
                "LlmTurnMemoryTracker.CreateSnapshot(DecisionSnapshot, LlmSearchGraph) exact overload required "
                + "(no PendingSearchGraph / SetSearchGraph alternatives)");

            PropertyInfo intentProp = typeof(LlmTurnMemoryState).GetProperty(
                "IntendedFollowup", BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(intentProp, "LlmTurnMemoryState.IntendedFollowup public property required");
            AssertTrue(
                memoryType.IsAssignableFrom(intentProp.PropertyType)
                || intentProp.PropertyType.Name == IntendedFollowupTypeName,
                "IntendedFollowup property type must be LlmIntendedFollowupMemory");

            // Record setup root on PRE-COMMIT snapshot with selected flip-Cave template line.
            DecisionSnapshot preCommit = MakeXyzSetupSnapshot();
            preCommit.Turn = 5;
            preCommit.RunEffectSeq = 77UL;
            LlmSearchLine selected = FindXyzTemplateLine(preRootGraph);
            AssertNotNull(selected, "selected pre-commit template line");
            LegalAction setupAction = preCommit.LegalActions[0];
            LlmBrokerDecisionResponse response = new LlmBrokerDecisionResponse()
            {
                ActionId = 0,
                Reason = "template_line",
                Plan = ExpectedXyzTemplateId,
            };

            LlmTurnMemoryTracker tracker = new LlmTurnMemoryTracker(8);
            record4.Invoke(tracker, new object[] { preCommit, response, setupAction, selected });

            // Then CreateSnapshot(POST-ROOT snapshot, POST-ROOT graph) only.
            DecisionSnapshot postRoot = MakePostRootMaterialsGroundedSnapshot(turn: 5, seq: 78UL);
            postRoot.LegalActions.Add(MakeUtopiaSummonSp(PostRootUtopiaSummonSpActionId));
            postRoot.LegalActions.Add(MakeEndRoot(1));
            postRoot.StrategicActionCount = 2;
            LlmSearchGraph postRootGraph = MakePostRootGraphWithUtopiaLegal(
                postRoot, MakePostRootBothMaterialsFaceUpSelf(), PostRootUtopiaSummonSpActionId);

            LlmTurnMemoryState memState = (LlmTurnMemoryState)createWithGraph.Invoke(
                tracker, new object[] { postRoot, postRootGraph });
            object intent = intentProp.GetValue(memState, null);
            AssertNotNull(intent,
                "after 4-arg RecordCommittedAction + CreateSnapshot(postRoot, postRootGraph), "
                + "IntendedFollowup must be present");
            AssertEqual(ExpectedXyzTemplateId, RequireStringProp(intent, "TemplateId"),
                "tracker-integrated intent TemplateId");
            AssertEqual(UtopiaInternalCardId, Convert.ToInt32(RequireProp(intent, "ExpectedTargetCardId")),
                "tracker-integrated ExpectedTargetCardId");

            // SerializeTurnMemory must include intended_followup + evaluation status (request path).
            Dictionary<string, object> serialized = LlmDecisionLogSerializer.SerializeTurnMemory(memState);
            AssertNotNull(serialized, "SerializeTurnMemory");
            AssertTrue(serialized.ContainsKey("intended_followup"),
                "SerializeTurnMemory must emit intended_followup dictionary");
            Dictionary<string, object> ifu = serialized["intended_followup"] as Dictionary<string, object>;
            AssertNotNull(ifu, "intended_followup bag");
            AssertTrue(
                (ifu.ContainsKey("TemplateId") || ifu.ContainsKey("template_id"))
                && (ifu.ContainsKey("status") || ifu.ContainsKey("Status")
                    || ifu.ContainsKey("evaluation_status") || ifu.ContainsKey("EvaluationStatus")),
                "intended_followup must include structured intent fields and current evaluation status");

            // Existing 3-arg path leaves intent absent
            LlmTurnMemoryTracker tracker3 = new LlmTurnMemoryTracker(8);
            MethodInfo record3 = trackerType.GetMethod(
                "RecordCommittedAction",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[]
                {
                    typeof(DecisionSnapshot),
                    typeof(LlmBrokerDecisionResponse),
                    typeof(LegalAction),
                },
                null);
            AssertNotNull(record3, "legacy 3-arg RecordCommittedAction preserved");
            record3.Invoke(tracker3, new object[] { preCommit, response, setupAction });
            LlmTurnMemoryState mem3 = (LlmTurnMemoryState)createWithGraph.Invoke(
                tracker3, new object[] { postRoot, postRootGraph });
            AssertTrue(intentProp.GetValue(mem3, null) == null,
                "3-arg recovery/normal path must leave IntendedFollowup absent");

            // Reset clears
            tracker.Reset();
            LlmTurnMemoryState afterReset = (LlmTurnMemoryState)createWithGraph.Invoke(
                tracker, new object[] { postRoot, postRootGraph });
            AssertTrue(intentProp.GetValue(afterReset, null) == null, "Reset clears IntendedFollowup");

            // Turn change via CreateSnapshot(postRoot with new turn, graph)
            record4.Invoke(tracker, new object[] { preCommit, response, setupAction, selected });
            DecisionSnapshot turnChanged = MakePostRootMaterialsGroundedSnapshot(turn: 6, seq: 79UL);
            turnChanged.LegalActions.Add(MakeUtopiaSummonSp(PostRootUtopiaSummonSpActionId));
            turnChanged.StrategicActionCount = 1;
            LlmSearchGraph turnGraph = MakePostRootGraphWithUtopiaLegal(
                turnChanged, MakePostRootBothMaterialsFaceUpSelf(), PostRootUtopiaSummonSpActionId);
            LlmTurnMemoryState memTurn = (LlmTurnMemoryState)createWithGraph.Invoke(
                tracker, new object[] { turnChanged, turnGraph });
            object intentTurn = intentProp.GetValue(memTurn, null);
            if (intentTurn != null)
            {
                MethodInfo evaluate = RequireExactPublicStaticMethod(
                    evalType, "Evaluate", memoryType, typeof(DecisionSnapshot), typeof(LlmSearchGraph));
                object st = evaluate.Invoke(null, new object[] { intentTurn, turnChanged, turnGraph });
                AssertTrue(
                    StatusName(st) == "expired" || StatusName(st) == "invalidated",
                    "turn change must expire/invalidate intended followup");
            }
        }

        // =====================================================================
        // Projection + SerializeTurnMemory paired-hidden identity
        // =====================================================================
        static void ProjectionAndSerializeTurnMemoryPairedHidden(
            LlmSearchGraph preRootGraph, Type memoryType)
        {
            MethodInfo fromSelected = RequireExactPublicStaticMethod(
                memoryType, "FromSelectedLine", typeof(DecisionSnapshot), typeof(LlmSearchLine));
            MethodInfo createWithGraph = typeof(LlmTurnMemoryTracker).GetMethod(
                "CreateSnapshot",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(DecisionSnapshot), typeof(LlmSearchGraph) },
                null);
            AssertNotNull(createWithGraph, "CreateSnapshot(DecisionSnapshot, LlmSearchGraph) for serialization test");
            MethodInfo record4 = typeof(LlmTurnMemoryTracker).GetMethod(
                "RecordCommittedAction",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[]
                {
                    typeof(DecisionSnapshot),
                    typeof(LlmBrokerDecisionResponse),
                    typeof(LegalAction),
                    typeof(LlmSearchLine),
                },
                null);
            AssertNotNull(record4, "4-arg RecordCommittedAction for serialization test");

            DecisionSnapshot preA = MakeXyzSetupSnapshot();
            DecisionSnapshot preB = MakeXyzSetupSnapshot();
            preA.Turn = 4;
            preB.Turn = 4;
            preA.RunEffectSeq = 10UL;
            preB.RunEffectSeq = 10UL;
            for (int i = 0; i < SentinelOppIds.Length; i++)
            {
                PlantOpponentHidden(preA, SentinelOppIds[i], SentinelOppNames[i] + "_A");
                PlantOpponentHidden(preB, SentinelOppIds[i] + 1000, SentinelOppNames[i] + "_B");
            }

            Type matcherType = RequireExactType(MatcherTypeName, "matcher");
            LlmSearchGraph gA = ExpandExact(matcherType, preA, MakeXyzReadySelf());
            LlmSearchGraph gB = ExpandExact(matcherType, preB, MakeXyzReadySelf());
            string pA = MiniJSON.Json.Serialize(LlmSearchProjection.Project(gA)) ?? string.Empty;
            string pB = MiniJSON.Json.Serialize(LlmSearchProjection.Project(gB)) ?? string.Empty;
            AssertEqual(pA, pB, "paired-hidden graph projections must be byte-identical");

            LlmSearchLine sel = FindXyzTemplateLine(gA);
            AssertNotNull(sel, "template line for serialization");
            LlmBrokerDecisionResponse response = new LlmBrokerDecisionResponse()
            {
                ActionId = 0,
                Reason = "template",
                Plan = ExpectedXyzTemplateId,
            };

            LlmTurnMemoryTracker trA = new LlmTurnMemoryTracker(8);
            LlmTurnMemoryTracker trB = new LlmTurnMemoryTracker(8);
            record4.Invoke(trA, new object[] { preA, response, preA.LegalActions[0], sel });
            record4.Invoke(trB, new object[] { preB, response, preB.LegalActions[0], sel });

            DecisionSnapshot postA = MakePostRootMaterialsGroundedSnapshot(turn: 4, seq: 11UL);
            DecisionSnapshot postB = MakePostRootMaterialsGroundedSnapshot(turn: 4, seq: 11UL);
            postA.LegalActions.Add(MakeUtopiaSummonSp(PostRootUtopiaSummonSpActionId));
            postB.LegalActions.Add(MakeUtopiaSummonSp(PostRootUtopiaSummonSpActionId));
            for (int i = 0; i < SentinelOppIds.Length; i++)
            {
                PlantOpponentHidden(postA, SentinelOppIds[i], SentinelOppNames[i] + "_A");
                PlantOpponentHidden(postB, SentinelOppIds[i] + 1000, SentinelOppNames[i] + "_B");
            }
            LlmSearchGraph postGA = MakePostRootGraphWithUtopiaLegal(
                postA, MakePostRootBothMaterialsFaceUpSelf(), PostRootUtopiaSummonSpActionId);
            LlmSearchGraph postGB = MakePostRootGraphWithUtopiaLegal(
                postB, MakePostRootBothMaterialsFaceUpSelf(), PostRootUtopiaSummonSpActionId);

            LlmTurnMemoryState memA = (LlmTurnMemoryState)createWithGraph.Invoke(
                trA, new object[] { postA, postGA });
            LlmTurnMemoryState memB = (LlmTurnMemoryState)createWithGraph.Invoke(
                trB, new object[] { postB, postGB });

            // Request-path only: SerializeTurnMemory — no property-bag fallbacks.
            string sA = MiniJSON.Json.Serialize(LlmDecisionLogSerializer.SerializeTurnMemory(memA)) ?? string.Empty;
            string sB = MiniJSON.Json.Serialize(LlmDecisionLogSerializer.SerializeTurnMemory(memB)) ?? string.Empty;
            AssertEqual(sA, sB,
                "paired-hidden variants must compare full SerializeTurnMemory output byte-for-byte");
            AssertTrue(sA.IndexOf("intended_followup", StringComparison.Ordinal) >= 0,
                "SerializeTurnMemory must emit intended_followup");
            AssertTrue(
                sA.IndexOf(ExpectedXyzTemplateId, StringComparison.Ordinal) >= 0
                || sA.IndexOf("9575", StringComparison.Ordinal) >= 0,
                "intended_followup must include structured template/target identity");
            AssertTrue(
                sA.IndexOf("available", StringComparison.OrdinalIgnoreCase) >= 0
                || sA.IndexOf("status", StringComparison.OrdinalIgnoreCase) >= 0,
                "intended_followup must include current evaluation status");
            foreach (string n in SentinelOppNames)
            {
                AssertTrue(sA.IndexOf(n, StringComparison.Ordinal) < 0
                    && pA.IndexOf(n, StringComparison.Ordinal) < 0,
                    "no opponent sentinel leak: " + n);
            }
        }

        // =====================================================================
        // Corpus manifest
        // =====================================================================
        static void CorpusCoverageManifestExact()
        {
            Type manifestType = RequireExactType(
                CorpusManifestTypeName,
                "LlmSlice6BCorpusCoverageManifest.CreateDefault() with Families and ClaimsExactSimulation=false");
            MethodInfo create = RequireExactPublicStaticMethod(manifestType, "CreateDefault", Type.EmptyTypes);
            object manifest = create.Invoke(null, null);
            AssertNotNull(manifest, "manifest");
            AssertFalse(Convert.ToBoolean(RequireProp(manifest, "ClaimsExactSimulation")),
                "ClaimsExactSimulation must be false");
            IEnumerable families = RequireExactEnumerableProperty(manifest, "Families");
            string blob = string.Join("|", families.Cast<object>().Select(o =>
            {
                if (o == null)
                {
                    return string.Empty;
                }
                object name = GetExactPropOrNull(o, "Family")
                    ?? GetExactPropOrNull(o, "Name")
                    ?? GetExactPropOrNull(o, "Id")
                    ?? o;
                return Convert.ToString(name) ?? string.Empty;
            }));
            AssertTrue(blob.IndexOf("xyz", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("girochin", StringComparison.OrdinalIgnoreCase) >= 0,
                "Families must name Xyz/girochin");
            AssertTrue(blob.IndexOf("synchro", StringComparison.OrdinalIgnoreCase) >= 0,
                "Families must name Synchro");
            AssertTrue(blob.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("boundary", StringComparison.OrdinalIgnoreCase) >= 0,
                "Families must name unknown-boundary");
            AssertTrue(blob.IndexOf("lethal", StringComparison.OrdinalIgnoreCase) >= 0,
                "Families must name lethal-vs-setup");
            AssertTrue(blob.IndexOf("no_viable", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("no-viable", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("noviable", StringComparison.OrdinalIgnoreCase) >= 0,
                "Families must name no-viable-continuation");

            string root = LlmSlice5Paths.FindRepoRoot();
            string dir = Path.Combine(root, "Tools", "fixtures", "llm_search");
            AssertTrue(File.Exists(Path.Combine(dir, "xyz_girochin_cave_rank4.fixture.json")), "xyz fixture");
            AssertTrue(File.Exists(Path.Combine(dir, "synchro_exact_tuner_nontuner_level_sum.fixture.json")),
                "synchro fixture");
            AssertTrue(File.Exists(Path.Combine(dir, "unknown_opponent_response_boundary.fixture.json")),
                "unknown boundary fixture");
            AssertTrue(File.Exists(Path.Combine(dir, "lethal_vs_setup.fixture.json")), "lethal fixture");
            AssertTrue(File.Exists(Path.Combine(dir, "no_viable_continuation.fixture.json")),
                "no_viable fixture");
        }

        // =====================================================================
        // Fixtures
        // =====================================================================
        static DecisionSnapshot MakeBaseSnapshot()
        {
            return new DecisionSnapshot()
            {
                ControlledPlayer = ControlledPlayer,
                ActingPlayer = ControlledPlayer,
                Turn = 3,
                CurrentPhase = (int)DuelPhase.Main1,
                RunEffectSeq = 10,
                StrategicActionCount = 0,
            };
        }

        static DecisionSnapshot MakeXyzSetupSnapshot()
        {
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Reverse,
                Player = ControlledPlayer,
                Position = 1,
                Index = 0,
                CardId = CaveDragonInternalCardId,
                Card = Meta(CaveDragonInternalCardId, CaveDragonName),
                ActionLabel = "Flip Summon " + CaveDragonName,
            });
            snap.LegalActions.Add(MakeBattleRoot(1));
            snap.LegalActions.Add(MakeEndRoot(2));
            snap.StrategicActionCount = 3;
            return snap;
        }

        static LegalAction MakeBattleRoot(int actionId)
        {
            return new LegalAction()
            {
                ActionId = actionId,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
                Player = ControlledPlayer,
            };
        }

        static LegalAction MakeEndRoot(int actionId)
        {
            return new LegalAction()
            {
                ActionId = actionId,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
                Player = ControlledPlayer,
            };
        }

        static LlmSelfResources MakeXyzReadySelf()
        {
            return MakeSelf(
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                    FieldCard(CaveDragonInternalCardId, CaveDragonName, 1, false, 4, false, 0),
                },
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 4),
                });
        }

        static LlmSelfResources MakeSelf(
            List<LlmSelfResourceCard> field,
            List<LlmSelfResourceExtraDeckEntry> extra)
        {
            return MakeSelf(field, null, extra);
        }

        static LlmSelfResources MakeSelf(
            List<LlmSelfResourceCard> field,
            List<LlmSelfResourceCard> hand,
            List<LlmSelfResourceExtraDeckEntry> extra)
        {
            return LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                hand ?? new List<LlmSelfResourceCard>(),
                field ?? new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                extra ?? new List<LlmSelfResourceExtraDeckEntry>());
        }

        static LlmSelfResources MakePostRootBothMaterialsFaceUpSelf()
        {
            return MakeSelf(
                field: new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, GirochinName, 0, true, 4, false, 0),
                    FieldCard(CaveDragonInternalCardId, CaveDragonName, 1, true, 4, false, 0),
                },
                extra: new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraXyz(UtopiaInternalCardId, UtopiaName, 4),
                });
        }

        /// <summary>
        /// Post-root window after flip-Cave committed: both materials face-up; no setup Reverse root.
        /// </summary>
        static DecisionSnapshot MakePostRootMaterialsGroundedSnapshot(int turn, ulong seq)
        {
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.Turn = turn;
            snap.RunEffectSeq = seq;
            return snap;
        }

        static LegalAction MakeUtopiaSummonSp(int actionId)
        {
            return new LegalAction()
            {
                ActionId = actionId,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.SummonSp,
                Player = ControlledPlayer,
                Position = 14,
                Index = 0,
                CardId = UtopiaInternalCardId,
                Card = new LlmCardMetadata()
                {
                    CardId = UtopiaInternalCardId,
                    Name = UtopiaName,
                    Kind = "Xyz",
                    Frame = "Xyz",
                    SummonFamily = "xyz",
                    UsesRank = true,
                    Level = 4,
                    IsExtraDeck = true,
                },
                ActionLabel = "Xyz Summon " + UtopiaName,
            };
        }

        static LlmSearchGraph MakePostRootGraphWithUtopiaLegal(
            DecisionSnapshot snap, LlmSelfResources self, int utopiaActionId)
        {
            LlmSearchGraph g = new LlmSearchGraph();
            g.SourceSnapshot = snap;
            g.SourceSelfResources = self;
            g.RootRunEffectSeq = unchecked((long)snap.RunEffectSeq);
            // Root shells for each legal action including Utopia SummonSp.
            foreach (LegalAction a in snap.LegalActions)
            {
                if (a == null)
                {
                    continue;
                }
                LlmSearchLine shell = RootShell(a.ActionId);
                if (a.CardId == UtopiaInternalCardId && a.Command == DuelCommandType.SummonSp)
                {
                    shell.LineId = "legal_utopia_summonsp_" + utopiaActionId;
                    shell.Steps[0].Label = "Xyz Summon " + UtopiaName;
                }
                g.Lines.Add(shell);
            }
            return g;
        }

        static LlmSearchGraph MakePostRootGraphNoUtopiaLegal(
            DecisionSnapshot snap, LlmSelfResources self)
        {
            LlmSearchGraph g = new LlmSearchGraph();
            g.SourceSnapshot = snap;
            g.SourceSelfResources = self;
            g.RootRunEffectSeq = unchecked((long)snap.RunEffectSeq);
            foreach (LegalAction a in snap.LegalActions)
            {
                if (a != null)
                {
                    g.Lines.Add(RootShell(a.ActionId));
                }
            }
            // Materials + Utopia ED remain grounded via SourceSelfResources; NO legal Utopia SummonSp root.
            return g;
        }

        static LlmSelfResourceExtraDeckEntry ExtraSynchro(int cardId, string name, int level)
        {
            return LlmSelfResourceExtraDeckEntry.Create(
                cardId, name, "Synchro", "synchro",
                level, null, false, null, false, "Synchro",
                2500, 2000, null, 1);
        }

        static LlmSelfResourceCard FieldCard(
            int cardId, string name, int zone, bool faceUp, int level, bool isTuner, int index)
        {
            return LlmSelfResourceCard.Create(
                cardId, name, zone, index,
                faceUp ? 1 : 0, faceUp,
                level > 0 ? (int?)level : null,
                null, false, isTuner, null,
                "Effect", "main_deck_monster", false, "Effect",
                1500, 0, null, 0);
        }

        static LlmSelfResourceExtraDeckEntry ExtraXyz(int cardId, string name, int rank)
        {
            return LlmSelfResourceExtraDeckEntry.Create(
                cardId, name, "Xyz", "xyz",
                null, rank, true, null, false, "Xyz",
                2500, 2000, null, 1);
        }

        static LlmCardMetadata Meta(int cardId, string name)
        {
            return new LlmCardMetadata()
            {
                CardId = cardId,
                Name = name,
                Level = 4,
                Kind = "Effect",
                Frame = "Effect",
                SummonFamily = "main_deck_monster",
            };
        }

        static void PlantOpponentHidden(DecisionSnapshot snap, int cardId, string name)
        {
            PublicPlayerState opp = snap.PublicState.Players.FirstOrDefault(p => p != null && p.Player == OpponentPlayer);
            if (opp == null)
            {
                opp = new PublicPlayerState() { Player = OpponentPlayer };
                snap.PublicState.Players.Add(opp);
            }
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = 13,
                Index = opp.KnownCards.Count,
                CardId = cardId,
                Face = 0,
                Card = new LlmCardMetadata() { CardId = cardId, Name = name },
            });
        }

        static LlmSearchLine RootShell(int rootId)
        {
            LlmSearchLine line = new LlmSearchLine()
            {
                LineId = "root_" + rootId,
                RootActionId = rootId,
                Provenance = "engine_current",
                CommitEligible = true,
                StrategicDepth = 0,
                IsRootShell = true,
            };
            line.Steps.Add(new LlmSearchStep()
            {
                Label = "root",
                CurrentLegal = true,
                CommitEligible = true,
                Provenance = "engine_current",
            });
            return line;
        }

        static LlmSearchLine MakeHypothesisLine(int rootId, bool uncertain, string leakLabel)
        {
            LlmSearchLine line = new LlmSearchLine()
            {
                LineId = "hyp_" + rootId,
                RootActionId = rootId,
                Provenance = ProvenanceModelHypothesis,
                CommitEligible = false,
                StrategicDepth = 1,
                Score = 0,
            };
            line.Steps.Add(new LlmSearchStep()
            {
                Label = "root",
                CurrentLegal = rootId == 0,
                CommitEligible = rootId == 0,
                Provenance = ProvenanceModelHypothesis,
            });
            line.Steps.Add(new LlmSearchStep()
            {
                Label = leakLabel ?? "future hypothesis step",
                CurrentLegal = false,
                CommitEligible = false,
                Provenance = ProvenanceModelHypothesis,
            });
            if (uncertain)
            {
                line.Uncertainty.Add("model_authored_uncertain");
            }
            return line;
        }

        static LlmSearchLine FindXyzTemplateLine(LlmSearchGraph graph)
        {
            return FindTemplateLineById(graph, ExpectedXyzTemplateId);
        }

        static LlmSearchLine FindSynchroTemplateLine(LlmSearchGraph graph)
        {
            return FindTemplateLineById(graph, ExpectedSynchroTemplateId);
        }

        static LlmSearchLine FindTemplateLineById(LlmSearchGraph graph, string templateId)
        {
            if (graph == null || graph.Lines == null)
            {
                return null;
            }
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line == null || line.IsRootShell)
                {
                    continue;
                }
                string tid = Convert.ToString(GetExactPropOrNull(line, "TemplateId") ?? string.Empty);
                if (tid == templateId)
                {
                    return line;
                }
            }
            return null;
        }

        // =====================================================================
        // Exact reflection helpers (no aliases)
        // =====================================================================
        static Type RequireExactType(string simpleName, string purpose)
        {
            Type t = TryGetType(simpleName);
            if (t == null)
            {
                throw new InvalidOperationException(
                    "YGOMASTER-LLM-005 Slice 6B RED: missing production type '"
                    + simpleName
                    + "'. Exact contract: "
                    + purpose
                    + ". No aliases. Default-off Layer A after Slice 5 engine NO-GO; "
                    + "do not claim exact duel.dll simulation.");
            }
            return t;
        }

        static Type TryGetType(string simpleName)
        {
            Assembly asm = typeof(Llm005Slice6BTests).Assembly;
            Type direct = asm.GetType("YgoMaster." + simpleName, false);
            if (direct != null)
            {
                return direct;
            }
            foreach (Type t in asm.GetTypes())
            {
                if (t != null && t.Name == simpleName)
                {
                    return t;
                }
            }
            return null;
        }

        static MethodInfo RequireExactPublicStaticMethod(Type type, string name, params Type[] paramTypes)
        {
            MethodInfo m = type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static,
                null,
                paramTypes,
                null);
            if (m == null)
            {
                throw new InvalidOperationException(
                    "YGOMASTER-LLM-005 Slice 6B RED: "
                    + type.Name
                    + "."
                    + name
                    + "("
                    + string.Join(", ", paramTypes.Select(DescribeType))
                    + ") public static method required (exact signature; no aliases)");
            }
            return m;
        }

        static string DescribeType(Type t)
        {
            if (t == null)
            {
                return "null";
            }
            if (t.IsByRef)
            {
                return "out/ref " + t.GetElementType().Name;
            }
            return t.Name;
        }

        static IEnumerable RequireExactEnumerableProperty(object target, string name)
        {
            object v = RequireProp(target, name);
            if (!IsNonStringEnumerable(v))
            {
                throw new InvalidOperationException(
                    target.GetType().Name + "." + name + " must be non-string IEnumerable");
            }
            return (IEnumerable)v;
        }

        static object RequireProp(object target, string name)
        {
            object v = GetExactPropOrNull(target, name);
            if (v == null && target != null)
            {
                // Distinguish missing property vs null value: property must exist.
                PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                FieldInfo f = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null && f == null)
                {
                    throw new InvalidOperationException(
                        target.GetType().Name + " missing exact public member '" + name + "'");
                }
            }
            if (v == null)
            {
                // null value may be invalid for required fields
                PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.PropertyType.IsValueType
                    && Nullable.GetUnderlyingType(p.PropertyType) == null)
                {
                    return v;
                }
            }
            return v;
        }

        static string RequireStringProp(object target, string name)
        {
            object v = RequireProp(target, name);
            if (v == null)
            {
                throw new InvalidOperationException(
                    target.GetType().Name + "." + name + " must be non-null string");
            }
            return Convert.ToString(v);
        }

        static object GetExactPropOrNull(object target, string name)
        {
            if (target == null)
            {
                return null;
            }
            PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null)
            {
                return p.GetValue(target, null);
            }
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null)
            {
                return f.GetValue(target);
            }
            // Also allow dictionary-style projection bags
            IDictionary dict = target as IDictionary;
            if (dict != null && dict.Contains(name))
            {
                return dict[name];
            }
            return null;
        }

        static bool IsNonStringEnumerable(object v)
        {
            return v is IEnumerable && !(v is string);
        }

        static List<int> ToIntList(object v)
        {
            List<int> list = new List<int>();
            if (!IsNonStringEnumerable(v))
            {
                throw new InvalidOperationException("expected non-string IEnumerable of card ids");
            }
            foreach (object item in (IEnumerable)v)
            {
                if (item == null)
                {
                    throw new InvalidOperationException("null card id in list");
                }
                list.Add(Convert.ToInt32(item));
            }
            return list;
        }

        static void AssertDistinctSourceIdentities(IList<object> sources)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (object s in sources)
            {
                int zone = Convert.ToInt32(RequireProp(s, "Zone"));
                int index = Convert.ToInt32(RequireProp(s, "Index"));
                string key = zone + ":" + index;
                AssertTrue(keys.Add(key),
                    "MaterialSources must have distinct Zone+Index identities; duplicate " + key);
            }
        }

        static string NormalizeFamily(string family)
        {
            return (family ?? string.Empty).Trim().ToLowerInvariant();
        }

        static string StatusName(object status)
        {
            if (status == null)
            {
                return string.Empty;
            }
            if (status is Enum)
            {
                return status.ToString();
            }
            object s = GetExactPropOrNull(status, "Status")
                ?? GetExactPropOrNull(status, "State")
                ?? GetExactPropOrNull(status, "Kind");
            return Convert.ToString(s ?? status) ?? string.Empty;
        }

        static void AssertStatus(object status, string expected)
        {
            AssertEqual(expected, StatusName(status), "status");
        }

        static Dictionary<string, int> CloneFeatures(Dictionary<string, int> src)
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            if (src == null)
            {
                return d;
            }
            foreach (KeyValuePair<string, int> kv in src)
            {
                d[kv.Key] = kv.Value;
            }
            return d;
        }

        static void AssertFeatureMapsEqual(
            Dictionary<string, int> a, Dictionary<string, int> b, string msg)
        {
            if (a == null && b == null)
            {
                return;
            }
            AssertNotNull(a, msg + " left");
            AssertNotNull(b, msg + " right");
            AssertEqual(a.Count, b.Count, msg + " count");
            foreach (KeyValuePair<string, int> kv in a)
            {
                AssertTrue(b.ContainsKey(kv.Key) && b[kv.Key] == kv.Value, msg + " key " + kv.Key);
            }
        }

        static void AssertTrue(bool v, string m)
        {
            if (!v)
            {
                throw new InvalidOperationException(m);
            }
        }

        static void AssertFalse(bool v, string m)
        {
            if (v)
            {
                throw new InvalidOperationException(m);
            }
        }

        static void AssertNotNull(object v, string m)
        {
            if (v == null)
            {
                throw new InvalidOperationException(m);
            }
        }

        static void AssertEqual<T>(T e, T a, string m)
        {
            if (!Equals(e, a))
            {
                throw new InvalidOperationException(m + " (expected " + e + ", got " + a + ")");
            }
        }
    }
}

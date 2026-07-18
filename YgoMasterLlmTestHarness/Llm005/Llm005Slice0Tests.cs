using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-005 Slice 0: planning-contract fixtures (TDD RED).
    ///
    /// Tests bind production APIs by name via reflection so the harness compiles before
    /// types exist. Do not implement production code here.
    ///
    /// Contract surface (Slices 1A–2A):
    ///   LlmSearchLimits
    ///   LlmSelfResources / LlmSelfResourceProjection.Project(ILegalActionQuery, ...)
    ///   LlmTacticalAffordanceGraph.Build(DecisionSnapshot, LlmSelfResources, LlmSearchLimits)
    ///   LlmBoundedLineSearch.Search (optional)
    ///   LlmSearchProjection.Project
    ///   LlmPlanningSearchAudit.TryBuild(..., graphFactory) fail-closed audit wrapper
    /// Provenance: engine_current | engine_confirmed | rules_inferred | model_hypothesis
    /// Face domains: RuntimeDllField (0/1) vs FixtureValidated (8/4) — declare explicitly.
    /// </summary>
    static class Llm005Slice0Tests
    {
        const int GirochinCardId = 71533;
        const int CaveDragonCardId = 66672569;
        const int Rank4XyzCardId = 84013237;
        const int OwnHandCardId = 46986414;
        const int OwnGyCardId = 83764718;
        const int OwnBanishedCardId = 81439173;
        const string GirochinName = "Girochin Kuwagata";
        const string CaveDragonName = "The Dragon Dwelling in the Cave";
        const string Rank4XyzName = "Number 39: Utopia";
        const string OwnHandName = "Dark Magician";
        const string OwnGyName = "Monster Reborn";
        const string OwnBanishedName = "Foolish Burial";

        const int SentinelOpponentHandId = 99105001;
        const string SentinelOpponentHandName = "SENTINEL_OPP_HAND_LEAK_LLM005";
        const int SentinelOpponentSetId = 99105002;
        const string SentinelOpponentSetName = "SENTINEL_OPP_SET_LEAK_LLM005";
        const int SentinelOpponentExtraId = 99105003;
        const string SentinelOpponentExtraName = "SENTINEL_OPP_EXTRA_LEAK_LLM005";

        const int AltOpponentHandId = 77777001;
        const string AltOpponentHandName = "ALT_OPP_HAND_HIDDEN";
        const int AltOpponentSetId = 77777002;
        const string AltOpponentSetName = "ALT_OPP_SET_HIDDEN";
        const int AltOpponentExtraId = 77777003;
        const string AltOpponentExtraName = "ALT_OPP_EXTRA_HIDDEN";

        const int ControlledPlayer = 1;
        const int OpponentPlayer = 0;
        const ulong FixtureRunEffectSeq = 402;
        const int MonsterZone0 = 0;
        const int MonsterZone1 = 1;
        const int OpponentSetZone = 2;
        const int PosHand = 13;
        // Live-validated engine positions: Extra=14, Deck=15.
        const int PosExtra = 14;
        const int PosDeck = 15;
        const int PosGrave = 16;
        const int PosBanished = 17;

        const int RuntimeFaceDown = 0;
        const int RuntimeFaceUp = 1;
        const int FixtureFaceDown = 4;
        const int FixtureFaceUp = 8;

        const string ForcedGraphFailureMessage = "forced_graph_failure_llm005_slice0";

        public static void RunAll()
        {
            GirochinCaveDragonRank4Fixture();
            RootCompletenessUnderTightBeam();
            SetupActionConsiderationPreservesLowImmediateValueRoot();
            CurrentVersusFutureLegality();
            ProvenanceInferredVersusEngineConfirmedCannotConflate();
            BoundaryHandlingOpponentResponse();
            HiddenInformationPairIdenticalLayerAGraphs();
            BudgetDeterminismIdenticalProjection();
            FallbackGraphFailureLeavesOneStepRequestValid();
            NoCandidateControls();
            CurrentXyzDistinctionNotDuplicatedAsFutureOnly();
            PrivateDomainSplitSelfResourcesNotPublic();
            UnsupportedPromptBoundary();
            StaleGenerationDiscardsOldGraph();
            FaceDomainDeclarationRuntimeVersusFixture();
        }

        // ---------------------------------------------------------------------
        // 1. Girochin/Cave Dragon Rank 4 fixture
        // ---------------------------------------------------------------------
        static void GirochinCaveDragonRank4Fixture()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);

            object graph = BuildAndSearch(scenario);
            AssertRootCoverage(graph, scenario.Snapshot, "girochin/cave rank4");

            object flipLine = FindLineForRoot(graph, scenario.FlipRootActionId);
            AssertNotNull(flipLine, "flip-summon root must have at least one candidate line");
            AssertEqual("rules_inferred", GetStringProp(flipLine, "Provenance"), "flip line provenance");
            AssertEqual(false, GetBoolProp(flipLine, "CommitEligible"),
                "flip Rank-4 candidate line must not be commit-eligible as a whole future line");

            AssertTrue(LineMentionsRank4(flipLine),
                "flip line must surface a Rank 4 Xyz candidate (rules_inferred, not commit)");
            AssertTrue(HasNonCommitEligibleFutureStep(flipLine),
                "Rank 4 continuation step must be non-commit-eligible / not current_legal");
        }

        // ---------------------------------------------------------------------
        // 2. Root completeness under tight beam
        // ---------------------------------------------------------------------
        static void RootCompletenessUnderTightBeam()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);
            scenario.Snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 3,
                Kind = LegalActionKind.Command,
                Player = ControlledPlayer,
                Position = MonsterZone0,
                Index = 0,
                Command = DuelCommandType.Action,
                CardId = GirochinCardId,
                Card = scenario.GirochinMeta,
                ActionLabel = "Activate Girochin Kuwagata",
                IsMechanical = false,
                StrategicRole = "activation",
            });
            scenario.Snapshot.StrategicActionCount = scenario.Snapshot.LegalActions.Count;

            object limits = CreateSearchLimits(maxStrategicDepth: 2, maxNodes: 4, beamWidth: 1, maxWallMs: 50);
            object graph = BuildAndSearch(scenario, limits);
            AssertRootCoverage(graph, scenario.Snapshot, "tight beam root completeness");

            object coverage = GetProp(graph, "Coverage");
            AssertNotNull(coverage, "coverage object");
            AssertEqual(
                CountStrategicRoots(scenario.Snapshot),
                GetIntProp(coverage, "RepresentedRootActions"),
                "represented_root_actions must equal strategic legal roots under tight beam");
        }

        // ---------------------------------------------------------------------
        // 3. Setup-action consideration (no hard-coded Xyz selection)
        // ---------------------------------------------------------------------
        static void SetupActionConsiderationPreservesLowImmediateValueRoot()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);
            object limits = CreateSearchLimits(maxStrategicDepth: 3, maxNodes: 12, beamWidth: 2, maxWallMs: 100);
            object graph = BuildAndSearch(scenario, limits);
            object projection = ProjectGraph(graph);

            object flipLine = FindLineForRoot(graph, scenario.FlipRootActionId);
            AssertNotNull(flipLine, "setup root (flip Cave Dragon) must remain after tight beam");
            AssertTrue(LineMentionsRank4(flipLine), "setup line must retain Rank 4 unlock");

            string projected = MiniJSON.Json.Serialize(projection);
            AssertTrue(
                projected.Contains(scenario.FlipRootActionId.ToString())
                || projected.IndexOf("reverse", StringComparison.OrdinalIgnoreCase) >= 0
                || projected.IndexOf("Flip", StringComparison.OrdinalIgnoreCase) >= 0
                || projected.IndexOf(CaveDragonName, StringComparison.OrdinalIgnoreCase) >= 0,
                "provider projection must preserve the setup root/line");
        }

        // ---------------------------------------------------------------------
        // 4. Current versus future legality
        // ---------------------------------------------------------------------
        static void CurrentVersusFutureLegality()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);
            object graph = BuildAndSearch(scenario);
            object flipLine = FindLineForRoot(graph, scenario.FlipRootActionId);
            AssertNotNull(flipLine, "flip line required for legality fixture");

            IEnumerable steps = GetEnumerableProp(flipLine, "Steps");
            AssertNotNull(steps, "flip line steps");
            bool sawFutureXyz = false;
            foreach (object step in steps)
            {
                string label = GetStringProp(step, "Label") ?? string.Empty;
                bool looksLikeXyz =
                    label.IndexOf("Rank 4", StringComparison.OrdinalIgnoreCase) >= 0
                    || label.IndexOf("Xyz", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!looksLikeXyz)
                {
                    continue;
                }
                sawFutureXyz = true;
                AssertEqual(false, GetBoolProp(step, "CurrentLegal"),
                    "future Xyz step current_legal must be false");
                PropertyInfo commitProp = step.GetType().GetProperty("CommitEligible");
                if (commitProp != null)
                {
                    AssertEqual(false, Convert.ToBoolean(commitProp.GetValue(step, null)),
                        "future Xyz step commit_eligible must be false");
                }
            }
            AssertTrue(sawFutureXyz, "expected a future Rank 4 / Xyz step on flip line");

            foreach (object line in GetLines(graph))
            {
                string provenance = GetStringProp(line, "Provenance") ?? string.Empty;
                if (provenance == "rules_inferred" || provenance == "model_hypothesis"
                    || provenance == "engine_confirmed")
                {
                    AssertEqual(false, GetBoolProp(line, "CommitEligible"),
                        "non-engine_current line must not be commit-eligible: " + provenance);
                }
            }
        }

        // ---------------------------------------------------------------------
        // 5. Provenance separation
        // ---------------------------------------------------------------------
        static void ProvenanceInferredVersusEngineConfirmedCannotConflate()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);
            object graph = BuildAndSearch(scenario);

            HashSet<string> provenances = new HashSet<string>(StringComparer.Ordinal);
            foreach (object line in GetLines(graph))
            {
                string p = GetStringProp(line, "Provenance");
                AssertNotNull(p, "every line needs provenance");
                provenances.Add(p);
                AssertTrue(
                    p == "engine_current"
                    || p == "engine_confirmed"
                    || p == "rules_inferred"
                    || p == "model_hypothesis",
                    "unknown provenance value: " + p);

                if (p == "engine_confirmed")
                {
                    throw new Exception(
                        "Layer A fixture must not label edges engine_confirmed without an isolated worker");
                }
            }

            AssertTrue(provenances.Contains("rules_inferred"),
                "Girochin/Cave fixture must include rules_inferred Rank 4 candidacy");
            AssertTrue(
                provenances.Contains("engine_current") || HasEngineCurrentRoot(graph, scenario),
                "current legal roots must be representable as engine_current (roots or edges)");
        }

        // ---------------------------------------------------------------------
        // 6. Boundary handling — opponent response
        // ---------------------------------------------------------------------
        static void BoundaryHandlingOpponentResponse()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);
            object graph = BuildAndSearch(scenario);
            object flipLine = FindLineForRoot(graph, scenario.FlipRootActionId);
            AssertNotNull(flipLine, "flip line for boundary fixture");

            string boundary = GetStringProp(flipLine, "Boundary");
            AssertNotNull(boundary, "flip line must declare a stop boundary");
            AssertTrue(
                boundary.IndexOf("opponent", StringComparison.OrdinalIgnoreCase) >= 0
                || boundary == "opponent_priority"
                || boundary == "opponent_response_or_fresh_engine_window"
                || boundary == "opponent_hidden_choice",
                "opponent-response boundary expected, got: " + boundary);

            object uncertainty = GetProp(flipLine, "Uncertainty");
            AssertNotNull(uncertainty, "uncertainty list required on boundary line");
        }

        // ---------------------------------------------------------------------
        // 7. Hidden-information pair → identical Layer A graphs
        // ---------------------------------------------------------------------
        static void HiddenInformationPairIdenticalLayerAGraphs()
        {
            // Paired engine queries: identical controlled + public projection surface;
            // different opponent-hidden identities living only in the omniscient query.
            // Sentinels must NOT enter turn_memory or any allowed controlled domain.
            GirochinCaveScenario a = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField,
                opponentHiddenHandId: SentinelOpponentHandId,
                opponentHiddenSetId: SentinelOpponentSetId,
                opponentHiddenExtraId: SentinelOpponentExtraId);
            GirochinCaveScenario b = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField,
                opponentHiddenHandId: AltOpponentHandId,
                opponentHiddenSetId: AltOpponentSetId,
                opponentHiddenExtraId: AltOpponentExtraId);

            // Allowed domains stay free of hidden sentinels.
            AssertFalse(
                (a.Snapshot.TurnMemory.PhasePlan ?? string.Empty).Contains("SENTINEL")
                || (a.Snapshot.TurnMemory.PhasePlan ?? string.Empty).Contains("ALT_OPP"),
                "turn_memory must not carry opponent-hidden sentinels");
            AssertEqual(a.Snapshot.TurnMemory.PhasePlan, b.Snapshot.TurnMemory.PhasePlan,
                "paired fixtures must share identical turn_memory");

            string publicA = MiniJSON.Json.Serialize(
                LlmDecisionLogSerializer.SerializePublicState(a.Snapshot.PublicState));
            string publicB = MiniJSON.Json.Serialize(
                LlmDecisionLogSerializer.SerializePublicState(b.Snapshot.PublicState));
            AssertEqual(publicA, publicB, "paired public_state projections must be identical");
            AssertNoLeak(publicA,
                SentinelOpponentHandName, SentinelOpponentHandId,
                SentinelOpponentSetName, SentinelOpponentSetId,
                SentinelOpponentExtraName, SentinelOpponentExtraId,
                AltOpponentHandName, AltOpponentHandId,
                AltOpponentSetName, AltOpponentSetId,
                AltOpponentExtraName, AltOpponentExtraId);

            // Production projector must be invariant across the hidden pair.
            object selfA = ProjectSelfResources(a);
            object selfB = ProjectSelfResources(b);
            string selfJsonA = MiniJSON.Json.Serialize(ToSerializable(selfA));
            string selfJsonB = MiniJSON.Json.Serialize(ToSerializable(selfB));
            AssertEqual(selfJsonA, selfJsonB,
                "LlmSelfResourceProjection must be identical across paired hidden-info queries");
            AssertNoLeak(selfJsonA,
                SentinelOpponentHandName, SentinelOpponentHandId,
                SentinelOpponentSetName, SentinelOpponentSetId,
                SentinelOpponentExtraName, SentinelOpponentExtraId,
                AltOpponentHandName, AltOpponentHandId,
                AltOpponentSetName, AltOpponentSetId,
                AltOpponentExtraName, AltOpponentExtraId);

            object graphA = BuildAndSearch(a);
            object graphB = BuildAndSearch(b);
            string projA = MiniJSON.Json.Serialize(ProjectGraph(graphA));
            string projB = MiniJSON.Json.Serialize(ProjectGraph(graphB));
            AssertEqual(projA, projB,
                "Layer A graphs must be identical across paired hidden-info fixtures");
            AssertNoLeak(projA,
                SentinelOpponentHandName, SentinelOpponentHandId,
                SentinelOpponentSetName, SentinelOpponentSetId,
                SentinelOpponentExtraName, SentinelOpponentExtraId,
                AltOpponentHandName, AltOpponentHandId,
                AltOpponentSetName, AltOpponentSetId,
                AltOpponentExtraName, AltOpponentExtraId);
        }

        // ---------------------------------------------------------------------
        // 8. Budget determinism
        // ---------------------------------------------------------------------
        static void BudgetDeterminismIdenticalProjection()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);
            object limits = CreateSearchLimits(maxStrategicDepth: 4, maxNodes: 96, beamWidth: 12, maxWallMs: 500);

            string first = MiniJSON.Json.Serialize(ProjectGraph(BuildAndSearch(scenario, limits)));
            string second = MiniJSON.Json.Serialize(ProjectGraph(BuildAndSearch(scenario, limits)));
            AssertEqual(first, second, "projected lines/pruning reasons must be byte-identical");
        }

        // ---------------------------------------------------------------------
        // 9. Fallback — forced graph failure leaves one-step request valid
        // ---------------------------------------------------------------------
        static void FallbackGraphFailureLeavesOneStepRequestValid()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);

            string requestBefore = LlmBrokerProtocol.SerializeDecisionRequest(scenario.Snapshot);
            AssertTrue(requestBefore.Contains("legal_actions"), "one-step request has legal_actions");
            AssertTrue(requestBefore.Contains("schema_version"), "one-step request has schema_version");

            object self = ProjectSelfResources(scenario);
            object limits = CreateSearchLimits(4, 96, 12, 500);

            // Fail-closed audit wrapper must catch builder exceptions (Slices 0–2A are audit-only).
            Type auditType = RequireType("LlmPlanningSearchAudit");
            MethodInfo tryBuild = FindTryBuildWithFactory(auditType);
            AssertNotNull(tryBuild,
                "LlmPlanningSearchAudit.TryBuild must accept an injectable graph factory "
                + "so tests can force a throw");

            ParameterInfo[] tryParams = tryBuild.GetParameters();
            object throwingFactory = CreateThrowingGraphFactory(tryParams[tryParams.Length - 1].ParameterType);

            object auditResult;
            try
            {
                object[] args = new object[tryParams.Length];
                for (int i = 0; i < tryParams.Length; i++)
                {
                    Type pt = tryParams[i].ParameterType;
                    if (pt == typeof(DecisionSnapshot) || pt.IsAssignableFrom(typeof(DecisionSnapshot)))
                    {
                        args[i] = scenario.Snapshot;
                    }
                    else if (self != null && pt.IsInstanceOfType(self))
                    {
                        args[i] = self;
                    }
                    else if (limits != null && pt.IsInstanceOfType(limits))
                    {
                        args[i] = limits;
                    }
                    else if (typeof(Delegate).IsAssignableFrom(pt) || pt.IsInterface
                        || pt == typeof(object)
                        || pt.Name.IndexOf("Factory", StringComparison.OrdinalIgnoreCase) >= 0
                        || pt.Name.IndexOf("Builder", StringComparison.OrdinalIgnoreCase) >= 0
                        || pt.IsInstanceOfType(throwingFactory)
                        || pt.IsAssignableFrom(throwingFactory.GetType()))
                    {
                        args[i] = throwingFactory;
                    }
                    else if (pt == typeof(LlmPublicVisibilityMode))
                    {
                        args[i] = scenario.FaceDomain;
                    }
                    else
                    {
                        args[i] = null;
                    }
                }
                auditResult = tryBuild.Invoke(null, args);
            }
            catch (TargetInvocationException ex)
            {
                // Outer TryBuild must not rethrow builder failures — fail closed instead.
                throw new Exception(
                    "LlmPlanningSearchAudit.TryBuild must catch graph factory exceptions "
                    + "and return a fail-closed audit result; got: "
                    + (ex.InnerException != null ? ex.InnerException.Message : ex.Message),
                    ex.InnerException ?? ex);
            }

            AssertNotNull(auditResult, "TryBuild audit result");
            bool success = GetBoolPropFlexible(auditResult, "Success", "IsSuccess", "Ok");
            AssertEqual(false, success, "forced graph failure must report Success=false");

            string error = GetStringProp(auditResult, "Error")
                ?? GetStringProp(auditResult, "FailureReason")
                ?? GetStringProp(auditResult, "Reason")
                ?? string.Empty;
            AssertTrue(
                error.IndexOf(ForcedGraphFailureMessage, StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("forced", StringComparison.OrdinalIgnoreCase) >= 0
                || error.Length > 0,
                "audit result must record the forced failure reason");

            string requestAfter = LlmBrokerProtocol.SerializeDecisionRequest(scenario.Snapshot);
            AssertEqual(requestBefore, requestAfter,
                "one-step serialized request must remain byte-identical after forced graph failure");
            AssertTrue(requestAfter.Contains("legal_actions"),
                "one-step request still has legal_actions after failure");
        }

        // ---------------------------------------------------------------------
        // 10. No-candidate controls — explicit exclusion reasons required
        // ---------------------------------------------------------------------
        static void NoCandidateControls()
        {
            // 10a empty matching Extra Deck
            GirochinCaveScenario emptyEd = CreateGirochinCaveScenario(
                includeRank4Extra: false,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);
            object graphEmpty = BuildAndSearch(emptyEd);
            AssertFalse(AnyLineMentionsRank4(graphEmpty),
                "empty Extra Deck must not invent Rank 4 edge");
            AssertTrue(
                HasExplicitNoCandidateReason(graphEmpty, emptyEd.FlipRootActionId, "empty_extra_deck"),
                "empty matching Extra Deck must emit an explicit exclusion/pruning/no-candidate reason");

            // 10b insufficient materials
            GirochinCaveScenario oneMaterial = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField,
                includeCaveDragon: false);
            object graphOne = BuildAndSearch(oneMaterial);
            AssertFalse(AnyLineMentionsRank4(graphOne),
                "insufficient materials must not invent Rank 4 edge");
            AssertTrue(
                HasExplicitNoCandidateReason(graphOne, -1, "insufficient_materials"),
                "insufficient materials must emit an explicit exclusion/pruning/no-candidate reason");

            // 10c missing metadata (catalog strips level/rank)
            GirochinCaveScenario noMeta = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField,
                stripMaterialMetadata: true);
            object graphNoMeta = BuildAndSearch(noMeta);
            AssertFalse(AnyLineMentionsRank4(graphNoMeta),
                "missing material metadata must not invent Rank 4 edge");
            AssertTrue(
                HasExplicitNoCandidateReason(graphNoMeta, noMeta.FlipRootActionId, "missing_metadata"),
                "missing material metadata must emit an explicit exclusion/pruning/no-candidate reason");
        }

        // ---------------------------------------------------------------------
        // 11. Current Xyz distinction
        // ---------------------------------------------------------------------
        static void CurrentXyzDistinctionNotDuplicatedAsFutureOnly()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField,
                includeCurrentSummonSp: true);
            object graph = BuildAndSearch(scenario);

            AssertRootCoverage(graph, scenario.Snapshot, "current SummonSp root coverage");
            int summonSpId = scenario.CurrentSummonSpActionId;
            AssertTrue(summonSpId >= 0, "fixture must include current SummonSp root");

            object rootLine = FindDepthZeroRootLine(graph, summonSpId);
            AssertNotNull(rootLine, "current SummonSp must appear as a depth-zero root line");
            AssertEqual("engine_current", GetStringProp(rootLine, "Provenance"),
                "current SummonSp root provenance must be engine_current");
            AssertEqual(true, GetBoolProp(rootLine, "CommitEligible"),
                "current SummonSp root must be commit-eligible at depth zero");

            int futureCurrentLegalDuplicates = 0;
            foreach (object line in GetLines(graph))
            {
                if (GetIntProp(line, "RootActionId") == summonSpId)
                {
                    // Depth-zero root representation is allowed; deeper steps under the same root
                    // must not re-mark the already-current SummonSp as a future current_legal edge.
                    IEnumerable ownSteps = GetEnumerableProp(line, "Steps");
                    if (ownSteps == null)
                    {
                        continue;
                    }
                    int stepIndex = 0;
                    foreach (object step in ownSteps)
                    {
                        if (stepIndex == 0)
                        {
                            stepIndex++;
                            continue;
                        }
                        if (StepLooksLikeCurrentSummonSp(step) && GetBoolPropFlexible(step, "CurrentLegal"))
                        {
                            futureCurrentLegalDuplicates++;
                        }
                        stepIndex++;
                    }
                    continue;
                }

                IEnumerable steps = GetEnumerableProp(line, "Steps");
                if (steps == null)
                {
                    continue;
                }
                foreach (object step in steps)
                {
                    if (StepLooksLikeCurrentSummonSp(step) && GetBoolPropFlexible(step, "CurrentLegal"))
                    {
                        futureCurrentLegalDuplicates++;
                    }
                }
            }
            AssertEqual(0, futureCurrentLegalDuplicates,
                "no future edge may duplicate current SummonSp as current_legal");
        }

        // ---------------------------------------------------------------------
        // 12. Private-domain split — projector extraction, not handcrafted DTO
        // ---------------------------------------------------------------------
        static void PrivateDomainSplitSelfResourcesNotPublic()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);

            // Must extract via production projector over the fake engine query.
            object self = ProjectSelfResources(scenario);
            string selfJson = MiniJSON.Json.Serialize(ToSerializable(self));
            AssertTrue(
                selfJson.Contains(CaveDragonName) || selfJson.Contains(CaveDragonCardId.ToString()),
                "projector must extract own face-down Cave Dragon from engine query");
            AssertTrue(
                selfJson.Contains(Rank4XyzName) || selfJson.Contains(Rank4XyzCardId.ToString()),
                "projector must extract own Extra Deck Rank 4 from engine query");
            AssertTrue(
                selfJson.Contains(OwnHandName) || selfJson.Contains(OwnHandCardId.ToString()),
                "projector must extract own hand identity from engine query");
            AssertTrue(
                selfJson.Contains(OwnGyName) || selfJson.Contains(OwnGyCardId.ToString()),
                "projector must extract own GY identity from engine query");
            AssertTrue(
                selfJson.Contains(OwnBanishedName) || selfJson.Contains(OwnBanishedCardId.ToString()),
                "projector must extract own face-up banished identity from engine query");
            AssertNoLeak(selfJson,
                SentinelOpponentHandName, SentinelOpponentHandId,
                SentinelOpponentSetName, SentinelOpponentSetId,
                SentinelOpponentExtraName, SentinelOpponentExtraId);

            Dictionary<string, object> publicState =
                LlmDecisionLogSerializer.SerializePublicState(scenario.Snapshot.PublicState);
            string publicJson = MiniJSON.Json.Serialize(publicState);
            AssertFalse(publicJson.Contains(CaveDragonName),
                "public_state must not expose own face-down Cave Dragon name");
            AssertFalse(publicJson.Contains(CaveDragonCardId.ToString()),
                "public_state must not expose own face-down Cave Dragon id");
            AssertFalse(publicJson.Contains(Rank4XyzName),
                "public_state must not expose Extra Deck card name");
            AssertFalse(publicJson.Contains(Rank4XyzCardId.ToString()),
                "public_state must not expose Extra Deck card id");

            LlmDuelHistoryTracker historyTracker = new LlmDuelHistoryTracker(null);
            string historyJson = historyTracker.SerializeRequestProjection(scenario.Snapshot.ControlledPlayer);
            AssertFalse(historyJson.Contains(Rank4XyzName),
                "duel_history must not expose Extra Deck");
            AssertFalse(historyJson.Contains(CaveDragonName),
                "duel_history must not expose unrevealed own face-down identity");

            string requestJson = LlmBrokerProtocol.SerializeDecisionRequest(scenario.Snapshot);
            AssertFalse(requestJson.Contains(Rank4XyzName),
                "current decision request must not leak Extra Deck via public fields");
            AssertFalse(requestJson.Contains("\"self_resources\""),
                "Slice 0–2A keep self_resources off /decide until schema v5 (Slice 3)");
        }

        // ---------------------------------------------------------------------
        // 13. Unsupported prompt boundary
        // ---------------------------------------------------------------------
        static void UnsupportedPromptBoundary()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: false,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField,
                includeCaveDragon: false);
            scenario.Snapshot.LegalActions.Clear();
            scenario.Snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.ListIndex,
                Player = ControlledPlayer,
                Index = 0,
                ListSelectMin = 1,
                ListSelectMax = 3,
                ListIsMultiMode = 1,
                ActionLabel = "Multi-select materials",
                IsMechanical = false,
                StrategicRole = "list_select",
            });
            scenario.Snapshot.StrategicActionCount = 1;
            scenario.FlipRootActionId = -1;
            scenario.BattleRootActionId = -1;
            scenario.EndRootActionId = -1;

            object graph = BuildAndSearch(scenario);
            bool sawBoundary = false;
            foreach (object line in GetLines(graph))
            {
                string boundary = GetStringProp(line, "Boundary") ?? string.Empty;
                if (boundary.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0
                    || boundary.IndexOf("multi_select", StringComparison.OrdinalIgnoreCase) >= 0
                    || boundary == "unsupported_multi_select")
                {
                    sawBoundary = true;
                }
                if ((GetStringProp(line, "Provenance") ?? string.Empty) != "engine_current")
                {
                    AssertEqual(false, GetBoolProp(line, "CommitEligible"),
                        "unsupported multi-select must not invent commit-eligible continuations");
                }
            }
            AssertTrue(sawBoundary,
                "multi-select / unknown dialog surfaces must become boundary nodes, not guessed commits");
        }

        // ---------------------------------------------------------------------
        // 14. Stale generation
        // ---------------------------------------------------------------------
        static void StaleGenerationDiscardsOldGraph()
        {
            GirochinCaveScenario scenario = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);
            object graphOld = BuildAndSearch(scenario);
            string oldSearchId = GetStringProp(graphOld, "SearchId");
            AssertNotNull(oldSearchId, "search_id required");
            AssertEqual((long)FixtureRunEffectSeq, Convert.ToInt64(GetProp(graphOld, "RootRunEffectSeq")),
                "root_run_effect_seq on old graph");

            scenario.Snapshot.RunEffectSeq = FixtureRunEffectSeq + 1;
            object graphNew = BuildAndSearch(scenario);
            string newSearchId = GetStringProp(graphNew, "SearchId");
            AssertNotNull(newSearchId, "new search_id required");
            AssertTrue(oldSearchId != newSearchId, "new run_effect_seq must create a new search generation");
            AssertEqual((long)(FixtureRunEffectSeq + 1), Convert.ToInt64(GetProp(graphNew, "RootRunEffectSeq")),
                "root_run_effect_seq on new graph");

            LlmBrokerDecisionResponse staleResponse = new LlmBrokerDecisionResponse()
            {
                RunEffectSeq = FixtureRunEffectSeq,
                ActionId = scenario.FlipRootActionId,
                Reason = "setup for rank 4",
                Confidence = 0.9,
            };
            LlmBrokerValidationResult validation = LlmBrokerProtocol.ValidateResponse(
                scenario.Snapshot,
                staleResponse);
            AssertEqual(false, validation.IsValid, "stale seq must fail validation");
            AssertEqual("stale_run_effect_seq", validation.Error, "stale_run_effect_seq error");
        }

        // ---------------------------------------------------------------------
        // 15. Face-domain declaration
        // ---------------------------------------------------------------------
        static void FaceDomainDeclarationRuntimeVersusFixture()
        {
            GirochinCaveScenario runtime = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.RuntimeDllField);
            AssertEqual(LlmPublicVisibilityMode.RuntimeDllField, runtime.FaceDomain, "runtime domain");
            AssertEqual(RuntimeFaceUp, runtime.GirochinFace, "runtime face-up domain 0/1");
            AssertEqual(RuntimeFaceDown, runtime.CaveFace, "runtime face-down domain 0/1");

            object selfRuntime = ProjectSelfResources(runtime);
            AssertFaceDomainDeclared(selfRuntime, LlmPublicVisibilityMode.RuntimeDllField);

            GirochinCaveScenario fixture = CreateGirochinCaveScenario(
                includeRank4Extra: true,
                faceDomain: LlmPublicVisibilityMode.FixtureValidated);
            AssertEqual(LlmPublicVisibilityMode.FixtureValidated, fixture.FaceDomain, "fixture domain");
            AssertEqual(FixtureFaceUp, fixture.GirochinFace, "fixture face-up domain 8/4");
            AssertEqual(FixtureFaceDown, fixture.CaveFace, "fixture face-down domain 8/4");

            object selfFixture = ProjectSelfResources(fixture);
            AssertFaceDomainDeclared(selfFixture, LlmPublicVisibilityMode.FixtureValidated);

            string runtimeJson = MiniJSON.Json.Serialize(ToSerializable(selfRuntime));
            string fixtureJson = MiniJSON.Json.Serialize(ToSerializable(selfFixture));
            AssertTrue(
                runtimeJson.IndexOf("RuntimeDllField", StringComparison.OrdinalIgnoreCase) >= 0
                || runtimeJson.IndexOf("runtime", StringComparison.OrdinalIgnoreCase) >= 0
                || GetStringProp(selfRuntime, "FaceDomain") != null
                || GetStringProp(selfRuntime, "VisibilityMode") != null,
                "runtime self_resources must declare face domain explicitly");
            AssertTrue(
                fixtureJson.IndexOf("FixtureValidated", StringComparison.OrdinalIgnoreCase) >= 0
                || fixtureJson.IndexOf("fixture", StringComparison.OrdinalIgnoreCase) >= 0
                || GetStringProp(selfFixture, "FaceDomain") != null
                || GetStringProp(selfFixture, "VisibilityMode") != null,
                "fixture self_resources must declare face domain explicitly");
        }

        // =====================================================================
        // Scenario + fake engine query
        // =====================================================================

        class GirochinCaveScenario
        {
            public DecisionSnapshot Snapshot;
            public Llm005EngineQuery Query;
            public Llm005CardCatalog Catalog;
            public LlmPublicVisibilityMode FaceDomain;
            public int FlipRootActionId;
            public int BattleRootActionId;
            public int EndRootActionId;
            public int CurrentSummonSpActionId = -1;
            public LlmCardMetadata GirochinMeta;
            public LlmCardMetadata CaveMeta;
            public LlmCardMetadata Rank4Meta;
            public int GirochinFace;
            public int CaveFace;
            public bool IncludeCaveDragon = true;
            public bool IncludeRank4Extra = true;
            public bool StripMaterialMetadata;
        }

        /// <summary>
        /// ILegalActionQuery-compatible omniscient fixture source.
        /// Controlled/public fields are identical across hidden pairs; opponent-hidden
        /// identities differ only in query cards that the projector must ignore.
        /// </summary>
        sealed class Llm005EngineQuery : ILegalActionQuery
        {
            public readonly Dictionary<string, int> CardNums = new Dictionary<string, int>();
            public readonly Dictionary<string, uint> CommandMasks = new Dictionary<string, uint>();
            public readonly Dictionary<string, int> CardUniqueIds = new Dictionary<string, int>();
            public readonly Dictionary<string, int> CardFaces = new Dictionary<string, int>();
            public readonly Dictionary<string, int> HandCardOpen = new Dictionary<string, int>();
            public readonly Dictionary<int, int> CardIdsByUniqueId = new Dictionary<int, int>();
            public readonly Dictionary<int, int> LifePoints = new Dictionary<int, int>();
            public uint MovablePhaseMask;
            public int CurrentPhase = (int)DuelPhase.Main1;
            public int CurrentStep;
            public int TurnNum = 3;
            public int TurnPlayer = ControlledPlayer;

            public int GetCardNum(int player, int position)
            {
                return Get(CardNums, Key(player, position));
            }

            public uint GetCommandMask(int player, int position, int index)
            {
                return GetU(CommandMasks, Key(player, position, index));
            }

            public int GetCardFace(int player, int position, int index)
            {
                return Get(CardFaces, Key(player, position, index));
            }

            public int GetCardIdByUniqueId(int uniqueId)
            {
                return Get(CardIdsByUniqueId, uniqueId);
            }

            public int GetCardUniqueId(int player, int position, int index)
            {
                return Get(CardUniqueIds, Key(player, position, index));
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
                return 0;
            }

            public int GetDialogCanYesNoSkip()
            {
                return 0;
            }

            public int GetDialogSelectItemEnable(int index)
            {
                return 0;
            }

            public int GetDialogSelectItemNum()
            {
                return 0;
            }

            public int GetDialogSelectItemTextId(int index)
            {
                return 0;
            }

            public int GetListItemAttribute(int index)
            {
                return 0;
            }

            public int GetListItemFrom(int index)
            {
                return 0;
            }

            public int GetListItemId(int index)
            {
                return 0;
            }

            public int GetListItemMax()
            {
                return 0;
            }

            public int GetListItemMsg(int index)
            {
                return 0;
            }

            public int GetListItemTargetUniqueId(int index)
            {
                return 0;
            }

            public int GetListItemUniqueId(int index)
            {
                return 0;
            }

            public int GetListSelectMax()
            {
                return 0;
            }

            public int GetListSelectMin()
            {
                return 0;
            }

            public int GetListIsMultiMode()
            {
                return 0;
            }

            public int GetSummoningMonsterUniqueId()
            {
                return 0;
            }

            public int GetSummonPositionMask()
            {
                return 0;
            }

            public void PlaceCard(int player, int position, int index, int uniqueId, int cardId, int face)
            {
                string numKey = Key(player, position);
                int current = Get(CardNums, numKey);
                if (index >= current)
                {
                    CardNums[numKey] = index + 1;
                }
                // Engine cardNum is often count with indexes 0..count inclusive in extractor;
                // store at least index+1 cards.
                if (!CardNums.ContainsKey(numKey) || CardNums[numKey] <= index)
                {
                    CardNums[numKey] = index + 1;
                }
                CardUniqueIds[Key(player, position, index)] = uniqueId;
                CardFaces[Key(player, position, index)] = face;
                CardIdsByUniqueId[uniqueId] = cardId;
            }

            static int Get(Dictionary<string, int> map, string key)
            {
                int value;
                return map.TryGetValue(key, out value) ? value : 0;
            }

            static int Get(Dictionary<int, int> map, int key)
            {
                int value;
                return map.TryGetValue(key, out value) ? value : 0;
            }

            static uint GetU(Dictionary<string, uint> map, string key)
            {
                uint value;
                return map.TryGetValue(key, out value) ? value : 0u;
            }

            static string Key(params int[] parts)
            {
                return string.Join(",", parts.Select(p => p.ToString()).ToArray());
            }
        }

        sealed class Llm005CardCatalog : ILlmCardCatalog
        {
            readonly Dictionary<int, LlmCardMetadata> _cards = new Dictionary<int, LlmCardMetadata>();
            public bool StripMaterialMetadata;

            public void Add(LlmCardMetadata card)
            {
                if (card == null)
                {
                    return;
                }
                _cards[card.CardId] = card;
            }

            public LlmCardMetadata GetCard(int cardId)
            {
                LlmCardMetadata card;
                if (!_cards.TryGetValue(cardId, out card) || card == null)
                {
                    return null;
                }
                if (!StripMaterialMetadata)
                {
                    return card;
                }
                // Missing material metadata control: level/rank unknown to rules.
                return new LlmCardMetadata()
                {
                    CardId = card.CardId,
                    Name = card.Name,
                    Text = card.Text,
                    Kind = card.Kind,
                    Attribute = card.Attribute,
                    Level = 0,
                    Atk = card.Atk,
                    Def = card.Def,
                    Scale = card.Scale,
                };
            }
        }

        static GirochinCaveScenario CreateGirochinCaveScenario(
            bool includeRank4Extra,
            LlmPublicVisibilityMode faceDomain,
            bool includeCaveDragon = true,
            bool includeCurrentSummonSp = false,
            bool stripMaterialMetadata = false,
            int opponentHiddenHandId = SentinelOpponentHandId,
            int opponentHiddenSetId = SentinelOpponentSetId,
            int opponentHiddenExtraId = SentinelOpponentExtraId)
        {
            bool runtime = faceDomain == LlmPublicVisibilityMode.RuntimeDllField
                || faceDomain == LlmPublicVisibilityMode.RuntimeSafe;
            int faceUp = runtime ? RuntimeFaceUp : FixtureFaceUp;
            int faceDown = runtime ? RuntimeFaceDown : FixtureFaceDown;

            LlmCardMetadata girochin = new LlmCardMetadata()
            {
                CardId = GirochinCardId,
                Name = GirochinName,
                Text = "A Level 4 Insect monster.",
                Kind = "Monster",
                Attribute = "EARTH",
                Level = 4,
                Atk = 1700,
                Def = 1000,
            };
            LlmCardMetadata cave = new LlmCardMetadata()
            {
                CardId = CaveDragonCardId,
                Name = CaveDragonName,
                Text = "A Level 4 Dragon with 2000 DEF.",
                Kind = "Monster",
                Attribute = "WIND",
                Level = 4,
                Atk = 1300,
                Def = 2000,
            };
            LlmCardMetadata rank4 = new LlmCardMetadata()
            {
                CardId = Rank4XyzCardId,
                Name = Rank4XyzName,
                Text = "2 Level 4 monsters",
                Kind = "Monster",
                Attribute = "LIGHT",
                Level = 4,
                Atk = 2500,
                Def = 2000,
            };
            LlmCardMetadata ownHand = new LlmCardMetadata()
            {
                CardId = OwnHandCardId,
                Name = OwnHandName,
                Kind = "Monster",
                Level = 7,
                Atk = 2500,
                Def = 2100,
            };
            LlmCardMetadata ownGy = new LlmCardMetadata()
            {
                CardId = OwnGyCardId,
                Name = OwnGyName,
                Kind = "Spell",
            };
            LlmCardMetadata ownBanished = new LlmCardMetadata()
            {
                CardId = OwnBanishedCardId,
                Name = OwnBanishedName,
                Kind = "Spell",
            };

            Llm005CardCatalog catalog = new Llm005CardCatalog()
            {
                StripMaterialMetadata = stripMaterialMetadata,
            };
            catalog.Add(girochin);
            catalog.Add(cave);
            catalog.Add(rank4);
            catalog.Add(ownHand);
            catalog.Add(ownGy);
            catalog.Add(ownBanished);
            catalog.Add(new LlmCardMetadata()
            {
                CardId = opponentHiddenHandId,
                Name = opponentHiddenHandId == SentinelOpponentHandId
                    ? SentinelOpponentHandName
                    : AltOpponentHandName,
            });
            catalog.Add(new LlmCardMetadata()
            {
                CardId = opponentHiddenSetId,
                Name = opponentHiddenSetId == SentinelOpponentSetId
                    ? SentinelOpponentSetName
                    : AltOpponentSetName,
            });
            catalog.Add(new LlmCardMetadata()
            {
                CardId = opponentHiddenExtraId,
                Name = opponentHiddenExtraId == SentinelOpponentExtraId
                    ? SentinelOpponentExtraName
                    : AltOpponentExtraName,
            });

            Llm005EngineQuery query = new Llm005EngineQuery()
            {
                TurnPlayer = ControlledPlayer,
                TurnNum = 3,
                CurrentPhase = (int)DuelPhase.Main1,
                MovablePhaseMask = (uint)((1 << (int)DuelPhase.Battle) | (1 << (int)DuelPhase.End)),
            };
            query.LifePoints[ControlledPlayer] = 8000;
            query.LifePoints[OpponentPlayer] = 8000;

            // Controlled self resources (private + public).
            query.PlaceCard(ControlledPlayer, MonsterZone0, 0, 1001, GirochinCardId, faceUp);
            if (includeCaveDragon)
            {
                query.PlaceCard(ControlledPlayer, MonsterZone1, 0, 1002, CaveDragonCardId, faceDown);
                query.CommandMasks[Key(ControlledPlayer, MonsterZone1, 0)] =
                    (uint)(1 << (int)DuelCommandType.Reverse);
            }
            query.PlaceCard(ControlledPlayer, PosHand, 0, 1101, OwnHandCardId, faceUp);
            query.HandCardOpen[Key(ControlledPlayer, 0)] = 1;
            query.PlaceCard(ControlledPlayer, PosGrave, 0, 1201, OwnGyCardId, faceUp);
            query.PlaceCard(ControlledPlayer, PosBanished, 0, 1301, OwnBanishedCardId, faceUp);
            if (includeRank4Extra)
            {
                query.PlaceCard(ControlledPlayer, PosExtra, 0, 2001, Rank4XyzCardId, faceUp);
            }

            // Opponent-hidden (omniscient query only — must not affect Layer A / self projection).
            query.PlaceCard(OpponentPlayer, PosHand, 0, 9001, opponentHiddenHandId, faceDown);
            query.HandCardOpen[Key(OpponentPlayer, 0)] = 0;
            query.PlaceCard(OpponentPlayer, OpponentSetZone, 0, 9002, opponentHiddenSetId, faceDown);
            query.PlaceCard(OpponentPlayer, PosExtra, 0, 9003, opponentHiddenExtraId, faceDown);

            // Snapshot legal actions + public_state (public omits face-down identities).
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = FixtureRunEffectSeq,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = ControlledPlayer,
                ControlledPlayer = ControlledPlayer,
                Turn = 3,
                TurnPlayer = ControlledPlayer,
                CurrentPhase = (int)DuelPhase.Main1,
                CurrentStep = 0,
                IsStrategicWindow = true,
                StrategicWindowReason = "strategic_actions",
            };
            // Keep turn_memory free of any opponent-hidden identity.
            snapshot.TurnMemory.PhasePlan = "develop_main1";

            PublicPlayerState selfPublic = new PublicPlayerState()
            {
                Player = ControlledPlayer,
                LifePoints = 8000,
            };
            selfPublic.Positions.Add(new PublicPositionState() { Position = MonsterZone0, Count = 1 });
            if (includeCaveDragon)
            {
                selfPublic.Positions.Add(new PublicPositionState() { Position = MonsterZone1, Count = 1 });
            }
            selfPublic.Positions.Add(new PublicPositionState() { Position = PosHand, Count = 1 });
            selfPublic.Positions.Add(new PublicPositionState() { Position = PosGrave, Count = 1 });
            selfPublic.Positions.Add(new PublicPositionState() { Position = PosBanished, Count = 1 });
            if (includeRank4Extra)
            {
                selfPublic.Positions.Add(new PublicPositionState() { Position = PosExtra, Count = 1 });
            }
            selfPublic.KnownCards.Add(new PublicKnownCard()
            {
                Player = ControlledPlayer,
                Position = MonsterZone0,
                Index = 0,
                CardUniqueId = 1001,
                CardId = GirochinCardId,
                Face = faceUp,
                Card = girochin,
            });
            // Face-down Cave, Extra Deck identities, private hand stay out of public known cards
            // except GY (public) and face-up banished (public when face-up).
            selfPublic.KnownCards.Add(new PublicKnownCard()
            {
                Player = ControlledPlayer,
                Position = PosGrave,
                Index = 0,
                CardUniqueId = 1201,
                CardId = OwnGyCardId,
                Face = faceUp,
                Card = ownGy,
            });
            selfPublic.KnownCards.Add(new PublicKnownCard()
            {
                Player = ControlledPlayer,
                Position = PosBanished,
                Index = 0,
                CardUniqueId = 1301,
                CardId = OwnBanishedCardId,
                Face = faceUp,
                Card = ownBanished,
            });

            PublicPlayerState oppPublic = new PublicPlayerState()
            {
                Player = OpponentPlayer,
                LifePoints = 8000,
            };
            oppPublic.Positions.Add(new PublicPositionState() { Position = PosHand, Count = 1 });
            oppPublic.Positions.Add(new PublicPositionState() { Position = OpponentSetZone, Count = 1 });
            oppPublic.Positions.Add(new PublicPositionState() { Position = PosExtra, Count = 1 });
            // No opponent-hidden identities in public known cards.

            snapshot.PublicState.Players.Add(oppPublic);
            snapshot.PublicState.Players.Add(selfPublic);

            int nextId = 0;
            int battleId = nextId++;
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = battleId,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
                ActionLabel = "Enter Battle Phase",
                IsMechanical = false,
                StrategicRole = "phase_transition",
            });
            int endId = nextId++;
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = endId,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
                ActionLabel = "End Phase",
                IsMechanical = false,
                StrategicRole = "phase_transition",
            });
            int flipId = -1;
            if (includeCaveDragon)
            {
                flipId = nextId++;
                snapshot.LegalActions.Add(new LegalAction()
                {
                    ActionId = flipId,
                    Kind = LegalActionKind.Command,
                    Player = ControlledPlayer,
                    Position = MonsterZone1,
                    Index = 0,
                    Command = DuelCommandType.Reverse,
                    CardUniqueId = 1002,
                    CardId = CaveDragonCardId,
                    Card = cave,
                    ActionLabel = "Flip Summon The Dragon Dwelling in the Cave",
                    IsMechanical = false,
                    StrategicRole = "flip_summon",
                    ConsequenceHint = "changes DEF wall into ATK position",
                });
            }

            int summonSpId = -1;
            if (includeCurrentSummonSp)
            {
                summonSpId = nextId++;
                snapshot.LegalActions.Add(new LegalAction()
                {
                    ActionId = summonSpId,
                    Kind = LegalActionKind.Command,
                    Player = ControlledPlayer,
                    Position = PosExtra,
                    Index = 0,
                    Command = DuelCommandType.SummonSp,
                    CardUniqueId = 2001,
                    CardId = Rank4XyzCardId,
                    Card = rank4,
                    ActionLabel = "Xyz Summon Number 39: Utopia",
                    IsMechanical = false,
                    StrategicRole = "extra_deck_summon",
                });
            }

            snapshot.StrategicActionCount = snapshot.LegalActions.Count;
            snapshot.MechanicalActionCount = 0;

            return new GirochinCaveScenario()
            {
                Snapshot = snapshot,
                Query = query,
                Catalog = catalog,
                FaceDomain = faceDomain,
                FlipRootActionId = flipId,
                BattleRootActionId = battleId,
                EndRootActionId = endId,
                CurrentSummonSpActionId = summonSpId,
                GirochinMeta = girochin,
                CaveMeta = cave,
                Rank4Meta = rank4,
                GirochinFace = faceUp,
                CaveFace = faceDown,
                IncludeCaveDragon = includeCaveDragon,
                IncludeRank4Extra = includeRank4Extra,
                StripMaterialMetadata = stripMaterialMetadata,
            };
        }

        // =====================================================================
        // Production API binding
        // =====================================================================

        static object BuildAndSearch(GirochinCaveScenario scenario)
        {
            return BuildAndSearch(scenario, CreateSearchLimits(4, 96, 12, 500));
        }

        static object BuildAndSearch(GirochinCaveScenario scenario, object limits)
        {
            // Graph input must come from the production projector over the engine query.
            object self = ProjectSelfResources(scenario);

            Type graphType = RequireType("LlmTacticalAffordanceGraph");
            MethodInfo build = graphType.GetMethod(
                "Build",
                BindingFlags.Public | BindingFlags.Static);
            AssertNotNull(build, "LlmTacticalAffordanceGraph.Build static method");

            object graph;
            try
            {
                ParameterInfo[] ps = build.GetParameters();
                if (ps.Length == 3)
                {
                    graph = build.Invoke(null, new object[] { scenario.Snapshot, self, limits });
                }
                else if (ps.Length == 2)
                {
                    graph = build.Invoke(null, new object[] { scenario.Snapshot, limits });
                }
                else if (ps.Length == 4)
                {
                    // Optional face-domain / catalog overload.
                    graph = build.Invoke(null, new object[]
                    {
                        scenario.Snapshot, self, limits, scenario.FaceDomain
                    });
                }
                else
                {
                    throw new Exception(
                        "LlmTacticalAffordanceGraph.Build unexpected arity " + ps.Length
                        + "; expected (DecisionSnapshot, LlmSelfResources, LlmSearchLimits)");
                }
            }
            catch (TargetInvocationException ex)
            {
                throw new Exception(
                    "LlmTacticalAffordanceGraph.Build failed: "
                    + (ex.InnerException != null ? ex.InnerException.Message : ex.Message),
                    ex.InnerException ?? ex);
            }
            AssertNotNull(graph, "Build returned null graph");

            Type searchType = TryGetType("LlmBoundedLineSearch");
            if (searchType != null)
            {
                MethodInfo search = searchType.GetMethod(
                    "Search",
                    BindingFlags.Public | BindingFlags.Static);
                if (search != null)
                {
                    try
                    {
                        ParameterInfo[] ps = search.GetParameters();
                        object expanded = ps.Length >= 2
                            ? search.Invoke(null, new object[] { graph, limits })
                            : search.Invoke(null, new object[] { graph });
                        if (expanded != null)
                        {
                            graph = expanded;
                        }
                    }
                    catch (TargetInvocationException ex)
                    {
                        throw new Exception(
                            "LlmBoundedLineSearch.Search failed: "
                            + (ex.InnerException != null ? ex.InnerException.Message : ex.Message),
                            ex.InnerException ?? ex);
                    }
                }
            }

            return graph;
        }

        static object ProjectGraph(object graph)
        {
            Type projType = RequireType("LlmSearchProjection");
            MethodInfo project = projType.GetMethod(
                "Project",
                BindingFlags.Public | BindingFlags.Static);
            AssertNotNull(project, "LlmSearchProjection.Project static method");
            try
            {
                object result = project.Invoke(null, new object[] { graph });
                AssertNotNull(result, "LlmSearchProjection.Project returned null");
                // Wall-clock elapsed_ms is not part of hidden-info or structural identity.
                ZeroCoverageElapsedMs(result);
                return result;
            }
            catch (TargetInvocationException ex)
            {
                throw new Exception(
                    "LlmSearchProjection.Project failed: "
                    + (ex.InnerException != null ? ex.InnerException.Message : ex.Message),
                    ex.InnerException ?? ex);
            }
        }

        static void ZeroCoverageElapsedMs(object projection)
        {
            Dictionary<string, object> root = projection as Dictionary<string, object>;
            if (root == null)
            {
                return;
            }
            object coverageObj;
            if (!root.TryGetValue("coverage", out coverageObj))
            {
                return;
            }
            Dictionary<string, object> coverage = coverageObj as Dictionary<string, object>;
            if (coverage != null && coverage.ContainsKey("elapsed_ms"))
            {
                coverage["elapsed_ms"] = 0L;
            }
        }

        /// <summary>
        /// Always invokes production LlmSelfResourceProjection over the fake engine query.
        /// Handcrafted LlmSelfResources DTOs are forbidden as a green path.
        /// </summary>
        static object ProjectSelfResources(GirochinCaveScenario scenario)
        {
            Type projType = RequireType("LlmSelfResourceProjection");
            MethodInfo project = FindSelfResourceProjectMethod(projType);
            AssertNotNull(project,
                "LlmSelfResourceProjection.Project must accept ILegalActionQuery "
                + "(or DecisionSnapshot + ILegalActionQuery) — extraction, not handcrafted DTOs");

            try
            {
                object result = InvokeSelfResourceProject(project, scenario);
                AssertNotNull(result, "LlmSelfResourceProjection.Project returned null");
                return result;
            }
            catch (TargetInvocationException ex)
            {
                throw new Exception(
                    "LlmSelfResourceProjection.Project failed: "
                    + (ex.InnerException != null ? ex.InnerException.Message : ex.Message),
                    ex.InnerException ?? ex);
            }
        }

        static MethodInfo FindSelfResourceProjectMethod(Type projType)
        {
            MethodInfo[] methods = projType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Project" || m.Name == "ProjectFromQuery"
                    || m.Name == "ProjectFromEngine" || m.Name == "ProjectFromSnapshot")
                .ToArray();
            // Prefer methods that take ILegalActionQuery.
            foreach (MethodInfo m in methods)
            {
                if (m.GetParameters().Any(p => typeof(ILegalActionQuery).IsAssignableFrom(p.ParameterType)))
                {
                    return m;
                }
            }
            // Next: any Project method (will still fail arity checks if wrong).
            return methods.FirstOrDefault();
        }

        static object InvokeSelfResourceProject(MethodInfo project, GirochinCaveScenario scenario)
        {
            ParameterInfo[] ps = project.GetParameters();
            object[] args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                if (typeof(ILegalActionQuery).IsAssignableFrom(pt))
                {
                    args[i] = scenario.Query;
                }
                else if (pt == typeof(DecisionSnapshot) || pt.IsAssignableFrom(typeof(DecisionSnapshot)))
                {
                    args[i] = scenario.Snapshot;
                }
                else if (typeof(ILlmCardCatalog).IsAssignableFrom(pt))
                {
                    args[i] = scenario.Catalog;
                }
                else if (pt == typeof(int))
                {
                    // controlled player or pos count
                    string name = (ps[i].Name ?? string.Empty).ToLowerInvariant();
                    if (name.Contains("pos") || name.Contains("zone"))
                    {
                        args[i] = PosBanished;
                    }
                    else
                    {
                        args[i] = ControlledPlayer;
                    }
                }
                else if (pt == typeof(LlmPublicVisibilityMode) || pt.IsEnum)
                {
                    args[i] = scenario.FaceDomain;
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
            if (!ps.Any(p => typeof(ILegalActionQuery).IsAssignableFrom(p.ParameterType)))
            {
                throw new Exception(
                    "YGOMASTER-LLM-005 LlmSelfResourceProjection.Project must take ILegalActionQuery "
                    + "so fixtures prove engine-query extraction (got " + project.Name + " arity "
                    + ps.Length + ")");
            }
            return project.Invoke(null, args);
        }

        static object CreateSearchLimits(int maxStrategicDepth, int maxNodes, int beamWidth, int maxWallMs)
        {
            Type limitsType = RequireType("LlmSearchLimits");
            object limits = Activator.CreateInstance(limitsType);
            SetProp(limits, "MaxStrategicDepth", maxStrategicDepth);
            SetProp(limits, "MaxNodes", maxNodes);
            SetProp(limits, "BeamWidth", beamWidth);
            SetProp(limits, "MaxWallMs", maxWallMs);
            return limits;
        }

        static MethodInfo FindTryBuildWithFactory(Type auditType)
        {
            MethodInfo[] methods = auditType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "TryBuild" || m.Name == "TryAttach" || m.Name == "TryBuildAudit")
                .ToArray();
            foreach (MethodInfo m in methods)
            {
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length < 2)
                {
                    continue;
                }
                ParameterInfo last = ps[ps.Length - 1];
                Type pt = last.ParameterType;
                if (typeof(Delegate).IsAssignableFrom(pt)
                    || pt.IsInterface
                    || pt.Name.IndexOf("Factory", StringComparison.OrdinalIgnoreCase) >= 0
                    || pt.Name.IndexOf("Builder", StringComparison.OrdinalIgnoreCase) >= 0
                    || pt == typeof(object))
                {
                    return m;
                }
            }
            // Any TryBuild is better than nothing — caller will fail on missing factory param.
            return methods.FirstOrDefault();
        }

        static object CreateThrowingGraphFactory(Type parameterType)
        {
            if (parameterType == null)
            {
                return new ThrowingGraphFactoryAdapter();
            }

            if (typeof(Delegate).IsAssignableFrom(parameterType))
            {
                if (parameterType == typeof(Func<object>)
                    || parameterType.IsAssignableFrom(typeof(Func<object>)))
                {
                    Func<object> f = () =>
                    {
                        throw new InvalidOperationException(ForcedGraphFailureMessage);
                    };
                    return f;
                }
                if (parameterType.IsGenericType
                    && parameterType.GetGenericTypeDefinition() == typeof(Func<,,,>))
                {
                    Type[] ga = parameterType.GetGenericArguments();
                    MethodInfo open = typeof(Llm005Slice0Tests).GetMethod(
                        "ThrowingFactory3",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    MethodInfo closed = open.MakeGenericMethod(ga[0], ga[1], ga[2], ga[3]);
                    return Delegate.CreateDelegate(parameterType, closed);
                }
                if (parameterType.IsGenericType
                    && parameterType.GetGenericTypeDefinition() == typeof(Func<,,>))
                {
                    Type[] ga = parameterType.GetGenericArguments();
                    MethodInfo open = typeof(Llm005Slice0Tests).GetMethod(
                        "ThrowingFactory2",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    MethodInfo closed = open.MakeGenericMethod(ga[0], ga[1], ga[2]);
                    return Delegate.CreateDelegate(parameterType, closed);
                }
                if (parameterType.IsGenericType
                    && parameterType.GetGenericTypeDefinition() == typeof(Func<,>))
                {
                    Type[] ga = parameterType.GetGenericArguments();
                    MethodInfo open = typeof(Llm005Slice0Tests).GetMethod(
                        "ThrowingFactory1",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    MethodInfo closed = open.MakeGenericMethod(ga[0], ga[1]);
                    return Delegate.CreateDelegate(parameterType, closed);
                }
            }

            if (parameterType == typeof(object)
                || parameterType.IsInterface
                || parameterType.IsAssignableFrom(typeof(ThrowingGraphFactoryAdapter))
                || parameterType.Name.IndexOf("Factory", StringComparison.OrdinalIgnoreCase) >= 0
                || parameterType.Name.IndexOf("Builder", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new ThrowingGraphFactoryAdapter();
            }

            return new ThrowingGraphFactoryAdapter();
        }

        static TResult ThrowingFactory3<T1, T2, T3, TResult>(T1 a, T2 b, T3 c)
        {
            throw new InvalidOperationException(ForcedGraphFailureMessage);
        }

        static TResult ThrowingFactory2<T1, T2, TResult>(T1 a, T2 b)
        {
            throw new InvalidOperationException(ForcedGraphFailureMessage);
        }

        static TResult ThrowingFactory1<T1, TResult>(T1 a)
        {
            throw new InvalidOperationException(ForcedGraphFailureMessage);
        }

        /// <summary>
        /// Marker type production may accept as ILlmSearchGraphFactory / builder duck-type.
        /// </summary>
        public sealed class ThrowingGraphFactoryAdapter
        {
            public object Build(object snapshot, object self, object limits)
            {
                throw new InvalidOperationException(ForcedGraphFailureMessage);
            }

            public object Build()
            {
                throw new InvalidOperationException(ForcedGraphFailureMessage);
            }
        }

        // =====================================================================
        // Graph assertions
        // =====================================================================

        static void AssertRootCoverage(object graph, DecisionSnapshot snapshot, string context)
        {
            int strategic = CountStrategicRoots(snapshot);
            object coverage = GetProp(graph, "Coverage");
            AssertNotNull(coverage, context + ": coverage");
            AssertEqual(strategic, GetIntProp(coverage, "LegalRootActions"),
                context + ": legal_root_actions");
            AssertEqual(strategic, GetIntProp(coverage, "RepresentedRootActions"),
                context + ": represented_root_actions");

            HashSet<int> represented = new HashSet<int>();
            foreach (object line in GetLines(graph))
            {
                represented.Add(GetIntProp(line, "RootActionId"));
            }
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action.IsMechanical)
                {
                    continue;
                }
                AssertTrue(represented.Contains(action.ActionId),
                    context + ": missing root action_id " + action.ActionId
                    + " (" + action.ActionLabel + ")");
            }
        }

        static int CountStrategicRoots(DecisionSnapshot snapshot)
        {
            int n = 0;
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (!action.IsMechanical)
                {
                    n++;
                }
            }
            return n;
        }

        static IEnumerable GetLines(object graph)
        {
            object lines = GetProp(graph, "Lines");
            AssertNotNull(lines, "graph.Lines");
            IEnumerable enumerable = lines as IEnumerable;
            AssertNotNull(enumerable, "graph.Lines enumerable");
            return enumerable;
        }

        static object FindLineForRoot(object graph, int rootActionId)
        {
            if (rootActionId < 0)
            {
                return null;
            }
            object best = null;
            foreach (object line in GetLines(graph))
            {
                if (GetIntProp(line, "RootActionId") != rootActionId)
                {
                    continue;
                }
                if (best == null)
                {
                    best = line;
                }
                if (LineMentionsRank4(line))
                {
                    return line;
                }
            }
            return best;
        }

        static object FindDepthZeroRootLine(object graph, int rootActionId)
        {
            foreach (object line in GetLines(graph))
            {
                if (GetIntProp(line, "RootActionId") != rootActionId)
                {
                    continue;
                }
                string provenance = GetStringProp(line, "Provenance") ?? string.Empty;
                if (provenance == "engine_current")
                {
                    return line;
                }
            }
            // Fallback: first line for root (still must assert engine_current).
            return FindLineForRoot(graph, rootActionId);
        }

        static bool AnyLineMentionsRank4(object graph)
        {
            foreach (object line in GetLines(graph))
            {
                if (LineMentionsRank4(line))
                {
                    return true;
                }
            }
            return false;
        }

        static bool LineMentionsRank4(object line)
        {
            if (LineMentionsLabel(line, "Rank 4") || LineMentionsLabel(line, "Xyz"))
            {
                return true;
            }
            object unlocks = GetProp(line, "Unlocks");
            if (unlocks is IEnumerable)
            {
                foreach (object u in (IEnumerable)unlocks)
                {
                    string s = u != null ? u.ToString() : string.Empty;
                    if (s.IndexOf("Rank 4", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("Xyz", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static bool LineMentionsLabel(object line, string fragment)
        {
            IEnumerable steps = GetEnumerableProp(line, "Steps");
            if (steps == null)
            {
                return false;
            }
            foreach (object step in steps)
            {
                string label = GetStringProp(step, "Label") ?? string.Empty;
                if (label.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            string lineId = GetStringProp(line, "LineId") ?? string.Empty;
            return lineId.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool StepLooksLikeCurrentSummonSp(object step)
        {
            string label = GetStringProp(step, "Label") ?? string.Empty;
            return label.IndexOf("SummonSp", StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf(Rank4XyzName, StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf("Xyz Summon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool HasNonCommitEligibleFutureStep(object line)
        {
            IEnumerable steps = GetEnumerableProp(line, "Steps");
            if (steps == null)
            {
                return !GetBoolProp(line, "CommitEligible");
            }
            foreach (object step in steps)
            {
                string label = GetStringProp(step, "Label") ?? string.Empty;
                bool looksLikeXyz =
                    label.IndexOf("Rank 4", StringComparison.OrdinalIgnoreCase) >= 0
                    || label.IndexOf("Xyz", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!looksLikeXyz)
                {
                    continue;
                }
                PropertyInfo currentLegal = step.GetType().GetProperty("CurrentLegal");
                if (currentLegal != null && Convert.ToBoolean(currentLegal.GetValue(step, null)))
                {
                    return false;
                }
                PropertyInfo commit = step.GetType().GetProperty("CommitEligible");
                if (commit != null && Convert.ToBoolean(commit.GetValue(step, null)))
                {
                    return false;
                }
                return true;
            }
            return !GetBoolProp(line, "CommitEligible");
        }

        static bool HasEngineCurrentRoot(object graph, GirochinCaveScenario scenario)
        {
            foreach (object line in GetLines(graph))
            {
                int rootId = GetIntProp(line, "RootActionId");
                if (rootId == scenario.BattleRootActionId
                    || rootId == scenario.EndRootActionId
                    || rootId == scenario.FlipRootActionId)
                {
                    string p = GetStringProp(line, "Provenance") ?? string.Empty;
                    if (p == "engine_current" || GetBoolProp(line, "CommitEligible"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Requires an explicit exclusion/pruning/no-candidate reason. Absence of a Rank 4
        /// edge alone is insufficient.
        /// </summary>
        static bool HasExplicitNoCandidateReason(object graph, int rootActionId, string expectedFamily)
        {
            string blob = MiniJSON.Json.Serialize(ToSerializable(graph));
            bool familyHint =
                blob.IndexOf(expectedFamily, StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("no_candidate", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("no-candidate", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("exclusion", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("prun", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("empty", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("no matching", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("no_extra", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("metadata", StringComparison.OrdinalIgnoreCase) >= 0;

            // Structured coverage fields.
            object coverage = GetProp(graph, "Coverage");
            if (coverage != null)
            {
                foreach (string name in new[]
                {
                    "RootExclusions", "Exclusions", "PrunedRoots", "NoCandidateReasons",
                    "ExclusionReasons", "PruningReasons"
                })
                {
                    object excl = GetProp(coverage, name);
                    if (excl != null && !(excl is string && string.IsNullOrEmpty((string)excl)))
                    {
                        if (excl is IEnumerable && !(excl is string))
                        {
                            foreach (object item in (IEnumerable)excl)
                            {
                                if (item != null && item.ToString().Length > 0)
                                {
                                    return true;
                                }
                            }
                        }
                        else
                        {
                            return true;
                        }
                    }
                }
            }

            foreach (object line in GetLines(graph))
            {
                if (rootActionId >= 0 && GetIntProp(line, "RootActionId") != rootActionId)
                {
                    continue;
                }
                foreach (string name in new[]
                {
                    "ExclusionReason", "NoCandidateReason", "PruneReason", "Boundary", "Uncertainty", "Unlocks"
                })
                {
                    object value = GetProp(line, name);
                    if (value == null)
                    {
                        continue;
                    }
                    if (value is IEnumerable && !(value is string))
                    {
                        foreach (object u in (IEnumerable)value)
                        {
                            if (IsExplicitNoCandidateText(u != null ? u.ToString() : null, expectedFamily))
                            {
                                return true;
                            }
                        }
                    }
                    else if (IsExplicitNoCandidateText(value.ToString(), expectedFamily))
                    {
                        return true;
                    }
                }
            }

            return familyHint && GraphDeclaresStructuredReason(graph);
        }

        static bool GraphDeclaresStructuredReason(object graph)
        {
            // Require that the reason is not only an accidental substring of card text —
            // at least one known reason field / coverage bucket must be present.
            object coverage = GetProp(graph, "Coverage");
            if (coverage != null)
            {
                foreach (string name in new[]
                {
                    "RootExclusions", "Exclusions", "PrunedRoots", "NoCandidateReasons",
                    "ExclusionReasons", "PruningReasons", "BoundaryNodes"
                })
                {
                    if (GetProp(coverage, name) != null)
                    {
                        return true;
                    }
                }
            }
            foreach (object line in GetLines(graph))
            {
                if (GetProp(line, "ExclusionReason") != null
                    || GetProp(line, "NoCandidateReason") != null
                    || GetProp(line, "PruneReason") != null)
                {
                    return true;
                }
                string boundary = GetStringProp(line, "Boundary") ?? string.Empty;
                if (boundary.IndexOf("no_candidate", StringComparison.OrdinalIgnoreCase) >= 0
                    || boundary.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0
                    || boundary.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0
                    || boundary.IndexOf("empty", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        static bool IsExplicitNoCandidateText(string text, string expectedFamily)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(expectedFamily)
                && text.IndexOf(expectedFamily, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return text.IndexOf("no_candidate", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("no-candidate", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("exclusion", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("prun", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("empty extra", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("empty_extra", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("missing metadata", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("missing_metadata", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("no matching", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("no rank", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void AssertFaceDomainDeclared(object self, LlmPublicVisibilityMode expected)
        {
            object domain = GetProp(self, "FaceDomain");
            if (domain == null)
            {
                domain = GetProp(self, "VisibilityMode");
            }
            if (domain == null)
            {
                string name = GetStringProp(self, "FaceDomainName");
                AssertNotNull(name, "self_resources face domain declaration");
                AssertTrue(
                    name.IndexOf(expected.ToString(), StringComparison.OrdinalIgnoreCase) >= 0,
                    "face domain name should declare " + expected);
                return;
            }
            AssertEqual(expected.ToString(), domain.ToString(), "declared face domain");
        }

        static object ToSerializable(object value)
        {
            if (value == null)
            {
                return null;
            }
            if (value is string || value.GetType().IsPrimitive || value is decimal)
            {
                return value;
            }
            if (value is Enum)
            {
                return value.ToString();
            }
            if (value is IDictionary)
            {
                return value;
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
                    + " (Slice 0 planning contract; implement in YgoMasterServer/Llm/)");
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

        static string GetStringProp(object target, string name)
        {
            object value = GetProp(target, name);
            return value != null ? Convert.ToString(value) : null;
        }

        static int GetIntProp(object target, string name)
        {
            object value = GetProp(target, name);
            if (value == null)
            {
                throw new Exception("missing int property " + name + " on " + target.GetType().Name);
            }
            return Convert.ToInt32(value);
        }

        static bool GetBoolProp(object target, string name)
        {
            object value = GetProp(target, name);
            if (value == null)
            {
                throw new Exception("missing bool property " + name + " on " + target.GetType().Name);
            }
            return Convert.ToBoolean(value);
        }

        static bool GetBoolPropFlexible(object target, params string[] names)
        {
            foreach (string name in names)
            {
                object value = GetProp(target, name);
                if (value != null)
                {
                    return Convert.ToBoolean(value);
                }
            }
            // Default for optional CurrentLegal on steps: false when missing.
            if (names.Length == 1 && names[0] == "CurrentLegal")
            {
                return false;
            }
            throw new Exception(
                "missing bool property among [" + string.Join(",", names) + "] on "
                + (target != null ? target.GetType().Name : "null"));
        }

        static IEnumerable GetEnumerableProp(object target, string name)
        {
            return GetProp(target, name) as IEnumerable;
        }

        static void SetProp(object target, string name, object value)
        {
            PropertyInfo prop = target.GetType().GetProperty(name);
            if (prop == null || !prop.CanWrite)
            {
                throw new Exception(
                    "YGOMASTER-LLM-005 type " + target.GetType().Name
                    + " missing writable property " + name);
            }
            object coerced = value;
            if (value != null && prop.PropertyType != value.GetType()
                && typeof(IConvertible).IsAssignableFrom(prop.PropertyType))
            {
                coerced = Convert.ChangeType(value, prop.PropertyType);
            }
            prop.SetValue(target, coerced, null);
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

        static string Key(params int[] parts)
        {
            return string.Join(",", parts.Select(p => p.ToString()).ToArray());
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
                AssertFalse(json.Contains(token), "Layer A / projection leaks " + token);
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

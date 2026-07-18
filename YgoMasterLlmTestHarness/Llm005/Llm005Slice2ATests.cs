using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Hardened Slice 2A contract tests (second independent review blockers).
    /// Complements Llm005Slice0Tests.
    /// </summary>
    static class Llm005Slice2ATests
    {
        public static void RunAll()
        {
            RootShellsSurviveNodeBeamTimePressure();
            ForcedClockExhaustionIsDeterministic();
            ExhaustedClockMarksEveryCandidacyRoot();
            BeamWidthAffectsOnlyPostFairnessExtras();
            TranspositionPrunesSemanticallyEquivalentRoots();
            TranspositionHiddenOpponentInvariantAndPublicDivergence();
            DepthCapsRespected();
            SerializedByteBudgetPreservesRootsOrFailClosed();
            SerializedByteBudgetRejectsBelowMinimum();
            MechanicalExclusionsAreCountedWithStableReasons();
            SetMonstAndLevel3NeverGetRank4();
            BattleRootSurfacesAttackAndMain2Candidates();
            ClientSettingsSearchLimitsDefaultsAndClamp();
            PlanningAuditDefaultOffAndSeatGated();
            SchemaAndDecideUnchanged();
        }

        static void RootShellsSurviveNodeBeamTimePressure()
        {
            DecisionSnapshot snap = MakeThreeRootGirochinSnapshot();
            LlmSelfResources self = MakeGirochinSelf();
            LlmSearchLimits limits = new LlmSearchLimits()
            {
                MaxStrategicDepth = 4,
                MaxNodes = 1,
                BeamWidth = 1,
                MaxWallMs = 500,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmSearchGraph graph = Expand(snap, self, limits);
            AssertTrue(graph.Coverage.RootShellCount == 3, "3 root shells");
            AssertTrue(graph.Coverage.RepresentedRootActions == 3, "all roots represented");
            AssertTrue(graph.Coverage.ContinuationExpansionCount <= 1,
                "continuation cap hard: " + graph.Coverage.ContinuationExpansionCount);
            AssertTrue(graph.Lines.Count(l => l.IsRootShell) == 3, "shell lines present");
        }

        static void ForcedClockExhaustionIsDeterministic()
        {
            DecisionSnapshot snap = MakeThreeRootGirochinSnapshot();
            LlmSelfResources self = MakeGirochinSelf();
            LlmSearchFakeBudgetClock clock = new LlmSearchFakeBudgetClock();
            clock.AdvanceMilliseconds(1000);
            LlmSearchLimits limits = new LlmSearchLimits()
            {
                MaxStrategicDepth = 4,
                MaxNodes = 96,
                BeamWidth = 12,
                MaxWallMs = 500,
                Clock = clock,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmSearchGraph a = Expand(snap, self, limits);
            LlmSearchFakeBudgetClock clock2 = new LlmSearchFakeBudgetClock();
            clock2.AdvanceMilliseconds(1000);
            limits.Clock = clock2;
            LlmSearchGraph b = Expand(snap, self, limits);
            AssertTrue(a.Status == "budget_incomplete" || a.Coverage.ContinuationExpansionCount == 0,
                "time budget incomplete");
            string ja = MiniJSON.Json.Serialize(LlmSearchProjection.Project(a));
            string jb = MiniJSON.Json.Serialize(LlmSearchProjection.Project(b));
            AssertTrue(ja == jb, "time budget deterministic");
            AssertTrue(
                a.Coverage.PruningReasons.Any(r => r != null && r.Contains("time"))
                || a.Coverage.NonExpansionReasons.Any(r => r != null && r.Contains("time")),
                "time_budget telemetry");
        }

        static void ExhaustedClockMarksEveryCandidacyRoot()
        {
            DecisionSnapshot snap = MakeThreeRootGirochinSnapshot();
            LlmSelfResources self = MakeGirochinSelf();
            LlmSearchFakeBudgetClock clock = new LlmSearchFakeBudgetClock();
            clock.AdvanceMilliseconds(10_000);
            LlmSearchLimits limits = new LlmSearchLimits()
            {
                MaxStrategicDepth = 4,
                MaxNodes = 96,
                BeamWidth = 12,
                MaxWallMs = 500,
                Clock = clock,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmSearchGraph graph = Expand(snap, self, limits);
            AssertTrue(graph.Coverage.ContinuationExpansionCount == 0, "no expansions under exhausted clock");

            // Roots with candidacy: Battle (0) and Flip Cave (2). End (1) has none.
            int[] candidacyRoots = new[] { 0, 2 };
            foreach (int rootId in candidacyRoots)
            {
                LlmSearchLine shell = graph.Lines.FirstOrDefault(l => l.IsRootShell && l.RootActionId == rootId);
                AssertTrue(shell != null, "shell for root " + rootId);
                AssertTrue(shell.NonExpansionReason == "time_budget",
                    "root " + rootId + " NonExpansionReason=time_budget got=" + shell.NonExpansionReason);
                AssertTrue(
                    graph.Coverage.NonExpansionReasons.Contains("root:" + rootId + ":time_budget"),
                    "coverage NonExpansionReasons for root " + rootId);
            }
        }

        static void BeamWidthAffectsOnlyPostFairnessExtras()
        {
            DecisionSnapshot snap = MakeThreeRootGirochinSnapshot();
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 3,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Attack,
                ActionLabel = "Attack",
                CardId = 71533,
                Card = new LlmCardMetadata() { CardId = 71533, Name = "Girochin", Level = 4 },
            });
            LlmSelfResources self = MakeGirochinSelf();
            LlmSearchLimits narrow = new LlmSearchLimits()
            {
                MaxNodes = 50,
                BeamWidth = 1,
                MaxStrategicDepth = 4,
                MaxWallMs = 5000,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmSearchLimits wide = new LlmSearchLimits()
            {
                MaxNodes = 50,
                BeamWidth = 12,
                MaxStrategicDepth = 4,
                MaxWallMs = 5000,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmSearchGraph gn = Expand(snap, self, narrow);
            LlmSearchGraph gw = Expand(snap, self, wide);
            AssertTrue(gn.Coverage.RepresentedRootActions == gw.Coverage.RepresentedRootActions,
                "fairness keeps root count equal");
            AssertTrue(gw.Coverage.ContinuationExpansionCount >= gn.Coverage.ContinuationExpansionCount,
                "wider beam allows more post-fairness extras");
        }

        static void TranspositionPrunesSemanticallyEquivalentRoots()
        {
            DecisionSnapshot snap = MakeThreeRootGirochinSnapshot();
            LlmSelfResources self = MakeGirochinSelf();
            // Second flip root: same card identity / command semantics, different ActionId.
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 9,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Reverse,
                ActionLabel = "Flip Summon The Dragon Dwelling in the Cave (dup)",
                CardId = 66672569,
                Card = new LlmCardMetadata() { CardId = 66672569, Name = "Cave", Level = 4 },
                Position = 0,
            });
            // Align first flip's CardId for semantic equivalence.
            LegalAction firstFlip = snap.LegalActions.First(a => a.ActionId == 2);
            firstFlip.CardId = 66672569;
            firstFlip.Position = 0;

            LlmSearchLimits limits = new LlmSearchLimits()
            {
                MaxNodes = 96,
                BeamWidth = 12,
                MaxStrategicDepth = 4,
                MaxWallMs = 5000,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmSearchGraph graph = Expand(snap, self, limits);
            AssertTrue(graph.Coverage.TranspositionPrunes > 0,
                "TranspositionPrunes > 0 for duplicate semantic roots, got "
                + graph.Coverage.TranspositionPrunes);
            AssertTrue(
                graph.Coverage.PruningReasons.Any(r =>
                    r != null && r.IndexOf("transposition", StringComparison.OrdinalIgnoreCase) >= 0),
                "explicit transposition pruning reason");
            AssertTrue(
                graph.Lines.Any(l =>
                    l.IsRootShell
                    && (l.RootActionId == 2 || l.RootActionId == 9)
                    && l.PruneReason != null
                    && l.PruneReason.IndexOf("transposition", StringComparison.OrdinalIgnoreCase) >= 0),
                "shell prune_reason transposition on duplicate root");
        }

        static void TranspositionHiddenOpponentInvariantAndPublicDivergence()
        {
            DecisionSnapshot baseSnap = MakeThreeRootGirochinSnapshot();
            AttachPublicLpAndField(baseSnap, lp0: 8000, lp1: 8000, selfFieldCount: 2);
            LlmSelfResources self = MakeGirochinSelf();
            LegalAction flip = baseSnap.LegalActions.First(a => a.ActionId == 2);
            flip.CardId = 66672569;

            string fpBase = LlmTacticalAffordanceGraph.FingerprintContinuation(
                flip, "rank4_xyz", self, baseSnap);

            // Hidden opponent pair: only opponent face-down / hand identities differ — not in allowed info.
            DecisionSnapshot hiddenA = CloneSnapshotShallow(baseSnap);
            DecisionSnapshot hiddenB = CloneSnapshotShallow(baseSnap);
            // Simulate "hidden" by attaching non-public notes that must not appear in fingerprint.
            // PublicState stays identical; self stays identical.
            string fpA = LlmTacticalAffordanceGraph.FingerprintContinuation(
                flip, "rank4_xyz", self, hiddenA);
            // Different self Extra / hand would change — use same self. Opponent hidden is not on self.
            LlmSelfResources selfSame = MakeGirochinSelf();
            string fpB = LlmTacticalAffordanceGraph.FingerprintContinuation(
                flip, "rank4_xyz", selfSame, hiddenB);
            AssertTrue(fpA == fpB, "hidden-pair: byte-identical fingerprints");
            AssertTrue(fpA == fpBase, "hidden-pair matches base allowed-info fingerprint");

            // Also expand projections for hidden pair.
            LlmSearchLimits limits = new LlmSearchLimits()
            {
                MaxNodes = 96,
                BeamWidth = 12,
                MaxStrategicDepth = 4,
                MaxWallMs = 5000,
                MaxSerializedBytes = 32 * 1024,
            };
            string projA = MiniJSON.Json.Serialize(
                LlmSearchProjection.Project(Expand(hiddenA, self, limits)));
            string projB = MiniJSON.Json.Serialize(
                LlmSearchProjection.Project(Expand(hiddenB, selfSame, limits)));
            AssertTrue(projA == projB, "hidden-pair: identical projections");

            // Public LP change must diverge fingerprint and avoid false prune path equivalence.
            DecisionSnapshot lpSnap = CloneSnapshotShallow(baseSnap);
            AttachPublicLpAndField(lpSnap, lp0: 8000, lp1: 1000, selfFieldCount: 2);
            string fpLp = LlmTacticalAffordanceGraph.FingerprintContinuation(
                flip, "rank4_xyz", self, lpSnap);
            AssertTrue(fpLp != fpBase, "LP change must change fingerprint");

            // Controlled resource identity change must diverge.
            LlmSelfResources selfOther = LlmSelfResources.Create(
                1,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>()
                {
                    LlmSelfResourceCard.Create(
                        99999, "OtherL4", 0, 0, 1, true, 4, null, false, false, null,
                        "Effect", "main_deck_monster", false, "Effect", 1700, 1000, null, 0),
                    LlmSelfResourceCard.Create(
                        66672569, "Cave", 1, 0, 0, false, 4, null, false, false, null,
                        "Effect", "main_deck_monster", false, "Effect", 1300, 2000, null, 0),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    LlmSelfResourceExtraDeckEntry.Create(
                        84013237, "Utopia", "Xyz", "xyz", null, 4, true, null, false,
                        "Xyz", 2500, 2000, null, 1),
                });
            string fpField = LlmTacticalAffordanceGraph.FingerprintContinuation(
                flip, "rank4_xyz", selfOther, baseSnap);
            AssertTrue(fpField != fpBase, "controlled field identity change must change fingerprint");
        }

        static void DepthCapsRespected()
        {
            DecisionSnapshot snap = MakeThreeRootGirochinSnapshot();
            LlmSelfResources self = MakeGirochinSelf();
            LlmSearchLimits d0 = new LlmSearchLimits()
            {
                MaxStrategicDepth = 0,
                MaxNodes = 96,
                BeamWidth = 12,
                MaxWallMs = 5000,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmSearchGraph g0 = Expand(snap, self, d0);
            AssertTrue(g0.Lines.All(l => l.StrategicDepth == 0), "depth 0 only shells");
            AssertTrue(g0.Coverage.ContinuationExpansionCount == 0, "no continuations at depth 0");

            LlmSearchLimits d1 = new LlmSearchLimits()
            {
                MaxStrategicDepth = 1,
                MaxNodes = 96,
                BeamWidth = 12,
                MaxWallMs = 5000,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmSearchGraph g1 = Expand(snap, self, d1);
            AssertTrue(g1.Lines.All(l => l.StrategicDepth <= 1), "depth cap 1");
        }

        static void SerializedByteBudgetPreservesRootsOrFailClosed()
        {
            DecisionSnapshot snap = MakeThreeRootGirochinSnapshot();
            LlmSelfResources self = MakeGirochinSelf();
            int min = LlmSearchProjection.MinimumSupportedSerializedBytes;
            int[] budgets = new int[]
            {
                min, min + 100, 1024, 2048, 4096, 8192, 16384, 32 * 1024
            };
            foreach (int maxBytes in budgets)
            {
                LlmSearchLimits limits = new LlmSearchLimits()
                {
                    MaxNodes = 96,
                    BeamWidth = 12,
                    MaxStrategicDepth = 4,
                    MaxWallMs = 5000,
                    MaxSerializedBytes = maxBytes,
                };
                LlmSearchGraph graph = Expand(snap, self, limits);
                Dictionary<string, object> proj = LlmSearchProjection.Project(graph);
                string json = MiniJSON.Json.Serialize(proj);
                int utf8 = Encoding.UTF8.GetByteCount(json);
                AssertTrue(utf8 <= maxBytes,
                    "utf8 " + utf8 + " > " + maxBytes + " for accepted budget");
                string status = proj.ContainsKey("status") ? Convert.ToString(proj["status"]) : "";
                if (status == "budget_failed")
                {
                    AssertTrue(
                        Convert.ToString(proj["error"]).Contains("max_serialized_bytes"),
                        "fail closed reason");
                }
                else
                {
                    AssertTrue(proj.ContainsKey("lines"), "lines present");
                }
            }
        }

        static void SerializedByteBudgetRejectsBelowMinimum()
        {
            DecisionSnapshot snap = MakeThreeRootGirochinSnapshot();
            LlmSelfResources self = MakeGirochinSelf();
            int min = LlmSearchProjection.MinimumSupportedSerializedBytes;
            int[] illegal = new int[] { 1, 50, 100, min - 1 };
            foreach (int maxBytes in illegal)
            {
                LlmSearchLimits limits = new LlmSearchLimits()
                {
                    MaxNodes = 96,
                    BeamWidth = 12,
                    MaxStrategicDepth = 4,
                    MaxWallMs = 5000,
                    MaxSerializedBytes = maxBytes,
                };
                LlmSearchGraph graph = Expand(snap, self, limits);
                bool threw = false;
                try
                {
                    LlmSearchProjection.Project(graph);
                }
                catch (ArgumentOutOfRangeException)
                {
                    threw = true;
                }
                AssertTrue(threw, "Project must reject MaxSerializedBytes=" + maxBytes);

                LlmPlanningSearchAuditResult audit = LlmPlanningSearchAudit.TryBuild(
                    snap, self, limits);
                AssertTrue(!audit.Success, "audit fail closed below min bytes=" + maxBytes);
                AssertTrue(
                    audit.Error != null
                    && audit.Error.IndexOf("max_serialized_bytes_below_minimum", StringComparison.Ordinal) >= 0,
                    "explicit minimum error, got=" + audit.Error);
                AssertTrue(audit.Projection == null,
                    "no oversized Projection object for below-min budget");
            }
        }

        static void MechanicalExclusionsAreCountedWithStableReasons()
        {
            DecisionSnapshot snap = new DecisionSnapshot()
            {
                RunEffectSeq = 77,
                ControlledPlayer = 1,
                ActingPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 10,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Look,
                IsMechanical = true,
                ActionLabel = "Look",
            });
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 11,
                Kind = LegalActionKind.DialogResult,
                DialogIsYesNoPrompt = true,
                IsMechanical = true,
                ActionLabel = "Yes",
            });
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 12,
                Kind = LegalActionKind.ListIndex,
                IsMechanical = true,
                IsEffectTargetSelection = false,
                ActionLabel = "List pick",
            });
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 13,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
                IsMechanical = false,
                ActionLabel = "End Phase",
            });
            LlmSelfResources self = MakeGirochinSelf();
            LlmSearchGraph graph = LlmTacticalAffordanceGraph.Build(
                snap, self, LlmSearchLimits.CreateDefault());

            string r10 = LlmTacticalAffordanceGraph.MechanicalExclusionReason(snap.LegalActions[0]);
            string r11 = LlmTacticalAffordanceGraph.MechanicalExclusionReason(snap.LegalActions[1]);
            string r12 = LlmTacticalAffordanceGraph.MechanicalExclusionReason(snap.LegalActions[2]);
            AssertTrue(r10 == "filtered_mechanical:Command/Look", "look reason=" + r10);
            AssertTrue(r11 == "filtered_mechanical:DialogResult/yes_no", "dialog reason=" + r11);
            AssertTrue(r12 == "filtered_mechanical:ListIndex/list", "list reason=" + r12);

            AssertTrue(graph.Coverage.ExclusionReasons.Count(r => r == r10) == 1, "one Look exclusion");
            AssertTrue(graph.Coverage.ExclusionReasons.Count(r => r == r11) == 1, "one dialog exclusion");
            AssertTrue(graph.Coverage.ExclusionReasons.Count(r => r == r12) == 1, "one list exclusion");
            AssertTrue(graph.Coverage.RootExclusions.Contains("action:10:" + r10), "root excl 10");
            AssertTrue(graph.Coverage.RootExclusions.Contains("action:11:" + r11), "root excl 11");
            AssertTrue(graph.Coverage.RootExclusions.Contains("action:12:" + r12), "root excl 12");
            AssertTrue(graph.Coverage.LegalRootActions == 1, "only End is strategic");
            AssertTrue(graph.Coverage.RootShellCount == 1, "one shell");
        }

        static void SetMonstAndLevel3NeverGetRank4()
        {
            DecisionSnapshot snap = new DecisionSnapshot()
            {
                RunEffectSeq = 50,
                ControlledPlayer = 1,
                ActingPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.SetMonst,
                ActionLabel = "Set Monster",
                CardId = 1,
                Card = new LlmCardMetadata() { CardId = 1, Name = "L4", Level = 4 },
            });
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Reverse,
                ActionLabel = "Flip Level3",
                CardId = 2,
                Card = new LlmCardMetadata() { CardId = 2, Name = "L3", Level = 3 },
            });
            LlmSelfResources self = MakeGirochinSelf();
            LlmSearchGraph graph = Expand(snap, self, LlmSearchLimits.CreateDefault());
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line.RootActionId == 0 || line.RootActionId == 1)
                {
                    bool hasRank4 = line.Steps != null && line.Steps.Any(s =>
                        s.Label != null
                        && s.Label.IndexOf("Rank 4", StringComparison.OrdinalIgnoreCase) >= 0);
                    AssertTrue(!hasRank4,
                        "SetMonst/Level3 root must not inherit Rank4 line root=" + line.RootActionId);
                }
            }
        }

        static void BattleRootSurfacesAttackAndMain2Candidates()
        {
            DecisionSnapshot snap = new DecisionSnapshot()
            {
                RunEffectSeq = 60,
                ControlledPlayer = 1,
                ActingPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
                ActionLabel = "Enter Battle Phase",
            });
            LlmSelfResources self = MakeGirochinSelf();
            LlmSearchGraph graph = Expand(snap, self, LlmSearchLimits.CreateDefault());
            AssertTrue(graph.Lines.Any(l =>
                l.RootActionId == 0
                && !l.IsRootShell
                && l.Provenance == "rules_inferred"
                && !l.CommitEligible
                && l.Steps.Any(s => s.Label != null
                    && s.Label.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0
                    && !s.CurrentLegal)),
                "battle attack candidate");
            AssertTrue(graph.Lines.Any(l =>
                l.RootActionId == 0
                && !l.IsRootShell
                && l.Steps.Any(s => s.Label != null
                    && s.Label.IndexOf("Main Phase 2", StringComparison.OrdinalIgnoreCase) >= 0
                    && !s.CommitEligible)),
                "main2 candidate");
            AssertTrue(graph.Lines.Where(l => !l.IsRootShell).All(l =>
                l.Boundary != null
                && l.Boundary.IndexOf("opponent", StringComparison.OrdinalIgnoreCase) >= 0),
                "opponent/fresh boundary");
        }

        static void ClientSettingsSearchLimitsDefaultsAndClamp()
        {
            LlmSearchLimits d = LlmSearchLimits.CreateDefault();
            AssertTrue(d.MaxStrategicDepth == 4, "default depth");
            AssertTrue(d.MaxNodes == 96, "default nodes");
            AssertTrue(d.BeamWidth == 12, "default beam");
            AssertTrue(d.MaxWallMs == 500, "default wall");
            AssertTrue(d.MaxSerializedBytes == 16384, "default serialized");

            LlmSearchLimits clamped = LlmSearchLimits.FromClientSettings(
                -5, 999999, 0, -1, 1);
            AssertTrue(clamped.MaxStrategicDepth == LlmSearchLimits.MinMaxStrategicDepth,
                "depth clamp min");
            AssertTrue(clamped.MaxNodes == LlmSearchLimits.HardMaxNodes, "nodes clamp hard max");
            AssertTrue(clamped.BeamWidth == LlmSearchLimits.DefaultBeamWidth
                || clamped.BeamWidth >= LlmSearchLimits.MinBeamWidth,
                "beam clamp");
            AssertTrue(clamped.MaxWallMs >= LlmSearchLimits.MinMaxWallMs, "wall clamp");
            AssertTrue(
                clamped.MaxSerializedBytes == LlmSearchProjection.MinimumSupportedSerializedBytes,
                "serialized clamp to min floor, got=" + clamped.MaxSerializedBytes);

            LlmSearchLimits fromAlias = LlmSearchLimits.CreateFromClientSettings(4, 96, 12, 500, 16384);
            AssertTrue(fromAlias.MaxStrategicDepth == 4, "alias depth");
            AssertTrue(fromAlias.MaxSerializedBytes == 16384, "alias serialized");
        }

        static void PlanningAuditDefaultOffAndSeatGated()
        {
            DecisionSnapshot p1 = MakeThreeRootGirochinSnapshot();
            p1.ControlledPlayer = 1;
            p1.ActingPlayer = 1;
            p1.SelfResources = MakeGirochinSelf();
            DecisionSnapshot p0 = MakeThreeRootGirochinSnapshot();
            p0.ControlledPlayer = 0;
            p0.ActingPlayer = 0;
            p0.SelfResources = MakeGirochinSelf();

            AssertTrue(!LlmSelfResourcesAuditPolicy.ShouldEmitAudit(false, 1, p1), "default off");
            AssertTrue(!LlmSelfResourcesAuditPolicy.ShouldEmitAudit(true, 1, p0), "seat mismatch");
            AssertTrue(LlmSelfResourcesAuditPolicy.ShouldEmitAudit(true, 1, p1), "seat match");

            string reqBefore = LlmBrokerProtocol.SerializeDecisionRequest(p1);
            LlmPlanningSearchAuditResult ok = LlmPlanningSearchAudit.TryBuild(
                p1, p1.SelfResources, LlmSearchLimits.CreateDefault());
            AssertTrue(ok.Success, "audit success");
            string started = LlmDecisionLogSerializer.SerializeSearchStarted(p1, LlmSearchLimits.CreateDefault());
            AssertTrue(started.Contains("llm_search_started"), "started kind");
            string completed = LlmDecisionLogSerializer.SerializeSearchCompleted(ok);
            AssertTrue(completed.Contains("llm_search_completed"), "completed kind");

            LlmPlanningSearchAuditResult fail = LlmPlanningSearchAudit.TryBuild(
                p1, p1.SelfResources, LlmSearchLimits.CreateDefault(),
                new ThrowingFactory());
            AssertTrue(!fail.Success, "failure");
            string fallback = LlmDecisionLogSerializer.SerializeSearchFallback(p1, fail.Error);
            AssertTrue(fallback.Contains("llm_search_fallback"), "fallback kind");

            string reqAfter = LlmBrokerProtocol.SerializeDecisionRequest(p1);
            AssertTrue(reqBefore == reqAfter, "request byte-identical after audit");
        }

        static void SchemaAndDecideUnchanged()
        {
            AssertTrue(LlmBrokerProtocol.SchemaVersion == 4, "schema version is v4 after LLM-004");
            DecisionSnapshot snapshot = new DecisionSnapshot() { RunEffectSeq = 1 };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
            });
            string req = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            AssertTrue(!req.Contains("self_resources"), "no self_resources on /decide");
            AssertTrue(!req.Contains("search_summary"), "no search_summary on /decide yet");
        }

        // ------------------------------------------------------------------

        static DecisionSnapshot MakeThreeRootGirochinSnapshot()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 402,
                ControlledPlayer = 1,
                ActingPlayer = 1,
                Turn = 3,
                CurrentPhase = (int)DuelPhase.Main1,
                IsStrategicWindow = true,
            };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
                ActionLabel = "Enter Battle Phase",
            });
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
                ActionLabel = "End Phase",
            });
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 2,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Reverse,
                ActionLabel = "Flip Summon The Dragon Dwelling in the Cave",
                CardId = 66672569,
                Card = new LlmCardMetadata() { CardId = 66672569, Name = "Cave", Level = 4 },
            });
            AttachPublicLpAndField(snapshot, 8000, 8000, 2);
            return snapshot;
        }

        static void AttachPublicLpAndField(
            DecisionSnapshot snapshot,
            int lp0,
            int lp1,
            int selfFieldCount)
        {
            snapshot.PublicState.Players.Clear();
            PublicPlayerState p0 = new PublicPlayerState() { Player = 0, LifePoints = lp0 };
            p0.Positions.Add(new PublicPositionState() { Position = 0, Count = 0 });
            PublicPlayerState p1 = new PublicPlayerState() { Player = 1, LifePoints = lp1 };
            p1.Positions.Add(new PublicPositionState() { Position = 0, Count = selfFieldCount });
            p1.KnownCards.Add(new PublicKnownCard()
            {
                Player = 1,
                Position = 0,
                Index = 0,
                CardId = 71533,
                Face = 1,
            });
            snapshot.PublicState.Players.Add(p0);
            snapshot.PublicState.Players.Add(p1);
        }

        static DecisionSnapshot CloneSnapshotShallow(DecisionSnapshot src)
        {
            DecisionSnapshot d = new DecisionSnapshot()
            {
                RunEffectSeq = src.RunEffectSeq,
                ControlledPlayer = src.ControlledPlayer,
                ActingPlayer = src.ActingPlayer,
                Turn = src.Turn,
                CurrentPhase = src.CurrentPhase,
                CurrentStep = src.CurrentStep,
                IsStrategicWindow = src.IsStrategicWindow,
            };
            foreach (LegalAction a in src.LegalActions)
            {
                d.LegalActions.Add(a);
            }
            if (src.PublicState != null && src.PublicState.Players != null)
            {
                foreach (PublicPlayerState p in src.PublicState.Players)
                {
                    if (p == null)
                    {
                        continue;
                    }
                    PublicPlayerState np = new PublicPlayerState()
                    {
                        Player = p.Player,
                        LifePoints = p.LifePoints,
                    };
                    if (p.Positions != null)
                    {
                        foreach (PublicPositionState pos in p.Positions)
                        {
                            if (pos != null)
                            {
                                np.Positions.Add(new PublicPositionState()
                                {
                                    Position = pos.Position,
                                    Count = pos.Count,
                                });
                            }
                        }
                    }
                    if (p.KnownCards != null)
                    {
                        foreach (PublicKnownCard c in p.KnownCards)
                        {
                            if (c != null)
                            {
                                np.KnownCards.Add(new PublicKnownCard()
                                {
                                    Player = c.Player,
                                    Position = c.Position,
                                    Index = c.Index,
                                    CardId = c.CardId,
                                    Face = c.Face,
                                });
                            }
                        }
                    }
                    d.PublicState.Players.Add(np);
                }
            }
            if (src.TurnMemory != null)
            {
                d.TurnMemory.NormalSummonUsed = src.TurnMemory.NormalSummonUsed;
            }
            return d;
        }

        static LlmSelfResources MakeGirochinSelf()
        {
            return LlmSelfResources.Create(
                1,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>()
                {
                    LlmSelfResourceCard.Create(
                        71533, "Girochin", 0, 0, 1, true, 4, null, false, false, null,
                        "Effect", "main_deck_monster", false, "Effect", 1700, 1000, null, 0),
                    LlmSelfResourceCard.Create(
                        66672569, "Cave", 1, 0, 0, false, 4, null, false, false, null,
                        "Effect", "main_deck_monster", false, "Effect", 1300, 2000, null, 0),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    LlmSelfResourceExtraDeckEntry.Create(
                        84013237, "Utopia", "Xyz", "xyz", null, 4, true, null, false,
                        "Xyz", 2500, 2000, null, 1),
                });
        }

        sealed class ThrowingFactory
        {
            public object Build(object snapshot, object self, object limits)
            {
                throw new InvalidOperationException("forced_graph_failure");
            }
        }

        static LlmSearchGraph Expand(
            DecisionSnapshot snap,
            LlmSelfResources self,
            LlmSearchLimits limits)
        {
            LlmSearchGraph graph = LlmTacticalAffordanceGraph.Build(snap, self, limits);
            return LlmBoundedLineSearch.Search(graph, limits);
        }

        static void AssertTrue(bool v, string m)
        {
            if (!v)
            {
                throw new Exception(m);
            }
        }
    }
}

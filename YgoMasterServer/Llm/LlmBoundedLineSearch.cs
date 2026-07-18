using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Iterative-deepening, per-root-fairness continuation expansion with transposition,
    /// hard node/depth/time budgets, and deterministic scoring (YGOMASTER-LLM-005 Slice 2A).
    /// MaxNodes applies to continuation expansions only; root shells are mandatory.
    /// Non-expansion telemetry tracks actual attempted/expanded outcomes (not fairness batching).
    /// </summary>
    static class LlmBoundedLineSearch
    {
        public static LlmSearchGraph Search(LlmSearchGraph graph, LlmSearchLimits limits)
        {
            if (graph == null)
            {
                return null;
            }
            if (limits == null)
            {
                limits = graph.Limits ?? LlmSearchLimits.CreateDefault();
            }
            else
            {
                // Preserve injectable Clock; clamp numeric budgets for search safety.
                ILlmSearchBudgetClock clockPreserve = limits.Clock;
                limits = limits.Clone();
                limits.Clock = clockPreserve;
                // Do NOT Normalize MaxSerializedBytes here when tests set strict low values —
                // projection/audit enforce MinimumSupported separately. Clamp only expansion dims.
                limits.MaxStrategicDepth = Math.Max(0, Math.Min(limits.MaxStrategicDepth, LlmSearchLimits.HardMaxStrategicDepth));
                limits.MaxNodes = Math.Max(0, Math.Min(limits.MaxNodes, LlmSearchLimits.HardMaxNodes));
                if (limits.BeamWidth <= 0)
                {
                    limits.BeamWidth = LlmSearchLimits.DefaultBeamWidth;
                }
                limits.BeamWidth = Math.Max(LlmSearchLimits.MinBeamWidth, Math.Min(limits.BeamWidth, LlmSearchLimits.HardMaxBeamWidth));
                if (limits.MaxWallMs <= 0)
                {
                    limits.MaxWallMs = LlmSearchLimits.DefaultMaxWallMs;
                }
                limits.MaxWallMs = Math.Max(LlmSearchLimits.MinMaxWallMs, Math.Min(limits.MaxWallMs, LlmSearchLimits.HardMaxWallMs));
            }

            ILlmSearchBudgetClock clock = limits.Clock ?? new LlmSearchSystemBudgetClock();
            DecisionSnapshot snapshot = graph.SourceSnapshot;
            LlmSelfResources self = graph.SourceSelfResources;

            List<LlmSearchLine> lines = new List<LlmSearchLine>();
            if (graph.Lines != null)
            {
                foreach (LlmSearchLine line in graph.Lines)
                {
                    lines.Add(CloneLineMutable(line));
                }
            }

            int maxDepth = limits.MaxStrategicDepth;
            int maxContinuations = limits.MaxNodes > 0 ? limits.MaxNodes : LlmSearchLimits.DefaultMaxNodes;
            int beam = limits.BeamWidth > 0 ? limits.BeamWidth : LlmSearchLimits.DefaultBeamWidth;
            long maxWall = limits.MaxWallMs > 0 ? limits.MaxWallMs : LlmSearchLimits.DefaultMaxWallMs;

            // Allowed-information state fingerprints only (never root ActionId).
            HashSet<string> seenStateFingerprints = new HashSet<string>();

            int continuationCount = 0;
            int pruned = graph.Coverage != null ? graph.Coverage.PrunedNodes : 0;
            int transpositionPrunes = 0;
            int boundaryNodes = graph.Coverage != null ? graph.Coverage.BoundaryNodes : 0;
            string status = "budget_complete";

            Dictionary<int, LegalAction> rootsById = new Dictionary<int, LegalAction>();
            if (snapshot != null && snapshot.LegalActions != null)
            {
                foreach (LegalAction action in snapshot.LegalActions)
                {
                    if (action != null && !action.IsMechanical)
                    {
                        rootsById[action.ActionId] = action;
                    }
                }
            }

            List<int> rootOrder = new List<int>();
            foreach (LlmSearchLine line in lines)
            {
                if (line != null && line.IsRootShell && !rootOrder.Contains(line.RootActionId))
                {
                    rootOrder.Add(line.RootActionId);
                }
            }

            List<PendingContinuation> pending = new List<PendingContinuation>();
            foreach (int rootId in rootOrder)
            {
                LegalAction root;
                if (!rootsById.TryGetValue(rootId, out root) || root == null)
                {
                    continue;
                }
                CollectContinuationCandidates(root, snapshot, self, pending);
            }

            HashSet<int> rootsWithCandidacy = new HashSet<int>();
            foreach (PendingContinuation p in pending)
            {
                rootsWithCandidacy.Add(p.RootActionId);
            }

            // Fairness: first pending per root, then remainder (beam applies only to remainder).
            HashSet<int> fairnessAssigned = new HashSet<int>();
            pending.Sort(ComparePending);
            List<PendingContinuation> fairnessBatch = new List<PendingContinuation>();
            List<PendingContinuation> remainder = new List<PendingContinuation>();
            foreach (PendingContinuation p in pending)
            {
                if (!fairnessAssigned.Contains(p.RootActionId))
                {
                    fairnessBatch.Add(p);
                    fairnessAssigned.Add(p.RootActionId);
                }
                else
                {
                    remainder.Add(p);
                }
            }

            HashSet<int> beamPrunedRoots = new HashSet<int>();
            if (remainder.Count > beam)
            {
                for (int i = beam; i < remainder.Count; i++)
                {
                    pruned++;
                    beamPrunedRoots.Add(remainder[i].RootActionId);
                    if (graph.Coverage != null)
                    {
                        graph.Coverage.PruningReasons.Add("beam_width");
                    }
                }
                remainder = remainder.GetRange(0, beam);
            }

            List<PendingContinuation> expansionOrder = new List<PendingContinuation>();
            expansionOrder.AddRange(fairnessBatch);
            expansionOrder.AddRange(remainder);

            // Actual outcomes — not fairness-batch assignment.
            HashSet<int> rootsAttempted = new HashSet<int>();
            HashSet<int> rootsExpanded = new HashSet<int>();
            Dictionary<int, string> rootBlockReason = new Dictionary<int, string>();
            bool timeExhausted = clock.ElapsedMilliseconds >= maxWall;

            foreach (PendingContinuation candidate in expansionOrder)
            {
                if (clock.ElapsedMilliseconds >= maxWall)
                {
                    timeExhausted = true;
                    status = "budget_incomplete";
                    if (graph.Coverage != null
                        && !graph.Coverage.PruningReasons.Contains(
                            LlmTacticalAffordanceGraph.BoundaryTimeBudget))
                    {
                        graph.Coverage.PruningReasons.Add(
                            LlmTacticalAffordanceGraph.BoundaryTimeBudget);
                    }
                    break;
                }

                rootsAttempted.Add(candidate.RootActionId);

                if (maxDepth < 1 || candidate.StrategicDepth > maxDepth)
                {
                    status = maxDepth < 1 ? "budget_incomplete" : status;
                    pruned++;
                    SetBlockReason(rootBlockReason, candidate.RootActionId, "depth_budget");
                    if (graph.Coverage != null)
                    {
                        graph.Coverage.PruningReasons.Add(LlmTacticalAffordanceGraph.BoundaryDepthBudget);
                    }
                    continue;
                }
                if (continuationCount >= maxContinuations)
                {
                    status = "budget_incomplete";
                    SetBlockReason(rootBlockReason, candidate.RootActionId, "node_budget");
                    if (graph.Coverage != null)
                    {
                        graph.Coverage.PruningReasons.Add(LlmTacticalAffordanceGraph.BoundaryNodeBudget);
                    }
                    continue;
                }

                if (seenStateFingerprints.Contains(candidate.Fingerprint))
                {
                    transpositionPrunes++;
                    pruned++;
                    SetBlockReason(rootBlockReason, candidate.RootActionId, "transposition");
                    if (graph.Coverage != null)
                    {
                        graph.Coverage.PruningReasons.Add(LlmTacticalAffordanceGraph.BoundaryTransposition);
                    }
                    LlmSearchLine shell = FindRootShell(lines, candidate.RootActionId);
                    if (shell != null && string.IsNullOrEmpty(shell.PruneReason))
                    {
                        shell.PruneReason = LlmTacticalAffordanceGraph.BoundaryTransposition;
                        shell.Uncertainty.Add(
                            "transposition: repeated allowed-information state fingerprint");
                    }
                    continue;
                }

                seenStateFingerprints.Add(candidate.Fingerprint);
                LlmSearchLine line = candidate.ToLine();
                lines.Add(line);
                continuationCount++;
                boundaryNodes++;
                rootsExpanded.Add(candidate.RootActionId);
                // Clear block reason if later expanded.
                rootBlockReason.Remove(candidate.RootActionId);
            }

            // Annotate every candidacy root that received no continuation.
            foreach (int rootId in rootsWithCandidacy)
            {
                if (rootsExpanded.Contains(rootId))
                {
                    continue;
                }
                string reason;
                if (timeExhausted && !rootsAttempted.Contains(rootId))
                {
                    reason = "time_budget";
                }
                else if (timeExhausted && !rootsExpanded.Contains(rootId))
                {
                    reason = "time_budget";
                }
                else if (rootBlockReason.TryGetValue(rootId, out reason) && !string.IsNullOrEmpty(reason))
                {
                    // use stored
                }
                else if (beamPrunedRoots.Contains(rootId) && !rootsAttempted.Contains(rootId))
                {
                    reason = "beam_width";
                }
                else
                {
                    reason = "budget_incomplete";
                }
                if (timeExhausted)
                {
                    status = "budget_incomplete";
                }
                AnnotateNonExpansion(lines, graph, rootId, reason, clock.ElapsedMilliseconds, maxWall);
            }

            // No-candidate annotations for material-setup roots that never expanded.
            foreach (int rootId in rootOrder)
            {
                LegalAction root;
                if (!rootsById.TryGetValue(rootId, out root) || root == null)
                {
                    continue;
                }
                if (!LlmTacticalAffordanceGraph.IsMaterialSetupAction(root))
                {
                    continue;
                }
                bool hasSetupLine = false;
                foreach (LlmSearchLine line in lines)
                {
                    if (line != null
                        && line.RootActionId == rootId
                        && !line.IsRootShell
                        && line.Provenance == LlmTacticalAffordanceGraph.ProvenanceRulesInferred)
                    {
                        hasSetupLine = true;
                        break;
                    }
                }
                if (hasSetupLine)
                {
                    continue;
                }
                string reason = null;
                string synchroReason = LlmTacticalAffordanceGraph.AnalyzeSynchroNoCandidateReason(
                    self, root);
                bool synchroRelevant = LlmTacticalAffordanceGraph.IsSynchroFamilyRelevant(self, root);
                if (synchroRelevant && !string.IsNullOrEmpty(synchroReason))
                {
                    // Prefer Synchro-specific evidence when the family is grounded in state.
                    reason = synchroReason;
                }
                else if (!LlmTacticalAffordanceGraph.RootHasGroundedLevel4Material(root))
                {
                    if (root.Card == null || root.Card.Level <= 0)
                    {
                        reason = "missing_metadata";
                    }
                    else if (!string.IsNullOrEmpty(synchroReason))
                    {
                        reason = synchroReason;
                    }
                    else
                    {
                        reason = "insufficient_materials";
                    }
                }
                else
                {
                    reason = LlmTacticalAffordanceGraph.AnalyzeRank4NoCandidateReason(self);
                    // If Rank4 has no failure string but Synchro does, surface Synchro reason.
                    if (string.IsNullOrEmpty(reason) && !string.IsNullOrEmpty(synchroReason))
                    {
                        reason = synchroReason;
                    }
                }
                if (string.IsNullOrEmpty(reason))
                {
                    // Had candidacy but lost to budget/transposition — NonExpansionReason already set.
                    continue;
                }
                LlmSearchLine shell = FindRootShell(lines, rootId);
                if (shell != null)
                {
                    shell.NoCandidateReason = reason;
                    shell.ExclusionReason = reason;
                    shell.Boundary = "no_candidate:" + reason;
                    shell.Uncertainty.Add(reason);
                    if (graph.Coverage != null)
                    {
                        graph.Coverage.NoCandidateReasons.Add(reason);
                        graph.Coverage.ExclusionReasons.Add(reason);
                        graph.Coverage.RootExclusions.Add("root:" + rootId + ":" + reason);
                    }
                }
            }

            lines.Sort(CompareLines);

            if (graph.Coverage == null)
            {
                graph.Coverage = new LlmSearchCoverage();
            }
            graph.Coverage.ContinuationExpansionCount = continuationCount;
            graph.Coverage.ExpandedNodes = continuationCount;
            graph.Coverage.PrunedNodes = pruned;
            graph.Coverage.TranspositionPrunes = transpositionPrunes;
            graph.Coverage.BoundaryNodes = boundaryNodes;
            graph.Coverage.ElapsedMs = clock.ElapsedMilliseconds;
            graph.Coverage.RepresentedRootActions = CountRepresentedRoots(lines);
            graph.Status = status;
            graph.Lines = lines;
            graph.Limits = limits.Clone();
            graph.Limits.Clock = limits.Clock;
            graph.Freeze();
            return graph;
        }

        static void SetBlockReason(Dictionary<int, string> map, int rootId, string reason)
        {
            if (!map.ContainsKey(rootId))
            {
                map[rootId] = reason;
            }
        }

        static void AnnotateNonExpansion(
            List<LlmSearchLine> lines,
            LlmSearchGraph graph,
            int rootId,
            string reason,
            long elapsedMs,
            long maxWallMs)
        {
            LlmSearchLine shell = FindRootShell(lines, rootId);
            if (shell != null && string.IsNullOrEmpty(shell.NonExpansionReason))
            {
                shell.NonExpansionReason = reason;
            }
            if (graph == null || graph.Coverage == null)
            {
                return;
            }
            string entry = "root:" + rootId + ":" + reason;
            if (!graph.Coverage.NonExpansionReasons.Contains(entry))
            {
                graph.Coverage.NonExpansionReasons.Add(entry);
            }
            if (reason == "time_budget")
            {
                string tel = "root:" + rootId + ":time_budget:elapsed_ms=" + elapsedMs
                    + ":max_wall_ms=" + maxWallMs;
                if (!graph.Coverage.PerRootBudgetTelemetry.Contains(tel))
                {
                    graph.Coverage.PerRootBudgetTelemetry.Add(tel);
                }
            }
        }

        static void CollectContinuationCandidates(
            LegalAction root,
            DecisionSnapshot snapshot,
            LlmSelfResources self,
            List<PendingContinuation> pending)
        {
            if (root == null)
            {
                return;
            }
            if (LlmTacticalAffordanceGraph.IsUnsupportedMultiSelect(root)
                || LlmTacticalAffordanceGraph.IsUnknownOrMandatoryPrompt(root))
            {
                return;
            }

            // Rank-4 from grounded flip/normal only. RootMeetsRank4MaterialAfterRoot is the
            // single candidacy gate (no duplicated HasRank4Candidacy(self)).
            if (LlmTacticalAffordanceGraph.RootMeetsRank4MaterialAfterRoot(self, root))
            {
                int fu = LlmTacticalAffordanceGraph.CountFaceUpLevel4(self);
                int after = fu + 1;
                string fp = LlmTacticalAffordanceGraph.FingerprintContinuation(
                    root, "rank4_xyz", self, snapshot);
                string rootLabel = !string.IsNullOrEmpty(root.ActionLabel)
                    ? root.ActionLabel
                    : LlmTacticalAffordanceGraph.DescribeAction(root);
                string extraName = LlmTacticalAffordanceGraph.FirstRank4ExtraName(self);
                pending.Add(new PendingContinuation()
                {
                    RootActionId = root.ActionId,
                    Kind = "rank4_xyz",
                    StrategicDepth = 1,
                    Score = 100 + after * 10,
                    Fingerprint = fp,
                    LineId = "line:reverse-to-rank4:root:" + root.ActionId,
                    RootLabel = rootLabel,
                    ExtraName = extraName,
                });
            }

            // Synchro candidacy: 1 tuner + 1+ non-tuners, exact level-sum to ED Synchro.
            // Not for engine-current SummonSp (IsMaterialSetupAction already excludes it).
            if (LlmTacticalAffordanceGraph.IsMaterialSetupAction(root)
                && root.Command != DuelCommandType.SummonSp)
            {
                List<LlmSynchroCandidateSpec> synSpecs =
                    LlmTacticalAffordanceGraph.EnumerateSynchroCandidates(self, root);
                if (synSpecs != null)
                {
                    string rootLabel = !string.IsNullOrEmpty(root.ActionLabel)
                        ? root.ActionLabel
                        : LlmTacticalAffordanceGraph.DescribeAction(root);
                    foreach (LlmSynchroCandidateSpec spec in synSpecs)
                    {
                        if (spec == null || spec.Extra == null || spec.Materials == null)
                        {
                            continue;
                        }
                        string materialKey = spec.MaterialKey();
                        int extraId = spec.Extra.CardId;
                        string extraName = spec.Extra.Name;
                        // Distinct fingerprint per material set + Extra Deck identity.
                        string fpBase = LlmTacticalAffordanceGraph.FingerprintContinuation(
                            root, "synchro", self, snapshot);
                        // Include full material source keys so same-CardId copies stay distinct.
                        string fp = fpBase + "|m:" + materialKey + "|e:" + extraId;
                        // Resource-efficient scoring: same Extra target prefers fewer materials.
                        // setup_base + level_cap + unlock, minus material resource cost.
                        int materialCount = spec.Materials.Count;
                        int resourceCost = materialCount * 12; // negative feature value below
                        int score = 100 + Math.Min(spec.LevelSum, 20) + 40 - resourceCost;
                        if (score < 1)
                        {
                            score = 1; // keep setup value positive overall
                        }
                        pending.Add(new PendingContinuation()
                        {
                            RootActionId = root.ActionId,
                            Kind = "synchro",
                            StrategicDepth = 1,
                            Score = score,
                            Fingerprint = fp,
                            LineId = "line:synchro:root:" + root.ActionId
                                + ":m:" + materialKey + ":e:" + extraId,
                            RootLabel = rootLabel,
                            ExtraName = extraName,
                            ExtraCardId = extraId,
                            SynchroMaterials = spec.Materials,
                            SynchroLevelSum = spec.LevelSum,
                            SynchroResourceCost = -resourceCost,
                        });
                    }
                }
            }

            if (LlmTacticalAffordanceGraph.IsBattlePhaseRoot(root)
                || LlmTacticalAffordanceGraph.IsAttackRoot(root))
            {
                string fpBattle = LlmTacticalAffordanceGraph.FingerprintContinuation(
                    root, "battle_sequence", self, snapshot);
                pending.Add(new PendingContinuation()
                {
                    RootActionId = root.ActionId,
                    Kind = "battle_sequence",
                    StrategicDepth = 1,
                    Score = 80,
                    Fingerprint = fpBattle,
                    LineId = "line:battle-sequence:root:" + root.ActionId,
                    RootLabel = !string.IsNullOrEmpty(root.ActionLabel)
                        ? root.ActionLabel
                        : LlmTacticalAffordanceGraph.DescribeAction(root),
                });

                string fpMain2 = LlmTacticalAffordanceGraph.FingerprintContinuation(
                    root, "main2_continuation", self, snapshot);
                pending.Add(new PendingContinuation()
                {
                    RootActionId = root.ActionId,
                    Kind = "main2_continuation",
                    StrategicDepth = 1,
                    Score = 70,
                    Fingerprint = fpMain2,
                    LineId = "line:main2-continuation:root:" + root.ActionId,
                    RootLabel = !string.IsNullOrEmpty(root.ActionLabel)
                        ? root.ActionLabel
                        : LlmTacticalAffordanceGraph.DescribeAction(root),
                });
            }
        }

        static int ComparePending(PendingContinuation a, PendingContinuation b)
        {
            if (a == null && b == null)
            {
                return 0;
            }
            if (a == null)
            {
                return 1;
            }
            if (b == null)
            {
                return -1;
            }
            int c = b.Score.CompareTo(a.Score);
            if (c != 0)
            {
                return c;
            }
            c = a.RootActionId.CompareTo(b.RootActionId);
            if (c != 0)
            {
                return c;
            }
            c = string.CompareOrdinal(a.Kind, b.Kind);
            if (c != 0)
            {
                return c;
            }
            return string.CompareOrdinal(a.LineId ?? string.Empty, b.LineId ?? string.Empty);
        }

        static int CompareLines(LlmSearchLine a, LlmSearchLine b)
        {
            if (a == null && b == null)
            {
                return 0;
            }
            if (a == null)
            {
                return 1;
            }
            if (b == null)
            {
                return -1;
            }
            int c = a.RootActionId.CompareTo(b.RootActionId);
            if (c != 0)
            {
                return c;
            }
            if (a.IsRootShell != b.IsRootShell)
            {
                return a.IsRootShell ? -1 : 1;
            }
            c = b.Score.CompareTo(a.Score);
            if (c != 0)
            {
                return c;
            }
            return string.CompareOrdinal(a.LineId, b.LineId);
        }

        static LlmSearchLine FindRootShell(List<LlmSearchLine> lines, int rootId)
        {
            foreach (LlmSearchLine line in lines)
            {
                if (line != null && line.IsRootShell && line.RootActionId == rootId)
                {
                    return line;
                }
            }
            return null;
        }

        static int CountRepresentedRoots(List<LlmSearchLine> lines)
        {
            HashSet<int> seen = new HashSet<int>();
            foreach (LlmSearchLine line in lines)
            {
                if (line != null)
                {
                    seen.Add(line.RootActionId);
                }
            }
            return seen.Count;
        }

        static LlmSearchLine CloneLineMutable(LlmSearchLine src)
        {
            if (src == null)
            {
                return null;
            }
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
                ExclusionReason = src.ExclusionReason,
                NoCandidateReason = src.NoCandidateReason,
                PruneReason = src.PruneReason,
                NonExpansionReason = src.NonExpansionReason,
                ImmediateOutcome = src.ImmediateOutcome,
            };
            if (src.ScoreFeatures != null)
            {
                foreach (KeyValuePair<string, int> pair in src.ScoreFeatures)
                {
                    copy.ScoreFeatures[pair.Key] = pair.Value;
                }
            }
            if (src.Steps != null)
            {
                foreach (LlmSearchStep step in src.Steps)
                {
                    if (step == null)
                    {
                        continue;
                    }
                    copy.Steps.Add(new LlmSearchStep()
                    {
                        Label = step.Label,
                        CurrentLegal = step.CurrentLegal,
                        CommitEligible = step.CommitEligible,
                        Provenance = step.Provenance,
                        Fingerprint = step.Fingerprint,
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
            return copy;
        }

        sealed class PendingContinuation
        {
            public int RootActionId;
            public string Kind;
            public int StrategicDepth;
            public int Score;
            public string Fingerprint;
            public string LineId;
            public string RootLabel;
            public string ExtraName;
            public int ExtraCardId;
            public List<LlmSynchroMaterial> SynchroMaterials;
            public int SynchroLevelSum;
            public int SynchroResourceCost;

            public LlmSearchLine ToLine()
            {
                LlmSearchLine line = new LlmSearchLine()
                {
                    LineId = LineId,
                    RootActionId = RootActionId,
                    Provenance = LlmTacticalAffordanceGraph.ProvenanceRulesInferred,
                    CommitEligible = false,
                    StrategicDepth = StrategicDepth,
                    Score = Score,
                    Fingerprint = Fingerprint,
                    IsRootShell = false,
                    Boundary = LlmTacticalAffordanceGraph.BoundaryOpponent,
                };
                line.Steps.Add(new LlmSearchStep()
                {
                    Label = RootLabel,
                    CurrentLegal = true,
                    CommitEligible = true,
                    Provenance = LlmTacticalAffordanceGraph.ProvenanceEngineCurrent,
                });

                if (Kind == "rank4_xyz")
                {
                    line.Steps.Add(new LlmSearchStep()
                    {
                        Label = "Potential Rank 4 Xyz Summon",
                        CurrentLegal = false,
                        CommitEligible = false,
                        Provenance = LlmTacticalAffordanceGraph.ProvenanceRulesInferred,
                        Fingerprint = Fingerprint,
                    });
                    line.Unlocks.Add("known face-up Level 4 count may meet Rank 4 material count");
                    if (!string.IsNullOrEmpty(ExtraName))
                    {
                        line.Unlocks.Add("controlled Extra Deck has Rank 4 candidate: " + ExtraName);
                    }
                    line.Uncertainty.Add("future summon legality requires engine confirmation");
                    line.Uncertainty.Add(
                        "material restrictions, zones, usage limits, and responses are unchecked");
                }
                else if (Kind == "synchro")
                {
                    string extraLabel = !string.IsNullOrEmpty(ExtraName)
                        ? ExtraName
                        : ("card_" + ExtraCardId);
                    line.Steps.Add(new LlmSearchStep()
                    {
                        Label = "Potential Synchro Summon " + extraLabel,
                        CurrentLegal = false,
                        CommitEligible = false,
                        Provenance = LlmTacticalAffordanceGraph.ProvenanceRulesInferred,
                        Fingerprint = Fingerprint,
                    });
                    // Exact material identities (name + id), sorted by card id in Materials list.
                    if (SynchroMaterials != null)
                    {
                        foreach (LlmSynchroMaterial m in SynchroMaterials)
                        {
                            if (m != null)
                            {
                                line.Unlocks.Add(m.IdentityUnlock());
                            }
                        }
                    }
                    line.Unlocks.Add(
                        "controlled Extra Deck has Synchro candidate: "
                        + extraLabel + " (" + ExtraCardId + ")");
                    line.Unlocks.Add(
                        "known tuner + non-tuner level-sum " + SynchroLevelSum
                        + " may meet Synchro level");
                    line.ScoreFeatures["synchro_setup_unlock"] = Score > 0 ? Score : 1;
                    line.ScoreFeatures["setup_unlock"] = Score > 0 ? Score : 1;
                    // Negative resource cost: more materials ⇒ more-negative cost feature.
                    line.ScoreFeatures["synchro_resource_cost"] = SynchroResourceCost;
                    line.ScoreFeatures["resource_cost"] = SynchroResourceCost;
                    line.Uncertainty.Add("future summon legality requires engine confirmation");
                    line.Uncertainty.Add(
                        "material restrictions, zones, usage limits, and responses are unchecked");
                    line.Boundary = LlmTacticalAffordanceGraph.BoundaryOpponent;
                }
                else if (Kind == "battle_sequence")
                {
                    line.Steps.Add(new LlmSearchStep()
                    {
                        Label = "Potential attack declaration sequence",
                        CurrentLegal = false,
                        CommitEligible = false,
                        Provenance = LlmTacticalAffordanceGraph.ProvenanceRulesInferred,
                        Fingerprint = Fingerprint,
                    });
                    line.Unlocks.Add("entering Battle may unlock attack declarations");
                    line.Uncertainty.Add("attack targets and battle outcomes require engine confirmation");
                    line.Uncertainty.Add("opponent responses may interrupt; do not assume a pass");
                    line.Boundary = LlmTacticalAffordanceGraph.BoundaryOpponent;
                }
                else if (Kind == "main2_continuation")
                {
                    line.Steps.Add(new LlmSearchStep()
                    {
                        Label = "Potential Main Phase 2 continuation",
                        CurrentLegal = false,
                        CommitEligible = false,
                        Provenance = LlmTacticalAffordanceGraph.ProvenanceRulesInferred,
                        Fingerprint = Fingerprint,
                    });
                    line.Unlocks.Add("Battle may lead to Main Phase 2 actions after combat");
                    line.Uncertainty.Add("Main2 legality requires a fresh engine window");
                    line.Uncertainty.Add("opponent responses and battle results are unchecked");
                    line.Boundary = LlmTacticalAffordanceGraph.BoundaryOpponent;
                }
                return line;
            }
        }
    }
}

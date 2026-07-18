using System;
using System.Collections.Generic;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Deterministic combo-template matcher. Builds/augments Layer A graph from
    /// LlmTacticalAffordanceGraph + LlmBoundedLineSearch without weakening generic candidates.
    /// Template continuations obey the same depth/node/beam/wall budgets and coverage accounting.
    /// </summary>
    static class LlmComboTemplateMatcher
    {
        internal const string TemplateDepthBudget = "template_depth_budget";
        internal const string TemplateNodeBudget = "template_node_budget";
        internal const string TemplateTimeBudget = "template_time_budget";
        internal const string TemplateBeamWidth = "template_beam_width";

        public static LlmSearchGraph ExpandToGraph(
            DecisionSnapshot snapshot,
            LlmSelfResources selfResources,
            LlmSearchLimits limits)
        {
            if (limits == null)
            {
                limits = LlmSearchLimits.CreateDefault();
            }

            ILlmSearchBudgetClock clock = limits.Clock ?? new LlmSearchSystemBudgetClock();

            LlmSearchGraph graph = LlmTacticalAffordanceGraph.Build(snapshot, selfResources, limits);
            graph = LlmBoundedLineSearch.Search(graph, limits);

            // Unfreeze lines for augmentation (Search freezes the graph).
            List<LlmSearchLine> lines = new List<LlmSearchLine>();
            if (graph.Lines != null)
            {
                foreach (LlmSearchLine line in graph.Lines)
                {
                    if (line != null)
                    {
                        lines.Add(CloneLine(line));
                    }
                }
            }

            // Mutable Coverage clone so list telemetry remains writable after Search Freeze.
            LlmSearchCoverage coverage = CloneCoverageMutable(graph.Coverage);
            graph.Coverage = coverage;

            // Effective budgets (0 MaxNodes => default hard cap, same as Layer A Search).
            int maxDepth = limits.MaxStrategicDepth;
            int maxNodes = limits.MaxNodes > 0 ? limits.MaxNodes : LlmSearchLimits.DefaultMaxNodes;
            int beam = limits.BeamWidth > 0 ? limits.BeamWidth : LlmSearchLimits.DefaultBeamWidth;
            long maxWall = limits.MaxWallMs > 0 ? limits.MaxWallMs : LlmSearchLimits.DefaultMaxWallMs;
            if (graph.Limits != null)
            {
                maxDepth = graph.Limits.MaxStrategicDepth;
                maxNodes = graph.Limits.MaxNodes > 0
                    ? graph.Limits.MaxNodes
                    : LlmSearchLimits.DefaultMaxNodes;
                beam = graph.Limits.BeamWidth > 0
                    ? graph.Limits.BeamWidth
                    : LlmSearchLimits.DefaultBeamWidth;
                maxWall = graph.Limits.MaxWallMs > 0
                    ? graph.Limits.MaxWallMs
                    : LlmSearchLimits.DefaultMaxWallMs;
            }

            int continuationCount = coverage.ContinuationExpansionCount;
            int pruned = coverage.PrunedNodes;
            int boundaryNodes = coverage.BoundaryNodes;
            string status = graph.Status ?? "budget_complete";
            Dictionary<int, int> contPerRoot = CountContinuationsPerRoot(lines);

            bool timeExhausted = clock.ElapsedMilliseconds >= maxWall;
            if (timeExhausted)
            {
                status = "budget_incomplete";
            }

            LlmComboTemplateCatalog catalog = LlmComboTemplateCatalog.CreateDefault();
            if (snapshot != null && selfResources != null && catalog.Templates != null)
            {
                foreach (LlmComboTemplate template in catalog.Templates)
                {
                    if (template == null)
                    {
                        continue;
                    }

                    // Structural match only — non-matching templates are silent (no spam).
                    LlmSearchLine candidate = TryEmitTemplateLine(snapshot, selfResources, template);
                    if (candidate == null || HasDuplicateTemplate(lines, candidate))
                    {
                        continue;
                    }

                    if (clock.ElapsedMilliseconds >= maxWall || timeExhausted)
                    {
                        timeExhausted = true;
                        status = "budget_incomplete";
                        RecordTemplateOmit(coverage, candidate, TemplateTimeBudget, ref pruned);
                        continue;
                    }
                    if (maxDepth < 1)
                    {
                        status = "budget_incomplete";
                        RecordTemplateOmit(coverage, candidate, TemplateDepthBudget, ref pruned);
                        continue;
                    }
                    if (continuationCount >= maxNodes)
                    {
                        status = "budget_incomplete";
                        RecordTemplateOmit(coverage, candidate, TemplateNodeBudget, ref pruned);
                        continue;
                    }

                    int rootId = candidate.RootActionId;
                    int rootCont;
                    contPerRoot.TryGetValue(rootId, out rootCont);
                    if (rootCont >= beam)
                    {
                        // Beam is a soft omit for templates (does not force incomplete alone).
                        RecordTemplateOmit(coverage, candidate, TemplateBeamWidth, ref pruned);
                        continue;
                    }

                    lines.Add(candidate);
                    continuationCount++;
                    contPerRoot[rootId] = rootCont + 1;
                    // Fresh-window template boundary (opponent_response_or_fresh_engine_window).
                    if (string.Equals(
                        candidate.Boundary,
                        LlmTacticalAffordanceGraph.BoundaryOpponent,
                        StringComparison.Ordinal))
                    {
                        boundaryNodes++;
                    }
                    coverage.PerRootBudgetTelemetry.Add(
                        "template_emitted:root:" + rootId + ":" + (template.TemplateId ?? string.Empty));
                }
            }

            coverage.ContinuationExpansionCount = continuationCount;
            coverage.ExpandedNodes = continuationCount;
            coverage.PrunedNodes = pruned;
            coverage.BoundaryNodes = boundaryNodes;
            coverage.ElapsedMs = clock.ElapsedMilliseconds;
            graph.Status = status;
            graph.Lines = lines;
            graph.SourceSnapshot = snapshot;
            graph.SourceSelfResources = selfResources;
            graph.Freeze();
            return graph;
        }

        static void RecordTemplateOmit(
            LlmSearchCoverage coverage,
            LlmSearchLine candidate,
            string reason,
            ref int pruned)
        {
            pruned++;
            if (coverage == null || string.IsNullOrEmpty(reason))
            {
                return;
            }
            if (!coverage.PruningReasons.Contains(reason))
            {
                coverage.PruningReasons.Add(reason);
            }
            string tid = candidate != null ? (candidate.TemplateId ?? string.Empty) : string.Empty;
            int rootId = candidate != null ? candidate.RootActionId : -1;
            string nonExp = "template:" + tid + ":" + reason;
            if (!coverage.NonExpansionReasons.Contains(nonExp))
            {
                coverage.NonExpansionReasons.Add(nonExp);
            }
            string perRoot = "root:" + rootId + ":" + reason;
            if (!coverage.PerRootBudgetTelemetry.Contains(perRoot))
            {
                coverage.PerRootBudgetTelemetry.Add(perRoot);
            }
        }

        static LlmSearchCoverage CloneCoverageMutable(LlmSearchCoverage src)
        {
            LlmSearchCoverage c = new LlmSearchCoverage();
            if (src == null)
            {
                return c;
            }
            c.LegalRootActions = src.LegalRootActions;
            c.RepresentedRootActions = src.RepresentedRootActions;
            c.RootShellCount = src.RootShellCount;
            c.ExpandedNodes = src.ExpandedNodes;
            c.ContinuationExpansionCount = src.ContinuationExpansionCount;
            c.PrunedNodes = src.PrunedNodes;
            c.BoundaryNodes = src.BoundaryNodes;
            c.TranspositionPrunes = src.TranspositionPrunes;
            c.ElapsedMs = src.ElapsedMs;
            CopyStringList(src.RootExclusions, c.RootExclusions);
            CopyStringList(src.ExclusionReasons, c.ExclusionReasons);
            CopyStringList(src.NoCandidateReasons, c.NoCandidateReasons);
            CopyStringList(src.PruningReasons, c.PruningReasons);
            CopyStringList(src.NonExpansionReasons, c.NonExpansionReasons);
            CopyStringList(src.PerRootBudgetTelemetry, c.PerRootBudgetTelemetry);
            return c;
        }

        static void CopyStringList(IList<string> src, IList<string> dst)
        {
            if (src == null || dst == null)
            {
                return;
            }
            foreach (string s in src)
            {
                if (s != null)
                {
                    dst.Add(s);
                }
            }
        }

        static Dictionary<int, int> CountContinuationsPerRoot(List<LlmSearchLine> lines)
        {
            Dictionary<int, int> d = new Dictionary<int, int>();
            if (lines == null)
            {
                return d;
            }
            foreach (LlmSearchLine line in lines)
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

        static bool HasDuplicateTemplate(List<LlmSearchLine> lines, LlmSearchLine candidate)
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.TemplateId))
            {
                return false;
            }
            foreach (LlmSearchLine line in lines)
            {
                if (line != null
                    && string.Equals(line.TemplateId, candidate.TemplateId, StringComparison.Ordinal)
                    && line.RootActionId == candidate.RootActionId)
                {
                    return true;
                }
            }
            return false;
        }

        static LlmSearchLine TryEmitTemplateLine(
            DecisionSnapshot snapshot,
            LlmSelfResources self,
            LlmComboTemplate template)
        {
            if (string.Equals(template.SummonFamily, "xyz", StringComparison.OrdinalIgnoreCase))
            {
                return TryEmitXyz(snapshot, self, template);
            }
            if (string.Equals(template.SummonFamily, "synchro", StringComparison.OrdinalIgnoreCase))
            {
                return TryEmitSynchro(snapshot, self, template);
            }
            return null;
        }

        static LlmSearchLine TryEmitXyz(
            DecisionSnapshot snapshot,
            LlmSelfResources self,
            LlmComboTemplate template)
        {
            if (self == null || self.Field == null || self.ExtraDeck == null)
            {
                return null;
            }
            if (!HasExtraTargetXyz(self, template.ExtraDeckTargetCardId, template.RequiredRank))
            {
                return null;
            }

            LegalAction root = FindRoot(snapshot, (DuelCommandType)template.SetupRootCommand, template.SetupRootCardId);
            if (root == null)
            {
                return null;
            }

            int partnerId = 0;
            foreach (int mid in template.MaterialCardIds ?? new int[0])
            {
                if (mid != template.SetupRootCardId)
                {
                    partnerId = mid;
                    break;
                }
            }
            if (partnerId <= 0)
            {
                return null;
            }

            LlmSelfResourceCard partner = FindFieldCard(self, partnerId, requireFaceUp: true);
            if (partner == null)
            {
                return null;
            }

            LlmSelfResourceCard rootMat = FindFieldAt(self, root.Position, root.Index);
            if (rootMat == null
                || rootMat.CardId != template.SetupRootCardId
                || rootMat.IsFaceUp)
            {
                return null;
            }

            int requiredLevel = template.RequiredRank > 0 ? template.RequiredRank : 4;
            if (!HasGroundedLevel(partner, requiredLevel) || !HasGroundedLevel(rootMat, requiredLevel))
            {
                return null;
            }

            if (partner.Zone == root.Position && partner.Index == root.Index)
            {
                return null;
            }

            List<int> ordered = new List<int>() { template.SetupRootCardId, partnerId };
            List<LlmMaterialSource> sources = new List<LlmMaterialSource>()
            {
                new LlmMaterialSource()
                {
                    Zone = root.Position,
                    Index = root.Index,
                    CardId = template.SetupRootCardId,
                    IsFaceUp = false,
                },
                new LlmMaterialSource()
                {
                    Zone = partner.Zone,
                    Index = partner.Index,
                    CardId = partnerId,
                    IsFaceUp = true,
                },
            };

            return BuildTemplateLine(
                template,
                root.ActionId,
                "Flip setup -> Rank " + template.RequiredRank + " Xyz",
                "Xyz Summon ED " + template.ExtraDeckTargetCardId,
                ordered,
                sources,
                rank: template.RequiredRank,
                level: 0);
        }

        static LlmSearchLine TryEmitSynchro(
            DecisionSnapshot snapshot,
            LlmSelfResources self,
            LlmComboTemplate template)
        {
            if (self == null || self.Field == null || self.ExtraDeck == null)
            {
                return null;
            }
            if (!HasExtraTargetSynchro(self, template.ExtraDeckTargetCardId, template.RequiredLevel))
            {
                return null;
            }

            int tunerId = 0;
            if (template.MaterialCardIds != null)
            {
                foreach (int mid in template.MaterialCardIds)
                {
                    if (mid > 0)
                    {
                        tunerId = mid;
                        break;
                    }
                }
            }
            if (tunerId <= 0)
            {
                tunerId = 7687;
            }

            LlmSelfResourceCard tuner = null;
            foreach (LlmSelfResourceCard c in self.Field)
            {
                if (c == null)
                {
                    continue;
                }
                if (c.IsFaceUp && c.IsTuner == true && c.CardId == tunerId)
                {
                    tuner = c;
                    break;
                }
            }
            if (tuner == null || !tuner.Level.HasValue || tuner.Level.Value <= 0)
            {
                return null;
            }

            int requiredSum = template.RequiredLevel > 0 ? template.RequiredLevel : 0;
            if (requiredSum <= 0)
            {
                return null;
            }

            LegalAction root = null;
            if (snapshot != null && snapshot.LegalActions != null)
            {
                foreach (LegalAction a in snapshot.LegalActions)
                {
                    if (a == null || a.Kind != LegalActionKind.Command)
                    {
                        continue;
                    }
                    if (a.Command != DuelCommandType.Summon)
                    {
                        continue;
                    }
                    int cid = a.CardId > 0 ? a.CardId : (a.Card != null ? a.Card.CardId : 0);
                    int lvl = a.Card != null ? a.Card.Level : 0;
                    bool isTuner = a.Card != null && a.Card.IsTuner;
                    if (cid <= 0 || lvl <= 0 || isTuner)
                    {
                        continue;
                    }
                    int sum = tuner.Level.Value + lvl;
                    if (sum == requiredSum)
                    {
                        root = a;
                        break;
                    }
                }
            }
            if (root == null)
            {
                return null;
            }

            int rootCid = root.CardId > 0 ? root.CardId : (root.Card != null ? root.Card.CardId : 0);
            int rootLvl = root.Card != null ? root.Card.Level : 0;
            List<int> ordered = new List<int>() { tuner.CardId, rootCid };
            List<LlmMaterialSource> sources = new List<LlmMaterialSource>()
            {
                new LlmMaterialSource()
                {
                    Zone = tuner.Zone,
                    Index = tuner.Index,
                    CardId = tuner.CardId,
                    IsFaceUp = true,
                },
                new LlmMaterialSource()
                {
                    Zone = root.Position,
                    Index = root.Index,
                    CardId = rootCid,
                    IsFaceUp = true,
                },
            };
            if (sources[0].IdentityKey == sources[1].IdentityKey)
            {
                return null;
            }

            return BuildTemplateLine(
                template,
                root.ActionId,
                "Summon non-tuner -> Synchro",
                "Synchro Summon ED " + template.ExtraDeckTargetCardId,
                ordered,
                sources,
                rank: 0,
                level: tuner.Level.Value + rootLvl);
        }

        static bool HasGroundedLevel(LlmSelfResourceCard card, int requiredLevel)
        {
            if (card == null || requiredLevel <= 0)
            {
                return false;
            }
            return card.Level.HasValue && card.Level.Value == requiredLevel;
        }

        static bool HasExtraTargetXyz(LlmSelfResources self, int edId, int requiredRank)
        {
            if (self.ExtraDeck == null || edId <= 0)
            {
                return false;
            }
            foreach (LlmSelfResourceExtraDeckEntry e in self.ExtraDeck)
            {
                if (e == null || e.CardId != edId)
                {
                    continue;
                }
                string fam = (e.SummonFamily ?? string.Empty).ToLowerInvariant();
                string frame = (e.Frame ?? string.Empty).ToLowerInvariant();
                bool familyOk = fam.IndexOf("xyz", StringComparison.Ordinal) >= 0
                    || frame.IndexOf("xyz", StringComparison.Ordinal) >= 0;
                bool rankOk = e.UsesRank
                    && e.Rank.HasValue
                    && requiredRank > 0
                    && e.Rank.Value == requiredRank;
                if (familyOk && rankOk)
                {
                    return true;
                }
            }
            return false;
        }

        static bool HasExtraTargetSynchro(LlmSelfResources self, int edId, int requiredLevel)
        {
            if (self.ExtraDeck == null || edId <= 0 || requiredLevel <= 0)
            {
                return false;
            }
            foreach (LlmSelfResourceExtraDeckEntry e in self.ExtraDeck)
            {
                if (e == null || e.CardId != edId)
                {
                    continue;
                }
                string fam = (e.SummonFamily ?? string.Empty).ToLowerInvariant();
                string frame = (e.Frame ?? string.Empty).ToLowerInvariant();
                bool familyOk = fam.IndexOf("synchro", StringComparison.Ordinal) >= 0
                    || frame.IndexOf("synchro", StringComparison.Ordinal) >= 0;
                bool levelOk = e.Level.HasValue && e.Level.Value == requiredLevel;
                if (familyOk && levelOk)
                {
                    return true;
                }
            }
            return false;
        }

        static LegalAction FindRoot(DecisionSnapshot snapshot, DuelCommandType command, int cardId)
        {
            if (snapshot == null || snapshot.LegalActions == null)
            {
                return null;
            }
            foreach (LegalAction a in snapshot.LegalActions)
            {
                if (a == null || a.Kind != LegalActionKind.Command)
                {
                    continue;
                }
                if (a.Command != command)
                {
                    continue;
                }
                int cid = a.CardId > 0 ? a.CardId : (a.Card != null ? a.Card.CardId : 0);
                if (cardId <= 0 || cid == cardId)
                {
                    return a;
                }
            }
            return null;
        }

        static LlmSelfResourceCard FindFieldCard(LlmSelfResources self, int cardId, bool requireFaceUp)
        {
            foreach (LlmSelfResourceCard c in self.Field)
            {
                if (c == null || c.CardId != cardId)
                {
                    continue;
                }
                if (requireFaceUp && !c.IsFaceUp)
                {
                    continue;
                }
                return c;
            }
            return null;
        }

        static LlmSelfResourceCard FindFieldAt(LlmSelfResources self, int zone, int index)
        {
            foreach (LlmSelfResourceCard c in self.Field)
            {
                if (c != null && c.Zone == zone && c.Index == index)
                {
                    return c;
                }
            }
            return null;
        }

        static LlmSearchLine BuildTemplateLine(
            LlmComboTemplate template,
            int rootActionId,
            string rootLabel,
            string futureLabel,
            List<int> orderedMats,
            List<LlmMaterialSource> sources,
            int rank,
            int level)
        {
            LlmSummonRequirement req = new LlmSummonRequirement()
            {
                SummonFamily = template.SummonFamily,
                Rank = rank,
                Level = level,
                MaterialCount = orderedMats != null ? orderedMats.Count : 0,
                ExtraDeckTargetCardId = template.ExtraDeckTargetCardId,
                OrderedMaterialCardIds = orderedMats ?? new List<int>(),
                MaterialSources = sources ?? new List<LlmMaterialSource>(),
            };

            LlmSearchLine line = new LlmSearchLine()
            {
                LineId = "tmpl:" + template.TemplateId + ":root:" + rootActionId,
                RootActionId = rootActionId,
                Provenance = LlmTacticalAffordanceGraph.ProvenanceRulesInferred,
                CommitEligible = false,
                StrategicDepth = 1,
                IsRootShell = false,
                TemplateId = template.TemplateId,
                SummonRequirement = req,
                Fingerprint = BuildFingerprint(template.TemplateId, rootActionId, orderedMats, sources),
                Boundary = LlmTacticalAffordanceGraph.BoundaryOpponent,
                Score = 40,
            };
            line.ScoreFeatures["template_setup_unlock"] = 40;
            line.Unlocks.Add(template.SummonFamily + ":" + template.ExtraDeckTargetCardId);
            line.Steps.Add(new LlmSearchStep()
            {
                Label = rootLabel,
                CurrentLegal = true,
                CommitEligible = true,
                Provenance = LlmTacticalAffordanceGraph.ProvenanceEngineCurrent,
            });
            line.Steps.Add(new LlmSearchStep()
            {
                Label = futureLabel,
                CurrentLegal = false,
                CommitEligible = false,
                Provenance = LlmTacticalAffordanceGraph.ProvenanceRulesInferred,
            });
            return line;
        }

        static string BuildFingerprint(
            string templateId, int rootId, List<int> mats, List<LlmMaterialSource> sources)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(templateId).Append('|').Append(rootId);
            if (mats != null)
            {
                foreach (int m in mats)
                {
                    sb.Append('|').Append(m);
                }
            }
            if (sources != null)
            {
                foreach (LlmMaterialSource s in sources)
                {
                    if (s != null)
                    {
                        sb.Append('|').Append(s.IdentityKey);
                    }
                }
            }
            return sb.ToString();
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
                ExclusionReason = src.ExclusionReason,
                NoCandidateReason = src.NoCandidateReason,
                PruneReason = src.PruneReason,
                NonExpansionReason = src.NonExpansionReason,
                ImmediateOutcome = src.ImmediateOutcome,
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
            return copy;
        }
    }
}

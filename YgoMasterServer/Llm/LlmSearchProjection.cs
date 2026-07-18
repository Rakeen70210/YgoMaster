using System;
using System.Collections.Generic;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Deterministic broker/audit projection of a Layer A search graph with hard UTF-8 byte budgets.
    /// Never drops depth-zero strategic root shells; fail closed if mandatory roots cannot fit.
    /// Values below <see cref="MinimumSupportedSerializedBytes"/> are rejected before projection
    /// (same contract pattern as self_resources) so callers never receive an oversized envelope.
    /// </summary>
    static class LlmSearchProjection
    {
        /// <summary>
        /// Smallest MaxSerializedBytes accepted by Project. Below this the fail-closed / compact
        /// floor cannot be guaranteed, so projection fails closed without emitting JSON larger
        /// than the requested budget.
        /// </summary>
        public const int MinimumSupportedSerializedBytes = 512;

        public static Dictionary<string, object> Project(LlmSearchGraph graph)
        {
            if (graph == null)
            {
                return new Dictionary<string, object>();
            }

            int maxBytes = graph.Limits != null && graph.Limits.MaxSerializedBytes > 0
                ? graph.Limits.MaxSerializedBytes
                : LlmSearchLimits.DefaultMaxSerializedBytes;

            string budgetError = LlmSearchLimits.ValidateSerializedBudgetOrError(maxBytes);
            if (budgetError != null)
            {
                throw new ArgumentOutOfRangeException(
                    "MaxSerializedBytes",
                    maxBytes,
                    budgetError
                    + "; impossible budgets fail closed rather than silently exceeding the limit.");
            }

            // Stage 0: full detail
            Dictionary<string, object> root = BuildProjection(graph, includeContinuations: true, compact: false);
            if (Fits(root, maxBytes))
            {
                return root;
            }

            // Stage 1: drop non-root continuation lines only
            root = BuildProjection(graph, includeContinuations: false, compact: false);
            root["budget_trimmed"] = "continuations";
            if (Fits(root, maxBytes))
            {
                return root;
            }

            // Stage 2: compact root shells (no unlock/uncertainty text)
            root = BuildProjection(graph, includeContinuations: false, compact: true);
            root["budget_trimmed"] = "compact_roots";
            if (Fits(root, maxBytes))
            {
                return root;
            }

            // Fail closed: mandatory roots cannot fit. Floor envelope is sized for MinimumSupported.
            Dictionary<string, object> failed = new Dictionary<string, object>()
            {
                { "search_id", graph.SearchId ?? string.Empty },
                { "root_run_effect_seq", graph.RootRunEffectSeq },
                { "status", "budget_failed" },
                { "error", "max_serialized_bytes_minimum_roots" },
                { "reason", "mandatory root projection exceeds MaxSerializedBytes" },
                { "max_serialized_bytes", maxBytes },
            };
            if (!Fits(failed, maxBytes))
            {
                throw new InvalidOperationException(
                    "search projection floor stub exceeds MaxSerializedBytes=" + maxBytes
                    + "; raise MaxSerializedBytes to at least MinimumSupportedSerializedBytes ("
                    + MinimumSupportedSerializedBytes + ").");
            }
            return failed;
        }

        public static string ProjectJson(LlmSearchGraph graph)
        {
            return MiniJSON.Json.Serialize(Project(graph));
        }

        static bool Fits(Dictionary<string, object> data, int maxBytes)
        {
            string json = MiniJSON.Json.Serialize(data);
            return Encoding.UTF8.GetByteCount(json) <= maxBytes;
        }

        static Dictionary<string, object> BuildProjection(
            LlmSearchGraph graph,
            bool includeContinuations,
            bool compact)
        {
            Dictionary<string, object> root = new Dictionary<string, object>()
            {
                { "search_id", graph.SearchId },
                { "root_run_effect_seq", graph.RootRunEffectSeq },
                { "status", graph.Status },
            };

            if (graph.Limits != null)
            {
                root["limits"] = new Dictionary<string, object>()
                {
                    { "max_strategic_depth", graph.Limits.MaxStrategicDepth },
                    { "max_nodes", graph.Limits.MaxNodes },
                    { "max_wall_ms", graph.Limits.MaxWallMs },
                    { "beam_width", graph.Limits.BeamWidth },
                    { "max_serialized_bytes", graph.Limits.MaxSerializedBytes },
                };
            }

            if (graph.Coverage != null)
            {
                Dictionary<string, object> coverage = new Dictionary<string, object>()
                {
                    { "legal_root_actions", graph.Coverage.LegalRootActions },
                    { "represented_root_actions", graph.Coverage.RepresentedRootActions },
                    { "root_shell_count", graph.Coverage.RootShellCount },
                    { "expanded_nodes", graph.Coverage.ExpandedNodes },
                    { "continuation_expansion_count", graph.Coverage.ContinuationExpansionCount },
                    { "pruned_nodes", graph.Coverage.PrunedNodes },
                    { "boundary_nodes", graph.Coverage.BoundaryNodes },
                    { "transposition_prunes", graph.Coverage.TranspositionPrunes },
                    { "elapsed_ms", graph.Coverage.ElapsedMs },
                };
                AddStringList(coverage, "no_candidate_reasons", graph.Coverage.NoCandidateReasons);
                AddStringList(coverage, "exclusion_reasons", graph.Coverage.ExclusionReasons);
                AddStringList(coverage, "root_exclusions", graph.Coverage.RootExclusions);
                AddStringList(coverage, "pruning_reasons", graph.Coverage.PruningReasons);
                AddStringList(coverage, "non_expansion_reasons", graph.Coverage.NonExpansionReasons);
                root["coverage"] = coverage;
            }

            List<object> lines = new List<object>();
            if (graph.Lines != null)
            {
                foreach (LlmSearchLine line in graph.Lines)
                {
                    if (line == null)
                    {
                        continue;
                    }
                    if (!includeContinuations && !line.IsRootShell)
                    {
                        continue;
                    }
                    lines.Add(ProjectLine(line, compact));
                }
            }
            root["lines"] = lines;
            return root;
        }

        static Dictionary<string, object> ProjectLine(LlmSearchLine line, bool compact)
        {
            Dictionary<string, object> lineData = new Dictionary<string, object>()
            {
                { "line_id", line.LineId },
                { "root_action_id", line.RootActionId },
                { "provenance", line.Provenance },
                { "commit_eligible", line.CommitEligible },
                { "boundary", line.Boundary },
                { "is_root_shell", line.IsRootShell },
                { "strategic_depth", line.StrategicDepth },
                { "score", line.Score },
            };
            // Single lowercase keys for new structured fields (16 KiB budget).
            if (!string.IsNullOrEmpty(line.TemplateId))
            {
                lineData["template_id"] = line.TemplateId;
            }
            if (line.SummonRequirement != null)
            {
                lineData["summon_requirement"] = line.SummonRequirement.ToProjectionDictionary();
            }
            if (line.ScoreFeatures != null && line.ScoreFeatures.Count > 0)
            {
                Dictionary<string, object> features = new Dictionary<string, object>();
                foreach (KeyValuePair<string, int> pair in line.ScoreFeatures)
                {
                    features[pair.Key] = pair.Value;
                }
                lineData["score_features"] = features;
            }
            if (line.ImmediateOutcome != null)
            {
                lineData["immediate_outcome"] = new Dictionary<string, object>()
                {
                    { "primary_effect_stopped", line.ImmediateOutcome.PrimaryEffectStopped },
                    { "target_effect_expected_to_resolve",
                        line.ImmediateOutcome.TargetEffectExpectedToResolve },
                    { "primary_disruption_value", line.ImmediateOutcome.PrimaryDisruptionValue },
                    { "value_zero_primary_penalty", line.ImmediateOutcome.ValueZeroPrimaryPenalty },
                    { "provenance", line.ImmediateOutcome.Provenance },
                    { "unknown_card_semantics", line.ImmediateOutcome.IsUnknownCardSemantics },
                };
            }
            if (!string.IsNullOrEmpty(line.NoCandidateReason))
            {
                lineData["no_candidate_reason"] = line.NoCandidateReason;
            }
            if (!string.IsNullOrEmpty(line.ExclusionReason))
            {
                lineData["exclusion_reason"] = line.ExclusionReason;
            }
            if (!string.IsNullOrEmpty(line.PruneReason))
            {
                lineData["prune_reason"] = line.PruneReason;
            }
            if (!string.IsNullOrEmpty(line.NonExpansionReason))
            {
                lineData["non_expansion_reason"] = line.NonExpansionReason;
            }
            if (!compact)
            {
                lineData["steps"] = ProjectSteps(line.Steps);
                lineData["unlocks"] = ToStringList(line.Unlocks);
                lineData["uncertainty"] = ToStringList(line.Uncertainty);
            }
            else
            {
                // Compact: single step label only
                List<object> steps = new List<object>();
                if (line.Steps != null && line.Steps.Count > 0 && line.Steps[0] != null)
                {
                    steps.Add(new Dictionary<string, object>()
                    {
                        { "label", line.Steps[0].Label },
                        { "current_legal", line.Steps[0].CurrentLegal },
                        { "commit_eligible", line.Steps[0].CommitEligible },
                        { "provenance", line.Steps[0].Provenance },
                    });
                }
                lineData["steps"] = steps;
            }
            return lineData;
        }

        static List<object> ProjectSteps(IList<LlmSearchStep> steps)
        {
            List<object> list = new List<object>();
            if (steps == null)
            {
                return list;
            }
            foreach (LlmSearchStep step in steps)
            {
                if (step == null)
                {
                    continue;
                }
                list.Add(new Dictionary<string, object>()
                {
                    { "label", step.Label },
                    { "current_legal", step.CurrentLegal },
                    { "commit_eligible", step.CommitEligible },
                    { "provenance", step.Provenance },
                });
            }
            return list;
        }

        static void AddStringList(Dictionary<string, object> target, string key, IList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return;
            }
            target[key] = ToStringList(values);
        }

        static List<object> ToStringList(IList<string> values)
        {
            List<object> list = new List<object>();
            if (values == null)
            {
                return list;
            }
            foreach (string value in values)
            {
                list.Add(value);
            }
            return list;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace YgoMaster
{
    /// <summary>
    /// Structured intended-followup intent captured from a pre-commit selected template line (Slice 6B).
    /// </summary>
    sealed class LlmIntendedFollowupMemory
    {
        public string TemplateId { get; set; }
        public string LineId { get; set; }
        public int RootActionId { get; set; }
        public ulong OriginRunEffectSeq { get; set; }
        public int OriginTurn { get; set; }
        public string ExpectedSummonFamily { get; set; }
        public int ExpectedTargetCardId { get; set; }
        public IList<int> ExpectedMaterialCardIds { get; set; }
        public IList<LlmMaterialSource> ExpectedMaterialSources { get; set; }
        /// <summary>Last evaluation status for SerializeTurnMemory (available/not_yet_available/...).</summary>
        public string EvaluationStatus { get; set; }
        public int EvaluationActionId { get; set; }
        public bool EvaluationAutoCommit { get; set; }

        public LlmIntendedFollowupMemory()
        {
            ExpectedMaterialCardIds = new List<int>();
            ExpectedMaterialSources = new List<LlmMaterialSource>();
            EvaluationAutoCommit = false;
        }

        public static LlmIntendedFollowupMemory FromSelectedLine(
            DecisionSnapshot snapshot,
            LlmSearchLine selectedLine)
        {
            if (snapshot == null || selectedLine == null)
            {
                return null;
            }
            if (selectedLine.IsRootShell)
            {
                return null;
            }
            if (string.IsNullOrEmpty(selectedLine.TemplateId))
            {
                return null;
            }
            if (selectedLine.SummonRequirement == null
                || !selectedLine.SummonRequirement.IsInternallyValid())
            {
                return null;
            }
            LlmSummonRequirement req = selectedLine.SummonRequirement;
            // Material source identities must be distinct (validated by IsInternallyValid).
            LlmIntendedFollowupMemory mem = new LlmIntendedFollowupMemory()
            {
                TemplateId = selectedLine.TemplateId,
                LineId = selectedLine.LineId ?? string.Empty,
                RootActionId = selectedLine.RootActionId,
                OriginRunEffectSeq = snapshot.RunEffectSeq,
                OriginTurn = snapshot.Turn,
                ExpectedSummonFamily = req.SummonFamily ?? string.Empty,
                ExpectedTargetCardId = req.ExtraDeckTargetCardId,
            };
            if (req.OrderedMaterialCardIds != null)
            {
                mem.ExpectedMaterialCardIds = new List<int>(req.OrderedMaterialCardIds);
            }
            if (req.MaterialSources != null)
            {
                List<LlmMaterialSource> sources = new List<LlmMaterialSource>();
                foreach (LlmMaterialSource s in req.MaterialSources)
                {
                    if (s == null)
                    {
                        continue;
                    }
                    sources.Add(new LlmMaterialSource()
                    {
                        Zone = s.Zone,
                        Index = s.Index,
                        CardId = s.CardId,
                        IsFaceUp = s.IsFaceUp,
                    });
                }
                mem.ExpectedMaterialSources = sources;
            }
            return mem;
        }

        public Dictionary<string, object> ToDictionary()
        {
            List<object> mats = new List<object>();
            if (ExpectedMaterialCardIds != null)
            {
                foreach (int id in ExpectedMaterialCardIds)
                {
                    mats.Add(id);
                }
            }
            List<object> sources = new List<object>();
            if (ExpectedMaterialSources != null)
            {
                foreach (LlmMaterialSource s in ExpectedMaterialSources)
                {
                    if (s == null)
                    {
                        continue;
                    }
                    sources.Add(new Dictionary<string, object>()
                    {
                        { "zone", s.Zone },
                        { "index", s.Index },
                        { "card_id", s.CardId },
                        { "is_face_up", s.IsFaceUp },
                    });
                }
            }
            // Single lowercase projection keys (16 KiB budget); runtime properties stay PascalCase.
            return new Dictionary<string, object>()
            {
                { "template_id", TemplateId ?? string.Empty },
                { "line_id", LineId ?? string.Empty },
                { "root_action_id", RootActionId },
                { "origin_run_effect_seq", OriginRunEffectSeq },
                { "origin_turn", OriginTurn },
                { "expected_summon_family", ExpectedSummonFamily ?? string.Empty },
                { "expected_target_card_id", ExpectedTargetCardId },
                { "expected_material_card_ids", mats },
                { "expected_material_sources", sources },
                { "status", EvaluationStatus ?? string.Empty },
                { "action_id", EvaluationActionId },
                { "auto_commit", EvaluationAutoCommit },
            };
        }
    }

    /// <summary>
    /// Evaluation result for intended followup against a fresh engine window.
    /// </summary>
    sealed class LlmIntendedFollowupEvaluation
    {
        public string Status { get; set; }
        public int ActionId { get; set; }
        public bool AutoCommit { get; set; }

        public LlmIntendedFollowupEvaluation()
        {
            AutoCommit = false;
            Status = string.Empty;
        }
    }

    /// <summary>
    /// Pure evaluator: inspects current legal actions by family/target; never auto-commits.
    /// Material grounding is multiset/count-aware (one copy cannot satisfy two expected slots).
    /// Stored source list must have been distinct; setup movement/flip between windows is allowed.
    /// </summary>
    static class LlmIntendedFollowupEvaluator
    {
        public static LlmIntendedFollowupEvaluation Evaluate(
            LlmIntendedFollowupMemory memory,
            DecisionSnapshot snapshot,
            LlmSearchGraph graph)
        {
            LlmIntendedFollowupEvaluation result = new LlmIntendedFollowupEvaluation()
            {
                AutoCommit = false,
                ActionId = -1,
                Status = "invalidated",
            };
            if (memory == null || snapshot == null)
            {
                result.Status = "invalidated";
                return Apply(memory, result);
            }
            if (snapshot.Turn != memory.OriginTurn)
            {
                result.Status = "expired";
                return Apply(memory, result);
            }

            // Stored source list must remain internally distinct (capture invariant).
            if (!StoredSourcesDistinct(memory))
            {
                result.Status = "invalidated";
                return Apply(memory, result);
            }

            LlmSelfResources self = graph != null ? graph.SourceSelfResources : null;
            if (self == null && snapshot.SelfResources != null)
            {
                self = snapshot.SelfResources;
            }

            if (!MaterialsGroundedMultiset(memory, self))
            {
                result.Status = "invalidated";
                return Apply(memory, result);
            }
            if (!ExtraDeckTargetPresent(memory, self))
            {
                result.Status = "invalidated";
                return Apply(memory, result);
            }

            int legalId = FindMatchingLegalActionId(snapshot, memory);
            if (legalId >= 0)
            {
                result.Status = "available";
                result.ActionId = legalId;
                result.AutoCommit = false;
                return Apply(memory, result);
            }

            result.Status = "not_yet_available";
            result.ActionId = -1;
            result.AutoCommit = false;
            return Apply(memory, result);
        }

        static LlmIntendedFollowupEvaluation Apply(
            LlmIntendedFollowupMemory memory, LlmIntendedFollowupEvaluation result)
        {
            if (memory != null)
            {
                memory.EvaluationStatus = result.Status;
                memory.EvaluationActionId = result.ActionId;
                memory.EvaluationAutoCommit = result.AutoCommit;
            }
            return result;
        }

        static bool StoredSourcesDistinct(LlmIntendedFollowupMemory memory)
        {
            if (memory.ExpectedMaterialSources == null || memory.ExpectedMaterialSources.Count == 0)
            {
                // Sources optional at evaluate if only card-id multiset is used; still require mats.
                return true;
            }
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (LlmMaterialSource s in memory.ExpectedMaterialSources)
            {
                if (s == null)
                {
                    return false;
                }
                if (!keys.Add(s.IdentityKey))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Multiset match on field card ids. Zones may move/flip between pre- and post-root windows.
        /// </summary>
        static bool MaterialsGroundedMultiset(LlmIntendedFollowupMemory memory, LlmSelfResources self)
        {
            if (memory.ExpectedMaterialCardIds == null || memory.ExpectedMaterialCardIds.Count == 0)
            {
                return false;
            }
            if (self == null || self.Field == null)
            {
                return false;
            }

            Dictionary<int, int> need = new Dictionary<int, int>();
            foreach (int id in memory.ExpectedMaterialCardIds)
            {
                if (id <= 0)
                {
                    return false;
                }
                int c;
                need.TryGetValue(id, out c);
                need[id] = c + 1;
            }

            Dictionary<int, int> have = new Dictionary<int, int>();
            foreach (LlmSelfResourceCard card in self.Field)
            {
                if (card == null || card.CardId <= 0)
                {
                    continue;
                }
                int c;
                have.TryGetValue(card.CardId, out c);
                have[card.CardId] = c + 1;
            }

            foreach (KeyValuePair<int, int> kv in need)
            {
                int available;
                have.TryGetValue(kv.Key, out available);
                if (available < kv.Value)
                {
                    return false;
                }
            }
            return true;
        }

        static bool ExtraDeckTargetPresent(LlmIntendedFollowupMemory memory, LlmSelfResources self)
        {
            if (memory.ExpectedTargetCardId <= 0)
            {
                return false;
            }
            if (self == null || self.ExtraDeck == null)
            {
                return false;
            }
            foreach (LlmSelfResourceExtraDeckEntry e in self.ExtraDeck)
            {
                if (e != null && e.CardId == memory.ExpectedTargetCardId)
                {
                    return true;
                }
            }
            return false;
        }

        static int FindMatchingLegalActionId(DecisionSnapshot snapshot, LlmIntendedFollowupMemory memory)
        {
            if (snapshot.LegalActions == null)
            {
                return -1;
            }
            string family = (memory.ExpectedSummonFamily ?? string.Empty).ToLowerInvariant();
            foreach (LegalAction a in snapshot.LegalActions)
            {
                if (a == null || a.Kind != LegalActionKind.Command)
                {
                    continue;
                }
                int cid = a.CardId > 0 ? a.CardId : (a.Card != null ? a.Card.CardId : 0);
                if (cid != memory.ExpectedTargetCardId)
                {
                    continue;
                }
                if (a.Command == DuelCommandType.SummonSp
                    || a.Command == DuelCommandType.Summon)
                {
                    if (family.IndexOf("xyz", StringComparison.Ordinal) >= 0
                        || family.IndexOf("synchro", StringComparison.Ordinal) >= 0
                        || family.Length == 0)
                    {
                        return a.ActionId;
                    }
                }
            }
            return -1;
        }
    }
}

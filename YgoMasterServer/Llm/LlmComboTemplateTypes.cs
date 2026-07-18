using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
// HashSet used by LlmSummonRequirement.IsInternallyValid

namespace YgoMaster
{
    /// <summary>
    /// Distinct material source identity (zone + index) for combo templates (Slice 6B).
    /// </summary>
    sealed class LlmMaterialSource
    {
        public int Zone { get; set; }
        public int Index { get; set; }
        public int CardId { get; set; }
        public bool IsFaceUp { get; set; }

        public string IdentityKey
        {
            get { return Zone + ":" + Index; }
        }
    }

    /// <summary>
    /// Structured summon requirement attached to template-emitted search lines.
    /// </summary>
    sealed class LlmSummonRequirement
    {
        public string SummonFamily { get; set; }
        public int Rank { get; set; }
        public int Level { get; set; }
        public int MaterialCount { get; set; }
        public int ExtraDeckTargetCardId { get; set; }
        public IList<int> OrderedMaterialCardIds { get; set; }
        public IList<LlmMaterialSource> MaterialSources { get; set; }

        public LlmSummonRequirement()
        {
            OrderedMaterialCardIds = new List<int>();
            MaterialSources = new List<LlmMaterialSource>();
        }

        /// <summary>
        /// Fail-closed structural validity for capture into intended-followup memory.
        /// Requires MaterialCount &gt; 0, positive ordered ids, matching source CardIds, distinct identities.
        /// </summary>
        public bool IsInternallyValid()
        {
            if (string.IsNullOrEmpty(SummonFamily))
            {
                return false;
            }
            if (ExtraDeckTargetCardId <= 0)
            {
                return false;
            }
            if (MaterialCount <= 0)
            {
                return false;
            }
            if (OrderedMaterialCardIds == null || OrderedMaterialCardIds.Count == 0)
            {
                return false;
            }
            if (MaterialCount != OrderedMaterialCardIds.Count)
            {
                return false;
            }
            if (MaterialSources == null || MaterialSources.Count == 0)
            {
                return false;
            }
            if (MaterialSources.Count != MaterialCount)
            {
                return false;
            }
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < OrderedMaterialCardIds.Count; i++)
            {
                int orderedId = OrderedMaterialCardIds[i];
                if (orderedId <= 0)
                {
                    return false;
                }
                LlmMaterialSource s = MaterialSources[i];
                if (s == null)
                {
                    return false;
                }
                if (s.CardId <= 0 || s.CardId != orderedId)
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

        public void Freeze()
        {
            OrderedMaterialCardIds = new ReadOnlyCollection<int>(
                new List<int>(OrderedMaterialCardIds ?? new List<int>()));
            MaterialSources = new ReadOnlyCollection<LlmMaterialSource>(
                new List<LlmMaterialSource>(MaterialSources ?? new List<LlmMaterialSource>()));
        }

        public Dictionary<string, object> ToProjectionDictionary()
        {
            List<object> mats = new List<object>();
            if (OrderedMaterialCardIds != null)
            {
                foreach (int id in OrderedMaterialCardIds)
                {
                    mats.Add(id);
                }
            }
            List<object> sources = new List<object>();
            if (MaterialSources != null)
            {
                foreach (LlmMaterialSource s in MaterialSources)
                {
                    if (s == null)
                    {
                        continue;
                    }
                    // Nested keys stay PascalCase for structured readability; outer projection
                    // uses a single lowercase summon_requirement key.
                    sources.Add(new Dictionary<string, object>()
                    {
                        { "Zone", s.Zone },
                        { "Index", s.Index },
                        { "CardId", s.CardId },
                        { "IsFaceUp", s.IsFaceUp },
                    });
                }
            }
            return new Dictionary<string, object>()
            {
                { "SummonFamily", SummonFamily ?? string.Empty },
                { "Rank", Rank },
                { "Level", Level },
                { "MaterialCount", MaterialCount },
                { "ExtraDeckTargetCardId", ExtraDeckTargetCardId },
                { "OrderedMaterialCardIds", mats },
                { "MaterialSources", sources },
            };
        }
    }

    /// <summary>
    /// Offline-validated combo template (semantic Layer A, not engine-confirmed).
    /// </summary>
    sealed class LlmComboTemplate
    {
        public string TemplateId { get; set; }
        public string SummonFamily { get; set; }
        public int ExtraDeckTargetCardId { get; set; }
        public int RequiredRank { get; set; }
        public int RequiredLevel { get; set; }
        public IList<int> MaterialCardIds { get; set; }
        /// <summary>Setup root command that unlocks the line (e.g. Reverse for flip).</summary>
        public int SetupRootCommand { get; set; }
        public int SetupRootCardId { get; set; }

        public LlmComboTemplate()
        {
            MaterialCardIds = new List<int>();
        }
    }
}

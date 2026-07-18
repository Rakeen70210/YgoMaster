using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace YgoMaster
{
    /// <summary>
    /// Offline-validated combo template catalog (YGOMASTER-LLM-005 Slice 6B).
    /// </summary>
    sealed class LlmComboTemplateCatalog
    {
        public IList<LlmComboTemplate> Templates { get; private set; }

        public LlmComboTemplateCatalog()
        {
            Templates = new List<LlmComboTemplate>();
        }

        public static LlmComboTemplateCatalog CreateDefault()
        {
            LlmComboTemplateCatalog catalog = new LlmComboTemplateCatalog();
            catalog.Templates = new ReadOnlyCollection<LlmComboTemplate>(new List<LlmComboTemplate>()
            {
                // Girochin 5136 face-up + flip Cave 4103 -> Rank 4 Utopia 9575
                new LlmComboTemplate()
                {
                    TemplateId = "cubic_girochin_5136_cave_4103_rank4_utopia_9575",
                    SummonFamily = "xyz",
                    ExtraDeckTargetCardId = 9575,
                    RequiredRank = 4,
                    SetupRootCommand = (int)DuelCommandType.Reverse,
                    SetupRootCardId = 4103,
                    MaterialCardIds = new ReadOnlyCollection<int>(new List<int>() { 4103, 5136 }),
                },
                // Junk Synchron 7687 + summon L5 non-tuner -> Stardust 7734 (level 8)
                new LlmComboTemplate()
                {
                    TemplateId = "junk_synchron_stardust_level_sum",
                    SummonFamily = "synchro",
                    ExtraDeckTargetCardId = 7734,
                    RequiredLevel = 8,
                    SetupRootCommand = (int)DuelCommandType.Summon,
                    SetupRootCardId = 0, // any matching non-tuner level contribution
                    MaterialCardIds = new ReadOnlyCollection<int>(new List<int>() { 7687 }),
                },
            });
            return catalog;
        }
    }
}

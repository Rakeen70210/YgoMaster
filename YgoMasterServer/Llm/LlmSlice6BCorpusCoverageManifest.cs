using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace YgoMaster
{
    /// <summary>
    /// Corpus-driven coverage manifest for Slice 6B (no exact engine simulation claim).
    /// </summary>
    sealed class LlmSlice6BCorpusCoverageManifest
    {
        public IList<string> Families { get; private set; }
        public bool ClaimsExactSimulation { get; private set; }

        public LlmSlice6BCorpusCoverageManifest()
        {
            Families = new List<string>();
            ClaimsExactSimulation = false;
        }

        public static LlmSlice6BCorpusCoverageManifest CreateDefault()
        {
            return new LlmSlice6BCorpusCoverageManifest()
            {
                ClaimsExactSimulation = false,
                Families = new ReadOnlyCollection<string>(new List<string>()
                {
                    "xyz_girochin_cave_rank4",
                    "synchro_exact_tuner_nontuner_level_sum",
                    "unknown_opponent_response_boundary",
                    "lethal_vs_setup",
                    "no_viable_continuation",
                }),
            };
        }
    }
}

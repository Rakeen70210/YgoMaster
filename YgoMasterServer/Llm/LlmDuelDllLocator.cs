using System;
using System.IO;

namespace YgoMaster
{
    /// <summary>
    /// Resolves the authoritative Master Duel duel.dll for isolated worker probes only.
    /// Prefer repo-adjacent masterduel_Data plugin (real game install layout).
    /// </summary>
    public static class LlmDuelDllLocator
    {
        public const long ExpectedMasterDuelDuelDllBytes = 19118592;

        public static string ResolveAuthoritativeDuelDllPath()
        {
            string root = LlmSlice5Paths.FindRepoRoot();
            string[] candidates = new string[]
            {
                // Primary: sibling of YgoMaster checkout under Master Duel install.
                Path.GetFullPath(Path.Combine(root, "..", "masterduel_Data", "Plugins", "x86_64", "duel.dll")),
                Path.GetFullPath(Path.Combine(root, "..", "MasterDuel_Data", "Plugins", "x86_64", "duel.dll")),
                // Alternate install layouts
                Path.Combine(root, "masterduel_Data", "Plugins", "x86_64", "duel.dll"),
                Path.Combine(root, "YgoMaster", "Data", "duel.dll"),
                Path.Combine(root, "Data", "duel.dll"),
            };

            foreach (string c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && File.Exists(c))
                {
                    return Path.GetFullPath(c);
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Testable native-then-record ordering helper (Slice 5 review contract).
    /// </summary>
    public static class LlmAcceptedInputRecordOrder
    {
        public static void InvokeVoidOriginalThenRecord(Action original, Action record)
        {
            if (original == null)
            {
                throw new ArgumentNullException("original");
            }
            original();
            if (record != null)
            {
                record();
            }
        }

        public static T InvokeReturningThenRecord<T>(Func<T> original, Action<T> recordAfterReturn)
        {
            if (original == null)
            {
                throw new ArgumentNullException("original");
            }
            T result = original();
            if (recordAfterReturn != null)
            {
                recordAfterReturn(result);
            }
            return result;
        }
    }
}

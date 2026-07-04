using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    static class LlmCardDataDirectoryResolver
    {
        static readonly string[] RequiredCardDataFiles = new string[]
        {
            Path.Combine("CardData", "#", "CARD_Prop.bytes"),
            Path.Combine("CardData", "en-US", "CARD_Indx.bytes"),
            Path.Combine("CardData", "en-US", "CARD_Name.bytes"),
            Path.Combine("CardData", "en-US", "CARD_Desc.bytes"),
        };

        public static string ResolveClientDataDirectory(IEnumerable<string> baseDirectories)
        {
            if (baseDirectories == null)
            {
                return null;
            }

            HashSet<string> seenDataDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string baseDirectory in baseDirectories)
            {
                foreach (string dataDirectory in EnumerateDataDirectoryCandidates(baseDirectory))
                {
                    string fullDataDirectory = GetFullPathOrNull(dataDirectory);
                    if (string.IsNullOrEmpty(fullDataDirectory) || !seenDataDirs.Add(fullDataDirectory))
                    {
                        continue;
                    }

                    if (HasRequiredCardDataFiles(fullDataDirectory))
                    {
                        return fullDataDirectory;
                    }
                }
            }
            return null;
        }

        public static bool HasRequiredCardDataFiles(string dataDirectory)
        {
            if (string.IsNullOrEmpty(dataDirectory))
            {
                return false;
            }

            foreach (string relativeFile in RequiredCardDataFiles)
            {
                if (!File.Exists(Path.Combine(dataDirectory, relativeFile)))
                {
                    return false;
                }
            }
            return true;
        }

        static IEnumerable<string> EnumerateDataDirectoryCandidates(string baseDirectory)
        {
            string fullBaseDirectory = GetFullPathOrNull(baseDirectory);
            if (string.IsNullOrEmpty(fullBaseDirectory))
            {
                yield break;
            }

            string overrideDataDirectory;
            if (TryReadOverrideDataDirectory(fullBaseDirectory, "DataDirClient.txt", out overrideDataDirectory))
            {
                yield return overrideDataDirectory;
            }
            if (TryReadOverrideDataDirectory(fullBaseDirectory, "DataDir.txt", out overrideDataDirectory))
            {
                yield return overrideDataDirectory;
            }
            yield return Path.Combine(fullBaseDirectory, "Data");
        }

        static bool TryReadOverrideDataDirectory(string baseDirectory, string fileName, out string dataDirectory)
        {
            dataDirectory = null;
            try
            {
                string overrideFile = Path.Combine(baseDirectory, fileName);
                if (!File.Exists(overrideFile))
                {
                    return false;
                }

                string[] lines = File.ReadAllLines(overrideFile);
                if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
                {
                    return false;
                }

                string value = lines[0].Trim();
                dataDirectory = Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value);
                return true;
            }
            catch
            {
                dataDirectory = null;
                return false;
            }
        }

        static string GetFullPathOrNull(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }
    }
}

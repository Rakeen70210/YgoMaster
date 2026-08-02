using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    /// <summary>
    /// Production JSONL append/rotation helper. The caller-owned line count may be reset
    /// to -1 on process start; the helper then reconstructs it from disk before applying
    /// the strict cap.
    /// </summary>
    static class CampaignCpuAuditFile
    {
        public static bool TryAppendLine(
            string path,
            string line,
            int maxLines,
            ref int writtenLines)
        {
            if (string.IsNullOrEmpty(path)
                || string.IsNullOrEmpty(line)
                || maxLines <= 0)
            {
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (writtenLines < 0)
                {
                    List<string> completeLines = ReadCompleteJsonLines(path);
                    if (File.Exists(path)
                        && File.ReadAllLines(path).Length != completeLines.Count)
                    {
                        File.WriteAllLines(path, completeLines.ToArray());
                    }
                    writtenLines = completeLines.Count;
                }

                if (writtenLines >= maxLines)
                {
                    List<string> validLines = ReadCompleteJsonLines(path);
                    int half = (validLines.Count + 1) / 2;
                    int keepCount = Math.Min(maxLines - 1, half);
                    if (keepCount < 0)
                    {
                        keepCount = 0;
                    }
                    int keepFrom = validLines.Count - keepCount;
                    string[] tail = keepCount == 0
                        ? new string[0]
                        : validLines.GetRange(keepFrom, keepCount).ToArray();
                    File.WriteAllLines(path, tail);
                    writtenLines = tail.Length;
                }

                File.AppendAllText(path, line + Environment.NewLine);
                writtenLines++;
                return true;
            }
            catch
            {
                return false;
            }
        }

        static List<string> ReadCompleteJsonLines(string path)
        {
            var rows = new List<string>();
            if (!File.Exists(path))
            {
                return rows;
            }
            foreach (string row in File.ReadAllLines(path))
            {
                string trimmed = row != null ? row.Trim() : string.Empty;
                if (trimmed.Length >= 2
                    && trimmed[0] == '{'
                    && trimmed[trimmed.Length - 1] == '}'
                    && MiniJSON.Json.Deserialize(trimmed)
                        is Dictionary<string, object>)
                {
                    rows.Add(trimmed);
                }
            }
            return rows;
        }
    }
}

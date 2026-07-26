using System;
using System.Collections.Generic;
using System.IO;
using YgoMaster;

namespace YgoMasterClient
{
    /// <summary>
    /// Append-only JSONL audit under ClientData. Never throws into the duel path.
    /// Cross-launch line accounting: WrittenLines is initialized from the existing file
    /// so CampaignCpuAuditMaxLines caps the on-disk file, not merely the current process.
    /// Rotation keeps the newest half (tail) so recent evidence is never discarded first.
    /// </summary>
    static class CampaignCpuAuditLog
    {
        static readonly object LockObj = new object();
        /// <summary>-1 = not yet counted from disk for this process.</summary>
        static int WrittenLines = -1;

        public static string ResolvePath()
        {
            try
            {
                if (!string.IsNullOrEmpty(ClientSettings.CampaignCpuAuditLogPath))
                {
                    return ClientSettings.CampaignCpuAuditLogPath;
                }
                return Path.Combine(Program.ClientDataDir, "CampaignCpuAuditLog.jsonl");
            }
            catch
            {
                return null;
            }
        }

        public static void Write(string eventName, Dictionary<string, object> fields)
        {
            if (!ClientSettings.CampaignCpuAuditLogEnabled)
            {
                return;
            }
            try
            {
                string line;
                if (!CampaignCpuAuditSerializer.TrySerialize(eventName, fields, out line)
                    || string.IsNullOrEmpty(line))
                {
                    return;
                }
                AppendLine(line);
            }
            catch
            {
            }
        }

        public static void WriteRaw(string line)
        {
            if (!ClientSettings.CampaignCpuAuditLogEnabled || string.IsNullOrEmpty(line))
            {
                return;
            }
            try
            {
                AppendLine(line);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Test/harness hook: force line accounting state. WrittenLines=-1 re-reads disk.
        /// </summary>
        internal static void ResetLineAccountingForTests()
        {
            lock (LockObj)
            {
                WrittenLines = -1;
            }
        }

        static void AppendLine(string line)
        {
            lock (LockObj)
            {
                string path = ResolvePath();
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                int max = ClientSettings.CampaignCpuAuditMaxLines > 0
                    ? ClientSettings.CampaignCpuAuditMaxLines
                    : CampaignCpuDefaults.DefaultAuditMaxLines;

                EnsureLineCountFromDisk(path);

                // Cap is on-disk size: when at/over max, keep newest half then append.
                // Never drop only the newest lines; tail retention preserves recent evidence.
                if (WrittenLines >= max)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            string[] all = File.ReadAllLines(path);
                            // Keep newest half (rounded up when odd so we do not drop more
                            // recent lines than older ones).
                            int keepCount = (all.Length + 1) / 2;
                            int keepFrom = all.Length - keepCount;
                            if (keepFrom < 0)
                            {
                                keepFrom = 0;
                            }
                            string[] tail = Slice(all, keepFrom);
                            // WriteAllLines is atomic enough for JSONL: full rewrite of valid lines.
                            File.WriteAllLines(path, tail);
                            WrittenLines = tail.Length;
                        }
                        else
                        {
                            WrittenLines = 0;
                        }
                    }
                    catch
                    {
                        // stop writing rather than throw / corrupt
                        return;
                    }
                }

                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.AppendAllText(path, line + Environment.NewLine);
                    WrittenLines++;
                }
                catch
                {
                }
            }
        }

        static void EnsureLineCountFromDisk(string path)
        {
            if (WrittenLines >= 0)
            {
                return;
            }
            try
            {
                if (!File.Exists(path))
                {
                    WrittenLines = 0;
                    return;
                }
                // Count non-empty lines only (matches analyzer skip of blanks).
                int count = 0;
                using (var reader = new StreamReader(path))
                {
                    string s;
                    while ((s = reader.ReadLine()) != null)
                    {
                        if (s.Length > 0)
                        {
                            count++;
                        }
                    }
                }
                WrittenLines = count;
            }
            catch
            {
                WrittenLines = 0;
            }
        }

        static string[] Slice(string[] all, int keepFrom)
        {
            if (keepFrom <= 0)
            {
                return all;
            }
            if (keepFrom >= all.Length)
            {
                return new string[0];
            }
            string[] tail = new string[all.Length - keepFrom];
            Array.Copy(all, keepFrom, tail, 0, tail.Length);
            return tail;
        }
    }
}

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
    /// Rotation keeps the newest valid tail while reserving one row for the append, so
    /// every positive configured maximum is a strict on-disk cap.
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

                CampaignCpuAuditFile.TryAppendLine(
                    path,
                    line,
                    max,
                    ref WrittenLines);
            }
        }
    }
}

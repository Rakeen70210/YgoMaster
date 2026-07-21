using System;
using System.Collections.Generic;
using System.IO;
using YgoMaster;

namespace YgoMasterClient
{
    /// <summary>
    /// Append-only JSONL audit under ClientData. Never throws into the duel path.
    /// </summary>
    static class CampaignCpuAuditLog
    {
        static readonly object LockObj = new object();
        static int WrittenLines;

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
                if (WrittenLines > max)
                {
                    try
                    {
                        // Drop oldest half by rewriting tail.
                        if (File.Exists(path))
                        {
                            string[] all = File.ReadAllLines(path);
                            int keepFrom = all.Length / 2;
                            File.WriteAllLines(path, Slice(all, keepFrom));
                            WrittenLines = all.Length - keepFrom;
                        }
                        else
                        {
                            WrittenLines = 0;
                        }
                    }
                    catch
                    {
                        // stop writing rather than throw
                        return;
                    }
                }
                File.AppendAllText(path, line + Environment.NewLine);
                WrittenLines++;
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

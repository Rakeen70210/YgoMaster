using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Canonical opponent Deck[1] fingerprint for CampaignCpu pack activation.
    /// Frozen v1: sort Main/Extra/Side ascending (retain multiplicity), compact JSON,
    /// SHA-256 lowercase hex with "sha256:" prefix.
    /// </summary>
    static class CampaignCpuDeckFingerprint
    {
        public static string Compute(IList<int> main, IList<int> extra, IList<int> side)
        {
            int[] mainSorted = SortCopy(main);
            int[] extraSorted = SortCopy(extra);
            int[] sideSorted = SortCopy(side);
            string payload = BuildPayload(mainSorted, extraSorted, sideSorted);
            return "sha256:" + Sha256Hex(payload);
        }

        public static string BuildPayload(IList<int> mainSorted, IList<int> extraSorted, IList<int> sideSorted)
        {
            var sb = new StringBuilder();
            sb.Append("{\"main\":");
            AppendIntArray(sb, mainSorted);
            sb.Append(",\"extra\":");
            AppendIntArray(sb, extraSorted);
            sb.Append(",\"side\":");
            AppendIntArray(sb, sideSorted);
            sb.Append('}');
            return sb.ToString();
        }

        public static bool Matches(string expectedHash, IList<int> main, IList<int> extra, IList<int> side)
        {
            if (string.IsNullOrEmpty(expectedHash))
            {
                return false;
            }
            string actual = Compute(main, extra, side);
            return string.Equals(expectedHash, actual, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWellFormedHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return false;
            }
            if (!hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string hex = hash.Substring("sha256:".Length);
            if (hex.Length != 64)
            {
                return false;
            }
            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                bool ok = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        static int[] SortCopy(IList<int> source)
        {
            if (source == null || source.Count == 0)
            {
                return new int[0];
            }
            int[] arr = new int[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                arr[i] = source[i];
            }
            Array.Sort(arr);
            return arr;
        }

        static void AppendIntArray(StringBuilder sb, IList<int> values)
        {
            sb.Append('[');
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(values[i]);
                }
            }
            sb.Append(']');
        }

        static string Sha256Hex(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Accepted native/server input kinds for the Layer B authoritative transcript
    /// (YGOMASTER-LLM-005 Slice 5). Rejected/stale attempts never use these as committed entries.
    /// </summary>
    public enum LlmAcceptedInputKind
    {
        DoCommand = 0,
        MovePhase = 1,
        Dialog = 2,
        List = 3,
        Cancel = 4,
        Automatic = 5,
    }

    /// <summary>
    /// One accepted input in the authoritative transcript. Only post-acceptance records.
    /// </summary>
    public sealed class LlmAcceptedInputEntry
    {
        public long AcceptedSequence { get; set; }
        public LlmAcceptedInputKind Kind { get; set; }
        public int Actor { get; set; }
        public ulong RunEffectSeq { get; set; }
        public object Payload { get; set; }
        public string AcceptanceProvenance { get; set; }
        public bool Accepted { get; set; }

        public LlmAcceptedInputEntry()
        {
            Accepted = true;
            AcceptanceProvenance = "engine_accepted";
            Payload = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Authoritative accepted-input transcript for offline replay research (default-off capture).
    /// </summary>
    public sealed class LlmAcceptedInputTranscript
    {
        public const string Schema = "ygomaster.llm_accepted_input_transcript.v1";

        public string SchemaVersion { get { return Schema; } }
        public string FixtureId { get; set; }
        public Dictionary<string, object> DuelSettings { get; set; }
        public List<LlmAcceptedInputEntry> Entries { get; set; }
        public List<Dictionary<string, object>> ExcludedAttempts { get; set; }

        public LlmAcceptedInputTranscript()
        {
            DuelSettings = new Dictionary<string, object>();
            Entries = new List<LlmAcceptedInputEntry>();
            ExcludedAttempts = new List<Dictionary<string, object>>();
        }

        public LlmAcceptedInputTranscript Clone()
        {
            string json = LlmAcceptedInputTranscriptSerializer.Serialize(this);
            return LlmAcceptedInputTranscriptSerializer.Deserialize(json);
        }

        /// <summary>
        /// Load a frozen scripted fixture from Tools/fixtures/llm_replay (offline tests).
        /// </summary>
        public static LlmAcceptedInputTranscript FromScriptedFixture(string fixtureId)
        {
            if (string.IsNullOrEmpty(fixtureId))
            {
                throw new ArgumentNullException("fixtureId");
            }
            string path = LlmSlice5Paths.ResolveFixturePath(fixtureId);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("scripted transcript fixture not found: " + fixtureId, path);
            }
            string text = File.ReadAllText(path, Encoding.UTF8);
            Dictionary<string, object> root = MiniJSON.Json.Deserialize(text) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidOperationException("invalid fixture JSON: " + fixtureId);
            }
            return FromDictionary(root);
        }

        public static LlmAcceptedInputTranscript FromDictionary(Dictionary<string, object> root)
        {
            LlmAcceptedInputTranscript t = new LlmAcceptedInputTranscript();
            t.FixtureId = GetString(root, "fixture_id");
            object settings = null;
            if (root.TryGetValue("duel_settings", out settings) && settings is Dictionary<string, object>)
            {
                t.DuelSettings = CloneDict(settings as Dictionary<string, object>);
                AliasPublicSettings(t.DuelSettings);
            }
            object entriesObj;
            if (root.TryGetValue("entries", out entriesObj) && entriesObj is IList)
            {
                foreach (object item in (IList)entriesObj)
                {
                    Dictionary<string, object> e = item as Dictionary<string, object>;
                    if (e == null)
                    {
                        continue;
                    }
                    LlmAcceptedInputEntry entry = new LlmAcceptedInputEntry();
                    entry.AcceptedSequence = GetLong(e, "accepted_sequence");
                    entry.Kind = ParseKind(GetString(e, "kind"));
                    entry.Actor = (int)GetLong(e, "actor");
                    entry.RunEffectSeq = (ulong)GetLong(e, "run_effect_seq");
                    entry.AcceptanceProvenance = GetString(e, "acceptance_provenance");
                    if (string.IsNullOrEmpty(entry.AcceptanceProvenance))
                    {
                        entry.AcceptanceProvenance = "engine_accepted";
                    }
                    object accepted;
                    if (e.TryGetValue("accepted", out accepted) && accepted is bool)
                    {
                        entry.Accepted = (bool)accepted;
                    }
                    object payload;
                    if (e.TryGetValue("payload", out payload))
                    {
                        entry.Payload = payload;
                    }
                    // Also expose Sequence/Seq/Order aliases via wrapper not needed —
                    // properties AcceptedSequence cover harness probes.
                    t.Entries.Add(entry);
                }
            }
            object excluded;
            if (root.TryGetValue("excluded_attempts", out excluded) && excluded is IList)
            {
                foreach (object item in (IList)excluded)
                {
                    Dictionary<string, object> e = item as Dictionary<string, object>;
                    if (e != null)
                    {
                        t.ExcludedAttempts.Add(CloneDict(e));
                    }
                }
            }
            return t;
        }

        /// <summary>
        /// Test-only mutation helper used by harness validation RED/GREEN cases.
        /// </summary>
        public static LlmAcceptedInputTranscript MutateForTest(LlmAcceptedInputTranscript source, string mode)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            LlmAcceptedInputTranscript copy = source.Clone();
            if (copy.Entries == null || copy.Entries.Count == 0)
            {
                return copy;
            }
            string m = mode ?? string.Empty;
            if (string.Equals(m, "ClearFirstKind", StringComparison.OrdinalIgnoreCase))
            {
                // Represent missing kind as invalid sentinel for validator.
                copy.Entries[0].AcceptanceProvenance = copy.Entries[0].AcceptanceProvenance;
                copy.Entries[0].Kind = (LlmAcceptedInputKind)(-1);
            }
            else if (string.Equals(m, "DuplicateFirstSequence", StringComparison.OrdinalIgnoreCase)
                && copy.Entries.Count >= 2)
            {
                copy.Entries[1].AcceptedSequence = copy.Entries[0].AcceptedSequence;
            }
            else if (string.Equals(m, "ReverseSequences", StringComparison.OrdinalIgnoreCase)
                && copy.Entries.Count >= 2)
            {
                long s0 = copy.Entries[0].AcceptedSequence;
                long s1 = copy.Entries[1].AcceptedSequence;
                copy.Entries[0].AcceptedSequence = s1;
                copy.Entries[1].AcceptedSequence = s0;
            }
            else if (string.Equals(m, "NullFirstPayload", StringComparison.OrdinalIgnoreCase))
            {
                copy.Entries[0].Payload = null;
            }
            else if (string.Equals(m, "InjectHiddenSentinel", StringComparison.OrdinalIgnoreCase))
            {
                copy.Entries[0].Payload = "SENTINEL_OPP_HAND_LEAK_LLM005_S5";
            }
            return copy;
        }

        internal static LlmAcceptedInputKind ParseKind(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                return (LlmAcceptedInputKind)(-1);
            }
            LlmAcceptedInputKind parsed;
            if (Enum.TryParse(kind, true, out parsed))
            {
                return parsed;
            }
            string n = kind.Replace("_", string.Empty).Replace(" ", string.Empty);
            if (Enum.TryParse(n, true, out parsed))
            {
                return parsed;
            }
            return (LlmAcceptedInputKind)(-1);
        }

        static string GetString(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
            {
                return string.Empty;
            }
            return Convert.ToString(v);
        }

        static long GetLong(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
            {
                return 0;
            }
            try
            {
                return Convert.ToInt64(v);
            }
            catch
            {
                return 0;
            }
        }

        static Dictionary<string, object> CloneDict(Dictionary<string, object> src)
        {
            if (src == null)
            {
                return new Dictionary<string, object>();
            }
            string json = MiniJSON.Json.Serialize(src);
            return MiniJSON.Json.Deserialize(json) as Dictionary<string, object>
                ?? new Dictionary<string, object>();
        }

        static void AliasKey(Dictionary<string, object> d, string sourceKey, params string[] aliases)
        {
            if (d == null || string.IsNullOrEmpty(sourceKey) || !d.ContainsKey(sourceKey))
            {
                return;
            }
            object v = d[sourceKey];
            foreach (string a in aliases)
            {
                if (!d.ContainsKey(a))
                {
                    d[a] = v;
                }
            }
        }

        public static void AliasPublicSettings(Dictionary<string, object> settings)
        {
            if (settings == null)
            {
                return;
            }
            AliasKey(settings, "seed", "Seed", "RandomSeed", "DuelSeed");
            AliasKey(settings, "first_player", "FirstPlayer", "StartingPlayer");
            AliasKey(settings, "limited_type", "LimitedType", "Regulation", "LimitRegulation");
            AliasKey(settings, "decks", "Decks", "MainDecks");
        }
    }

    public static class LlmAcceptedInputTranscriptSerializer
    {
        public static string Serialize(LlmAcceptedInputTranscript transcript)
        {
            return ToCanonical(transcript);
        }

        public static string ToCanonical(LlmAcceptedInputTranscript transcript)
        {
            if (transcript == null)
            {
                return "null";
            }
            Dictionary<string, object> root = ToDictionary(transcript);
            return MiniJSON.Json.Serialize(root);
        }

        public static LlmAcceptedInputTranscript Deserialize(string json)
        {
            return Parse(json);
        }

        public static LlmAcceptedInputTranscript Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new ArgumentNullException("json");
            }
            Dictionary<string, object> root = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidOperationException("invalid transcript JSON");
            }
            return LlmAcceptedInputTranscript.FromDictionary(root);
        }

        public static Dictionary<string, object> ToDictionary(LlmAcceptedInputTranscript t)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["schema"] = LlmAcceptedInputTranscript.Schema;
            if (!string.IsNullOrEmpty(t.FixtureId))
            {
                root["fixture_id"] = t.FixtureId;
            }
            root["duel_settings"] = t.DuelSettings ?? new Dictionary<string, object>();
            List<object> entries = new List<object>();
            if (t.Entries != null)
            {
                foreach (LlmAcceptedInputEntry e in t.Entries.OrderBy(x => x.AcceptedSequence))
                {
                    if (e == null)
                    {
                        continue;
                    }
                    Dictionary<string, object> row = new Dictionary<string, object>();
                    row["accepted_sequence"] = e.AcceptedSequence;
                    row["kind"] = Enum.IsDefined(typeof(LlmAcceptedInputKind), e.Kind)
                        ? e.Kind.ToString()
                        : string.Empty;
                    row["actor"] = e.Actor;
                    row["run_effect_seq"] = e.RunEffectSeq;
                    row["payload"] = e.Payload;
                    row["acceptance_provenance"] = e.AcceptanceProvenance ?? "engine_accepted";
                    row["accepted"] = e.Accepted;
                    entries.Add(row);
                }
            }
            root["entries"] = entries;
            root["excluded_attempts"] = t.ExcludedAttempts ?? new List<Dictionary<string, object>>();
            return root;
        }
    }

    public sealed class LlmAcceptedInputTranscriptValidationResult
    {
        public bool Ok { get; set; }
        public bool IsValid { get { return Ok; } }
        public bool Valid { get { return Ok; } }
        public bool Success { get { return Ok; } }
        public List<string> Errors { get; set; }

        public LlmAcceptedInputTranscriptValidationResult()
        {
            Errors = new List<string>();
            Ok = true;
        }

        public override string ToString()
        {
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "ok", Ok },
                { "errors", Errors ?? new List<string>() },
            }) ?? ("ok=" + Ok + " errors=" + string.Join(";", Errors ?? new List<string>()));
        }
    }

    public static class LlmAcceptedInputTranscriptValidator
    {
        static readonly string[] HiddenSentinelTokens = new string[]
        {
            "SENTINEL_OPP_HAND_LEAK_LLM005_S5",
            "SENTINEL_OPP_SET_LEAK_LLM005_S5",
            "SENTINEL_OPP_DECK_LEAK_LLM005_S5",
            "SENTINEL_OPP_EXTRA_LEAK_LLM005_S5",
            "99106001",
            "99106002",
            "99106003",
            "99106004",
        };

        public static LlmAcceptedInputTranscriptValidationResult Validate(LlmAcceptedInputTranscript transcript)
        {
            LlmAcceptedInputTranscriptValidationResult result = new LlmAcceptedInputTranscriptValidationResult();
            if (transcript == null)
            {
                result.Ok = false;
                result.Errors.Add("transcript is null");
                return result;
            }
            if (transcript.Entries == null)
            {
                result.Ok = false;
                result.Errors.Add("entries missing");
                return result;
            }

            long prev = -1;
            HashSet<long> seen = new HashSet<long>();
            for (int i = 0; i < transcript.Entries.Count; i++)
            {
                LlmAcceptedInputEntry e = transcript.Entries[i];
                if (e == null)
                {
                    result.Errors.Add("null entry at " + i);
                    continue;
                }
                if (!Enum.IsDefined(typeof(LlmAcceptedInputKind), e.Kind))
                {
                    result.Errors.Add("missing or invalid kind at sequence " + e.AcceptedSequence);
                }
                if (e.AcceptedSequence <= prev)
                {
                    result.Errors.Add("accepted_sequence out of order: " + e.AcceptedSequence + " after " + prev);
                }
                if (!seen.Add(e.AcceptedSequence))
                {
                    result.Errors.Add("duplicate accepted_sequence: " + e.AcceptedSequence);
                }
                prev = e.AcceptedSequence;
                if (e.Payload == null)
                {
                    result.Errors.Add("null payload at sequence " + e.AcceptedSequence);
                }
                if (!e.Accepted)
                {
                    result.Errors.Add("rejected entry must not appear in committed entries: " + e.AcceptedSequence);
                }
                string blob = (e.AcceptanceProvenance ?? string.Empty) + " "
                    + MiniJSON.Json.Serialize(e.Payload);
                foreach (string token in HiddenSentinelTokens)
                {
                    if (blob.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Errors.Add("hidden sentinel leak in entry payload/provenance: " + token);
                    }
                }
            }

            result.Ok = result.Errors.Count == 0;
            return result;
        }
    }

    /// <summary>
    /// Path helpers for Slice 5 fixtures and worker layout (offline / research).
    /// </summary>
    public static class LlmSlice5Paths
    {
        public static string FindRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "YgoMaster.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, "Tools", "fixtures", "llm_replay")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Tools", "fixtures", "llm_replay")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }

        public static string ResolveFixturePath(string fixtureId)
        {
            string root = FindRepoRoot();
            string file = fixtureId.EndsWith(".fixture.json", StringComparison.OrdinalIgnoreCase)
                ? fixtureId
                : fixtureId + ".fixture.json";
            return Path.Combine(root, "Tools", "fixtures", "llm_replay", file);
        }
    }
}

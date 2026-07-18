using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Result of converting server Pvp DuelSettings into a transcript settings snapshot.
    /// </summary>
    public sealed class LlmPvpTranscriptSettingsResult
    {
        public Dictionary<string, object> Settings { get; set; }
        public bool SettingsComplete { get; set; }
        public string IncompleteSettingsReason { get; set; }
        public string SettingsProvenance { get; set; }

        public LlmPvpTranscriptSettingsResult()
        {
            Settings = new Dictionary<string, object>();
            SettingsComplete = false;
            IncompleteSettingsReason = string.Empty;
            SettingsProvenance = LlmPvpAcceptedInputTranscriptAuthority.ExpectedSettingsProvenance;
        }
    }

    /// <summary>
    /// Server-side authoritative accepted-input transcript for Pvp (YGOMASTER-LLM-005 Slice 5).
    /// Default-off. Captures only after matching RunEffectSeq + successful native return.
    /// Never consumes search-worker branch results. Never invoked from the game client assembly.
    /// </summary>
    public sealed class LlmPvpAcceptedInputTranscriptAuthority
    {
        public static bool DefaultEnabled
        {
            get { return false; }
        }

        public const string LaunchKeyEnabled = "llm_pvp_accepted_input_transcript_enabled";
        public const string LaunchKeyFlushPath = "llm_pvp_accepted_input_transcript_flush_path";
        public const string ExpectedSettingsProvenance = "pvp_server_duel_settings";
        public const string AutomaticCommitmentOrigin = "automatic_client_commit";
        public const string EngineInitSequence = "pvp_dll_init_v1";

        /// <summary>
        /// Test-only fault injection after native success, before commit. Production leaves null.
        /// Non-public so it is not production API surface (harness uses NonPublic reflection).
        /// </summary>
#pragma warning disable 0649 // assigned only via harness reflection (NonPublic)
        internal static Action TestFaultInjectPostNativeCapture;
#pragma warning restore 0649

        static readonly HashSet<string> KnownSubtypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "DoCommand",
            "MovePhase",
            "CancelCommand",
            "CancelCommand2",
            "Dialog",
            "ListCardExData",
            "ListIndex",
            "ListInitString",
        };

        public bool Enabled { get; private set; }
        public LlmAcceptedInputTranscript Transcript { get; private set; }
        public string FlushPath { get; private set; }

        long nextAcceptedSequence;
        readonly object sync = new object();

        public LlmPvpAcceptedInputTranscriptAuthority()
        {
            Enabled = DefaultEnabled;
            Transcript = new LlmAcceptedInputTranscript();
            FlushPath = string.Empty;
            nextAcceptedSequence = 1;
        }

        /// <summary>
        /// Convert a server DuelSettings instance or a dictionary-shaped snapshot into transcript settings.
        /// Seed 0 is legitimate and preserved. Provenance is always pvp_server_duel_settings for this path.
        /// </summary>
        public static LlmPvpTranscriptSettingsResult FromServerDuelSettings(object serverDuelSettings)
        {
            LlmPvpTranscriptSettingsResult result = new LlmPvpTranscriptSettingsResult();
            Dictionary<string, object> settings = NormalizeServerSettingsObject(serverDuelSettings);
            result.Settings = settings;
            result.SettingsProvenance = ExpectedSettingsProvenance;
            settings["source"] = ExpectedSettingsProvenance;
            settings["settings_provenance"] = ExpectedSettingsProvenance;
            settings["provenance"] = ExpectedSettingsProvenance;
            if (!settings.ContainsKey("engine_init_sequence"))
            {
                settings["engine_init_sequence"] = EngineInitSequence;
            }

            string reason;
            result.SettingsComplete = IsSettingsComplete(settings, out reason);
            result.IncompleteSettingsReason = result.SettingsComplete ? string.Empty : (reason ?? "incomplete");
            return result;
        }

        public static bool IsSettingsComplete(Dictionary<string, object> settings, out string reason)
        {
            reason = null;
            if (settings == null)
            {
                reason = "duel_settings_null";
                return false;
            }

            // Shared deck/seed baseline (nonempty mains, both seats).
            string baseReason;
            if (!LlmAcceptedInputTranscriptRecorder.TryValidateCompleteDuelSettings(settings, out baseReason))
            {
                reason = baseReason;
                return false;
            }

            string source = GetString(settings, "source", "settings_provenance", "provenance");
            if (!string.IsNullOrEmpty(source)
                && !string.Equals(source, ExpectedSettingsProvenance, StringComparison.Ordinal)
                && source.IndexOf("client", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reason = "non_authoritative_settings_source";
                return false;
            }

            // Authoritative path requires full engine-init field set.
            string[] requiredKeys = new string[]
            {
                "seed", "first_player", "regulation_id", "duel_limited_type",
                "my_player_num", "duel_type", "tag",
                "life0", "life1", "hnum0", "hnum1", "noshuffle",
                "player0_type", "player1_type", "cpu0_param", "cpu1_param",
                "engine_init_sequence",
            };
            foreach (string key in requiredKeys)
            {
                if (!settings.ContainsKey(key) || settings[key] == null)
                {
                    reason = "missing_engine_init_field:" + key;
                    return false;
                }
            }

            string engineInit = Convert.ToString(settings["engine_init_sequence"]);
            if (!string.Equals(engineInit, EngineInitSequence, StringComparison.Ordinal))
            {
                reason = "engine_init_sequence_mismatch";
                return false;
            }

            int firstPlayer = Convert.ToInt32(settings["first_player"]);
            if (firstPlayer != 0 && firstPlayer != 1)
            {
                reason = "first_player_out_of_range";
                return false;
            }

            // All six deck keys required; each a non-string IEnumerable of strictly-positive card ids.
            Dictionary<string, object> decks = settings["decks"] as Dictionary<string, object>;
            if (decks == null)
            {
                reason = "decks_not_object";
                return false;
            }
            string[] deckKeys = new string[]
            {
                "player0_main", "player1_main", "player0_extra", "player1_extra",
                "player0_side", "player1_side",
            };
            foreach (string dk in deckKeys)
            {
                if (!decks.ContainsKey(dk) || decks[dk] == null)
                {
                    reason = "missing_deck_key:" + dk;
                    return false;
                }
                if (!TryValidateDeckList(decks[dk], out reason))
                {
                    reason = "invalid_deck_list:" + dk + (reason != null ? ":" + reason : "");
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public static bool IsReplayEligible(LlmAcceptedInputTranscript transcript)
        {
            if (transcript == null || transcript.DuelSettings == null)
            {
                return false;
            }
            string source = GetString(
                transcript.DuelSettings, "source", "settings_provenance", "provenance");
            if (!string.Equals(source, ExpectedSettingsProvenance, StringComparison.Ordinal))
            {
                return false;
            }
            string reason;
            if (!IsSettingsComplete(transcript.DuelSettings, out reason))
            {
                return false;
            }
            LlmAcceptedInputTranscriptValidationResult validation =
                LlmAcceptedInputTranscriptValidator.Validate(transcript);
            if (validation == null || !validation.Ok)
            {
                return false;
            }
            if (transcript.Entries != null)
            {
                foreach (LlmAcceptedInputEntry e in transcript.Entries)
                {
                    if (e != null && e.Kind == LlmAcceptedInputKind.Automatic)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public static LlmPvpAcceptedInputTranscriptAuthority BeginFromPvpLaunch(
            Dictionary<string, object> launchSettings,
            object serverDuelSettings = null)
        {
            if (serverDuelSettings == null && launchSettings != null)
            {
                object embedded;
                if (launchSettings.TryGetValue("server_duel_settings", out embedded) && embedded != null)
                {
                    serverDuelSettings = embedded;
                }
                else if (launchSettings.TryGetValue("Duel", out embedded) && embedded != null)
                {
                    serverDuelSettings = embedded;
                }
            }
            LlmPvpAcceptedInputTranscriptAuthority session = new LlmPvpAcceptedInputTranscriptAuthority();
            session.InitializeFromLaunch(launchSettings, serverDuelSettings);
            return session;
        }

        void InitializeFromLaunch(Dictionary<string, object> launchSettings, object serverDuelSettings)
        {
            lock (sync)
            {
                bool enabled = false;
                string flushPath = string.Empty;
                if (launchSettings != null)
                {
                    object en;
                    if (launchSettings.TryGetValue(LaunchKeyEnabled, out en) && en != null)
                    {
                        enabled = Convert.ToBoolean(en);
                    }
                    object fp;
                    if (launchSettings.TryGetValue(LaunchKeyFlushPath, out fp) && fp != null)
                    {
                        flushPath = Convert.ToString(fp) ?? string.Empty;
                    }
                }

                Enabled = enabled;
                FlushPath = flushPath ?? string.Empty;
                Transcript = new LlmAcceptedInputTranscript();
                nextAcceptedSequence = 1;

                LlmPvpTranscriptSettingsResult converted = FromServerDuelSettings(
                    serverDuelSettings ?? launchSettings);
                if (converted != null && converted.Settings != null)
                {
                    Transcript.DuelSettings = converted.Settings;
                }
            }
        }

        /// <summary>
        /// Idempotent process-exit flush (safe to call from disconnect / Environment.Exit paths).
        /// </summary>
        public static void SafeFlushOnProcessExit(LlmPvpAcceptedInputTranscriptAuthority authority)
        {
            if (authority == null)
            {
                return;
            }
            try
            {
                authority.FlushOnShutdown();
            }
            catch
            {
            }
        }

        public void FlushOnShutdown()
        {
            lock (sync)
            {
                if (string.IsNullOrEmpty(FlushPath) || !Enabled)
                {
                    return;
                }

                string tempPath = null;
                try
                {
                    string dir = Path.GetDirectoryName(FlushPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    Dictionary<string, object> root =
                        LlmAcceptedInputTranscriptSerializer.ToDictionary(Transcript);
                    root["settings_complete"] = false;
                    string reason;
                    if (Transcript != null && Transcript.DuelSettings != null
                        && IsSettingsComplete(Transcript.DuelSettings, out reason))
                    {
                        root["settings_complete"] = true;
                    }
                    else if (Transcript != null && Transcript.DuelSettings != null)
                    {
                        IsSettingsComplete(Transcript.DuelSettings, out reason);
                        if (!string.IsNullOrEmpty(reason))
                        {
                            root["incomplete_settings_reason"] = reason;
                        }
                    }
                    root["settings_provenance"] = ExpectedSettingsProvenance;
                    root["replay_eligible"] = IsReplayEligible(Transcript);

                    string json = MiniJSON.Json.Serialize(root) ?? "{}";
                    tempPath = FlushPath + ".tmp." + Guid.NewGuid().ToString("N");
                    File.WriteAllText(tempPath, json, Encoding.UTF8);

                    // Atomic same-volume replace: never delete destination before rename.
                    if (File.Exists(FlushPath))
                    {
                        // File.Replace(source, dest, backup) — backup optional; use null backup via overload.
                        // .NET Framework / .NET Core: Replace(string, string, string) requires backup path.
                        string backup = FlushPath + ".bak." + Guid.NewGuid().ToString("N");
                        try
                        {
                            File.Replace(tempPath, FlushPath, backup);
                            try { File.Delete(backup); } catch { }
                            tempPath = null; // consumed by Replace
                        }
                        catch
                        {
                            // Retain old destination on failure; clean backup if partial.
                            try { if (File.Exists(backup)) File.Delete(backup); } catch { }
                            throw;
                        }
                    }
                    else
                    {
                        File.Move(tempPath, FlushPath);
                        tempPath = null;
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        Console.Error.WriteLine(
                            "[LlmPvpAcceptedInputTranscriptAuthority] FlushOnShutdown failed: "
                            + ex.Message);
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    if (!string.IsNullOrEmpty(tempPath))
                    {
                        try
                        {
                            if (File.Exists(tempPath))
                            {
                                File.Delete(tempPath);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        public bool TryAcceptVoidAfterNative(
            ulong currentEngineSeq,
            ulong messageSeq,
            int actorPlayer,
            string inputSubtype,
            Dictionary<string, object> payload,
            Action nativeInvoke)
        {
            if (currentEngineSeq != messageSeq)
            {
                NoteStale(inputSubtype, messageSeq, actorPlayer);
                return false;
            }
            if (nativeInvoke == null)
            {
                return false;
            }
            try
            {
                nativeInvoke();
            }
            catch
            {
                throw;
            }
            return TryCommitAfterNative(actorPlayer, messageSeq, inputSubtype, payload);
        }

        public bool TryAcceptReturnAfterNative(
            ulong currentEngineSeq,
            ulong messageSeq,
            int actorPlayer,
            string inputSubtype,
            Dictionary<string, object> payload,
            Func<int> nativeInvoke,
            out int nativeResult)
        {
            nativeResult = 0;
            if (currentEngineSeq != messageSeq)
            {
                NoteStale(inputSubtype, messageSeq, actorPlayer);
                return false;
            }
            if (nativeInvoke == null)
            {
                return false;
            }
            try
            {
                nativeResult = nativeInvoke();
            }
            catch
            {
                throw;
            }
            return TryCommitAfterNative(actorPlayer, messageSeq, inputSubtype, payload);
        }

        bool TryCommitAfterNative(
            int actorPlayer,
            ulong runEffectSeq,
            string inputSubtype,
            Dictionary<string, object> payload)
        {
            try
            {
                Action inject = TestFaultInjectPostNativeCapture;
                if (inject != null)
                {
                    inject();
                }
                return CommitAccepted(actorPlayer, runEffectSeq, inputSubtype, payload);
            }
            catch (Exception ex)
            {
                // Capture/serialization faults are diagnostic only — never alter duel control.
                try
                {
                    NoteCaptureFault(inputSubtype, runEffectSeq, actorPlayer, ex.Message);
                }
                catch
                {
                }
                return false;
            }
        }

        bool CommitAccepted(
            int actorPlayer,
            ulong runEffectSeq,
            string inputSubtype,
            Dictionary<string, object> payload)
        {
            if (!Enabled)
            {
                return false;
            }
            if (actorPlayer != 0 && actorPlayer != 1)
            {
                NoteRejected(inputSubtype, runEffectSeq, actorPlayer, "invalid_actor");
                return false;
            }
            if (string.IsNullOrEmpty(inputSubtype) || !KnownSubtypes.Contains(inputSubtype))
            {
                NoteRejected(inputSubtype, runEffectSeq, actorPlayer, "unknown_input_subtype");
                return false;
            }

            lock (sync)
            {
                Dictionary<string, object> stored = ClonePayload(payload);
                if (stored == null)
                {
                    stored = new Dictionary<string, object>();
                }
                if (!stored.ContainsKey("input_subtype"))
                {
                    stored["input_subtype"] = inputSubtype;
                }

                LlmAcceptedInputKind kind;
                if (!TryMapSubtypeToKind(inputSubtype, out kind))
                {
                    NoteRejected(inputSubtype, runEffectSeq, actorPlayer, "unknown_input_subtype");
                    return false;
                }

                LlmAcceptedInputEntry entry = new LlmAcceptedInputEntry();
                entry.AcceptedSequence = nextAcceptedSequence++;
                entry.Kind = kind;
                entry.Actor = actorPlayer;
                entry.RunEffectSeq = runEffectSeq;
                entry.Payload = stored;
                entry.Accepted = true;
                entry.AcceptanceProvenance = "pvp_engine_accepted";
                Transcript.Entries.Add(entry);
                return true;
            }
        }

        void NoteStale(string inputSubtype, ulong messageSeq, int actorPlayer)
        {
            NoteRejected(inputSubtype, messageSeq, actorPlayer, "stale_seq");
        }

        void NoteCaptureFault(string inputSubtype, ulong seq, int actor, string detail)
        {
            NoteRejected(inputSubtype, seq, actor, "capture_fault:" + (detail ?? string.Empty));
        }

        void NoteRejected(string inputSubtype, ulong messageSeq, int actorPlayer, string status)
        {
            if (!Enabled)
            {
                return;
            }
            lock (sync)
            {
                Transcript.ExcludedAttempts.Add(new Dictionary<string, object>()
                {
                    { "kind", inputSubtype ?? string.Empty },
                    { "run_effect_seq", messageSeq },
                    { "actor", actorPlayer },
                    { "status", status ?? "rejected" },
                    { "accepted", false },
                });
            }
        }

        static bool TryMapSubtypeToKind(string inputSubtype, out LlmAcceptedInputKind kind)
        {
            kind = LlmAcceptedInputKind.DoCommand;
            if (string.IsNullOrEmpty(inputSubtype) || !KnownSubtypes.Contains(inputSubtype))
            {
                return false;
            }
            switch (inputSubtype)
            {
                case "DoCommand":
                    kind = LlmAcceptedInputKind.DoCommand;
                    return true;
                case "MovePhase":
                    kind = LlmAcceptedInputKind.MovePhase;
                    return true;
                case "Dialog":
                    kind = LlmAcceptedInputKind.Dialog;
                    return true;
                case "CancelCommand":
                case "CancelCommand2":
                    kind = LlmAcceptedInputKind.Cancel;
                    return true;
                case "ListCardExData":
                case "ListIndex":
                case "ListInitString":
                    kind = LlmAcceptedInputKind.List;
                    return true;
                default:
                    return false;
            }
        }

        static Dictionary<string, object> ClonePayload(Dictionary<string, object> payload)
        {
            if (payload == null)
            {
                return new Dictionary<string, object>();
            }
            Dictionary<string, object> copy = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kv in payload)
            {
                copy[kv.Key] = kv.Value;
            }
            return copy;
        }

        /// <summary>
        /// Build the exact snapshot of values Pvp uses to initialize duel.dll after life/hnum normalization.
        /// regulation_id and duel_limited_type are separate fields (never conflated).
        /// </summary>
        public static Dictionary<string, object> BuildSnapshotFromEngineInit(
            uint randSeed,
            int firstPlayer,
            int regulationId,
            int duelLimitedType,
            int myPlayerNum,
            int duelType,
            bool tag,
            int life0,
            int life1,
            int hnum0,
            int hnum1,
            bool noshuffle,
            int player0Type,
            int player1Type,
            uint cpu0Param,
            uint cpu1Param,
            IList player0Main,
            IList player1Main,
            IList player0Extra,
            IList player1Extra,
            IList player0Side,
            IList player1Side)
        {
            return new Dictionary<string, object>()
            {
                { "seed", unchecked((long)randSeed) },
                { "RandSeed", randSeed },
                { "first_player", firstPlayer },
                { "FirstPlayer", firstPlayer },
                { "regulation_id", regulationId },
                { "duel_limited_type", duelLimitedType },
                { "my_player_num", myPlayerNum },
                { "duel_type", duelType },
                { "tag", tag },
                { "life0", life0 },
                { "life1", life1 },
                { "hnum0", hnum0 },
                { "hnum1", hnum1 },
                { "noshuffle", noshuffle },
                { "player0_type", player0Type },
                { "player1_type", player1Type },
                { "cpu0_param", unchecked((long)cpu0Param) },
                { "cpu1_param", unchecked((long)cpu1Param) },
                { "engine_init_sequence", EngineInitSequence },
                {
                    "decks", new Dictionary<string, object>()
                    {
                        { "player0_main", ToObjectList(player0Main) },
                        { "player1_main", ToObjectList(player1Main) },
                        { "player0_extra", ToObjectList(player0Extra) },
                        { "player1_extra", ToObjectList(player1Extra) },
                        { "player0_side", ToObjectList(player0Side) },
                        { "player1_side", ToObjectList(player1Side) },
                    }
                },
                { "source", ExpectedSettingsProvenance },
                { "settings_provenance", ExpectedSettingsProvenance },
                { "settings_snapshot_kind", "server_pvp_duel_settings" },
            };
        }

        static List<object> ToObjectList(IList list)
        {
            List<object> result = new List<object>();
            if (list == null)
            {
                return result;
            }
            foreach (object item in list)
            {
                result.Add(item);
            }
            return result;
        }

        /// <summary>
        /// Deck list must be a non-string IEnumerable; every item a convertible strictly-positive card id.
        /// Empty lists are valid (extra/side). Null items and non-enumerable values fail.
        /// </summary>
        static bool TryValidateDeckList(object listObj, out string reason)
        {
            reason = null;
            if (listObj == null)
            {
                reason = "deck_list_null";
                return false;
            }
            if (listObj is string)
            {
                reason = "deck_list_is_string";
                return false;
            }
            IEnumerable e = listObj as IEnumerable;
            if (e == null)
            {
                reason = "deck_list_not_enumerable";
                return false;
            }
            foreach (object item in e)
            {
                if (item == null)
                {
                    reason = "null_card_id";
                    return false;
                }
                int id;
                try
                {
                    id = Convert.ToInt32(item);
                }
                catch
                {
                    reason = "non_integer_card_id";
                    return false;
                }
                if (id <= 0)
                {
                    reason = "non_positive_card_id:" + id;
                    return false;
                }
            }
            return true;
        }

        static Dictionary<string, object> NormalizeServerSettingsObject(object serverDuelSettings)
        {
            if (serverDuelSettings == null)
            {
                return new Dictionary<string, object>();
            }
            Dictionary<string, object> asDict = serverDuelSettings as Dictionary<string, object>;
            if (asDict != null)
            {
                return NormalizeDictionarySnapshot(asDict);
            }
            return NormalizeDictionarySnapshot(ExtractSettingsViaReflection(serverDuelSettings));
        }

        static Dictionary<string, object> NormalizeDictionarySnapshot(Dictionary<string, object> input)
        {
            Dictionary<string, object> settings = new Dictionary<string, object>();
            if (input == null)
            {
                return settings;
            }

            object seed = Coalesce(input, "seed", "Seed", "RandSeed", "rand_seed", "RandomSeed");
            if (seed != null)
            {
                settings["seed"] = Convert.ToInt64(seed);
                settings["RandSeed"] = Convert.ToUInt32(Convert.ToInt64(seed));
            }

            object first = Coalesce(input, "first_player", "FirstPlayer", "StartingPlayer");
            if (first != null)
            {
                settings["first_player"] = Convert.ToInt32(first);
            }

            // regulation_id and duel_limited_type are independent.
            object reg = Coalesce(input, "regulation_id", "RegulationId");
            if (reg != null)
            {
                settings["regulation_id"] = Convert.ToInt32(reg);
            }
            object dlt = Coalesce(input, "duel_limited_type", "DuelLimitedType");
            if (dlt != null)
            {
                settings["duel_limited_type"] = Convert.ToInt32(dlt);
            }
            // Do not copy limited_type into regulation_id (avoids historical conflation).
            // limited_type alone is not an authoritative alias for either field.

            CopyIfPresent(input, settings, "my_player_num");
            CopyIfPresent(input, settings, "duel_type");
            CopyIfPresent(input, settings, "tag");
            CopyIfPresent(input, settings, "life0");
            CopyIfPresent(input, settings, "life1");
            CopyIfPresent(input, settings, "hnum0");
            CopyIfPresent(input, settings, "hnum1");
            CopyIfPresent(input, settings, "noshuffle");
            CopyIfPresent(input, settings, "player0_type");
            CopyIfPresent(input, settings, "player1_type");
            CopyIfPresent(input, settings, "cpu0_param");
            CopyIfPresent(input, settings, "cpu1_param");
            CopyIfPresent(input, settings, "engine_init_sequence");

            object decksObj = Coalesce(input, "decks", "Decks");
            Dictionary<string, object> decksIn = decksObj as Dictionary<string, object>;
            Dictionary<string, object> decks = new Dictionary<string, object>();
            if (decksIn != null)
            {
                decks["player0_main"] = NormalizeIdList(Coalesce(decksIn, "player0_main", "Player0Main", "main0"));
                decks["player1_main"] = NormalizeIdList(Coalesce(decksIn, "player1_main", "Player1Main", "main1"));
                decks["player0_extra"] = NormalizeIdList(Coalesce(decksIn, "player0_extra", "Player0Extra", "extra0"));
                decks["player1_extra"] = NormalizeIdList(Coalesce(decksIn, "player1_extra", "Player1Extra", "extra1"));
                decks["player0_side"] = NormalizeIdList(Coalesce(decksIn, "player0_side", "Player0Side", "side0"));
                decks["player1_side"] = NormalizeIdList(Coalesce(decksIn, "player1_side", "Player1Side", "side1"));
            }
            else
            {
                decks["player0_main"] = new List<object>();
                decks["player1_main"] = new List<object>();
                decks["player0_extra"] = new List<object>();
                decks["player1_extra"] = new List<object>();
                decks["player0_side"] = new List<object>();
                decks["player1_side"] = new List<object>();
            }
            settings["decks"] = decks;

            object src = Coalesce(input, "source", "settings_provenance", "provenance");
            if (src != null)
            {
                settings["source"] = Convert.ToString(src);
            }

            return settings;
        }

        static void CopyIfPresent(Dictionary<string, object> input, Dictionary<string, object> output, string key)
        {
            object v;
            if (input.TryGetValue(key, out v) && v != null)
            {
                output[key] = v;
            }
        }

        static Dictionary<string, object> ExtractSettingsViaReflection(object obj)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            Type t = obj.GetType();
            TryCopyMember(obj, t, d, "RandSeed", "seed");
            TryCopyMember(obj, t, d, "FirstPlayer", "first_player");
            TryCopyMember(obj, t, d, "regulation_id", "regulation_id");
            // Never map regulation_id -> limited_type / duel_limited_type here.

            object decks = null;
            System.Reflection.PropertyInfo deckProp = t.GetProperty("Deck");
            if (deckProp != null)
            {
                decks = deckProp.GetValue(obj, null);
            }
            if (decks is IList)
            {
                IList deckArr = (IList)decks;
                Dictionary<string, object> deckMap = new Dictionary<string, object>();
                deckMap["player0_main"] = ExtractDeckIds(deckArr, 0, "MainDeckCards");
                deckMap["player1_main"] = ExtractDeckIds(deckArr, 1, "MainDeckCards");
                deckMap["player0_extra"] = ExtractDeckIds(deckArr, 0, "ExtraDeckCards");
                deckMap["player1_extra"] = ExtractDeckIds(deckArr, 1, "ExtraDeckCards");
                deckMap["player0_side"] = ExtractDeckIds(deckArr, 0, "SideDeckCards");
                deckMap["player1_side"] = ExtractDeckIds(deckArr, 1, "SideDeckCards");
                d["decks"] = deckMap;
            }
            return d;
        }

        static List<object> ExtractDeckIds(IList deckArr, int index, string collectionProp)
        {
            List<object> ids = new List<object>();
            if (deckArr == null || index < 0 || index >= deckArr.Count || deckArr[index] == null)
            {
                return ids;
            }
            object deck = deckArr[index];
            System.Reflection.PropertyInfo colProp = deck.GetType().GetProperty(collectionProp);
            if (colProp == null)
            {
                return ids;
            }
            object col = colProp.GetValue(deck, null);
            if (col == null)
            {
                return ids;
            }
            System.Reflection.MethodInfo getIds = col.GetType().GetMethod("GetIds", Type.EmptyTypes);
            if (getIds == null)
            {
                return ids;
            }
            object enumObj = getIds.Invoke(col, null);
            IEnumerable enumerable = enumObj as IEnumerable;
            if (enumerable == null)
            {
                return ids;
            }
            foreach (object id in enumerable)
            {
                ids.Add(Convert.ToInt32(id));
            }
            return ids;
        }

        static void TryCopyMember(object obj, Type t, Dictionary<string, object> d, string member, string key)
        {
            System.Reflection.FieldInfo f = t.GetField(member);
            if (f != null)
            {
                d[key] = f.GetValue(obj);
                return;
            }
            System.Reflection.PropertyInfo p = t.GetProperty(member);
            if (p != null)
            {
                d[key] = p.GetValue(obj, null);
            }
        }

        static List<object> NormalizeIdList(object value)
        {
            List<object> list = new List<object>();
            if (value == null)
            {
                return list;
            }
            IEnumerable e = value as IEnumerable;
            if (e == null || value is string)
            {
                return list;
            }
            foreach (object item in e)
            {
                if (item == null)
                {
                    continue;
                }
                try
                {
                    list.Add(Convert.ToInt32(item));
                }
                catch
                {
                    list.Add(item);
                }
            }
            return list;
        }

        static object Coalesce(Dictionary<string, object> d, params string[] keys)
        {
            if (d == null)
            {
                return null;
            }
            foreach (string k in keys)
            {
                object v;
                if (d.TryGetValue(k, out v) && v != null)
                {
                    return v;
                }
            }
            return null;
        }

        static string GetString(Dictionary<string, object> d, params string[] keys)
        {
            object v = Coalesce(d, keys);
            return v != null ? Convert.ToString(v) : null;
        }
    }
}

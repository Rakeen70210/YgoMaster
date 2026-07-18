using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Independent GREEN review remediation for Slice 5 Pvp authority (TDD).
    /// Tests-first: intended RED against pre-fix production, then GREEN.
    /// </summary>
    static class Llm005Slice5PvpAuthorityRemediationTests
    {
        const string AuthorityTypeName = "LlmPvpAcceptedInputTranscriptAuthority";
        const string ExpectedProvenance = "pvp_server_duel_settings";
        const string LaunchKeyEnabled = "llm_pvp_accepted_input_transcript_enabled";
        const string LaunchKeyFlushPath = "llm_pvp_accepted_input_transcript_flush_path";

        // Distinct sentinels — must never be conflated.
        const int SentinelRegulationId = 73;
        const int SentinelDuelLimitedType = 0; // DuelLimitedType.None

        public static void RunAll()
        {
            Type authority = RequireAuthorityType();

            RegulationIdAndDuelLimitedTypeAreSeparateFields(authority);
            CompleteSettingsRequireFullEngineInitInputs(authority);
            MissingInitFieldsNotCompleteOrReplayEligible(authority);
            PvpBeginsAuthorityAfterLifeHnumNormalizationBeforeEngineInit();
            PvpFlushCoverageOnDisconnectAndEarlyExitPaths();
            FlushIsAtomicReplaceWithoutDeletingDestination(authority);
            CaptureFaultAfterNativeDoesNotAlterDuelControl(authority);
            UnknownSubtypeDoesNotMapToDoCommand(authority);
            InvalidActorAndFirstPlayerRejected(authority);
            PositiveCardIdsRequiredForComplete(authority);

            // Independent review: deck-slot strictness, engine_init_sequence, API hygiene.
            AllSixDeckKeysRequiredAndStrictlyPositiveIds(authority);
            NullDeckItemAndNonEnumerableDeckValueFail(authority);
            EngineInitSequenceRequiredExactValue(authority);
            ObsoleteBuildSnapshotOverloadRemoved(authority);
            FaultInjectHookIsNonPublic(authority);
        }

        static Type RequireAuthorityType()
        {
            Type t = TryGetType(AuthorityTypeName);
            if (t == null)
            {
                throw new InvalidOperationException(
                    "YGOMASTER-LLM-005 Slice 5 remediation RED: missing " + AuthorityTypeName);
            }
            return t;
        }

        // -----------------------------------------------------------------
        // (1) regulation_id vs duel_limited_type
        // -----------------------------------------------------------------
        static void RegulationIdAndDuelLimitedTypeAreSeparateFields(Type authority)
        {
            MethodInfo convert = RequirePublicStaticMethod(authority, "FromServerDuelSettings");
            Dictionary<string, object> input = BuildFullAuthoritativeSettings(seed: 9u);
            // Distinct sentinels
            input["regulation_id"] = SentinelRegulationId;
            input["duel_limited_type"] = SentinelDuelLimitedType;
            // Intentionally do NOT set limited_type as alias of regulation — GREEN must keep both exact.
            input.Remove("limited_type");

            object result = convert.Invoke(null, new object[] { input });
            Dictionary<string, object> settings = RequireSettingsDict(result);

            AssertTrue(settings.ContainsKey("regulation_id"), "regulation_id present");
            AssertTrue(settings.ContainsKey("duel_limited_type"), "duel_limited_type present");
            AssertEqual(SentinelRegulationId, Convert.ToInt32(settings["regulation_id"]),
                "regulation_id preserved exactly (sentinel 73)");
            AssertEqual(SentinelDuelLimitedType, Convert.ToInt32(settings["duel_limited_type"]),
                "duel_limited_type preserved exactly (sentinel 0 / DuelLimitedType.None)");
            AssertFalse(
                Convert.ToInt32(settings["regulation_id"]) == Convert.ToInt32(settings["duel_limited_type"])
                && SentinelRegulationId != SentinelDuelLimitedType,
                "regulation_id must not be conflated with duel_limited_type");

            // Pvp snapshot builder must not write regulation_id into limited_type alone.
            string pvp = File.ReadAllText(Path.Combine(LlmSlice5Paths.FindRepoRoot(),
                "YgoMasterServer", "Pvp.cs"), Encoding.UTF8);
            AssertFalse(
                pvp.IndexOf("regulation_id != 0 ? duelSettings.regulation_id : duelSettings.Limit",
                    StringComparison.Ordinal) >= 0
                || pvp.IndexOf("duelSettings.regulation_id != 0 ? duelSettings.regulation_id",
                    StringComparison.Ordinal) >= 0,
                "Pvp BuildPvpAuthoritySettingsSnapshot must not conflate regulation_id with Limit/limited_type");
            AssertTrue(
                pvp.IndexOf("duel_limited_type", StringComparison.Ordinal) >= 0
                || pvp.IndexOf("DuelLimitedType", StringComparison.Ordinal) >= 0,
                "Pvp snapshot must record duel_limited_type from DLL_DuelSetDuelLimitedType value");
        }

        // -----------------------------------------------------------------
        // (2) Full engine-init completeness
        // -----------------------------------------------------------------
        static void CompleteSettingsRequireFullEngineInitInputs(Type authority)
        {
            MethodInfo isComplete = RequirePublicStaticMethod(authority, "IsSettingsComplete");
            MethodInfo isEligible = RequirePublicStaticMethod(authority, "IsReplayEligible");

            Dictionary<string, object> full = BuildFullAuthoritativeSettings(1u);
            object[] args = BindOutString(isComplete, full);
            AssertTrue(Convert.ToBoolean(isComplete.Invoke(null, args)),
                "full engine-init snapshot is settings-complete; reason=" + args[1]);

            LlmAcceptedInputTranscript t = new LlmAcceptedInputTranscript();
            t.DuelSettings = full;
            AssertTrue(Convert.ToBoolean(isEligible.Invoke(null, new object[] { t })),
                "full authoritative snapshot is replay-eligible");

            // Required keys must all appear in FromServerDuelSettings result.
            MethodInfo convert = RequirePublicStaticMethod(authority, "FromServerDuelSettings");
            object result = convert.Invoke(null, new object[] { full });
            Dictionary<string, object> settings = RequireSettingsDict(result);
            string[] required = new string[]
            {
                "seed", "first_player", "regulation_id", "duel_limited_type",
                "my_player_num", "duel_type", "tag",
                "life0", "life1", "hnum0", "hnum1", "noshuffle",
                "player0_type", "player1_type", "cpu0_param", "cpu1_param",
            };
            foreach (string key in required)
            {
                AssertTrue(settings.ContainsKey(key),
                    "authoritative settings must include engine-init field '" + key + "'");
            }
            AssertEqual(0, Convert.ToInt32(settings["my_player_num"]), "my_player_num is 0");
            AssertEqual(false, Convert.ToBoolean(settings["tag"]), "tag is false");
        }

        static void MissingInitFieldsNotCompleteOrReplayEligible(Type authority)
        {
            MethodInfo isComplete = RequirePublicStaticMethod(authority, "IsSettingsComplete");
            MethodInfo isEligible = RequirePublicStaticMethod(authority, "IsReplayEligible");

            // Legacy sparse shape (seed/decks only) — must NOT be complete on authoritative path.
            Dictionary<string, object> sparse = new Dictionary<string, object>()
            {
                { "seed", 1 },
                { "first_player", 0 },
                { "limited_type", 0 },
                { "source", ExpectedProvenance },
                {
                    "decks", new Dictionary<string, object>()
                    {
                        { "player0_main", new object[] { 1, 2 } },
                        { "player1_main", new object[] { 3, 4 } },
                        { "player0_extra", new object[] { } },
                        { "player1_extra", new object[] { } },
                        { "player0_side", new object[] { } },
                        { "player1_side", new object[] { } },
                    }
                },
            };
            object[] args = BindOutString(isComplete, sparse);
            AssertFalse(Convert.ToBoolean(isComplete.Invoke(null, args)),
                "sparse settings missing engine-init fields must be incomplete");
            AssertTrue(!string.IsNullOrEmpty(args[1] as string),
                "incomplete reason for missing engine-init fields");

            LlmAcceptedInputTranscript t = new LlmAcceptedInputTranscript();
            t.DuelSettings = sparse;
            AssertFalse(Convert.ToBoolean(isEligible.Invoke(null, new object[] { t })),
                "sparse must not be replay-eligible");
        }

        // -----------------------------------------------------------------
        // (3) Begin after life/hnum normalization, before engine init
        // -----------------------------------------------------------------
        static void PvpBeginsAuthorityAfterLifeHnumNormalizationBeforeEngineInit()
        {
            string pvp = File.ReadAllText(Path.Combine(LlmSlice5Paths.FindRepoRoot(),
                "YgoMasterServer", "Pvp.cs"), Encoding.UTF8);

            // Strip client ifdef branches for server path analysis: keep #else/#if !YGO_MASTER_CLIENT content.
            int beginIdx = pvp.IndexOf("BeginFromPvpLaunch", StringComparison.Ordinal);
            AssertTrue(beginIdx >= 0, "Pvp must call BeginFromPvpLaunch");

            int lifeNorm = pvp.IndexOf("life[i] = 8000", StringComparison.Ordinal);
            int hnumNorm = pvp.IndexOf("hnum[i] = 5", StringComparison.Ordinal);
            AssertTrue(lifeNorm >= 0 && hnumNorm >= 0, "Pvp must normalize life=8000 and hnum=5");

            // The authoritative Begin that uses BuildPvpAuthoritySettingsSnapshot must be AFTER normalization.
            int snapshotAfterNorm = pvp.IndexOf("BuildPvpAuthoritySettingsSnapshot", lifeNorm, StringComparison.Ordinal);
            // Allow first early Begin only if there is also a post-normalization Begin.
            int postNormBegin = pvp.IndexOf("BeginFromPvpLaunch", Math.Max(lifeNorm, hnumNorm), StringComparison.Ordinal);
            AssertTrue(postNormBegin >= 0,
                "Pvp must BeginFromPvpLaunch AFTER life/hnum normalization so captured values match engine calls");

            int engineInit = pvp.IndexOf("DLL_DuelSetMyPlayerNum", Math.Max(lifeNorm, hnumNorm), StringComparison.Ordinal);
            AssertTrue(engineInit >= 0, "engine init present");
            AssertTrue(postNormBegin < engineInit,
                "BeginFromPvpLaunch must run immediately before engine init calls (after normalization)");

            // Early Begin before normalization is forbidden for the authoritative snapshot path.
            int firstBegin = pvp.IndexOf("BeginFromPvpLaunch", StringComparison.Ordinal);
            int firstLife = pvp.IndexOf("life[i] = 8000", StringComparison.Ordinal);
            // If there is a Begin before life norm, it must not be the only one with BuildPvpAuthoritySettingsSnapshot.
            if (firstBegin < firstLife)
            {
                // Pre-norm Begin is a regression — fail unless it's only in a comment.
                string window = pvp.Substring(Math.Max(0, firstBegin - 80), Math.Min(200, pvp.Length - Math.Max(0, firstBegin - 80)));
                AssertFalse(
                    window.IndexOf("BuildPvpAuthoritySettingsSnapshot", StringComparison.Ordinal) >= 0
                    && firstBegin < firstLife,
                    "must not Begin authoritative snapshot before life/hnum normalization");
            }
        }

        // -----------------------------------------------------------------
        // (4) Flush on Environment.Exit / disconnect paths
        // -----------------------------------------------------------------
        static void PvpFlushCoverageOnDisconnectAndEarlyExitPaths()
        {
            string pvp = File.ReadAllText(Path.Combine(LlmSlice5Paths.FindRepoRoot(),
                "YgoMasterServer", "Pvp.cs"), Encoding.UTF8);

            // Safe flush helper must exist on authority or Pvp.
            Type authority = TryGetType(AuthorityTypeName);
            MethodInfo safe = authority.GetMethod("SafeFlushOnProcessExit",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
            MethodInfo safe2 = authority.GetMethod("FlushOnShutdown",
                BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(safe2, "FlushOnShutdown still required");

            // Prefer explicit SafeFlushOnProcessExit for exit-path coverage.
            AssertNotNull(safe,
                AuthorityTypeName + ".SafeFlushOnProcessExit required for Environment.Exit / disconnect coverage");

            AssertTrue(
                pvp.IndexOf("SafeFlushOnProcessExit", StringComparison.Ordinal) >= 0
                || (pvp.IndexOf("FlushOnShutdown", StringComparison.Ordinal) >= 0
                    && CountOccurrences(pvp, "Environment.Exit") <= CountOccurrences(pvp, "FlushOnShutdown") + 2),
                "Pvp must invoke safe flush on process exit paths (disconnect / !IsConnected / end)");

            // Disconnect callback must flush before Exit.
            int disc = pvp.IndexOf("netClient.Disconnected", StringComparison.Ordinal);
            AssertTrue(disc >= 0, "disconnect handler present");
            string discWindow = pvp.Substring(disc, Math.Min(500, pvp.Length - disc));
            AssertTrue(
                discWindow.IndexOf("SafeFlushOnProcessExit", StringComparison.Ordinal) >= 0
                || discWindow.IndexOf("FlushOnShutdown", StringComparison.Ordinal) >= 0,
                "disconnect path must flush before Environment.Exit");

            // !IsConnected early exit
            int notConn = pvp.IndexOf("!netClient.IsConnected", StringComparison.Ordinal);
            AssertTrue(notConn >= 0, "!IsConnected path present");
            string notConnWindow = pvp.Substring(notConn, Math.Min(400, pvp.Length - notConn));
            AssertTrue(
                notConnWindow.IndexOf("SafeFlushOnProcessExit", StringComparison.Ordinal) >= 0
                || notConnWindow.IndexOf("FlushOnShutdown", StringComparison.Ordinal) >= 0
                || notConnWindow.IndexOf("Environment.Exit", StringComparison.Ordinal) < 0,
                "!IsConnected Environment.Exit path must flush (or not Exit without flush)");
        }

        // -----------------------------------------------------------------
        // (5) Atomic replace without Delete(destination)
        // -----------------------------------------------------------------
        static void FlushIsAtomicReplaceWithoutDeletingDestination(Type authority)
        {
            // Source guard: authority must not Delete(FlushPath) before Move.
            string src = File.ReadAllText(Path.Combine(LlmSlice5Paths.FindRepoRoot(),
                "YgoMasterServer", "Llm", "LlmPvpAcceptedInputTranscriptAuthority.cs"), Encoding.UTF8);
            // crude: after writing temp, must not File.Delete(FlushPath)
            AssertFalse(
                src.IndexOf("File.Delete(FlushPath)", StringComparison.Ordinal) >= 0
                || src.IndexOf("File.Delete(this.FlushPath)", StringComparison.Ordinal) >= 0
                || src.IndexOf("File.Delete(path)", StringComparison.Ordinal) >= 0
                    && src.IndexOf("File.Move(tempPath, FlushPath)", StringComparison.Ordinal) >= 0
                    && src.IndexOf("File.Delete", src.IndexOf("WriteAllText", StringComparison.Ordinal),
                        StringComparison.Ordinal)
                        < src.IndexOf("File.Move", StringComparison.Ordinal),
                "FlushOnShutdown must not File.Delete(destination) before replace/move");

            AssertTrue(
                src.IndexOf("File.Replace", StringComparison.Ordinal) >= 0
                || src.IndexOf("Replace(", StringComparison.Ordinal) >= 0,
                "Flush should use File.Replace when destination exists (atomic same-volume replace)");

            MethodInfo begin = RequirePublicStaticMethod(authority, "BeginFromPvpLaunch");
            string flushPath = Path.Combine(Path.GetTempPath(),
                "ygomaster-llm005-atomic-" + Guid.NewGuid().ToString("N") + ".json");
            // Pre-create destination with known content
            File.WriteAllText(flushPath, "{\"old\":true}", Encoding.UTF8);

            Dictionary<string, object> launch = new Dictionary<string, object>()
            {
                { LaunchKeyEnabled, true },
                { LaunchKeyFlushPath, flushPath },
            };
            object session = begin.Invoke(null, new object[] { launch, BuildFullAuthoritativeSettings(2u) });
            // Commit one entry so flush content differs
            MethodInfo accept = session.GetType().GetMethod("TryAcceptVoidAfterNative",
                BindingFlags.Public | BindingFlags.Instance);
            accept.Invoke(session, new object[]
            {
                1UL, 1UL, 0, "DoCommand",
                new Dictionary<string, object>()
                {
                    { "input_subtype", "DoCommand" },
                    { "player", 0 }, { "position", 1 }, { "index", 0 }, { "command_id", 1 },
                },
                (Action)(() => { }),
            });

            MethodInfo flush = session.GetType().GetMethod("FlushOnShutdown",
                BindingFlags.Public | BindingFlags.Instance);
            flush.Invoke(session, null);
            AssertTrue(File.Exists(flushPath), "destination retained/replaced");
            string content = File.ReadAllText(flushPath, Encoding.UTF8);
            AssertTrue(content.IndexOf("old", StringComparison.Ordinal) < 0
                || content.IndexOf("entries", StringComparison.Ordinal) >= 0,
                "flush replaced content (not left as pure old stub without new data)");
            // Idempotent second flush
            flush.Invoke(session, null);
            string content2 = File.ReadAllText(flushPath, Encoding.UTF8);
            AssertEqual(content, content2, "second flush idempotent content");

            // No leftover temp files next to destination
            string dir = Path.GetDirectoryName(flushPath);
            string baseName = Path.GetFileName(flushPath);
            foreach (string f in Directory.GetFiles(dir, baseName + ".tmp*"))
            {
                throw new InvalidOperationException("temp flush file not cleaned: " + f);
            }
            try { File.Delete(flushPath); } catch { }
        }

        // -----------------------------------------------------------------
        // (6) Post-native capture fault is non-critical
        // -----------------------------------------------------------------
        static void CaptureFaultAfterNativeDoesNotAlterDuelControl(Type authority)
        {
            MethodInfo begin = RequirePublicStaticMethod(authority, "BeginFromPvpLaunch");
            Dictionary<string, object> launch = new Dictionary<string, object>()
            {
                { LaunchKeyEnabled, true },
                { LaunchKeyFlushPath, "" },
            };
            object session = begin.Invoke(null, new object[] { launch, BuildFullAuthoritativeSettings(3u) });

            // Fault injection via reflection (NonPublic preferred; public forbidden by FaultInjectHookIsNonPublic).
            FieldInfo hookField = authority.GetField(
                "TestFaultInjectPostNativeCapture",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? authority.GetField(
                    "TestFaultInjectPostNativeCapture",
                    BindingFlags.Public | BindingFlags.Static);
            AssertNotNull(hookField,
                "TestFaultInjectPostNativeCapture static field required for capture-fault isolation");

            Action inject = () => { throw new InvalidOperationException("injected capture fault"); };
            hookField.SetValue(null, inject);

            try
            {
                MethodInfo accept = session.GetType().GetMethod("TryAcceptVoidAfterNative",
                    BindingFlags.Public | BindingFlags.Instance);
                int nativeCalls = 0;
                bool threwToCaller = false;
                bool committed;
                try
                {
                    committed = Convert.ToBoolean(accept.Invoke(session, new object[]
                    {
                        5UL, 5UL, 0, "MovePhase",
                        new Dictionary<string, object>()
                        {
                            { "input_subtype", "MovePhase" },
                            { "phase", 1 },
                        },
                        (Action)(() => { nativeCalls++; }),
                    }));
                }
                catch (TargetInvocationException)
                {
                    threwToCaller = true;
                    committed = false;
                }

                AssertEqual(1, nativeCalls,
                    "matching input must invoke native exactly once even if capture faults");
                AssertFalse(threwToCaller,
                    "post-native capture/serialization exception must be swallowed (not alter duel control)");
                object _ = committed;
            }
            finally
            {
                hookField.SetValue(null, null);
            }

            // Native exception still propagates and commits zero
            LlmAcceptedInputTranscript transcript =
                session.GetType().GetProperty("Transcript").GetValue(session, null) as LlmAcceptedInputTranscript;
            int before = transcript.Entries.Count;
            MethodInfo accept2 = session.GetType().GetMethod("TryAcceptVoidAfterNative",
                BindingFlags.Public | BindingFlags.Instance);
            bool nativeThrew = false;
            try
            {
                accept2.Invoke(session, new object[]
                {
                    6UL, 6UL, 0, "DoCommand",
                    new Dictionary<string, object>()
                    {
                        { "input_subtype", "DoCommand" },
                        { "player", 0 }, { "position", 1 }, { "index", 0 }, { "command_id", 1 },
                    },
                    (Action)(() => { throw new InvalidOperationException("native boom"); }),
                });
            }
            catch (TargetInvocationException)
            {
                nativeThrew = true;
            }
            AssertTrue(nativeThrew, "native exception must propagate");
            AssertEqual(before, transcript.Entries.Count, "native exception commits zero");
        }

        // -----------------------------------------------------------------
        // (7) Unknown subtype / invalid actor / first player / positive ids
        // -----------------------------------------------------------------
        static void UnknownSubtypeDoesNotMapToDoCommand(Type authority)
        {
            object session = BeginEnabled(authority, 4u);
            MethodInfo accept = session.GetType().GetMethod("TryAcceptVoidAfterNative",
                BindingFlags.Public | BindingFlags.Instance);
            LlmAcceptedInputTranscript tr =
                session.GetType().GetProperty("Transcript").GetValue(session, null) as LlmAcceptedInputTranscript;
            int before = tr.Entries.Count;
            int native = 0;
            bool committed = Convert.ToBoolean(accept.Invoke(session, new object[]
            {
                8UL, 8UL, 0, "NotARealSubtype",
                new Dictionary<string, object>() { { "input_subtype", "NotARealSubtype" } },
                (Action)(() => { native++; }),
            }));
            AssertEqual(1, native, "unknown subtype still invokes native once (duel control)");
            AssertFalse(committed, "unknown subtype must not commit");
            AssertEqual(before, tr.Entries.Count,
                "unknown subtype must not create a DoCommand (or any) committed row");
        }

        static void InvalidActorAndFirstPlayerRejected(Type authority)
        {
            MethodInfo isComplete = RequirePublicStaticMethod(authority, "IsSettingsComplete");
            Dictionary<string, object> badFp = BuildFullAuthoritativeSettings(5u);
            badFp["first_player"] = 2;
            object[] args = BindOutString(isComplete, badFp);
            AssertFalse(Convert.ToBoolean(isComplete.Invoke(null, args)),
                "first_player must be 0 or 1");

            object session = BeginEnabled(authority, 6u);
            MethodInfo accept = session.GetType().GetMethod("TryAcceptVoidAfterNative",
                BindingFlags.Public | BindingFlags.Instance);
            LlmAcceptedInputTranscript tr =
                session.GetType().GetProperty("Transcript").GetValue(session, null) as LlmAcceptedInputTranscript;
            int before = tr.Entries.Count;
            int native = 0;
            bool committed = Convert.ToBoolean(accept.Invoke(session, new object[]
            {
                9UL, 9UL, 7, "DoCommand",
                new Dictionary<string, object>()
                {
                    { "input_subtype", "DoCommand" },
                    { "player", 0 }, { "position", 1 }, { "index", 0 }, { "command_id", 1 },
                },
                (Action)(() => { native++; }),
            }));
            AssertEqual(1, native, "invalid actor still runs native (duel)");
            AssertFalse(committed, "actor must be 0 or 1 to commit");
            AssertEqual(before, tr.Entries.Count, "invalid actor no committed row");
        }

        static void PositiveCardIdsRequiredForComplete(Type authority)
        {
            MethodInfo isComplete = RequirePublicStaticMethod(authority, "IsSettingsComplete");
            Dictionary<string, object> bad = BuildFullAuthoritativeSettings(7u);
            Dictionary<string, object> decks = bad["decks"] as Dictionary<string, object>;
            decks["player0_main"] = new object[] { 0, -5, 1001 };
            object[] args = BindOutString(isComplete, bad);
            AssertFalse(Convert.ToBoolean(isComplete.Invoke(null, args)),
                "non-positive card ids must fail completeness");
        }

        // -----------------------------------------------------------------
        // Review: strict deck slots + engine_init_sequence + API hygiene
        // -----------------------------------------------------------------
        static void AllSixDeckKeysRequiredAndStrictlyPositiveIds(Type authority)
        {
            MethodInfo isComplete = RequirePublicStaticMethod(authority, "IsSettingsComplete");
            string[] requiredDeckKeys = new string[]
            {
                "player0_main", "player1_main",
                "player0_extra", "player1_extra",
                "player0_side", "player1_side",
            };

            foreach (string missingKey in requiredDeckKeys)
            {
                Dictionary<string, object> s = BuildFullAuthoritativeSettings(11u);
                Dictionary<string, object> decks = s["decks"] as Dictionary<string, object>;
                decks.Remove(missingKey);
                object[] args = BindOutString(isComplete, s);
                AssertFalse(Convert.ToBoolean(isComplete.Invoke(null, args)),
                    "missing deck key '" + missingKey + "' must fail IsSettingsComplete");
                AssertTrue(!string.IsNullOrEmpty(args[1] as string),
                    "reason for missing deck key " + missingKey);
            }

            // Empty extra/side still valid; mains nonempty + positive ids.
            Dictionary<string, object> emptyExtraSide = BuildFullAuthoritativeSettings(12u);
            Dictionary<string, object> d2 = emptyExtraSide["decks"] as Dictionary<string, object>;
            d2["player0_extra"] = new List<object>();
            d2["player1_extra"] = new List<object>();
            d2["player0_side"] = new List<object>();
            d2["player1_side"] = new List<object>();
            object[] okArgs = BindOutString(isComplete, emptyExtraSide);
            AssertTrue(Convert.ToBoolean(isComplete.Invoke(null, okArgs)),
                "empty extra/side lists remain valid when all six keys present");
        }

        static void NullDeckItemAndNonEnumerableDeckValueFail(Type authority)
        {
            MethodInfo isComplete = RequirePublicStaticMethod(authority, "IsSettingsComplete");

            Dictionary<string, object> nullItem = BuildFullAuthoritativeSettings(13u);
            Dictionary<string, object> decks = nullItem["decks"] as Dictionary<string, object>;
            decks["player0_extra"] = new object[] { 9001, null };
            object[] a1 = BindOutString(isComplete, nullItem);
            AssertFalse(Convert.ToBoolean(isComplete.Invoke(null, a1)),
                "null item in deck list must fail IsSettingsComplete");

            Dictionary<string, object> stringDeck = BuildFullAuthoritativeSettings(14u);
            Dictionary<string, object> decks2 = stringDeck["decks"] as Dictionary<string, object>;
            decks2["player1_side"] = "not-an-enumerable-of-ids";
            object[] a2 = BindOutString(isComplete, stringDeck);
            AssertFalse(Convert.ToBoolean(isComplete.Invoke(null, a2)),
                "string (non-list) deck value must fail IsSettingsComplete");

            Dictionary<string, object> scalarDeck = BuildFullAuthoritativeSettings(15u);
            Dictionary<string, object> decks3 = scalarDeck["decks"] as Dictionary<string, object>;
            decks3["player0_side"] = 42;
            object[] a3 = BindOutString(isComplete, scalarDeck);
            AssertFalse(Convert.ToBoolean(isComplete.Invoke(null, a3)),
                "non-enumerable deck value must fail IsSettingsComplete");
        }

        static void EngineInitSequenceRequiredExactValue(Type authority)
        {
            MethodInfo isComplete = RequirePublicStaticMethod(authority, "IsSettingsComplete");

            Dictionary<string, object> missing = BuildFullAuthoritativeSettings(16u);
            missing.Remove("engine_init_sequence");
            object[] a1 = BindOutString(isComplete, missing);
            AssertFalse(Convert.ToBoolean(isComplete.Invoke(null, a1)),
                "missing engine_init_sequence must fail IsSettingsComplete");

            Dictionary<string, object> wrong = BuildFullAuthoritativeSettings(17u);
            wrong["engine_init_sequence"] = "wrong_sequence";
            object[] a2 = BindOutString(isComplete, wrong);
            AssertFalse(Convert.ToBoolean(isComplete.Invoke(null, a2)),
                "engine_init_sequence must be exact pvp_dll_init_v1");

            Dictionary<string, object> ok = BuildFullAuthoritativeSettings(18u);
            AssertEqual("pvp_dll_init_v1", Convert.ToString(ok["engine_init_sequence"]),
                "fixture uses exact sequence");
            object[] a3 = BindOutString(isComplete, ok);
            AssertTrue(Convert.ToBoolean(isComplete.Invoke(null, a3)),
                "exact pvp_dll_init_v1 is complete");
        }

        static void ObsoleteBuildSnapshotOverloadRemoved(Type authority)
        {
            MethodInfo obsolete = authority.GetMethod(
                "BuildSnapshotFromDuelSettingsFields",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            AssertTrue(obsolete == null,
                "BuildSnapshotFromDuelSettingsFields must be removed (fabricates defaults; no callers)");
            MethodInfo engine = authority.GetMethod(
                "BuildSnapshotFromEngineInit",
                BindingFlags.Public | BindingFlags.Static);
            AssertNotNull(engine, "BuildSnapshotFromEngineInit remains the public snapshot builder");
        }

        static void FaultInjectHookIsNonPublic(Type authority)
        {
            FieldInfo pub = authority.GetField(
                "TestFaultInjectPostNativeCapture",
                BindingFlags.Public | BindingFlags.Static);
            PropertyInfo pubProp = authority.GetProperty(
                "TestFaultInjectPostNativeCapture",
                BindingFlags.Public | BindingFlags.Static);
            AssertTrue(pub == null && pubProp == null,
                "TestFaultInjectPostNativeCapture must not be public production API");

            FieldInfo internalField = authority.GetField(
                "TestFaultInjectPostNativeCapture",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertNotNull(internalField,
                "TestFaultInjectPostNativeCapture internal/non-public static field required for tests");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------
        static object BeginEnabled(Type authority, uint seed)
        {
            MethodInfo begin = RequirePublicStaticMethod(authority, "BeginFromPvpLaunch");
            Dictionary<string, object> launch = new Dictionary<string, object>()
            {
                { LaunchKeyEnabled, true },
                { LaunchKeyFlushPath, "" },
            };
            return begin.Invoke(null, new object[] { launch, BuildFullAuthoritativeSettings(seed) });
        }

        internal static Dictionary<string, object> BuildFullAuthoritativeSettings(uint seed)
        {
            return new Dictionary<string, object>()
            {
                { "seed", seed },
                { "RandSeed", seed },
                { "first_player", 0 },
                { "FirstPlayer", 0 },
                { "regulation_id", SentinelRegulationId },
                { "duel_limited_type", SentinelDuelLimitedType },
                { "my_player_num", 0 },
                { "duel_type", 0 }, // Normal
                { "tag", false },
                { "life0", 8000 },
                { "life1", 8000 },
                { "hnum0", 5 },
                { "hnum1", 5 },
                { "noshuffle", false },
                { "player0_type", 0 }, // Human
                { "player1_type", 0 },
                { "cpu0_param", 100 },
                { "cpu1_param", 100 },
                { "engine_init_sequence", "pvp_dll_init_v1" },
                {
                    "decks", new Dictionary<string, object>()
                    {
                        { "player0_main", new List<object>() { 1001, 1002, 1003 } },
                        { "player1_main", new List<object>() { 2001, 2002 } },
                        { "player0_extra", new List<object>() { 9001 } },
                        { "player1_extra", new List<object>() },
                        { "player0_side", new List<object>() },
                        { "player1_side", new List<object>() { 8001 } },
                    }
                },
                { "source", ExpectedProvenance },
                { "settings_provenance", ExpectedProvenance },
                { "settings_snapshot_kind", "server_pvp_duel_settings" },
            };
        }

        static Dictionary<string, object> RequireSettingsDict(object result)
        {
            object settings = result.GetType().GetProperty("Settings").GetValue(result, null);
            Dictionary<string, object> d = settings as Dictionary<string, object>;
            AssertNotNull(d, "Settings dict");
            return d;
        }

        static object[] BindOutString(MethodInfo method, object first)
        {
            return new object[] { first, null };
        }

        static MethodInfo RequirePublicStaticMethod(Type type, string name)
        {
            MethodInfo m = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            AssertNotNull(m, type.Name + "." + name);
            return m;
        }

        static Type TryGetType(string simpleName)
        {
            Assembly asm = typeof(Llm005Slice5PvpAuthorityRemediationTests).Assembly;
            Type direct = asm.GetType("YgoMaster." + simpleName, false);
            if (direct != null)
            {
                return direct;
            }
            foreach (Type t in asm.GetTypes())
            {
                if (t != null && t.Name == simpleName)
                {
                    return t;
                }
            }
            return null;
        }

        static int CountOccurrences(string hay, string needle)
        {
            int count = 0;
            int idx = 0;
            while ((idx = hay.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }

        static void AssertTrue(bool v, string m)
        {
            if (!v)
            {
                throw new InvalidOperationException(m);
            }
        }

        static void AssertFalse(bool v, string m)
        {
            if (v)
            {
                throw new InvalidOperationException(m);
            }
        }

        static void AssertNotNull(object v, string m)
        {
            if (v == null)
            {
                throw new InvalidOperationException(m);
            }
        }

        static void AssertEqual<T>(T e, T a, string m)
        {
            if (!Equals(e, a))
            {
                throw new InvalidOperationException(m + " (expected " + e + ", got " + a + ")");
            }
        }
    }
}

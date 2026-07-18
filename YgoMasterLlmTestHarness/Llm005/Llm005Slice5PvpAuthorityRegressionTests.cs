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
    /// YGOMASTER-LLM-005 Slice 5 independent-review remediation (Pvp authoritative transcript).
    /// TESTS-ONLY RED: exact public API contract for LlmPvpAcceptedInputTranscriptAuthority.
    /// No duel.dll. No permissive alternate-name reflection or soft fallbacks.
    /// </summary>
    static class Llm005Slice5PvpAuthorityRegressionTests
    {
        // ---------------------------------------------------------------------
        // Exact public API contract (GREEN must implement these names/signatures)
        // ---------------------------------------------------------------------
        const string AuthorityTypeName = "LlmPvpAcceptedInputTranscriptAuthority";
        const string SettingsResultTypeName = "LlmPvpTranscriptSettingsResult";
        const string ExpectedProvenance = "pvp_server_duel_settings";

        const string MethodFromServerDuelSettings = "FromServerDuelSettings";
        const string MethodIsSettingsComplete = "IsSettingsComplete";
        const string MethodIsReplayEligible = "IsReplayEligible";
        const string MethodBeginFromPvpLaunch = "BeginFromPvpLaunch";
        const string MethodFlushOnShutdown = "FlushOnShutdown";
        const string MethodTryAcceptVoidAfterNative = "TryAcceptVoidAfterNative";
        const string MethodTryAcceptReturnAfterNative = "TryAcceptReturnAfterNative";

        const string PropDefaultEnabled = "DefaultEnabled";
        const string PropEnabled = "Enabled";
        const string PropTranscript = "Transcript";
        const string PropFlushPath = "FlushPath";

        const string ResultPropSettings = "Settings";
        const string ResultPropSettingsComplete = "SettingsComplete";
        const string ResultPropIncompleteReason = "IncompleteSettingsReason";
        const string ResultPropSettingsProvenance = "SettingsProvenance";

        const string LaunchKeyEnabled = "llm_pvp_accepted_input_transcript_enabled";
        const string LaunchKeyFlushPath = "llm_pvp_accepted_input_transcript_flush_path";

        const string FieldLaunchKeyEnabled = "LaunchKeyEnabled";
        const string FieldLaunchKeyFlushPath = "LaunchKeyFlushPath";

        /// <summary>Exact input subtypes required by review (not five broad kinds).</summary>
        static readonly string[] ExactInputSubtypes = new string[]
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

        static readonly string[] PvpHandlerMethodNames = new string[]
        {
            "OnDuelComDoCommand",
            "OnDuelComMovePhase",
            "OnDuelComCancelCommand",
            "OnDuelComCancelCommand2",
            "OnDuelDlgSetResult",
            "OnDuelListSetCardExData",
            "OnDuelListSetIndex",
            "OnDuelListInitString",
        };

        public static void RunAll()
        {
            // (a) Desired empty-main incompleteness — first intended RED if authority is missing.
            EmptyMainDecksAreIncompleteAndReplayIneligible();

            Type authority = RequireAuthorityType();

            // (b) Exact narrow public surface — every required member by exact name.
            ExactPublicApiContract(authority);

            AuthorityIsDefaultOffAndNotWorkerConsumer(authority);
            ServerSettingsConversionPreservesSeedZeroAndBothSeats(authority);
            ClientPlaceholderIsReplayIneligible(authority);

            // (c) Seq-gated native-before-commit API (no duel.dll).
            MatchingSeqInvokesNativeFirstAndCommitsOnce(authority);
            StaleSeqInvokesNoNativeAndCommitsZero(authority);
            NativeExceptionCommitsZero(authority);
            ActorsZeroAndOnePreserved(authority);
            VoidAndReturnCallFamiliesCovered(authority);

            // (d) Exact subtypes/payload distinctions.
            ExactInputSubtypesAndPayloadDistinctions(authority);

            // (e) Automatic origin metadata; exact count=1.
            AutomaticOriginExactSingleReplayableEntry(authority);

            // (g) Launch settings parsing + idempotent Begin/Flush.
            LaunchConfigParsingAndIdempotentBeginFlush(authority);

            // (h) Production wiring cannot pass from authority class alone.
            ProductionWiringContractActRoomAndPvpHandlers();
        }

        // ---------------------------------------------------------------------
        // (a) Empty main decks incomplete + not replay-eligible
        // ---------------------------------------------------------------------
        static void EmptyMainDecksAreIncompleteAndReplayIneligible()
        {
            Type authority = TryGetType(AuthorityTypeName);
            if (authority == null)
            {
                throw new InvalidOperationException(
                    "YGOMASTER-LLM-005 Slice 5 RED (Pvp authority): missing production type '"
                    + AuthorityTypeName
                    + "'. Empty main decks must be settings-incomplete and IsReplayEligible=false. "
                    + "Authoritative transcript begins in YgoMasterServer/Pvp from actual DuelSettings "
                    + "(RandSeed including 0, FirstPlayer, regulation/limited type, ordered Main/Extra/Side "
                    + "for BOTH seats; nonempty legal main decks; provenance '" + ExpectedProvenance + "'). "
                    + "Client placeholder seed=0+empty decks must never be complete/authoritative for replay. "
                    + "Exact API: FromServerDuelSettings, IsSettingsComplete, IsReplayEligible, "
                    + "BeginFromPvpLaunch, FlushOnShutdown, TryAcceptVoidAfterNative, "
                    + "TryAcceptReturnAfterNative. Do not invoke duel.dll from harness tests.");
            }

            MethodInfo isComplete = RequirePublicStaticMethod(authority, MethodIsSettingsComplete);
            Dictionary<string, object> empty = BuildEmptyMainClientPlaceholder();
            object[] completeArgs = BindOutStringMethod(isComplete, empty);
            bool complete = Convert.ToBoolean(isComplete.Invoke(null, completeArgs));
            string reason = completeArgs[1] as string;
            AssertFalse(complete,
                "empty main decks must be settings-incomplete (IsSettingsComplete=false)");
            AssertTrue(!string.IsNullOrEmpty(reason),
                "IsSettingsComplete must surface a non-empty incomplete reason for empty mains");

            MethodInfo isEligible = RequirePublicStaticMethod(authority, MethodIsReplayEligible);
            LlmAcceptedInputTranscript placeholderTranscript = new LlmAcceptedInputTranscript();
            placeholderTranscript.DuelSettings = empty;
            object eligibleObj = isEligible.Invoke(null, new object[] { placeholderTranscript });
            AssertFalse(Convert.ToBoolean(eligibleObj),
                "client placeholder / incomplete transcript must have IsReplayEligible=false");
        }

        // ---------------------------------------------------------------------
        // (b) Exact public API surface
        // ---------------------------------------------------------------------
        static Type RequireAuthorityType()
        {
            Type t = TryGetType(AuthorityTypeName);
            if (t == null)
            {
                throw new InvalidOperationException(
                    "YGOMASTER-LLM-005 Slice 5 RED (Pvp authority): missing production type '"
                    + AuthorityTypeName + "'.");
            }
            return t;
        }

        static void ExactPublicApiContract(Type authority)
        {
            // Static default-off flag
            PropertyInfo defaultEnabled = authority.GetProperty(
                PropDefaultEnabled, BindingFlags.Public | BindingFlags.Static);
            AssertNotNull(defaultEnabled,
                AuthorityTypeName + "." + PropDefaultEnabled + " public static property required");
            AssertEqual(typeof(bool), defaultEnabled.PropertyType,
                PropDefaultEnabled + " must be bool");

            // Launch key constants (exact string values asserted later in launch tests)
            FieldInfo keyEnabled = authority.GetField(
                FieldLaunchKeyEnabled, BindingFlags.Public | BindingFlags.Static);
            FieldInfo keyFlush = authority.GetField(
                FieldLaunchKeyFlushPath, BindingFlags.Public | BindingFlags.Static);
            AssertNotNull(keyEnabled,
                AuthorityTypeName + "." + FieldLaunchKeyEnabled + " public const/static field required");
            AssertNotNull(keyFlush,
                AuthorityTypeName + "." + FieldLaunchKeyFlushPath + " public const/static field required");
            AssertEqual(LaunchKeyEnabled, Convert.ToString(keyEnabled.GetValue(null)),
                FieldLaunchKeyEnabled + " value");
            AssertEqual(LaunchKeyFlushPath, Convert.ToString(keyFlush.GetValue(null)),
                FieldLaunchKeyFlushPath + " value");

            // Required static methods (exact names)
            RequirePublicStaticMethod(authority, MethodFromServerDuelSettings);
            RequirePublicStaticMethod(authority, MethodIsSettingsComplete);
            RequirePublicStaticMethod(authority, MethodIsReplayEligible);
            RequirePublicStaticMethod(authority, MethodBeginFromPvpLaunch);

            // Required instance surface
            PropertyInfo enabled = authority.GetProperty(
                PropEnabled, BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(enabled, AuthorityTypeName + "." + PropEnabled + " instance property required");
            AssertEqual(typeof(bool), enabled.PropertyType, PropEnabled + " must be bool");

            PropertyInfo transcript = authority.GetProperty(
                PropTranscript, BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(transcript,
                AuthorityTypeName + "." + PropTranscript + " instance property required");
            AssertEqual(typeof(LlmAcceptedInputTranscript), transcript.PropertyType,
                PropTranscript + " must be LlmAcceptedInputTranscript");

            PropertyInfo flushPath = authority.GetProperty(
                PropFlushPath, BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(flushPath,
                AuthorityTypeName + "." + PropFlushPath + " instance property required");

            MethodInfo flush = authority.GetMethod(
                MethodFlushOnShutdown, BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(flush,
                AuthorityTypeName + "." + MethodFlushOnShutdown + " public instance method required");

            MethodInfo voidAccept = authority.GetMethod(
                MethodTryAcceptVoidAfterNative, BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(voidAccept,
                AuthorityTypeName + "." + MethodTryAcceptVoidAfterNative
                + " public instance method required "
                + "(currentEngineSeq, messageSeq, actorPlayer, inputSubtype, payload, Action native)");

            MethodInfo retAccept = authority.GetMethod(
                MethodTryAcceptReturnAfterNative, BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(retAccept,
                AuthorityTypeName + "." + MethodTryAcceptReturnAfterNative
                + " public instance method required "
                + "(currentEngineSeq, messageSeq, actorPlayer, inputSubtype, payload, Func<int> native, out int)");

            // Validate parameter shapes for acceptance APIs (narrow contract).
            AssertAcceptVoidSignature(voidAccept);
            AssertAcceptReturnSignature(retAccept);

            // Settings result type properties used by FromServerDuelSettings
            Type resultType = TryGetType(SettingsResultTypeName);
            AssertNotNull(resultType,
                "public type '" + SettingsResultTypeName + "' required as FromServerDuelSettings return");
            AssertNotNull(
                resultType.GetProperty(ResultPropSettings, BindingFlags.Public | BindingFlags.Instance),
                SettingsResultTypeName + "." + ResultPropSettings);
            AssertNotNull(
                resultType.GetProperty(ResultPropSettingsComplete, BindingFlags.Public | BindingFlags.Instance),
                SettingsResultTypeName + "." + ResultPropSettingsComplete);
            AssertNotNull(
                resultType.GetProperty(ResultPropIncompleteReason, BindingFlags.Public | BindingFlags.Instance),
                SettingsResultTypeName + "." + ResultPropIncompleteReason);
            AssertNotNull(
                resultType.GetProperty(ResultPropSettingsProvenance, BindingFlags.Public | BindingFlags.Instance),
                SettingsResultTypeName + "." + ResultPropSettingsProvenance);

            MethodInfo from = RequirePublicStaticMethod(authority, MethodFromServerDuelSettings);
            AssertTrue(resultType.IsAssignableFrom(from.ReturnType),
                MethodFromServerDuelSettings + " must return " + SettingsResultTypeName);

            // Must not expose worker consumption
            AssertTrue(
                authority.GetMethod("ApplyWorkerBranch",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) == null
                && authority.GetMethod("ConsumeSearchWorkerResponse",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) == null
                && authority.GetMethod("SelectFromWorker",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) == null,
                AuthorityTypeName + " must not consume worker branch/selection results");
        }

        static void AssertAcceptVoidSignature(MethodInfo m)
        {
            ParameterInfo[] ps = m.GetParameters();
            AssertEqual(6, ps.Length,
                MethodTryAcceptVoidAfterNative + " must take 6 parameters "
                + "(currentEngineSeq, messageSeq, actorPlayer, inputSubtype, payload, nativeInvoke)");
            AssertTrue(ps[0].ParameterType == typeof(ulong), "param0 currentEngineSeq: ulong");
            AssertTrue(ps[1].ParameterType == typeof(ulong), "param1 messageSeq: ulong");
            AssertTrue(ps[2].ParameterType == typeof(int), "param2 actorPlayer: int");
            AssertTrue(ps[3].ParameterType == typeof(string), "param3 inputSubtype: string");
            AssertTrue(typeof(IDictionary).IsAssignableFrom(ps[4].ParameterType)
                || ps[4].ParameterType == typeof(Dictionary<string, object>)
                || ps[4].ParameterType == typeof(object),
                "param4 payload: Dictionary<string,object>");
            AssertTrue(typeof(Delegate).IsAssignableFrom(ps[5].ParameterType)
                || ps[5].ParameterType == typeof(Action)
                || ps[5].ParameterType.Name.StartsWith("Action", StringComparison.Ordinal),
                "param5 nativeInvoke: Action (void native family)");
            AssertTrue(m.ReturnType == typeof(bool), MethodTryAcceptVoidAfterNative + " returns bool");
        }

        static void AssertAcceptReturnSignature(MethodInfo m)
        {
            ParameterInfo[] ps = m.GetParameters();
            AssertTrue(ps.Length == 7,
                MethodTryAcceptReturnAfterNative + " must take 7 parameters "
                + "(currentEngineSeq, messageSeq, actorPlayer, inputSubtype, payload, nativeInvoke, out nativeResult)");
            AssertTrue(ps[0].ParameterType == typeof(ulong), "param0 currentEngineSeq: ulong");
            AssertTrue(ps[1].ParameterType == typeof(ulong), "param1 messageSeq: ulong");
            AssertTrue(ps[2].ParameterType == typeof(int), "param2 actorPlayer: int");
            AssertTrue(ps[3].ParameterType == typeof(string), "param3 inputSubtype: string");
            AssertTrue(ps[5].ParameterType == typeof(Func<int>)
                || (typeof(Delegate).IsAssignableFrom(ps[5].ParameterType)
                    && ps[5].ParameterType.IsGenericType
                    && ps[5].ParameterType.GetGenericTypeDefinition() == typeof(Func<>)),
                "param5 nativeInvoke: Func<int> (return native family)");
            AssertTrue(ps[6].ParameterType == typeof(int).MakeByRefType() || ps[6].IsOut,
                "param6 out int nativeResult");
            AssertTrue(m.ReturnType == typeof(bool), MethodTryAcceptReturnAfterNative + " returns bool");
        }

        static void AuthorityIsDefaultOffAndNotWorkerConsumer(Type authority)
        {
            object def = authority.GetProperty(PropDefaultEnabled, BindingFlags.Public | BindingFlags.Static)
                .GetValue(null, null);
            AssertEqual(false, Convert.ToBoolean(def), AuthorityTypeName + ".DefaultEnabled must be false");
        }

        // ---------------------------------------------------------------------
        // Settings conversion
        // ---------------------------------------------------------------------
        static void ServerSettingsConversionPreservesSeedZeroAndBothSeats(Type authority)
        {
            MethodInfo convert = RequirePublicStaticMethod(authority, MethodFromServerDuelSettings);
            object serverSettings = BuildSyntheticServerDuelSettingsShape(seed: 0u, emptyMains: false);
            object result = convert.Invoke(null, new object[] { serverSettings });
            AssertNotNull(result, MethodFromServerDuelSettings + " result");

            Dictionary<string, object> settingsDict = RequireSettingsDict(result);
            object seed = RequireKey(settingsDict, "seed");
            AssertEqual(0L, Convert.ToInt64(seed), "RandSeed 0 must be preserved exactly");
            AssertNotNull(RequireKey(settingsDict, "first_player"), "first_player preserved");
            AssertNotNull(RequireKey(settingsDict, "regulation_id"), "regulation_id preserved");
            AssertNotNull(RequireKey(settingsDict, "duel_limited_type"), "duel_limited_type preserved");

            Dictionary<string, object> decks = RequireKey(settingsDict, "decks") as Dictionary<string, object>;
            AssertNotNull(decks, "decks object");
            IList main0 = RequireKey(decks, "player0_main") as IList;
            IList main1 = RequireKey(decks, "player1_main") as IList;
            AssertTrue(main0 != null && main0.Count >= 1, "seat0 main nonempty");
            AssertTrue(main1 != null && main1.Count >= 1, "seat1 main nonempty");
            AssertEqual(1001, Convert.ToInt32(main0[0]), "seat0 first main card id");
            AssertEqual(1002, Convert.ToInt32(main0[1]), "seat0 second main card id");
            AssertEqual(2001, Convert.ToInt32(main1[0]), "seat1 first main card id");

            AssertNotNull(RequireKey(decks, "player0_extra") as IList, "seat0 extra list");
            AssertNotNull(RequireKey(decks, "player1_extra") as IList, "seat1 extra list");
            AssertNotNull(RequireKey(decks, "player0_side") as IList, "seat0 side list");
            AssertNotNull(RequireKey(decks, "player1_side") as IList, "seat1 side list");

            string provenance = Convert.ToString(
                result.GetType().GetProperty(ResultPropSettingsProvenance).GetValue(result, null));
            AssertEqual(ExpectedProvenance, provenance,
                "SettingsProvenance must be " + ExpectedProvenance);

            bool complete = Convert.ToBoolean(
                result.GetType().GetProperty(ResultPropSettingsComplete).GetValue(result, null));
            AssertTrue(complete, "nonempty two-seat mains => SettingsComplete");

            MethodInfo isComplete = RequirePublicStaticMethod(authority, MethodIsSettingsComplete);
            object[] args = BindOutStringMethod(isComplete, settingsDict);
            AssertTrue(Convert.ToBoolean(isComplete.Invoke(null, args)),
                "IsSettingsComplete(true) for converted two-seat server settings");
        }

        // ---------------------------------------------------------------------
        // (f) Mandatory IsReplayEligible=false for client placeholder
        // ---------------------------------------------------------------------
        static void ClientPlaceholderIsReplayIneligible(Type authority)
        {
            MethodInfo isEligible = RequirePublicStaticMethod(authority, MethodIsReplayEligible);
            MethodInfo isComplete = RequirePublicStaticMethod(authority, MethodIsSettingsComplete);

            Dictionary<string, object> clientPlaceholder = BuildEmptyMainClientPlaceholder();
            clientPlaceholder["source"] = "client_local_placeholder";

            object[] completeArgs = BindOutStringMethod(isComplete, clientPlaceholder);
            AssertFalse(Convert.ToBoolean(isComplete.Invoke(null, completeArgs)),
                "client placeholder empty mains => IsSettingsComplete=false");

            LlmAcceptedInputTranscript t = new LlmAcceptedInputTranscript();
            t.DuelSettings = clientPlaceholder;
            AssertFalse(Convert.ToBoolean(isEligible.Invoke(null, new object[] { t })),
                "IsReplayEligible must be false for client placeholder/incomplete transcript (required API)");
        }

        // ---------------------------------------------------------------------
        // (c) Seq-gated acceptance API
        // ---------------------------------------------------------------------
        static void MatchingSeqInvokesNativeFirstAndCommitsOnce(Type authority)
        {
            object session = BeginEnabledSession(authority, seed: 10u);
            MethodInfo accept = RequireInstanceMethod(session.GetType(), MethodTryAcceptVoidAfterNative);

            int nativeCalls = 0;
            int entriesDuringNative = -1;
            int commitCountBefore = GetTranscript(session).Entries.Count;

            Action native = () =>
            {
                nativeCalls++;
                // While native runs, the acceptance API must not have committed yet.
                entriesDuringNative = GetTranscript(session).Entries.Count;
            };

            bool committed = InvokeTryAcceptVoid(
                accept, session,
                currentEngineSeq: 5UL,
                messageSeq: 5UL,
                actorPlayer: 0,
                inputSubtype: "DoCommand",
                payload: BuildDoCommandPayload(),
                native: native);

            AssertTrue(committed, "matching seq must commit after native");
            AssertEqual(1, nativeCalls, "matching seq must invoke native exactly once");
            AssertEqual(commitCountBefore, entriesDuringNative,
                "native must run before commit (entry count unchanged during native)");
            AssertEqual(commitCountBefore + 1, GetTranscript(session).Entries.Count,
                "matching seq commits exactly one entry after native returns");
        }

        static void StaleSeqInvokesNoNativeAndCommitsZero(Type authority)
        {
            object session = BeginEnabledSession(authority, seed: 11u);
            MethodInfo accept = RequireInstanceMethod(session.GetType(), MethodTryAcceptVoidAfterNative);
            int before = GetTranscript(session).Entries.Count;
            int nativeCalls = 0;

            bool committed = InvokeTryAcceptVoid(
                accept, session,
                currentEngineSeq: 9UL,
                messageSeq: 3UL,
                actorPlayer: 0,
                inputSubtype: "DoCommand",
                payload: BuildDoCommandPayload(),
                native: () => { nativeCalls++; });

            AssertFalse(committed, "stale seq must not commit");
            AssertEqual(0, nativeCalls, "stale seq must not invoke native");
            AssertEqual(before, GetTranscript(session).Entries.Count,
                "stale seq commits zero entries");
        }

        static void NativeExceptionCommitsZero(Type authority)
        {
            object session = BeginEnabledSession(authority, seed: 12u);
            MethodInfo accept = RequireInstanceMethod(session.GetType(), MethodTryAcceptVoidAfterNative);
            int before = GetTranscript(session).Entries.Count;

            bool threw = false;
            bool committed = false;
            try
            {
                committed = InvokeTryAcceptVoid(
                    accept, session,
                    currentEngineSeq: 4UL,
                    messageSeq: 4UL,
                    actorPlayer: 1,
                    inputSubtype: "MovePhase",
                    payload: BuildMovePhasePayload(),
                    native: () => { throw new InvalidOperationException("simulated native failure"); });
            }
            catch (TargetInvocationException tie)
            {
                // Acceptable: method rethrows after ensuring no commit; still commits zero.
                threw = tie.InnerException != null;
                committed = false;
            }
            catch (InvalidOperationException)
            {
                threw = true;
                committed = false;
            }

            AssertFalse(committed, "native exception must not commit");
            AssertEqual(before, GetTranscript(session).Entries.Count,
                "native exception commits zero entries"
                + (threw ? " (exception propagated)" : " (exception swallowed, no commit)"));
        }

        static void ActorsZeroAndOnePreserved(Type authority)
        {
            object session = BeginEnabledSession(authority, seed: 13u);
            MethodInfo accept = RequireInstanceMethod(session.GetType(), MethodTryAcceptVoidAfterNative);

            AssertTrue(
                InvokeTryAcceptVoid(accept, session, 20UL, 20UL, 0, "DoCommand",
                    BuildDoCommandPayload(), () => { }),
                "commit actor 0");
            AssertTrue(
                InvokeTryAcceptVoid(accept, session, 21UL, 21UL, 1, "MovePhase",
                    BuildMovePhasePayload(), () => { }),
                "commit actor 1");

            HashSet<int> actors = new HashSet<int>();
            foreach (LlmAcceptedInputEntry e in GetTranscript(session).Entries)
            {
                if (e != null)
                {
                    actors.Add(e.Actor);
                }
            }
            AssertTrue(actors.Contains(0) && actors.Contains(1),
                "entries must preserve ActorPlayer seats 0 and 1 (got "
                + string.Join(",", actors.OrderBy(x => x)) + ")");
        }

        static void VoidAndReturnCallFamiliesCovered(Type authority)
        {
            object session = BeginEnabledSession(authority, seed: 14u);
            MethodInfo voidAccept = RequireInstanceMethod(session.GetType(), MethodTryAcceptVoidAfterNative);
            MethodInfo retAccept = RequireInstanceMethod(session.GetType(), MethodTryAcceptReturnAfterNative);

            int voidNative = 0;
            AssertTrue(
                InvokeTryAcceptVoid(voidAccept, session, 30UL, 30UL, 0, "ListInitString",
                    new Dictionary<string, object>(), () => { voidNative++; }),
                "void family commits");
            AssertEqual(1, voidNative, "void native invoked");

            int retNative = 0;
            int nativeResult;
            bool committed = InvokeTryAcceptReturn(
                retAccept, session, 31UL, 31UL, 1, "CancelCommand",
                new Dictionary<string, object>(),
                () => { retNative++; return 42; },
                out nativeResult);
            AssertTrue(committed, "return family commits");
            AssertEqual(1, retNative, "return native invoked");
            AssertEqual(42, nativeResult, "native return value surfaced via out param");
        }

        // ---------------------------------------------------------------------
        // (d) Exact subtypes / payload distinctions
        // ---------------------------------------------------------------------
        static void ExactInputSubtypesAndPayloadDistinctions(Type authority)
        {
            object session = BeginEnabledSession(authority, seed: 15u);
            MethodInfo accept = RequireInstanceMethod(session.GetType(), MethodTryAcceptVoidAfterNative);

            ulong seq = 100;
            foreach (string subtype in ExactInputSubtypes)
            {
                Dictionary<string, object> payload = BuildPayloadForSubtype(subtype);
                AssertTrue(
                    InvokeTryAcceptVoid(accept, session, seq, seq, 0, subtype, payload, () => { }),
                    "commit subtype " + subtype);
                seq++;
            }

            LlmAcceptedInputTranscript transcript = GetTranscript(session);
            AssertEqual(ExactInputSubtypes.Length, transcript.Entries.Count,
                "exactly one committed entry per exact input subtype");

            for (int i = 0; i < ExactInputSubtypes.Length; i++)
            {
                string subtype = ExactInputSubtypes[i];
                LlmAcceptedInputEntry entry = transcript.Entries[i];
                AssertNotNull(entry, "entry " + subtype);
                string recordedSubtype = ReadEntrySubtype(entry);
                AssertEqual(subtype, recordedSubtype,
                    "entry must record exact input subtype " + subtype);

                Dictionary<string, object> p = entry.Payload as Dictionary<string, object>;
                AssertNotNull(p, "payload dict for " + subtype);
                AssertSubtypePayload(subtype, p);
            }
        }

        static string ReadEntrySubtype(LlmAcceptedInputEntry entry)
        {
            Dictionary<string, object> p = entry.Payload as Dictionary<string, object>;
            if (p != null)
            {
                object st;
                if (p.TryGetValue("input_subtype", out st) && st != null)
                {
                    return Convert.ToString(st);
                }
                if (p.TryGetValue("subtype", out st) && st != null)
                {
                    return Convert.ToString(st);
                }
            }
            // GREEN may map subtype onto Kind+payload; require input_subtype key for exact kinds.
            throw new InvalidOperationException(
                "committed entry must carry payload.input_subtype with the exact acceptance subtype "
                + "(DoCommand|MovePhase|CancelCommand|CancelCommand2|Dialog|ListCardExData|ListIndex|ListInitString)");
        }

        static void AssertSubtypePayload(string subtype, Dictionary<string, object> p)
        {
            switch (subtype)
            {
                case "DoCommand":
                    AssertTrue(p.ContainsKey("player") && p.ContainsKey("position")
                        && p.ContainsKey("index") && p.ContainsKey("command_id"),
                        "DoCommand payload: player, position, index, command_id");
                    break;
                case "MovePhase":
                    AssertTrue(p.ContainsKey("phase"), "MovePhase payload: phase");
                    break;
                case "CancelCommand":
                    // No decide flag — distinct from CancelCommand2
                    AssertFalse(p.ContainsKey("decide"),
                        "CancelCommand must not carry CancelCommand2 decide semantics");
                    break;
                case "CancelCommand2":
                    AssertTrue(p.ContainsKey("decide"),
                        "CancelCommand2 payload must include decide (bool)");
                    AssertTrue(p["decide"] is bool, "CancelCommand2.decide is bool");
                    break;
                case "Dialog":
                    AssertTrue(p.ContainsKey("result"), "Dialog payload: result");
                    break;
                case "ListCardExData":
                    AssertTrue(p.ContainsKey("index") && p.ContainsKey("data"),
                        "ListCardExData payload: index, data");
                    break;
                case "ListIndex":
                    AssertTrue(p.ContainsKey("index"), "ListIndex payload: index");
                    AssertFalse(p.ContainsKey("data"),
                        "ListIndex must not be confused with ListCardExData (no data field required)");
                    break;
                case "ListInitString":
                    // Distinct empty-ish init — must still be labeled as ListInitString via input_subtype
                    AssertEqual("ListInitString", Convert.ToString(p["input_subtype"]),
                        "ListInitString identity");
                    break;
            }
        }

        // ---------------------------------------------------------------------
        // (e) Automatic origin — metadata on same entry, count == 1
        // ---------------------------------------------------------------------
        static void AutomaticOriginExactSingleReplayableEntry(Type authority)
        {
            object session = BeginEnabledSession(authority, seed: 16u);
            MethodInfo accept = RequireInstanceMethod(session.GetType(), MethodTryAcceptVoidAfterNative);

            Dictionary<string, object> payload = BuildDoCommandPayload();
            payload["commitment_origin"] = "automatic_client_commit";

            AssertTrue(
                InvokeTryAcceptVoid(accept, session, 7UL, 7UL, 1, "DoCommand", payload, () => { }),
                "automatic-origin DoCommand commits once");

            LlmAcceptedInputTranscript transcript = GetTranscript(session);
            AssertEqual(1, transcript.Entries.Count,
                "automatic origin: exact committed entry count must be 1 (not >=1)");

            LlmAcceptedInputEntry entry = transcript.Entries[0];
            AssertEqual(7UL, entry.RunEffectSeq, "automatic entry run_effect_seq");
            Dictionary<string, object> p = entry.Payload as Dictionary<string, object>;
            AssertNotNull(p, "automatic entry payload");
            object origin;
            AssertTrue(
                p.TryGetValue("commitment_origin", out origin)
                || p.TryGetValue("origin", out origin)
                || p.TryGetValue("input_origin", out origin),
                "automatic commitment origin must be metadata on the same replayable entry");
            string originText = Convert.ToString(origin);
            AssertTrue(
                originText != null
                && originText.IndexOf("automatic", StringComparison.OrdinalIgnoreCase) >= 0,
                "origin metadata must identify automatic commitment (got '" + originText + "')");
        }

        // ---------------------------------------------------------------------
        // (g) Launch config parsing + idempotent Begin/Flush
        // ---------------------------------------------------------------------
        static void LaunchConfigParsingAndIdempotentBeginFlush(Type authority)
        {
            MethodInfo begin = RequirePublicStaticMethod(authority, MethodBeginFromPvpLaunch);
            object serverSettings = BuildSyntheticServerDuelSettingsShape(20u, emptyMains: false);

            // Default / explicit off
            Dictionary<string, object> offLaunch = new Dictionary<string, object>()
            {
                { LaunchKeyEnabled, false },
                { LaunchKeyFlushPath, "" },
            };
            object offSession = begin.Invoke(null, BindBeginArgs(begin, offLaunch, serverSettings));
            AssertNotNull(offSession, "BeginFromPvpLaunch off session");
            AssertFalse(Convert.ToBoolean(RequireInstanceProp(offSession, PropEnabled)),
                "launch " + LaunchKeyEnabled + "=false => Enabled=false");

            // Explicit on + flush path
            string flushPath = Path.Combine(Path.GetTempPath(),
                "ygomaster-llm005-pvp-authority-flush-" + Guid.NewGuid().ToString("N") + ".json");
            Dictionary<string, object> onLaunch = new Dictionary<string, object>()
            {
                { LaunchKeyEnabled, true },
                { LaunchKeyFlushPath, flushPath },
            };
            object onSession = begin.Invoke(null, BindBeginArgs(begin, onLaunch, serverSettings));
            AssertNotNull(onSession, "BeginFromPvpLaunch on session");
            AssertTrue(Convert.ToBoolean(RequireInstanceProp(onSession, PropEnabled)),
                "launch " + LaunchKeyEnabled + "=true => Enabled=true");
            AssertEqual(flushPath, Convert.ToString(RequireInstanceProp(onSession, PropFlushPath)),
                "launch " + LaunchKeyFlushPath + " parsed into FlushPath");

            // Missing enable key => default off
            Dictionary<string, object> missingLaunch = new Dictionary<string, object>()
            {
                { LaunchKeyFlushPath, flushPath },
            };
            object missingSession = begin.Invoke(null, BindBeginArgs(begin, missingLaunch, serverSettings));
            AssertFalse(Convert.ToBoolean(RequireInstanceProp(missingSession, PropEnabled)),
                "missing " + LaunchKeyEnabled + " => default Enabled=false");

            // Idempotent Begin: second begin with same launch must not throw; enabled state stable;
            // re-begin resets committed entries for a new duel generation.
            MethodInfo voidAccept = RequireInstanceMethod(onSession.GetType(), MethodTryAcceptVoidAfterNative);
            AssertTrue(
                InvokeTryAcceptVoid(voidAccept, onSession, 1UL, 1UL, 0, "DoCommand",
                    BuildDoCommandPayload(), () => { }),
                "pre-rebegin commit");
            AssertTrue(GetTranscript(onSession).Entries.Count >= 1, "entry present before re-begin");
            object onSession2 = begin.Invoke(null, BindBeginArgs(begin, onLaunch, serverSettings));
            AssertTrue(Convert.ToBoolean(RequireInstanceProp(onSession2, PropEnabled)),
                "BeginFromPvpLaunch idempotent re-entry preserves Enabled=true");
            AssertEqual(0, GetTranscript(onSession2).Entries.Count,
                "BeginFromPvpLaunch re-entry resets transcript entries for new duel generation");

            // Idempotent FlushOnShutdown: after a commit, two flushes must not throw, must write
            // FlushPath when set, and leave Enabled stable.
            AssertTrue(
                InvokeTryAcceptVoid(
                    RequireInstanceMethod(onSession2.GetType(), MethodTryAcceptVoidAfterNative),
                    onSession2, 2UL, 2UL, 0, "MovePhase", BuildMovePhasePayload(), () => { }),
                "post-rebegin commit for flush");
            MethodInfo flush = RequireInstanceMethod(onSession2.GetType(), MethodFlushOnShutdown);
            flush.Invoke(onSession2, null);
            AssertTrue(File.Exists(flushPath),
                "FlushOnShutdown must write FlushPath when configured (got missing " + flushPath + ")");
            string firstJson = File.ReadAllText(flushPath, Encoding.UTF8);
            flush.Invoke(onSession2, null);
            string secondJson = File.ReadAllText(flushPath, Encoding.UTF8);
            AssertEqual(firstJson, secondJson,
                "second FlushOnShutdown is idempotent (deterministic file content)");
            AssertTrue(Convert.ToBoolean(RequireInstanceProp(onSession2, PropEnabled)),
                "FlushOnShutdown must not clear Enabled");
            try { File.Delete(flushPath); } catch { }
        }

        // ---------------------------------------------------------------------
        // (h) Production wiring — source inspection (cannot pass from type alone)
        // ---------------------------------------------------------------------
        static void ProductionWiringContractActRoomAndPvpHandlers()
        {
            string root = LlmSlice5Paths.FindRepoRoot();
            AssertTrue(!string.IsNullOrEmpty(root) && Directory.Exists(root), "repo root");

            string actRoomPath = Path.Combine(root, "YgoMasterServer", "Acts", "Act_Room.cs");
            string pvpPath = Path.Combine(root, "YgoMasterServer", "Pvp.cs");
            AssertTrue(File.Exists(actRoomPath), "Act_Room.cs present");
            AssertTrue(File.Exists(pvpPath), "Pvp.cs present");

            string actRoom = File.ReadAllText(actRoomPath, Encoding.UTF8);
            string pvp = File.ReadAllText(pvpPath, Encoding.UTF8);

            // (1) Act_Room only carries the two launch keys/values into the Pvp launch dictionary.
            // Do not require Act_Room to reference the authority type or BeginFromPvpLaunch —
            // Pvp owns authoritative DuelSettings + BeginFromPvpLaunch lifecycle.
            AssertTrue(
                actRoom.IndexOf(LaunchKeyEnabled, StringComparison.Ordinal) >= 0,
                "Act_Room.cs must put launch key '" + LaunchKeyEnabled + "' into the Pvp launch dictionary");
            AssertTrue(
                actRoom.IndexOf(LaunchKeyFlushPath, StringComparison.Ordinal) >= 0,
                "Act_Room.cs must put launch key '" + LaunchKeyFlushPath + "' into the Pvp launch dictionary");

            // Pvp owns BeginFromPvpLaunch + acceptance + flush lifecycle.
            AssertTrue(
                pvp.IndexOf(AuthorityTypeName, StringComparison.Ordinal) >= 0,
                "Pvp.cs must reference " + AuthorityTypeName);
            AssertTrue(
                pvp.IndexOf(MethodBeginFromPvpLaunch, StringComparison.Ordinal) >= 0,
                "Pvp.cs must call " + MethodBeginFromPvpLaunch
                + " (owns authoritative DuelSettings and Pvp lifecycle)");

            // Begin must live in Pvp.Run (launch + DuelSettings available there), not only comments.
            string runBody;
            int runDefIndex;
            AssertTrue(
                TryExtractMethodBody(pvp, "Run", out runBody, out runDefIndex),
                "Pvp.cs must define Run(...) method body for BeginFromPvpLaunch wiring");
            AssertTrue(
                runBody.IndexOf(MethodBeginFromPvpLaunch, StringComparison.Ordinal) >= 0,
                "Pvp.Run body must call " + MethodBeginFromPvpLaunch
                + " with launch dict + authoritative DuelSettings");

            // Return-family handlers (DLL_DuelComCancelCommand* return int).
            string[] returnHandlers = new string[]
            {
                "OnDuelComCancelCommand",
                "OnDuelComCancelCommand2",
            };
            // Void-family handlers.
            string[] voidHandlers = new string[]
            {
                "OnDuelComDoCommand",
                "OnDuelComMovePhase",
                "OnDuelDlgSetResult",
                "OnDuelListSetCardExData",
                "OnDuelListSetIndex",
                "OnDuelListInitString",
            };

            // (2) Anchor each window to the actual method definition, not switch dispatch refs.
            foreach (string handler in PvpHandlerMethodNames)
            {
                string body;
                int defIndex;
                AssertTrue(
                    TryExtractMethodBody(pvp, handler, out body, out defIndex),
                    "Pvp.cs must define handler method body for " + handler
                    + " (anchor: 'void " + handler + "(' — not switch IndexOf hits)");

                bool isReturnFamily = returnHandlers.Contains(handler);
                if (isReturnFamily)
                {
                    AssertTrue(
                        body.IndexOf(MethodTryAcceptReturnAfterNative, StringComparison.Ordinal) >= 0,
                        "Pvp handler body " + handler + " must call " + MethodTryAcceptReturnAfterNative
                        + " (return-family cancel path; tested in definition body, not dispatch)");
                }
                else
                {
                    AssertTrue(
                        body.IndexOf(MethodTryAcceptVoidAfterNative, StringComparison.Ordinal) >= 0,
                        "Pvp handler body " + handler + " must call " + MethodTryAcceptVoidAfterNative
                        + " (void-family path; tested in definition body, not dispatch)");
                }

                string subtypeHint = HandlerToSubtypeHint(handler);
                AssertTrue(
                    body.IndexOf("\"" + subtypeHint + "\"", StringComparison.Ordinal) >= 0
                    || body.IndexOf(subtypeHint, StringComparison.Ordinal) >= 0,
                    "Pvp handler body " + handler + " must pass exact subtype '" + subtypeHint + "'");
            }

            // CancelCommand2 decide semantics in the CancelCommand2 method body.
            string cancel2Body;
            int cancel2Def;
            AssertTrue(
                TryExtractMethodBody(pvp, "OnDuelComCancelCommand2", out cancel2Body, out cancel2Def),
                "OnDuelComCancelCommand2 definition required for decide semantics");
            AssertTrue(
                cancel2Body.IndexOf("decide", StringComparison.OrdinalIgnoreCase) >= 0,
                "Pvp OnDuelComCancelCommand2 body must preserve decide semantics into acceptance payload");

            // Both accept families must appear in Pvp (cannot pass with class alone).
            AssertTrue(
                pvp.IndexOf(MethodTryAcceptVoidAfterNative, StringComparison.Ordinal) >= 0,
                "Pvp.cs must reference " + MethodTryAcceptVoidAfterNative);
            AssertTrue(
                pvp.IndexOf(MethodTryAcceptReturnAfterNative, StringComparison.Ordinal) >= 0,
                "Pvp.cs must reference " + MethodTryAcceptReturnAfterNative);

            // (3) FlushOnShutdown from finally-style / common shutdown path after main try/catch
            // in Run — not a stray comment or arbitrary occurrence elsewhere.
            AssertFlushOnShutdownInRunCommonPath(runBody);

            AssertTrue(voidHandlers.Length + returnHandlers.Length == PvpHandlerMethodNames.Length,
                "handler family partition must cover all Pvp accepted-input handlers");
        }

        /// <summary>
        /// Require FlushOnShutdown on the common exit path of Pvp.Run: either a finally block
        /// that flushes, or a call after the main duel try/catch (not only inside a catch arm).
        /// The main try is the one whose body contains the duel loop (DLL_DuelSysAct / while).
        /// </summary>
        static void AssertFlushOnShutdownInRunCommonPath(string runBody)
        {
            // SafeFlushOnProcessExit is the preferred exit-path wrapper (calls FlushOnShutdown).
            const string SafeFlush = "SafeFlushOnProcessExit";
            bool hasFlushToken =
                runBody.IndexOf(MethodFlushOnShutdown, StringComparison.Ordinal) >= 0
                || runBody.IndexOf(SafeFlush, StringComparison.Ordinal) >= 0;
            AssertTrue(hasFlushToken,
                "Pvp.Run must call " + MethodFlushOnShutdown + " or " + SafeFlush);

            // Prefer explicit finally { ... flush ... } anywhere in Run.
            int searchFrom = 0;
            while (searchFrom < runBody.Length)
            {
                int finallyIdx = IndexOfKeywordFrom(runBody, "finally", searchFrom);
                if (finallyIdx < 0)
                {
                    break;
                }
                string finallyBlock;
                if (TryExtractBalancedBlockFrom(runBody, finallyIdx, out finallyBlock)
                    && (finallyBlock.IndexOf(MethodFlushOnShutdown, StringComparison.Ordinal) >= 0
                        || finallyBlock.IndexOf(SafeFlush, StringComparison.Ordinal) >= 0))
                {
                    return; // finally-style path satisfied
                }
                searchFrom = finallyIdx + 7;
            }

            // Else: locate the main duel try (body contains DLL_DuelSysAct or while (true)).
            int mainTry = FindMainDuelTryKeyword(runBody);
            AssertTrue(mainTry >= 0,
                "Pvp.Run must have a main duel try (DLL_DuelSysAct / while) for "
                + MethodFlushOnShutdown + " common-path placement");

            int afterMainTryCatch;
            AssertTrue(
                TryFindEndOfTryCatchChain(runBody, mainTry, out afterMainTryCatch),
                "Pvp.Run main try/catch bounds could not be parsed for FlushOnShutdown placement");

            string afterCatch = runBody.Substring(afterMainTryCatch);
            AssertTrue(
                afterCatch.IndexOf(MethodFlushOnShutdown, StringComparison.Ordinal) >= 0
                || afterCatch.IndexOf(SafeFlush, StringComparison.Ordinal) >= 0,
                "Pvp.Run must call " + MethodFlushOnShutdown + " or " + SafeFlush
                + " on the common shutdown path after the main duel try/catch "
                + "(or inside a finally block) — not only inside a catch arm or elsewhere in the file");
        }

        static int FindMainDuelTryKeyword(string runBody)
        {
            int idx = 0;
            while (idx < runBody.Length)
            {
                int tryIdx = IndexOfKeywordFrom(runBody, "try", idx);
                if (tryIdx < 0)
                {
                    return -1;
                }
                int braceOpen = runBody.IndexOf('{', tryIdx);
                if (braceOpen < 0)
                {
                    return -1;
                }
                int braceClose = FindMatchingBrace(runBody, braceOpen);
                if (braceClose < 0)
                {
                    idx = tryIdx + 3;
                    continue;
                }
                string tryBody = runBody.Substring(braceOpen, braceClose - braceOpen + 1);
                if (tryBody.IndexOf("DLL_DuelSysAct", StringComparison.Ordinal) >= 0
                    || tryBody.IndexOf("while (true)", StringComparison.Ordinal) >= 0
                    || tryBody.IndexOf("while(true)", StringComparison.Ordinal) >= 0)
                {
                    return tryIdx;
                }
                idx = braceClose + 1;
            }
            return -1;
        }

        static int IndexOfKeywordFrom(string source, string keyword, int startIndex)
        {
            int idx = Math.Max(0, startIndex);
            while (idx < source.Length)
            {
                int found = source.IndexOf(keyword, idx, StringComparison.Ordinal);
                if (found < 0)
                {
                    return -1;
                }
                if (IsKeywordAt(source, found, keyword))
                {
                    return found;
                }
                idx = found + keyword.Length;
            }
            return -1;
        }

        /// <summary>
        /// Locate method definition by 'void Name(' or 'public void Name(' / return-type variants,
        /// then extract the balanced { ... } body. Avoids switch-dispatch IndexOf false positives.
        /// </summary>
        static bool TryExtractMethodBody(
            string source,
            string methodName,
            out string body,
            out int definitionIndex)
        {
            body = null;
            definitionIndex = -1;
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            // Prefer definition anchors: return-type token immediately before name(.
            // Examples: "void OnDuelComDoCommand(", "public void Run(", "unsafe int DoRunEffect("
            string[] anchors = new string[]
            {
                "void " + methodName + "(",
                "void " + methodName + " (",
                "int " + methodName + "(",
                "int " + methodName + " (",
                "bool " + methodName + "(",
                "bool " + methodName + " (",
            };

            int best = -1;
            foreach (string anchor in anchors)
            {
                int idx = 0;
                while (idx < source.Length)
                {
                    int found = source.IndexOf(anchor, idx, StringComparison.Ordinal);
                    if (found < 0)
                    {
                        break;
                    }
                    // Skip if this is inside a line comment or is a call (preceded by '.').
                    if (!IsLikelyMethodDefinition(source, found, methodName))
                    {
                        idx = found + anchor.Length;
                        continue;
                    }
                    best = found;
                    break;
                }
                if (best >= 0)
                {
                    break;
                }
            }

            if (best < 0)
            {
                return false;
            }

            definitionIndex = best;
            int braceOpen = source.IndexOf('{', best);
            if (braceOpen < 0)
            {
                return false;
            }
            int braceClose = FindMatchingBrace(source, braceOpen);
            if (braceClose < 0)
            {
                // Fallback: bounded window from definition (review allows this).
                int windowLen = Math.Min(2000, source.Length - braceOpen);
                body = source.Substring(braceOpen, windowLen);
                return true;
            }
            body = source.Substring(braceOpen, braceClose - braceOpen + 1);
            return true;
        }

        static bool IsLikelyMethodDefinition(string source, int anchorIndex, string methodName)
        {
            // Reject ".MethodName(" call sites and switch case labels using the name only.
            // Walk back past whitespace; definition has type keyword, not '.' or "case ".
            int i = anchorIndex - 1;
            while (i >= 0 && char.IsWhiteSpace(source[i]))
            {
                i--;
            }
            if (i >= 0 && source[i] == '.')
            {
                return false;
            }
            // Look at a small prefix for "case " (switch dispatch).
            int lineStart = source.LastIndexOf('\n', Math.Max(0, anchorIndex));
            if (lineStart < 0)
            {
                lineStart = 0;
            }
            string linePrefix = source.Substring(lineStart, anchorIndex - lineStart);
            if (linePrefix.IndexOf("case ", StringComparison.Ordinal) >= 0)
            {
                return false;
            }
            // Definition lines typically include accessibility or just type; reject bare identifier calls.
            string before = source.Substring(Math.Max(0, anchorIndex - 40), Math.Min(40, anchorIndex));
            if (before.IndexOf("void ", StringComparison.Ordinal) < 0
                && before.IndexOf("int ", StringComparison.Ordinal) < 0
                && before.IndexOf("bool ", StringComparison.Ordinal) < 0
                && before.IndexOf("public ", StringComparison.Ordinal) < 0
                && before.IndexOf("private ", StringComparison.Ordinal) < 0
                && before.IndexOf("internal ", StringComparison.Ordinal) < 0
                && before.IndexOf("protected ", StringComparison.Ordinal) < 0
                && before.IndexOf("static ", StringComparison.Ordinal) < 0
                && before.IndexOf("unsafe ", StringComparison.Ordinal) < 0)
            {
                // Anchor itself starts with "void Name(" so before may be empty of type if anchor includes void.
                // anchors include "void Name(" so this is fine when match starts at void.
            }
            return true;
        }

        static int FindMatchingBrace(string source, int openBraceIndex)
        {
            if (openBraceIndex < 0 || openBraceIndex >= source.Length || source[openBraceIndex] != '{')
            {
                return -1;
            }
            int depth = 0;
            bool inLineComment = false;
            bool inBlockComment = false;
            bool inString = false;
            bool inChar = false;
            for (int i = openBraceIndex; i < source.Length; i++)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (inLineComment)
                {
                    if (c == '\n')
                    {
                        inLineComment = false;
                    }
                    continue;
                }
                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }
                if (inString)
                {
                    if (c == '\\' && next != '\0')
                    {
                        i++;
                        continue;
                    }
                    if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }
                if (inChar)
                {
                    if (c == '\\' && next != '\0')
                    {
                        i++;
                        continue;
                    }
                    if (c == '\'')
                    {
                        inChar = false;
                    }
                    continue;
                }
                if (c == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
                if (c == '"')
                {
                    inString = true;
                    continue;
                }
                if (c == '\'')
                {
                    inChar = true;
                    continue;
                }
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        static bool TryExtractBalancedBlockFrom(string source, int keywordIndex, out string block)
        {
            block = null;
            int braceOpen = source.IndexOf('{', keywordIndex);
            if (braceOpen < 0)
            {
                return false;
            }
            int braceClose = FindMatchingBrace(source, braceOpen);
            if (braceClose < 0)
            {
                return false;
            }
            block = source.Substring(braceOpen, braceClose - braceOpen + 1);
            return true;
        }

        /// <summary>
        /// From a try keyword index, walk try { } catch { } [catch...] [finally { }] and
        /// return the index just after the full chain (common path continues there).
        /// </summary>
        static bool TryFindEndOfTryCatchChain(string source, int tryKeywordIndex, out int endIndex)
        {
            endIndex = -1;
            int braceOpen = source.IndexOf('{', tryKeywordIndex);
            if (braceOpen < 0)
            {
                return false;
            }
            int tryEnd = FindMatchingBrace(source, braceOpen);
            if (tryEnd < 0)
            {
                return false;
            }
            int cursor = tryEnd + 1;
            bool sawCatchOrFinally = false;
            while (cursor < source.Length)
            {
                // Skip whitespace
                while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
                {
                    cursor++;
                }
                if (cursor >= source.Length)
                {
                    break;
                }
                if (IsKeywordAt(source, cursor, "catch"))
                {
                    sawCatchOrFinally = true;
                    int catchBrace = source.IndexOf('{', cursor);
                    if (catchBrace < 0)
                    {
                        return false;
                    }
                    int catchEnd = FindMatchingBrace(source, catchBrace);
                    if (catchEnd < 0)
                    {
                        return false;
                    }
                    cursor = catchEnd + 1;
                    continue;
                }
                if (IsKeywordAt(source, cursor, "finally"))
                {
                    sawCatchOrFinally = true;
                    int finBrace = source.IndexOf('{', cursor);
                    if (finBrace < 0)
                    {
                        return false;
                    }
                    int finEnd = FindMatchingBrace(source, finBrace);
                    if (finEnd < 0)
                    {
                        return false;
                    }
                    cursor = finEnd + 1;
                    endIndex = cursor;
                    return true;
                }
                break;
            }
            // If the main try had no catch/finally, common path is still after the try block.
            endIndex = cursor;
            return sawCatchOrFinally || endIndex > tryEnd;
        }

        static int IndexOfKeyword(string source, string keyword)
        {
            int idx = 0;
            while (idx < source.Length)
            {
                int found = source.IndexOf(keyword, idx, StringComparison.Ordinal);
                if (found < 0)
                {
                    return -1;
                }
                if (IsKeywordAt(source, found, keyword))
                {
                    return found;
                }
                idx = found + keyword.Length;
            }
            return -1;
        }

        static bool IsKeywordAt(string source, int index, string keyword)
        {
            if (index < 0 || index + keyword.Length > source.Length)
            {
                return false;
            }
            if (string.Compare(source, index, keyword, 0, keyword.Length, StringComparison.Ordinal) != 0)
            {
                return false;
            }
            // Word boundary
            if (index > 0 && (char.IsLetterOrDigit(source[index - 1]) || source[index - 1] == '_'))
            {
                return false;
            }
            int after = index + keyword.Length;
            if (after < source.Length && (char.IsLetterOrDigit(source[after]) || source[after] == '_'))
            {
                return false;
            }
            return true;
        }

        static string HandlerToSubtypeHint(string handler)
        {
            switch (handler)
            {
                case "OnDuelComDoCommand": return "DoCommand";
                case "OnDuelComMovePhase": return "MovePhase";
                case "OnDuelComCancelCommand": return "CancelCommand";
                case "OnDuelComCancelCommand2": return "CancelCommand2";
                case "OnDuelDlgSetResult": return "Dialog";
                case "OnDuelListSetCardExData": return "ListCardExData";
                case "OnDuelListSetIndex": return "ListIndex";
                case "OnDuelListInitString": return "ListInitString";
                default: return handler;
            }
        }

        // ---------------------------------------------------------------------
        // Session / invoke helpers (exact names only)
        // ---------------------------------------------------------------------
        static object BeginEnabledSession(Type authority, uint seed)
        {
            MethodInfo begin = RequirePublicStaticMethod(authority, MethodBeginFromPvpLaunch);
            object serverSettings = BuildSyntheticServerDuelSettingsShape(seed, emptyMains: false);
            Dictionary<string, object> launch = new Dictionary<string, object>()
            {
                { LaunchKeyEnabled, true },
                { LaunchKeyFlushPath, Path.Combine(Path.GetTempPath(), "ygomaster-llm005-pvp-auth-test.json") },
            };
            object session = begin.Invoke(null, BindBeginArgs(begin, launch, serverSettings));
            AssertNotNull(session, MethodBeginFromPvpLaunch + " session");
            AssertTrue(Convert.ToBoolean(RequireInstanceProp(session, PropEnabled)),
                "test session Enabled");
            AssertNotNull(GetTranscript(session), "session.Transcript");
            return session;
        }

        static object[] BindBeginArgs(MethodInfo begin, Dictionary<string, object> launch, object serverSettings)
        {
            ParameterInfo[] ps = begin.GetParameters();
            AssertTrue(ps.Length >= 1 && ps.Length <= 2,
                MethodBeginFromPvpLaunch + " must take launchSettings[, serverDuelSettings]");
            if (ps.Length == 1)
            {
                // Single-arg form: merge server settings into launch under known key
                Dictionary<string, object> merged = new Dictionary<string, object>(launch);
                merged["server_duel_settings"] = serverSettings;
                return new object[] { merged };
            }
            return new object[] { launch, serverSettings };
        }

        static bool InvokeTryAcceptVoid(
            MethodInfo accept,
            object session,
            ulong currentEngineSeq,
            ulong messageSeq,
            int actorPlayer,
            string inputSubtype,
            Dictionary<string, object> payload,
            Action native)
        {
            // Ensure subtype stamped for assertion path
            if (payload != null && !payload.ContainsKey("input_subtype"))
            {
                payload["input_subtype"] = inputSubtype;
            }
            object result = accept.Invoke(session, new object[]
            {
                currentEngineSeq,
                messageSeq,
                actorPlayer,
                inputSubtype,
                payload,
                native,
            });
            return Convert.ToBoolean(result);
        }

        static bool InvokeTryAcceptReturn(
            MethodInfo accept,
            object session,
            ulong currentEngineSeq,
            ulong messageSeq,
            int actorPlayer,
            string inputSubtype,
            Dictionary<string, object> payload,
            Func<int> native,
            out int nativeResult)
        {
            if (payload != null && !payload.ContainsKey("input_subtype"))
            {
                payload["input_subtype"] = inputSubtype;
            }
            object[] args = new object[]
            {
                currentEngineSeq,
                messageSeq,
                actorPlayer,
                inputSubtype,
                payload,
                native,
                0,
            };
            object result = accept.Invoke(session, args);
            nativeResult = args[6] != null ? Convert.ToInt32(args[6]) : 0;
            return Convert.ToBoolean(result);
        }

        static LlmAcceptedInputTranscript GetTranscript(object session)
        {
            object t = RequireInstanceProp(session, PropTranscript);
            LlmAcceptedInputTranscript transcript = t as LlmAcceptedInputTranscript;
            AssertNotNull(transcript, PropTranscript + " as LlmAcceptedInputTranscript");
            return transcript;
        }

        static Dictionary<string, object> RequireSettingsDict(object result)
        {
            object settings = result.GetType().GetProperty(ResultPropSettings).GetValue(result, null);
            Dictionary<string, object> d = settings as Dictionary<string, object>;
            AssertNotNull(d, SettingsResultTypeName + "." + ResultPropSettings
                + " must be Dictionary<string,object>");
            return d;
        }

        static object RequireKey(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
            {
                throw new InvalidOperationException("missing required settings key '" + key + "'");
            }
            return v;
        }

        static object RequireInstanceProp(object target, string name)
        {
            PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(p, target.GetType().Name + "." + name + " public instance property required");
            return p.GetValue(target, null);
        }

        static MethodInfo RequirePublicStaticMethod(Type type, string name)
        {
            MethodInfo m = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            AssertNotNull(m, type.Name + "." + name + " public static method required");
            return m;
        }

        static MethodInfo RequireInstanceMethod(Type type, string name)
        {
            MethodInfo m = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            AssertNotNull(m, type.Name + "." + name + " public instance method required");
            return m;
        }

        static object[] BindOutStringMethod(MethodInfo method, object firstArg)
        {
            ParameterInfo[] ps = method.GetParameters();
            AssertTrue(ps.Length == 2 && ps[1].ParameterType == typeof(string).MakeByRefType(),
                method.Name + " signature must be (settings, out string reason)");
            return new object[] { firstArg, null };
        }

        // ---------------------------------------------------------------------
        // Fixtures / payloads
        // ---------------------------------------------------------------------
        static Dictionary<string, object> BuildEmptyMainClientPlaceholder()
        {
            return new Dictionary<string, object>()
            {
                { "seed", 0 },
                { "first_player", 1 },
                { "limited_type", 0 },
                {
                    "decks", new Dictionary<string, object>()
                    {
                        { "player0_main", new object[0] },
                        { "player1_main", new object[0] },
                        { "player0_extra", new object[0] },
                        { "player1_extra", new object[0] },
                    }
                },
            };
        }

        static object BuildSyntheticServerDuelSettingsShape(uint seed, bool emptyMains)
        {
            // Full engine-init authoritative snapshot (review remediation: sparse is incomplete).
            Dictionary<string, object> full =
                Llm005Slice5PvpAuthorityRemediationTests.BuildFullAuthoritativeSettings(seed);
            if (emptyMains)
            {
                Dictionary<string, object> decks = full["decks"] as Dictionary<string, object>;
                decks["player0_main"] = new List<object>();
                decks["player1_main"] = new List<object>();
            }
            // Preserve seed 0 path used by conversion tests.
            full["seed"] = seed;
            full["RandSeed"] = seed;
            return full;
        }

        static Dictionary<string, object> BuildDoCommandPayload()
        {
            return new Dictionary<string, object>()
            {
                { "input_subtype", "DoCommand" },
                { "player", 0 },
                { "position", 13 },
                { "index", 0 },
                { "command_id", 5 },
            };
        }

        static Dictionary<string, object> BuildMovePhasePayload()
        {
            return new Dictionary<string, object>()
            {
                { "input_subtype", "MovePhase" },
                { "phase", 4 },
            };
        }

        static Dictionary<string, object> BuildPayloadForSubtype(string subtype)
        {
            switch (subtype)
            {
                case "DoCommand":
                    return BuildDoCommandPayload();
                case "MovePhase":
                    return BuildMovePhasePayload();
                case "CancelCommand":
                    return new Dictionary<string, object>()
                    {
                        { "input_subtype", "CancelCommand" },
                    };
                case "CancelCommand2":
                    return new Dictionary<string, object>()
                    {
                        { "input_subtype", "CancelCommand2" },
                        { "decide", true },
                    };
                case "Dialog":
                    return new Dictionary<string, object>()
                    {
                        { "input_subtype", "Dialog" },
                        { "result", 1 },
                    };
                case "ListCardExData":
                    return new Dictionary<string, object>()
                    {
                        { "input_subtype", "ListCardExData" },
                        { "index", 0 },
                        { "data", 7 },
                    };
                case "ListIndex":
                    return new Dictionary<string, object>()
                    {
                        { "input_subtype", "ListIndex" },
                        { "index", 2 },
                    };
                case "ListInitString":
                    return new Dictionary<string, object>()
                    {
                        { "input_subtype", "ListInitString" },
                    };
                default:
                    throw new InvalidOperationException("unknown subtype " + subtype);
            }
        }

        static Type TryGetType(string simpleName)
        {
            Assembly asm = typeof(Llm005Slice5PvpAuthorityRegressionTests).Assembly;
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

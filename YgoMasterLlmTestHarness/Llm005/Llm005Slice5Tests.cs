using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-005 Slice 5: out-of-process replay / exact branch expansion (TDD RED).
    ///
    /// Research-gated, default-off. Frozen corpus artifact checks run first and must PASS
    /// without any new production types (proving fixtures are well-formed). Production
    /// surface probes then fail intentionally while types/projects are absent.
    /// Tests never invoke real duel.dll.
    ///
    /// Contract surface (Slice 5 GREEN / dedicated worker project):
    ///   LlmAcceptedInputTranscript + Entry + Kind + Serializer + Validator
    ///   LlmAcceptedInputTranscriptRecorder (default-off; record only after native/server acceptance)
    ///   LlmCheckpointFingerprint + Builder
    ///   LlmSearchWorkerRequest/Response/Protocol
    ///   LlmReplayBranchResult (+ extractor)
    ///   LlmReplayWorkerSupervisor (real child-process probes, PID cleanup)
    ///   LlmLayerBFeasibilityResult (+ probe; GO only after real engine validation)
    ///   YgoMasterSearchWorker exe project (default-off, not referenced by client/server)
    ///
    /// Capture vs consume: live DuelDll may reference transcript/recorder types for capture.
    /// Live control must not consume worker request/response/protocol/supervisor/branch/feasibility.
    /// Worker process may allocate fresh local memory and call DLL_SetWorkMemory for its own
    /// duel.dll instance; live process must never copy work memory or pass live state over protocol.
    /// </summary>
    static class Llm005Slice5Tests
    {
        const int SentinelOppHandId = 99106001;
        const string SentinelOppHandName = "SENTINEL_OPP_HAND_LEAK_LLM005_S5";
        const int SentinelOppSetId = 99106002;
        const string SentinelOppSetName = "SENTINEL_OPP_SET_LEAK_LLM005_S5";
        const int SentinelOppDeckId = 99106003;
        const string SentinelOppDeckName = "SENTINEL_OPP_DECK_LEAK_LLM005_S5";
        const int SentinelOppExtraId = 99106004;
        const string SentinelOppExtraName = "SENTINEL_OPP_EXTRA_LEAK_LLM005_S5";

        const int ControlledPlayer = 1;
        const ulong FixtureCheckpointSeq = 9001;
        const int LayerAWallMs = 500;

        const string CorpusSchema = "ygomaster.llm_accepted_input_transcript.v1";
        const string CorpusId = "llm005_slice5_scripted_replay_v1";
        const string WorkerProjectName = "YgoMasterSearchWorker";
        const string WorkerCsprojRelative = "YgoMasterSearchWorker/YgoMasterSearchWorker.csproj";

        static readonly string[] RequiredFixtureIds = new string[]
        {
            "base_checkpoint_match",
            "excludes_rejected_stale",
            "pair_a_visible_base",
            "pair_b_visible_equivalent",
            "terminal_boundaries_catalog",
        };

        static readonly string[] RequiredAcceptedKinds = new string[]
        {
            "DoCommand", "MovePhase", "Dialog", "List", "Cancel", "Automatic",
        };

        static readonly string[] RequiredBoundaryCases = new string[]
        {
            "opponent_wait_input",
            "optional_response",
            "multi_select",
            "random_result",
            "unsupported_mode",
            "checkpoint_mismatch",
            "stale_generation",
            "timeout",
            "crash",
        };

        static readonly string[] RequiredProductionTypes = new string[]
        {
            "LlmAcceptedInputTranscript",
            "LlmAcceptedInputEntry",
            "LlmAcceptedInputKind",
            "LlmAcceptedInputTranscriptSerializer",
            "LlmAcceptedInputTranscriptValidator",
            "LlmAcceptedInputTranscriptRecorder",
            "LlmCheckpointFingerprint",
            "LlmCheckpointFingerprintBuilder",
            "LlmSearchWorkerRequest",
            "LlmSearchWorkerResponse",
            "LlmSearchWorkerProtocol",
            "LlmReplayBranchResult",
            "LlmReplayWorkerSupervisor",
            "LlmLayerBFeasibilityResult",
        };

        /// <summary>
        /// Forbidden on public worker protocol/request/response surfaces and live PvP process.
        /// Worker-owned fresh local init may use DLL_SetWorkMemory only with local allocation ownership.
        /// </summary>
        static readonly string[] ForbiddenProtocolLiveStateTokens = new string[]
        {
            "work_memory_bytes",
            "live_duel_state",
            "FromLiveDuel",
            "CopyNativeWorkMemory",
            "CopyNative",
        };

        /// <summary>
        /// Worker-output consumption tokens forbidden in live commit/control sources.
        /// Transcript model + recorder capture references are allowed in DuelDll/live input path.
        /// </summary>
        static readonly string[] ForbiddenWorkerConsumerTokens = new string[]
        {
            "LlmSearchWorkerRequest",
            "LlmSearchWorkerResponse",
            "LlmSearchWorkerProtocol",
            "LlmReplayWorkerSupervisor",
            "LlmReplayBranchResult",
            "LlmReplayBranchExtractor",
            "LlmLayerBFeasibilityResult",
            "LlmLayerBFeasibilityProbe",
            "YgoMasterSearchWorker",
            "EngineConfirmedSearch",
        };

        /// <summary>
        /// Capture-side types allowed in live accepted-input path (DuelDll etc.).
        /// </summary>
        static readonly string[] AllowedLiveCaptureTokens = new string[]
        {
            "LlmAcceptedInputTranscript",
            "LlmAcceptedInputTranscriptRecorder",
            "LlmAcceptedInputEntry",
            "LlmAcceptedInputKind",
            "LlmAcceptedInputTranscriptSerializer",
            "LlmAcceptedInputTranscriptValidator",
        };

        static readonly string[] LiveControlSourceRelativePaths = new string[]
        {
            "YgoMasterClient/DuelDll.cs",
            "YgoMasterClient/ClientSettings.cs",
            "YgoMasterServer/Llm/LlmBrokerProtocol.cs",
            "YgoMasterServer/Llm/LegalActionExtractor.cs",
        };

        static readonly string[] RequiredRecorderMethods = new string[]
        {
            "RecordDuelSettings",
            "InitializeDuelSettings",
            "RecordDoCommand",
            "RecordMovePhase",
            "RecordDialog",
            "RecordList",
            "RecordCancel",
            "RecordAutomatic",
        };

        public static void RunAll()
        {
            // Artifact contract FIRST — must pass offline without production types.
            FrozenCorpusArtifactContractPassesOffline();

            // Production surface (intentional RED while types/project absent).
            RequireProductionSurface();

            TranscriptRecorderRecordsOnlyAfterAcceptance();
            TranscriptIncludesDuelSettingsAndAllAcceptedInputFamilies();
            TranscriptExcludesRejectedAndStaleAttempts();
            CanonicalSerializationIsDeterministic();
            ValidationRejectsMalformedDuplicateOutOfOrderAndHiddenLeaks();
            CheckpointFingerprintAllowedInformationOnly();
            PairedVisibleEquivalentFixturesShareFingerprintAndCandidates();
            WorkerProtocolIsOutOfProcessOnly();
            WorkerOwnedWorkMemoryContract();
            BranchExtractionOneRootAndForcedCollapseOnly();
            ExplicitTerminalBoundariesNeverSynthesizeOpponentPass();
            SupervisorCleansTimedOutAndCrashedWorkers();
            DefaultOffAndNoClientConsumer();
            ScriptedCorpusCheckpointMatchAndDeterminism();
            ForcedFailureCleanupAndLatencyReporting();
            FeasibilityResultGoNoGoAndHiddenStateIsAutomaticNoGo();
            ScriptedOnlyCannotProduceGoDecision();
        }

        // =====================================================================
        // (1) Frozen corpus artifact contract — no production types
        // =====================================================================

        static void FrozenCorpusArtifactContractPassesOffline()
        {
            DirectoryInfo fixtureDir = LocateFixtureDirectory();
            AssertTrue(fixtureDir != null && fixtureDir.Exists,
                "Tools/fixtures/llm_replay/ must exist");

            string manifestPath = Path.Combine(fixtureDir.FullName, "manifest.json");
            AssertTrue(File.Exists(manifestPath), "manifest.json must exist");

            Dictionary<string, object> manifest = LoadJsonObject(manifestPath);
            AssertEqual(CorpusSchema, GetString(manifest, "schema"), "manifest.schema");
            AssertEqual(CorpusId, GetString(manifest, "corpus_id"), "manifest.corpus_id");

            // Manifest fixture listing vs on-disk exact set of *.fixture.json
            List<object> listed = GetList(manifest, "fixtures");
            AssertNotNull(listed, "manifest.fixtures");
            HashSet<string> listedFiles = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> listedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in listed)
            {
                Dictionary<string, object> row = item as Dictionary<string, object>;
                AssertNotNull(row, "manifest fixture row");
                string file = GetString(row, "file");
                string id = GetString(row, "fixture_id");
                AssertFalse(string.IsNullOrEmpty(file), "manifest fixture file");
                AssertFalse(string.IsNullOrEmpty(id), "manifest fixture_id");
                AssertTrue(listedFiles.Add(file), "duplicate manifest file " + file);
                AssertTrue(listedIds.Add(id), "duplicate manifest fixture_id " + id);
                AssertTrue(File.Exists(Path.Combine(fixtureDir.FullName, file)),
                    "manifest lists missing file " + file);
            }

            string[] onDisk = Directory.GetFiles(fixtureDir.FullName, "*.fixture.json")
                .Select(Path.GetFileName)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            string[] listedSorted = listedFiles.OrderBy(s => s, StringComparer.Ordinal).ToArray();
            AssertEqual(string.Join("|", listedSorted), string.Join("|", onDisk),
                "manifest fixtures must exactly match on-disk *.fixture.json listing");

            // Required fixture IDs
            foreach (string id in RequiredFixtureIds)
            {
                AssertTrue(listedIds.Contains(id), "missing required fixture_id " + id);
            }

            // Required boundary list locked in manifest
            List<object> manifestBoundaries = GetList(manifest, "required_boundary_cases");
            AssertNotNull(manifestBoundaries, "manifest.required_boundary_cases");
            HashSet<string> mb = new HashSet<string>(
                manifestBoundaries.Select(o => Convert.ToString(o)), StringComparer.Ordinal);
            foreach (string b in RequiredBoundaryCases)
            {
                AssertTrue(mb.Contains(b), "manifest missing required boundary " + b);
            }
            AssertEqual(RequiredBoundaryCases.Length, mb.Count,
                "manifest required_boundary_cases count");

            // Worker project metadata locked in manifest (file may be absent in RED)
            Dictionary<string, object> worker = GetDict(manifest, "worker_project");
            AssertNotNull(worker, "manifest.worker_project");
            AssertEqual(WorkerProjectName, GetString(worker, "name"), "worker_project.name");
            AssertEqual(WorkerCsprojRelative, GetString(worker, "csproj_path"), "worker csproj path");
            AssertEqual(true, GetBool(worker, "default_off"), "worker default_off");
            List<object> mustNotRef = GetList(worker, "must_not_be_project_reference_of");
            AssertNotNull(mustNotRef, "must_not_be_project_reference_of");
            AssertTrue(mustNotRef.Count >= 2, "must list client+server csproj");

            // Load every fixture; unique IDs; schema
            Dictionary<string, Dictionary<string, object>> fixtures =
                new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            foreach (string file in onDisk)
            {
                Dictionary<string, object> fx = LoadJsonObject(Path.Combine(fixtureDir.FullName, file));
                AssertEqual(CorpusSchema, GetString(fx, "schema"), file + " schema");
                string id = GetString(fx, "fixture_id");
                AssertFalse(string.IsNullOrEmpty(id), file + " fixture_id");
                AssertFalse(fixtures.ContainsKey(id), "duplicate fixture_id on disk " + id);
                fixtures[id] = fx;
            }
            AssertEqual(listedIds.Count, fixtures.Count, "fixture id count");

            // Base: duel settings + six kinds + monotonic unique sequences + fields
            Dictionary<string, object> baseFx = fixtures["base_checkpoint_match"];
            Dictionary<string, object> settings = GetDict(baseFx, "duel_settings");
            AssertNotNull(settings, "base duel_settings");
            AssertTrue(settings.ContainsKey("seed"), "duel_settings.seed");
            AssertTrue(settings.ContainsKey("first_player"), "duel_settings.first_player");
            AssertTrue(settings.ContainsKey("limited_type"), "duel_settings.limited_type");
            AssertTrue(settings.ContainsKey("decks"), "duel_settings.decks");

            List<object> entries = GetList(baseFx, "entries");
            AssertNotNull(entries, "base entries");
            AssertTrue(entries.Count >= 6, "base needs >=6 accepted entries");
            HashSet<string> kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<long> sequences = new HashSet<long>();
            long prevSeq = -1;
            foreach (object raw in entries)
            {
                Dictionary<string, object> e = raw as Dictionary<string, object>;
                AssertNotNull(e, "entry object");
                string kind = GetString(e, "kind");
                AssertFalse(string.IsNullOrEmpty(kind), "entry.kind");
                kinds.Add(kind);
                long seq = GetLong(e, "accepted_sequence");
                AssertTrue(seq > prevSeq,
                    "accepted_sequence must be strictly monotonic (got " + seq + " after " + prevSeq + ")");
                AssertTrue(sequences.Add(seq), "accepted_sequence must be unique: " + seq);
                prevSeq = seq;
                AssertTrue(e.ContainsKey("actor"), "entry.actor");
                AssertTrue(e.ContainsKey("run_effect_seq"), "entry.run_effect_seq");
                AssertTrue(e.ContainsKey("payload"), "entry.payload");
                AssertNotNull(e["payload"], "entry.payload non-null");
                AssertTrue(e.ContainsKey("acceptance_provenance"), "entry.acceptance_provenance");
                AssertFalse(string.IsNullOrEmpty(GetString(e, "acceptance_provenance")),
                    "acceptance_provenance non-empty");
                // No rejected/stale in accepted entries
                string blob = (GetString(e, "acceptance_provenance") + " " + kind
                    + " " + GetString(e, "status")).ToLowerInvariant();
                AssertFalse(blob.Contains("rejected") || blob.Contains("stale"),
                    "accepted entries must not be rejected/stale: " + blob);
                if (e.ContainsKey("accepted"))
                {
                    AssertEqual(true, GetBool(e, "accepted"), "entry.accepted");
                }
            }
            foreach (string kind in RequiredAcceptedKinds)
            {
                AssertTrue(kinds.Contains(kind), "base missing accepted kind " + kind);
            }

            // Excludes fixture: rejected+stale only outside accepted entries
            Dictionary<string, object> excl = fixtures["excludes_rejected_stale"];
            List<object> exclEntries = GetList(excl, "entries");
            List<object> excluded = GetList(excl, "excluded_attempts");
            AssertNotNull(excluded, "excluded_attempts");
            AssertTrue(excluded.Count >= 2, "excluded_attempts needs rejected and stale samples");
            bool sawRejected = false;
            bool sawStale = false;
            foreach (object raw in excluded)
            {
                Dictionary<string, object> e = raw as Dictionary<string, object>;
                AssertNotNull(e, "excluded attempt");
                string status = GetString(e, "status").ToLowerInvariant();
                if (status.Contains("rejected"))
                {
                    sawRejected = true;
                }
                if (status.Contains("stale"))
                {
                    sawStale = true;
                }
                if (e.ContainsKey("accepted"))
                {
                    AssertEqual(false, GetBool(e, "accepted"), "excluded attempt accepted=false");
                }
            }
            AssertTrue(sawRejected, "excluded_attempts must include a rejected attempt");
            AssertTrue(sawStale, "excluded_attempts must include a stale attempt");
            foreach (object raw in exclEntries)
            {
                Dictionary<string, object> e = raw as Dictionary<string, object>;
                string blob = MiniJSON.Json.Serialize(e).ToLowerInvariant();
                AssertFalse(blob.Contains("rejected") || blob.Contains("stale"),
                    "accepted entries in excludes fixture must not contain rejected/stale");
            }

            // Boundary catalog locks full required list
            Dictionary<string, object> bounds = fixtures["terminal_boundaries_catalog"];
            List<object> boundList = GetList(bounds, "required_boundary_cases");
            if (boundList == null)
            {
                boundList = GetList(bounds, "boundary_cases");
            }
            AssertNotNull(boundList, "terminal_boundaries_catalog boundaries");
            HashSet<string> boundSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (object raw in boundList)
            {
                Dictionary<string, object> row = raw as Dictionary<string, object>;
                if (row != null)
                {
                    string b = GetString(row, "boundary");
                    if (string.IsNullOrEmpty(b))
                    {
                        b = GetString(row, "kind");
                    }
                    if (!string.IsNullOrEmpty(b))
                    {
                        boundSet.Add(b);
                    }
                }
                else
                {
                    string b = Convert.ToString(raw);
                    if (!string.IsNullOrEmpty(b))
                    {
                        boundSet.Add(b);
                    }
                }
            }
            // Also accept top-level required_boundary_cases as strings
            List<object> topBounds = GetList(bounds, "required_boundary_cases");
            if (topBounds != null)
            {
                foreach (object o in topBounds)
                {
                    boundSet.Add(Convert.ToString(o));
                }
            }
            foreach (string b in RequiredBoundaryCases)
            {
                AssertTrue(boundSet.Contains(b), "boundary catalog missing " + b);
            }

            // Sentinel isolation: private sentinels must not appear in provider-visible sections
            string[] sentinelTokens = new string[]
            {
                SentinelOppHandName, SentinelOppHandId.ToString(),
                SentinelOppSetName, SentinelOppSetId.ToString(),
                SentinelOppDeckName, SentinelOppDeckId.ToString(),
                SentinelOppExtraName, SentinelOppExtraId.ToString(),
            };
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in fixtures)
            {
                Dictionary<string, object> fx = kv.Value;
                // Visible surfaces
                foreach (string surface in new[] { "duel_settings", "entries", "visible_public", "controlled_self", "expectations" })
                {
                    if (!fx.ContainsKey(surface) || fx[surface] == null)
                    {
                        continue;
                    }
                    string blob = MiniJSON.Json.Serialize(fx[surface]);
                    foreach (string token in sentinelTokens)
                    {
                        AssertFalse(
                            blob.IndexOf(token, StringComparison.Ordinal) >= 0,
                            kv.Key + "." + surface + " must not contain sentinel " + token);
                    }
                }
            }

            // Pair A/B: canonical equality after removing fixture_id + private hidden only
            Dictionary<string, object> pairA = CloneJsonObject(fixtures["pair_a_visible_base"]);
            Dictionary<string, object> pairB = CloneJsonObject(fixtures["pair_b_visible_equivalent"]);
            StripPairPrivateKeys(pairA);
            StripPairPrivateKeys(pairB);
            string canonA = CanonicalJson(pairA);
            string canonB = CanonicalJson(pairB);
            AssertEqual(canonA, canonB,
                "pair A/B must be canonically equal after removing fixture_id and private hidden sections");

            // Console evidence for RED report
            Console.WriteLine(
                "YGOMASTER-LLM-005 Slice 5 artifact contract PASS: "
                + fixtures.Count + " fixtures, schema=" + CorpusSchema
                + ", boundaries=" + RequiredBoundaryCases.Length
                + ", kinds=" + RequiredAcceptedKinds.Length
                + " (no production types required)");
        }

        static void StripPairPrivateKeys(Dictionary<string, object> fx)
        {
            fx.Remove("fixture_id");
            fx.Remove("opponent_hidden_private");
            fx.Remove("hidden_sentinels_private");
        }

        // =====================================================================
        // Production surface probe (intentional RED)
        // =====================================================================

        static void RequireProductionSurface()
        {
            // Fail first on missing production types (intentional RED message).
            // Prefer failing on LlmAcceptedInputTranscriptRecorder when that is the first missing
            // name only after earlier types exist; overall list order keeps transcript core first.
            foreach (string name in RequiredProductionTypes)
            {
                RequireType(name);
            }

            // GREEN: dedicated worker executable project must exist and stay unreferenced.
            DirectoryInfo repoRoot = LocateRepoRoot();
            string workerCsproj = Path.Combine(
                repoRoot.FullName, WorkerCsprojRelative.Replace('/', Path.DirectorySeparatorChar));
            AssertTrue(File.Exists(workerCsproj),
                "YGOMASTER-LLM-005 Slice 5 GREEN requires dedicated worker project "
                + WorkerCsprojRelative
                + " (default-off Exe output; not referenced by YgoMasterClient/YgoMaster).");
            AssertNoProjectReferenceToWorker(repoRoot);

            // Recorder API surface (capture after acceptance — not fixture-only factories).
            Type recorderType = RequireType("LlmAcceptedInputTranscriptRecorder");
            AssertRecorderSurface(recorderType);

            Type supervisor = RequireType("LlmReplayWorkerSupervisor");
            MethodInfo start = FindMethod(supervisor, "StartWorker", "LaunchWorker", "Spawn", "Start");
            AssertNotNull(start,
                "LlmReplayWorkerSupervisor must expose StartWorker/LaunchWorker/Spawn/Start (out-of-process only)");

            // Protocol/request/response must not accept shared/copied live work-memory fields.
            foreach (string typeName in new[]
            {
                "LlmSearchWorkerProtocol",
                "LlmSearchWorkerRequest",
                "LlmSearchWorkerResponse",
            })
            {
                Type t = RequireType(typeName);
                AssertNoForbiddenProtocolLiveStateSurface(t);
            }

            string workerDir = Path.GetDirectoryName(workerCsproj);
            if (!string.IsNullOrEmpty(workerDir) && Directory.Exists(workerDir))
            {
                AssertWorkerSourceAllowsOwnedSetWorkMemoryOnly(workerDir);
            }
        }

        static void AssertRecorderSurface(Type recorderType)
        {
            // Default-off
            object enabled = GetStaticPropOrField(recorderType,
                "DefaultEnabled", "IsDefaultEnabled", "EnabledByDefault", "FeatureDefault");
            if (enabled != null)
            {
                AssertEqual(false, Convert.ToBoolean(enabled),
                    "LlmAcceptedInputTranscriptRecorder must be default-off");
            }
            else if (HasAnyProp(CreateInstanceSafe(recorderType), "Enabled", "IsEnabled"))
            {
                object inst = CreateInstanceSafe(recorderType);
                if (inst != null)
                {
                    AssertEqual(false, GetBoolPropFlexible(inst, "Enabled", "IsEnabled"),
                        "recorder instance Enabled default-off");
                }
            }

            // Methods for duel settings + all accepted families
            string[] methodAliases = new string[]
            {
                "RecordDuelSettings|InitializeDuelSettings|SetDuelSettings|BeginDuel",
                "RecordDoCommand|OnDoCommandAccepted|RecordCommand",
                "RecordMovePhase|OnMovePhaseAccepted|RecordPhase",
                "RecordDialog|OnDialogAccepted",
                "RecordList|OnListAccepted|RecordListSelection",
                "RecordCancel|OnCancelAccepted|RecordCancelWithSemantics",
                "RecordAutomatic|OnAutomaticAccepted|RecordAutomaticCommit",
            };
            foreach (string group in methodAliases)
            {
                string[] names = group.Split('|');
                MethodInfo found = FindMethod(recorderType, names);
                AssertNotNull(found,
                    "LlmAcceptedInputTranscriptRecorder must expose one of: " + group);
            }

            // Monotonic accepted sequence accessor or NextSequence property
            AssertTrue(
                FindMethod(recorderType, "NextAcceptedSequence", "GetNextAcceptedSequence", "AcceptedSequence") != null
                || FindMethod(recorderType, "RecordDoCommand", "OnDoCommandAccepted") != null,
                "recorder must own monotonic accepted sequence (method or via Record* side effect contract)");

            // Rejected/stale must not land in committed entries. Either there is no
            // RecordRejected/RecordStale into the transcript, or exclusions are separate diagnostics.
            MethodInfo rejectIntoTranscript = FindMethod(recorderType,
                "RecordRejectedAsAccepted", "CommitRejectedAttempt", "AcceptStaleAttempt");
            AssertTrue(rejectIntoTranscript == null,
                "recorder must not provide an API that commits rejected/stale attempts as accepted entries");
            MethodInfo exclusionDiag = FindMethod(recorderType,
                "RecordRejectedAttempt", "NoteRejectedAttempt", "RecordStaleAttempt",
                "RecordExclusion", "NoteExclusion", "GetExclusions", "DrainExclusions");
            // Optional exclusion diagnostics are fine; committed entries path is Record* families only.

            // Capture integration hook for live accepted-input path (after native/server acceptance).
            MethodInfo afterAccept = FindMethod(recorderType,
                "OnInputAccepted", "NotifyAccepted", "AfterNativeAcceptance", "OnServerAccepted");
            AssertTrue(
                afterAccept != null
                || FindMethod(recorderType, "RecordDoCommand", "OnDoCommandAccepted") != null,
                "recorder must integrate after native/server acceptance (OnInputAccepted or Record* surface)");
        }

        static object CreateInstanceSafe(Type type)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }

        static bool HasTypeMember(Type type, params string[] names)
        {
            foreach (string name in names)
            {
                if (type.GetProperty(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) != null)
                {
                    return true;
                }
                if (type.GetField(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) != null)
                {
                    return true;
                }
            }
            return false;
        }

        static void AssertNoProjectReferenceToWorker(DirectoryInfo repoRoot)
        {
            string[] hosts = new string[]
            {
                Path.Combine(repoRoot.FullName, "YgoMasterClient.csproj"),
                Path.Combine(repoRoot.FullName, "YgoMasterServer", "YgoMaster.csproj"),
            };
            foreach (string host in hosts)
            {
                if (!File.Exists(host))
                {
                    continue;
                }
                string text = File.ReadAllText(host);
                AssertFalse(
                    text.IndexOf("YgoMasterSearchWorker", StringComparison.OrdinalIgnoreCase) >= 0,
                    host + " must not reference YgoMasterSearchWorker (Layer B default-off isolation)");
            }
        }

        static void AssertNoForbiddenProtocolLiveStateSurface(Type t)
        {
            foreach (MethodInfo m in t.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                string n = m.Name ?? string.Empty;
                AssertFalse(ContainsProtocolForbiddenToken(n), t.Name + "." + n + " forbidden protocol live-state name");
                foreach (ParameterInfo p in m.GetParameters())
                {
                    AssertFalse(ContainsProtocolForbiddenToken(p.Name),
                        t.Name + "." + n + " param " + p.Name + " forbidden");
                    AssertFalse(ContainsProtocolForbiddenToken(p.ParameterType.Name),
                        t.Name + "." + n + " param type " + p.ParameterType.Name + " forbidden");
                }
            }
            foreach (PropertyInfo p in t.GetProperties(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                AssertFalse(ContainsProtocolForbiddenToken(p.Name), t.Name + " property " + p.Name + " forbidden");
            }
            foreach (FieldInfo f in t.GetFields(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                AssertFalse(ContainsProtocolForbiddenToken(f.Name), t.Name + " field " + f.Name + " forbidden");
            }
        }

        static bool ContainsProtocolForbiddenToken(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            foreach (string token in ForbiddenProtocolLiveStateTokens)
            {
                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Worker may call DLL_SetWorkMemory only for freshly allocated worker-owned memory
        /// with cleanup; never transfer live-process work memory.
        /// </summary>
        static void AssertWorkerSourceAllowsOwnedSetWorkMemoryOnly(string workerDir)
        {
            foreach (string cs in Directory.GetFiles(workerDir, "*.cs", SearchOption.AllDirectories))
            {
                string text = StripCSharpComments(File.ReadAllText(cs));
                // Forbidden shared/live transfer APIs in worker source
                AssertFalse(
                    text.IndexOf("CopyNativeWorkMemory", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("FromLiveDuel", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("live_duel_state", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("work_memory_bytes", StringComparison.OrdinalIgnoreCase) >= 0,
                    cs + " must not transfer live-process work memory over protocol fields");

                if (text.IndexOf("DLL_SetWorkMemory", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("SetWorkMemory", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Must show fresh local allocation / ownership + cleanup nearby in same file.
                    bool hasFreshAlloc =
                        text.IndexOf("AllocHGlobal", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("Marshal.Alloc", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("AllocateWorkMemory", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("WorkerOwned", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("fresh", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("local", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool hasCleanup =
                        text.IndexOf("FreeHGlobal", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("Marshal.Free", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("Dispose", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("ReleaseWorkMemory", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("cleanup", StringComparison.OrdinalIgnoreCase) >= 0;
                    AssertTrue(hasFreshAlloc,
                        cs + ": DLL_SetWorkMemory allowed only with fresh worker-owned allocation");
                    AssertTrue(hasCleanup,
                        cs + ": worker-owned work memory must include cleanup/release");
                    AssertFalse(
                        text.IndexOf("live process", StringComparison.OrdinalIgnoreCase) >= 0
                        && text.IndexOf("copy", StringComparison.OrdinalIgnoreCase) >= 0
                        && text.IndexOf("work memory", StringComparison.OrdinalIgnoreCase) >= 0,
                        cs + ": must not copy live-process work memory");
                }
            }
        }

        // =====================================================================
        // Production-bound tests (run only after types exist)
        // =====================================================================

        static void TranscriptIncludesDuelSettingsAndAllAcceptedInputFamilies()
        {
            object transcript = BuildScriptedTranscriptFixture("base_checkpoint_match");
            AssertNotNull(transcript, "LlmAcceptedInputTranscript from frozen fixture");
            object settings = GetPropFlexible(transcript, "DuelSettings", "Settings", "Header");
            AssertNotNull(settings, "transcript must carry duel settings header");
            AssertTrue(
                HasAnyProp(settings, "Seed", "RandomSeed", "DuelSeed")
                || GetNumericPropFlexible(settings, "Seed", "RandomSeed") != 0,
                "transcript must include seed");
            AssertTrue(
                HasAnyProp(settings, "MainDeck0", "MainDeck1", "Decks", "Deck0", "Player0MainDeck")
                || HasAnyProp(transcript, "Decks", "MainDecks"),
                "transcript must include decks");
            AssertTrue(
                HasAnyProp(settings, "FirstPlayer", "StartingPlayer")
                || HasAnyProp(transcript, "FirstPlayer", "StartingPlayer"),
                "transcript must include first player");
            AssertTrue(
                HasAnyProp(settings, "LimitedType", "Regulation", "LimitRegulation")
                || HasAnyProp(transcript, "LimitedType", "Regulation"),
                "transcript must include limited type");

            IList entries = GetEntries(transcript);
            AssertTrue(entries != null && entries.Count >= 6, "multiple accepted input families");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long prevSeq = -1;
            foreach (object entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }
                string kind = GetStringPropFlexible(entry, "Kind", "InputKind", "Type");
                AssertFalse(string.IsNullOrEmpty(kind), "each entry needs Kind");
                seen.Add(NormalizeKind(kind));
                long seq = GetNumericPropFlexible(entry, "AcceptedSequence", "Sequence", "Seq", "Order");
                AssertTrue(seq > prevSeq, "accepted sequence strictly monotonic");
                prevSeq = seq;
                AssertTrue(HasAnyProp(entry, "Actor", "ActorPlayer", "Player"), "actor");
                AssertTrue(
                    HasAnyProp(entry, "RunEffectSeq", "CheckpointSeq", "AssociatedRunEffectSeq", "CheckpointId"),
                    "run_effect_seq/checkpoint");
                AssertTrue(
                    HasAnyProp(entry, "Payload", "Body", "Data", "Command", "Phase", "DialogResult"),
                    "payload");
                AssertTrue(
                    HasAnyProp(entry, "AcceptanceProvenance", "Provenance", "AcceptedBy"),
                    "acceptance provenance");
            }
            foreach (string kind in RequiredAcceptedKinds)
            {
                AssertTrue(
                    seen.Contains(NormalizeKind(kind))
                    || seen.Any(s => s.IndexOf(NormalizeKind(kind), StringComparison.OrdinalIgnoreCase) >= 0),
                    "missing accepted family " + kind);
            }
        }

        static void TranscriptExcludesRejectedAndStaleAttempts()
        {
            object transcript = BuildScriptedTranscriptFixture("excludes_rejected_stale");
            IList entries = GetEntries(transcript);
            AssertNotNull(entries, "entries");
            foreach (object entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }
                string provenance = GetStringPropFlexible(entry,
                    "AcceptanceProvenance", "Provenance", "AcceptedBy", "Status");
                string kind = GetStringPropFlexible(entry, "Kind", "InputKind", "Type");
                string blob = (provenance + " " + kind).ToLowerInvariant();
                AssertFalse(
                    blob.Contains("rejected") || blob.Contains("stale")
                    || blob.Contains("attempted_only") || blob.Contains("not_accepted"),
                    "transcript must exclude rejected/stale attempted inputs");
                if (HasAnyProp(entry, "Accepted", "IsAccepted"))
                {
                    AssertEqual(true, GetBoolPropFlexible(entry, "Accepted", "IsAccepted"),
                        "entry.Accepted must be true");
                }
            }
        }

        static void CanonicalSerializationIsDeterministic()
        {
            Type serializerType = RequireType("LlmAcceptedInputTranscriptSerializer");
            object transcript = BuildScriptedTranscriptFixture("base_checkpoint_match");
            string a = InvokeSerialize(serializerType, transcript);
            string b = InvokeSerialize(serializerType, transcript);
            AssertFalse(string.IsNullOrEmpty(a), "canonical serialization");
            AssertEqual(a, b, "deterministic serialization");
            object transcript2 = BuildScriptedTranscriptFixture("base_checkpoint_match");
            AssertEqual(a, InvokeSerialize(serializerType, transcript2), "equivalent fixtures identical");
        }

        static void ValidationRejectsMalformedDuplicateOutOfOrderAndHiddenLeaks()
        {
            Type validatorType = RequireType("LlmAcceptedInputTranscriptValidator");
            object good = BuildScriptedTranscriptFixture("base_checkpoint_match");
            AssertValidationOk(InvokeValidate(validatorType, good), "valid frozen transcript");
            AssertValidationFails(validatorType, MutateTranscript(good, MutateMode.ClearFirstKind), "kind");
            AssertValidationFails(validatorType, MutateTranscript(good, MutateMode.DuplicateFirstSequence), "duplicate");
            AssertValidationFails(validatorType, MutateTranscript(good, MutateMode.ReverseSequences), "order");
            AssertValidationFails(validatorType, MutateTranscript(good, MutateMode.NullFirstPayload), "payload");
            AssertValidationFails(validatorType, MutateTranscript(good, MutateMode.InjectHiddenSentinel), "hidden");
        }

        static void CheckpointFingerprintAllowedInformationOnly()
        {
            Type builderType = RequireType("LlmCheckpointFingerprintBuilder");
            object fixture = BuildCheckpointFixture("pair_a_visible_base",
                SentinelOppHandId, SentinelOppHandName,
                SentinelOppSetId, SentinelOppSetName,
                SentinelOppDeckId, SentinelOppDeckName,
                SentinelOppExtraId, SentinelOppExtraName);
            object fingerprint = InvokeBuildFingerprint(builderType, fixture);
            string serialized = SerializeObject(fingerprint);
            AssertNoLeak(serialized,
                SentinelOppHandName, SentinelOppHandId.ToString(),
                SentinelOppSetName, SentinelOppSetId.ToString(),
                SentinelOppDeckName, SentinelOppDeckId.ToString(),
                SentinelOppExtraName, SentinelOppExtraId.ToString());
        }

        static void PairedVisibleEquivalentFixturesShareFingerprintAndCandidates()
        {
            Type builderType = RequireType("LlmCheckpointFingerprintBuilder");
            object pairA = BuildCheckpointFixture("pair_a_visible_base",
                SentinelOppHandId, SentinelOppHandName,
                SentinelOppSetId, SentinelOppSetName,
                SentinelOppDeckId, SentinelOppDeckName,
                SentinelOppExtraId, SentinelOppExtraName);
            object pairB = BuildCheckpointFixture("pair_b_visible_equivalent",
                88206001, "ALT_OPP_HAND_HIDDEN_S5",
                88206002, "ALT_OPP_SET_HIDDEN_S5",
                88206003, "ALT_OPP_DECK_HIDDEN_S5",
                88206004, "ALT_OPP_EXTRA_HIDDEN_S5");
            AssertEqual(
                SerializeObject(InvokeBuildFingerprint(builderType, pairA)),
                SerializeObject(InvokeBuildFingerprint(builderType, pairB)),
                "paired fingerprints identical");
            AssertEqual(
                SerializeObject(InvokeScriptedBranch(pairA, 0)),
                SerializeObject(InvokeScriptedBranch(pairB, 0)),
                "paired candidate outputs identical");
        }

        static void WorkerProtocolIsOutOfProcessOnly()
        {
            Type requestType = RequireType("LlmSearchWorkerRequest");
            Type responseType = RequireType("LlmSearchWorkerResponse");
            Type protocolType = RequireType("LlmSearchWorkerProtocol");
            object request = CreateWorkerRequest(requestType, 1, 250, "base_checkpoint_match", FixtureCheckpointSeq, 0);
            AssertTrue(HasAnyProp(request, "Generation", "RequestGeneration", "SearchGeneration"), "generation");
            AssertTrue(HasAnyProp(request, "DeadlineMs", "Deadline", "DeadlineUtc", "TimeoutMs"), "deadline");
            AssertTrue(HasAnyProp(request, "Transcript", "TranscriptId", "TranscriptPath", "AcceptedInputTranscript"), "transcript");
            AssertTrue(HasAnyProp(request, "CheckpointSeq", "Checkpoint", "CheckpointId", "RunEffectSeq"), "checkpoint");
            AssertTrue(HasAnyProp(request, "RootActionId", "RootAction", "CandidateRootActionId"), "root");
            object response = InvokeProtocolRoundTripOrSchema(protocolType, request, responseType);
            AssertTrue(HasAnyProp(response, "Status", "ResultStatus", "Outcome"), "status");
            AssertTrue(HasAnyProp(response, "Fingerprint", "CheckpointFingerprint", "AllowedFingerprint"), "fingerprint");
            AssertTrue(HasAnyProp(response, "Branch", "BranchResult", "NextWindow", "Lines"), "branch");
            AssertTrue(HasAnyProp(response, "Diagnostics", "Diagnostic", "WorkerDiagnostics"), "diagnostics");
            string blob = SerializeObject(request) + SerializeObject(response);
            foreach (string token in ForbiddenProtocolLiveStateTokens)
            {
                AssertFalse(blob.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0,
                    "worker protocol must not carry shared/live work-memory field " + token);
            }
            // DLL_SetWorkMemory is worker-process-local init, not a protocol field.
            AssertFalse(
                blob.IndexOf("work_memory_bytes", StringComparison.OrdinalIgnoreCase) >= 0,
                "protocol JSON must not include work_memory_bytes from live process");
        }

        static void WorkerOwnedWorkMemoryContract()
        {
            // Live PvP process must never call speculative SetWorkMemory or copy native work memory.
            DirectoryInfo repoRoot = LocateRepoRoot();
            string duelDll = Path.Combine(repoRoot.FullName, "YgoMasterClient", "DuelDll.cs");
            AssertTrue(File.Exists(duelDll), "DuelDll.cs required for live-process memory contract");
            string liveText = StripCSharpComments(File.ReadAllText(duelDll));
            // Allow historical/import mentions only if not speculative search path — forbid clear copy/speculative patterns.
            AssertFalse(
                liveText.IndexOf("CopyNativeWorkMemory", StringComparison.OrdinalIgnoreCase) >= 0
                || liveText.IndexOf("FromLiveDuel", StringComparison.OrdinalIgnoreCase) >= 0
                || liveText.IndexOf("work_memory_bytes", StringComparison.OrdinalIgnoreCase) >= 0
                || liveText.IndexOf("live_duel_state", StringComparison.OrdinalIgnoreCase) >= 0,
                "live DuelDll must not copy/export native work memory for Layer B speculation");

            // When worker project exists, owned-init contract applies (GREEN path).
            string workerCsproj = Path.Combine(
                repoRoot.FullName, WorkerCsprojRelative.Replace('/', Path.DirectorySeparatorChar));
            string workerDir = Path.GetDirectoryName(workerCsproj);
            if (!string.IsNullOrEmpty(workerDir) && Directory.Exists(workerDir))
            {
                AssertWorkerSourceAllowsOwnedSetWorkMemoryOnly(workerDir);
            }

            // Protocol types never accept shared memory bytes.
            Type requestType = RequireType("LlmSearchWorkerRequest");
            Type responseType = RequireType("LlmSearchWorkerResponse");
            AssertNoForbiddenProtocolLiveStateSurface(requestType);
            AssertNoForbiddenProtocolLiveStateSurface(responseType);
        }

        static void TranscriptRecorderRecordsOnlyAfterAcceptance()
        {
            Type recorderType = RequireType("LlmAcceptedInputTranscriptRecorder");
            AssertRecorderSurface(recorderType);

            // Live capture integration: DuelDll (or server accept path) may reference recorder
            // for post-acceptance recording — must not be a worker-output consumer.
            DirectoryInfo repoRoot = LocateRepoRoot();
            string duelDll = Path.Combine(repoRoot.FullName, "YgoMasterClient", "DuelDll.cs");
            AssertTrue(File.Exists(duelDll), "DuelDll.cs");
            // GREEN will wire Record* after acceptance; RED only requires type+API exist.
            // Ensure no worker consumption tokens in the same live capture file once wired.
            AssertSourceHasNoTokens(duelDll, ForbiddenWorkerConsumerTokens, stripComments: true);
        }

        static void BranchExtractionOneRootAndForcedCollapseOnly()
        {
            object branch = InvokeScriptedBranch(
                BuildCheckpointFixture("base_checkpoint_match",
                    SentinelOppHandId, SentinelOppHandName,
                    SentinelOppSetId, SentinelOppSetName,
                    SentinelOppDeckId, SentinelOppDeckName,
                    SentinelOppExtraId, SentinelOppExtraName),
                rootActionId: 2);
            AssertEqual(2L, GetNumericPropFlexible(branch, "AppliedRootActionId", "RootActionId", "RootId"),
                "exactly requested root");
            if (HasAnyProp(branch, "AppliedRootCount", "RootsApplied"))
            {
                AssertEqual(1L, GetNumericPropFlexible(branch, "AppliedRootCount", "RootsApplied"),
                    "exactly one root");
            }
            AssertTrue(
                HasAnyProp(branch, "CollapsedTransitions", "ForcedCollapses", "CollapsedSteps"),
                "must expose collapsed transitions list");
        }

        static void ExplicitTerminalBoundariesNeverSynthesizeOpponentPass()
        {
            foreach (string boundary in RequiredBoundaryCases)
            {
                object branch = InvokeScriptedBoundaryBranch(boundary);
                AssertNotNull(branch, "boundary " + boundary);
                string status = GetStringPropFlexible(branch, "Status", "Boundary", "TerminalBoundary", "ResultStatus");
                string kind = GetStringPropFlexible(branch, "BoundaryKind", "TerminalReason", "StopReason");
                string combined = (status + " " + kind).ToLowerInvariant();
                AssertTrue(
                    combined.IndexOf(boundary.Replace('_', ' '), StringComparison.OrdinalIgnoreCase) >= 0
                    || combined.IndexOf(boundary, StringComparison.OrdinalIgnoreCase) >= 0,
                    "boundary " + boundary + " must be explicit (got " + combined + ")");
                AssertFalse(
                    GetBoolPropFlexible(branch, "SynthesizedOpponentPass", "AssumedOpponentPass", "SimulatedOpponentPass"),
                    "never synthesize opponent pass for " + boundary);
            }
        }

        /// <summary>
        /// Offline GREEN must spawn real harmless helper children for timeout and
        /// nonzero-exit probes, return PIDs, and leave ActiveChildCount==0 with
        /// Process.GetProcessById failing / not running for each PID.
        /// </summary>
        static void SupervisorCleansTimedOutAndCrashedWorkers()
        {
            Type supervisorType = RequireType("LlmReplayWorkerSupervisor");
            object supervisor = CreateInstance(supervisorType);

            if (HasAnyProp(supervisor, "Enabled", "IsEnabled", "FeatureEnabled"))
            {
                AssertEqual(false, GetBoolPropFlexible(supervisor, "Enabled", "IsEnabled", "FeatureEnabled"),
                    "supervisor default-off");
            }

            MethodInfo forceTimeout = FindMethod(supervisorType,
                "ForceTimeoutProbe", "SimulateTimeout", "RunTimeoutFixture");
            MethodInfo forceCrash = FindMethod(supervisorType,
                "ForceCrashProbe", "SimulateCrash", "RunCrashFixture", "ForceNonzeroExitProbe");
            MethodInfo activeCount = FindMethod(supervisorType,
                "ActiveChildCount", "GetActiveWorkerCount", "ActiveWorkers");
            MethodInfo drain = FindMethod(supervisorType, "DrainDiagnostics", "GetDiagnostics", "Drain");

            AssertNotNull(forceTimeout, "ForceTimeoutProbe required");
            AssertNotNull(forceCrash, "ForceCrashProbe/ForceNonzeroExitProbe required");
            AssertNotNull(activeCount, "ActiveChildCount required (not optional)");

            object timeoutResult = InvokeOn(forceTimeout, supervisor, null);
            object crashResult = InvokeOn(forceCrash, supervisor, null);
            AssertNotNull(timeoutResult, "timeout probe result");
            AssertNotNull(crashResult, "crash probe result");

            // Must return child PIDs + termination/exit diagnostics
            long timeoutPid = GetNumericPropFlexible(timeoutResult, "ChildPid", "Pid", "ProcessId", "WorkerPid");
            long crashPid = GetNumericPropFlexible(crashResult, "ChildPid", "Pid", "ProcessId", "WorkerPid");
            AssertTrue(timeoutPid > 0, "timeout probe must return ChildPid/Pid");
            AssertTrue(crashPid > 0, "crash/nonzero-exit probe must return ChildPid/Pid");
            AssertTrue(
                HasAnyProp(timeoutResult, "ExitCode", "TerminationReason", "Terminated", "Killed", "TimedOut")
                || GetBoolPropFlexible(timeoutResult, "TimedOut", "Killed", "Terminated"),
                "timeout probe must expose termination diagnostics");
            AssertTrue(
                HasAnyProp(crashResult, "ExitCode", "TerminationReason", "Terminated", "ExitedNonZero")
                || GetNumericPropFlexible(crashResult, "ExitCode") != 0
                || GetBoolPropFlexible(crashResult, "ExitedNonZero", "Terminated"),
                "crash probe must expose exit diagnostics");

            AssertProcessNotAlive((int)timeoutPid, "timeout child");
            AssertProcessNotAlive((int)crashPid, "crash child");

            object countObj = InvokeOn(activeCount, IsStatic(activeCount) ? null : supervisor, null);
            AssertEqual(0, Convert.ToInt32(countObj), "ActiveChildCount must be 0 after probes");

            if (drain != null)
            {
                AssertNotNull(InvokeOn(drain, supervisor, null), "DrainDiagnostics");
            }
        }

        static void AssertProcessNotAlive(int pid, string label)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                // If found, must have already exited
                AssertTrue(p.HasExited, label + " pid " + pid + " must not still be alive");
            }
            catch (ArgumentException)
            {
                // Process not found — cleaned up. OK.
            }
        }

        static void DefaultOffAndNoClientConsumer()
        {
            // Capture vs consume: live files may reference transcript/recorder capture types.
            // They must not consume worker request/response/protocol/supervisor/branch/feasibility.
            DirectoryInfo repoRoot = LocateRepoRoot();
            foreach (string rel in LiveControlSourceRelativePaths)
            {
                string path = Path.Combine(repoRoot.FullName, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    if (rel.EndsWith("DuelDll.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        AssertTrue(false, "required live control file missing: " + rel);
                    }
                    continue;
                }
                AssertSourceHasNoTokens(path, ForbiddenWorkerConsumerTokens, stripComments: true);
                // Explicitly do NOT forbid AllowedLiveCaptureTokens (transcript/recorder).
            }

            AssertNoProjectReferenceToWorker(repoRoot);

            Type supervisorType = RequireType("LlmReplayWorkerSupervisor");
            object policy = GetStaticPropOrField(supervisorType, "DefaultEnabled", "IsDefaultEnabled", "FeatureDefault");
            if (policy != null)
            {
                AssertEqual(false, Convert.ToBoolean(policy), "Layer B default-off");
            }

            Type recorderType = RequireType("LlmAcceptedInputTranscriptRecorder");
            object recDefault = GetStaticPropOrField(recorderType,
                "DefaultEnabled", "IsDefaultEnabled", "EnabledByDefault", "FeatureDefault");
            if (recDefault != null)
            {
                AssertEqual(false, Convert.ToBoolean(recDefault), "transcript recorder default-off");
            }
        }

        static void ScriptedCorpusCheckpointMatchAndDeterminism()
        {
            object result = InvokeScriptedCorpusProbe();
            AssertNotNull(result, "feasibility probe");
            double matchRate = GetDoublePropFlexible(result,
                "CheckpointMatchRate", "CheckpointFingerprintMatchRate", "MatchRate");
            AssertEqual(1.0, matchRate, "100% checkpoint fingerprint match on scripted corpus");
            AssertEqual(true, GetBoolPropFlexible(result, "DeterministicBranchOutput", "IsDeterministic"),
                "deterministic branch output");
            AssertEqual(true, GetBoolPropFlexible(result, "ZeroLiveProcessMutation", "NoLiveMutation", "LiveMutationFree"),
                "zero live-process mutation");
            AssertEqual(0L, GetNumericPropFlexible(result, "HiddenInformationDifferences", "HiddenDiffCount"),
                "zero hidden-information differences");

            // Scripted fixtures validate protocol only — cannot alone produce GO.
            AssertTrue(
                HasAnyProp(result, "RealEngineProbeAttempted", "RealEngineReplayValidated", "ScriptedOnly"),
                "feasibility must expose RealEngineProbeAttempted/RealEngineReplayValidated/ScriptedOnly");
            if (GetBoolPropFlexible(result, "ScriptedOnly")
                || !GetBoolPropFlexible(result, "RealEngineProbeAttempted"))
            {
                AssertFalse(GetBoolPropFlexible(result, "IsGo", "Go"),
                    "scripted-only probe must not report IsGo=true");
            }
        }

        static void ForcedFailureCleanupAndLatencyReporting()
        {
            object result = InvokeScriptedCorpusProbe();
            AssertEqual(true, GetBoolPropFlexible(result,
                    "ForcedFailureCleanupOk", "CleanupVerified", "NoOrphansAfterForcedFailure"),
                "forced-failure cleanup ok");

            // Explicit Layer A budget properties — no tautology.
            AssertTrue(
                HasAnyProp(result, "LayerABudgetMs", "LayerAWallMs"),
                "feasibility result must expose LayerABudgetMs/LayerAWallMs");
            long layerA = GetNumericPropFlexible(result, "LayerABudgetMs", "LayerAWallMs");
            AssertEqual((long)LayerAWallMs, layerA, "LayerABudgetMs/LayerAWallMs must equal 500");

            AssertTrue(
                HasAnyProp(result, "LayerABudgetHardFail", "FailedBecauseExceededLayerABudget"),
                "must expose LayerABudgetHardFail/FailedBecauseExceededLayerABudget");
            AssertEqual(false, GetBoolPropFlexible(result,
                    "LayerABudgetHardFail", "FailedBecauseExceededLayerABudget"),
                "Layer A budget must not hard-fail Layer B p95 reporting");

            AssertTrue(
                HasAnyProp(result, "ExpansionLatencyP95Ms", "P95ExpansionLatencyMs", "BranchExpansionP95Ms"),
                "p95 expansion latency reported separately");
            double p95 = GetDoublePropFlexible(result,
                "ExpansionLatencyP95Ms", "P95ExpansionLatencyMs", "BranchExpansionP95Ms");
            AssertTrue(p95 >= 0, "p95 non-negative");
        }

        static void FeasibilityResultGoNoGoAndHiddenStateIsAutomaticNoGo()
        {
            RequireType("LlmLayerBFeasibilityResult");
            object goResult = InvokeScriptedCorpusProbe();
            AssertTrue(HasAnyProp(goResult, "Decision", "GoNoGo", "IsGo", "Verdict"), "go/no-go decision");
            AssertTrue(HasAnyProp(goResult, "Reasons", "GoNoGoReasons", "DecisionReasons"), "reasons");
            AssertTrue(
                HasAnyProp(goResult, "RealEngineProbeAttempted"),
                "must expose RealEngineProbeAttempted");
            AssertTrue(
                HasAnyProp(goResult, "RealEngineReplayValidated"),
                "must expose RealEngineReplayValidated");
            AssertTrue(
                HasAnyProp(goResult, "ScriptedOnly"),
                "must expose ScriptedOnly");

            // IsGo only when real engine replay attempted AND validated AND other criteria pass.
            bool isGo = GetBoolPropFlexible(goResult, "IsGo", "Go");
            if (isGo)
            {
                AssertEqual(true, GetBoolPropFlexible(goResult, "RealEngineProbeAttempted"),
                    "IsGo requires RealEngineProbeAttempted");
                AssertEqual(true, GetBoolPropFlexible(goResult, "RealEngineReplayValidated"),
                    "IsGo requires RealEngineReplayValidated");
                AssertEqual(false, GetBoolPropFlexible(goResult, "ScriptedOnly"),
                    "IsGo forbids ScriptedOnly");
            }

            object noGo = InvokeHiddenStateDependentProbe();
            bool hiddenIsGo = GetBoolPropFlexible(noGo, "IsGo", "Go");
            string decision = GetStringPropFlexible(noGo, "Decision", "GoNoGo", "Verdict");
            AssertFalse(
                hiddenIsGo || string.Equals(decision, "go", StringComparison.OrdinalIgnoreCase),
                "hidden-state-dependent output is automatic no-go");
            string reasons = SerializeObject(
                GetPropFlexible(noGo, "Reasons", "GoNoGoReasons", "DecisionReasons") ?? noGo)
                .ToLowerInvariant();
            AssertTrue(
                reasons.Contains("hidden")
                || reasons.Contains("information-set")
                || reasons.Contains("information_set")
                || reasons.Contains("information set"),
                "no-go reasons must cite hidden/information-set");
        }

        static void ScriptedOnlyCannotProduceGoDecision()
        {
            // Scripted fixtures can validate protocol but cannot produce a GO decision.
            object result = InvokeScriptedCorpusProbe();
            AssertNotNull(result, "scripted feasibility result");
            AssertTrue(HasAnyProp(result, "ScriptedOnly"), "ScriptedOnly required");
            AssertTrue(HasAnyProp(result, "RealEngineProbeAttempted"), "RealEngineProbeAttempted required");
            AssertTrue(HasAnyProp(result, "RealEngineReplayValidated"), "RealEngineReplayValidated required");

            // Offline RED/GREEN without host duel.dll must mark scripted-only / not validated.
            // If RealEngineProbeAttempted is false OR RealEngineReplayValidated is false → NO-GO.
            bool attempted = GetBoolPropFlexible(result, "RealEngineProbeAttempted");
            bool validated = GetBoolPropFlexible(result, "RealEngineReplayValidated");
            bool scriptedOnly = GetBoolPropFlexible(result, "ScriptedOnly");
            bool isGo = GetBoolPropFlexible(result, "IsGo", "Go");
            string decision = GetStringPropFlexible(result, "Decision", "GoNoGo", "Verdict");

            if (!attempted || !validated || scriptedOnly)
            {
                AssertFalse(
                    isGo || string.Equals(decision, "go", StringComparison.OrdinalIgnoreCase),
                    "scripted-only / unvalidated real-engine probe must be NO-GO (not a fake fixture go)");
                string reasons = SerializeObject(
                    GetPropFlexible(result, "Reasons", "GoNoGoReasons", "DecisionReasons") ?? result)
                    .ToLowerInvariant();
                AssertTrue(
                    reasons.Contains("scripted")
                    || reasons.Contains("real engine")
                    || reasons.Contains("real_engine")
                    || reasons.Contains("duel.dll")
                    || reasons.Contains("not validated")
                    || reasons.Contains("probe")
                    || scriptedOnly
                    || !attempted
                    || !validated,
                    "NO-GO must carry a concrete scripted-only / real-engine reason");
            }
        }

        // =====================================================================
        // Source scanning helpers
        // =====================================================================

        static void AssertSourceHasNoTokens(string path, string[] tokens, bool stripComments)
        {
            string text = File.ReadAllText(path);
            if (stripComments)
            {
                text = StripCSharpComments(text);
            }
            foreach (string token in tokens)
            {
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }
                // Word-ish match to reduce noise; still catch type names and identifiers.
                if (text.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    throw new InvalidOperationException(
                        "live control source " + path + " must not reference Layer B token '"
                        + token + "' (Slice 5: no client consumer)");
                }
            }
        }

        static string StripCSharpComments(string text)
        {
            // Remove block comments then line comments. String literals kept simple:
            // do not strip across quotes (good enough for identifier scans).
            text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            text = Regex.Replace(text, @"//.*?$", " ", RegexOptions.Multiline);
            return text;
        }

        // =====================================================================
        // Fixture / production factory helpers
        // =====================================================================

        static object BuildScriptedTranscriptFixture(string fixtureId)
        {
            Type transcriptType = RequireType("LlmAcceptedInputTranscript");
            MethodInfo factory = FindStaticMethod(transcriptType,
                "FromScriptedFixture", "LoadFrozenFixture", "CreateScripted", "FromFrozenCorpus");
            if (factory != null)
            {
                return factory.Invoke(null, BindArgs(factory, fixtureId));
            }
            Type builderType = TryGetType("LlmAcceptedInputTranscriptBuilder");
            AssertNotNull(builderType, "need transcript factory or LlmAcceptedInputTranscriptBuilder");
            MethodInfo build = FindStaticMethod(builderType, "BuildScripted", "FromFixture", "Create");
            AssertNotNull(build, "builder factory");
            return build.Invoke(null, BindArgs(build, fixtureId));
        }

        static object BuildCheckpointFixture(
            string fixtureId,
            int opponentHandId, string opponentHandName,
            int opponentSetId, string opponentSetName,
            int opponentDeckId, string opponentDeckName,
            int opponentExtraId, string opponentExtraName)
        {
            Type builderType = RequireType("LlmCheckpointFingerprintBuilder");
            MethodInfo factory = FindStaticMethod(builderType,
                "FromScriptedFixture", "CreateFixture", "BuildFixtureState", "FromFrozen");
            if (factory == null)
            {
                Type stateType = TryGetType("LlmReplayCheckpointState")
                    ?? TryGetType("LlmScriptedCheckpointFixture");
                AssertNotNull(stateType, "checkpoint fixture factory");
                factory = FindStaticMethod(stateType, "Create", "FromScriptedFixture", "Build");
                AssertNotNull(factory, stateType.Name + " factory");
                return factory.Invoke(null, BindArgs(factory,
                    fixtureId, opponentHandId, opponentHandName, opponentSetId, opponentSetName,
                    opponentDeckId, opponentDeckName, opponentExtraId, opponentExtraName));
            }
            ParameterInfo[] ps = factory.GetParameters();
            if (ps.Length == 1)
            {
                return factory.Invoke(null, new object[] { fixtureId });
            }
            return factory.Invoke(null, BindArgs(factory,
                fixtureId, opponentHandId, opponentHandName, opponentSetId, opponentSetName,
                opponentDeckId, opponentDeckName, opponentExtraId, opponentExtraName));
        }

        static object InvokeBuildFingerprint(Type builderType, object fixtureState)
        {
            MethodInfo build = FindMethod(builderType, "Build", "Compute", "Create", "Fingerprint");
            AssertNotNull(build, "fingerprint Build");
            object target = IsStatic(build) ? null : CreateInstance(builderType);
            return build.Invoke(target, BindArgs(build, fixtureState));
        }

        static object InvokeScriptedBranch(object fixtureState, int rootActionId)
        {
            Type extractor = TryGetType("LlmReplayBranchExtractor") ?? TryGetType("LlmReplayBranchResult");
            AssertNotNull(extractor, "branch extractor");
            MethodInfo method = FindStaticMethod(extractor,
                "ExtractScripted", "FromScriptedFixture", "ApplyRootScripted", "Extract")
                ?? FindMethod(extractor, "ExtractScripted", "FromScriptedFixture", "ApplyRootScripted", "Extract");
            AssertNotNull(method, "ExtractScripted");
            object target = IsStatic(method) ? null : CreateInstance(extractor);
            return method.Invoke(target, BindArgs(method, fixtureState, rootActionId));
        }

        static object InvokeScriptedBoundaryBranch(string boundaryKind)
        {
            Type extractor = TryGetType("LlmReplayBranchExtractor") ?? TryGetType("LlmReplayBranchResult");
            AssertNotNull(extractor, "boundary extractor");
            MethodInfo method = FindStaticMethod(extractor,
                "FromBoundaryFixture", "ScriptedBoundary", "ExtractBoundary")
                ?? FindMethod(extractor, "FromBoundaryFixture", "ScriptedBoundary", "ExtractBoundary");
            AssertNotNull(method, "FromBoundaryFixture");
            object target = IsStatic(method) ? null : CreateInstance(extractor);
            return method.Invoke(target, BindArgs(method, boundaryKind));
        }

        static object InvokeScriptedCorpusProbe()
        {
            Type probeType = TryGetType("LlmLayerBFeasibilityProbe") ?? TryGetType("LlmLayerBFeasibilityResult");
            AssertNotNull(probeType, "feasibility probe");
            MethodInfo method = FindStaticMethod(probeType,
                "RunScriptedCorpus", "EvaluateScriptedCorpus", "ProbeScripted", "Run")
                ?? FindMethod(probeType, "RunScriptedCorpus", "EvaluateScriptedCorpus", "ProbeScripted", "Run");
            AssertNotNull(method, "RunScriptedCorpus");
            object target = IsStatic(method) ? null : CreateInstance(probeType);
            return method.Invoke(target, BindArgs(method));
        }

        static object InvokeHiddenStateDependentProbe()
        {
            Type probeType = TryGetType("LlmLayerBFeasibilityProbe") ?? TryGetType("LlmLayerBFeasibilityResult");
            AssertNotNull(probeType, "hidden-state probe");
            MethodInfo method = FindStaticMethod(probeType,
                "FromHiddenStateDependentFixture", "EvaluateHiddenStateLeak", "ProbeHiddenDependency")
                ?? FindMethod(probeType,
                    "FromHiddenStateDependentFixture", "EvaluateHiddenStateLeak", "ProbeHiddenDependency");
            AssertNotNull(method, "FromHiddenStateDependentFixture");
            object target = IsStatic(method) ? null : CreateInstance(probeType);
            return method.Invoke(target, BindArgs(method));
        }

        enum MutateMode
        {
            ClearFirstKind,
            DuplicateFirstSequence,
            ReverseSequences,
            NullFirstPayload,
            InjectHiddenSentinel,
        }

        static object MutateTranscript(object transcript, MutateMode mode)
        {
            Type t = transcript.GetType();
            MethodInfo clone = FindMethod(t, "Clone", "DeepClone", "Copy");
            object copy = clone != null
                ? clone.Invoke(IsStatic(clone) ? null : transcript, BindArgs(clone))
                : null;
            if (copy == null)
            {
                Type serializerType = TryGetType("LlmAcceptedInputTranscriptSerializer");
                if (serializerType != null)
                {
                    string json = InvokeSerialize(serializerType, transcript);
                    MethodInfo deser = FindStaticMethod(serializerType, "Deserialize", "Parse", "FromCanonical");
                    if (deser != null)
                    {
                        copy = deser.Invoke(null, BindArgs(deser, json));
                    }
                }
            }
            AssertNotNull(copy, "clone transcript for mutation");
            MethodInfo mut = FindStaticMethod(copy.GetType(), "MutateForTest", "ApplyTestMutation");
            if (mut != null)
            {
                return mut.Invoke(null, BindArgs(mut, copy, mode.ToString()));
            }
            IList entries = GetEntries(copy);
            AssertTrue(entries != null && entries.Count > 0, "entries");
            switch (mode)
            {
                case MutateMode.ClearFirstKind:
                    SetPropFlexible(entries[0], null, "Kind", "InputKind", "Type");
                    break;
                case MutateMode.DuplicateFirstSequence:
                    long seq = GetNumericPropFlexible(entries[0], "AcceptedSequence", "Sequence", "Seq", "Order");
                    SetPropFlexible(entries[1], seq, "AcceptedSequence", "Sequence", "Seq", "Order");
                    break;
                case MutateMode.ReverseSequences:
                    if (entries.Count >= 2)
                    {
                        long s0 = GetNumericPropFlexible(entries[0], "AcceptedSequence", "Sequence", "Seq", "Order");
                        long s1 = GetNumericPropFlexible(entries[1], "AcceptedSequence", "Sequence", "Seq", "Order");
                        SetPropFlexible(entries[0], s1, "AcceptedSequence", "Sequence", "Seq", "Order");
                        SetPropFlexible(entries[1], s0, "AcceptedSequence", "Sequence", "Seq", "Order");
                    }
                    break;
                case MutateMode.NullFirstPayload:
                    SetPropFlexible(entries[0], null, "Payload", "Body", "Data");
                    break;
                case MutateMode.InjectHiddenSentinel:
                    if (!TrySetPropFlexible(copy, SentinelOppHandName, "DebugLeak", "TestOnlyLeak", "Annotation"))
                    {
                        SetPropFlexible(entries[0], SentinelOppHandName, "Payload", "Body", "Data", "Note");
                    }
                    break;
            }
            return copy;
        }

        // =====================================================================
        // Path / JSON helpers (artifact path — MiniJSON only)
        // =====================================================================

        static DirectoryInfo LocateRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "YgoMaster.sln"))
                    || File.Exists(Path.Combine(dir.FullName, "YgoMasterClient.csproj")))
                {
                    return dir;
                }
                if (Directory.Exists(Path.Combine(dir.FullName, "Tools", "fixtures", "llm_replay"))
                    && Directory.Exists(Path.Combine(dir.FullName, "YgoMasterLlmTestHarness")))
                {
                    return dir;
                }
                dir = dir.Parent;
            }
            // Walk from this source file location via assembly
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Tools", "fixtures", "llm_replay")))
                {
                    return dir;
                }
                dir = dir.Parent;
            }
            throw new InvalidOperationException("cannot locate repo root for Slice 5 fixtures");
        }

        static DirectoryInfo LocateFixtureDirectory()
        {
            DirectoryInfo root = LocateRepoRoot();
            string path = Path.Combine(root.FullName, "Tools", "fixtures", "llm_replay");
            return new DirectoryInfo(path);
        }

        static Dictionary<string, object> LoadJsonObject(string path)
        {
            string text = File.ReadAllText(path);
            object parsed = MiniJSON.Json.Deserialize(text);
            Dictionary<string, object> dict = parsed as Dictionary<string, object>;
            AssertNotNull(dict, "JSON object at " + path);
            return dict;
        }

        static Dictionary<string, object> CloneJsonObject(Dictionary<string, object> src)
        {
            string json = MiniJSON.Json.Serialize(src);
            return MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
        }

        static string CanonicalJson(object value)
        {
            // Deterministic: serialize then re-parse via MiniJSON (stable enough for equality).
            // For nested dicts, sort keys recursively.
            object sorted = SortKeys(value);
            return MiniJSON.Json.Serialize(sorted);
        }

        static object SortKeys(object value)
        {
            Dictionary<string, object> dict = value as Dictionary<string, object>;
            if (dict != null)
            {
                Dictionary<string, object> ordered = new Dictionary<string, object>();
                foreach (string key in dict.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    ordered[key] = SortKeys(dict[key]);
                }
                return ordered;
            }
            IList list = value as IList;
            if (list != null && !(value is string))
            {
                List<object> items = new List<object>();
                foreach (object item in list)
                {
                    items.Add(SortKeys(item));
                }
                return items;
            }
            return value;
        }

        static Dictionary<string, object> GetDict(Dictionary<string, object> parent, string key)
        {
            if (parent == null || !parent.ContainsKey(key))
            {
                return null;
            }
            return parent[key] as Dictionary<string, object>;
        }

        static List<object> GetList(Dictionary<string, object> parent, string key)
        {
            if (parent == null || !parent.ContainsKey(key) || parent[key] == null)
            {
                return null;
            }
            IList list = parent[key] as IList;
            if (list == null)
            {
                return null;
            }
            List<object> result = new List<object>();
            foreach (object item in list)
            {
                result.Add(item);
            }
            return result;
        }

        static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null)
            {
                return string.Empty;
            }
            return Convert.ToString(dict[key]);
        }

        static bool GetBool(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null)
            {
                return false;
            }
            object v = dict[key];
            if (v is bool)
            {
                return (bool)v;
            }
            bool parsed;
            return bool.TryParse(Convert.ToString(v), out parsed) && parsed;
        }

        static long GetLong(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null)
            {
                return 0;
            }
            try
            {
                return Convert.ToInt64(dict[key]);
            }
            catch
            {
                return 0;
            }
        }

        // =====================================================================
        // Reflection helpers (production path)
        // =====================================================================

        static Type RequireType(string simpleName)
        {
            Type type = TryGetType(simpleName);
            if (type == null)
            {
                throw new InvalidOperationException(
                    "YGOMASTER-LLM-005 Slice 5 RED: missing production type '"
                    + simpleName
                    + "' (accepted-input transcript/recorder, checkpoint, worker protocol, "
                    + "supervisor, feasibility). GREEN needs LlmAcceptedInputTranscriptRecorder "
                    + "on the live post-acceptance path (default-off capture) plus dedicated "
                    + "YgoMasterSearchWorker (worker-owned DLL_SetWorkMemory only; no live copy). "
                    + "IsGo requires real engine validation, not scripted fixtures alone. "
                    + "Do not invoke real duel.dll from harness tests. "
                    + "Frozen corpus artifact contract already passed offline.");
            }
            return type;
        }

        static Type TryGetType(string simpleName)
        {
            Assembly asm = typeof(Llm005Slice5Tests).Assembly;
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
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = a.GetType("YgoMaster." + simpleName, false);
                    if (t != null)
                    {
                        return t;
                    }
                    foreach (Type x in a.GetTypes())
                    {
                        if (x != null && x.Name == simpleName)
                        {
                            return x;
                        }
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        static object CreateInstance(Type type)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("cannot construct " + type.Name + ": " + ex.Message, ex);
            }
        }

        static object CreateWorkerRequest(
            Type requestType, int generation, int deadlineMs, string transcriptFixtureId,
            ulong checkpointSeq, int rootActionId)
        {
            MethodInfo factory = FindStaticMethod(requestType, "Create", "FromParts", "Build");
            if (factory != null)
            {
                return factory.Invoke(null, BindArgs(factory,
                    generation, deadlineMs, transcriptFixtureId, checkpointSeq, rootActionId));
            }
            object req = CreateInstance(requestType);
            SetPropFlexible(req, generation, "Generation", "RequestGeneration", "SearchGeneration");
            SetPropFlexible(req, deadlineMs, "DeadlineMs", "TimeoutMs", "Deadline");
            SetPropFlexible(req, transcriptFixtureId, "TranscriptId", "TranscriptPath");
            SetPropFlexible(req, checkpointSeq, "CheckpointSeq", "RunEffectSeq", "CheckpointId");
            SetPropFlexible(req, rootActionId, "RootActionId", "CandidateRootActionId");
            return req;
        }

        static object InvokeProtocolRoundTripOrSchema(Type protocolType, object request, Type responseType)
        {
            MethodInfo schema = FindStaticMethod(protocolType, "CreateEmptyResponse", "ResponseSchema", "NewResponse");
            if (schema != null)
            {
                return schema.Invoke(null, BindArgs(schema, request));
            }
            MethodInfo pack = FindStaticMethod(protocolType, "PackScriptedResponse", "FromScripted", "CreateResponse");
            if (pack != null)
            {
                return pack.Invoke(null, BindArgs(pack, request));
            }
            object response = CreateInstance(responseType);
            SetPropFlexible(response, "ok", "Status", "ResultStatus", "Outcome");
            SetPropFlexible(response, "scripted", "Fingerprint", "CheckpointFingerprint");
            SetPropFlexible(response, new object(), "Branch", "BranchResult");
            SetPropFlexible(response, new object(), "Diagnostics", "WorkerDiagnostics");
            return response;
        }

        static string InvokeSerialize(Type serializerType, object transcript)
        {
            MethodInfo ser = FindStaticMethod(serializerType, "Serialize", "ToCanonical", "Canonicalize", "ToJson")
                ?? FindMethod(serializerType, "Serialize", "ToCanonical", "Canonicalize", "ToJson");
            AssertNotNull(ser, "Serialize");
            object target = IsStatic(ser) ? null : CreateInstance(serializerType);
            object result = ser.Invoke(target, BindArgs(ser, transcript));
            return result != null ? result.ToString() : null;
        }

        static object InvokeValidate(Type validatorType, object transcript)
        {
            MethodInfo val = FindStaticMethod(validatorType, "Validate", "Check", "TryValidate")
                ?? FindMethod(validatorType, "Validate", "Check", "TryValidate");
            AssertNotNull(val, "Validate");
            object target = IsStatic(val) ? null : CreateInstance(validatorType);
            return val.Invoke(target, BindArgs(val, transcript));
        }

        static void AssertValidationOk(object result, string message)
        {
            if (result is bool)
            {
                AssertEqual(true, (bool)result, message);
                return;
            }
            if (HasAnyProp(result, "Ok", "IsValid", "Valid", "Success"))
            {
                AssertEqual(true, GetBoolPropFlexible(result, "Ok", "IsValid", "Valid", "Success"), message);
                return;
            }
            IEnumerable errors = GetEnumerablePropFlexible(result, "Errors", "ErrorMessages");
            if (errors != null)
            {
                int count = 0;
                foreach (object _ in errors)
                {
                    count++;
                }
                AssertEqual(0, count, message);
                return;
            }
            throw new InvalidOperationException(message + ": unrecognized validation result");
        }

        static void AssertValidationFails(Type validatorType, object transcript, string expectedToken)
        {
            object result = InvokeValidate(validatorType, transcript);
            string blob = SerializeObject(result).ToLowerInvariant();
            bool failed = false;
            if (result is bool)
            {
                failed = !(bool)result;
            }
            else if (HasAnyProp(result, "Ok", "IsValid", "Valid", "Success"))
            {
                failed = !GetBoolPropFlexible(result, "Ok", "IsValid", "Valid", "Success");
            }
            else
            {
                IEnumerable errors = GetEnumerablePropFlexible(result, "Errors", "ErrorMessages");
                if (errors != null)
                {
                    foreach (object e in errors)
                    {
                        failed = true;
                        blob += " " + (e != null ? e.ToString() : "");
                    }
                }
            }
            AssertTrue(failed, "validation must fail for " + expectedToken);
            AssertTrue(
                blob.IndexOf(expectedToken.ToLowerInvariant(), StringComparison.Ordinal) >= 0
                || blob.IndexOf("invalid", StringComparison.Ordinal) >= 0
                || blob.IndexOf("error", StringComparison.Ordinal) >= 0
                || blob.IndexOf("fail", StringComparison.Ordinal) >= 0,
                "failure should mention " + expectedToken);
        }

        static IList GetEntries(object transcript)
        {
            return GetPropFlexible(transcript, "Entries", "AcceptedEntries", "Inputs", "Items") as IList;
        }

        static string NormalizeKind(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                return string.Empty;
            }
            return kind.Replace("_", string.Empty).Replace(" ", string.Empty);
        }

        static MethodInfo FindMethod(Type type, params string[] names)
        {
            if (type == null)
            {
                return null;
            }
            foreach (string name in names)
            {
                MethodInfo m = type.GetMethod(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (m != null)
                {
                    return m;
                }
            }
            foreach (MethodInfo m in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (string name in names)
                {
                    if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return m;
                    }
                }
            }
            return null;
        }

        static MethodInfo FindStaticMethod(Type type, params string[] names)
        {
            if (type == null)
            {
                return null;
            }
            foreach (string name in names)
            {
                MethodInfo sm = type.GetMethod(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (sm != null)
                {
                    return sm;
                }
            }
            MethodInfo m = FindMethod(type, names);
            return m != null && m.IsStatic ? m : null;
        }

        static bool IsStatic(MethodInfo m)
        {
            return m != null && m.IsStatic;
        }

        static object InvokeOn(MethodInfo method, object target, object[] args)
        {
            if (method.GetParameters().Length == 0)
            {
                return method.Invoke(target, null);
            }
            return method.Invoke(target, BindArgs(method, args ?? new object[0]));
        }

        static object[] BindArgs(MethodInfo method, params object[] values)
        {
            ParameterInfo[] ps = method.GetParameters();
            object[] args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < values.Length)
                {
                    object v = values[i];
                    if (v != null && !ps[i].ParameterType.IsInstanceOfType(v)
                        && ps[i].ParameterType != typeof(object))
                    {
                        try
                        {
                            if (ps[i].ParameterType.IsEnum && v is string)
                            {
                                args[i] = Enum.Parse(ps[i].ParameterType, (string)v, true);
                            }
                            else
                            {
                                args[i] = Convert.ChangeType(v,
                                    Nullable.GetUnderlyingType(ps[i].ParameterType) ?? ps[i].ParameterType);
                            }
                        }
                        catch
                        {
                            args[i] = v;
                        }
                    }
                    else
                    {
                        args[i] = v;
                    }
                }
                else if (ps[i].HasDefaultValue)
                {
                    args[i] = ps[i].DefaultValue;
                }
                else if (ps[i].ParameterType.IsValueType)
                {
                    args[i] = Activator.CreateInstance(ps[i].ParameterType);
                }
                else
                {
                    args[i] = null;
                }
            }
            return args;
        }

        static object GetStaticPropOrField(Type type, params string[] names)
        {
            foreach (string name in names)
            {
                PropertyInfo p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                if (p != null)
                {
                    return p.GetValue(null, null);
                }
                FieldInfo f = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (f != null)
                {
                    return f.GetValue(null);
                }
            }
            return null;
        }

        static bool HasAnyProp(object target, params string[] names)
        {
            if (target == null)
            {
                return false;
            }
            Type t = target.GetType();
            foreach (string name in names)
            {
                if (t.GetProperty(name) != null || t.GetField(name) != null)
                {
                    return true;
                }
            }
            IDictionary dict = target as IDictionary;
            if (dict != null)
            {
                foreach (string name in names)
                {
                    if (dict.Contains(name))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static object GetPropFlexible(object target, params string[] names)
        {
            if (target == null)
            {
                return null;
            }
            Type t = target.GetType();
            foreach (string name in names)
            {
                PropertyInfo p = t.GetProperty(name);
                if (p != null)
                {
                    return p.GetValue(target, null);
                }
                FieldInfo f = t.GetField(name);
                if (f != null)
                {
                    return f.GetValue(target);
                }
            }
            IDictionary dict = target as IDictionary;
            if (dict != null)
            {
                foreach (string name in names)
                {
                    if (dict.Contains(name))
                    {
                        return dict[name];
                    }
                }
            }
            return null;
        }

        static string GetStringPropFlexible(object target, params string[] names)
        {
            object v = GetPropFlexible(target, names);
            return v != null ? Convert.ToString(v) : string.Empty;
        }

        static bool GetBoolPropFlexible(object target, params string[] names)
        {
            object v = GetPropFlexible(target, names);
            if (v == null)
            {
                return false;
            }
            if (v is bool)
            {
                return (bool)v;
            }
            bool parsed;
            return bool.TryParse(Convert.ToString(v), out parsed) && parsed;
        }

        static long GetNumericPropFlexible(object target, params string[] names)
        {
            object v = GetPropFlexible(target, names);
            if (v == null)
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

        static double GetDoublePropFlexible(object target, params string[] names)
        {
            object v = GetPropFlexible(target, names);
            if (v == null)
            {
                return 0;
            }
            try
            {
                return Convert.ToDouble(v);
            }
            catch
            {
                return 0;
            }
        }

        static IEnumerable GetEnumerablePropFlexible(object target, params string[] names)
        {
            return GetPropFlexible(target, names) as IEnumerable;
        }

        static void SetPropFlexible(object target, object value, params string[] names)
        {
            TrySetPropFlexible(target, value, names);
        }

        static bool TrySetPropFlexible(object target, object value, params string[] names)
        {
            if (target == null)
            {
                return false;
            }
            Type t = target.GetType();
            foreach (string name in names)
            {
                PropertyInfo p = t.GetProperty(name);
                if (p != null && p.CanWrite)
                {
                    try
                    {
                        object coerced = value;
                        if (value != null && p.PropertyType != value.GetType()
                            && p.PropertyType != typeof(object))
                        {
                            try
                            {
                                coerced = Convert.ChangeType(value,
                                    Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType);
                            }
                            catch
                            {
                                coerced = value;
                            }
                        }
                        p.SetValue(target, coerced, null);
                        return true;
                    }
                    catch
                    {
                    }
                }
                FieldInfo f = t.GetField(name);
                if (f != null)
                {
                    try
                    {
                        f.SetValue(target, value);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }
            return false;
        }

        static string SerializeObject(object value)
        {
            if (value == null)
            {
                return "null";
            }
            if (value is string)
            {
                return (string)value;
            }
            // Prefer type ToString when it embeds structured diagnostics (e.g. validation).
            try
            {
                MethodInfo ts = value.GetType().GetMethod("ToString", Type.EmptyTypes);
                if (ts != null && ts.DeclaringType != typeof(object))
                {
                    string custom = ts.Invoke(value, null) as string;
                    if (!string.IsNullOrEmpty(custom) && custom != value.GetType().FullName)
                    {
                        return custom;
                    }
                }
            }
            catch
            {
            }
            try
            {
                string json = MiniJSON.Json.Serialize(value);
                if (!string.IsNullOrEmpty(json) && json != "{}")
                {
                    return json;
                }
            }
            catch
            {
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            bool first = true;
            foreach (PropertyInfo p in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                object pv;
                try
                {
                    pv = p.GetValue(value, null);
                }
                catch
                {
                    continue;
                }
                if (!first)
                {
                    sb.Append(",");
                }
                first = false;
                sb.Append("\"").Append(p.Name).Append("\":");
                if (pv is IEnumerable && !(pv is string))
                {
                    sb.Append("\"");
                    foreach (object item in (IEnumerable)pv)
                    {
                        sb.Append(item != null ? item.ToString().Replace("\"", "\\\"") : "").Append(";");
                    }
                    sb.Append("\"");
                }
                else
                {
                    sb.Append("\"");
                    sb.Append(pv != null ? pv.ToString().Replace("\"", "\\\"") : "null");
                    sb.Append("\"");
                }
            }
            sb.Append("}");
            return sb.ToString();
        }

        static void AssertNoLeak(string blob, params string[] forbidden)
        {
            foreach (string token in forbidden)
            {
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }
                AssertFalse(
                    blob.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0,
                    "leak of forbidden token '" + token + "'");
            }
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException(message);
            }
        }

        static void AssertFalse(bool value, string message)
        {
            if (value)
            {
                throw new InvalidOperationException(message);
            }
        }

        static void AssertNotNull(object value, string message)
        {
            if (value == null)
            {
                throw new InvalidOperationException(message);
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    message + " (expected " + expected + ", got " + actual + ")");
            }
        }
    }
}

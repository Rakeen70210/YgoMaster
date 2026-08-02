using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Independent-review regressions for Slice 5 first GREEN rejection.
    /// </summary>
    static class Llm005Slice5ReviewRegressionTests
    {
        const long ExpectedDuelDllBytes = 19443200;

        public static void RunAll()
        {
            RealDuelDllPathIsResolvedFromRepoParentMasterDuelData();
            RealEngineProbeAttemptedWhenDllExistsViaWorkerNotForgeableMarker();
            ClientAssemblyDoesNotContainWorkerResearchTypes();
            RecorderDefaultOffAndSettingsLifecycle();
            RecorderRequiresCompleteDuelSettingsOrRecordsIncompleteReason();
            RecorderFlushExportsDeterministicJson();
            NativeCallOrderingRecordsAfterSuccessfulOriginal();
            FailedCommitGoesToExclusionNotAcceptedEntries();
            WorkerProbeRealEngineEmitsStructuredJson();
        }

        static void RealDuelDllPathIsResolvedFromRepoParentMasterDuelData()
        {
            string path = LlmDuelDllLocator.ResolveAuthoritativeDuelDllPath();
            AssertTrue(!string.IsNullOrEmpty(path) && File.Exists(path),
                "must resolve real duel.dll (repo parent masterduel_Data/Plugins/x86_64/duel.dll)");
            long len = new FileInfo(path).Length;
            AssertEqual(ExpectedDuelDllBytes, len,
                "resolved duel.dll size must match Master Duel plugin (19443200)");
            AssertTrue(
                path.Replace('\\', '/').IndexOf("masterduel_Data/Plugins/x86_64/duel.dll",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || path.EndsWith("duel.dll", StringComparison.OrdinalIgnoreCase),
                "path should be the Master Duel Plugins duel.dll");
        }

        static void RealEngineProbeAttemptedWhenDllExistsViaWorkerNotForgeableMarker()
        {
            // Forgeable marker must not exist as validation path.
            string root = LlmSlice5Paths.FindRepoRoot();
            string marker = Path.Combine(root, "YgoMasterSearchWorker", "last_real_engine_probe.json");
            AssertFalse(File.Exists(marker) && File.ReadAllText(marker).IndexOf("\"validated\":true", StringComparison.OrdinalIgnoreCase) >= 0
                && !File.Exists(LlmDuelDllLocator.ResolveAuthoritativeDuelDllPath() ?? ""),
                "validation must not rely on forgeable last_real_engine_probe.json");

            // Source must not reference the forgeable marker path.
            string feasibilitySrc = Path.Combine(root, "YgoMasterServer", "Llm", "LlmLayerBFeasibility.cs");
            if (File.Exists(feasibilitySrc))
            {
                string text = File.ReadAllText(feasibilitySrc);
                AssertFalse(
                    text.IndexOf("last_real_engine_probe.json", StringComparison.OrdinalIgnoreCase) >= 0,
                    "LlmLayerBFeasibility must not use forgeable last_real_engine_probe.json");
            }

            bool attempted;
            bool validated;
            string reason;
            Dictionary<string, object> detail;
            LlmLayerBFeasibilityProbe.TryRealEngineReplayProbe(out attempted, out validated, out reason, out detail);
            AssertTrue(File.Exists(LlmDuelDllLocator.ResolveAuthoritativeDuelDllPath() ?? ""),
                "dll must exist for this regression");
            AssertTrue(attempted,
                "RealEngineProbeAttempted must be true when authoritative duel.dll exists (got reason=" + reason + ")");
            // validated may be false on PE/Linux — only current supervised worker can set true.
            if (validated)
            {
                AssertTrue(detail != null && detail.ContainsKey("worker_stdout"),
                    "validated requires current worker invocation evidence");
            }
            AssertFalse(
                reason != null && reason.IndexOf("last_real_engine_probe", StringComparison.OrdinalIgnoreCase) >= 0,
                "reason must not cite forgeable marker file");
        }

        static void ClientAssemblyDoesNotContainWorkerResearchTypes()
        {
            string root = LlmSlice5Paths.FindRepoRoot();
            // Prefer freshly built redirected client binary if present.
            string[] candidates = new string[]
            {
                "/tmp/ygomaster-build/solution/YgoMasterClient.exe",
                Path.Combine(root, "YgoMasterClient.exe"),
                Path.Combine(root, "bin", "YgoMasterClient.exe"),
            };
            string clientPath = candidates.FirstOrDefault(File.Exists);
            if (clientPath == null)
            {
                // Fall back: inspect csproj includes only (assembly may be absent on dev box).
                string csproj = Path.Combine(root, "YgoMasterClient.csproj");
                string text = File.ReadAllText(csproj);
                string[] forbidden = new string[]
                {
                    "LlmCheckpointFingerprint.cs",
                    "LlmSearchWorkerProtocol.cs",
                    "LlmReplayBranchResult.cs",
                    "LlmReplayWorkerSupervisor.cs",
                    "LlmLayerBFeasibility.cs",
                };
                foreach (string f in forbidden)
                {
                    AssertFalse(text.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0,
                        "YgoMasterClient.csproj must not compile worker research type " + f);
                }
                return;
            }

            // Metadata check without fully loading game deps: scan IL strings via file bytes for type names.
            byte[] bytes = File.ReadAllBytes(clientPath);
            string ascii = Encoding.ASCII.GetString(bytes);
            string[] workerTypes = new string[]
            {
                "LlmCheckpointFingerprint",
                "LlmSearchWorkerProtocol",
                "LlmSearchWorkerRequest",
                "LlmReplayBranchResult",
                "LlmReplayWorkerSupervisor",
                "LlmLayerBFeasibilityResult",
                "LlmLayerBFeasibilityProbe",
            };
            foreach (string t in workerTypes)
            {
                AssertFalse(ascii.IndexOf(t, StringComparison.Ordinal) >= 0,
                    "client assembly must not contain worker type metadata: " + t + " (" + clientPath + ")");
            }
            // Capture types may remain.
            AssertTrue(
                ascii.IndexOf("LlmAcceptedInputTranscript", StringComparison.Ordinal) >= 0
                || ascii.IndexOf("LlmAcceptedInputTranscriptRecorder", StringComparison.Ordinal) >= 0,
                "client should still contain transcript capture types");
        }

        static void RecorderDefaultOffAndSettingsLifecycle()
        {
            LlmAcceptedInputTranscriptRecorder.ResetForTests();
            AssertEqual(false, LlmAcceptedInputTranscriptRecorder.DefaultEnabled, "DefaultEnabled false");
            AssertEqual(false, LlmAcceptedInputTranscriptRecorder.Instance.Enabled, "instance starts off");

            // Simulate settings lifecycle
            LlmAcceptedInputTranscriptRecorder.Instance.BeginDuelFromSettings(new Dictionary<string, object>()
            {
                { "seed", 123u },
                { "first_player", 1 },
                { "limited_type", 0 },
                { "decks", new Dictionary<string, object>()
                    {
                        { "player0_main", new object[] { 1, 2 } },
                        { "player1_main", new object[] { 3, 4 } },
                        { "player0_extra", new object[] { 10 } },
                        { "player1_extra", new object[] { 11 } },
                    }
                },
            }, enabledFromClientSettings: false);
            AssertEqual(false, LlmAcceptedInputTranscriptRecorder.Instance.Enabled,
                "BeginDuelFromSettings respects enabledFromClientSettings=false");
            LlmAcceptedInputTranscriptRecorder.Instance.RecordDoCommand(1, 1, 1, 0, 0, 5);
            AssertEqual(0, LlmAcceptedInputTranscriptRecorder.Instance.Transcript.Entries.Count,
                "disabled recorder must not accept entries");

            LlmAcceptedInputTranscriptRecorder.Instance.BeginDuelFromSettings(new Dictionary<string, object>()
            {
                { "seed", 123u },
                { "first_player", 1 },
                { "limited_type", 0 },
                { "decks", new Dictionary<string, object>()
                    {
                        { "player0_main", new object[] { 1 } },
                        { "player1_main", new object[] { 2 } },
                        { "player0_extra", new object[] { } },
                        { "player1_extra", new object[] { } },
                    }
                },
            }, enabledFromClientSettings: true);
            AssertEqual(true, LlmAcceptedInputTranscriptRecorder.Instance.Enabled, "enabled from settings");
            AssertEqual(0, LlmAcceptedInputTranscriptRecorder.Instance.Transcript.Entries.Count,
                "BeginDuel resets entries");
            AssertTrue(LlmAcceptedInputTranscriptRecorder.Instance.TranscriptSettingsComplete,
                "complete settings marked complete");
        }

        static void RecorderRequiresCompleteDuelSettingsOrRecordsIncompleteReason()
        {
            LlmAcceptedInputTranscriptRecorder.ResetForTests();
            LlmAcceptedInputTranscriptRecorder.Instance.BeginDuelFromSettings(
                new Dictionary<string, object>() { { "seed", 1 } },
                enabledFromClientSettings: true);
            AssertEqual(false, LlmAcceptedInputTranscriptRecorder.Instance.TranscriptSettingsComplete,
                "incomplete settings");
            AssertTrue(
                !string.IsNullOrEmpty(LlmAcceptedInputTranscriptRecorder.Instance.IncompleteSettingsReason)
                || LlmAcceptedInputTranscriptRecorder.Instance.ExclusionDiagnostics.Count > 0,
                "must record structured incomplete/no-go reason for missing duel settings");
        }

        static void RecorderFlushExportsDeterministicJson()
        {
            LlmAcceptedInputTranscriptRecorder.ResetForTests();
            LlmAcceptedInputTranscriptRecorder.Instance.BeginDuelFromSettings(CompleteSettings(), true);
            LlmAcceptedInputTranscriptRecorder.Instance.RecordDoCommand(1, 9, 1, 13, 0, 5);
            string path = Path.Combine(Path.GetTempPath(), "llm005_s5_flush_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                string a = LlmAcceptedInputTranscriptRecorder.Instance.FlushToFile(path);
                string b = LlmAcceptedInputTranscriptRecorder.Instance.FlushToFile(path);
                AssertTrue(File.Exists(path), "flush creates file");
                AssertEqual(a, b, "flush serialization deterministic");
                AssertTrue(a.IndexOf("DoCommand", StringComparison.Ordinal) >= 0
                    || a.IndexOf("\"kind\"", StringComparison.Ordinal) >= 0,
                    "flush JSON contains accepted entries");
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        static void NativeCallOrderingRecordsAfterSuccessfulOriginal()
        {
            var order = new List<string>();
            LlmAcceptedInputTranscriptRecorder.ResetForTests();
            LlmAcceptedInputTranscriptRecorder.Instance.Enabled = true;
            LlmAcceptedInputTranscriptRecorder.Instance.BeginDuelFromSettings(CompleteSettings(), true);

            // Simulated void path: original then record
            Action native = () => order.Add("native");
            Action record = () =>
            {
                order.Add("record");
                LlmAcceptedInputTranscriptRecorder.Instance.RecordDoCommand(1, 1, 1, 0, 0, 1);
            };
            LlmAcceptedInputRecordOrder.InvokeVoidOriginalThenRecord(native, record);
            AssertEqual("native", order[0], "native first");
            AssertEqual("record", order[1], "record after native");
            AssertEqual(1, LlmAcceptedInputTranscriptRecorder.Instance.Transcript.Entries.Count, "one accepted");

            // Simulated cancel return path: only record after return
            order.Clear();
            LlmAcceptedInputTranscriptRecorder.ResetForTests();
            LlmAcceptedInputTranscriptRecorder.Instance.BeginDuelFromSettings(CompleteSettings(), true);
            LlmAcceptedInputTranscriptRecorder.Instance.Enabled = true;
            Func<int> cancelNative = () =>
            {
                order.Add("native_cancel");
                return 0;
            };
            int ret = LlmAcceptedInputRecordOrder.InvokeReturningThenRecord(
                cancelNative,
                r =>
                {
                    order.Add("record_cancel");
                    LlmAcceptedInputTranscriptRecorder.Instance.RecordCancel(1, 2, false);
                });
            AssertEqual(0, ret, "return preserved");
            AssertEqual("native_cancel", order[0], "cancel native first");
            AssertEqual("record_cancel", order[1], "record after return");
        }

        static void FailedCommitGoesToExclusionNotAcceptedEntries()
        {
            LlmAcceptedInputTranscriptRecorder.ResetForTests();
            LlmAcceptedInputTranscriptRecorder.Instance.BeginDuelFromSettings(CompleteSettings(), true);
            LlmAcceptedInputTranscriptRecorder.Instance.Enabled = true;
            LlmAcceptedInputTranscriptRecorder.Instance.NoteRejectedAttempt("DoCommand", 99, "commit_failed");
            AssertEqual(0, LlmAcceptedInputTranscriptRecorder.Instance.Transcript.Entries.Count,
                "failed commit must not enter accepted entries");
            AssertTrue(LlmAcceptedInputTranscriptRecorder.Instance.ExclusionDiagnostics.Count >= 1,
                "failed commit in exclusion diagnostics");
        }

        static void WorkerProbeRealEngineEmitsStructuredJson()
        {
            string root = LlmSlice5Paths.FindRepoRoot();
            string workerDll = "/tmp/ygomaster-build/worker/YgoMasterSearchWorker.dll";
            if (!File.Exists(workerDll))
            {
                // Build worker if needed
                Process build = Process.Start(new ProcessStartInfo
                {
                    FileName = ResolveDotnetPath(),
                    Arguments = "build \"" + Path.Combine(root, "YgoMasterSearchWorker", "YgoMasterSearchWorker.csproj")
                        + "\" -v:minimal -p:OutputPath=/tmp/ygomaster-build/worker/",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = root,
                });
                if (build != null)
                {
                    build.WaitForExit(120000);
                }
            }
            AssertTrue(File.Exists(workerDll), "worker dll must build for probe test");
            string dllPath = LlmDuelDllLocator.ResolveAuthoritativeDuelDllPath();
            AssertTrue(File.Exists(dllPath), "authoritative duel.dll");

            string dotnet = ResolveDotnetPath();
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = dotnet,
                Arguments = "\"" + workerDll + "\" --probe-real-engine --dll \"" + dllPath + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = root,
            };
            Process p = Process.Start(psi);
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
            AssertTrue(stdout.Trim().Length > 0, "probe must emit stdout JSON (stderr=" + stderr + ")");
            Dictionary<string, object> json = MiniJSON.Json.Deserialize(stdout.Trim()) as Dictionary<string, object>;
            AssertTrue(json != null, "probe stdout must be JSON object: " + stdout);
            AssertTrue(json.ContainsKey("attempted") || json.ContainsKey("RealEngineProbeAttempted"),
                "JSON must include attempted");
            object att = json.ContainsKey("attempted") ? json["attempted"] : json["RealEngineProbeAttempted"];
            AssertEqual(true, Convert.ToBoolean(att), "attempted true when dll path exists");
            AssertTrue(json.ContainsKey("validated") || json.ContainsKey("RealEngineReplayValidated"),
                "JSON must include validated");
            AssertTrue(json.ContainsKey("dll_path") || json.ContainsKey("DllPath"),
                "JSON must include dll_path");
            AssertTrue(json.ContainsKey("reason") || json.ContainsKey("Reason"),
                "JSON must include reason");
        }

        static Dictionary<string, object> CompleteSettings()
        {
            return new Dictionary<string, object>()
            {
                { "seed", 0xA11CE5u },
                { "first_player", 1 },
                { "limited_type", 0 },
                { "decks", new Dictionary<string, object>()
                    {
                        { "player0_main", new object[] { 1, 2, 3 } },
                        { "player1_main", new object[] { 4, 5, 6 } },
                        { "player0_extra", new object[] { 10 } },
                        { "player1_extra", new object[] { 11 } },
                    }
                },
            };
        }

        static string ResolveDotnetPath()
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
            string[] candidates = new string[]
            {
                Path.Combine(home, ".dotnet", "dotnet"),
                "/usr/share/dotnet/dotnet",
                "dotnet",
            };
            foreach (string c in candidates)
            {
                if (c == "dotnet" || File.Exists(c))
                {
                    return c;
                }
            }
            return "dotnet";
        }

        static void AssertTrue(bool v, string m)
        {
            if (!v) throw new InvalidOperationException(m);
        }

        static void AssertFalse(bool v, string m)
        {
            if (v) throw new InvalidOperationException(m);
        }

        static void AssertEqual<T>(T e, T a, string m)
        {
            if (!Equals(e, a))
                throw new InvalidOperationException(m + " (expected " + e + ", got " + a + ")");
        }
    }
}

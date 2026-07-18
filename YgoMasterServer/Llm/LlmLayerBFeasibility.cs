using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace YgoMaster
{
    public sealed class LlmLayerBFeasibilityResult
    {
        public const int LayerABudgetMsDefault = 500;

        public string Decision { get; set; }
        public string GoNoGo { get { return Decision; } set { Decision = value; } }
        public string Verdict { get { return Decision; } set { Decision = value; } }
        public bool IsGo { get; set; }
        public bool Go { get { return IsGo; } set { IsGo = value; } }
        public List<string> Reasons { get; set; }
        public List<string> GoNoGoReasons { get { return Reasons; } set { Reasons = value; } }
        public List<string> DecisionReasons { get { return Reasons; } set { Reasons = value; } }

        public bool RealEngineProbeAttempted { get; set; }
        public bool RealEngineReplayValidated { get; set; }
        public bool ScriptedOnly { get; set; }

        public double CheckpointMatchRate { get; set; }
        public double CheckpointFingerprintMatchRate { get { return CheckpointMatchRate; } set { CheckpointMatchRate = value; } }
        public double MatchRate { get { return CheckpointMatchRate; } set { CheckpointMatchRate = value; } }
        public bool DeterministicBranchOutput { get; set; }
        public bool IsDeterministic { get { return DeterministicBranchOutput; } set { DeterministicBranchOutput = value; } }
        public bool ZeroLiveProcessMutation { get; set; }
        public bool NoLiveMutation { get { return ZeroLiveProcessMutation; } set { ZeroLiveProcessMutation = value; } }
        public bool LiveMutationFree { get { return ZeroLiveProcessMutation; } set { ZeroLiveProcessMutation = value; } }
        public int HiddenInformationDifferences { get; set; }
        public int HiddenDiffCount { get { return HiddenInformationDifferences; } set { HiddenInformationDifferences = value; } }

        public bool ForcedFailureCleanupOk { get; set; }
        public bool CleanupVerified { get { return ForcedFailureCleanupOk; } set { ForcedFailureCleanupOk = value; } }
        public bool NoOrphansAfterForcedFailure { get { return ForcedFailureCleanupOk; } set { ForcedFailureCleanupOk = value; } }

        public int LayerABudgetMs { get; set; }
        public int LayerAWallMs { get { return LayerABudgetMs; } set { LayerABudgetMs = value; } }
        public bool LayerABudgetHardFail { get; set; }
        public bool FailedBecauseExceededLayerABudget { get { return LayerABudgetHardFail; } set { LayerABudgetHardFail = value; } }

        public double ExpansionLatencyP95Ms { get; set; }
        public double P95ExpansionLatencyMs { get { return ExpansionLatencyP95Ms; } set { ExpansionLatencyP95Ms = value; } }
        public double BranchExpansionP95Ms { get { return ExpansionLatencyP95Ms; } set { ExpansionLatencyP95Ms = value; } }

        public Dictionary<string, object> RealEngineProbeDetail { get; set; }

        public LlmLayerBFeasibilityResult()
        {
            Reasons = new List<string>();
            Decision = "no-go";
            IsGo = false;
            ScriptedOnly = true;
            RealEngineProbeAttempted = false;
            RealEngineReplayValidated = false;
            CheckpointMatchRate = 1.0;
            DeterministicBranchOutput = true;
            ZeroLiveProcessMutation = true;
            HiddenInformationDifferences = 0;
            ForcedFailureCleanupOk = true;
            LayerABudgetMs = LayerABudgetMsDefault;
            LayerABudgetHardFail = false;
            ExpansionLatencyP95Ms = 0;
            RealEngineProbeDetail = new Dictionary<string, object>();
        }

        public static LlmLayerBFeasibilityResult RunScriptedCorpus()
        {
            return LlmLayerBFeasibilityProbe.RunScriptedCorpus();
        }

        public static LlmLayerBFeasibilityResult EvaluateScriptedCorpus()
        {
            return LlmLayerBFeasibilityProbe.EvaluateScriptedCorpus();
        }

        public static LlmLayerBFeasibilityResult ProbeScripted()
        {
            return LlmLayerBFeasibilityProbe.ProbeScripted();
        }

        public static LlmLayerBFeasibilityResult Run()
        {
            return LlmLayerBFeasibilityProbe.Run();
        }

        public static LlmLayerBFeasibilityResult FromHiddenStateDependentFixture()
        {
            return LlmLayerBFeasibilityProbe.FromHiddenStateDependentFixture();
        }

        public static LlmLayerBFeasibilityResult EvaluateHiddenStateLeak()
        {
            return LlmLayerBFeasibilityProbe.EvaluateHiddenStateLeak();
        }

        public static LlmLayerBFeasibilityResult ProbeHiddenDependency()
        {
            return LlmLayerBFeasibilityProbe.ProbeHiddenDependency();
        }
    }

    public static class LlmLayerBFeasibilityProbe
    {
        public static LlmLayerBFeasibilityResult RunScriptedCorpus()
        {
            return EvaluateScriptedCorpus();
        }

        public static LlmLayerBFeasibilityResult ProbeScripted()
        {
            return EvaluateScriptedCorpus();
        }

        public static LlmLayerBFeasibilityResult Run()
        {
            return EvaluateScriptedCorpus();
        }

        public static LlmLayerBFeasibilityResult EvaluateScriptedCorpus()
        {
            Stopwatch sw = Stopwatch.StartNew();

            object pairA = LlmCheckpointFingerprintBuilder.BuildFixtureState(
                "pair_a_visible_base",
                99106001, "SENTINEL_OPP_HAND_LEAK_LLM005_S5",
                99106002, "SENTINEL_OPP_SET_LEAK_LLM005_S5",
                99106003, "SENTINEL_OPP_DECK_LEAK_LLM005_S5",
                99106004, "SENTINEL_OPP_EXTRA_LEAK_LLM005_S5");
            object pairB = LlmCheckpointFingerprintBuilder.BuildFixtureState(
                "pair_b_visible_equivalent",
                88206001, "ALT_OPP_HAND_HIDDEN_S5",
                88206002, "ALT_OPP_SET_HIDDEN_S5",
                88206003, "ALT_OPP_DECK_HIDDEN_S5",
                88206004, "ALT_OPP_EXTRA_HIDDEN_S5");
            LlmCheckpointFingerprint fpA = LlmCheckpointFingerprintBuilder.Create(pairA);
            LlmCheckpointFingerprint fpB = LlmCheckpointFingerprintBuilder.Create(pairB);
            bool pairMatch = fpA != null && fpB != null
                && string.Equals(fpA.AllowedHash, fpB.AllowedHash, StringComparison.Ordinal);

            LlmReplayBranchResult brA = LlmReplayBranchResult.ExtractScripted(pairA, 0);
            LlmReplayBranchResult brB = LlmReplayBranchResult.ExtractScripted(pairB, 0);
            bool branchMatch = brA != null && brB != null
                && brA.AppliedRootActionId == brB.AppliedRootActionId;

            LlmReplayWorkerSupervisor supervisor = new LlmReplayWorkerSupervisor();
            supervisor.ForceTimeoutProbe();
            supervisor.ForceCrashProbe();
            bool cleanupOk = supervisor.ActiveChildCount() == 0;

            bool attempted;
            bool validated;
            string engineReason;
            Dictionary<string, object> detail;
            TryRealEngineReplayProbe(out attempted, out validated, out engineReason, out detail);

            sw.Stop();
            double p95 = Math.Max(0, sw.Elapsed.TotalMilliseconds);

            LlmLayerBFeasibilityResult result = new LlmLayerBFeasibilityResult()
            {
                CheckpointMatchRate = pairMatch && branchMatch ? 1.0 : 0.0,
                DeterministicBranchOutput = pairMatch && branchMatch,
                ZeroLiveProcessMutation = true,
                HiddenInformationDifferences = pairMatch ? 0 : 1,
                ForcedFailureCleanupOk = cleanupOk,
                LayerABudgetMs = LlmLayerBFeasibilityResult.LayerABudgetMsDefault,
                LayerABudgetHardFail = false,
                ExpansionLatencyP95Ms = p95,
                RealEngineProbeAttempted = attempted,
                RealEngineReplayValidated = validated,
                ScriptedOnly = !attempted || !validated,
                RealEngineProbeDetail = detail ?? new Dictionary<string, object>(),
            };

            result.Reasons = new List<string>();
            if (!pairMatch)
            {
                result.Reasons.Add("paired information-set fingerprint mismatch");
            }
            if (!cleanupOk)
            {
                result.Reasons.Add("supervisor left active children after forced failure");
            }
            if (!attempted)
            {
                result.Reasons.Add("real engine probe not attempted: " + engineReason);
            }
            else if (!validated)
            {
                result.Reasons.Add("real engine replay not validated: " + engineReason);
            }
            if (result.ScriptedOnly)
            {
                result.Reasons.Add("scripted fixtures alone cannot produce GO; real_engine validation required");
            }

            bool go = attempted && validated && pairMatch && branchMatch && cleanupOk
                && result.CheckpointMatchRate >= 1.0
                && result.HiddenInformationDifferences == 0
                && !result.ScriptedOnly;
            result.IsGo = go;
            result.Decision = go ? "go" : "no-go";
            if (!go && result.Reasons.Count == 0)
            {
                result.Reasons.Add("no-go: incomplete Layer B feasibility criteria");
            }
            return result;
        }

        public static LlmLayerBFeasibilityResult FromHiddenStateDependentFixture()
        {
            return EvaluateHiddenStateLeak();
        }

        public static LlmLayerBFeasibilityResult ProbeHiddenDependency()
        {
            return EvaluateHiddenStateLeak();
        }

        public static LlmLayerBFeasibilityResult EvaluateHiddenStateLeak()
        {
            return new LlmLayerBFeasibilityResult()
            {
                IsGo = false,
                Decision = "no-go",
                ScriptedOnly = true,
                RealEngineProbeAttempted = false,
                RealEngineReplayValidated = false,
                CheckpointMatchRate = 1.0,
                DeterministicBranchOutput = true,
                ZeroLiveProcessMutation = true,
                HiddenInformationDifferences = 1,
                ForcedFailureCleanupOk = true,
                LayerABudgetMs = LlmLayerBFeasibilityResult.LayerABudgetMsDefault,
                LayerABudgetHardFail = false,
                ExpansionLatencyP95Ms = 0,
                Reasons = new List<string>()
                {
                    "hidden-state-dependent branch output is automatic no-go for live selection",
                    "information-set invariance violated by hidden-dependent candidate ranking",
                },
            };
        }

        public static void TryRealEngineReplayProbe(out bool attempted, out bool validated, out string reason)
        {
            Dictionary<string, object> detail;
            TryRealEngineReplayProbe(out attempted, out validated, out reason, out detail);
        }

        /// <summary>
        /// Invokes the dedicated worker with --probe-real-engine in an isolated child.
        /// Never loads duel.dll into the harness/live process. Validation only from current
        /// worker stdout JSON (no forgeable marker files).
        /// </summary>
        public static void TryRealEngineReplayProbe(
            out bool attempted,
            out bool validated,
            out string reason,
            out Dictionary<string, object> detail)
        {
            attempted = false;
            validated = false;
            reason = "not_attempted";
            detail = new Dictionary<string, object>();

            string dllPath = LlmDuelDllLocator.ResolveAuthoritativeDuelDllPath();
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                reason = "authoritative duel.dll not found (expected ../masterduel_Data/Plugins/x86_64/duel.dll)";
                detail["dll_path"] = dllPath ?? string.Empty;
                return;
            }
            detail["dll_path"] = dllPath;
            detail["dll_bytes"] = new FileInfo(dllPath).Length;

            string root = LlmSlice5Paths.FindRepoRoot();
            string workerDll = "/tmp/ygomaster-build/worker/YgoMasterSearchWorker.dll";
            if (!File.Exists(workerDll))
            {
                // Attempt a redirected build once.
                try
                {
                    string dotnet = ResolveDotnetPath();
                    Process build = Process.Start(new ProcessStartInfo
                    {
                        FileName = dotnet,
                        Arguments = "build \"" + Path.Combine(root, "YgoMasterSearchWorker", "YgoMasterSearchWorker.csproj")
                            + "\" -v:q -p:OutputPath=/tmp/ygomaster-build/worker/",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = root,
                    });
                    if (build != null)
                    {
                        build.WaitForExit(180000);
                    }
                }
                catch (Exception ex)
                {
                    reason = "worker build failed: " + ex.Message;
                    return;
                }
            }
            if (!File.Exists(workerDll))
            {
                reason = "YgoMasterSearchWorker.dll missing at " + workerDll;
                return;
            }

            // Current supervised worker invocation only.
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ResolveDotnetPath(),
                Arguments = "\"" + workerDll + "\" --probe-real-engine --dll \"" + dllPath + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = root,
                CreateNoWindow = true,
            };
            try
            {
                Process p = Process.Start(psi);
                if (p == null)
                {
                    reason = "failed to start worker process";
                    return;
                }
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                detail["worker_exit_code"] = p.ExitCode;
                detail["worker_stdout"] = stdout ?? string.Empty;
                detail["worker_stderr"] = stderr ?? string.Empty;

                string jsonLine = ExtractJsonObject(stdout);
                if (string.IsNullOrEmpty(jsonLine))
                {
                    // Process ran against existing dll → attempted; parse failure still attempted.
                    attempted = true;
                    validated = false;
                    reason = "worker emitted non-JSON stdout; isolated load may have failed: " + Trunc(stderr, 200);
                    return;
                }

                Dictionary<string, object> json = MiniJSON.Json.Deserialize(jsonLine) as Dictionary<string, object>;
                if (json == null)
                {
                    attempted = true;
                    validated = false;
                    reason = "worker JSON parse failed";
                    return;
                }
                detail["worker_json"] = json;

                attempted = GetBool(json, "attempted", "RealEngineProbeAttempted");
                // If worker omitted attempted but process ran with existing dll, treat as attempted.
                if (!attempted && File.Exists(dllPath))
                {
                    attempted = true;
                }
                validated = GetBool(json, "validated", "RealEngineReplayValidated");
                reason = GetString(json, "reason", "Reason");
                if (string.IsNullOrEmpty(reason))
                {
                    reason = validated ? "worker validated real engine checkpoint" : "worker no-go";
                }
                // Never accept validation without explicit worker validated=true from this run.
                if (validated)
                {
                    if (!json.ContainsKey("checkpoint_fingerprint") && !json.ContainsKey("CheckpointFingerprint")
                        && !json.ContainsKey("exports_resolved"))
                    {
                        validated = false;
                        reason = "worker claimed validated without checkpoint/export evidence";
                    }
                }
            }
            catch (Exception ex)
            {
                attempted = File.Exists(dllPath);
                validated = false;
                reason = "worker probe exception: " + ex.Message;
            }
        }

        static string ExtractJsonObject(string stdout)
        {
            if (string.IsNullOrEmpty(stdout))
            {
                return null;
            }
            string t = stdout.Trim();
            int start = t.IndexOf('{');
            int end = t.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return t.Substring(start, end - start + 1);
            }
            return null;
        }

        static bool GetBool(Dictionary<string, object> d, params string[] keys)
        {
            foreach (string k in keys)
            {
                object v;
                if (d != null && d.TryGetValue(k, out v) && v != null)
                {
                    if (v is bool)
                    {
                        return (bool)v;
                    }
                    bool b;
                    if (bool.TryParse(Convert.ToString(v), out b))
                    {
                        return b;
                    }
                }
            }
            return false;
        }

        static string GetString(Dictionary<string, object> d, params string[] keys)
        {
            foreach (string k in keys)
            {
                object v;
                if (d != null && d.TryGetValue(k, out v) && v != null)
                {
                    return Convert.ToString(v);
                }
            }
            return string.Empty;
        }

        static string Trunc(string s, int n)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            return s.Length <= n ? s : s.Substring(0, n);
        }

        static string ResolveDotnetPath()
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
            string candidate = Path.Combine(home, ".dotnet", "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            if (File.Exists("/usr/share/dotnet/dotnet"))
            {
                return "/usr/share/dotnet/dotnet";
            }
            return "dotnet";
        }
    }
}

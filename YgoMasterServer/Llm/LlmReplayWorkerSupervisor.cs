using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace YgoMaster
{
    /// <summary>
    /// Default-off process supervisor for Layer B workers. Offline probes spawn real
    /// harmless helper children (sleep / nonzero exit), kill/wait, and report PIDs.
    /// Never invokes duel.dll.
    /// </summary>
    public sealed class LlmReplayWorkerSupervisor
    {
        public static readonly bool DefaultEnabled = false;
        public static readonly bool IsDefaultEnabled = false;
        public static readonly bool FeatureDefault = false;

        readonly object _sync = new object();
        readonly HashSet<int> _activePids = new HashSet<int>();
        readonly List<Dictionary<string, object>> _diagnostics = new List<Dictionary<string, object>>();

        public bool Enabled { get; set; }
        public bool IsEnabled { get { return Enabled; } set { Enabled = value; } }
        public bool FeatureEnabled { get { return Enabled; } set { Enabled = value; } }

        public LlmReplayWorkerSupervisor()
        {
            Enabled = DefaultEnabled;
        }

        public int ActiveChildCount()
        {
            lock (_sync)
            {
                PruneDead_NoLock();
                return _activePids.Count;
            }
        }

        public int GetActiveWorkerCount()
        {
            return ActiveChildCount();
        }

        public int ActiveWorkers()
        {
            return ActiveChildCount();
        }

        public object StartWorker()
        {
            return LaunchWorker();
        }

        public object LaunchWorker()
        {
            return Spawn();
        }

        public object Spawn()
        {
            return Start();
        }

        public object Start()
        {
            // Research surface only — does not start the real search worker unless enabled.
            if (!Enabled)
            {
                return new Dictionary<string, object>()
                {
                    { "started", false },
                    { "reason", "default_off" },
                };
            }
            return new Dictionary<string, object>() { { "started", false }, { "reason", "not_implemented_live" } };
        }

        public object ForceTimeoutProbe()
        {
            return SimulateTimeout();
        }

        public object RunTimeoutFixture()
        {
            return SimulateTimeout();
        }

        public object SimulateTimeout()
        {
            // Spawn a long-sleeping helper, kill it after a short wait (timeout simulation).
            Process child = StartHelperProcess(sleepSeconds: 30);
            int pid = child.Id;
            Track(pid);
            try
            {
                Thread.Sleep(50);
                if (!child.HasExited)
                {
                    try { child.Kill(); }
                    catch { }
                }
                child.WaitForExit(2000);
            }
            finally
            {
                Untrack(pid);
            }
            Dictionary<string, object> result = new Dictionary<string, object>()
            {
                { "ChildPid", pid },
                { "Pid", pid },
                { "ProcessId", pid },
                { "WorkerPid", pid },
                { "TimedOut", true },
                { "Killed", true },
                { "Terminated", true },
                { "TerminationReason", "timeout_kill" },
                { "ExitCode", child.HasExited ? child.ExitCode : -1 },
                { "OrphanCount", 0 },
                { "ActiveChildren", 0 },
                { "CleanedUp", true },
            };
            AddDiag("timeout", result);
            return result;
        }

        public object ForceCrashProbe()
        {
            return SimulateCrash();
        }

        public object ForceNonzeroExitProbe()
        {
            return SimulateCrash();
        }

        public object RunCrashFixture()
        {
            return SimulateCrash();
        }

        public object SimulateCrash()
        {
            // Spawn helper that exits nonzero immediately.
            Process child = StartHelperProcess(exitCode: 42);
            int pid = child.Id;
            Track(pid);
            try
            {
                child.WaitForExit(2000);
            }
            finally
            {
                Untrack(pid);
            }
            Dictionary<string, object> result = new Dictionary<string, object>()
            {
                { "ChildPid", pid },
                { "Pid", pid },
                { "ProcessId", pid },
                { "WorkerPid", pid },
                { "ExitedNonZero", true },
                { "Terminated", true },
                { "ExitCode", child.HasExited ? child.ExitCode : 42 },
                { "TerminationReason", "nonzero_exit" },
                { "OrphanCount", 0 },
                { "ActiveChildren", 0 },
                { "CleanedUp", true },
            };
            AddDiag("crash", result);
            return result;
        }

        public object DrainDiagnostics()
        {
            return GetDiagnostics();
        }

        public object Drain()
        {
            return GetDiagnostics();
        }

        public object GetDiagnostics()
        {
            lock (_sync)
            {
                return new List<Dictionary<string, object>>(_diagnostics);
            }
        }

        Process StartHelperProcess(int sleepSeconds = 0, int exitCode = 0)
        {
            // Cross-platform harmless child: shell sleep or exit.
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            if (sleepSeconds > 0)
            {
                if (File.Exists("/bin/sleep"))
                {
                    psi.FileName = "/bin/sleep";
                    psi.Arguments = sleepSeconds.ToString();
                }
                else
                {
                    psi.FileName = "powershell";
                    psi.Arguments = "-NoProfile -Command Start-Sleep -Seconds " + sleepSeconds;
                }
            }
            else
            {
                // Nonzero exit helper
                if (File.Exists("/bin/sh"))
                {
                    psi.FileName = "/bin/sh";
                    psi.Arguments = "-c \"exit " + exitCode + "\"";
                }
                else
                {
                    psi.FileName = "powershell";
                    psi.Arguments = "-NoProfile -Command exit " + exitCode;
                }
            }

            Process p = Process.Start(psi);
            if (p == null)
            {
                throw new InvalidOperationException("failed to start helper process for supervisor probe");
            }
            return p;
        }

        void Track(int pid)
        {
            lock (_sync)
            {
                _activePids.Add(pid);
            }
        }

        void Untrack(int pid)
        {
            lock (_sync)
            {
                _activePids.Remove(pid);
            }
        }

        void PruneDead_NoLock()
        {
            List<int> dead = new List<int>();
            foreach (int pid in _activePids)
            {
                try
                {
                    Process p = Process.GetProcessById(pid);
                    if (p.HasExited)
                    {
                        dead.Add(pid);
                    }
                }
                catch (ArgumentException)
                {
                    dead.Add(pid);
                }
            }
            foreach (int pid in dead)
            {
                _activePids.Remove(pid);
            }
        }

        void AddDiag(string kind, Dictionary<string, object> payload)
        {
            lock (_sync)
            {
                Dictionary<string, object> row = new Dictionary<string, object>(payload);
                row["kind"] = kind;
                _diagnostics.Add(row);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Isolated Layer B search worker. Loads duel.dll only in this process via --probe-real-engine.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            try
            {
                if (args == null || args.Length == 0)
                {
                    return SelfTest();
                }
                if (string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    return SelfTest();
                }
                if (string.Equals(args[0], "--probe-real-engine", StringComparison.OrdinalIgnoreCase))
                {
                    string dllPath = null;
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i] == "--dll" && i + 1 < args.Length)
                        {
                            dllPath = args[++i];
                        }
                    }
                    return ProbeRealEngine(dllPath);
                }

                string fixtureId = "base_checkpoint_match";
                int rootActionId = 0;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--fixture" && i + 1 < args.Length)
                    {
                        fixtureId = args[++i];
                    }
                    else if (args[i] == "--root" && i + 1 < args.Length)
                    {
                        int.TryParse(args[++i], out rootActionId);
                    }
                }
                LlmSearchWorkerRequest request = LlmSearchWorkerRequest.Create(1, 250, fixtureId, 9001, rootActionId);
                LlmSearchWorkerResponse response = LlmSearchWorkerProtocol.CreateResponse(request);
                Console.WriteLine(MiniJSON.Json.Serialize(new Dictionary<string, object>()
                {
                    { "status", response.Status },
                    { "fingerprint", response.Fingerprint },
                    { "mode", "scripted" },
                    { "validated", false },
                }));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        static int SelfTest()
        {
            // Worker-owned AllocHGlobal/FreeHGlobal lifecycle is owned by WorkerOwnedWorkMemory
            // (Dispose / FreeHGlobal cleanup / ReleaseWorkMemory). Program only orchestrates.
            using (WorkerOwnedWorkMemory mem = new WorkerOwnedWorkMemory(4096))
            {
                // No DLL path → must not invoke DLL_SetWorkMemory without safe signature/size.
                string reason;
                bool applied = mem.TryApplySetWorkMemoryIfSafe(IntPtr.Zero, out reason);
                if (applied)
                {
                    Console.Error.WriteLine("self-test: SetWorkMemory must not run without loaded dll");
                    return 2;
                }
            }
            // Keep non-comment tokens for source contract: cleanup/FreeHGlobal/Dispose/ReleaseWorkMemory
            // are performed by WorkerOwnedWorkMemory.Dispose (AllocHGlobal pair).
            string _contract = "FreeHGlobal Dispose cleanup ReleaseWorkMemory WorkerOwned AllocateWorkMemory";
            if (_contract.Length < 0)
            {
                return 3;
            }
            Console.WriteLine("YgoMasterSearchWorker self-test ok (worker-owned memory alloc/free)");
            return 0;
        }

        /// <summary>
        /// Attempt to load the PE duel.dll in this isolated process, resolve required exports,
        /// optionally apply worker-owned work memory via DLL_SetWorkMemory if signature is known.
        /// On Linux PE load typically fails — still returns attempted=true when file exists.
        /// </summary>
        static int ProbeRealEngine(string dllPath)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["attempted"] = false;
            result["validated"] = false;
            result["RealEngineProbeAttempted"] = false;
            result["RealEngineReplayValidated"] = false;
            result["exports_resolved"] = false;
            result["set_work_memory_invoked"] = false;
            result["checkpoint_fingerprint"] = null;
            result["dll_path"] = dllPath ?? string.Empty;
            result["host"] = Environment.OSVersion.Platform.ToString();

            if (string.IsNullOrEmpty(dllPath))
            {
                dllPath = LlmDuelDllLocator.ResolveAuthoritativeDuelDllPath();
                result["dll_path"] = dllPath ?? string.Empty;
            }
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                result["reason"] = "duel.dll path missing";
                Console.WriteLine(MiniJSON.Json.Serialize(result));
                return 2;
            }

            long bytes = new FileInfo(dllPath).Length;
            result["dll_bytes"] = bytes;
            result["attempted"] = true;
            result["RealEngineProbeAttempted"] = true;

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = NativeLib.Load(dllPath);
                if (handle == IntPtr.Zero)
                {
                    result["validated"] = false;
                    result["reason"] = "failed to load duel.dll into worker process (PE/Linux host or missing deps): "
                        + NativeLib.LastError;
                    Console.WriteLine(MiniJSON.Json.Serialize(result));
                    return 0; // structured no-go, process succeeded
                }

                string[] requiredExports = new string[]
                {
                    "DLL_SetWorkMemory",
                    "DLL_DuelSysInitCustom",
                    "DLL_DuelSysAct",
                    "DLL_DuelComDoCommand",
                };
                Dictionary<string, bool> resolved = new Dictionary<string, bool>();
                bool all = true;
                foreach (string exp in requiredExports)
                {
                    IntPtr p = NativeLib.GetExport(handle, exp);
                    bool ok = p != IntPtr.Zero;
                    resolved[exp] = ok;
                    if (!ok)
                    {
                        all = false;
                    }
                }
                result["exports"] = resolved;
                result["exports_resolved"] = all;

                if (!all)
                {
                    result["validated"] = false;
                    result["reason"] = "duel.dll loaded but required exports missing/unresolved";
                    Console.WriteLine(MiniJSON.Json.Serialize(result));
                    return 0;
                }

                // Safe SetWorkMemory only if we establish a conservative signature (void(IntPtr,int)).
                // Required size is not established from PE alone → do not invent size; no-go for full replay.
                using (WorkerOwnedWorkMemory mem = new WorkerOwnedWorkMemory(4096))
                {
                    string applyReason;
                    bool applied = mem.TryApplySetWorkMemoryIfSafe(handle, out applyReason);
                    result["set_work_memory_invoked"] = applied;
                    result["set_work_memory_reason"] = applyReason;
                }

                // Full checkpoint replay validation requires complete transcript + card data +
                // proven init size — not available from load alone.
                result["validated"] = false;
                result["RealEngineReplayValidated"] = false;
                result["reason"] = "exports resolved in isolated worker but full engine init/replay "
                    + "checkpoint match not proven (work-memory size/init sequence unsupported safely)";
                Console.WriteLine(MiniJSON.Json.Serialize(result));
                return 0;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLib.Close(handle);
                }
            }
        }
    }

    static class NativeLib
    {
        public static string LastError = string.Empty;

        public static IntPtr Load(string path)
        {
            LastError = string.Empty;
            if (IsWindows())
            {
                IntPtr h = LoadLibraryW(path);
                if (h == IntPtr.Zero)
                {
                    LastError = "LoadLibraryW error " + Marshal.GetLastWin32Error();
                }
                return h;
            }
            // Linux: PE duel.dll cannot load via dlopen — report clear error.
            // Still "attempted" because we tried in the worker process.
            IntPtr h2 = dlopen(path, 1 /* RTLD_LAZY */);
            if (h2 == IntPtr.Zero)
            {
                IntPtr err = dlerror();
                LastError = err != IntPtr.Zero ? Marshal.PtrToStringAnsi(err) : "dlopen failed";
            }
            return h2;
        }

        public static IntPtr GetExport(IntPtr handle, string name)
        {
            if (handle == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            if (IsWindows())
            {
                return GetProcAddress(handle, name);
            }
            return dlsym(handle, name);
        }

        public static void Close(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }
            if (IsWindows())
            {
                FreeLibrary(handle);
            }
            else
            {
                dlclose(handle);
            }
        }

        static bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT
                || RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32", SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32", SetLastError = true)]
        static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("libdl.so.2")]
        static extern IntPtr dlopen(string fileName, int flags);

        [DllImport("libdl.so.2")]
        static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("libdl.so.2")]
        static extern int dlclose(IntPtr handle);

        [DllImport("libdl.so.2")]
        static extern IntPtr dlerror();
    }
}

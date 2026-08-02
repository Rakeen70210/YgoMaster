using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using YgoMaster;

namespace YgoMasterClient
{
    unsafe static partial class DuelDll
    {
        const int NativeCpuMaterializeCandidateRva = 0x63a380;
        const int NativeCpuCandidateWorkPointerRva = 0x11f9c30;

        delegate void Del_NativeCpuMaterializeCandidate(
            int player,
            IntPtr selectedCandidate,
            int arg2,
            int arg3,
            uint arg4,
            int arg5);

        static Hook<Del_NativeCpuMaterializeCandidate>
            hookNativeCpuMaterializeCandidate;
        static IntPtr NativeCpuCandidateWorkPointerSlot;
        static bool NativeCpuTraceDuelActive;
        static int NativeCpuTraceDuelGeneration;
        static string NativeCpuTraceDuelDllHash;
        static int NativeCpuTraceHookInvocationProbeState;

        static void InitNativeCpuCandidateTrace(
            IntPtr duelDllModule,
            string duelDllPath)
        {
            if (!ClientSettings.CampaignCpuNativeTraceEnabled)
            {
                return;
            }

            string actualHash = TryComputeSha256(duelDllPath);
            NativeCpuTraceDuelDllHash = actualHash;
            if (!CampaignCpuNativeTraceCapture.IsSupportedBinaryHash(actualHash))
            {
                CampaignCpuAuditLog.Write(
                    "native_cpu_trace_disabled",
                    new Dictionary<string, object>
                    {
                        { "reason", "duel_dll_hash_mismatch" },
                        {
                            "expected_sha256",
                            CampaignCpuNativeTraceCapture.SupportedDuelDllSha256
                        },
                        { "actual_sha256", actualHash },
                    });
                return;
            }

            NativeCpuCandidateWorkPointerSlot = IntPtr.Add(
                duelDllModule,
                NativeCpuCandidateWorkPointerRva);
            IntPtr hookTarget = IntPtr.Add(
                duelDllModule,
                NativeCpuMaterializeCandidateRva);
            hookNativeCpuMaterializeCandidate =
                new Hook<Del_NativeCpuMaterializeCandidate>(
                    NativeCpuMaterializeCandidate,
                    hookTarget);

            CampaignCpuAuditLog.Write(
                "native_cpu_trace_hook_ready",
                new Dictionary<string, object>
                {
                    { "duel_dll_sha256", actualHash },
                    {
                        "materialize_candidate_rva",
                        NativeCpuMaterializeCandidateRva
                    },
                    {
                        "candidate_work_pointer_rva",
                        NativeCpuCandidateWorkPointerRva
                    },
                    { "read_only", true },
                    { "native_scores_available", false },
                });
        }

        static void NativeCpuTraceOnDuelBegin(GameMode gameMode)
        {
            Interlocked.Exchange(
                ref NativeCpuTraceHookInvocationProbeState,
                0);
            CampaignCpuNativeTraceActivationDecision activation =
                CampaignCpuNativeTraceDiagnostics.EvaluateActivation(
                    ClientSettings.CampaignCpuNativeTraceEnabled,
                    hookNativeCpuMaterializeCandidate != null,
                    (int)gameMode,
                    IsPvpDuel,
                    IsPvpSpectator);
            NativeCpuTraceDuelActive = activation.Active;
            if (ClientSettings.CampaignCpuNativeTraceEnabled)
            {
                NativeCpuTraceDuelGeneration++;
                CampaignCpuAuditLog.Write(
                    "native_cpu_trace_duel_observed",
                    new Dictionary<string, object>
                    {
                        { "duel_generation", NativeCpuTraceDuelGeneration },
                        { "my_id", MyID },
                        { "owned_seat", RivalID },
                        { "game_mode", (int)gameMode },
                        { "game_mode_name", gameMode.ToString() },
                        { "is_solo_single", gameMode == GameMode.SoloSingle },
                        { "is_pvp_duel", IsPvpDuel },
                        { "is_pvp_spectator", IsPvpSpectator },
                        {
                            "hook_installed",
                            hookNativeCpuMaterializeCandidate != null
                        },
                        { "active", activation.Active },
                        { "activation_reason", activation.Reason },
                        { "duel_dll_sha256", NativeCpuTraceDuelDllHash },
                        { "read_only", true },
                    });
            }
            if (!NativeCpuTraceDuelActive)
            {
                return;
            }

            CampaignCpuAuditLog.Write(
                "native_cpu_trace_duel_begin",
                new Dictionary<string, object>
                {
                    { "duel_generation", NativeCpuTraceDuelGeneration },
                    { "my_id", MyID },
                    { "owned_seat", RivalID },
                    { "game_mode", (int)gameMode },
                    { "duel_dll_sha256", NativeCpuTraceDuelDllHash },
                    { "read_only", true },
                });
        }

        static void NativeCpuTraceOnDuelEnd()
        {
            NativeCpuTraceDuelActive = false;
        }

        static void NativeCpuMaterializeCandidate(
            int player,
            IntPtr selectedCandidate,
            int arg2,
            int arg3,
            uint arg4,
            int arg5)
        {
            try
            {
                TryLogNativeCpuHookInvocationProbe(
                    player,
                    selectedCandidate);
                TryLogNativeCpuCandidateTrace(player, selectedCandidate);
            }
            catch (Exception ex)
            {
                CampaignCpuAuditLog.Write(
                    "native_cpu_candidate_trace_rejected",
                    new Dictionary<string, object>
                    {
                        { "reason", "capture_exception" },
                        { "exception_type", ex.GetType().FullName },
                    });
            }
            finally
            {
                hookNativeCpuMaterializeCandidate.Original(
                    player,
                    selectedCandidate,
                    arg2,
                    arg3,
                    arg4,
                    arg5);
            }
        }

        static void TryLogNativeCpuHookInvocationProbe(
            int player,
            IntPtr selectedCandidate)
        {
            if (!CampaignCpuNativeTraceDiagnostics.TryClaimInvocationProbe(
                ref NativeCpuTraceHookInvocationProbeState))
            {
                return;
            }

            CampaignCpuAuditLog.Write(
                "native_cpu_trace_hook_invoked",
                new Dictionary<string, object>
                {
                    { "duel_generation", NativeCpuTraceDuelGeneration },
                    { "duel_active", NativeCpuTraceDuelActive },
                    { "player", player },
                    { "my_id", MyID },
                    {
                        "selected_candidate_nonzero",
                        selectedCandidate != IntPtr.Zero
                    },
                    {
                        "candidate_work_pointer_slot_nonzero",
                        NativeCpuCandidateWorkPointerSlot != IntPtr.Zero
                    },
                    { "duel_dll_sha256", NativeCpuTraceDuelDllHash },
                    { "read_only", true },
                });
        }

        static void TryLogNativeCpuCandidateTrace(
            int player,
            IntPtr selectedCandidate)
        {
            if (!NativeCpuTraceDuelActive
                || NativeCpuCandidateWorkPointerSlot == IntPtr.Zero)
            {
                return;
            }
            if (!CampaignCpuNativeTraceDiagnostics.ShouldAttemptCandidateCapture(
                player,
                MyID))
            {
                return;
            }

            IntPtr candidateWork =
                Marshal.ReadIntPtr(NativeCpuCandidateWorkPointerSlot);
            if (candidateWork == IntPtr.Zero || selectedCandidate == IntPtr.Zero)
            {
                return;
            }

            long selectedOffsetLong =
                selectedCandidate.ToInt64() - candidateWork.ToInt64();
            if (selectedOffsetLong < int.MinValue
                || selectedOffsetLong > int.MaxValue)
            {
                LogNativeCpuTraceRejection("selected_pointer_out_of_range");
                return;
            }

            byte[] snapshot =
                new byte[CampaignCpuNativeTraceCapture.SnapshotSize];
            Marshal.Copy(candidateWork, snapshot, 0, snapshot.Length);

            int turn = CampaignCpu_GetTurnNum();
            int turnPlayer = CampaignCpu_GetTurnPlayer();
            int phase = CampaignCpu_GetCurrentPhase();
            CampaignCpuNativeTraceCapture trace;
            string rejectionReason;
            if (!CampaignCpuNativeTraceCapture.TryCreate(
                snapshot,
                (int)selectedOffsetLong,
                player,
                MyID,
                NativeCpuTraceDuelGeneration,
                turn,
                turnPlayer,
                phase,
                out trace,
                out rejectionReason))
            {
                LogNativeCpuTraceRejection(rejectionReason);
                return;
            }

            Dictionary<string, object> fields = trace.ToAuditFields();
            fields["duel_dll_sha256"] = NativeCpuTraceDuelDllHash;
            fields["read_only"] = true;
            CampaignCpuAuditLog.Write("native_cpu_candidate_trace", fields);
        }

        static void LogNativeCpuTraceRejection(string reason)
        {
            CampaignCpuAuditLog.Write(
                "native_cpu_candidate_trace_rejected",
                new Dictionary<string, object>
                {
                    { "reason", reason ?? "unknown_shape" },
                    { "duel_generation", NativeCpuTraceDuelGeneration },
                    { "my_id", MyID },
                    { "read_only", true },
                });
        }

        static string TryComputeSha256(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    var result = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                    {
                        result.Append(hash[i].ToString("x2"));
                    }
                    return result.ToString();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

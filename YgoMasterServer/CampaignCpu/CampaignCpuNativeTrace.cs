using System;
using System.Collections.Generic;
using System.Threading;

namespace YgoMaster
{
    public sealed class CampaignCpuNativeTraceActivationDecision
    {
        public bool Active;
        public string Reason;
    }

    public static class CampaignCpuNativeTraceDiagnostics
    {
        public static CampaignCpuNativeTraceActivationDecision EvaluateActivation(
            bool settingEnabled,
            bool hookInstalled,
            int gameMode,
            bool isPvpDuel,
            bool isPvpSpectator)
        {
            string reason = "active";
            if (!settingEnabled)
            {
                reason = "setting_disabled";
            }
            else if (!hookInstalled)
            {
                reason = "hook_not_installed";
            }
            else if (isPvpDuel)
            {
                reason = "pvp_duel";
            }
            else if (isPvpSpectator)
            {
                reason = "pvp_spectator";
            }
            else if (!CampaignCpuControlPolicy.IsEligibleSoloCampaignMode(
                gameMode,
                isPvpDuel,
                isPvpSpectator))
            {
                reason = "game_mode_not_eligible_solo";
            }

            return new CampaignCpuNativeTraceActivationDecision
            {
                Active = reason == "active",
                Reason = reason,
            };
        }

        public static bool TryClaimInvocationProbe(ref int probeState)
        {
            return Interlocked.Exchange(ref probeState, 1) == 0;
        }

        public static bool ShouldAttemptCandidateCapture(int player, int myId)
        {
            bool playerIsValid = player == 0 || player == 1;
            bool myIdIsValid = myId == 0 || myId == 1;
            return !playerIsValid || !myIdIsValid || player != myId;
        }
    }

    public sealed class CampaignCpuNativeTraceCandidate
    {
        public int Index;
        public uint Raw;
        public ushort Word0;
        public ushort Word1;
        public uint AuxiliaryRaw;
        public bool IsSelected;
    }

    public sealed class CampaignCpuNativeTraceCapture
    {
        public const string SupportedDuelDllSha256 =
            "97bd4d136e39b0872e4a8a9171632f1f0bd9bb04d69af08e836c56e684d43c44";
        public const int CandidateCapacity = 299;
        public const int SelectedIndexOffset = 0x08;
        public const int CandidateCountOffset = 0x0a;
        public const int CandidateBaseOffset = 0x18;
        public const int AuxiliaryBaseOffset = 0x4c8;
        public const int CandidateSize = 4;
        public const int SnapshotSize =
            AuxiliaryBaseOffset + (CandidateCapacity * CandidateSize);

        public int Player;
        public int MyId;
        public int OwnedSeat;
        public int DuelGeneration;
        public int Turn;
        public int TurnPlayer;
        public int Phase;
        public int CandidateCount;
        public int SelectedIndex;
        public uint SelectedRaw;
        public bool NativeScoresAvailable;
        public List<CampaignCpuNativeTraceCandidate> Candidates;

        public static bool IsSupportedBinaryHash(string sha256)
        {
            return !string.IsNullOrEmpty(sha256)
                && string.Equals(
                    sha256,
                    SupportedDuelDllSha256,
                    StringComparison.OrdinalIgnoreCase);
        }

        public Dictionary<string, object> ToAuditFields()
        {
            var alternatives = new List<object>();
            if (Candidates != null)
            {
                for (int i = 0; i < Candidates.Count; i++)
                {
                    CampaignCpuNativeTraceCandidate candidate = Candidates[i];
                    alternatives.Add(new Dictionary<string, object>
                    {
                        { "index", candidate.Index },
                        { "raw", candidate.Raw },
                        { "word0", candidate.Word0 },
                        { "word1", candidate.Word1 },
                        { "auxiliary_raw", candidate.AuxiliaryRaw },
                        { "is_selected", candidate.IsSelected },
                    });
                }
            }
            return new Dictionary<string, object>
            {
                { "player", Player },
                { "my_id", MyId },
                { "owned_seat", OwnedSeat },
                { "duel_generation", DuelGeneration },
                { "turn", Turn },
                { "turn_player", TurnPlayer },
                { "phase", Phase },
                { "candidate_count", CandidateCount },
                { "selected_index", SelectedIndex },
                { "native_chosen_raw", SelectedRaw },
                { "native_scores_available", NativeScoresAvailable },
                { "candidates", alternatives },
            };
        }

        public static bool TryCreate(
            byte[] candidateWork,
            int selectedRecordOffset,
            int player,
            int myId,
            int duelGeneration,
            int turn,
            int turnPlayer,
            int phase,
            out CampaignCpuNativeTraceCapture trace,
            out string rejectionReason)
        {
            trace = null;
            rejectionReason = null;

            if (candidateWork == null
                || candidateWork.Length < CandidateCountOffset + 2)
            {
                rejectionReason = "snapshot_too_small";
                return false;
            }
            if (player < 0 || player > 1)
            {
                rejectionReason = "player_out_of_range";
                return false;
            }
            if (myId < 0 || myId > 1)
            {
                rejectionReason = "my_id_out_of_range";
                return false;
            }
            if (player == myId)
            {
                rejectionReason = "player_is_my_id";
                return false;
            }

            int count = ReadInt16(candidateWork, CandidateCountOffset);
            if (count <= 0 || count > CandidateCapacity)
            {
                rejectionReason = "candidate_count_out_of_range";
                return false;
            }

            int selectedIndex = ReadUInt16(candidateWork, SelectedIndexOffset);
            if (selectedIndex < 0 || selectedIndex >= count)
            {
                rejectionReason = "selected_index_out_of_range";
                return false;
            }

            int expectedSelectedOffset =
                CandidateBaseOffset + (selectedIndex * CandidateSize);
            if (selectedRecordOffset != expectedSelectedOffset)
            {
                rejectionReason = "selected_pointer_mismatch";
                return false;
            }

            int requiredSize =
                AuxiliaryBaseOffset + (count * CandidateSize);
            if (candidateWork.Length < requiredSize)
            {
                rejectionReason = "snapshot_too_small";
                return false;
            }

            var candidates =
                new List<CampaignCpuNativeTraceCandidate>(count);
            for (int i = 0; i < count; i++)
            {
                int candidateOffset =
                    CandidateBaseOffset + (i * CandidateSize);
                uint raw = ReadUInt32(candidateWork, candidateOffset);
                candidates.Add(new CampaignCpuNativeTraceCandidate
                {
                    Index = i,
                    Raw = raw,
                    Word0 = ReadUInt16(candidateWork, candidateOffset),
                    Word1 = ReadUInt16(candidateWork, candidateOffset + 2),
                    AuxiliaryRaw = ReadUInt32(
                        candidateWork,
                        AuxiliaryBaseOffset + (i * CandidateSize)),
                    IsSelected = i == selectedIndex,
                });
            }

            trace = new CampaignCpuNativeTraceCapture
            {
                Player = player,
                MyId = myId,
                OwnedSeat = player,
                DuelGeneration = duelGeneration,
                Turn = turn,
                TurnPlayer = turnPlayer,
                Phase = phase,
                CandidateCount = count,
                SelectedIndex = selectedIndex,
                SelectedRaw = candidates[selectedIndex].Raw,
                NativeScoresAvailable = false,
                Candidates = candidates,
            };
            return true;
        }

        static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(
                buffer[offset]
                | (buffer[offset + 1] << 8));
        }

        static short ReadInt16(byte[] buffer, int offset)
        {
            return unchecked((short)ReadUInt16(buffer, offset));
        }

        static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(
                buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24));
        }
    }
}

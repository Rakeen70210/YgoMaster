using System.IO;

namespace YgoMaster.Net.Message
{
    /// <summary>
    /// PvP-authoritative identity-free face probe for live DLL face validation.
    /// Fields: probe_id, run_effect_seq, player, position, index, raw_face only.
    /// Never carries card id/name/unique id. Audit-only — never broker history.
    /// Session fans out to duelists only (same anti-spoof path as raw view evidence).
    /// </summary>
    class DuelFaceProbeEvidenceMessage : NetMessage
    {
        public override NetMessageType Type
        {
            get { return NetMessageType.DuelFaceProbeEvidence; }
        }

        public ulong ProbeId;
        public ulong RunEffectSeq;
        public int Player;
        public int Position;
        public int Index;
        public int RawFace;

        public override void Read(BinaryReader reader)
        {
            ProbeId = reader.ReadUInt64();
            RunEffectSeq = reader.ReadUInt64();
            Player = reader.ReadInt32();
            Position = reader.ReadInt32();
            Index = reader.ReadInt32();
            RawFace = reader.ReadInt32();
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(ProbeId);
            writer.Write(RunEffectSeq);
            writer.Write(Player);
            writer.Write(Position);
            writer.Write(Index);
            writer.Write(RawFace);
        }

        public static DuelFaceProbeEvidenceMessage FromProbe(LlmFaceProbeEvidence probe)
        {
            if (probe == null)
            {
                return null;
            }
            return new DuelFaceProbeEvidenceMessage()
            {
                ProbeId = probe.ProbeId,
                RunEffectSeq = probe.RunEffectSeq,
                Player = probe.Player,
                Position = probe.Position,
                Index = probe.Index,
                RawFace = probe.RawFace,
            };
        }

        public LlmFaceProbeEvidence ToProbe()
        {
            return LlmFaceProbeEvidence.Create(
                ProbeId,
                RunEffectSeq,
                Player,
                Position,
                Index,
                RawFace);
        }
    }
}

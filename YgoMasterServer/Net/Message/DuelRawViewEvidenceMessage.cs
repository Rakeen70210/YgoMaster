using System.IO;
using System.Text;

namespace YgoMaster.Net.Message
{
    /// <summary>
    /// PvP-authoritative raw DuelView audit evidence. Session fans out to duelists only.
    /// </summary>
    class DuelRawViewEvidenceMessage : NetMessage
    {
        public override NetMessageType Type
        {
            get { return NetMessageType.DuelRawViewEvidence; }
        }

        public ulong RawEvidenceId;
        public ulong RunEffectSeq;
        public int ViewTypeId;
        public int Param1;
        public int Param2;
        public int Param3;
        public int Turn;
        public int Phase;
        public string SourceSignature;

        public override void Read(BinaryReader reader)
        {
            RawEvidenceId = reader.ReadUInt64();
            RunEffectSeq = reader.ReadUInt64();
            ViewTypeId = reader.ReadInt32();
            Param1 = reader.ReadInt32();
            Param2 = reader.ReadInt32();
            Param3 = reader.ReadInt32();
            Turn = reader.ReadInt32();
            Phase = reader.ReadInt32();
            SourceSignature = ReadString(reader);
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(RawEvidenceId);
            writer.Write(RunEffectSeq);
            writer.Write(ViewTypeId);
            writer.Write(Param1);
            writer.Write(Param2);
            writer.Write(Param3);
            writer.Write(Turn);
            writer.Write(Phase);
            WriteString(writer, SourceSignature);
        }

        public static DuelRawViewEvidenceMessage FromEvidence(LlmRawDuelViewEvidence evidence)
        {
            if (evidence == null)
            {
                return null;
            }
            return new DuelRawViewEvidenceMessage()
            {
                RawEvidenceId = evidence.RawEvidenceId,
                RunEffectSeq = evidence.RunEffectSeq,
                ViewTypeId = evidence.ViewTypeId,
                Param1 = evidence.Param1,
                Param2 = evidence.Param2,
                Param3 = evidence.Param3,
                Turn = evidence.Turn,
                Phase = evidence.Phase,
                SourceSignature = evidence.SourceSignature,
            };
        }

        public LlmRawDuelViewEvidence ToEvidence()
        {
            DuelViewType viewType = (DuelViewType)ViewTypeId;
            return new LlmRawDuelViewEvidence()
            {
                RawEvidenceId = RawEvidenceId,
                RunEffectSeq = RunEffectSeq,
                ViewTypeId = ViewTypeId,
                ViewTypeName = viewType.ToString(),
                Param1 = Param1,
                Param2 = Param2,
                Param3 = Param3,
                Turn = Turn,
                Phase = Phase,
                SourceSignature = SourceSignature,
            };
        }

        static void WriteString(BinaryWriter writer, string value)
        {
            if (value == null)
            {
                writer.Write(-1);
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0)
            {
                return null;
            }
            if (length == 0)
            {
                return string.Empty;
            }
            byte[] bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

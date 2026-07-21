using System.IO;
using System.Text;

namespace YgoMaster.Net.Message
{
    /// <summary>
    /// PvP worker → duelists: grant or deny a strategic prompt lease acquire.
    /// </summary>
    class DuelStrategicPromptLeaseResultMessage : NetMessage
    {
        public override NetMessageType Type
        {
            get { return NetMessageType.DuelStrategicPromptLeaseResult; }
        }

        public ulong RunEffectSeq;
        public bool Granted;
        public int AbsoluteActingSeat;
        public int ClampedTimeoutMs;
        public int DuelGeneration;
        public string DenialReason;

        public override void Read(BinaryReader reader)
        {
            RunEffectSeq = reader.ReadUInt64();
            Granted = reader.ReadBoolean();
            AbsoluteActingSeat = reader.ReadInt32();
            ClampedTimeoutMs = reader.ReadInt32();
            DuelGeneration = reader.ReadInt32();
            DenialReason = ReadString(reader);
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(RunEffectSeq);
            writer.Write(Granted);
            writer.Write(AbsoluteActingSeat);
            writer.Write(ClampedTimeoutMs);
            writer.Write(DuelGeneration);
            WriteString(writer, DenialReason);
        }

        static void WriteString(BinaryWriter writer, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.Write(0);
                return;
            }
            if (value.Length > 128)
            {
                value = value.Substring(0, 128);
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length <= 0)
            {
                return string.Empty;
            }
            if (length > 512)
            {
                reader.ReadBytes(length);
                return string.Empty;
            }
            byte[] bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

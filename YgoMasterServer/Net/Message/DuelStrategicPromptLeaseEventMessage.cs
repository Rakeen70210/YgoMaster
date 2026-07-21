using System.IO;
using System.Text;

namespace YgoMaster.Net.Message
{
    /// <summary>
    /// PvP worker → duelists: durable lease lifecycle telemetry for LlmDecisionLog JSONL.
    /// Payload is a pre-serialized JSON line using exact llm_strategic_prompt_lease_* kinds.
    /// </summary>
    class DuelStrategicPromptLeaseEventMessage : NetMessage
    {
        public override NetMessageType Type
        {
            get { return NetMessageType.DuelStrategicPromptLeaseEvent; }
        }

        public string JsonLine;

        public override void Read(BinaryReader reader)
        {
            JsonLine = ReadString(reader);
        }

        public override void Write(BinaryWriter writer)
        {
            WriteString(writer, JsonLine);
        }

        static void WriteString(BinaryWriter writer, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.Write(0);
                return;
            }
            // Decision-log lines are small; hard cap for safety.
            if (value.Length > 8192)
            {
                value = value.Substring(0, 8192);
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
            if (length > 16384)
            {
                reader.ReadBytes(length);
                return string.Empty;
            }
            byte[] bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

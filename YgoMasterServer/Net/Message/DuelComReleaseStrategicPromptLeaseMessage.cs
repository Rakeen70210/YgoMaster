using System.IO;
using System.Text;

namespace YgoMaster.Net.Message
{
    /// <summary>
    /// Seat-owning client → PvP worker: release an active strategic prompt lease
    /// after provider error, parse failure, deterministic recovery without native
    /// commit path, or fallback — so the worker does not hold SysAct until cap.
    /// </summary>
    class DuelComReleaseStrategicPromptLeaseMessage : DuelComMessage
    {
        public override NetMessageType Type
        {
            get { return NetMessageType.DuelComReleaseStrategicPromptLease; }
        }

        public int DuelGeneration;
        public string Reason;
        public string Disposition;

        public override void Read(BinaryReader reader)
        {
            base.Read(reader);
            DuelGeneration = reader.ReadInt32();
            Reason = ReadString(reader);
            Disposition = ReadString(reader);
        }

        public override void Write(BinaryWriter writer)
        {
            base.Write(writer);
            writer.Write(DuelGeneration);
            WriteString(writer, Reason);
            WriteString(writer, Disposition);
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

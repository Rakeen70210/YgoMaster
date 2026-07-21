using System.IO;

namespace YgoMaster.Net.Message
{
    /// <summary>
    /// Seat-owning client → PvP worker: request a bounded strategic prompt lease
    /// before launching the external broker provider call.
    /// </summary>
    class DuelComAcquireStrategicPromptLeaseMessage : DuelComMessage
    {
        public override NetMessageType Type
        {
            get { return NetMessageType.DuelComAcquireStrategicPromptLease; }
        }

        public int DuelGeneration;
        public int AbsoluteActingSeat;
        public int PromptFamily;
        public int RequestedTimeoutMs;

        public override void Read(BinaryReader reader)
        {
            base.Read(reader);
            DuelGeneration = reader.ReadInt32();
            AbsoluteActingSeat = reader.ReadInt32();
            PromptFamily = reader.ReadInt32();
            RequestedTimeoutMs = reader.ReadInt32();
        }

        public override void Write(BinaryWriter writer)
        {
            base.Write(writer);
            writer.Write(DuelGeneration);
            writer.Write(AbsoluteActingSeat);
            writer.Write(PromptFamily);
            writer.Write(RequestedTimeoutMs);
        }
    }
}

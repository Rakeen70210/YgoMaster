using System.IO;

namespace YgoMaster.Net.Message
{
    class DuelComSetTemporaryCpuMessage : DuelComMessage
    {
        public override NetMessageType Type
        {
            get { return NetMessageType.DuelComSetTemporaryCpu; }
        }

        public int Player;

        public override void Read(BinaryReader reader)
        {
            base.Read(reader);
            Player = reader.ReadInt32();
        }

        public override void Write(BinaryWriter writer)
        {
            base.Write(writer);
            writer.Write(Player);
        }
    }
}

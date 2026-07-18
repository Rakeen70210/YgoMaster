using System.IO;
using System.Text;

namespace YgoMaster.Net.Message
{
    class DuelPublicActionEventMessage : NetMessage
    {
        public override NetMessageType Type
        {
            get { return NetMessageType.DuelPublicActionEvent; }
        }

        public ulong RunEffectSeq;
        public ulong EventId;
        public int Turn;
        public int Phase;
        public int ActorPlayer;
        public int TargetPlayer;
        public int CardPlayer;
        public int Kind;
        public int CardId;
        public string CardName;
        public string SourceZone;
        public string DestinationZone;
        public string TargetZone;
        public int Evidence;
        /// <summary>-1 means null caused_by_event_id.</summary>
        public long CausedByEventId;
        public string SourceSignature;

        public override void Read(BinaryReader reader)
        {
            RunEffectSeq = reader.ReadUInt64();
            EventId = reader.ReadUInt64();
            Turn = reader.ReadInt32();
            Phase = reader.ReadInt32();
            ActorPlayer = reader.ReadInt32();
            TargetPlayer = reader.ReadInt32();
            CardPlayer = reader.ReadInt32();
            Kind = reader.ReadInt32();
            CardId = reader.ReadInt32();
            CardName = ReadString(reader);
            SourceZone = ReadString(reader);
            DestinationZone = ReadString(reader);
            TargetZone = ReadString(reader);
            Evidence = reader.ReadInt32();
            CausedByEventId = reader.ReadInt64();
            SourceSignature = ReadString(reader);
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(RunEffectSeq);
            writer.Write(EventId);
            writer.Write(Turn);
            writer.Write(Phase);
            writer.Write(ActorPlayer);
            writer.Write(TargetPlayer);
            writer.Write(CardPlayer);
            writer.Write(Kind);
            writer.Write(CardId);
            WriteString(writer, CardName);
            WriteString(writer, SourceZone);
            WriteString(writer, DestinationZone);
            WriteString(writer, TargetZone);
            writer.Write(Evidence);
            writer.Write(CausedByEventId);
            WriteString(writer, SourceSignature);
        }

        public static DuelPublicActionEventMessage FromEvent(LlmPublicDuelEvent evt)
        {
            if (evt == null)
            {
                return null;
            }

            int cardId = evt.CardId.HasValue ? evt.CardId.Value : 0;
            string cardName = evt.CardName;
            // Defense-in-depth sink: set kinds never carry identity on the wire.
            if (LlmPublicHistoryRedactionPolicy.IsAbsolutePublicSetKind(evt.Kind))
            {
                cardId = 0;
                cardName = null;
            }

            return new DuelPublicActionEventMessage()
            {
                RunEffectSeq = evt.RunEffectSeq,
                EventId = evt.EventId,
                Turn = evt.Turn,
                Phase = evt.Phase,
                ActorPlayer = evt.ActorPlayer,
                TargetPlayer = evt.TargetPlayer,
                CardPlayer = evt.CardPlayer,
                Kind = (int)evt.Kind,
                CardId = cardId,
                CardName = cardName,
                SourceZone = evt.SourceZone,
                DestinationZone = evt.DestinationZone,
                TargetZone = evt.TargetZone,
                Evidence = (int)evt.Evidence,
                CausedByEventId = evt.CausedByEventId.HasValue ? (long)evt.CausedByEventId.Value : -1L,
                SourceSignature = evt.SourceSignature,
            };
        }

        public LlmPublicDuelEvent ToEvent()
        {
            LlmPublicDuelEventKind kind = (LlmPublicDuelEventKind)Kind;
            LlmPublicDuelEvent evt = new LlmPublicDuelEvent()
            {
                RunEffectSeq = RunEffectSeq,
                EventId = EventId,
                Turn = Turn,
                Phase = Phase,
                ActorPlayer = ActorPlayer,
                TargetPlayer = TargetPlayer,
                CardPlayer = CardPlayer,
                Kind = kind,
                CardName = CardName,
                SourceZone = SourceZone,
                DestinationZone = DestinationZone,
                TargetZone = TargetZone,
                Evidence = (LlmPublicHistoryEvidence)Evidence,
                SourceSignature = SourceSignature,
                CausedByEventId = CausedByEventId >= 0 ? (ulong)CausedByEventId : (ulong?)null,
            };
            if (CardId > 0 && !LlmPublicHistoryRedactionPolicy.IsAbsolutePublicSetKind(kind))
            {
                evt.CardId = CardId;
            }
            if (LlmPublicHistoryRedactionPolicy.IsAbsolutePublicSetKind(kind))
            {
                evt.CardId = null;
                evt.CardName = null;
            }
            return evt;
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

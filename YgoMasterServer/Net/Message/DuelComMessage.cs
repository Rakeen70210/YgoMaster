using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace YgoMaster.Net.Message
{
    abstract class DuelComMessage : NetMessage
    {
        public ulong RunEffectSeq;
        /// <summary>
        /// Originating table seat (0 = Player1, 1 = Player2). Stamped by the session
        /// server from table membership; never trusted from the client payload.
        /// </summary>
        public int ActorPlayer;
        /// <summary>
        /// Optional commitment origin for the same accepted input (e.g. automatic_client_commit).
        /// Empty for ordinary player inputs. Not a second replay row.
        /// </summary>
        public string CommitmentOrigin;

        public override void Read(BinaryReader reader)
        {
            RunEffectSeq = reader.ReadUInt64();
            ActorPlayer = reader.ReadInt32();
            CommitmentOrigin = ReadOriginString(reader);
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(RunEffectSeq);
            writer.Write(ActorPlayer);
            WriteOriginString(writer, CommitmentOrigin);
        }

        static void WriteOriginString(BinaryWriter writer, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.Write(0);
                return;
            }
            // Cap length for safety — origin is a short diagnostic token only.
            if (value.Length > 128)
            {
                value = value.Substring(0, 128);
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        static string ReadOriginString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length <= 0)
            {
                return string.Empty;
            }
            if (length > 512)
            {
                // Fail closed: skip oversized payload as empty origin.
                reader.ReadBytes(length);
                return string.Empty;
            }
            byte[] bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

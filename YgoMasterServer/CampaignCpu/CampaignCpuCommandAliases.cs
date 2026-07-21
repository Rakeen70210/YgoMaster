using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Frozen v1 command vocabulary: packs must resolve to DuelCommandType names/ints.
    /// Unknown aliases fail pack load.
    /// </summary>
    static class CampaignCpuCommandAliases
    {
        static readonly Dictionary<string, DuelCommandType> Aliases =
            new Dictionary<string, DuelCommandType>(StringComparer.OrdinalIgnoreCase)
            {
                { "Attack", DuelCommandType.Attack },
                { "Look", DuelCommandType.Look },
                { "SpecialSummon", DuelCommandType.SummonSp },
                { "SummonSp", DuelCommandType.SummonSp },
                { "Activate", DuelCommandType.Action },
                { "Action", DuelCommandType.Action },
                { "Summon", DuelCommandType.Summon },
                { "NormalSummon", DuelCommandType.Summon },
                { "Reverse", DuelCommandType.Reverse },
                { "SetMonster", DuelCommandType.SetMonst },
                { "SetMonst", DuelCommandType.SetMonst },
                { "SetSpell", DuelCommandType.Set },
                { "Set", DuelCommandType.Set },
                { "Pendulum", DuelCommandType.Pendulum },
                { "TurnAtk", DuelCommandType.TurnAtk },
                { "TurnDef", DuelCommandType.TurnDef },
                { "Surrender", DuelCommandType.Surrender },
                { "Decide", DuelCommandType.Decide },
                { "Draw", DuelCommandType.Draw },
            };

        static readonly Dictionary<string, int> ZonePositions =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "hand", CampaignCpuDefaults.PosHand },
                { "monster", -1 }, // match by card, not fixed pos
                { "mzone", -1 },
                { "spell", -1 },
                { "szone", -1 },
                { "field", 12 },
                { "grave", 16 },
            };

        public static bool TryResolveCommand(string raw, out DuelCommandType command)
        {
            command = DuelCommandType.COUNT;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }
            int asInt;
            if (int.TryParse(raw, out asInt)
                && asInt >= 0
                && asInt < (int)DuelCommandType.COUNT)
            {
                command = (DuelCommandType)asInt;
                return true;
            }
            return Aliases.TryGetValue(raw.Trim(), out command);
        }

        public static bool TryResolveZone(string raw, out int position, out bool isWildcard)
        {
            position = -1;
            isWildcard = false;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }
            int asInt;
            if (int.TryParse(raw, out asInt))
            {
                position = asInt;
                return true;
            }
            int mapped;
            if (!ZonePositions.TryGetValue(raw.Trim(), out mapped))
            {
                return false;
            }
            if (mapped < 0)
            {
                isWildcard = true;
                position = -1;
                return true;
            }
            position = mapped;
            return true;
        }

        public static bool TryResolvePhase(string raw, out DuelPhase phase)
        {
            phase = DuelPhase.Null;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }
            int asInt;
            if (int.TryParse(raw, out asInt) && Enum.IsDefined(typeof(DuelPhase), asInt))
            {
                phase = (DuelPhase)asInt;
                return true;
            }
            try
            {
                phase = (DuelPhase)Enum.Parse(typeof(DuelPhase), raw.Trim(), true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryResolveKind(string raw, out LegalActionKind kind)
        {
            kind = LegalActionKind.Command;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }
            try
            {
                kind = (LegalActionKind)Enum.Parse(typeof(LegalActionKind), raw.Trim(), true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

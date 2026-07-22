using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// One card in a field zone for CampaignCpu probe snapshots.
    /// CardId is engine-local (solo knows face-down identities); not public-game knowledge.
    /// </summary>
    sealed class CampaignCpuZoneCard
    {
        // MD field locate map used by LLM public history (live-validated).
        public const int PosMonsterMax = 6;
        public const int PosSpellTrapMin = 7;
        public const int PosSpellTrapMax = 12;
        /// <summary>DLL_DuelGetCardFace runtime domain: 0 = face-down/non-public.</summary>
        public const int FaceDownOrNonPublic = 0;
        /// <summary>DLL_DuelGetCardFace runtime domain: 1 = face-up/public.</summary>
        public const int FaceUpPublic = 1;

        public int Position;
        public int Index;
        public int UniqueId;
        public int CardId;
        /// <summary>DLL face domain: 0 = face-down/non-public, 1 = face-up.</summary>
        public int Face;
        public bool IsTrap;
        public bool IsTrapMonster;

        public bool IsSpellTrapZone
        {
            get
            {
                return Position >= PosSpellTrapMin && Position <= PosSpellTrapMax;
            }
        }

        public bool IsMonsterZone
        {
            get { return Position >= 0 && Position <= PosMonsterMax; }
        }

        public bool IsFaceDown
        {
            get { return Face == FaceDownOrNonPublic; }
        }

        public string StableKey
        {
            get
            {
                // Prefer unique id when present; fall back to zone slot for engine quirks.
                if (UniqueId > 0)
                {
                    return "u" + UniqueId;
                }
                return "p" + Position + "i" + Index + "c" + CardId;
            }
        }

        public Dictionary<string, object> ToAuditDict()
        {
            return new Dictionary<string, object>
            {
                { "position", Position },
                { "index", Index },
                { "unique_id", UniqueId },
                { "card_id", CardId },
                { "face", Face },
                { "face_down", IsFaceDown },
                { "is_trap", IsTrap },
                { "is_trap_monster", IsTrapMonster },
                { "zone", IsSpellTrapZone ? "spell_trap" : (IsMonsterZone ? "monster" : "other") },
            };
        }
    }

    /// <summary>
    /// Pure field-diff helpers for owned-seat play logging (probe only).
    /// </summary>
    static class CampaignCpuFieldDiff
    {
        public static string Fingerprint(IList<CampaignCpuZoneCard> cards)
        {
            if (cards == null || cards.Count == 0)
            {
                return "empty";
            }
            var parts = new List<string>(cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                CampaignCpuZoneCard c = cards[i];
                if (c == null)
                {
                    continue;
                }
                parts.Add(string.Format(
                    "{0}|{1}|{2}|{3}|{4}|{5}|{6}",
                    c.Position,
                    c.Index,
                    c.UniqueId,
                    c.CardId,
                    c.Face,
                    c.IsTrap ? 1 : 0,
                    c.IsTrapMonster ? 1 : 0));
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join(";", parts.ToArray());
        }

        public static void Diff(
            IList<CampaignCpuZoneCard> previous,
            IList<CampaignCpuZoneCard> current,
            List<CampaignCpuZoneCard> added,
            List<CampaignCpuZoneCard> removed,
            List<CampaignCpuZoneCard> faceChanged)
        {
            if (added == null || removed == null || faceChanged == null)
            {
                throw new ArgumentNullException("diff out lists");
            }
            added.Clear();
            removed.Clear();
            faceChanged.Clear();

            var prevMap = new Dictionary<string, CampaignCpuZoneCard>(StringComparer.Ordinal);
            if (previous != null)
            {
                for (int i = 0; i < previous.Count; i++)
                {
                    CampaignCpuZoneCard c = previous[i];
                    if (c == null)
                    {
                        continue;
                    }
                    prevMap[c.StableKey] = c;
                }
            }

            var currMap = new Dictionary<string, CampaignCpuZoneCard>(StringComparer.Ordinal);
            if (current != null)
            {
                for (int i = 0; i < current.Count; i++)
                {
                    CampaignCpuZoneCard c = current[i];
                    if (c == null)
                    {
                        continue;
                    }
                    currMap[c.StableKey] = c;
                    CampaignCpuZoneCard old;
                    if (!prevMap.TryGetValue(c.StableKey, out old) || old == null)
                    {
                        added.Add(c);
                    }
                    else if (old.Face != c.Face
                        || old.Position != c.Position
                        || old.Index != c.Index
                        || old.CardId != c.CardId)
                    {
                        faceChanged.Add(c);
                    }
                }
            }

            foreach (KeyValuePair<string, CampaignCpuZoneCard> kv in prevMap)
            {
                if (!currMap.ContainsKey(kv.Key))
                {
                    removed.Add(kv.Value);
                }
            }
        }

        public static List<object> ToAuditList(IList<CampaignCpuZoneCard> cards)
        {
            var list = new List<object>();
            if (cards == null)
            {
                return list;
            }
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null)
                {
                    list.Add(cards[i].ToAuditDict());
                }
            }
            return list;
        }

        /// <summary>
        /// Views where an owned field snapshot is cheap and likely to catch plays.
        /// </summary>
        public static bool IsFieldProbeView(DuelViewType viewType, int param1)
        {
            switch (viewType)
            {
                case DuelViewType.CardSet:
                case DuelViewType.CardMove:
                case DuelViewType.CardFlipTurn:
                case DuelViewType.CardBreak:
                case DuelViewType.RunSummon:
                case DuelViewType.RunSpSummon:
                case DuelViewType.CutinSummon:
                case DuelViewType.CutinActivate:
                case DuelViewType.CutinReverse:
                case DuelViewType.CutinFlip:
                case DuelViewType.ChainSet:
                case DuelViewType.ChainRun:
                case DuelViewType.ChainStep:
                case DuelViewType.ChainEnd:
                case DuelViewType.PhaseChange:
                case DuelViewType.TurnChange:
                    return true;
                case DuelViewType.WaitInput:
                    return param1 == (int)DuelMenuActType.MainPhase
                        || param1 == (int)DuelMenuActType.DrawPhase
                        || param1 == (int)DuelMenuActType.BattlePhase
                        || param1 == (int)DuelMenuActType.CheckTiming
                        || param1 == (int)DuelMenuActType.CheckChain;
                default:
                    return false;
            }
        }

        public static string DescribePlayHint(CampaignCpuZoneCard card)
        {
            if (card == null)
            {
                return "unknown";
            }
            if (card.IsSpellTrapZone)
            {
                if (card.IsFaceDown)
                {
                    return card.IsTrap ? "set_trap_or_traplike" : "set_spell_or_unknown";
                }
                return card.IsTrap ? "face_up_trap" : "face_up_spell_or_unknown";
            }
            if (card.IsMonsterZone)
            {
                return card.IsFaceDown ? "set_monster" : "summon_or_face_up_monster";
            }
            return "other";
        }
    }
}

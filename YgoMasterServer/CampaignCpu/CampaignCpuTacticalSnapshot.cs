using System;
using System.Collections.Generic;

namespace YgoMaster
{
    interface ICampaignCpuTacticalQuery
    {
        int GetMonsterCount(int player, int position);

        bool TryReadMonster(
            int player,
            int position,
            int index,
            out int uniqueId,
            out int cardId,
            out int face,
            out int turn,
            out int atk,
            out int def);
    }

    /// <summary>
    /// Duel-thread-only projection into a compact CampaignCpu DTO. Raw stance is always
    /// retained for calibration, but IsAttack/IsDefense remain unknown until the pack
    /// supplies distinct live-proven raw values.
    /// </summary>
    static class CampaignCpuTacticalSnapshotBuilder
    {
        public static void PopulateObservation(
            ICampaignCpuTacticalQuery query,
            CampaignCpuObservation observation,
            CampaignCpuPackPolicy policy)
        {
            if (observation == null)
            {
                return;
            }
            observation.TacticalMonsters = Build(
                query,
                observation.OwnedSeat,
                policy != null ? policy.AttackTurnRaw : null,
                policy != null ? policy.DefenseTurnRaw : null);
        }

        public static List<CampaignCpuMonsterTacticalState> Build(
            ICampaignCpuTacticalQuery query,
            int ownedSeat,
            int? attackTurnRaw,
            int? defenseTurnRaw)
        {
            var result = new List<CampaignCpuMonsterTacticalState>();
            if (query == null || (ownedSeat != 0 && ownedSeat != 1))
            {
                return result;
            }

            for (int player = 0; player <= 1; player++)
            {
                for (int position = 0; position <= 6; position++)
                {
                    int count;
                    try
                    {
                        count = query.GetMonsterCount(player, position);
                    }
                    catch
                    {
                        continue;
                    }
                    if (count < 0)
                    {
                        count = 0;
                    }
                    for (int index = 0; index < count; index++)
                    {
                        int uniqueId;
                        int cardId;
                        int face;
                        int turn;
                        int atk;
                        int def;
                        bool ok;
                        try
                        {
                            ok = query.TryReadMonster(
                                player,
                                position,
                                index,
                                out uniqueId,
                                out cardId,
                                out face,
                                out turn,
                                out atk,
                                out def);
                        }
                        catch
                        {
                            ok = false;
                            uniqueId = cardId = face = turn = atk = def = 0;
                        }
                        if (!ok)
                        {
                            continue;
                        }

                        bool faceKnown = face == 0 || face == 1;
                        bool faceUp = faceKnown && face == 1;
                        bool publicStats = player == ownedSeat || faceUp;
                        bool turnDomainKnown = attackTurnRaw.HasValue
                            && defenseTurnRaw.HasValue
                            && attackTurnRaw.Value != defenseTurnRaw.Value;
                        bool attack = turnDomainKnown
                            && turn == attackTurnRaw.Value;
                        bool defense = turnDomainKnown
                            && turn == defenseTurnRaw.Value;

                        result.Add(new CampaignCpuMonsterTacticalState
                        {
                            Player = player,
                            Position = position,
                            Index = index,
                            UniqueId = uniqueId,
                            CardId = cardId,
                            FaceKnown = faceKnown,
                            FaceUp = faceUp,
                            TurnRaw = turn,
                            TurnKnown = attack || defense,
                            IsAttack = attack,
                            IsDefense = defense,
                            HasAtk = publicStats && atk >= 0,
                            Atk = atk,
                            HasDef = publicStats && def >= 0,
                            Def = def,
                        });
                    }
                }
            }
            return result;
        }
    }
}

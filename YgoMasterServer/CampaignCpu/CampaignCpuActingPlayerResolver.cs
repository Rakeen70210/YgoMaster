namespace YgoMaster
{
    /// <summary>
    /// Pure acting-player resolution for solo CampaignCpu.
    /// Logic forked from LlmBrokerPlayerResolver (isolation: do not call LLM type).
    /// </summary>
    static class CampaignCpuActingPlayerResolver
    {
        public static bool TryResolve(
            DuelViewType viewType,
            int doCommandUser,
            int runDialogUser,
            int param1,
            int myId,
            int turnPlayer,
            out int player)
        {
            player = -1;
            switch (viewType)
            {
                case DuelViewType.WaitInput:
                    // Contextual prompts can belong to the non-turn player.
                    if (param1 >= (int)DuelMenuActType.CheckTiming &&
                        param1 <= (int)DuelMenuActType.LockOn)
                    {
                        return TryResolveReportedOrTurn(
                            doCommandUser, turnPlayer, out player);
                    }
                    player = IsPlayerIndex(turnPlayer) ? turnPlayer : doCommandUser;
                    return IsPlayerIndex(player);
                case DuelViewType.RunDialog:
                    // 1 = YgomGame.Duel.Engine.DialogType.Info
                    if (param1 == 1)
                    {
                        return false;
                    }
                    return TryResolveReportedOrTurn(runDialogUser, turnPlayer, out player);
                case DuelViewType.RunList:
                    return TryResolveReportedOrTurn(param1, turnPlayer, out player);
            }
            return false;
        }

        static bool TryResolveReportedOrTurn(
            int reportedPlayer,
            int turnPlayer,
            out int player)
        {
            if (IsPlayerIndex(reportedPlayer))
            {
                player = reportedPlayer;
                return true;
            }
            if (IsPlayerIndex(turnPlayer))
            {
                player = turnPlayer;
                return true;
            }
            player = -1;
            return false;
        }

        public static bool IsPlayerIndex(int player)
        {
            return player >= 0 && player <= 1;
        }
    }
}

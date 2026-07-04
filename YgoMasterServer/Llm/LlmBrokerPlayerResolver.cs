namespace YgoMaster
{
    static class LlmBrokerPlayerResolver
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
                    player = IsPlayerIndex(turnPlayer) ? turnPlayer : doCommandUser;
                    return IsPlayerIndex(player);
                case DuelViewType.RunDialog:
                    // 1 = YgomGame.Duel.Engine.DialogType.Info
                    if (param1 == 1)
                    {
                        return false;
                    }
                    return TryResolveReportedOrRivalTurn(runDialogUser, myId, turnPlayer, out player);
                case DuelViewType.RunList:
                    return TryResolveReportedOrRivalTurn(param1, myId, turnPlayer, out player);
            }
            return false;
        }

        static bool TryResolveReportedOrRivalTurn(
            int reportedPlayer,
            int myId,
            int turnPlayer,
            out int player)
        {
            if (IsPlayerIndex(reportedPlayer) && reportedPlayer != myId)
            {
                player = reportedPlayer;
                return true;
            }
            if (IsPlayerIndex(turnPlayer) && turnPlayer != myId)
            {
                player = turnPlayer;
                return true;
            }
            if (IsPlayerIndex(reportedPlayer))
            {
                player = reportedPlayer;
                return true;
            }
            player = -1;
            return false;
        }

        static bool IsPlayerIndex(int player)
        {
            return player >= 0 && player <= 1;
        }
    }
}

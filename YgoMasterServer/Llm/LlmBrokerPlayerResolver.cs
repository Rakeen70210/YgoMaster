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
                    // Contextual prompts can belong to the non-turn player, so
                    // use the absolute DLL-reported command seat. Phase menus
                    // retain the turn-player rule because DoCommandUser can be stale.
                    if (param1 >= (int)DuelMenuActType.CheckTiming &&
                        param1 <= (int)DuelMenuActType.LockOn)
                    {
                        return TryResolveReportedOrRivalTurn(
                            doCommandUser, myId, turnPlayer, out player);
                    }
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
            // DoCommandUser / RunDialogUser are absolute seat ids broadcast
            // identically to both clients. Never interpret them relative to myId:
            // that made the two clients disagree on acting_player for the same
            // seq (live hang 2026-07-09: ROOT saw RunDialog acting=1 while P2
            // rewrote the same reported local seat to turnPlayer).
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

        static bool IsPlayerIndex(int player)
        {
            return player >= 0 && player <= 1;
        }
    }
}

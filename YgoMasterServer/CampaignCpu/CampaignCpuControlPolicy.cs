namespace YgoMaster
{
    /// <summary>
    /// Solo control polarity: own opponent seat only; never MyID.
    /// Inverse of LLM (which requires player == myId == controlPlayer).
    /// </summary>
    static class CampaignCpuControlPolicy
    {
        public static int ResolveOwnedSeat(int myId)
        {
            return myId == 0 ? 1 : 0;
        }

        public static bool IsOwnedOpponentSeat(int actingPlayer, int ownedSeat, int myId)
        {
            if (!CampaignCpuActingPlayerResolver.IsPlayerIndex(actingPlayer))
            {
                return false;
            }
            if (actingPlayer == myId)
            {
                return false;
            }
            return actingPlayer == ownedSeat;
        }

        public static bool ShouldSuppressOriginal(
            bool gateActive,
            SoloTemporaryCpuState state,
            int actingPlayer,
            int ownedSeat,
            int myId,
            CampaignCpuRoute route)
        {
            if (!gateActive)
            {
                return false;
            }
            if (state == SoloTemporaryCpuState.NativeLease
                || state == SoloTemporaryCpuState.Inactive)
            {
                return false;
            }
            if (!IsOwnedOpponentSeat(actingPlayer, ownedSeat, myId))
            {
                return false;
            }
            return route == CampaignCpuRoute.RuleCommit
                || route == CampaignCpuRoute.MechanicalAuto;
        }
    }
}

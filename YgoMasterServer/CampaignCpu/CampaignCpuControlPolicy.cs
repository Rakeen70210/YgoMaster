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

        // YgomGame.Duel.Util.GameMode numeric values (keep as ints so pure CampaignCpu
        // sources stay linkable without the full enum unit in every csproj).
        const int GameModeNormal = 0;
        const int GameModeSingle = 2;
        const int GameModeAudience = 6;
        const int GameModeReplay = 7;
        const int GameModeSoloSingle = 9;
        const int GameModeRoom = 10;

        /// <summary>
        /// Whether this duel begin is a campaign/solo path CampaignCpu may arm for.
        /// Live SoloDuels often leave GameMode at 0 (Normal); the server already treats 0 as
        /// SoloSingle when writing replay mode. Reject Room/Audience/Replay always.
        /// Chapter presence is checked separately (non-solo Normal stays inert via chapter_missing).
        /// </summary>
        public static bool IsEligibleSoloCampaignMode(
            int gameMode,
            bool isPvpDuel,
            bool isPvpSpectator)
        {
            if (isPvpDuel || isPvpSpectator)
            {
                return false;
            }
            if (gameMode == GameModeRoom
                || gameMode == GameModeAudience
                || gameMode == GameModeReplay)
            {
                return false;
            }
            // Official solo + the default/zero GameMode used by SoloDuels data.
            return gameMode == GameModeSoloSingle
                || gameMode == GameModeNormal
                || gameMode == GameModeSingle;
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

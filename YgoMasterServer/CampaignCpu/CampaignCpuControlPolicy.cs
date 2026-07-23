namespace YgoMaster
{
    /// <summary>
    /// Solo control polarity: own opponent seat only; never MyID.
    /// Inverse of LLM (which requires player == myId == controlPlayer).
    /// </summary>
    static class CampaignCpuControlPolicy
    {
        /// <summary>MyID must be a real duel seat before OwnedSeat is derived.</summary>
        public static bool IsValidMyId(int myId)
        {
            return myId == 0 || myId == 1;
        }

        /// <summary>
        /// Resolve OwnedSeat only when MyID is 0|1. Invalid MyID leaves ownedSeat=-1 and returns false
        /// so the gate stays inactive (never maps -1 → seat 0).
        /// </summary>
        public static bool TryResolveOwnedSeat(int myId, out int ownedSeat)
        {
            if (!IsValidMyId(myId))
            {
                ownedSeat = -1;
                return false;
            }
            ownedSeat = myId == 0 ? 1 : 0;
            return true;
        }

        /// <summary>
        /// Legacy helper for tests/callers that already validated MyID.
        /// Invalid MyID returns -1 (not seat 0).
        /// </summary>
        public static int ResolveOwnedSeat(int myId)
        {
            int owned;
            if (!TryResolveOwnedSeat(myId, out owned))
            {
                return -1;
            }
            return owned;
        }

        /// <summary>DLL_DuelIsHuman returns 1 for Human; only that counts as confirmed.</summary>
        public static bool IsPositiveHumanReadback(int isHumanReadback)
        {
            return isHumanReadback == 1;
        }

        /// <summary>
        /// Seat ownership assert succeeds only when both OwnedSeat and MyID read back Human.
        /// </summary>
        public static bool IsSeatOwnershipConfirmed(int ownedIsHumanReadback, int myIsHumanReadback)
        {
            return IsPositiveHumanReadback(ownedIsHumanReadback)
                && IsPositiveHumanReadback(myIsHumanReadback);
        }

        /// <summary>
        /// After this many failed ready-engine assert attempts, disable scripting for the duel.
        /// </summary>
        public const int MaxSeatAssertAttempts = 8;

        public static bool ShouldDisableScriptingAfterSeatAssertFailures(int failedAttempts)
        {
            return failedAttempts >= MaxSeatAssertAttempts;
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

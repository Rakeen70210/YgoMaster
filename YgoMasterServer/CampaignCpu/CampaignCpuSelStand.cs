using System;

namespace YgoMaster
{
    /// <summary>
    /// Pure helpers for commit-on post-summon SelStand (battle position) auto-resolve.
    /// Live M7 (2026-07-23): after scripted SummonSp, dual-Human attributes RunDialog/SelStand
    /// to MyId; A4 TemporaryCpu cannot answer MyId-bound dialogs and originalRunEffect paints UI.
    /// </summary>
    static class CampaignCpuSelStand
    {
        /// <summary>Ygom / classic stand bit: face-up attack (preferred mechanical default).</summary>
        public const int StandFaceUpAttack = 0x1;
        /// <summary>Face-down attack (rare for normal summons).</summary>
        public const int StandFaceDownAttack = 0x2;
        /// <summary>Face-up defense.</summary>
        public const int StandFaceUpDefense = 0x4;
        /// <summary>Face-down defense (sets).</summary>
        public const int StandFaceDownDefense = 0x8;

        public const string TargetScope = "sel_stand";
        public const string RuleId = "mechanical_sel_stand";
        public const string Reason = "owned_turn_sel_stand";

        /// <summary>
        /// Commit-on gate: HumanOwned-era intercept eligibility (controller also checks state).
        /// Disabled under LogOnly / when scripted commits are off / when seats collapse.
        /// </summary>
        public static bool ShouldIntercept(
            bool logOnly,
            bool allowScriptedCommits,
            bool seatOwnershipConfirmed,
            bool scriptingDisabledForDuel,
            SoloTemporaryCpuState state,
            DuelViewType viewType,
            int param1,
            int turnPlayer,
            int ownedSeat,
            int myId)
        {
            if (logOnly || !allowScriptedCommits || scriptingDisabledForDuel || !seatOwnershipConfirmed)
            {
                return false;
            }
            if (state != SoloTemporaryCpuState.HumanOwned)
            {
                return false;
            }
            return CampaignCpuWindowClassifier.IsOwnedTurnSelStand(
                viewType, param1, turnPlayer, ownedSeat, myId);
        }

        /// <summary>
        /// Prefer face-up attack when legal; else face-up defense; else lowest set bit.
        /// Mask 0 → fail closed (no blind default).
        /// </summary>
        public static bool TryPickStandFromMask(int positionMask, out int stand)
        {
            stand = 0;
            if (positionMask == 0)
            {
                return false;
            }
            if ((positionMask & StandFaceUpAttack) != 0)
            {
                stand = StandFaceUpAttack;
                return true;
            }
            if ((positionMask & StandFaceUpDefense) != 0)
            {
                stand = StandFaceUpDefense;
                return true;
            }
            // Lowest set bit (any remaining legal stand, including face-down variants).
            stand = positionMask & -positionMask;
            return stand != 0;
        }

        /// <summary>
        /// Mechanical DialogResult action for <see cref="CampaignCpuNativeCommit"/>.
        /// Position/Index carry the stand value so progress fingerprints differ per choice
        /// (CanonicalIdentity does not embed DialogResult today).
        /// </summary>
        public static bool TryBuildMechanicalAction(int positionMask, out CampaignCpuLegalAction action)
        {
            action = null;
            int stand;
            if (!TryPickStandFromMask(positionMask, out stand))
            {
                return false;
            }
            action = new CampaignCpuLegalAction
            {
                ActionId = 0,
                Kind = LegalActionKind.DialogResult,
                DialogResult = stand,
                Position = stand,
                Index = stand,
                IsMechanical = true,
                TargetScope = TargetScope,
                Label = "SelStand mechanical",
                Player = 0,
            };
            return true;
        }
    }
}

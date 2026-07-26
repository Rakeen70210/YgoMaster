using System;

namespace YgoMaster
{
    /// <summary>
    /// Pure helpers for commit-on post-summon SelStand (battle position) auto-resolve.
    /// Live M7 (2026-07-23): after scripted SummonSp, dual-Human attributes RunDialog/SelStand
    /// to MyId; A4 TemporaryCpu cannot answer MyId-bound dialogs and originalRunEffect paints UI.
    ///
    /// Stand encoding notes:
    /// - Classic low bits (0x1/0x2/0x4/0x8) appear in some YGO engines and remain preferred
    ///   when present in the mask.
    /// - Live Master Duel SelStand for the first-slice path used a high-bit mask
    ///   <see cref="LiveRecordedStandMask"/> (0x1F0000) with dialog result
    ///   <see cref="LiveRecordedStandResult"/> (0x10000 = lowest set bit). Kaiser summoned
    ///   face-up after that commit. Named classic constants alone do not describe MD masks.
    /// </summary>
    static class CampaignCpuSelStand
    {
        /// <summary>Classic low-bit stand: face-up attack (preferred when present in mask).</summary>
        public const int StandFaceUpAttack = 0x1;
        /// <summary>Classic low-bit: face-down attack (rare for normal summons).</summary>
        public const int StandFaceDownAttack = 0x2;
        /// <summary>Classic low-bit: face-up defense.</summary>
        public const int StandFaceUpDefense = 0x4;
        /// <summary>Classic low-bit: face-down defense (sets).</summary>
        public const int StandFaceDownDefense = 0x8;

        /// <summary>
        /// Live MD (mechanical_sel_stand, first-slice): position mask observed at intercept.
        /// Bits 16–20 set (0x1F0000). Not the same encoding as classic 0x1/0x4 constants.
        /// </summary>
        public const int LiveRecordedStandMask = 0x1F0000;
        /// <summary>
        /// Live MD dialog result for <see cref="LiveRecordedStandMask"/> via lowest-set-bit
        /// fallback (0x10000). Summoned Kaiser appeared face-up after this commit.
        /// </summary>
        public const int LiveRecordedStandResult = 0x10000;
        /// <summary>
        /// Preferred high-bit when classic low bits are absent (MD face-up ATK bit from live).
        /// Equal to <see cref="LiveRecordedStandResult"/>; named for picker preference order.
        /// </summary>
        public const int StandMdFaceUpAttackBit = 0x10000;

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
        /// Prefer classic face-up ATK (0x1) when legal; else classic face-up DEF (0x4);
        /// else MD high-bit face-up ATK (<see cref="StandMdFaceUpAttackBit"/>) when set;
        /// else lowest set bit (covers live 0x1F0000 → 0x10000).
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
            // Live MD high-bit face-up ATK (recorded 0x1F0000 mask → 0x10000). Prefer explicitly
            // before generic lowest-bit so comments/constants match production-shaped tests.
            if ((positionMask & StandMdFaceUpAttackBit) != 0)
            {
                stand = StandMdFaceUpAttackBit;
                return true;
            }
            // Lowest set bit (any remaining legal stand, including face-down / other high bits).
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

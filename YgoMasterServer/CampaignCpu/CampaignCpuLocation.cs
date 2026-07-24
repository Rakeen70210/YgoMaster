using System;

namespace YgoMaster
{
    /// <summary>
    /// Pure helpers for commit-on post-action WaitInput/Location (zone placement) auto-resolve.
    /// Live M7 (2026-07-23): after scripted Activate (Future Fusion) or similar, dual-Human
    /// attributes WaitInput_Location to MyId; A4 TemporaryCpu cannot answer MyId-bound menus
    /// and originalRunEffect paints "Select position for …" on the human.
    /// Prefer DLL_DuelComDefaultLocation (live-proven): Decide@PosSelect from
    /// DlgGetPosMaskOfThisSummon is unreliable for dual-Human WaitInput/Location —
    /// low bits can look like monster zones while the engine still re-prompts, then
    /// AwaitingProgress suppresses the same view and post_commit_stall freezes Main.
    /// High-bit S/T masks (e.g. 0x1F0000) also sit above PosField and cannot be walked.
    /// Keep Decide@PosSelect as a pure helper for harness/mask experiments only.
    /// </summary>
    static class CampaignCpuLocation
    {
        /// <summary>Ygom field zone bit walk upper bound (matches LegalActionExtractor.PosField).</summary>
        public const int PosField = 12;

        public const string TargetScope = "summon_placement";
        /// <summary>Engine default zone pick — preferred live transport for Location intercept.</summary>
        public const string DefaultLocationScope = "default_location";
        public const string RuleId = "mechanical_location";
        public const string RuleIdDefault = "mechanical_location_default";
        public const string Reason = "owned_turn_location";
        public const string ReasonDefault = "owned_turn_location_default";

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
            return CampaignCpuWindowClassifier.IsOwnedTurnLocation(
                viewType, param1, turnPlayer, ownedSeat, myId);
        }

        /// <summary>
        /// Resolve placing-card unique id: prefer dialog API, else WaitInput Location param2
        /// (LLM path / live PvP: view_param2 carries the unique id).
        /// </summary>
        public static int ResolveCardUniqueId(int dlgProcUniqueId, int viewParam2)
        {
            if (dlgProcUniqueId > 0)
            {
                return dlgProcUniqueId;
            }
            if (viewParam2 > 0)
            {
                return viewParam2;
            }
            return 0;
        }

        /// <summary>
        /// Prefer lowest legal field zone index (bit 0 = zone 0). Mask 0 → fail closed.
        /// </summary>
        public static bool TryPickZoneFromMask(int positionMask, out int zone)
        {
            zone = -1;
            if (positionMask == 0)
            {
                return false;
            }
            for (int position = 0; position <= PosField; position++)
            {
                int positionBit = 1 << position;
                if ((positionMask & positionBit) != 0)
                {
                    zone = position;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Mechanical Decide placement action for <see cref="CampaignCpuNativeCommit"/>.
        /// Position holds the field zone; TargetScope summon_placement rewrites to
        /// DoCommand(player, PosSelect, zone, Decide). Player must be OwnedSeat.
        /// </summary>
        public static bool TryBuildMechanicalAction(
            int positionMask,
            int ownedSeat,
            int cardId,
            out CampaignCpuLegalAction action)
        {
            action = null;
            if (ownedSeat < 0)
            {
                return false;
            }
            int zone;
            if (!TryPickZoneFromMask(positionMask, out zone))
            {
                return false;
            }
            action = new CampaignCpuLegalAction
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Decide,
                Position = zone,
                Index = 0,
                CardId = cardId > 0 ? cardId : 0,
                IsMechanical = true,
                TargetScope = TargetScope,
                Label = "Location mechanical",
                Player = ownedSeat,
            };
            return true;
        }

        /// <summary>
        /// Engine DefaultLocation when placement mask is empty at RunEffect intercept.
        /// Committed via <see cref="CampaignCpuNativeCommit"/> defaultLocation delegate.
        /// </summary>
        public static bool TryBuildDefaultLocationAction(
            int ownedSeat,
            int cardId,
            out CampaignCpuLegalAction action)
        {
            action = null;
            if (ownedSeat < 0)
            {
                return false;
            }
            action = new CampaignCpuLegalAction
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Decide,
                Position = 0,
                Index = 0,
                CardId = cardId > 0 ? cardId : 0,
                IsMechanical = true,
                TargetScope = DefaultLocationScope,
                Label = "Location default",
                Player = ownedSeat,
            };
            return true;
        }

        public static bool IsDefaultLocationAction(CampaignCpuLegalAction action)
        {
            return action != null
                && string.Equals(action.TargetScope, DefaultLocationScope, StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolve mechanical placement: always DefaultLocation for live commit-on.
        /// <paramref name="positionMask"/> is retained for audit callers; Decide@mask is
        /// not used here after live M7 post_commit_stall (Decide@zone0, mask=31).
        /// </summary>
        public static bool TryResolveMechanicalPlacement(
            int positionMask,
            int ownedSeat,
            int cardId,
            out CampaignCpuLegalAction action,
            out string ruleId,
            out string reason)
        {
            action = null;
            ruleId = null;
            reason = null;
            // positionMask intentionally unused for transport selection (audit only).
            if (TryBuildDefaultLocationAction(ownedSeat, cardId, out action) && action != null)
            {
                ruleId = RuleIdDefault;
                reason = ReasonDefault;
                return true;
            }
            return false;
        }
    }
}

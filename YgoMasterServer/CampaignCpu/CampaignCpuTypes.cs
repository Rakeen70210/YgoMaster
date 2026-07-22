using System;

namespace YgoMaster
{
    /// <summary>
    /// Built-in defaults for CampaignCpu (engine-work seat offsets, return codes).
    /// ClientSettings offsets of 0 mean "use these constants".
    /// </summary>
    static class CampaignCpuDefaults
    {
        /// <summary>MD v2.5.0 DoCommandUser work offset (0x3C90) — same value as PvP docs.</summary>
        public const int DoCommandUserOffset = 15504;

        /// <summary>MD v2.5.0 RunDialogUser work offset (0x3C28).</summary>
        public const int RunDialogUserOffset = 15400;

        /// <summary>Provisional return when a scripted path handled a view (spike may adjust).</summary>
        public const int ScriptedHandledReturnCode = 0;

        public const int PosHand = 13;
        public const int PosSelect = 18;
        public const int DefaultProgressTimeoutMs = 2000;
        public const int DefaultAuditMaxLines = 50000;
    }

    enum CampaignCpuRoute
    {
        Inactive,
        PassThroughNative,
        RuleCommit,
        MechanicalAuto,
        FallbackNative,
        ShadowScore,
        Error
    }

    enum CampaignCpuCommitOutcome
    {
        /// <summary>Mapping/prevalidation failed before any native delegate invocation.</summary>
        NotStarted,
        /// <summary>Native delegate returned normally; progress still must be observed.</summary>
        Applied,
        /// <summary>Native delegate threw; may have partially mutated engine state.</summary>
        Indeterminate
    }

    enum SoloTemporaryCpuState
    {
        Inactive,
        HumanOwned,
        NativeLease,
        AwaitingProgress,
        CommitQuarantine
    }

    sealed class CampaignCpuLegalAction
    {
        public int ActionId;
        public LegalActionKind Kind;
        public DuelCommandType Command;
        public DuelPhase Phase;
        public int CardId;
        public int Position;
        public int Index;
        public int DialogResult;
        public bool CancelDecide;
        public string Label;
        public bool IsMechanical;
        public string TargetScope;
        public int Player;

        /// <summary>Stable identity for golden fixtures (not transient ActionId).</summary>
        public string CanonicalIdentity
        {
            get
            {
                return string.Format(
                    "{0}|{1}|{2}|{3}|{4}|{5}",
                    Kind,
                    Kind == LegalActionKind.MovePhase ? Phase.ToString() : Command.ToString(),
                    CardId,
                    Position,
                    Kind == LegalActionKind.MovePhase ? (int)Phase : Index,
                    TargetScope ?? string.Empty);
            }
        }
    }

    sealed class CampaignCpuDecision
    {
        public CampaignCpuRoute Route;
        public CampaignCpuLegalAction Action;
        public string Reason;
        public string RuleId;
        public int Score;
        public bool Matched;

        public static CampaignCpuDecision Native(string reason)
        {
            return new CampaignCpuDecision
            {
                Route = CampaignCpuRoute.FallbackNative,
                Reason = reason ?? "native",
                Matched = false,
            };
        }

        public static CampaignCpuDecision Commit(
            CampaignCpuRoute route,
            CampaignCpuLegalAction action,
            string reason,
            string ruleId,
            int score,
            bool matched)
        {
            return new CampaignCpuDecision
            {
                Route = route,
                Action = action,
                Reason = reason ?? string.Empty,
                RuleId = ruleId ?? string.Empty,
                Score = score,
                Matched = matched,
            };
        }
    }

    sealed class CampaignCpuProgressToken : IEquatable<CampaignCpuProgressToken>
    {
        public int DuelGeneration;
        public DuelViewType ViewType;
        public int ViewParam1;
        public int ViewParam2;
        public int ViewParam3;
        public int ActingSeat;
        public int Turn;
        public int Phase;
        public string LegalActionFingerprint;

        public static CampaignCpuProgressToken Create(
            int duelGeneration,
            DuelViewType viewType,
            int p1,
            int p2,
            int p3,
            int actingSeat,
            int turn,
            int phase,
            string legalActionFingerprint)
        {
            return new CampaignCpuProgressToken
            {
                DuelGeneration = duelGeneration,
                ViewType = viewType,
                ViewParam1 = p1,
                ViewParam2 = p2,
                ViewParam3 = p3,
                ActingSeat = actingSeat,
                Turn = turn,
                Phase = phase,
                LegalActionFingerprint = legalActionFingerprint ?? string.Empty,
            };
        }

        public bool Equals(CampaignCpuProgressToken other)
        {
            if (other == null)
            {
                return false;
            }
            return DuelGeneration == other.DuelGeneration
                && ViewType == other.ViewType
                && ViewParam1 == other.ViewParam1
                && ViewParam2 == other.ViewParam2
                && ViewParam3 == other.ViewParam3
                && ActingSeat == other.ActingSeat
                && Turn == other.Turn
                && Phase == other.Phase
                && string.Equals(
                    LegalActionFingerprint,
                    other.LegalActionFingerprint,
                    StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as CampaignCpuProgressToken);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + DuelGeneration;
                hash = hash * 31 + (int)ViewType;
                hash = hash * 31 + ViewParam1;
                hash = hash * 31 + ViewParam2;
                hash = hash * 31 + ViewParam3;
                hash = hash * 31 + ActingSeat;
                hash = hash * 31 + Turn;
                hash = hash * 31 + Phase;
                hash = hash * 31 + (LegalActionFingerprint != null
                    ? LegalActionFingerprint.GetHashCode()
                    : 0);
                return hash;
            }
        }

        public static bool IsFreshSemanticProgress(
            CampaignCpuProgressToken current,
            CampaignCpuProgressToken previous)
        {
            if (current == null)
            {
                return false;
            }
            if (previous == null)
            {
                return true;
            }
            if (current.DuelGeneration != previous.DuelGeneration)
            {
                return true;
            }
            return !current.Equals(previous);
        }
    }

    static class CampaignCpuWindowClassifier
    {
        public static bool IsMainPhaseWaitInput(DuelViewType viewType, int param1)
        {
            return viewType == DuelViewType.WaitInput
                && param1 == (int)DuelMenuActType.MainPhase;
        }

        /// <summary>
        /// Draw-phase WaitInput only. Used for TemporaryCpu one-shot restore so a long
        /// Draw lease does not eat rival Main extract, without restoring Human after every
        /// non-Main native window (dialogs / timing / battle).
        /// </summary>
        public static bool IsDrawPhaseWaitInput(DuelViewType viewType, int param1)
        {
            return viewType == DuelViewType.WaitInput
                && param1 == (int)DuelMenuActType.DrawPhase;
        }

        /// <summary>
        /// Under AllowScriptedCommits, one-shot Human restore after BeginFallback only for
        /// pure DrawPhase. All other native leases hold until handshake restore.
        /// </summary>
        public static bool ShouldOneShotRestoreAfterNativeForward(
            bool allowScriptedCommits,
            DuelViewType viewType,
            int param1)
        {
            return allowScriptedCommits && IsDrawPhaseWaitInput(viewType, param1);
        }

        /// <summary>
        /// Owned response windows that must re-lease TemporaryCpu when HumanOwned so MD
        /// does not paint opponent activate/timing UI on the local human seat. Info
        /// RunDialog (param1==1) is excluded — acting is unresolved.
        /// </summary>
        public static bool IsOwnedResponseNativeWindow(DuelViewType viewType, int param1)
        {
            if (viewType == DuelViewType.RunDialog)
            {
                // 1 = YgomGame.Duel.Engine.DialogType.Info — no acting seat.
                return param1 != 1;
            }
            if (viewType == DuelViewType.RunList)
            {
                return true;
            }
            if (viewType == DuelViewType.WaitInput)
            {
                return param1 == (int)DuelMenuActType.CheckTiming
                    || param1 == (int)DuelMenuActType.CheckChain;
            }
            return false;
        }

        /// <summary>
        /// A4 dual-Human residual: while HumanOwned, re-lease OwnedSeat→CPU for every
        /// response-class window before originalRunEffect — even when acting resolves as
        /// MyId. Live audit shows MD sets run_dialog_user/do_command_user to the local
        /// seat under dual-Human, so opponent trap/chain UI was pass_through_myid.
        /// </summary>
        public static bool ShouldReLeaseDualHumanResponseWindow(
            SoloTemporaryCpuState state,
            DuelViewType viewType,
            int param1)
        {
            return state == SoloTemporaryCpuState.HumanOwned
                && IsOwnedResponseNativeWindow(viewType, param1);
        }

        /// <summary>
        /// Audit reason for A4 re-lease: distinguishes owned-seat resolve from MyId/unknown
        /// misroute (both still re-lease; MyId/unknown is the live trap residual).
        /// </summary>
        public static string DualHumanResponseHoldReason(
            bool actingResolved,
            int actingPlayer,
            int ownedSeat,
            int myId)
        {
            if (actingResolved && actingPlayer == ownedSeat)
            {
                return "owned_response_hold";
            }
            if (actingResolved && actingPlayer == myId)
            {
                return "dual_human_myid_response_hold";
            }
            return "dual_human_unknown_response_hold";
        }

        public static string ClassifyWindow(DuelViewType viewType, int param1)
        {
            if (IsMainPhaseWaitInput(viewType, param1))
            {
                return "WaitInput_MainPhase";
            }
            if (viewType == DuelViewType.RunDialog)
            {
                return "RunDialog";
            }
            if (viewType == DuelViewType.RunList)
            {
                return "RunList";
            }
            if (viewType == DuelViewType.WaitInput)
            {
                return "WaitInput_" + ((DuelMenuActType)param1).ToString();
            }
            return "Unsupported";
        }

        public static bool IsScriptedWindow(
            DuelViewType viewType,
            int param1,
            CampaignCpuRulePack pack)
        {
            string windowClass = ClassifyWindow(viewType, param1);
            if (pack == null || pack.Policy == null || pack.Policy.ScriptedViews == null
                || pack.Policy.ScriptedViews.Count == 0)
            {
                return IsMainPhaseWaitInput(viewType, param1);
            }
            for (int i = 0; i < pack.Policy.ScriptedViews.Count; i++)
            {
                if (string.Equals(
                    pack.Policy.ScriptedViews[i],
                    windowClass,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

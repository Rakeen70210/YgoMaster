using System;
using System.Collections.Generic;

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
        /// <summary>
        /// Optional live BasicVal for a card at this legal action's current engine
        /// location. Unknown values must never be synthesized by policy code.
        /// </summary>
        public bool BasicLevelKnown;
        public int BasicLevel;
        public bool BasicAtkKnown;
        public int BasicAtk;
        public bool BasicDefKnown;
        public int BasicDef;

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
        public string TacticalFilterReason;
        public List<string> TacticalFilteredActionIdentities;
        public string LegalAfterTacticalFingerprint;
        public string ReplacedActionIdentity;

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
        /// <summary>
        /// When false, <see cref="LegalActionFingerprint"/> must not participate in freshness.
        /// Empty legal menu is known via fingerprint "empty"; unavailable is not the same.
        /// </summary>
        public bool LegalFingerprintKnown;
        public string LegalActionFingerprint;

        /// <summary>
        /// Production check-path builder: same base fields as arming, legal fingerprint unknown.
        /// </summary>
        public static CampaignCpuProgressToken CreateWithoutLegalFingerprint(
            int duelGeneration,
            DuelViewType viewType,
            int p1,
            int p2,
            int p3,
            int actingSeat,
            int turn,
            int phase)
        {
            return CreateCore(
                duelGeneration,
                viewType,
                p1,
                p2,
                p3,
                actingSeat,
                turn,
                phase,
                legalFingerprintKnown: false,
                legalActionFingerprint: string.Empty);
        }

        /// <summary>
        /// Production arming/extract builder: legal fingerprint is known (empty menu → "empty").
        /// </summary>
        public static CampaignCpuProgressToken CreateWithLegalFingerprint(
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
            string fp = string.IsNullOrEmpty(legalActionFingerprint)
                ? "empty"
                : legalActionFingerprint;
            return CreateCore(
                duelGeneration,
                viewType,
                p1,
                p2,
                p3,
                actingSeat,
                turn,
                phase,
                legalFingerprintKnown: true,
                legalActionFingerprint: fp);
        }

        /// <summary>
        /// Backward-compatible factory: treats the supplied fingerprint as known.
        /// Prefer <see cref="CreateWithLegalFingerprint"/> / <see cref="CreateWithoutLegalFingerprint"/>.
        /// </summary>
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
            return CreateWithLegalFingerprint(
                duelGeneration,
                viewType,
                p1,
                p2,
                p3,
                actingSeat,
                turn,
                phase,
                legalActionFingerprint);
        }

        static CampaignCpuProgressToken CreateCore(
            int duelGeneration,
            DuelViewType viewType,
            int p1,
            int p2,
            int p3,
            int actingSeat,
            int turn,
            int phase,
            bool legalFingerprintKnown,
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
                LegalFingerprintKnown = legalFingerprintKnown,
                LegalActionFingerprint = legalActionFingerprint ?? string.Empty,
            };
        }

        public bool BaseFieldsEqual(CampaignCpuProgressToken other)
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
                && Phase == other.Phase;
        }

        public bool Equals(CampaignCpuProgressToken other)
        {
            if (other == null)
            {
                return false;
            }
            return BaseFieldsEqual(other)
                && LegalFingerprintKnown == other.LegalFingerprintKnown
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
                hash = hash * 31 + (LegalFingerprintKnown ? 1 : 0);
                hash = hash * 31 + (LegalActionFingerprint != null
                    ? LegalActionFingerprint.GetHashCode()
                    : 0);
                return hash;
            }
        }

        /// <summary>
        /// Freshness for AwaitingProgress / CommitQuarantine / lease restore.
        /// Base-field change is always fresh. Legal-menu change is fresh only when both
        /// fingerprints are known and differ. Stored-known / current-unknown is not fresh.
        /// </summary>
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
            if (!current.BaseFieldsEqual(previous))
            {
                return true;
            }
            // Base fields identical: fingerprint may only prove progress when both known.
            if (!current.LegalFingerprintKnown || !previous.LegalFingerprintKnown)
            {
                return false;
            }
            return !string.Equals(
                current.LegalActionFingerprint,
                previous.LegalActionFingerprint,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Pure post-commit progress / quarantine gate used by production controller and harness.
    /// </summary>
    enum CampaignCpuProgressCheckResult
    {
        /// <summary>Same-view / indeterminate: no originalRunEffect, return scripted handled.</summary>
        SuppressSameView,
        /// <summary>AwaitingProgress observed true progress; clear watch and continue routing.</summary>
        ProgressObserved,
        /// <summary>CommitQuarantine saw a truly fresh view; disable scripting and native-forward once.</summary>
        FreshAfterQuarantine,
    }

    static class CampaignCpuProgressCheck
    {
        public static CampaignCpuProgressCheckResult EvaluateAwaitingProgress(
            CampaignCpuProgressToken current,
            CampaignCpuProgressToken committed)
        {
            return CampaignCpuProgressToken.IsFreshSemanticProgress(current, committed)
                ? CampaignCpuProgressCheckResult.ProgressObserved
                : CampaignCpuProgressCheckResult.SuppressSameView;
        }

        public static CampaignCpuProgressCheckResult EvaluateCommitQuarantine(
            CampaignCpuProgressToken current,
            CampaignCpuProgressToken committed)
        {
            return CampaignCpuProgressToken.IsFreshSemanticProgress(current, committed)
                ? CampaignCpuProgressCheckResult.FreshAfterQuarantine
                : CampaignCpuProgressCheckResult.SuppressSameView;
        }

        /// <summary>
        /// Production check-path token builder. Base fields always populated from the same
        /// sources as arming. When <paramref name="legalFingerprintKnown"/> is true, a
        /// read-only extract supplied the fingerprint (empty menu → "empty"). When false,
        /// fingerprint is unavailable and must not prove freshness.
        /// </summary>
        public static CampaignCpuProgressToken BuildCheckToken(
            int duelGeneration,
            DuelViewType viewType,
            int p1,
            int p2,
            int p3,
            int actingSeat,
            int turn,
            int phase,
            bool legalFingerprintKnown,
            string legalActionFingerprint)
        {
            if (legalFingerprintKnown)
            {
                return CampaignCpuProgressToken.CreateWithLegalFingerprint(
                    duelGeneration,
                    viewType,
                    p1,
                    p2,
                    p3,
                    actingSeat,
                    turn,
                    phase,
                    legalActionFingerprint);
            }
            return CampaignCpuProgressToken.CreateWithoutLegalFingerprint(
                duelGeneration,
                viewType,
                p1,
                p2,
                p3,
                actingSeat,
                turn,
                phase);
        }

        /// <summary>
        /// Whether a check-path re-extract of the legal menu is considered safe for the view.
        /// Main WaitInput is the only production extract surface in v1 (same as scripted windows).
        /// </summary>
        public static bool IsSafeLegalFingerprintExtractView(DuelViewType viewType, int param1)
        {
            return CampaignCpuWindowClassifier.IsMainPhaseWaitInput(viewType, param1);
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
        /// RunDialog SelStand (battle position ATK/DEF). param1 == DuelDialogType.SelStand (10).
        /// </summary>
        public static bool IsSelStandRunDialog(DuelViewType viewType, int param1)
        {
            return viewType == DuelViewType.RunDialog
                && param1 == (int)DuelDialogType.SelStand;
        }

        /// <summary>
        /// Owned-turn SelStand under dual-Human: turn is OwnedSeat, seats are distinct.
        /// Acting often misroutes to MyId — turn_player is the ownership signal (A5 family).
        /// Human-turn SelStand must never match (PR2b: no CampaignCpu on MyId).
        /// </summary>
        public static bool IsOwnedTurnSelStand(
            DuelViewType viewType,
            int param1,
            int turnPlayer,
            int ownedSeat,
            int myId)
        {
            if (!IsSelStandRunDialog(viewType, param1))
            {
                return false;
            }
            if (ownedSeat < 0 || myId < 0 || ownedSeat == myId)
            {
                return false;
            }
            return turnPlayer == ownedSeat;
        }

        /// <summary>
        /// WaitInput Location (field zone placement). param1 == DuelMenuActType.Location (7).
        /// </summary>
        public static bool IsLocationWaitInput(DuelViewType viewType, int param1)
        {
            return viewType == DuelViewType.WaitInput
                && param1 == (int)DuelMenuActType.Location;
        }

        /// <summary>
        /// Owned-turn Location under dual-Human: turn is OwnedSeat, seats are distinct.
        /// Acting often misroutes to MyId (do_command_user) — turn_player is the ownership
        /// signal (same family as SelStand / stale MyId Main). Human-turn Location must
        /// never match (PR2b: human places their own cards via pass_through_myid).
        /// </summary>
        public static bool IsOwnedTurnLocation(
            DuelViewType viewType,
            int param1,
            int turnPlayer,
            int ownedSeat,
            int myId)
        {
            if (!IsLocationWaitInput(viewType, param1))
            {
                return false;
            }
            if (ownedSeat < 0 || myId < 0 || ownedSeat == myId)
            {
                return false;
            }
            return turnPlayer == ownedSeat;
        }

        /// <summary>
        /// A4 dual-Human residual: while HumanOwned, re-lease OwnedSeat→CPU for every
        /// response-class window before originalRunEffect — even when acting resolves as
        /// MyId. Live audit shows MD sets run_dialog_user/do_command_user to the local
        /// seat under dual-Human, so opponent trap/chain UI was pass_through_myid.
        /// Commit-on owned-turn SelStand and Location are handled before this gate
        /// (CampaignCpuSelStand / CampaignCpuLocation). Location is NOT response-class —
        /// mechanical answer is required; TemporaryCpu alone still paints MyId UI.
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

        /// <summary>
        /// True for Main1/Main2 phase ids (not Battle/End/Draw/Standby).
        /// </summary>
        public static bool IsMainPhaseId(int phaseId)
        {
            return phaseId == (int)DuelPhase.Main1 || phaseId == (int)DuelPhase.Main2;
        }

        /// <summary>
        /// A5 owned-Main capture boundary: PhaseChange entering Main1/Main2 for OwnedSeat.
        /// Live (2026-07-22): PhaseChange.param1 = seat, param2 = **new** phase id;
        /// DLL_DuelGetCurrentPhase still reports the **old** phase at callback time.
        /// Prefer param2 over current phase. Does not apply to response windows.
        /// </summary>
        public static bool IsOwnedMainCapturePhaseChange(
            DuelViewType viewType,
            int param1,
            int param2,
            int turnPlayer,
            int ownedSeat)
        {
            if (viewType != DuelViewType.PhaseChange)
            {
                return false;
            }
            if (turnPlayer != ownedSeat || param1 != ownedSeat)
            {
                return false;
            }
            return IsMainPhaseId(param2);
        }

        /// <summary>
        /// Stale MyID authority on an owned-turn Main WaitInput: under dual-Human residual,
        /// MD can report acting=MyId while turn_player is OwnedSeat. Never score/commit that
        /// sample as owned Main; fail closed to native lease instead of pass_through_myid.
        /// </summary>
        public static bool IsStaleMyIdOwnedMainWaitInput(
            DuelViewType viewType,
            int param1,
            bool actingResolved,
            int actingPlayer,
            int turnPlayer,
            int ownedSeat,
            int myId)
        {
            if (!IsMainPhaseWaitInput(viewType, param1))
            {
                return false;
            }
            if (turnPlayer != ownedSeat)
            {
                return false;
            }
            return actingResolved && actingPlayer == myId;
        }

        /// <summary>
        /// M3 owned-Main handoff probe: candidate views while NativeLease is active.
        /// Instrumentation only — does not change restore policy.
        /// </summary>
        public static bool IsNativeLeaseBoundaryProbeView(DuelViewType viewType, int param1)
        {
            if (viewType == DuelViewType.TurnChange
                || viewType == DuelViewType.PhaseChange
                || viewType == DuelViewType.CpuThinking
                || viewType == DuelViewType.CutinDraw)
            {
                return true;
            }
            if (viewType == DuelViewType.WaitInput)
            {
                return param1 == (int)DuelMenuActType.DrawPhase
                    || param1 == (int)DuelMenuActType.MainPhase;
            }
            return false;
        }

        /// <summary>
        /// Stable probe kind string for M3 boundary audits (includes PhaseChange/TurnChange).
        /// </summary>
        public static string BoundaryProbeKind(DuelViewType viewType, int param1)
        {
            if (viewType == DuelViewType.WaitInput)
            {
                if (param1 == (int)DuelMenuActType.DrawPhase)
                {
                    return "WaitInput_DrawPhase";
                }
                if (param1 == (int)DuelMenuActType.MainPhase)
                {
                    return "WaitInput_MainPhase";
                }
                return "WaitInput_" + ((DuelMenuActType)param1).ToString();
            }
            if (viewType == DuelViewType.TurnChange
                || viewType == DuelViewType.PhaseChange
                || viewType == DuelViewType.CpuThinking
                || viewType == DuelViewType.CutinDraw)
            {
                return viewType.ToString();
            }
            return ClassifyWindow(viewType, param1);
        }

        public static string ClassifyWindow(DuelViewType viewType, int param1)
        {
            if (IsMainPhaseWaitInput(viewType, param1))
            {
                return "WaitInput_MainPhase";
            }
            if (viewType == DuelViewType.TurnChange)
            {
                return "TurnChange";
            }
            if (viewType == DuelViewType.PhaseChange)
            {
                return "PhaseChange";
            }
            if (viewType == DuelViewType.CpuThinking)
            {
                return "CpuThinking";
            }
            if (viewType == DuelViewType.CutinDraw)
            {
                return "CutinDraw";
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

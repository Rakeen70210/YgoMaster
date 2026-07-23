using System;

namespace YgoMaster
{
    /// <summary>
    /// Production RunEffect effect kinds used by <see cref="CampaignCpuRunEffectRouter"/>
    /// and the client controller. Counts originalRunEffect invocations per entry.
    /// </summary>
    enum CampaignCpuEffectKind
    {
        /// <summary>Same-view / indeterminate: no original; return scripted handled.</summary>
        SuppressSameView,
        /// <summary>AwaitingProgress saw true progress: clear watch and continue routing.</summary>
        CompleteAwaitingProgress,
        /// <summary>CommitQuarantine saw a fresh view: disable scripting + BeginNativeLease + forward once.</summary>
        DisableAndBeginNativeLease,
        /// <summary>Pass-through MyID / non-owned / unknown: outer forwards original once.</summary>
        ForwardOriginal,
        /// <summary>Enter NativeLease, flip OwnedSeat to CPU (confirmed), forward original once inside BeginFallback.</summary>
        BeginNativeLeaseAndForward,
        /// <summary>CPU→Human confirmed: RestoreHumanOwned then continue routing this view.</summary>
        RestoreHumanThenContinue,
        /// <summary>Player-type transition failed/unknown: remain in current state, forward original once (fail-closed).</summary>
        TransitionFailedForwardOriginal,
        /// <summary>Scripted commit Applied: arm AwaitingProgress, no original.</summary>
        CommitApplied,
        /// <summary>Scripted commit Indeterminate: enter quarantine, no original.</summary>
        CommitIndeterminate,
    }

    /// <summary>
    /// Pure progress/quarantine and dual-driver routing helpers shared by production controller
    /// and the harness. Replaces the old test-only OuterAction counter as the production seam.
    /// </summary>
    static class CampaignCpuRunEffectRouter
    {
        /// <summary>
        /// Map a post-commit progress check while in AwaitingProgress / CommitQuarantine
        /// to the next controller effect. Production controller must execute this result.
        /// </summary>
        public static CampaignCpuEffectKind MapProgressGate(
            SoloTemporaryCpuState state,
            CampaignCpuProgressCheckResult check)
        {
            if (state == SoloTemporaryCpuState.CommitQuarantine)
            {
                return check == CampaignCpuProgressCheckResult.SuppressSameView
                    ? CampaignCpuEffectKind.SuppressSameView
                    : CampaignCpuEffectKind.DisableAndBeginNativeLease;
            }
            if (state == SoloTemporaryCpuState.AwaitingProgress)
            {
                return check == CampaignCpuProgressCheckResult.SuppressSameView
                    ? CampaignCpuEffectKind.SuppressSameView
                    : CampaignCpuEffectKind.CompleteAwaitingProgress;
            }
            // Not in a progress gate; callers should not use this helper.
            return CampaignCpuEffectKind.ForwardOriginal;
        }

        /// <summary>
        /// Owned-window gate when scripting is unavailable (always-native, disabled, no pack,
        /// seat unconfirmed, non-scripted window). Always BeginFallback.
        /// </summary>
        public static CampaignCpuEffectKind MapOwnedWindowForcedNative()
        {
            return CampaignCpuEffectKind.BeginNativeLeaseAndForward;
        }

        /// <summary>
        /// Human-seat / non-owned / unresolved acting: single outer forward.
        /// </summary>
        public static CampaignCpuEffectKind MapPassThrough()
        {
            return CampaignCpuEffectKind.ForwardOriginal;
        }

        /// <summary>
        /// A4 dual-Human response hold or stale MyID Main: re-lease CPU then forward once.
        /// </summary>
        public static CampaignCpuEffectKind MapDualHumanResponseHold()
        {
            return CampaignCpuEffectKind.BeginNativeLeaseAndForward;
        }

        /// <summary>
        /// After a confirmed player-type transition attempt.
        /// </summary>
        public static CampaignCpuEffectKind MapTransitionResult(
            CampaignCpuPlayerTypeTransitionResult result,
            bool restoreHumanPath)
        {
            if (result == CampaignCpuPlayerTypeTransitionResult.Confirmed)
            {
                return restoreHumanPath
                    ? CampaignCpuEffectKind.RestoreHumanThenContinue
                    : CampaignCpuEffectKind.BeginNativeLeaseAndForward;
            }
            return CampaignCpuEffectKind.TransitionFailedForwardOriginal;
        }

        /// <summary>
        /// Map commit outcome to a terminal effect (LogOnly/NotStarted are not terminal here —
        /// controller maps those to BeginNativeLeaseAndForward).
        /// </summary>
        public static CampaignCpuEffectKind MapCommitOutcome(CampaignCpuCommitOutcome outcome)
        {
            if (outcome == CampaignCpuCommitOutcome.Applied)
            {
                return CampaignCpuEffectKind.CommitApplied;
            }
            if (outcome == CampaignCpuCommitOutcome.Indeterminate)
            {
                return CampaignCpuEffectKind.CommitIndeterminate;
            }
            // NotStarted → controller BeginFallback
            return CampaignCpuEffectKind.BeginNativeLeaseAndForward;
        }

        /// <summary>
        /// Exact-once originalRunEffect contract for terminal effects.
        /// CompleteAwaitingProgress / RestoreHumanThenContinue are non-terminal (0 here;
        /// outer continues and will count its eventual terminal effect).
        /// </summary>
        public static int CountOriginalInvocations(CampaignCpuEffectKind effect)
        {
            switch (effect)
            {
                case CampaignCpuEffectKind.SuppressSameView:
                case CampaignCpuEffectKind.CompleteAwaitingProgress:
                case CampaignCpuEffectKind.RestoreHumanThenContinue:
                case CampaignCpuEffectKind.CommitApplied:
                case CampaignCpuEffectKind.CommitIndeterminate:
                    return 0;
                case CampaignCpuEffectKind.DisableAndBeginNativeLease:
                case CampaignCpuEffectKind.ForwardOriginal:
                case CampaignCpuEffectKind.BeginNativeLeaseAndForward:
                case CampaignCpuEffectKind.TransitionFailedForwardOriginal:
                    return 1;
                default:
                    throw new ArgumentOutOfRangeException("effect");
            }
        }

        /// <summary>
        /// Simulate the dual-driver invariant with a counting original for harness tests.
        /// </summary>
        public static int CountOriginalInvocations(
            CampaignCpuEffectKind effect,
            Func<int> originalOnce)
        {
            if (originalOnce == null)
            {
                throw new ArgumentNullException("originalOnce");
            }
            int expected = CountOriginalInvocations(effect);
            int calls = 0;
            for (int i = 0; i < expected; i++)
            {
                calls++;
                originalOnce();
            }
            return calls;
        }
    }

    /// <summary>
    /// Backward-compatible aliases used by older harness names. Prefer
    /// <see cref="CampaignCpuRunEffectRouter"/> + <see cref="CampaignCpuEffectKind"/>.
    /// </summary>
    static class CampaignCpuRunEffectContract
    {
        public enum OuterAction
        {
            ForwardOriginalOnce = 0,
            ScriptedHandledNoOriginal = 1,
            BeginFallbackThenReturn = 2,
        }

        public static int CountOriginalInvocations(
            OuterAction action,
            Func<int> originalOnce)
        {
            CampaignCpuEffectKind kind;
            switch (action)
            {
                case OuterAction.ForwardOriginalOnce:
                    kind = CampaignCpuEffectKind.ForwardOriginal;
                    break;
                case OuterAction.ScriptedHandledNoOriginal:
                    kind = CampaignCpuEffectKind.SuppressSameView;
                    break;
                case OuterAction.BeginFallbackThenReturn:
                    kind = CampaignCpuEffectKind.BeginNativeLeaseAndForward;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("action");
            }
            return CampaignCpuRunEffectRouter.CountOriginalInvocations(kind, originalOnce);
        }

        public static OuterAction DecideAlwaysNativeOwnedWindow()
        {
            return OuterAction.BeginFallbackThenReturn;
        }

        public static OuterAction DecidePassThrough()
        {
            return OuterAction.ForwardOriginalOnce;
        }
    }
}

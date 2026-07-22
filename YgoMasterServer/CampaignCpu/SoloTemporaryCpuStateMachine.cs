using System;

namespace YgoMaster
{
    sealed class SoloTemporaryCpuLease
    {
        public string Reason;
        public int DuelGeneration;
        public ulong EntryViewSeq;
        public CampaignCpuProgressToken EntryProgressToken;
        public int OwnedSeat;
        public DateTime EnteredUtc;
        public bool ObservedCpuThinking;
    }

    sealed class SoloTemporaryCpuCommittedWatch
    {
        public ulong CommittedViewSeq;
        public CampaignCpuProgressToken CommittedToken;
        public string ActionIdentity;
        public DateTime StartedMonotonicUtc;
    }

    /// <summary>
    /// Pure state transitions for solo TemporaryCpu. Pattern-only from LLM coordinator;
    /// does not reference LLM types.
    /// </summary>
    sealed class SoloTemporaryCpuStateMachine
    {
        public SoloTemporaryCpuState State { get; private set; }
        public SoloTemporaryCpuLease ActiveLease { get; private set; }
        public SoloTemporaryCpuCommittedWatch ProgressWatch { get; private set; }
        public SoloTemporaryCpuCommittedWatch QuarantineWatch { get; private set; }
        public bool ScriptingDisabledForDuel { get; private set; }
        public string ScriptingDisabledReason { get; private set; }

        public void Reset()
        {
            State = SoloTemporaryCpuState.Inactive;
            ActiveLease = null;
            ProgressWatch = null;
            QuarantineWatch = null;
            ScriptingDisabledForDuel = false;
            ScriptingDisabledReason = null;
        }

        public void ActivateHumanOwned()
        {
            State = SoloTemporaryCpuState.HumanOwned;
            ActiveLease = null;
            ProgressWatch = null;
            QuarantineWatch = null;
            ScriptingDisabledForDuel = false;
            ScriptingDisabledReason = null;
        }

        public void Deactivate()
        {
            Reset();
        }

        public void BeginNativeLease(
            string reason,
            int duelGeneration,
            ulong entryViewSeq,
            CampaignCpuProgressToken entryToken,
            int ownedSeat,
            DateTime enteredUtc)
        {
            ActiveLease = new SoloTemporaryCpuLease
            {
                Reason = reason ?? "native",
                DuelGeneration = duelGeneration,
                EntryViewSeq = entryViewSeq,
                EntryProgressToken = entryToken,
                OwnedSeat = ownedSeat,
                EnteredUtc = enteredUtc,
                ObservedCpuThinking = false,
            };
            ProgressWatch = null;
            State = SoloTemporaryCpuState.NativeLease;
        }

        public void MarkCpuThinkingObserved()
        {
            if (ActiveLease != null)
            {
                ActiveLease.ObservedCpuThinking = true;
            }
        }

        public void ArmAwaitingProgress(
            ulong committedViewSeq,
            CampaignCpuProgressToken committedToken,
            string actionIdentity,
            DateTime startedUtc)
        {
            ProgressWatch = new SoloTemporaryCpuCommittedWatch
            {
                CommittedViewSeq = committedViewSeq,
                CommittedToken = committedToken,
                ActionIdentity = actionIdentity ?? string.Empty,
                StartedMonotonicUtc = startedUtc,
            };
            State = SoloTemporaryCpuState.AwaitingProgress;
        }

        public void CompleteAwaitingProgress()
        {
            ProgressWatch = null;
            if (State == SoloTemporaryCpuState.AwaitingProgress)
            {
                State = SoloTemporaryCpuState.HumanOwned;
            }
        }

        public void EnterCommitQuarantine(
            ulong viewSeq,
            CampaignCpuProgressToken token,
            string actionIdentity,
            DateTime startedUtc,
            string reason)
        {
            QuarantineWatch = new SoloTemporaryCpuCommittedWatch
            {
                CommittedViewSeq = viewSeq,
                CommittedToken = token,
                ActionIdentity = actionIdentity ?? string.Empty,
                StartedMonotonicUtc = startedUtc,
            };
            ProgressWatch = null;
            ScriptingDisabledForDuel = true;
            ScriptingDisabledReason = reason ?? "commit_quarantine";
            State = SoloTemporaryCpuState.CommitQuarantine;
        }

        public void DisableScripting(string reason)
        {
            ScriptingDisabledForDuel = true;
            ScriptingDisabledReason = reason ?? "disabled";
        }

        /// <summary>
        /// Shared lease-progress gates for any NativeLease restore (generation, seq, token).
        /// </summary>
        bool TryLeaseProgressGates(
            int currentDuelGeneration,
            ulong currentViewSeq,
            CampaignCpuProgressToken currentToken,
            out string denyReason)
        {
            denyReason = null;
            if (State != SoloTemporaryCpuState.NativeLease || ActiveLease == null)
            {
                denyReason = "not_in_lease";
                return false;
            }
            if (ActiveLease.DuelGeneration != currentDuelGeneration)
            {
                denyReason = "generation_mismatch";
                return false;
            }
            if (currentViewSeq <= ActiveLease.EntryViewSeq)
            {
                denyReason = "view_seq_not_advanced";
                return false;
            }
            if (!CampaignCpuProgressToken.IsFreshSemanticProgress(
                currentToken, ActiveLease.EntryProgressToken))
            {
                denyReason = "semantic_token_unchanged";
                return false;
            }
            return true;
        }

        /// <summary>
        /// A5: restore Human on owned-turn PhaseChange entering Main1/Main2.
        /// Does not require acting-seat resolution (PhaseChange has no acting seat live).
        /// Never treats response windows as capture boundaries.
        /// </summary>
        public bool CanRestoreOwnedMainCaptureBoundary(
            int currentDuelGeneration,
            ulong currentViewSeq,
            CampaignCpuProgressToken currentToken,
            DuelViewType viewType,
            int param1,
            int param2,
            int turnPlayer,
            int ownedSeat,
            out string denyReason)
        {
            if (!TryLeaseProgressGates(
                currentDuelGeneration, currentViewSeq, currentToken, out denyReason))
            {
                return false;
            }
            if (IsNativeContinuationView(viewType, param1))
            {
                denyReason = "native_continuation_view";
                return false;
            }
            if (!CampaignCpuWindowClassifier.IsOwnedMainCapturePhaseChange(
                viewType, param1, param2, turnPlayer, ownedSeat))
            {
                denyReason = "not_owned_main_capture_boundary";
                return false;
            }
            denyReason = null;
            return true;
        }

        /// <summary>
        /// Restoration requires generation match, semantic progress past entry,
        /// resolved acting seat at boundary, and optional CpuThinking evidence.
        /// A5 PhaseChange capture uses <see cref="CanRestoreOwnedMainCaptureBoundary"/> instead.
        /// </summary>
        public bool CanRestoreFromNativeLease(
            int currentDuelGeneration,
            ulong currentViewSeq,
            CampaignCpuProgressToken currentToken,
            int? resolvedActingSeat,
            int ownedSeat,
            int myId,
            bool requireCpuThinking,
            DuelViewType viewType,
            int param1,
            out string denyReason)
        {
            denyReason = null;
            if (!TryLeaseProgressGates(
                currentDuelGeneration, currentViewSeq, currentToken, out denyReason))
            {
                return false;
            }
            if (!resolvedActingSeat.HasValue)
            {
                denyReason = "acting_seat_unresolved";
                return false;
            }
            int acting = resolvedActingSeat.Value;
            bool ownedMain = acting == ownedSeat
                && CampaignCpuWindowClassifier.IsMainPhaseWaitInput(viewType, param1);
            bool humanBoundary = acting == myId;
            if (!ownedMain && !humanBoundary)
            {
                denyReason = "boundary_seat_not_restore";
                return false;
            }
            if (IsNativeContinuationView(viewType, param1))
            {
                denyReason = "native_continuation_view";
                return false;
            }
            if (requireCpuThinking && !ActiveLease.ObservedCpuThinking)
            {
                denyReason = "cpu_thinking_required";
                return false;
            }
            return true;
        }

        public void RestoreHumanOwned()
        {
            ActiveLease = null;
            State = SoloTemporaryCpuState.HumanOwned;
        }

        public bool IsProgressTimedOut(DateTime nowUtc, int timeoutMs)
        {
            if (State != SoloTemporaryCpuState.AwaitingProgress || ProgressWatch == null)
            {
                return false;
            }
            if (timeoutMs <= 0)
            {
                timeoutMs = CampaignCpuDefaults.DefaultProgressTimeoutMs;
            }
            return (nowUtc - ProgressWatch.StartedMonotonicUtc).TotalMilliseconds >= timeoutMs;
        }

        public static bool IsNativeContinuationView(DuelViewType viewType, int param1)
        {
            if (viewType == DuelViewType.RunDialog || viewType == DuelViewType.RunList)
            {
                return true;
            }
            // CpuThinking is not a formal DuelViewType in all builds; treat Selection/Location as native.
            if (viewType == DuelViewType.WaitInput)
            {
                DuelMenuActType menu = (DuelMenuActType)param1;
                return menu == DuelMenuActType.Location
                    || menu == DuelMenuActType.Selection
                    || menu == DuelMenuActType.SummonChance
                    || menu == DuelMenuActType.LockOn
                    || menu == DuelMenuActType.CheckTiming
                    || menu == DuelMenuActType.CheckChain
                    || menu == DuelMenuActType.BattlePhase;
            }
            return false;
        }
    }
}

using System;
using System.Collections.Generic;

namespace YgoMaster
{
    enum CampaignCpuContinuationEligibility
    {
        FullNativeFallback,
        ScriptedResponseContinuation,
        UnsafeContinuation,
        HumanBoundaryHold,
    }

    enum CampaignCpuRecaptureDecision
    {
        HoldNative,
        CommitStableOwnedMain,
        AbandonRecaptureForTurn,
        ClearAtPhaseOrTurnBoundary,
    }

    sealed class CampaignCpuActionChain
    {
        public int DuelGeneration;
        public ulong OriginViewSeq;
        public int OriginTurn;
        public int OriginTurnPlayer;
        public int OriginPhase;
        public string AppliedActionIdentity;
        public string RuleId;
        public string OriginLegalActionFingerprint;
        public CampaignCpuContinuationEligibility Eligibility;
        public CampaignCpuProgressToken LastProgressToken;
        public bool NativeResponseSeen;
        public int CpuThinkingCount;
        public int RecaptureAttempts;
        public int RecaptureAttemptsThisTurn;
        public ulong LastRecaptureViewSeq;
        public DateTime StartedUtc;
        public DateTime LastSemanticProgressUtc;
        public bool Abandoned;
        public string AbandonReason;
    }

    static class CampaignCpuActionChainFactory
    {
        public static CampaignCpuActionChain Create(
            int duelGeneration,
            ulong originViewSeq,
            int originTurn,
            int originTurnPlayer,
            int originPhase,
            CampaignCpuLegalAction action,
            string ruleId,
            bool levelKnown,
            int level,
            CampaignCpuProgressToken progressToken,
            DateTime startedUtc)
        {
            return new CampaignCpuActionChain
            {
                DuelGeneration = duelGeneration,
                OriginViewSeq = originViewSeq,
                OriginTurn = originTurn,
                OriginTurnPlayer = originTurnPlayer,
                OriginPhase = originPhase,
                AppliedActionIdentity = action != null
                    ? action.CanonicalIdentity
                    : string.Empty,
                RuleId = ruleId ?? string.Empty,
                OriginLegalActionFingerprint =
                    progressToken != null
                        && progressToken.LegalFingerprintKnown
                        ? progressToken.LegalActionFingerprint
                        : null,
                Eligibility = Classify(action, levelKnown, level),
                LastProgressToken = progressToken,
                StartedUtc = startedUtc,
                LastSemanticProgressUtc = startedUtc,
            };
        }

        public static CampaignCpuContinuationEligibility Classify(
            CampaignCpuLegalAction action,
            bool levelKnown,
            int level)
        {
            if (action == null || action.Kind != LegalActionKind.Command)
            {
                return CampaignCpuContinuationEligibility.FullNativeFallback;
            }

            if (action.Command == DuelCommandType.Summon
                || action.Command == DuelCommandType.SetMonst)
            {
                if (levelKnown && level >= 1 && level <= 4)
                {
                    return CampaignCpuContinuationEligibility.ScriptedResponseContinuation;
                }
                return CampaignCpuContinuationEligibility.UnsafeContinuation;
            }

            // Spell/Trap Set is a single-step command with multiple successful live
            // Track-B commits and no target/material/list continuation.
            if (action.Command == DuelCommandType.Set)
            {
                return CampaignCpuContinuationEligibility.ScriptedResponseContinuation;
            }

            // Effects, Special Summons, position changes, and every other command can
            // open target/material/list windows. They remain native until explicitly proven.
            return CampaignCpuContinuationEligibility.UnsafeContinuation;
        }

        /// <summary>
        /// M3 fail-native gate: do not issue a scripted Main-menu command unless its
        /// complete continuation lineage is a proven single-step slice: low-level Normal
        /// Summon/monster Set, or Spell/Trap Set.
        /// Flipping OwnedSeat to CPU after a RunDialog is emitted is too late because
        /// dual-Human engine state may already have stamped that dialog to MyId.
        /// </summary>
        public static bool ShouldDeferScriptedCommit(
            CampaignCpuLegalAction action,
            bool levelKnown,
            int level)
        {
            return Classify(action, levelKnown, level)
                != CampaignCpuContinuationEligibility.ScriptedResponseContinuation;
        }
    }

    sealed class CampaignCpuSafeContinuationSelection
    {
        public CampaignCpuDecision Decision;
        public bool SelectedCardLevelKnown;
        public int SelectedCardLevel;
        public bool SelectedTerminalPhaseExit;
        public List<string> ExcludedActionIdentities;
    }

    /// <summary>
    /// Filters a Main menu to complete, already-proven scripted continuations before
    /// applying the normal deterministic scorer. This prevents a higher-scored unsafe
    /// effect from hiding a lower-scored safe Summon/monster Set/Spell-Trap Set.
    /// </summary>
    static class CampaignCpuSafeContinuationSelector
    {
        public static CampaignCpuSafeContinuationSelection Decide(
            CampaignCpuObservation observation,
            CampaignCpuRulePack pack,
            bool predicateEvalFailed,
            int appliedDecisionCount,
            Func<CampaignCpuLegalAction, int> resolveCardLevel)
        {
            var result = new CampaignCpuSafeContinuationSelection
            {
                ExcludedActionIdentities = new List<string>(),
            };
            if (observation == null)
            {
                result.Decision = CampaignCpuDecision.Native("null_observation");
                return result;
            }

            CampaignCpuDecision tacticalDecision = CampaignCpuScorer.Decide(
                observation,
                pack,
                predicateEvalFailed,
                appliedDecisionCount);
            bool hasDominatedPositionRemoval =
                HasDominatedPositionRemoval(tacticalDecision);
            if (IsTacticalTerminalPhaseExit(tacticalDecision))
            {
                result.Decision = tacticalDecision;
                result.SelectedTerminalPhaseExit = true;
                if (observation.LegalActions != null)
                {
                    for (int i = 0; i < observation.LegalActions.Count; i++)
                    {
                        CampaignCpuLegalAction action =
                            observation.LegalActions[i];
                        if (action != null
                            && !string.Equals(
                                action.CanonicalIdentity,
                                tacticalDecision.Action.CanonicalIdentity,
                                StringComparison.Ordinal))
                        {
                            result.ExcludedActionIdentities.Add(
                                action.CanonicalIdentity);
                        }
                    }
                }
                return result;
            }

            CampaignCpuObservation filtered = CloneWithoutLegalActions(observation);
            var levels = new Dictionary<string, int>(StringComparer.Ordinal);
            if (observation.LegalActions != null)
            {
                for (int i = 0; i < observation.LegalActions.Count; i++)
                {
                    CampaignCpuLegalAction action = observation.LegalActions[i];
                    if (action == null)
                    {
                        continue;
                    }
                    int level = 0;
                    bool levelKnown = false;
                    if (CampaignCpuSummonSafety.IsNormalSummonOrSet(action)
                        && resolveCardLevel != null)
                    {
                        try
                        {
                            level = resolveCardLevel(action);
                            levelKnown = level > 0;
                        }
                        catch
                        {
                            level = 0;
                            levelKnown = false;
                        }
                    }
                    bool safeContinuation =
                        CampaignCpuActionChainFactory.Classify(
                        action,
                        levelKnown,
                        level)
                        == CampaignCpuContinuationEligibility
                            .ScriptedResponseContinuation;
                    bool tacticalPhaseCandidate =
                        hasDominatedPositionRemoval
                        && IsTerminalPhaseAction(action);
                    if (safeContinuation || tacticalPhaseCandidate)
                    {
                        filtered.LegalActions.Add(action);
                        levels[action.CanonicalIdentity] =
                            levelKnown ? level : 0;
                    }
                    else
                    {
                        result.ExcludedActionIdentities.Add(
                            action.CanonicalIdentity);
                    }
                }
            }

            if (filtered.LegalActions.Count == 0)
            {
                result.Decision = CampaignCpuDecision.Native(
                    "no_safe_scripted_continuation");
                return result;
            }

            result.Decision = CampaignCpuScorer.Decide(
                filtered,
                pack,
                predicateEvalFailed,
                appliedDecisionCount);
            if (hasDominatedPositionRemoval
                && result.Decision != null
                && IsTerminalPhaseAction(result.Decision.Action))
            {
                result.Decision.TacticalFilterReason =
                    tacticalDecision.TacticalFilterReason;
                result.Decision.TacticalFilteredActionIdentities =
                    new List<string>(
                        tacticalDecision.TacticalFilteredActionIdentities);
                result.Decision.LegalAfterTacticalFingerprint =
                    tacticalDecision.LegalAfterTacticalFingerprint;
                result.SelectedTerminalPhaseExit =
                    IsTacticalTerminalPhaseExit(result.Decision);
            }
            CampaignCpuLegalAction selected = result.Decision != null
                ? result.Decision.Action
                : null;
            int selectedLevel;
            if (selected != null
                && levels.TryGetValue(
                    selected.CanonicalIdentity,
                    out selectedLevel)
                && selectedLevel > 0)
            {
                result.SelectedCardLevelKnown = true;
                result.SelectedCardLevel = selectedLevel;
            }
            return result;
        }

        static bool HasDominatedPositionRemoval(
            CampaignCpuDecision decision)
        {
            return decision != null
                && decision.TacticalFilteredActionIdentities != null
                && decision.TacticalFilteredActionIdentities.Count > 0
                && (string.Equals(
                        decision.TacticalFilterReason,
                        "dominated_turn_defense",
                        StringComparison.Ordinal)
                    || string.Equals(
                        decision.TacticalFilterReason,
                        "dominated_turn_attack",
                        StringComparison.Ordinal));
        }

        static bool IsTerminalPhaseAction(
            CampaignCpuLegalAction action)
        {
            return action != null
                && action.Kind == LegalActionKind.MovePhase
                && (action.Phase == DuelPhase.Battle
                    || action.Phase == DuelPhase.End);
        }

        static bool IsTacticalTerminalPhaseExit(
            CampaignCpuDecision decision)
        {
            return decision != null
                && decision.Route == CampaignCpuRoute.RuleCommit
                && IsTerminalPhaseAction(decision.Action)
                && (string.Equals(
                        decision.RuleId,
                        "phase_exit_battle",
                        StringComparison.Ordinal)
                    || string.Equals(
                        decision.RuleId,
                        "phase_exit_end",
                        StringComparison.Ordinal))
                && decision.TacticalFilteredActionIdentities != null
                && decision.TacticalFilteredActionIdentities.Count > 0;
        }

        static CampaignCpuObservation CloneWithoutLegalActions(
            CampaignCpuObservation source)
        {
            return new CampaignCpuObservation
            {
                ChapterId = source.ChapterId,
                CampaignCpuViewSeq = source.CampaignCpuViewSeq,
                ViewType = source.ViewType,
                ViewParam1 = source.ViewParam1,
                ViewParam2 = source.ViewParam2,
                ViewParam3 = source.ViewParam3,
                ActingPlayer = source.ActingPlayer,
                OwnedSeat = source.OwnedSeat,
                MyId = source.MyId,
                DuelGeneration = source.DuelGeneration,
                Turn = source.Turn,
                TurnPlayer = source.TurnPlayer,
                Phase = source.Phase,
                SelfLp = source.SelfLp,
                OppLp = source.OppLp,
                SelfMonsterCount = source.SelfMonsterCount,
                OppMonsterCount = source.OppMonsterCount,
                SelfHandCardIds = source.SelfHandCardIds,
                SelfFieldFaceUpCardIds = source.SelfFieldFaceUpCardIds,
                OppFieldFaceUpCardIds = source.OppFieldFaceUpCardIds,
                IsMainPhaseWaitInput = source.IsMainPhaseWaitInput,
                IsMultiSelectList = source.IsMultiSelectList,
                WindowClass = source.WindowClass,
                PredicateQueryFailed = source.PredicateQueryFailed,
                TacticalMonsters = source.TacticalMonsters,
            };
        }
    }

    sealed class CampaignCpuRecapturePolicyInput
    {
        public bool Enabled;
        public CampaignCpuActionChain Chain;
        public int DuelGeneration;
        public ulong CurrentViewSeq;
        public CampaignCpuProgressToken CurrentProgressToken;
        public DuelViewType ViewType;
        public int Param1;
        public int Turn;
        public int TurnPlayer;
        public int Phase;
        public int OwnedSeat;
        public int MyId;
        public bool IsStableMainMenuBoundary;
        public int OwnedIsHuman;
        public int MyIsHuman;
        public int DoCommandUser;
        public int RunDialogUser;
        public bool ResponseWindowInFlight;
        public bool ScriptingDisabled;
        public DateTime NowUtc;
        public int MaxAttemptsPerChain;
        public int MaxAttemptsPerTurn;
        public int TimeoutMs;
    }

    sealed class CampaignCpuStableMainBoundarySample
    {
        public int DuelGeneration;
        public int Turn;
        public int Phase;
        public int TurnPlayer;
        public int OwnedSeat;
        public int OwnedIsHuman;
        public int MyIsHuman;
        public int DoCommandUser;
        public int RunDialogUser;
        public string LegalActionFingerprint;
    }

    /// <summary>
    /// Requires the same nonempty owned-Main legal menu on two consecutive pre-SysAct
    /// samples. Any dialog, ownership ambiguity, boundary change, or menu change resets
    /// the proof. This is deliberately independent of RunEffect/CpuThinking callbacks.
    /// </summary>
    sealed class CampaignCpuStableMainBoundaryTracker
    {
        int LastDuelGeneration;
        int LastTurn;
        int LastPhase;
        int LastOwnedSeat;
        string LastLegalActionFingerprint;
        int MatchingSamples;

        public void Reset()
        {
            LastDuelGeneration = 0;
            LastTurn = 0;
            LastPhase = 0;
            LastOwnedSeat = -1;
            LastLegalActionFingerprint = null;
            MatchingSamples = 0;
        }

        public bool Observe(CampaignCpuStableMainBoundarySample sample)
        {
            if (sample == null
                || sample.OwnedSeat < 0
                || sample.TurnPlayer != sample.OwnedSeat
                || sample.OwnedIsHuman != 0
                || sample.MyIsHuman != 1
                || !CampaignCpuWindowClassifier.IsMainPhaseId(sample.Phase)
                || string.IsNullOrEmpty(sample.LegalActionFingerprint)
                || string.Equals(
                    sample.LegalActionFingerprint,
                    "empty",
                    StringComparison.Ordinal))
            {
                Reset();
                return false;
            }

            bool matches =
                MatchingSamples > 0
                && LastDuelGeneration == sample.DuelGeneration
                && LastTurn == sample.Turn
                && LastPhase == sample.Phase
                && LastOwnedSeat == sample.OwnedSeat
                && string.Equals(
                    LastLegalActionFingerprint,
                    sample.LegalActionFingerprint,
                    StringComparison.Ordinal);

            LastDuelGeneration = sample.DuelGeneration;
            LastTurn = sample.Turn;
            LastPhase = sample.Phase;
            LastOwnedSeat = sample.OwnedSeat;
            LastLegalActionFingerprint = sample.LegalActionFingerprint;
            MatchingSamples = matches ? MatchingSamples + 1 : 1;
            return MatchingSamples >= 2;
        }
    }

    sealed class CampaignCpuRecapturePolicyResult
    {
        public CampaignCpuRecaptureDecision Decision;
        public string Reason;

        public static CampaignCpuRecapturePolicyResult Of(
            CampaignCpuRecaptureDecision decision,
            string reason)
        {
            return new CampaignCpuRecapturePolicyResult
            {
                Decision = decision,
                Reason = reason ?? string.Empty,
            };
        }
    }

    sealed class CampaignCpuStableMainDirectCommitResult
    {
        public CampaignCpuCommitOutcome CommitOutcome;
        public bool Applied;
    }

    /// <summary>
    /// Commits directly at a stable pre-SysAct owned-Main menu while NativeLease
    /// remains authoritative. The stable menu is already the decision transport;
    /// no player-type flip or second RunEffect/WaitInput delivery is required.
    /// </summary>
    static class CampaignCpuStableMainDirectCommit
    {
        public static CampaignCpuStableMainDirectCommitResult Execute(
            SoloTemporaryCpuStateMachine stateMachine,
            ulong boundaryViewSeq,
            Func<CampaignCpuCommitOutcome> commit)
        {
            if (stateMachine == null)
            {
                throw new ArgumentNullException("stateMachine");
            }
            if (commit == null)
            {
                throw new ArgumentNullException("commit");
            }
            if (stateMachine.State != SoloTemporaryCpuState.NativeLease
                || stateMachine.ActiveActionChain == null)
            {
                throw new InvalidOperationException(
                    "stable Main direct commit requires NativeLease with active action chain");
            }

            var result = new CampaignCpuStableMainDirectCommitResult
            {
                CommitOutcome = commit(),
            };
            stateMachine.RecordStableMainDirectCommitAttempt(boundaryViewSeq);
            result.Applied =
                result.CommitOutcome == CampaignCpuCommitOutcome.Applied;
            return result;
        }
    }

    /// <summary>
    /// Pure, fail-native successive-Main recapture policy. It never performs a player-type
    /// transition and accepts only the stable pre-SysAct owned-Main boundary.
    /// </summary>
    static class CampaignCpuRecapturePolicy
    {
        public const int DefaultMaxAttemptsPerChain = 2;
        public const int DefaultMaxAttemptsPerTurn = 3;
        public const int DefaultTimeoutMs = 5000;

        public static CampaignCpuRecapturePolicyResult Evaluate(
            CampaignCpuRecapturePolicyInput input)
        {
            if (input == null || input.Chain == null)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "no_action_chain");
            }

            CampaignCpuActionChain chain = input.Chain;
            if (chain.DuelGeneration != input.DuelGeneration)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.ClearAtPhaseOrTurnBoundary,
                    "generation_mismatch");
            }
            if (input.Turn != chain.OriginTurn
                || input.TurnPlayer != chain.OriginTurnPlayer
                || input.TurnPlayer != input.OwnedSeat)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.ClearAtPhaseOrTurnBoundary,
                    "turn_boundary");
            }
            if (!CampaignCpuWindowClassifier.IsMainPhaseId(input.Phase))
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.ClearAtPhaseOrTurnBoundary,
                    "phase_boundary");
            }
            if (chain.Abandoned)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "already_abandoned");
            }
            if (chain.Eligibility
                != CampaignCpuContinuationEligibility.ScriptedResponseContinuation)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.AbandonRecaptureForTurn,
                    "unsafe_lineage");
            }
            if (!input.Enabled)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "policy_disabled");
            }
            if (input.ScriptingDisabled)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.AbandonRecaptureForTurn,
                    "scripting_disabled");
            }
            if (input.MyId < 0
                || input.OwnedSeat < 0
                || input.MyId == input.OwnedSeat)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.AbandonRecaptureForTurn,
                    "invalid_seat_context");
            }

            int maxChain = input.MaxAttemptsPerChain > 0
                ? input.MaxAttemptsPerChain
                : DefaultMaxAttemptsPerChain;
            int maxTurn = input.MaxAttemptsPerTurn > 0
                ? input.MaxAttemptsPerTurn
                : DefaultMaxAttemptsPerTurn;
            if (chain.RecaptureAttempts >= maxChain)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.AbandonRecaptureForTurn,
                    "chain_attempt_cap");
            }
            if (chain.RecaptureAttemptsThisTurn >= maxTurn)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.AbandonRecaptureForTurn,
                    "turn_attempt_cap");
            }

            int timeout = input.TimeoutMs > 0 ? input.TimeoutMs : DefaultTimeoutMs;
            DateTime timeoutAnchor =
                chain.LastSemanticProgressUtc != default(DateTime)
                    ? chain.LastSemanticProgressUtc
                    : chain.StartedUtc;
            if (input.NowUtc != default(DateTime)
                && timeoutAnchor != default(DateTime)
                && (input.NowUtc - timeoutAnchor).TotalMilliseconds >= timeout)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.AbandonRecaptureForTurn,
                    "recapture_timeout");
            }
            if (!chain.NativeResponseSeen)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "native_response_required");
            }
            if (!input.IsStableMainMenuBoundary)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "stable_main_menu_required");
            }
            if (input.ResponseWindowInFlight)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "response_in_flight");
            }
            if (input.OwnedIsHuman != 0)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "owned_seat_not_cpu");
            }
            if (input.MyIsHuman != 1)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "my_id_not_human");
            }
            if (input.CurrentProgressToken == null
                || !input.CurrentProgressToken.LegalFingerprintKnown)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "legal_menu_unknown");
            }
            if (!string.IsNullOrEmpty(chain.OriginLegalActionFingerprint)
                && string.Equals(
                    input.CurrentProgressToken.LegalActionFingerprint,
                    chain.OriginLegalActionFingerprint,
                    StringComparison.Ordinal))
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "legal_menu_not_changed_from_origin");
            }
            if (input.CurrentViewSeq <= chain.OriginViewSeq)
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "view_seq_not_advanced");
            }
            if (!CampaignCpuProgressToken.IsFreshSemanticProgress(
                input.CurrentProgressToken,
                chain.LastProgressToken))
            {
                return CampaignCpuRecapturePolicyResult.Of(
                    CampaignCpuRecaptureDecision.HoldNative,
                    "semantic_token_unchanged");
            }

            return CampaignCpuRecapturePolicyResult.Of(
                CampaignCpuRecaptureDecision.CommitStableOwnedMain,
                "stable_owned_main_menu");
        }
    }
}

using System;
using System.Collections.Generic;
using YgoMaster.Net;
using YgoMaster.Net.Message;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-003 Milestone 3B + YGOMASTER-LLM-005 Slice 2G:
    /// server-authoritative strategic prompt lease and continuation integrity.
    /// </summary>
    static class Llm003Milestone3BTests
    {
        public static void RunAll()
        {
            LeaseStateMachineAcquiresAndReleasesOnce();
            LeaseRejectsWrongGenerationSeqFamilyAndSeat();
            MechanicalAndInfoFamiliesNeverAcquire();
            LeaseHoldsSysActAndBlocksOvertakes();
            MatchingNativeInputReleasesExactlyOnce();
            ExpiryAndForcedReleaseRecoverDeterministically();
            ProviderErrorDisconnectAndDuelEndReleaseLease();
            Goblindbergh202HeldThrough203And204ReplayRegression();
            TelemetryEventsUseExactKindNames();
            ForcedDialogAckIsLeaseAware();
            ContinuationDivergenceLoggedOnTimeoutOrFailure();
            ClampServerLeaseDeadline();
            StaleBrokerResponseNeverRemappedUnderLease();
            Rank4ContinuationVisibilityOrExplicitDivergence();
            // Corrective pass (2026-07-21): P2 seat-1 wire actor, generation authority,
            // server release recovery, durable telemetry emission.
            AuthorityAcceptsP2Seat1AndDeniesSpoofedActor();
            AuthorityDeniesStaleGenerationBeforeProvider();
            AuthorityClientReleaseRecoversProviderErrorAndParseFailure();
            AuthorityRejectsStaleReleaseAfterGenerationChange();
            AuthorityEmitsDurableLeaseTelemetryOnServerPaths();
        }

        static void LeaseStateMachineAcquiresAndReleasesOnce()
        {
            LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
            DateTime t0 = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

            AssertEqual(LlmStrategicPromptLeaseState.Idle, lease.State, "initial state");
            AssertFalse(lease.IsActive, "not active initially");

            LlmStrategicPromptLeaseAcquireResult grant = lease.TryAcquire(
                currentDuelGeneration: 3,
                currentRunEffectSeq: 202,
                currentFamily: LlmPromptFamily.RunDialog,
                currentAbsoluteActingSeat: 1,
                request: Request(3, 202, 1, LlmPromptFamily.RunDialog, 55000, t0));

            AssertTrue(grant.Granted, "strategic RunDialog acquires");
            AssertEqual(LlmStrategicPromptLeaseState.Active, lease.State, "active after grant");
            AssertTrue(lease.IsActive, "IsActive after grant");
            AssertEqual((ulong)202, lease.RunEffectSeq, "leased seq");
            AssertEqual(1, lease.AbsoluteActingSeat, "leased seat");
            AssertEqual(LlmPromptFamily.RunDialog, lease.PromptFamily, "leased family");
            AssertEqual(3, lease.DuelGeneration, "leased generation");

            AssertTrue(
                lease.TryReleaseOnCommit(t0.AddSeconds(1), "provider_commit"),
                "first release on commit");
            AssertEqual(LlmStrategicPromptLeaseState.Released, lease.State, "released");
            AssertFalse(lease.IsActive, "not active after release");
            AssertFalse(
                lease.TryReleaseOnCommit(t0.AddSeconds(2), "duplicate"),
                "second release is rejected");
        }

        static void LeaseRejectsWrongGenerationSeqFamilyAndSeat()
        {
            LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
            DateTime t0 = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

            AssertFalse(
                lease.TryAcquire(1, 10, LlmPromptFamily.RunDialog, 1,
                    Request(2, 10, 1, LlmPromptFamily.RunDialog, 1000, t0)).Granted,
                "wrong generation denied");
            AssertFalse(
                lease.TryAcquire(1, 11, LlmPromptFamily.RunDialog, 1,
                    Request(1, 10, 1, LlmPromptFamily.RunDialog, 1000, t0)).Granted,
                "stale seq denied");
            AssertFalse(
                lease.TryAcquire(1, 10, LlmPromptFamily.WaitInput, 1,
                    Request(1, 10, 1, LlmPromptFamily.RunDialog, 1000, t0)).Granted,
                "family mismatch denied");
            AssertFalse(
                lease.TryAcquire(1, 10, LlmPromptFamily.RunDialog, 0,
                    Request(1, 10, 1, LlmPromptFamily.RunDialog, 1000, t0)).Granted,
                "seat mismatch denied");

            AssertTrue(
                lease.TryAcquire(1, 10, LlmPromptFamily.RunDialog, 1,
                    Request(1, 10, 1, LlmPromptFamily.RunDialog, 1000, t0)).Granted,
                "matching acquire granted");
            AssertFalse(
                lease.TryAcquire(1, 10, LlmPromptFamily.RunDialog, 1,
                    Request(1, 10, 1, LlmPromptFamily.RunDialog, 1000, t0)).Granted,
                "duplicate acquire denied while active");
        }

        static void MechanicalAndInfoFamiliesNeverAcquire()
        {
            LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
            DateTime t0 = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

            AssertFalse(LlmStrategicPromptLease.IsLeaseEligibleFamily(LlmPromptFamily.InfoOnly),
                "InfoOnly not eligible");
            AssertFalse(LlmStrategicPromptLease.IsLeaseEligibleFamily(LlmPromptFamily.Unsupported),
                "Unsupported not eligible");
            AssertTrue(LlmStrategicPromptLease.IsLeaseEligibleFamily(LlmPromptFamily.WaitInput),
                "WaitInput eligible");
            AssertTrue(LlmStrategicPromptLease.IsLeaseEligibleFamily(LlmPromptFamily.RunDialog),
                "RunDialog eligible");
            AssertTrue(LlmStrategicPromptLease.IsLeaseEligibleFamily(LlmPromptFamily.RunList),
                "RunList eligible");

            AssertFalse(
                lease.TryAcquire(1, 5, LlmPromptFamily.InfoOnly, 1,
                    Request(1, 5, 1, LlmPromptFamily.InfoOnly, 1000, t0)).Granted,
                "InfoOnly acquire denied");
            AssertFalse(
                lease.TryAcquire(1, 5, LlmPromptFamily.Unsupported, 1,
                    Request(1, 5, 1, LlmPromptFamily.Unsupported, 1000, t0)).Granted,
                "Unsupported acquire denied");
        }

        static void LeaseHoldsSysActAndBlocksOvertakes()
        {
            LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
            DateTime t0 = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
            AssertTrue(lease.TryAcquire(1, 202, LlmPromptFamily.RunDialog, 1,
                Request(1, 202, 1, LlmPromptFamily.RunDialog, 55000, t0)).Granted,
                "grant");

            AssertTrue(lease.ShouldHoldSysAct(1, 202, t0.AddSeconds(1)),
                "holds SysAct at leased seq");
            AssertFalse(lease.ShouldHoldSysAct(2, 202, t0.AddSeconds(1)),
                "different generation does not hold");

            string blockReason;
            AssertTrue(
                lease.TryBlockOvertake(203, 1, "dialog_ack", t0.AddSeconds(2), out blockReason),
                "blocks same-seat later dialog");
            AssertEqual("overtake_seq", blockReason, "overtake reason seq");
            AssertTrue(
                lease.TryBlockOvertake(204, 0, "dialog_ack", t0.AddSeconds(3), out blockReason),
                "blocks remote dialog");
            AssertTrue(
                lease.TryBlockOvertake(202, 0, "dialog_result", t0.AddSeconds(3), out blockReason),
                "blocks wrong seat on leased seq");
            AssertFalse(
                lease.TryBlockOvertake(202, 1, "dialog_result", t0.AddSeconds(3), out blockReason),
                "matching seat+seq is not an overtake");
        }

        static void MatchingNativeInputReleasesExactlyOnce()
        {
            LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
            DateTime t0 = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
            AssertTrue(lease.TryAcquire(1, 50, LlmPromptFamily.WaitInput, 1,
                Request(1, 50, 1, LlmPromptFamily.WaitInput, 10000, t0)).Granted,
                "grant WaitInput");

            string fail;
            AssertFalse(
                lease.TryAcceptMatchingInput(49, 1, t0, out fail),
                "stale seq not matching");
            AssertFalse(
                lease.TryAcceptMatchingInput(50, 0, t0, out fail),
                "wrong seat not matching");
            AssertTrue(
                lease.TryAcceptMatchingInput(50, 1, t0, out fail),
                "matching input accepted");
            AssertTrue(lease.IsActive, "still active until release");
            AssertTrue(lease.TryReleaseOnCommit(t0, "provider_commit"), "release once");
            AssertFalse(lease.TryAcceptMatchingInput(50, 1, t0, out fail),
                "accept after release fails");
        }

        static void ExpiryAndForcedReleaseRecoverDeterministically()
        {
            LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
            DateTime t0 = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
            LlmStrategicPromptLeaseAcquireResult grant = lease.TryAcquire(
                1, 9, LlmPromptFamily.RunDialog, 1,
                Request(1, 9, 1, LlmPromptFamily.RunDialog, 1000, t0));
            AssertTrue(grant.Granted, "grant");
            AssertTrue(grant.ClampedTimeoutMs <= LlmStrategicPromptLease.ServerLeaseCapMs,
                "clamped under cap");

            bool expired;
            AssertFalse(
                lease.TryExpire(t0.AddMilliseconds(grant.ClampedTimeoutMs - 1),
                    "timeout", "temporary_cpu", out expired),
                "not yet expired");
            AssertFalse(expired, "expired flag false before deadline");

            AssertTrue(
                lease.TryExpire(t0.AddMilliseconds(grant.ClampedTimeoutMs + 1),
                    "timeout", "temporary_cpu", out expired),
                "expires after deadline");
            AssertTrue(expired, "expired flag");
            AssertEqual(LlmStrategicPromptLeaseState.Expired, lease.State, "expired state");
            AssertEqual("timeout", lease.LastReleaseReason, "expiry reason");
            AssertEqual("temporary_cpu", lease.LastDisposition, "expiry disposition");
            AssertFalse(lease.ShouldHoldSysAct(1, 9, t0.AddMinutes(1)),
                "no hold after expiry");
            AssertFalse(
                lease.TryExpire(t0.AddMinutes(2), "timeout", "temporary_cpu", out expired),
                "second expiry is no-op");
        }

        static void ProviderErrorDisconnectAndDuelEndReleaseLease()
        {
            DateTime t0 = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
            string[] reasons = new string[]
            {
                "provider_error",
                "parse_failure",
                "disconnect",
                "duel_end",
                "shutdown",
            };
            foreach (string reason in reasons)
            {
                LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
                AssertTrue(lease.TryAcquire(1, 70, LlmPromptFamily.RunList, 0,
                    Request(1, 70, 0, LlmPromptFamily.RunList, 5000, t0)).Granted,
                    "grant for " + reason);
                AssertTrue(
                    lease.TryForceRelease(reason, "deterministic_recovery", t0.AddSeconds(1)),
                    "force release " + reason);
                AssertFalse(lease.IsActive, "inactive after " + reason);
                AssertEqual(reason, lease.LastReleaseReason, "reason " + reason);
            }
        }

        static void Goblindbergh202HeldThrough203And204ReplayRegression()
        {
            // Live regression: seq 202 strategic optional-effect pending; mechanical
            // 203 (P2) and 204 (ROOT) must not advance authoritative state to 205.
            LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
            DateTime t0 = new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);

            AssertTrue(lease.TryAcquire(
                currentDuelGeneration: 1,
                currentRunEffectSeq: 202,
                currentFamily: LlmPromptFamily.RunDialog,
                currentAbsoluteActingSeat: 1,
                request: Request(1, 202, 1, LlmPromptFamily.RunDialog, 55000, t0)).Granted,
                "lease 202 optional effect");

            AssertTrue(lease.ShouldHoldSysAct(1, 202, t0.AddSeconds(5)),
                "SysAct held at 202 while provider decides");

            string reason;
            AssertTrue(lease.TryBlockOvertake(203, 1, "forced_dialog_acknowledgement",
                t0.AddSeconds(6), out reason),
                "P2 seq 203 mechanical ack blocked");
            AssertTrue(lease.TryBlockOvertake(204, 0, "forced_dialog_acknowledgement",
                t0.AddSeconds(7), out reason),
                "ROOT seq 204 mechanical ack blocked");

            // Simulated delayed provider Activate (DlgSetResult(1)) on exact seq 202.
            string fail;
            AssertTrue(lease.TryAcceptMatchingInput(202, 1, t0.AddSeconds(17), out fail),
                "activate input accepted at leased seq");
            AssertTrue(lease.TryReleaseOnCommit(t0.AddSeconds(17), "provider_commit"),
                "lease released after single commit");
            AssertFalse(lease.IsActive, "lease inactive");
            AssertFalse(lease.ShouldHoldSysAct(1, 202, t0.AddSeconds(18)),
                "SysAct resumes after release");

            // Late mechanical attempts after release do not re-open the old lease.
            AssertFalse(lease.TryBlockOvertake(203, 1, "dialog_ack", t0.AddSeconds(19), out reason),
                "no overtake block without active lease");
        }

        static void TelemetryEventsUseExactKindNames()
        {
            DateTime t0 = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
            LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
            lease.TryAcquire(1, 202, LlmPromptFamily.RunDialog, 1,
                Request(1, 202, 1, LlmPromptFamily.RunDialog, 1000, t0));

            string started = LlmDecisionLogSerializer.SerializeStrategicPromptLeaseStarted(
                lease, t0);
            string held = LlmDecisionLogSerializer.SerializeStrategicPromptLeaseHeld(
                lease, 203, t0.AddSeconds(1));
            string overtake = LlmDecisionLogSerializer.SerializeStrategicPromptLeaseOvertakeBlocked(
                lease, 203, 1, "dialog_ack", "overtake_seq", t0.AddSeconds(1));
            AssertTrue(lease.TryReleaseOnCommit(t0.AddSeconds(2), "provider_commit"),
                "release for telemetry");
            string released = LlmDecisionLogSerializer.SerializeStrategicPromptLeaseReleased(
                lease, "provider_commit", "committed", t0.AddSeconds(2));
            AssertTrue(lease.TryAcquire(1, 300, LlmPromptFamily.WaitInput, 1,
                Request(1, 300, 1, LlmPromptFamily.WaitInput, 100, t0)).Granted,
                "second lease for expiry telemetry");
            AssertTrue(lease.TryExpire(t0.AddSeconds(5), "timeout", "temporary_cpu", out _),
                "expire for telemetry");
            string expired = LlmDecisionLogSerializer.SerializeStrategicPromptLeaseExpired(
                lease, "timeout", "temporary_cpu", t0.AddSeconds(5));

            AssertContains(started, "\"kind\":\"llm_strategic_prompt_lease_started\"", "started kind");
            AssertContains(held, "\"kind\":\"llm_strategic_prompt_lease_held\"", "held kind");
            AssertContains(overtake, "\"kind\":\"llm_strategic_prompt_lease_overtake_blocked\"",
                "overtake kind");
            AssertContains(released, "\"kind\":\"llm_strategic_prompt_lease_released\"", "released kind");
            AssertContains(expired, "\"kind\":\"llm_strategic_prompt_lease_expired\"", "expired kind");
            AssertContains(started, "\"origin_run_effect_seq\":202", "origin seq");
            AssertContains(started, "\"absolute_acting_seat\":1", "seat");
            AssertContains(started, "\"prompt_family\":\"RunDialog\"", "family");
            AssertContains(overtake, "\"current_run_effect_seq\":203", "current seq on overtake");
            AssertContains(released, "\"reason\":\"provider_commit\"", "release reason");
            AssertContains(expired, "\"disposition\":\"temporary_cpu\"", "expiry disposition");
        }

        static void ForcedDialogAckIsLeaseAware()
        {
            LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
            DateTime t0 = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
            AssertTrue(lease.TryAcquire(1, 202, LlmPromptFamily.RunDialog, 1,
                Request(1, 202, 1, LlmPromptFamily.RunDialog, 5000, t0)).Granted,
                "lease active");

            AssertTrue(
                lease.BlocksMechanicalAutomaticCommit(203),
                "mechanical 203 blocked while lease holds 202");
            AssertTrue(
                lease.BlocksMechanicalAutomaticCommit(204),
                "mechanical 204 blocked while lease holds 202");
            AssertFalse(
                lease.BlocksMechanicalAutomaticCommit(202),
                "same-seq mechanical not the lease-overtake case (strategic owns 202)");

            lease.TryForceRelease("provider_error", "deterministic_recovery", t0.AddSeconds(1));
            AssertFalse(lease.BlocksMechanicalAutomaticCommit(203),
                "after release mechanical not blocked by lease");
        }

        static void ContinuationDivergenceLoggedOnTimeoutOrFailure()
        {
            string json = LlmDecisionLogSerializer.SerializeStrategicContinuationDiverged(
                originRunEffectSeq: 202,
                currentRunEffectSeq: 202,
                absoluteActingSeat: 1,
                promptFamily: LlmPromptFamily.RunDialog,
                reason: "timeout",
                disposition: "temporary_cpu",
                detail: "optional_effect_not_committed");
            AssertContains(json, "\"kind\":\"llm_strategic_continuation_diverged\"", "divergence kind");
            AssertContains(json, "\"origin_run_effect_seq\":202", "origin");
            AssertContains(json, "\"reason\":\"timeout\"", "reason");
            AssertContains(json, "\"disposition\":\"temporary_cpu\"", "disposition");
            AssertContains(json, "optional_effect_not_committed", "detail");
        }

        static void ClampServerLeaseDeadline()
        {
            AssertEqual(
                LlmStrategicPromptLease.DefaultLeaseMs,
                LlmStrategicPromptLease.ClampLeaseTimeoutMs(0),
                "zero uses default");
            AssertEqual(
                LlmStrategicPromptLease.DefaultLeaseMs,
                LlmStrategicPromptLease.ClampLeaseTimeoutMs(-5),
                "negative uses default");
            AssertEqual(
                55000,
                LlmStrategicPromptLease.ClampLeaseTimeoutMs(55000),
                "client timeout under cap");
            AssertEqual(
                LlmStrategicPromptLease.ServerLeaseCapMs,
                LlmStrategicPromptLease.ClampLeaseTimeoutMs(
                    LlmStrategicPromptLease.ServerLeaseCapMs + 60000),
                "over cap clamped");
        }

        static void StaleBrokerResponseNeverRemappedUnderLease()
        {
            // Lease does not weaken stale_run_effect_seq validation.
            DecisionSnapshot current = new DecisionSnapshot()
            {
                RunEffectSeq = 205,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                IsStrategicWindow = true,
            };
            current.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.DialogResult,
                DialogResult = 1,
                ActionLabel = "Activate optional effect",
                IsMechanical = false,
            });
            LegalActionExtractor.ClassifySnapshot(current);

            LlmBrokerDecisionResponse staleResponse = new LlmBrokerDecisionResponse()
            {
                RunEffectSeq = 202,
                ActionId = 0,
                Reason = "Activate Goblindbergh to Special Summon Little-Winguard.",
                Confidence = 0.9,
            };
            LlmBrokerValidationResult validation = LlmBrokerProtocol.ValidateResponse(
                current, staleResponse);
            AssertFalse(validation.IsValid, "stale seq rejected");
            AssertEqual("stale_run_effect_seq", validation.Error, "stale error code preserved");
        }

        static void Rank4ContinuationVisibilityOrExplicitDivergence()
        {
            // After successful optional-effect continuation, Rank-4 SummonSp root must
            // either appear as a legal action or be logged as explicit divergence.
            DecisionSnapshot afterSs = new DecisionSnapshot()
            {
                RunEffectSeq = 220,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                Turn = 4,
                TurnPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
                IsStrategicWindow = true,
            };
            afterSs.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.SummonSp,
                Player = 1,
                Position = 15,
                Index = 0,
                CardId = 84013237,
                ActionLabel = "Xyz Summon Rank 4",
                IsMechanical = false,
                StrategicRole = "extra_deck_summon",
            });
            afterSs.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
                ActionLabel = "Enter Battle Phase",
                IsMechanical = false,
            });
            LegalActionExtractor.ClassifySnapshot(afterSs);

            bool hasRank4Root = false;
            foreach (LegalAction action in afterSs.LegalActions)
            {
                if (action != null &&
                    action.Kind == LegalActionKind.Command &&
                    action.Command == DuelCommandType.SummonSp)
                {
                    hasRank4Root = true;
                    break;
                }
            }
            AssertTrue(hasRank4Root, "Rank-4 SummonSp root visible after successful continuation");

            // Failure path: no Rank-4 root → explicit divergence string required.
            DecisionSnapshot noRank4 = new DecisionSnapshot()
            {
                RunEffectSeq = 221,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                IsStrategicWindow = true,
            };
            noRank4.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
                ActionLabel = "Enter Battle Phase",
            });
            LegalActionExtractor.ClassifySnapshot(noRank4);
            bool stillHasRank4 = false;
            foreach (LegalAction action in noRank4.LegalActions)
            {
                if (action != null &&
                    action.Kind == LegalActionKind.Command &&
                    action.Command == DuelCommandType.SummonSp)
                {
                    stillHasRank4 = true;
                }
            }
            AssertFalse(stillHasRank4, "control without Rank-4");
            string divergence = LlmDecisionLogSerializer.SerializeStrategicContinuationDiverged(
                202, 221, 1, LlmPromptFamily.RunDialog,
                "continuation_miss", "none", "rank4_root_not_legal");
            AssertContains(divergence, "rank4_root_not_legal", "explicit divergence detail");
        }

        static void AuthorityAcceptsP2Seat1AndDeniesSpoofedActor()
        {
            DateTime t0 = new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);
            LlmStrategicPromptLeaseAuthority auth = new LlmStrategicPromptLeaseAuthority();
            AssertEqual(1, auth.BeginDuelGeneration(), "Pvp worker generation starts at 1");

            // Live controlled P2 seat 1: session stamps ActorPlayer=1; AbsoluteActingSeat=1.
            LlmStrategicPromptLeaseAcquireResult grant = auth.TryAcquireFromWire(
                requestDuelGeneration: 1,
                requestSeq: 202,
                requestAbsoluteActingSeat: 1,
                requestFamily: LlmPromptFamily.RunDialog,
                requestedTimeoutMs: 55000,
                actorPlayer: 1,
                engineRunEffectSeq: 202,
                engineFamily: LlmPromptFamily.RunDialog,
                engineAbsoluteActingSeat: 1,
                utcNow: t0);
            AssertTrue(grant.Granted, "P2 seat-1 acquire granted when actor matches seat");
            AssertTrue(auth.IsActive, "lease active for seat 1");
            AssertTrue(auth.ShouldHoldSysAct(202, t0.AddSeconds(1)), "holds SysAct at 202");

            // Spoofed actor (default 0) with AbsoluteActingSeat=1 must be denied.
            LlmStrategicPromptLeaseAuthority spoofAuth = new LlmStrategicPromptLeaseAuthority();
            spoofAuth.BeginDuelGeneration();
            LlmStrategicPromptLeaseAcquireResult spoof = spoofAuth.TryAcquireFromWire(
                1, 202, 1, LlmPromptFamily.RunDialog, 55000,
                actorPlayer: 0,
                engineRunEffectSeq: 202,
                engineFamily: LlmPromptFamily.RunDialog,
                engineAbsoluteActingSeat: 1,
                utcNow: t0);
            AssertFalse(spoof.Granted, "spoofed ActorPlayer=0 denied for seat 1");
            AssertEqual("actor_seat_spoof", spoof.DenialReason, "spoof reason");
            AssertFalse(spoofAuth.IsActive, "no lease after spoof");
        }

        static void AuthorityDeniesStaleGenerationBeforeProvider()
        {
            DateTime t0 = new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);
            LlmStrategicPromptLeaseAuthority auth = new LlmStrategicPromptLeaseAuthority();
            auth.BeginDuelGeneration();
            AssertEqual(1, auth.AuthoritativeDuelGeneration, "auth gen 1");

            LlmStrategicPromptLeaseAcquireResult stale = auth.TryAcquireFromWire(
                requestDuelGeneration: 99,
                requestSeq: 50,
                requestAbsoluteActingSeat: 1,
                requestFamily: LlmPromptFamily.WaitInput,
                requestedTimeoutMs: 1000,
                actorPlayer: 1,
                engineRunEffectSeq: 50,
                engineFamily: LlmPromptFamily.WaitInput,
                engineAbsoluteActingSeat: 1,
                utcNow: t0);
            AssertFalse(stale.Granted, "stale generation denied");
            AssertEqual("duel_generation_mismatch", stale.DenialReason, "stale gen reason");
            AssertFalse(auth.IsActive, "no lease when gen mismatch");

            // Authoritative generation is NOT taken from the client request.
            auth.SetAuthoritativeDuelGenerationForTest(7);
            LlmStrategicPromptLeaseAcquireResult ok = auth.TryAcquireFromWire(
                7, 51, 1, LlmPromptFamily.WaitInput, 1000, 1,
                51, LlmPromptFamily.WaitInput, 1, t0);
            AssertTrue(ok.Granted, "matching authoritative generation grants");
            LlmStrategicPromptLeaseAcquireResult wrong = auth.TryAcquireFromWire(
                7, 52, 1, LlmPromptFamily.WaitInput, 1000, 1,
                52, LlmPromptFamily.WaitInput, 1, t0);
            AssertFalse(wrong.Granted, "second acquire blocked while active");
        }

        static void AuthorityClientReleaseRecoversProviderErrorAndParseFailure()
        {
            DateTime t0 = new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);
            string[] reasons = new string[]
            {
                "provider_error",
                "parse_failure",
                "deterministic_recovery",
                "timeout",
                "cpu_fallback",
            };
            foreach (string reason in reasons)
            {
                LlmStrategicPromptLeaseAuthority auth = new LlmStrategicPromptLeaseAuthority();
                auth.BeginDuelGeneration();
                AssertTrue(auth.TryAcquireFromWire(
                    1, 80, 1, LlmPromptFamily.RunDialog, 60000, 1,
                    80, LlmPromptFamily.RunDialog, 1, t0).Granted,
                    "grant for " + reason);
                AssertTrue(auth.ShouldHoldSysAct(80, t0.AddSeconds(1)),
                    "held before release " + reason);

                string fail;
                string disposition = reason == "timeout" ? "temporary_cpu" : "cpu_fallback";
                AssertTrue(auth.TryReleaseFromWire(
                    1, 80, 1, reason, disposition, t0.AddSeconds(2), out fail),
                    "server release for " + reason);
                AssertFalse(auth.IsActive, "not held after " + reason);
                AssertFalse(auth.ShouldHoldSysAct(80, t0.AddSeconds(3)),
                    "SysAct not held after " + reason);
                AssertTrue(auth.TelemetryContainsKind("llm_strategic_prompt_lease_released"),
                    "released telemetry for " + reason);
            }
        }

        static void AuthorityRejectsStaleReleaseAfterGenerationChange()
        {
            DateTime t0 = new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);
            LlmStrategicPromptLeaseAuthority auth = new LlmStrategicPromptLeaseAuthority();
            auth.BeginDuelGeneration();
            AssertTrue(auth.TryAcquireFromWire(
                1, 90, 1, LlmPromptFamily.RunDialog, 5000, 1,
                90, LlmPromptFamily.RunDialog, 1, t0).Granted, "grant");

            // Generation advances (new duel) — old release must not remap/release.
            auth.SetAuthoritativeDuelGenerationForTest(2);
            // Lease still active with gen 1 internal; release with gen 2 fails.
            string fail;
            AssertFalse(auth.TryReleaseFromWire(
                2, 90, 1, "provider_error", "cpu_fallback", t0.AddSeconds(1), out fail),
                "stale gen release denied");
            AssertEqual("duel_generation_mismatch", fail, "stale release reason");

            // Old seq after seq change style: wrong seq denied.
            auth.SetAuthoritativeDuelGenerationForTest(1);
            AssertFalse(auth.TryReleaseFromWire(
                1, 91, 1, "provider_error", "cpu_fallback", t0.AddSeconds(1), out fail),
                "wrong seq release denied");
            AssertEqual("run_effect_seq_mismatch", fail, "wrong seq reason");
        }

        static void AuthorityEmitsDurableLeaseTelemetryOnServerPaths()
        {
            DateTime t0 = new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);
            LlmStrategicPromptLeaseAuthority auth = new LlmStrategicPromptLeaseAuthority();
            auth.BeginDuelGeneration();
            auth.ClearTelemetry();

            AssertTrue(auth.TryAcquireFromWire(
                1, 202, 1, LlmPromptFamily.RunDialog, 1000, 1,
                202, LlmPromptFamily.RunDialog, 1, t0).Granted, "grant");

            AssertTrue(auth.TelemetryContainsKind("llm_strategic_prompt_lease_started"),
                "server path emits started");
            AssertTrue(auth.TelemetryContainsKind("llm_strategic_prompt_lease_held"),
                "server path emits held");

            string block;
            AssertTrue(auth.TryBlockOvertake(203, 1, "dialog_ack", t0.AddMilliseconds(10), out block),
                "overtake 203");
            AssertTrue(auth.TelemetryContainsKind("llm_strategic_prompt_lease_overtake_blocked"),
                "server path emits overtake_blocked");

            string fail;
            AssertTrue(auth.TryReleaseAfterAcceptedInput(202, 1, t0.AddMilliseconds(20), out fail),
                "matching commit release");
            AssertTrue(auth.TelemetryContainsKind("llm_strategic_prompt_lease_released"),
                "server path emits released on commit");

            // Expire path telemetry.
            LlmStrategicPromptLeaseAuthority exp = new LlmStrategicPromptLeaseAuthority();
            exp.BeginDuelGeneration();
            AssertTrue(exp.TryAcquireFromWire(
                1, 300, 1, LlmPromptFamily.WaitInput, 50, 1,
                300, LlmPromptFamily.WaitInput, 1, t0).Granted, "short lease");
            bool expired;
            AssertTrue(exp.MaybeExpire(t0.AddMilliseconds(100), out expired) && expired,
                "expire");
            AssertTrue(exp.TelemetryContainsKind("llm_strategic_prompt_lease_expired"),
                "server path emits expired");
            AssertTrue(exp.TelemetryContainsKind("llm_strategic_continuation_diverged"),
                "server path emits continuation divergence on expiry");

            // Wire message type for durable fan-out exists and round-trips JSON line.
            DuelStrategicPromptLeaseEventMessage wire =
                new DuelStrategicPromptLeaseEventMessage()
                {
                    JsonLine = exp.TelemetryEvents[exp.TelemetryEvents.Count - 1],
                };
            AssertTrue(!string.IsNullOrEmpty(wire.JsonLine), "event message carries JSONL");
            AssertEqual(
                NetMessageType.DuelStrategicPromptLeaseEvent,
                wire.Type,
                "event message type");

            // Acquire message includes ActorPlayer field for P2 seat-1 wire path.
            DuelComAcquireStrategicPromptLeaseMessage acquire =
                new DuelComAcquireStrategicPromptLeaseMessage()
                {
                    RunEffectSeq = 202,
                    ActorPlayer = 1,
                    AbsoluteActingSeat = 1,
                    DuelGeneration = 1,
                    PromptFamily = (int)LlmPromptFamily.RunDialog,
                    RequestedTimeoutMs = 55000,
                };
            AssertEqual(1, acquire.ActorPlayer, "acquire wire ActorPlayer set for seat 1");
            AssertEqual(1, acquire.AbsoluteActingSeat, "acquire AbsoluteActingSeat seat 1");
            AssertEqual(
                NetMessageType.DuelComAcquireStrategicPromptLease,
                acquire.Type,
                "acquire message type");

            DuelComReleaseStrategicPromptLeaseMessage release =
                new DuelComReleaseStrategicPromptLeaseMessage()
                {
                    RunEffectSeq = 202,
                    ActorPlayer = 1,
                    DuelGeneration = 1,
                    Reason = "provider_error",
                    Disposition = "cpu_fallback",
                };
            AssertEqual(
                NetMessageType.DuelComReleaseStrategicPromptLease,
                release.Type,
                "release message type");
        }

        static LlmStrategicPromptLeaseAcquireRequest Request(
            int generation,
            ulong seq,
            int seat,
            LlmPromptFamily family,
            int timeoutMs,
            DateTime utcNow)
        {
            return new LlmStrategicPromptLeaseAcquireRequest()
            {
                DuelGeneration = generation,
                RunEffectSeq = seq,
                AbsoluteActingSeat = seat,
                PromptFamily = family,
                RequestedTimeoutMs = timeoutMs,
                UtcNow = utcNow,
            };
        }

        static void AssertContains(string value, string expected, string message)
        {
            if (value == null || value.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new Exception(message + ": expected to contain " + expected +
                    ", got " + (value ?? "<null>"));
            }
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value)
            {
                throw new Exception(message + ": expected true");
            }
        }

        static void AssertFalse(bool value, string message)
        {
            if (value)
            {
                throw new Exception(message + ": expected false");
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + ": expected " + expected + ", got " + actual);
            }
        }
    }
}

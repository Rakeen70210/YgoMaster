using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Server-authoritative bounded lease for one strategic decision prompt.
    /// While active, the PvP worker must not advance DLL_DuelSysAct past the leased
    /// sequence, and non-matching automatic/remote inputs are treated as overtakes.
    /// Generic: no card ids or provider-prose parsing.
    /// </summary>
    enum LlmStrategicPromptLeaseState
    {
        Idle,
        Active,
        Released,
        Expired,
    }

    sealed class LlmStrategicPromptLeaseAcquireRequest
    {
        public int DuelGeneration;
        public ulong RunEffectSeq;
        public int AbsoluteActingSeat;
        public LlmPromptFamily PromptFamily;
        public int RequestedTimeoutMs;
        public DateTime UtcNow;
    }

    sealed class LlmStrategicPromptLeaseAcquireResult
    {
        public bool Granted;
        public string DenialReason;
        public DateTime DeadlineUtc;
        public int ClampedTimeoutMs;

        public static LlmStrategicPromptLeaseAcquireResult Deny(string reason)
        {
            return new LlmStrategicPromptLeaseAcquireResult()
            {
                Granted = false,
                DenialReason = reason ?? "denied",
            };
        }

        public static LlmStrategicPromptLeaseAcquireResult Allow(
            int clampedTimeoutMs,
            DateTime deadlineUtc)
        {
            return new LlmStrategicPromptLeaseAcquireResult()
            {
                Granted = true,
                ClampedTimeoutMs = clampedTimeoutMs,
                DeadlineUtc = deadlineUtc,
            };
        }
    }

    sealed class LlmStrategicPromptLease
    {
        /// <summary>Hard server-side cap; client timeout cannot extend past this.</summary>
        public const int ServerLeaseCapMs = 90000;
        /// <summary>Used when the client requests a non-positive timeout.</summary>
        public const int DefaultLeaseMs = 60000;

        LlmStrategicPromptLeaseState state = LlmStrategicPromptLeaseState.Idle;
        int duelGeneration;
        ulong runEffectSeq;
        int absoluteActingSeat = -1;
        LlmPromptFamily promptFamily = LlmPromptFamily.Unsupported;
        DateTime startedUtc;
        DateTime deadlineUtc;
        string lastReleaseReason = string.Empty;
        string lastDisposition = string.Empty;
        bool matchingInputAccepted;

        public LlmStrategicPromptLeaseState State { get { return state; } }
        public bool IsActive { get { return state == LlmStrategicPromptLeaseState.Active; } }
        public int DuelGeneration { get { return duelGeneration; } }
        public ulong RunEffectSeq { get { return runEffectSeq; } }
        public int AbsoluteActingSeat { get { return absoluteActingSeat; } }
        public LlmPromptFamily PromptFamily { get { return promptFamily; } }
        public DateTime StartedUtc { get { return startedUtc; } }
        public DateTime DeadlineUtc { get { return deadlineUtc; } }
        public string LastReleaseReason { get { return lastReleaseReason; } }
        public string LastDisposition { get { return lastDisposition; } }
        public bool MatchingInputAccepted { get { return matchingInputAccepted; } }

        public static int ClampLeaseTimeoutMs(int requestedMs)
        {
            if (requestedMs <= 0)
            {
                return DefaultLeaseMs;
            }
            if (requestedMs > ServerLeaseCapMs)
            {
                return ServerLeaseCapMs;
            }
            return requestedMs;
        }

        public static bool IsLeaseEligibleFamily(LlmPromptFamily family)
        {
            return family == LlmPromptFamily.WaitInput ||
                family == LlmPromptFamily.RunDialog ||
                family == LlmPromptFamily.RunList;
        }

        public LlmStrategicPromptLeaseAcquireResult TryAcquire(
            int currentDuelGeneration,
            ulong currentRunEffectSeq,
            LlmPromptFamily currentFamily,
            int currentAbsoluteActingSeat,
            LlmStrategicPromptLeaseAcquireRequest request)
        {
            if (request == null)
            {
                return LlmStrategicPromptLeaseAcquireResult.Deny("null_request");
            }
            if (state == LlmStrategicPromptLeaseState.Active)
            {
                return LlmStrategicPromptLeaseAcquireResult.Deny("lease_already_active");
            }
            if (!IsLeaseEligibleFamily(request.PromptFamily) ||
                !IsLeaseEligibleFamily(currentFamily))
            {
                return LlmStrategicPromptLeaseAcquireResult.Deny("non_strategic_family");
            }
            if (request.DuelGeneration != currentDuelGeneration)
            {
                return LlmStrategicPromptLeaseAcquireResult.Deny("duel_generation_mismatch");
            }
            if (request.RunEffectSeq != currentRunEffectSeq)
            {
                return LlmStrategicPromptLeaseAcquireResult.Deny("run_effect_seq_mismatch");
            }
            if (request.PromptFamily != currentFamily)
            {
                return LlmStrategicPromptLeaseAcquireResult.Deny("prompt_family_mismatch");
            }
            if (request.AbsoluteActingSeat != currentAbsoluteActingSeat ||
                request.AbsoluteActingSeat < 0 ||
                request.AbsoluteActingSeat > 1 ||
                currentAbsoluteActingSeat < 0 ||
                currentAbsoluteActingSeat > 1)
            {
                return LlmStrategicPromptLeaseAcquireResult.Deny("absolute_seat_mismatch");
            }

            int clamped = ClampLeaseTimeoutMs(request.RequestedTimeoutMs);
            DateTime now = request.UtcNow.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.UtcNow, DateTimeKind.Utc)
                : request.UtcNow.ToUniversalTime();

            state = LlmStrategicPromptLeaseState.Active;
            duelGeneration = request.DuelGeneration;
            runEffectSeq = request.RunEffectSeq;
            absoluteActingSeat = request.AbsoluteActingSeat;
            promptFamily = request.PromptFamily;
            startedUtc = now;
            deadlineUtc = now.AddMilliseconds(clamped);
            lastReleaseReason = string.Empty;
            lastDisposition = string.Empty;
            matchingInputAccepted = false;
            return LlmStrategicPromptLeaseAcquireResult.Allow(clamped, deadlineUtc);
        }

        public bool ShouldHoldSysAct(
            int currentDuelGeneration,
            ulong currentRunEffectSeq,
            DateTime utcNow)
        {
            if (!IsActive)
            {
                return false;
            }
            if (IsPastDeadline(utcNow))
            {
                return false;
            }
            return currentDuelGeneration == duelGeneration &&
                currentRunEffectSeq == runEffectSeq;
        }

        public bool TryBlockOvertake(
            ulong inputSeq,
            int actorSeat,
            string inputKind,
            DateTime utcNow,
            out string blockReason)
        {
            blockReason = null;
            if (!IsActive || IsPastDeadline(utcNow))
            {
                return false;
            }
            if (inputSeq != runEffectSeq)
            {
                blockReason = "overtake_seq";
                return true;
            }
            if (actorSeat != absoluteActingSeat)
            {
                blockReason = "overtake_seat";
                return true;
            }
            // Matching seat+seq is the leased decision path, not an overtake.
            return false;
        }

        public bool TryAcceptMatchingInput(
            ulong inputSeq,
            int actorSeat,
            DateTime utcNow,
            out string failureReason)
        {
            failureReason = null;
            if (!IsActive)
            {
                failureReason = "lease_not_active";
                return false;
            }
            if (IsPastDeadline(utcNow))
            {
                failureReason = "lease_expired";
                return false;
            }
            if (inputSeq != runEffectSeq)
            {
                failureReason = "seq_mismatch";
                return false;
            }
            if (actorSeat != absoluteActingSeat)
            {
                failureReason = "seat_mismatch";
                return false;
            }
            if (matchingInputAccepted)
            {
                failureReason = "already_accepted";
                return false;
            }
            matchingInputAccepted = true;
            return true;
        }

        public bool TryReleaseOnCommit(DateTime utcNow, string disposition)
        {
            if (!IsActive)
            {
                return false;
            }
            state = LlmStrategicPromptLeaseState.Released;
            lastReleaseReason = "provider_commit";
            lastDisposition = string.IsNullOrEmpty(disposition) ? "committed" : disposition;
            return true;
        }

        public bool TryExpire(
            DateTime utcNow,
            string reason,
            string disposition,
            out bool expired)
        {
            expired = false;
            if (!IsActive)
            {
                return false;
            }
            if (!IsPastDeadline(utcNow))
            {
                return false;
            }
            state = LlmStrategicPromptLeaseState.Expired;
            lastReleaseReason = string.IsNullOrEmpty(reason) ? "timeout" : reason;
            lastDisposition = string.IsNullOrEmpty(disposition) ? "temporary_cpu" : disposition;
            expired = true;
            return true;
        }

        public bool TryForceRelease(string reason, string disposition, DateTime utcNow)
        {
            if (!IsActive)
            {
                return false;
            }
            state = LlmStrategicPromptLeaseState.Released;
            lastReleaseReason = string.IsNullOrEmpty(reason) ? "forced" : reason;
            lastDisposition = string.IsNullOrEmpty(disposition) ? "none" : disposition;
            return true;
        }

        public bool BlocksMechanicalAutomaticCommit(ulong mechanicalSeq)
        {
            return IsActive && mechanicalSeq != runEffectSeq;
        }

        public double ElapsedMs(DateTime utcNow)
        {
            if (state == LlmStrategicPromptLeaseState.Idle)
            {
                return 0;
            }
            DateTime now = NormalizeUtc(utcNow);
            return Math.Max(0, (now - startedUtc).TotalMilliseconds);
        }

        public void Reset()
        {
            state = LlmStrategicPromptLeaseState.Idle;
            duelGeneration = 0;
            runEffectSeq = 0;
            absoluteActingSeat = -1;
            promptFamily = LlmPromptFamily.Unsupported;
            startedUtc = default(DateTime);
            deadlineUtc = default(DateTime);
            lastReleaseReason = string.Empty;
            lastDisposition = string.Empty;
            matchingInputAccepted = false;
        }

        bool IsPastDeadline(DateTime utcNow)
        {
            return NormalizeUtc(utcNow) >= deadlineUtc;
        }

        static DateTime NormalizeUtc(DateTime utcNow)
        {
            if (utcNow.Kind == DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            }
            return utcNow.ToUniversalTime();
        }
    }

    /// <summary>
    /// Client-side acquire/grant handshake coordinator. Waits off the network thread.
    /// </summary>
    sealed class LlmStrategicPromptLeaseClientSession
    {
        readonly object locker = new object();
        readonly System.Threading.ManualResetEventSlim grantEvent =
            new System.Threading.ManualResetEventSlim(false);
        ulong pendingSeq;
        bool waiting;
        bool granted;
        string denyReason = string.Empty;
        ulong activeGrantedSeq;
        bool hasActiveGrant;

        public bool HasActiveGrant
        {
            get { lock (locker) { return hasActiveGrant; } }
        }

        public ulong ActiveGrantedSeq
        {
            get { lock (locker) { return activeGrantedSeq; } }
        }

        public void BeginAcquireWait(ulong seq)
        {
            lock (locker)
            {
                pendingSeq = seq;
                waiting = true;
                granted = false;
                denyReason = string.Empty;
                grantEvent.Reset();
            }
        }

        public void OnResult(bool resultGranted, ulong seq, string reason)
        {
            lock (locker)
            {
                // Ignore late results after WaitForGrant timed out or was cleared.
                if (!waiting || seq != pendingSeq)
                {
                    return;
                }
                granted = resultGranted;
                denyReason = reason ?? string.Empty;
                if (resultGranted)
                {
                    hasActiveGrant = true;
                    activeGrantedSeq = seq;
                }
                waiting = false;
                grantEvent.Set();
            }
        }

        public bool WaitForGrant(int timeoutMs, out string reason)
        {
            reason = "grant_timeout";
            if (timeoutMs < 1)
            {
                timeoutMs = 1;
            }
            if (!grantEvent.Wait(timeoutMs))
            {
                lock (locker)
                {
                    waiting = false;
                }
                return false;
            }
            lock (locker)
            {
                reason = granted ? string.Empty : (string.IsNullOrEmpty(denyReason) ? "denied" : denyReason);
                return granted;
            }
        }

        public bool ShouldSuppressMechanicalAutomatic(ulong seq)
        {
            lock (locker)
            {
                return hasActiveGrant && activeGrantedSeq != seq;
            }
        }

        public void ClearGrant()
        {
            lock (locker)
            {
                hasActiveGrant = false;
                activeGrantedSeq = 0;
                waiting = false;
                granted = false;
                denyReason = string.Empty;
                grantEvent.Set();
                grantEvent.Reset();
            }
        }

        public void Reset()
        {
            ClearGrant();
        }
    }

    /// <summary>
    /// Server-side lease authority: owns duel generation, acquire/release validation,
    /// overtake hold, and durable telemetry event lines (JSONL kind strings).
    /// Testable without a live duel.dll worker.
    /// </summary>
    sealed class LlmStrategicPromptLeaseAuthority
    {
        readonly LlmStrategicPromptLease lease = new LlmStrategicPromptLease();
        readonly List<string> telemetryEvents = new List<string>();
        int authoritativeDuelGeneration;
        bool duelGenerationStarted;
        int heldLogCounter;

        public LlmStrategicPromptLease Lease { get { return lease; } }
        public int AuthoritativeDuelGeneration { get { return authoritativeDuelGeneration; } }
        public bool IsActive { get { return lease.IsActive; } }
        public IList<string> TelemetryEvents { get { return telemetryEvents; } }
        public int HeldLogCounter { get { return heldLogCounter; } set { heldLogCounter = value; } }

        /// <summary>
        /// PvP worker duel lifecycle: establishes non-client-sourced generation.
        /// Single-duel workers start at 1; multi-duel hosts may call again to advance.
        /// </summary>
        public int BeginDuelGeneration()
        {
            if (!duelGenerationStarted)
            {
                authoritativeDuelGeneration = 1;
                duelGenerationStarted = true;
            }
            else
            {
                authoritativeDuelGeneration++;
            }
            lease.Reset();
            return authoritativeDuelGeneration;
        }

        /// <summary>Test/helper: force the authoritative generation to a value.</summary>
        public void SetAuthoritativeDuelGenerationForTest(int generation)
        {
            authoritativeDuelGeneration = generation;
            duelGenerationStarted = true;
        }

        public LlmStrategicPromptLeaseAcquireResult TryAcquireFromWire(
            int requestDuelGeneration,
            ulong requestSeq,
            int requestAbsoluteActingSeat,
            LlmPromptFamily requestFamily,
            int requestedTimeoutMs,
            int actorPlayer,
            ulong engineRunEffectSeq,
            LlmPromptFamily engineFamily,
            int engineAbsoluteActingSeat,
            DateTime utcNow)
        {
            if (!duelGenerationStarted)
            {
                BeginDuelGeneration();
            }

            // Session-stamped actor is sole seat authority for spoof checks.
            if (actorPlayer != requestAbsoluteActingSeat)
            {
                return LlmStrategicPromptLeaseAcquireResult.Deny("actor_seat_spoof");
            }
            if (requestDuelGeneration != authoritativeDuelGeneration)
            {
                return LlmStrategicPromptLeaseAcquireResult.Deny("duel_generation_mismatch");
            }

            LlmStrategicPromptLeaseAcquireResult result = lease.TryAcquire(
                authoritativeDuelGeneration,
                engineRunEffectSeq,
                engineFamily,
                engineAbsoluteActingSeat,
                new LlmStrategicPromptLeaseAcquireRequest()
                {
                    DuelGeneration = requestDuelGeneration,
                    RunEffectSeq = requestSeq,
                    AbsoluteActingSeat = requestAbsoluteActingSeat,
                    PromptFamily = requestFamily,
                    RequestedTimeoutMs = requestedTimeoutMs,
                    UtcNow = utcNow,
                });

            if (result.Granted)
            {
                heldLogCounter = 0;
                EmitEvent("llm_strategic_prompt_lease_started", lease, requestSeq, utcNow,
                    null, null);
                EmitEvent("llm_strategic_prompt_lease_held", lease, requestSeq, utcNow,
                    null, null);
            }
            return result;
        }

        public bool TryReleaseFromWire(
            int requestDuelGeneration,
            ulong requestSeq,
            int actorPlayer,
            string reason,
            string disposition,
            DateTime utcNow,
            out string failureReason)
        {
            failureReason = null;
            if (!lease.IsActive)
            {
                failureReason = "lease_not_active";
                return false;
            }
            if (requestDuelGeneration != authoritativeDuelGeneration ||
                requestDuelGeneration != lease.DuelGeneration)
            {
                failureReason = "duel_generation_mismatch";
                return false;
            }
            if (requestSeq != lease.RunEffectSeq)
            {
                failureReason = "run_effect_seq_mismatch";
                return false;
            }
            if (actorPlayer != lease.AbsoluteActingSeat)
            {
                failureReason = "seat_mismatch";
                return false;
            }
            string releaseReason = string.IsNullOrEmpty(reason) ? "client_release" : reason;
            string releaseDisposition = string.IsNullOrEmpty(disposition) ? "none" : disposition;
            if (!lease.TryForceRelease(releaseReason, releaseDisposition, utcNow))
            {
                failureReason = "release_failed";
                return false;
            }
            EmitEvent("llm_strategic_prompt_lease_released", lease, requestSeq, utcNow,
                releaseReason, releaseDisposition);
            if (releaseReason != "provider_commit" &&
                releaseDisposition != "committed")
            {
                EmitContinuationDiverged(requestSeq, requestSeq, actorPlayer,
                    lease.PromptFamily, releaseReason, releaseDisposition, utcNow);
            }
            return true;
        }

        public bool ShouldHoldSysAct(ulong engineRunEffectSeq, DateTime utcNow)
        {
            MaybeExpire(utcNow, out _);
            return lease.ShouldHoldSysAct(
                authoritativeDuelGeneration, engineRunEffectSeq, utcNow);
        }

        public bool TryBlockOvertake(
            ulong inputSeq,
            int actorSeat,
            string inputKind,
            DateTime utcNow,
            out string blockReason)
        {
            MaybeExpire(utcNow, out _);
            if (!lease.TryBlockOvertake(inputSeq, actorSeat, inputKind, utcNow, out blockReason))
            {
                return false;
            }
            EmitEvent("llm_strategic_prompt_lease_overtake_blocked", lease, inputSeq, utcNow,
                blockReason, null);
            return true;
        }

        public bool TryReleaseAfterAcceptedInput(
            ulong inputSeq,
            int actorSeat,
            DateTime utcNow,
            out string failureReason)
        {
            failureReason = null;
            if (!lease.IsActive)
            {
                failureReason = "lease_not_active";
                return false;
            }
            if (inputSeq != lease.RunEffectSeq || actorSeat != lease.AbsoluteActingSeat)
            {
                failureReason = "not_matching_lease";
                return false;
            }
            if (!lease.TryAcceptMatchingInput(inputSeq, actorSeat, utcNow, out failureReason))
            {
                return false;
            }
            if (!lease.TryReleaseOnCommit(utcNow, "provider_commit"))
            {
                failureReason = "release_failed";
                return false;
            }
            EmitEvent("llm_strategic_prompt_lease_released", lease, inputSeq, utcNow,
                "provider_commit", "committed");
            return true;
        }

        public bool MaybeExpire(DateTime utcNow, out bool expired)
        {
            expired = false;
            if (!lease.TryExpire(utcNow, "timeout", "temporary_cpu", out expired) || !expired)
            {
                return false;
            }
            EmitEvent("llm_strategic_prompt_lease_expired", lease, lease.RunEffectSeq, utcNow,
                "timeout", "temporary_cpu");
            EmitContinuationDiverged(
                lease.RunEffectSeq,
                lease.RunEffectSeq,
                lease.AbsoluteActingSeat,
                lease.PromptFamily,
                "timeout",
                "temporary_cpu",
                utcNow);
            return true;
        }

        public bool ForceRelease(string reason, string disposition, DateTime utcNow)
        {
            if (!lease.IsActive)
            {
                return false;
            }
            ulong seq = lease.RunEffectSeq;
            int seat = lease.AbsoluteActingSeat;
            LlmPromptFamily family = lease.PromptFamily;
            if (!lease.TryForceRelease(reason, disposition, utcNow))
            {
                return false;
            }
            EmitEvent("llm_strategic_prompt_lease_released", lease, seq, utcNow,
                reason ?? "forced", disposition ?? "none");
            if ((reason ?? string.Empty) != "provider_commit")
            {
                EmitContinuationDiverged(seq, seq, seat, family,
                    reason ?? "forced", disposition ?? "none", utcNow);
            }
            return true;
        }

        public bool BlocksMechanicalAutomaticCommit(ulong mechanicalSeq)
        {
            return lease.BlocksMechanicalAutomaticCommit(mechanicalSeq);
        }

        public void ClearTelemetry()
        {
            telemetryEvents.Clear();
        }

        public bool TelemetryContainsKind(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                return false;
            }
            string needle = "\"kind\":\"" + kind + "\"";
            foreach (string line in telemetryEvents)
            {
                if (line != null && line.IndexOf(needle, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        void EmitEvent(
            string kind,
            LlmStrategicPromptLease source,
            ulong currentSeq,
            DateTime utcNow,
            string reason,
            string disposition)
        {
            string line;
            if (kind == "llm_strategic_prompt_lease_started")
            {
                line = LlmDecisionLogSerializer.SerializeStrategicPromptLeaseStarted(source, utcNow);
            }
            else if (kind == "llm_strategic_prompt_lease_held")
            {
                line = LlmDecisionLogSerializer.SerializeStrategicPromptLeaseHeld(
                    source, currentSeq, utcNow);
            }
            else if (kind == "llm_strategic_prompt_lease_overtake_blocked")
            {
                line = LlmDecisionLogSerializer.SerializeStrategicPromptLeaseOvertakeBlocked(
                    source, currentSeq,
                    source == null ? -1 : source.AbsoluteActingSeat,
                    "input", reason ?? "overtake", utcNow);
            }
            else if (kind == "llm_strategic_prompt_lease_released")
            {
                line = LlmDecisionLogSerializer.SerializeStrategicPromptLeaseReleased(
                    source, reason ?? "released", disposition ?? "none", utcNow);
            }
            else if (kind == "llm_strategic_prompt_lease_expired")
            {
                line = LlmDecisionLogSerializer.SerializeStrategicPromptLeaseExpired(
                    source, reason ?? "timeout", disposition ?? "temporary_cpu", utcNow);
            }
            else
            {
                line = null;
            }
            if (!string.IsNullOrEmpty(line))
            {
                telemetryEvents.Add(line);
            }
        }

        void EmitContinuationDiverged(
            ulong originSeq,
            ulong currentSeq,
            int seat,
            LlmPromptFamily family,
            string reason,
            string disposition,
            DateTime utcNow)
        {
            string line = LlmDecisionLogSerializer.SerializeStrategicContinuationDiverged(
                originSeq, currentSeq, seat, family,
                reason ?? "unknown", disposition ?? "none",
                "strategic_prompt_lease_continuation");
            if (!string.IsNullOrEmpty(line))
            {
                telemetryEvents.Add(line);
            }
        }
    }
}

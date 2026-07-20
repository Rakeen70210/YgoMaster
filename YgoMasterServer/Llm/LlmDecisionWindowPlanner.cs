using System.Collections.Generic;

namespace YgoMaster
{
    enum LlmDecisionWindowRoute
    {
        Broker,
        Automatic,
        TemporaryCpu,
        CpuFallback,
        Default,
        Suppressed,
    }

    class LlmTemporaryCpuSelectionCoordinator
    {
        bool active;
        ulong runEffectSeq;
        int player = -1;

        public bool IsActive { get { return active; } }
        public ulong RunEffectSeq { get { return runEffectSeq; } }
        public int Player { get { return player; } }

        public bool TryBegin(ulong seq, int actingPlayer)
        {
            if (active)
            {
                return false;
            }

            active = true;
            runEffectSeq = seq;
            player = actingPlayer;
            return true;
        }

        public bool ShouldRestore(DuelViewType viewType, int param1)
        {
            return ShouldRestore(viewType, param1, false);
        }

        public bool ShouldRestore(
            DuelViewType viewType,
            int param1,
            bool isSummonPlacementPrompt)
        {
            if (!active)
            {
                return false;
            }

            if (viewType == DuelViewType.CpuThinking ||
                viewType == DuelViewType.RunDialog ||
                viewType == DuelViewType.RunList ||
                IsSummonTransitionView(viewType))
            {
                return false;
            }

            if (viewType != DuelViewType.WaitInput)
            {
                return true;
            }

            return param1 != (int)DuelMenuActType.Selection &&
                param1 != (int)DuelMenuActType.Location;
        }

        public bool ShouldSuppressDecisionView(DuelViewType viewType, int param1)
        {
            return ShouldSuppressDecisionView(viewType, param1, false);
        }

        public bool ShouldSuppressDecisionView(
            DuelViewType viewType,
            int param1,
            bool isSummonPlacementPrompt)
        {
            return active && !ShouldRestore(viewType, param1, isSummonPlacementPrompt) &&
                (viewType == DuelViewType.WaitInput ||
                    viewType == DuelViewType.RunDialog ||
                    viewType == DuelViewType.RunList);
        }

        public static bool CanBeginRequest(
            ulong requestSeq,
            ulong currentSeq,
            DuelViewType currentViewType,
            int currentParam1,
            int currentActingPlayer,
            int actorPlayer,
            int requestedPlayer)
        {
            return CanBeginRequest(
                requestSeq,
                currentSeq,
                currentViewType,
                currentParam1,
                currentActingPlayer,
                actorPlayer,
                requestedPlayer,
                false);
        }

        public static bool CanBeginRequest(
            ulong requestSeq,
            ulong currentSeq,
            DuelViewType currentViewType,
            int currentParam1,
            int currentActingPlayer,
            int actorPlayer,
            int requestedPlayer,
            bool currentSummonPlacementPrompt)
        {
            return CanBeginRequest(
                requestSeq,
                currentSeq,
                currentViewType,
                currentParam1,
                currentActingPlayer,
                actorPlayer,
                requestedPlayer,
                currentSummonPlacementPrompt,
                false);
        }

        public static bool CanBeginRequest(
            ulong requestSeq,
            ulong currentSeq,
            DuelViewType currentViewType,
            int currentParam1,
            int currentActingPlayer,
            int actorPlayer,
            int requestedPlayer,
            bool currentSummonPlacementPrompt,
            bool isWatchdogRecovery)
        {
            bool ownsSelectionPrompt =
                (currentViewType == DuelViewType.WaitInput &&
                    currentParam1 == (int)DuelMenuActType.Selection &&
                    currentActingPlayer == requestedPlayer) ||
                (currentViewType == DuelViewType.RunList &&
                    currentParam1 == requestedPlayer) ||
                (currentViewType == DuelViewType.WaitInput &&
                    currentParam1 == (int)DuelMenuActType.Location &&
                    currentActingPlayer == requestedPlayer);
            bool ownsWatchdogRecoveryPrompt = isWatchdogRecovery &&
                currentViewType == DuelViewType.WaitInput &&
                currentActingPlayer == requestedPlayer;
            return requestSeq == currentSeq &&
                (ownsSelectionPrompt || ownsWatchdogRecoveryPrompt) &&
                actorPlayer == requestedPlayer &&
                requestedPlayer >= 0 && requestedPlayer <= 1;
        }

        public void MarkRestored()
        {
            active = false;
            runEffectSeq = 0;
            player = -1;
        }

        public void Reset()
        {
            MarkRestored();
        }

        static bool IsSummonTransitionView(DuelViewType viewType)
        {
            switch (viewType)
            {
                case DuelViewType.Noop:
                case DuelViewType.Null:
                case DuelViewType.WaitFrame:
                case DuelViewType.FieldChange:
                case DuelViewType.MaterialSet:
                case DuelViewType.MaterialReset:
                case DuelViewType.MaterialRun:
                case DuelViewType.TuningSet:
                case DuelViewType.TuningReset:
                case DuelViewType.TuningRun:
                case DuelViewType.RunSummon:
                case DuelViewType.RunSpSummon:
                case DuelViewType.RunFusion:
                case DuelViewType.CutinSummon:
                case DuelViewType.CutinFusion:
                case DuelViewType.OverlaySet:
                case DuelViewType.OverlayReset:
                case DuelViewType.OverlayRun:
                case DuelViewType.LinkSet:
                case DuelViewType.LinkReset:
                case DuelViewType.LinkRun:
                    return true;
                default:
                    return false;
            }
        }
    }

    enum LlmPromptFamily
    {
        WaitInput,
        RunDialog,
        RunList,
        InfoOnly,
        Unsupported,
    }

    class LlmDecisionWindowPlan
    {
        public LlmDecisionWindowRoute Route { get; set; }
        public LlmPromptFamily PromptFamily { get; set; }
        public string Reason { get; set; }
        public DecisionSnapshot Snapshot { get; set; }
        public LegalAction AutomaticAction { get; set; }
        public int MyId { get; set; }
        public bool HasActingPlayer { get; set; }
        public bool BrokerControlsActingPlayer { get; set; }
        public bool IsUnsupportedWindow { get; set; }
    }

    class LlmStuckWindowObservation
    {
        public bool ShouldRecover { get; set; }
        public int RepeatCount { get; set; }
        public string Fingerprint { get; set; }
        public List<string> RouteHistory { get; set; }

        public LlmStuckWindowObservation()
        {
            Fingerprint = string.Empty;
            RouteHistory = new List<string>();
        }
    }

    class LlmStuckWindowWatchdog
    {
        const int MaxHistory = 12;
        readonly int repeatThreshold;
        readonly Dictionary<string, int> repeatCounts = new Dictionary<string, int>();
        readonly List<string> routeHistory = new List<string>();
        int duelGeneration = -1;
        int phase = -1;

        public LlmStuckWindowWatchdog(int repeatThreshold = 3)
        {
            this.repeatThreshold = repeatThreshold < 2 ? 2 : repeatThreshold;
        }

        public LlmStuckWindowObservation Observe(
            DecisionSnapshot snapshot,
            LlmDecisionWindowPlan plan,
            int currentDuelGeneration,
            bool brokerRequestPending)
        {
            LlmStuckWindowObservation result = new LlmStuckWindowObservation();
            if (snapshot == null || plan == null)
            {
                return result;
            }

            if (duelGeneration != currentDuelGeneration || phase != snapshot.CurrentPhase)
            {
                Reset();
                duelGeneration = currentDuelGeneration;
                phase = snapshot.CurrentPhase;
            }

            bool isWatchableAutomaticDecline =
                plan.Route == LlmDecisionWindowRoute.Automatic &&
                LlmDecisionWindowPlanner.ShouldPreserveWatchdogAfterAutomaticAction(
                    plan.AutomaticAction);
            if (!plan.HasActingPlayer || !plan.BrokerControlsActingPlayer ||
                brokerRequestPending || plan.Route == LlmDecisionWindowRoute.Suppressed ||
                (plan.Route == LlmDecisionWindowRoute.Automatic &&
                    !isWatchableAutomaticDecline) ||
                plan.Route == LlmDecisionWindowRoute.TemporaryCpu ||
                snapshot.ViewType != DuelViewType.WaitInput)
            {
                return result;
            }

            string fingerprint = BuildFingerprint(snapshot, plan);
            int count;
            repeatCounts.TryGetValue(fingerprint, out count);
            count++;
            repeatCounts[fingerprint] = count;
            routeHistory.Add(snapshot.RunEffectSeq + ":" + fingerprint);
            if (routeHistory.Count > MaxHistory)
            {
                routeHistory.RemoveAt(0);
            }

            result.Fingerprint = fingerprint;
            result.RepeatCount = count;
            result.RouteHistory = new List<string>(routeHistory);
            result.ShouldRecover = count >= repeatThreshold;
            if (result.ShouldRecover)
            {
                Reset();
            }
            return result;
        }

        public void Reset()
        {
            repeatCounts.Clear();
            routeHistory.Clear();
            duelGeneration = -1;
            phase = -1;
        }

        static string BuildFingerprint(DecisionSnapshot snapshot, LlmDecisionWindowPlan plan)
        {
            return snapshot.ViewType + "|" + snapshot.ViewParam1 + "|" +
                snapshot.ViewParam2 + "|" + snapshot.ActingPlayer + "|" +
                plan.Route + "|" + (plan.Reason ?? string.Empty);
        }
    }

    class LlmAutomaticActionLoopGuard
    {
        const int MaxPlacementAttempts = 2;
        readonly LlmSummonInteractionTracker interactionTracker;

        public LlmAutomaticActionLoopGuard()
        {
            interactionTracker = new LlmSummonInteractionTracker(MaxPlacementAttempts);
        }

        public bool TryAcquire(DecisionSnapshot snapshot, LegalAction action)
        {
            if (!IsSummonPlacementAction(action))
            {
                Reset();
                return true;
            }

            LlmSummonInteractionDecision decision = TryAcquirePlacement(
                snapshot,
                action,
                0);
            if (decision.Disposition == LlmSummonInteractionDisposition.Rejected)
            {
                // Preserve the historical bool helper's "new prompt starts a new
                // guard" behavior. Production uses TryAcquirePlacement directly,
                // which rejects cross-card and cross-generation reuse.
                Reset();
                decision = TryAcquirePlacement(snapshot, action, 0);
            }
            return decision.Disposition == LlmSummonInteractionDisposition.Started ||
                decision.Disposition == LlmSummonInteractionDisposition.Retry;
        }

        public LlmSummonInteractionDecision TryAcquirePlacement(
            DecisionSnapshot snapshot,
            LegalAction action,
            int duelGeneration)
        {
            if (!PreparePlacementAction(snapshot, action))
            {
                Reset();
                return null;
            }

            return interactionTracker.Observe(BuildPlacementObservation(
                snapshot,
                action,
                duelGeneration));
        }

        public bool PreparePlacementAction(
            DecisionSnapshot snapshot,
            LegalAction action)
        {
            if (action == null || action.Command != DuelCommandType.Decide)
            {
                return false;
            }

            bool activePlacementFallback = interactionTracker.IsActive &&
                snapshot != null &&
                snapshot.ViewType == DuelViewType.WaitInput;
            if (IsRawSummonPlacementPrompt(snapshot) || activePlacementFallback)
            {
                action.IsMechanical = true;
                action.RequiresTarget = true;
                action.StrategicRole = "placement";
                action.TargetScope = "summon_placement";
                if (action.CardUniqueId <= 0 && interactionTracker.IsActive)
                {
                    action.CardUniqueId = interactionTracker.ActiveCardUniqueId;
                }
                if (action.CardId <= 0 && interactionTracker.IsActive)
                {
                    action.CardId = interactionTracker.ActiveCardId;
                }
                if (action.CardUniqueId <= 0 && snapshot.ViewParam2 > 0)
                {
                    action.CardUniqueId = snapshot.ViewParam2;
                }
            }

            return IsSummonPlacementAction(action);
        }

        public LlmSummonInteractionDecision BeginSummonInteraction(
            DecisionSnapshot snapshot,
            LegalAction action,
            int duelGeneration)
        {
            if (!IsSummonDeclarationAction(action))
            {
                Reset();
                return null;
            }

            return interactionTracker.Begin(BuildInteractionObservation(
                snapshot,
                action,
                duelGeneration,
                false));
        }

        public LlmSummonInteractionDecision ObserveCompletion(
            DecisionSnapshot snapshot,
            int duelGeneration)
        {
            if (!interactionTracker.IsActive)
            {
                return null;
            }

            if (snapshot == null)
            {
                return interactionTracker.Observe(BuildCurrentObservation(
                    null,
                    duelGeneration,
                    true,
                    false,
                    true,
                    null));
            }

            bool hasPlacementPrompt = HasSummonPlacementAction(snapshot);
            hasPlacementPrompt = hasPlacementPrompt ||
                IsRawSummonPlacementPrompt(snapshot) ||
                IsActiveUnannotatedPlacementPrompt(snapshot);
            bool isTransition = IsSummonFollowupTransition(snapshot);
            bool isCancellation = snapshot.ViewType == DuelViewType.WaitInput &&
                snapshot.ViewParam1 == (int)DuelMenuActType.Location &&
                !hasPlacementPrompt;
            bool isBoundary = !hasPlacementPrompt && !isTransition &&
                snapshot.ViewType == DuelViewType.WaitInput;
            LlmSummonResourceFacts currentFacts =
                LlmSummonResourceFacts.FromSnapshot(
                    snapshot,
                    interactionTracker.ActiveCardId,
                    interactionTracker.ActiveSourceFamily);
            LlmSummonCompletionEvidence evidence = interactionTracker.CompareCurrentResources(
                currentFacts,
                isBoundary);
            return interactionTracker.Observe(BuildCurrentObservation(
                snapshot,
                duelGeneration,
                isBoundary,
                isTransition,
                isCancellation,
                evidence));
        }

        public bool IsActive
        {
            get { return interactionTracker.IsActive; }
        }

        public void Reset()
        {
            interactionTracker.Reset();
        }

        static LlmSummonInteractionObservation BuildPlacementObservation(
            DecisionSnapshot snapshot,
            LegalAction selectedAction,
            int duelGeneration)
        {
            return BuildInteractionObservation(
                snapshot,
                selectedAction,
                duelGeneration,
                true);
        }

        static LlmSummonInteractionObservation BuildInteractionObservation(
            DecisionSnapshot snapshot,
            LegalAction selectedAction,
            int duelGeneration,
            bool isPlacementPrompt)
        {
            string sourceFamily = selectedAction.Card == null ? null :
                selectedAction.Card.SummonFamily;
            if (string.IsNullOrEmpty(sourceFamily) ||
                string.Equals(sourceFamily, "unknown", System.StringComparison.OrdinalIgnoreCase))
            {
                sourceFamily = FindSourceFamily(snapshot, selectedAction.CardId);
            }
            if (string.IsNullOrEmpty(sourceFamily))
            {
                sourceFamily = "unknown";
            }

            return new LlmSummonInteractionObservation()
            {
                DuelGeneration = duelGeneration,
                RunEffectSeq = snapshot == null ? 0 : snapshot.RunEffectSeq,
                ActingPlayer = selectedAction.Player,
                CardUniqueId = selectedAction.CardUniqueId,
                CardId = selectedAction.CardId,
                SourceFamily = sourceFamily,
                ExpectedFollowupFamily = "summon_placement",
                IsPlacementPrompt = isPlacementPrompt,
                ResourceFacts = LlmSummonResourceFacts.FromSnapshot(
                    snapshot,
                    selectedAction.CardId,
                    sourceFamily),
            };
        }

        LlmSummonInteractionObservation BuildCurrentObservation(
            DecisionSnapshot snapshot,
            int duelGeneration,
            bool isBoundary,
            bool isTransition,
            bool isCancellation,
            LlmSummonCompletionEvidence evidence)
        {
            return new LlmSummonInteractionObservation()
            {
                DuelGeneration = duelGeneration,
                RunEffectSeq = snapshot == null ? 0 : snapshot.RunEffectSeq,
                ActingPlayer = snapshot == null ? interactionTracker.ActiveActingPlayer : snapshot.ActingPlayer,
                CardUniqueId = interactionTracker.ActiveCardUniqueId,
                CardId = interactionTracker.ActiveCardId,
                SourceFamily = interactionTracker.ActiveSourceFamily,
                ExpectedFollowupFamily = interactionTracker.ActiveExpectedFollowupFamily,
                IsPlacementPrompt = false,
                IsSummonTransition = isTransition,
                IsCancellation = isCancellation,
                IsBoundary = isBoundary,
                CompletionEvidence = evidence,
            };
        }

        bool HasSummonPlacementAction(DecisionSnapshot snapshot)
        {
            if (snapshot == null || snapshot.LegalActions == null)
            {
                return false;
            }

            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (IsSummonPlacementAction(action))
                {
                    return true;
                }
            }
            return false;
        }

        static bool IsRawSummonPlacementPrompt(DecisionSnapshot snapshot)
        {
            return snapshot != null &&
                snapshot.ViewType == DuelViewType.WaitInput &&
                snapshot.ViewParam1 == (int)DuelMenuActType.Location &&
                snapshot.ViewParam2 > 0;
        }

        bool IsActiveUnannotatedPlacementPrompt(DecisionSnapshot snapshot)
        {
            return interactionTracker.IsActive &&
                snapshot != null &&
                snapshot.ViewType == DuelViewType.WaitInput &&
                snapshot.LegalActions != null &&
                snapshot.LegalActions.Count == 0;
        }

        bool IsSummonFollowupTransition(DecisionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }
            return snapshot.ViewType == DuelViewType.RunDialog ||
                snapshot.ViewType == DuelViewType.RunList ||
                (snapshot.ViewType == DuelViewType.WaitInput &&
                    snapshot.ViewParam1 == (int)DuelMenuActType.Selection);
        }

        static string FindSourceFamily(DecisionSnapshot snapshot, int cardId)
        {
            if (snapshot == null || snapshot.SelfResources == null || cardId <= 0)
            {
                return null;
            }
            if (snapshot.SelfResources.ExtraDeck != null)
            {
                foreach (LlmSelfResourceExtraDeckEntry entry in snapshot.SelfResources.ExtraDeck)
                {
                    if (entry != null && entry.CardId == cardId &&
                        !string.IsNullOrEmpty(entry.SummonFamily))
                    {
                        return entry.SummonFamily;
                    }
                }
            }
            return FindSourceFamily(snapshot.SelfResources.Hand, cardId) ??
                FindSourceFamily(snapshot.SelfResources.Field, cardId);
        }

        static string FindSourceFamily(
            System.Collections.Generic.IList<LlmSelfResourceCard> cards,
            int cardId)
        {
            if (cards == null)
            {
                return null;
            }
            foreach (LlmSelfResourceCard card in cards)
            {
                if (card != null && card.CardId == cardId &&
                    !string.IsNullOrEmpty(card.SummonFamily))
                {
                    return card.SummonFamily;
                }
            }
            return null;
        }

        public static bool IsSummonPlacementAction(LegalAction action)
        {
            return action != null &&
                action.Command == DuelCommandType.Decide &&
                action.TargetScope == "summon_placement";
        }

        public static bool IsSummonDeclarationAction(LegalAction action)
        {
            if (action == null || action.Kind != LegalActionKind.Command)
            {
                return false;
            }

            return action.Command == DuelCommandType.Summon ||
                action.Command == DuelCommandType.SummonSp ||
                action.Command == DuelCommandType.Pendulum;
        }
    }

    static class LlmDecisionWindowPlanner
    {
        public static LlmDecisionWindowPlan Plan(
            DecisionSnapshot snapshot,
            bool hasActingPlayer,
            int actingPlayer,
            int myId,
            bool brokerControlsActingPlayer,
            LlmBrokerRequestGateDecision? gateDecision,
            bool isInfoDialog,
            bool hasLocalDefaultInteraction,
            LegalAction automaticAction)
        {
            LlmDecisionWindowPlan plan = new LlmDecisionWindowPlan()
            {
                Snapshot = snapshot,
                MyId = myId,
                HasActingPlayer = hasActingPlayer,
                BrokerControlsActingPlayer = brokerControlsActingPlayer,
                AutomaticAction = automaticAction,
                PromptFamily = ClassifyPromptFamily(snapshot, isInfoDialog),
            };

            if (isInfoDialog || !hasActingPlayer)
            {
                plan.Route = LlmDecisionWindowRoute.Default;
                plan.Reason = isInfoDialog ? "info_only" : "no_acting_player";
                return plan;
            }

            if (!brokerControlsActingPlayer)
            {
                if (actingPlayer == myId)
                {
                    plan.Route = LlmDecisionWindowRoute.Default;
                    plan.Reason = "uncontrolled_local_player";
                    return plan;
                }

                plan.Route = LlmDecisionWindowRoute.CpuFallback;
                plan.Reason = "uncontrolled_remote_player";
                return plan;
            }

            if (RequiresCpuFallback(snapshot))
            {
                plan.Route = LlmDecisionWindowRoute.TemporaryCpu;
                plan.Reason = "selection_multi_step";
                plan.AutomaticAction = null;
                plan.IsUnsupportedWindow = false;
                return plan;
            }

            // Same-seq suppress must win over automatic commit so we never auto-act
            // on top of an in-flight broker request for this prompt.
            if (gateDecision.HasValue)
            {
                switch (gateDecision.Value)
                {
                    case LlmBrokerRequestGateDecision.SuppressForPendingRequest:
                        plan.Route = LlmDecisionWindowRoute.Suppressed;
                        plan.Reason = "pending_broker_request";
                        return plan;
                    case LlmBrokerRequestGateDecision.SuppressForCompletedRequest:
                        plan.Route = LlmDecisionWindowRoute.Suppressed;
                        plan.Reason = "completed_broker_request";
                        return plan;
                }
            }

            if (automaticAction != null)
            {
                plan.Route = LlmDecisionWindowRoute.Automatic;
                plan.Reason = snapshot != null && snapshot.StrategicActionCount == 0 ?
                    "mechanical_window" :
                    "automatic_action";
                return plan;
            }

            LegalAction knownMechanicalAction;
            if (TrySelectKnownMechanicalAutomaticAction(snapshot, out knownMechanicalAction))
            {
                plan.Route = LlmDecisionWindowRoute.Automatic;
                plan.Reason = IsEmptyResponseDeclineAction(knownMechanicalAction) ?
                    knownMechanicalAction.TargetScope :
                    "summon_placement";
                plan.AutomaticAction = knownMechanicalAction;
                return plan;
            }

            if (snapshot == null || snapshot.LegalActions.Count == 0)
            {
                plan.Route = LlmDecisionWindowRoute.CpuFallback;
                // Empty/missing controlled prompts are discovery gaps for the adapter.
                plan.Reason = snapshot == null ? "missing_snapshot" :
                    (string.IsNullOrEmpty(snapshot.StrategicWindowReason) ?
                        "no_actions" :
                        snapshot.StrategicWindowReason);
                plan.IsUnsupportedWindow = true;
                return plan;
            }

            if (!snapshot.IsStrategicWindow)
            {
                // Intentional mechanical skip (not an "unsupported" surface).
                plan.Route = LlmDecisionWindowRoute.CpuFallback;
                plan.Reason = string.IsNullOrEmpty(snapshot.StrategicWindowReason) ?
                    "mechanical_only" :
                    snapshot.StrategicWindowReason;
                plan.IsUnsupportedWindow = false;
                return plan;
            }

            // FallbackToDefault only applies to strategic broker candidates. Do not
            // let a different-seq in-flight request block automatic mechanical paths.
            if (gateDecision.HasValue &&
                gateDecision.Value == LlmBrokerRequestGateDecision.FallbackToDefault)
            {
                plan.Route = LlmDecisionWindowRoute.CpuFallback;
                plan.Reason = "gate_fallback";
                return plan;
            }

            plan.Route = LlmDecisionWindowRoute.Broker;
            plan.Reason = IsAttackTargetSelection(snapshot) ?
                "attack_target" :
                "strategic_choices";
            return plan;
        }

        public static LegalAction ResolveAutomaticActionForCommit(
            LlmDecisionWindowPlan plan,
            LegalAction extractedAutomaticAction)
        {
            if (plan == null || plan.Route != LlmDecisionWindowRoute.Automatic)
            {
                return null;
            }

            return plan.AutomaticAction ?? extractedAutomaticAction;
        }

        public static bool RequiresCpuFallback(DecisionSnapshot snapshot)
        {
            return snapshot != null &&
                ((snapshot.ViewType == DuelViewType.WaitInput &&
                    snapshot.ViewParam1 == (int)DuelMenuActType.Selection &&
                    !IsAttackTargetSelection(snapshot)) ||
                 snapshot.ViewType == DuelViewType.RunList);
        }

        static bool IsAttackTargetSelection(DecisionSnapshot snapshot)
        {
            if (snapshot == null || snapshot.LegalActions == null ||
                snapshot.LegalActions.Count < 2)
            {
                return false;
            }
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action == null || !action.IsAttackTargetSelection ||
                    action.Command != DuelCommandType.Decide ||
                    action.TargetScope != "attack_target")
                {
                    return false;
                }
            }
            return true;
        }

        static bool TrySelectKnownMechanicalAutomaticAction(
            DecisionSnapshot snapshot,
            out LegalAction action)
        {
            action = null;

            if (snapshot == null ||
                snapshot.IsStrategicWindow ||
                snapshot.LegalActions.Count == 0)
            {
                return false;
            }

            // Empty optional CheckChain/TrueCancel window: sole mechanical decline.
            if (snapshot.LegalActions.Count == 1 &&
                IsEmptyResponseDeclineAction(snapshot.LegalActions[0]))
            {
                action = snapshot.LegalActions[0];
                return true;
            }

            foreach (LegalAction candidate in snapshot.LegalActions)
            {
                if (!IsSummonPlacementAction(candidate))
                {
                    return false;
                }
            }

            action = snapshot.LegalActions[0];
            return true;
        }

        public static bool ShouldPreserveWatchdogAfterAutomaticAction(LegalAction action)
        {
            return IsEmptyResponseDeclineAction(action);
        }

        static bool IsEmptyResponseDeclineAction(LegalAction action)
        {
            return action != null &&
                action.Kind == LegalActionKind.Cancel &&
                action.IsMechanical &&
                !action.CancelDecide &&
                (action.TargetScope == "empty_check_chain_decline" ||
                    action.TargetScope == "empty_check_timing_decline" ||
                    action.StrategicRole == "empty_response_window");
        }

        static bool IsSummonPlacementAction(LegalAction action)
        {
            return action != null &&
                action.Kind == LegalActionKind.Command &&
                action.Command == DuelCommandType.Decide &&
                action.IsMechanical &&
                action.TargetScope == "summon_placement";
        }

        static LlmPromptFamily ClassifyPromptFamily(DecisionSnapshot snapshot, bool isInfoDialog)
        {
            if (snapshot == null)
            {
                return LlmPromptFamily.Unsupported;
            }

            switch (snapshot.ViewType)
            {
                case DuelViewType.WaitInput:
                    return LlmPromptFamily.WaitInput;
                case DuelViewType.RunDialog:
                    return isInfoDialog ? LlmPromptFamily.InfoOnly : LlmPromptFamily.RunDialog;
                case DuelViewType.RunList:
                    return LlmPromptFamily.RunList;
                default:
                    return LlmPromptFamily.Unsupported;
            }
        }
    }
}

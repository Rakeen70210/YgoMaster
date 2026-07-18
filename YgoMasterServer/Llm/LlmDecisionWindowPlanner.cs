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
            if (!active)
            {
                return false;
            }

            if (viewType == DuelViewType.CpuThinking ||
                viewType == DuelViewType.RunDialog ||
                viewType == DuelViewType.RunList)
            {
                return false;
            }

            return viewType != DuelViewType.WaitInput ||
                param1 != (int)DuelMenuActType.Selection;
        }

        public bool ShouldSuppressDecisionView(DuelViewType viewType, int param1)
        {
            return active && !ShouldRestore(viewType, param1) &&
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
            bool ownsSelectionPrompt =
                (currentViewType == DuelViewType.WaitInput &&
                    currentParam1 == (int)DuelMenuActType.Selection &&
                    currentActingPlayer == requestedPlayer) ||
                (currentViewType == DuelViewType.RunList &&
                    currentParam1 == requestedPlayer);
            return requestSeq == currentSeq &&
                ownsSelectionPrompt &&
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

    class LlmAutomaticActionLoopGuard
    {
        string lastPlacementSignature;

        public bool TryAcquire(DecisionSnapshot snapshot, LegalAction action)
        {
            if (!IsSummonPlacementAction(action))
            {
                Reset();
                return true;
            }

            string signature = BuildPlacementSignature(snapshot, action);
            if (signature == lastPlacementSignature)
            {
                return false;
            }

            lastPlacementSignature = signature;
            return true;
        }

        public void Reset()
        {
            lastPlacementSignature = null;
        }

        static string BuildPlacementSignature(DecisionSnapshot snapshot, LegalAction selectedAction)
        {
            string signature = selectedAction.Player + ":" + selectedAction.CardUniqueId;
            if (snapshot == null)
            {
                return signature + ":" + selectedAction.Position;
            }

            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (IsSummonPlacementAction(action))
                {
                    signature += ":" + action.Position;
                }
            }
            return signature;
        }

        static bool IsSummonPlacementAction(LegalAction action)
        {
            return action != null &&
                action.Command == DuelCommandType.Decide &&
                action.TargetScope == "summon_placement";
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
                plan.Reason = IsEmptyCheckChainDeclineAction(knownMechanicalAction) ?
                    "empty_check_chain_decline" :
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
            plan.Reason = "strategic_choices";
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
                    snapshot.ViewParam1 == (int)DuelMenuActType.Selection) ||
                 snapshot.ViewType == DuelViewType.RunList);
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
                IsEmptyCheckChainDeclineAction(snapshot.LegalActions[0]))
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

        static bool IsEmptyCheckChainDeclineAction(LegalAction action)
        {
            return action != null &&
                action.Kind == LegalActionKind.Cancel &&
                action.IsMechanical &&
                !action.CancelDecide &&
                (action.TargetScope == "empty_check_chain_decline" ||
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

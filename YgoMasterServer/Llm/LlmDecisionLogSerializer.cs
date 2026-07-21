using System;
using System.Collections.Generic;

namespace YgoMaster
{
    static class LlmDecisionLogSerializer
    {
        public static string SerializeDecisionWindow(DecisionSnapshot snapshot)
        {
            // Default decision-window log: never include private self_resources.
            return MiniJSON.Json.Serialize(BuildDecisionWindowData(snapshot));
        }

        /// <summary>
        /// Explicit private audit path (YGOMASTER-LLM-005). Payload is included only when
        /// the emission policy passes for the configured control seat (defense in depth).
        /// Does not alter SerializeDecisionWindow or /decide request shape.
        /// </summary>
        public static string SerializeSelfResourcesAudit(
            DecisionSnapshot snapshot,
            bool selfResourcesAuditEnabled,
            int configuredControlPlayer)
        {
            bool emitPayload = LlmSelfResourcesAuditPolicy.ShouldSerializeSelfResourcesPayload(
                selfResourcesAuditEnabled,
                configuredControlPlayer,
                snapshot);
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "self_resources_audit" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", snapshot == null ? 0L : (long)snapshot.RunEffectSeq },
                { "controlled_player", snapshot == null ? -1 : snapshot.ControlledPlayer },
                { "acting_player", snapshot == null ? -1 : snapshot.ActingPlayer },
                { "configured_control_player", configuredControlPlayer },
                { "audit_enabled", selfResourcesAuditEnabled },
                { "audit_emitted", emitPayload },
            };
            if (emitPayload)
            {
                data["self_resources"] = LlmSelfResourceProjection.Serialize(
                    snapshot.SelfResources,
                    null);
            }
            return MiniJSON.Json.Serialize(data);
        }

        /// <summary>
        /// Test helper: treats the snapshot's controlled seat as the configured control
        /// player when the caller has already authorized the seat.
        /// </summary>
        public static string SerializeSelfResourcesAudit(
            DecisionSnapshot snapshot,
            bool selfResourcesAuditEnabled)
        {
            int seat = snapshot != null ? snapshot.ControlledPlayer : -1;
            return SerializeSelfResourcesAudit(snapshot, selfResourcesAuditEnabled, seat);
        }

        public static string SerializeSearchStarted(
            DecisionSnapshot snapshot,
            LlmSearchLimits limits)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_search_started" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", snapshot == null ? 0L : (long)snapshot.RunEffectSeq },
                { "controlled_player", snapshot == null ? -1 : snapshot.ControlledPlayer },
                { "acting_player", snapshot == null ? -1 : snapshot.ActingPlayer },
            };
            if (limits != null)
            {
                data["limits"] = new Dictionary<string, object>()
                {
                    { "max_strategic_depth", limits.MaxStrategicDepth },
                    { "max_nodes", limits.MaxNodes },
                    { "max_wall_ms", limits.MaxWallMs },
                    { "beam_width", limits.BeamWidth },
                    { "max_serialized_bytes", limits.MaxSerializedBytes },
                };
            }
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeSearchCompleted(LlmPlanningSearchAuditResult result)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_search_completed" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "success", result != null && result.Success },
                { "status", result != null ? result.Status : "failed" },
                { "elapsed_ms", result != null ? result.ElapsedMs : 0L },
            };
            if (result != null && !string.IsNullOrEmpty(result.Error))
            {
                data["error"] = result.Error;
            }
            if (result != null && result.Graph != null)
            {
                data["search_id"] = result.Graph.SearchId;
                data["root_run_effect_seq"] = result.Graph.RootRunEffectSeq;
                if (result.Graph.Coverage != null)
                {
                    data["coverage"] = new Dictionary<string, object>()
                    {
                        { "legal_root_actions", result.Graph.Coverage.LegalRootActions },
                        { "represented_root_actions", result.Graph.Coverage.RepresentedRootActions },
                        { "root_shell_count", result.Graph.Coverage.RootShellCount },
                        { "continuation_expansion_count", result.Graph.Coverage.ContinuationExpansionCount },
                        { "pruned_nodes", result.Graph.Coverage.PrunedNodes },
                        { "boundary_nodes", result.Graph.Coverage.BoundaryNodes },
                    };
                }
                // Slice 2B: compact immediate-outcome summaries on the planning audit path.
                List<object> outcomes = CollectImmediateOutcomeSummaries(result.Graph);
                if (outcomes.Count > 0)
                {
                    data["immediate_outcomes"] = outcomes;
                }
            }
            if (result != null && result.Projection != null)
            {
                data["search_projection"] = result.Projection;
            }
            return MiniJSON.Json.Serialize(data);
        }

        /// <summary>
        /// Default-off, seat-gated immediate-outcome audit line (YGOMASTER-LLM-005 Slice 2B).
        /// Never widens /decide or SchemaVersion. Only emits annotation fields — never
        /// dumps PublicState or opponent-hidden identities.
        /// </summary>
        public static string SerializeImmediateOutcomeAudit(
            DecisionSnapshot snapshot,
            LlmSearchGraph graph,
            bool auditEnabled,
            int configuredControlPlayer)
        {
            bool emit = LlmImmediateOutcomeAuditPolicy.ShouldEmitAudit(
                auditEnabled, configuredControlPlayer, snapshot);
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_immediate_outcome_audit" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", snapshot == null ? 0L : (long)snapshot.RunEffectSeq },
                { "controlled_player", snapshot == null ? -1 : snapshot.ControlledPlayer },
                { "acting_player", snapshot == null ? -1 : snapshot.ActingPlayer },
                { "configured_control_player", configuredControlPlayer },
                { "audit_enabled", auditEnabled },
                { "audit_emitted", emit },
            };
            if (emit && graph != null)
            {
                data["immediate_outcomes"] = CollectImmediateOutcomeSummaries(graph);
                data["search_id"] = graph.SearchId;
            }
            return MiniJSON.Json.Serialize(data);
        }

        static List<object> CollectImmediateOutcomeSummaries(LlmSearchGraph graph)
        {
            List<object> list = new List<object>();
            if (graph == null || graph.Lines == null)
            {
                return list;
            }
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line == null || !line.IsRootShell || line.ImmediateOutcome == null)
                {
                    continue;
                }
                LlmImmediateOutcomeAnnotation ann = line.ImmediateOutcome;
                Dictionary<string, object> item = new Dictionary<string, object>()
                {
                    { "root_action_id", line.RootActionId },
                    { "primary_effect_stopped", ann.PrimaryEffectStopped },
                    { "target_effect_expected_to_resolve", ann.TargetEffectExpectedToResolve },
                    { "primary_disruption_value", ann.PrimaryDisruptionValue },
                    { "value_zero_primary_penalty", ann.ValueZeroPrimaryPenalty },
                    { "provenance", ann.Provenance },
                    { "boundary", ann.Boundary },
                    { "unknown_card_semantics", ann.IsUnknownCardSemantics },
                    { "target_already_activated", ann.TargetAlreadyActivated },
                    { "target_card_id", ann.TargetCardId },
                    { "target_name", ann.TargetName },
                    { "response_capabilities", ToStringObjectList(ann.ResponseCapabilities) },
                    { "secondary_benefits", ToStringObjectList(ann.SecondaryBenefits) },
                    { "score", line.Score },
                };
                if (line.ScoreFeatures != null && line.ScoreFeatures.Count > 0)
                {
                    Dictionary<string, object> features = new Dictionary<string, object>();
                    foreach (KeyValuePair<string, int> pair in line.ScoreFeatures)
                    {
                        features[pair.Key] = pair.Value;
                    }
                    item["score_features"] = features;
                }
                list.Add(item);
            }
            return list;
        }

        static List<object> ToStringObjectList(System.Collections.Generic.IList<string> items)
        {
            List<object> list = new List<object>();
            if (items == null)
            {
                return list;
            }
            foreach (string s in items)
            {
                list.Add(s);
            }
            return list;
        }

        public static string SerializeSearchFallback(DecisionSnapshot snapshot, string reason)
        {
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "llm_search_fallback" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", snapshot == null ? 0L : (long)snapshot.RunEffectSeq },
                { "controlled_player", snapshot == null ? -1 : snapshot.ControlledPlayer },
                { "reason", reason ?? "search_failed" },
            });
        }

        static Dictionary<string, object> BuildDecisionWindowData(DecisionSnapshot snapshot)
        {
            return new Dictionary<string, object>()
            {
                { "kind", "decision_window" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", (long)snapshot.RunEffectSeq },
                { "view_type", snapshot.ViewType.ToString() },
                { "view_type_id", (int)snapshot.ViewType },
                { "view_param1", snapshot.ViewParam1 },
                { "view_param2", snapshot.ViewParam2 },
                { "view_param3", snapshot.ViewParam3 },
                { "acting_player", snapshot.ActingPlayer },
                { "controlled_player", snapshot.ControlledPlayer },
                { "turn", snapshot.Turn },
                { "turn_player", snapshot.TurnPlayer },
                { "current_phase", snapshot.CurrentPhase },
                { "current_step", snapshot.CurrentStep },
                { "is_strategic_window", snapshot.IsStrategicWindow },
                { "strategic_window_reason", snapshot.StrategicWindowReason },
                { "strategic_action_count", snapshot.StrategicActionCount },
                { "mechanical_action_count", snapshot.MechanicalActionCount },
                { "public_state", SerializePublicState(snapshot.PublicState) },
                { "board_context", SerializeBoardContext(snapshot.PublicState) },
                { "opponent_context", SerializeOpponentContext(
                    snapshot.PublicState,
                    snapshot.ControlledPlayer) },
                { "turn_memory", SerializeTurnMemory(snapshot.TurnMemory) },
                { "duel_history", SerializeDuelHistory(snapshot == null ? null : snapshot.DuelHistory) },
                { "legal_actions", SerializeLegalActions(snapshot.LegalActions) },
            };
        }

        /// <summary>
        /// Shared schema-v4 duel_history payload for decision_window and /decide request.
        /// </summary>
        public static Dictionary<string, object> SerializeDuelHistory(LlmDuelHistoryState history)
        {
            return LlmDuelHistoryTracker.SerializeDuelHistoryObject(history);
        }

        public static string SerializePublicDuelEvent(LlmPublicDuelEvent publicEvent)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            if (publicEvent != null)
            {
                Dictionary<string, object> projection =
                    LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(publicEvent);
                foreach (KeyValuePair<string, object> pair in projection)
                {
                    data[pair.Key] = pair.Value;
                }
            }
            // Event-kind from the projection is history kind (normal_summon, etc.).
            // Audit envelope kind is always llm_public_duel_event and must win.
            // Preserve history kind separately as public_event_kind for Slice 5 audit tooling.
            data["kind"] = "llm_public_duel_event";
            if (publicEvent != null)
            {
                data["public_event_kind"] = LlmPublicHistoryRedactionPolicy.KindName(publicEvent.Kind);
                data["duel_generation"] = publicEvent.DuelGeneration;
            }
            else
            {
                data["duel_generation"] = 0;
            }
            return MiniJSON.Json.Serialize(data);
        }

        static void AddSchemaV4HistoryGroundingFields(
            Dictionary<string, object> data,
            LlmBrokerDecisionResponse response)
        {
            if (data == null)
            {
                return;
            }
            data["opponent_action_assessment"] =
                response == null ? null : response.OpponentActionAssessment;
            // Explicit empty list remains present when citations are empty.
            List<object> ids = new List<object>();
            if (response != null && response.HistoryEventIdsUsed != null)
            {
                foreach (long eventId in response.HistoryEventIdsUsed)
                {
                    ids.Add(eventId);
                }
            }
            data["history_event_ids_used"] = ids;
        }

        public static string SerializeCommittedCommand(
            ulong runEffectSeq,
            int player,
            int position,
            int index,
            int commandId)
        {
            DuelCommandType command = (DuelCommandType)commandId;
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "committed_action" },
                { "run_effect_seq", (long)runEffectSeq },
                { "action_type", "command" },
                { "player", player },
                { "position", position },
                { "index", index },
                { "command", command.ToString() },
                { "command_id", commandId },
            });
        }

        public static string SerializeCommittedPhase(ulong runEffectSeq, int phaseId)
        {
            DuelPhase phase = (DuelPhase)phaseId;
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "committed_action" },
                { "run_effect_seq", (long)runEffectSeq },
                { "action_type", "move_phase" },
                { "phase", phase.ToString() },
                { "phase_id", phaseId },
            });
        }

        public static string SerializeCommittedDialogResult(ulong runEffectSeq, uint result)
        {
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "committed_action" },
                { "run_effect_seq", (long)runEffectSeq },
                { "action_type", "dialog_result" },
                { "result", (long)result },
            });
        }

        public static string SerializeCommittedListIndex(ulong runEffectSeq, int index)
        {
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "committed_action" },
                { "run_effect_seq", (long)runEffectSeq },
                { "action_type", "list_index" },
                { "index", index },
            });
        }

        public static string SerializeBrokerCommittedAction(
            ulong requestRunEffectSeq,
            ulong commitRunEffectSeq,
            LlmBrokerDecisionResponse response,
            LegalAction action)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_broker_committed" },
                { "request_run_effect_seq", (long)requestRunEffectSeq },
                { "commit_run_effect_seq", (long)commitRunEffectSeq },
                { "action_id", response == null ? -1 : response.ActionId },
                { "reason", response == null ? null : response.Reason },
                { "confidence", response == null ? null : response.Confidence },
                { "plan", response == null ? null : response.Plan },
                { "why_now", response == null ? null : response.WhyNow },
                { "alternatives_considered", response == null ? null : response.AlternativesConsidered },
                { "risk", response == null ? null : response.Risk },
                { "opponent_board_assessment", response == null ? null : response.OpponentBoardAssessment },
                { "action", SerializeLegalAction(action) },
            };
            AddSchemaV4HistoryGroundingFields(data, response);
            if (action != null)
            {
                data["action_type"] = GetKindName(action.Kind);
            }
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeBrokerDeterministicRecovery(
            ulong requestRunEffectSeq,
            ulong commitRunEffectSeq,
            string rejectedError,
            LlmBrokerDecisionResponse response,
            LegalAction action)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_broker_deterministic_recovery" },
                { "request_run_effect_seq", (long)requestRunEffectSeq },
                { "commit_run_effect_seq", (long)commitRunEffectSeq },
                { "rejected_error", rejectedError },
                { "provider_action_id", response == null ? -1 : response.ActionId },
                { "provider_reason", response == null ? null : response.Reason },
                { "action", SerializeLegalAction(action) },
            };
            if (action != null)
            {
                data["action_type"] = GetKindName(action.Kind);
            }
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeBrokerAutomaticAction(
            ulong runEffectSeq,
            string reason,
            LegalAction action)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_broker_automatic_action" },
                { "run_effect_seq", (long)runEffectSeq },
                { "reason", reason },
                { "action", SerializeLegalAction(action) },
            };
            if (action != null)
            {
                data["action_type"] = GetKindName(action.Kind);
            }
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeWindowRouted(
            ulong runEffectSeq,
            LlmDecisionWindowPlan plan)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_broker_window_routed" },
                { "run_effect_seq", (long)runEffectSeq },
                { "route", plan == null ? null : plan.Route.ToString() },
                { "prompt_family", plan == null ? null : plan.PromptFamily.ToString() },
                { "reason", plan == null ? null : plan.Reason },
                { "my_id", plan == null ? -1 : plan.MyId },
                { "has_acting_player", plan != null && plan.HasActingPlayer },
                { "broker_controls_acting_player", plan != null && plan.BrokerControlsActingPlayer },
                { "has_automatic_action", plan != null && plan.AutomaticAction != null },
            };
            if (plan != null && plan.Snapshot != null)
            {
                data["view_type"] = plan.Snapshot.ViewType.ToString();
                data["view_type_id"] = (int)plan.Snapshot.ViewType;
                data["acting_player"] = plan.Snapshot.ActingPlayer;
                data["controlled_player"] = plan.Snapshot.ControlledPlayer;
                data["turn"] = plan.Snapshot.Turn;
                data["turn_player"] = plan.Snapshot.TurnPlayer;
                data["current_phase"] = plan.Snapshot.CurrentPhase;
                data["current_step"] = plan.Snapshot.CurrentStep;
                data["legal_action_count"] = plan.Snapshot.LegalActions.Count;
                data["strategic_action_count"] = plan.Snapshot.StrategicActionCount;
                data["mechanical_action_count"] = plan.Snapshot.MechanicalActionCount;
            }
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeBrokerUnsupportedWindow(
            ulong runEffectSeq,
            LlmDecisionWindowPlan plan)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_broker_unsupported_window" },
                { "run_effect_seq", (long)runEffectSeq },
                { "route", plan == null ? null : plan.Route.ToString() },
                { "prompt_family", plan == null ? null : plan.PromptFamily.ToString() },
                { "reason", plan == null ? null : plan.Reason },
                { "my_id", plan == null ? -1 : plan.MyId },
            };
            if (plan != null && plan.Snapshot != null)
            {
                data["view_type"] = plan.Snapshot.ViewType.ToString();
                data["view_type_id"] = (int)plan.Snapshot.ViewType;
                data["acting_player"] = plan.Snapshot.ActingPlayer;
                data["controlled_player"] = plan.Snapshot.ControlledPlayer;
                data["turn"] = plan.Snapshot.Turn;
                data["turn_player"] = plan.Snapshot.TurnPlayer;
                data["current_phase"] = plan.Snapshot.CurrentPhase;
                data["current_step"] = plan.Snapshot.CurrentStep;
                data["legal_action_count"] = plan.Snapshot.LegalActions.Count;
                data["strategic_action_count"] = plan.Snapshot.StrategicActionCount;
                data["mechanical_action_count"] = plan.Snapshot.MechanicalActionCount;
            }
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeBrokerSkippedWindow(ulong runEffectSeq, DecisionSnapshot snapshot, string reason)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_broker_skipped_window" },
                { "run_effect_seq", (long)runEffectSeq },
                { "reason", reason },
            };
            if (snapshot != null)
            {
                data["view_type"] = snapshot.ViewType.ToString();
                data["view_type_id"] = (int)snapshot.ViewType;
                data["acting_player"] = snapshot.ActingPlayer;
                data["controlled_player"] = snapshot.ControlledPlayer;
                data["turn"] = snapshot.Turn;
                data["turn_player"] = snapshot.TurnPlayer;
                data["current_phase"] = snapshot.CurrentPhase;
                data["current_step"] = snapshot.CurrentStep;
                data["is_strategic_window"] = snapshot.IsStrategicWindow;
                data["strategic_window_reason"] = snapshot.StrategicWindowReason;
                data["strategic_action_count"] = snapshot.StrategicActionCount;
                data["mechanical_action_count"] = snapshot.MechanicalActionCount;
                data["legal_action_count"] = snapshot.LegalActions.Count;
            }
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeSummonInteraction(
            ulong runEffectSeq,
            LlmSummonInteractionDecision decision,
            string eventKind)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", string.IsNullOrEmpty(eventKind) ? "summon_interaction" : eventKind },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", (long)runEffectSeq },
                { "disposition", decision == null ? null : decision.Disposition.ToString() },
                { "reason", decision == null ? null : decision.Reason },
                { "attempt", decision == null ? 0 : decision.Attempt },
                { "stable_signature", decision == null ? null : decision.StableSignature },
                { "interaction_signature", decision == null ? null : decision.InteractionSignature },
                { "originating_run_effect_seq", decision == null ? 0L : (long)decision.OriginatingRunEffectSeq },
                { "temporary_cpu", decision != null && decision.ShouldUseTemporaryCpu },
            };
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeStuckWindowRecovered(
            DecisionSnapshot snapshot,
            LlmDecisionWindowPlan plan,
            LlmStuckWindowObservation observation)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_broker_stuck_window_recovered" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", snapshot == null ? 0L : (long)snapshot.RunEffectSeq },
                { "view_type", snapshot == null ? null : snapshot.ViewType.ToString() },
                { "view_param1", snapshot == null ? 0 : snapshot.ViewParam1 },
                { "view_param2", snapshot == null ? 0 : snapshot.ViewParam2 },
                { "acting_player", snapshot == null ? -1 : snapshot.ActingPlayer },
                { "route", plan == null ? null : plan.Route.ToString() },
                { "reason", plan == null ? null : plan.Reason },
                { "prompt_family", plan == null ? null : plan.PromptFamily.ToString() },
                { "repeat_count", observation == null ? 0 : observation.RepeatCount },
                { "fingerprint", observation == null ? null : observation.Fingerprint },
                { "route_history", observation == null ? null : observation.RouteHistory },
            };
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeBrokerRequestStarted(DecisionSnapshot snapshot)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_broker_request_started" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "request_run_effect_seq", snapshot == null ? -1 : (long)snapshot.RunEffectSeq },
            };
            if (snapshot != null)
            {
                data["view_type"] = snapshot.ViewType.ToString();
                data["view_type_id"] = (int)snapshot.ViewType;
                data["acting_player"] = snapshot.ActingPlayer;
                data["controlled_player"] = snapshot.ControlledPlayer;
                data["turn"] = snapshot.Turn;
                data["turn_player"] = snapshot.TurnPlayer;
                data["current_phase"] = snapshot.CurrentPhase;
                data["current_step"] = snapshot.CurrentStep;
                data["is_strategic_window"] = snapshot.IsStrategicWindow;
                data["strategic_window_reason"] = snapshot.StrategicWindowReason;
                data["strategic_action_count"] = snapshot.StrategicActionCount;
                data["mechanical_action_count"] = snapshot.MechanicalActionCount;
                data["legal_action_count"] = snapshot.LegalActions.Count;
            }
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeBrokerResponse(
            ulong requestRunEffectSeq,
            ulong currentRunEffectSeq,
            LlmBrokerDecisionResult result)
        {
            bool success = result != null && result.IsSuccess;
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_broker_response" },
                { "request_run_effect_seq", (long)requestRunEffectSeq },
                { "current_run_effect_seq", (long)currentRunEffectSeq },
                { "success", success },
            };
            if (success)
            {
                LlmBrokerDecisionResponse response = result.Response;
                LegalAction action = result.Action;
                data["action_id"] = response == null ? -1 : response.ActionId;
                data["reason"] = response == null ? null : response.Reason;
                data["confidence"] = response == null ? null : response.Confidence;
                data["plan"] = response == null ? null : response.Plan;
                data["why_now"] = response == null ? null : response.WhyNow;
                data["alternatives_considered"] =
                    response == null ? null : response.AlternativesConsidered;
                data["risk"] = response == null ? null : response.Risk;
                data["opponent_board_assessment"] =
                    response == null ? null : response.OpponentBoardAssessment;
                AddSchemaV4HistoryGroundingFields(data, response);
                data["latency_ms"] = result.LatencyMs;
                data["request_json"] = result.RequestJson;
                data["response_json"] = result.ResponseJson;
                if (action != null)
                {
                    data["action_type"] = GetKindName(action.Kind);
                }
            }
            else
            {
                string error = result == null ? "missing_result" : result.Error;
                data["error"] = string.IsNullOrEmpty(error) ? "unknown_error" : error;
                if (result != null && !string.IsNullOrEmpty(result.ErrorDetail))
                {
                    data["error_detail"] = result.ErrorDetail;
                }
                if (result != null)
                {
                    data["latency_ms"] = result.LatencyMs;
                }
                if (result != null)
                {
                    data["request_json"] = result.RequestJson;
                    data["response_json"] = result.ResponseJson;
                }
            }
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeBrokerCommitSkipped(
            ulong requestRunEffectSeq,
            ulong currentRunEffectSeq,
            string reason)
        {
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "llm_broker_commit_skipped" },
                { "request_run_effect_seq", (long)requestRunEffectSeq },
                { "current_run_effect_seq", (long)currentRunEffectSeq },
                { "reason", reason },
            });
        }

        public static string SerializeAttackTargetDivergence(
            DecisionSnapshot snapshot,
            string reason,
            string fallback)
        {
            AttackTargetContext origin = snapshot == null ? null : snapshot.AttackTargetContext;
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "llm_attack_target_divergence" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", snapshot == null ? 0L : (long)snapshot.RunEffectSeq },
                { "origin_run_effect_seq", origin == null ? 0L : (long)origin.OriginRunEffectSeq },
                { "duel_generation", origin == null ? 0 : origin.OriginDuelGeneration },
                { "attacker_player", origin == null ? -1 : origin.AttackingPlayer },
                { "attacker_position", origin == null ? -1 : origin.AttackerPosition },
                { "reason", reason ?? "broker_fallback" },
                { "fallback", fallback ?? "cpu" },
                { "legal_target_count", snapshot == null ? 0 : snapshot.LegalActions.Count },
            });
        }

        public static string SerializeBrokerRejectedAction(
            ulong requestRunEffectSeq,
            ulong currentRunEffectSeq,
            string error)
        {
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "llm_broker_rejected" },
                { "request_run_effect_seq", (long)requestRunEffectSeq },
                { "current_run_effect_seq", (long)currentRunEffectSeq },
                { "error", error },
            });
        }

        public static string SerializeBrokerRecoveredAction(
            ulong requestRunEffectSeq,
            ulong commitRunEffectSeq,
            string error,
            string policyBranch,
            LegalAction action,
            LlmBrokerDecisionResponse response)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_broker_recovered" },
                { "request_run_effect_seq", (long)requestRunEffectSeq },
                { "commit_run_effect_seq", (long)commitRunEffectSeq },
                { "error", error },
                { "policy_branch", policyBranch },
                { "action", SerializeLegalAction(action) },
            };
            if (action != null)
            {
                data["action_type"] = GetKindName(action.Kind);
                data["action_id"] = action.ActionId;
            }
            if (response != null)
            {
                data["provider_action_id"] = response.ActionId;
                data["reason"] = response.Reason;
                data["confidence"] = response.Confidence;
                data["opponent_board_assessment"] = response.OpponentBoardAssessment;
            }
            AddSchemaV4HistoryGroundingFields(data, response);
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeSemanticWindowDecisionReused(
            DecisionSnapshot snapshot,
            string fingerprint,
            int occurrenceCount,
            ulong originRunEffectSeq,
            LegalAction reusedAction)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_semantic_window_decision_reused" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", snapshot == null ? 0L : (long)snapshot.RunEffectSeq },
                { "origin_run_effect_seq", (long)originRunEffectSeq },
                { "fingerprint", fingerprint },
                { "occurrence_count", occurrenceCount },
                { "view_type", snapshot == null ? null : snapshot.ViewType.ToString() },
                { "view_param1", snapshot == null ? 0 : snapshot.ViewParam1 },
                { "acting_player", snapshot == null ? -1 : snapshot.ActingPlayer },
                { "reused_action", SerializeLegalAction(reusedAction) },
            };
            if (reusedAction != null)
            {
                data["action_type"] = GetKindName(reusedAction.Kind);
                data["action_id"] = reusedAction.ActionId;
                data["action_label"] = reusedAction.ActionLabel;
            }
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeSemanticWindowRecoveryFailed(
            DecisionSnapshot snapshot,
            string fingerprint,
            int occurrenceCount,
            string reason)
        {
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "llm_semantic_window_recovery_failed" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", snapshot == null ? 0L : (long)snapshot.RunEffectSeq },
                { "fingerprint", fingerprint },
                { "occurrence_count", occurrenceCount },
                { "reason", reason },
                { "acting_player", snapshot == null ? -1 : snapshot.ActingPlayer },
            });
        }

        public static string SerializeSemanticWindowRecoveryDiverged(
            DecisionSnapshot snapshot,
            string fingerprint,
            string disposition)
        {
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "llm_semantic_window_recovery_diverged" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", snapshot == null ? 0L : (long)snapshot.RunEffectSeq },
                { "fingerprint", fingerprint },
                { "disposition", disposition },
                { "acting_player", snapshot == null ? -1 : snapshot.ActingPlayer },
            });
        }

        public static Dictionary<string, object> SerializePublicState(PublicDuelState publicState)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            List<object> players = new List<object>();
            if (publicState != null)
            {
                foreach (PublicPlayerState player in publicState.Players)
                {
                    players.Add(SerializePublicPlayerState(player));
                }
            }
            data["players"] = players;
            return data;
        }

        static Dictionary<string, object> SerializePublicPlayerState(PublicPlayerState player)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "player", player.Player },
                { "life_points", player.LifePoints },
                { "positions", SerializePublicPositions(player.Positions) },
                { "known_cards", SerializeKnownCards(player.KnownCards) },
            };
            return data;
        }

        public static Dictionary<string, object> SerializeBoardContext(PublicDuelState publicState)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            List<object> players = new List<object>();
            if (publicState != null)
            {
                foreach (PublicPlayerState player in publicState.Players)
                {
                    players.Add(SerializeBoardPlayerContext(player));
                }
            }
            data["players"] = players;
            return data;
        }

        static Dictionary<string, object> SerializeBoardPlayerContext(PublicPlayerState player)
        {
            int fieldCount = 0;
            int graveyardCount = 0;
            foreach (PublicPositionState position in player.Positions)
            {
                if (position.Position >= 0 && position.Position <= 12)
                {
                    fieldCount += position.Count;
                }
                else if (position.Position == 16)
                {
                    graveyardCount = position.Count;
                }
            }

            List<object> knownGraveyardCards = new List<object>();
            foreach (PublicKnownCard card in player.KnownCards)
            {
                if (card.Position != 16)
                {
                    continue;
                }
                Dictionary<string, object> cardData = new Dictionary<string, object>()
                {
                    { "card_id", card.CardId },
                    { "card_unique_id", card.CardUniqueId },
                };
                if (card.Card != null && !string.IsNullOrEmpty(card.Card.Name))
                {
                    cardData["name"] = card.Card.Name;
                }
                knownGraveyardCards.Add(cardData);
            }

            return new Dictionary<string, object>()
            {
                { "player", player.Player },
                { "life_points", player.LifePoints },
                { "field_count", fieldCount },
                { "graveyard_count", graveyardCount },
                { "known_graveyard_count", knownGraveyardCards.Count },
                { "known_graveyard_cards", knownGraveyardCards },
            };
        }

        public static Dictionary<string, object> SerializeOpponentContext(
            PublicDuelState publicState,
            int controlledPlayer)
        {
            PublicPlayerState opponent = FindOpponent(publicState, controlledPlayer);
            if (opponent == null)
            {
                return new Dictionary<string, object>()
                {
                    { "opponent_player", -1 },
                    { "life_points", 0 },
                    { "field_count", 0 },
                    { "graveyard_count", 0 },
                    { "known_public_threats", new List<object>() },
                    { "summary", "opponent context unavailable" },
                };
            }

            int fieldCount = 0;
            int graveyardCount = 0;
            foreach (PublicPositionState position in opponent.Positions)
            {
                if (position.Position >= 0 && position.Position <= 12)
                {
                    fieldCount += position.Count;
                }
                else if (position.Position == 16)
                {
                    graveyardCount = position.Count;
                }
            }

            List<object> knownPublicThreats = new List<object>();
            int knownFieldCount = 0;
            foreach (PublicKnownCard card in opponent.KnownCards)
            {
                if (!IsOpponentPublicThreatPosition(card.Position))
                {
                    continue;
                }
                // Same public-identity gate as SerializeKnownCards (no face-down / extra / deck).
                if (!IsPubliclyExposableKnownCardIdentity(card))
                {
                    continue;
                }
                if (card.Position >= 0 && card.Position <= 12)
                {
                    knownFieldCount++;
                }
                Dictionary<string, object> threat = new Dictionary<string, object>()
                {
                    { "card_id", card.CardId },
                    { "card_unique_id", card.CardUniqueId },
                    { "position", card.Position },
                    { "index", card.Index },
                    { "zone", GetPublicZoneName(card.Position) },
                    { "face", card.Face },
                };
                if (card.Card != null && !string.IsNullOrEmpty(card.Card.Name))
                {
                    threat["name"] = card.Card.Name;
                }
                knownPublicThreats.Add(threat);
            }

            return new Dictionary<string, object>()
            {
                { "opponent_player", opponent.Player },
                { "life_points", opponent.LifePoints },
                { "field_count", fieldCount },
                { "graveyard_count", graveyardCount },
                { "known_field_count", knownFieldCount },
                { "known_public_threats", knownPublicThreats },
                { "summary", BuildOpponentContextSummary(fieldCount, knownFieldCount) },
            };
        }

        static PublicPlayerState FindOpponent(PublicDuelState publicState, int controlledPlayer)
        {
            if (publicState == null)
            {
                return null;
            }
            foreach (PublicPlayerState player in publicState.Players)
            {
                if (player.Player != controlledPlayer)
                {
                    return player;
                }
            }
            return null;
        }

        static bool IsOpponentPublicThreatPosition(int position)
        {
            return (position >= 0 && position <= 12) ||
                position == 13 ||
                position == 16 ||
                position == 17;
        }

        static string GetPublicZoneName(int position)
        {
            if (position >= 0 && position <= 12)
            {
                return "field";
            }
            if (position == LlmPublicHistoryRedactionPolicy.PosHand)
            {
                return "revealed_hand";
            }
            if (position == LlmPublicHistoryRedactionPolicy.PosExtra)
            {
                return "extra_deck";
            }
            if (position == LlmPublicHistoryRedactionPolicy.PosDeck)
            {
                return "deck";
            }
            if (position == LlmPublicHistoryRedactionPolicy.PosGrave)
            {
                return "graveyard";
            }
            if (position == LlmPublicHistoryRedactionPolicy.PosBanishedMin)
            {
                return "banished";
            }
            return "unknown";
        }

        static string BuildOpponentContextSummary(int fieldCount, int knownFieldCount)
        {
            if (fieldCount <= 0)
            {
                return "opponent has no field cards";
            }
            if (knownFieldCount <= 0)
            {
                return "opponent has " + fieldCount + " field card(s); identities unavailable";
            }
            if (knownFieldCount < fieldCount)
            {
                return "opponent has " + fieldCount + " field card(s); " +
                    knownFieldCount + " identity known";
            }
            return "opponent has " + fieldCount + " known field card(s)";
        }

        public static Dictionary<string, object> SerializeTurnMemory(LlmTurnMemoryState memory)
        {
            if (memory == null)
            {
                memory = new LlmTurnMemoryState();
            }

            List<object> recentActions = new List<object>();
            foreach (LlmRecentActionMemory action in memory.RecentActions)
            {
                recentActions.Add(new Dictionary<string, object>()
                {
                    { "turn", action.Turn },
                    { "phase", action.Phase },
                    { "action_type", action.ActionType },
                    { "action_label", action.ActionLabel },
                    { "card_id", action.CardId },
                    { "card_name", action.CardName },
                    { "reason", action.Reason },
                    { "plan", action.Plan },
                });
            }

            List<object> cardsUsed = new List<object>();
            foreach (LlmUsedCardMemory card in memory.CardsUsedThisTurn)
            {
                cardsUsed.Add(new Dictionary<string, object>()
                {
                    { "card_id", card.CardId },
                    { "name", card.Name },
                });
            }

            Dictionary<string, object> root = new Dictionary<string, object>()
            {
                { "recent_actions", recentActions },
                { "cards_used_this_turn", cardsUsed },
                { "normal_summon_used", memory.NormalSummonUsed },
                { "phase_plan", memory.PhasePlan },
            };
            if (memory.IntendedFollowup != null)
            {
                root["intended_followup"] = memory.IntendedFollowup.ToDictionary();
            }
            return root;
        }

        static List<object> SerializePublicPositions(List<PublicPositionState> positions)
        {
            List<object> data = new List<object>();
            foreach (PublicPositionState position in positions)
            {
                data.Add(new Dictionary<string, object>()
                {
                    { "position", position.Position },
                    { "count", position.Count },
                });
            }
            return data;
        }

        static List<object> SerializeKnownCards(List<PublicKnownCard> knownCards)
        {
            List<object> data = new List<object>();
            if (knownCards == null)
            {
                return data;
            }
            foreach (PublicKnownCard card in knownCards)
            {
                if (card == null)
                {
                    continue;
                }
                // Defense in depth (YGOMASTER-LLM-005): never emit non-public identities
                // even if a fixture or extractor accidentally planted them in KnownCards.
                if (!IsPubliclyExposableKnownCardIdentity(card))
                {
                    continue;
                }
                data.Add(new Dictionary<string, object>()
                {
                    { "player", card.Player },
                    { "position", card.Position },
                    { "index", card.Index },
                    { "card_unique_id", card.CardUniqueId },
                    { "card_id", card.CardId },
                    { "face", card.Face },
                    { "card", SerializeCardMetadata(card.Card) },
                });
            }
            return data;
        }

        /// <summary>
        /// Whether a KnownCards entry may expose card_id / name in public_state sinks.
        /// Extra/Deck identities are never public. Face-down identities are never public.
        /// GY remains public. Face-up field / revealed (face-up) hand / face-up banished may expose.
        /// Extractor normalizes open/own hand face to 1 when identity is legitimately known.
        /// </summary>
        static bool IsPubliclyExposableKnownCardIdentity(PublicKnownCard card)
        {
            if (card == null)
            {
                return false;
            }
            if (card.Position == LlmPublicHistoryRedactionPolicy.PosExtra
                || card.Position == LlmPublicHistoryRedactionPolicy.PosDeck)
            {
                return false;
            }
            if (card.Position == LlmPublicHistoryRedactionPolicy.PosGrave)
            {
                return true;
            }
            // Runtime domain face-up=1; fixture-validated face-up=8.
            return IsPublicFaceUp(card.Face);
        }

        static bool IsPublicFaceUp(int face)
        {
            return face == 1 || face == 8;
        }

        public static List<object> SerializeLegalActions(List<LegalAction> legalActions)
        {
            List<object> data = new List<object>();
            foreach (LegalAction action in legalActions)
            {
                data.Add(SerializeLegalAction(action));
            }
            return data;
        }

        public static Dictionary<string, object> SerializeLegalAction(LegalAction action)
        {
            if (action == null)
            {
                return null;
            }

            Dictionary<string, object> actionData = new Dictionary<string, object>()
            {
                { "action_id", action.ActionId },
                { "kind", GetKindName(action.Kind) },
                { "action_label", action.ActionLabel },
                { "action_group", action.ActionGroup },
                { "is_mechanical", action.IsMechanical },
                { "strategic_role", action.StrategicRole },
                { "requires_target", action.RequiresTarget },
                { "target_scope", action.TargetScope },
                { "target_token", action.TargetToken },
                { "consequence_hint", action.ConsequenceHint },
            };
            if (action.EffectApplicability != null)
            {
                actionData["effect_applicability"] =
                    action.EffectApplicability.ToDictionary();
            }
            if (action.Kind == LegalActionKind.MovePhase)
            {
                actionData["phase"] = action.Phase.ToString();
                actionData["phase_id"] = (int)action.Phase;
            }
            else if (action.Kind == LegalActionKind.Command)
            {
                actionData["player"] = action.Player;
                actionData["position"] = action.Position;
                actionData["index"] = action.Index;
                actionData["command"] = action.Command.ToString();
                actionData["command_id"] = (int)action.Command;
                actionData["card_unique_id"] = action.CardUniqueId;
                actionData["card_id"] = action.CardId;
                actionData["card"] = SerializeCardMetadata(action.Card);
            }
            else if (action.Kind == LegalActionKind.DialogResult)
            {
                actionData["result"] = action.DialogResult;
                actionData["text_id"] = action.DialogTextId;
                actionData["is_yes_no_prompt"] = action.DialogIsYesNoPrompt;
            }
            else if (action.Kind == LegalActionKind.ListIndex)
            {
                actionData["index"] = action.Index;
                actionData["item_id"] = action.ListItemId;
                actionData["item_unique_id"] = action.ListItemUniqueId;
                actionData["target_unique_id"] = action.ListItemTargetUniqueId;
                actionData["msg"] = action.ListItemMsg;
                actionData["attribute"] = action.ListItemAttribute;
                actionData["from"] = action.ListItemFrom;
                actionData["select_min"] = action.ListSelectMin;
                actionData["select_max"] = action.ListSelectMax;
                actionData["is_multi_mode"] = action.ListIsMultiMode;
            }
            else if (action.Kind == LegalActionKind.Cancel)
            {
                actionData["decide"] = action.CancelDecide;
            }
            return actionData;
        }

        public static string SerializeIntendedFollowupEvaluation(
            LlmPromisedFollowupEvaluation evaluation)
        {
            if (evaluation == null)
            {
                return null;
            }
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", evaluation.EventKind },
                { "status", evaluation.Status },
                { "reason", evaluation.Reason },
                { "origin_run_effect_seq", (long)evaluation.OriginRunEffectSeq },
                { "current_run_effect_seq", (long)evaluation.CurrentRunEffectSeq },
                { "duel_generation", evaluation.DuelGeneration },
                { "origin_action_id", evaluation.OriginActionId },
                { "origin_card_id", evaluation.OriginCardId },
                { "origin_action_label", evaluation.OriginActionLabel },
                { "action_family", evaluation.ActionFamily },
                { "expected_card_id", evaluation.ExpectedCardId },
                { "expected_card_name", evaluation.ExpectedCardName },
                { "description", evaluation.Description },
                { "matched_action_id", evaluation.ActionId },
                { "current_legal_action_families", evaluation.CurrentLegalActionFamilies },
            });
        }

        public static string SerializeStrategicPromptLeaseStarted(
            LlmStrategicPromptLease lease,
            DateTime utcNow)
        {
            return SerializeStrategicPromptLeaseEvent(
                "llm_strategic_prompt_lease_started",
                lease,
                lease == null ? 0 : lease.RunEffectSeq,
                null,
                null,
                null,
                utcNow);
        }

        public static string SerializeStrategicPromptLeaseHeld(
            LlmStrategicPromptLease lease,
            ulong currentRunEffectSeq,
            DateTime utcNow)
        {
            return SerializeStrategicPromptLeaseEvent(
                "llm_strategic_prompt_lease_held",
                lease,
                currentRunEffectSeq,
                null,
                null,
                null,
                utcNow);
        }

        public static string SerializeStrategicPromptLeaseOvertakeBlocked(
            LlmStrategicPromptLease lease,
            ulong currentRunEffectSeq,
            int actorSeat,
            string inputKind,
            string reason,
            DateTime utcNow)
        {
            Dictionary<string, object> data = BuildStrategicPromptLeaseBase(
                "llm_strategic_prompt_lease_overtake_blocked",
                lease,
                currentRunEffectSeq,
                utcNow);
            data["actor_seat"] = actorSeat;
            data["input_kind"] = inputKind ?? string.Empty;
            data["reason"] = reason ?? "overtake";
            return MiniJSON.Json.Serialize(data);
        }

        public static string SerializeStrategicPromptLeaseReleased(
            LlmStrategicPromptLease lease,
            string reason,
            string disposition,
            DateTime utcNow)
        {
            return SerializeStrategicPromptLeaseEvent(
                "llm_strategic_prompt_lease_released",
                lease,
                lease == null ? 0 : lease.RunEffectSeq,
                reason,
                disposition,
                null,
                utcNow);
        }

        public static string SerializeStrategicPromptLeaseExpired(
            LlmStrategicPromptLease lease,
            string reason,
            string disposition,
            DateTime utcNow)
        {
            return SerializeStrategicPromptLeaseEvent(
                "llm_strategic_prompt_lease_expired",
                lease,
                lease == null ? 0 : lease.RunEffectSeq,
                reason,
                disposition,
                null,
                utcNow);
        }

        public static string SerializeStrategicContinuationDiverged(
            ulong originRunEffectSeq,
            ulong currentRunEffectSeq,
            int absoluteActingSeat,
            LlmPromptFamily promptFamily,
            string reason,
            string disposition,
            string detail)
        {
            return MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "kind", "llm_strategic_continuation_diverged" },
                { "origin_run_effect_seq", (long)originRunEffectSeq },
                { "current_run_effect_seq", (long)currentRunEffectSeq },
                { "absolute_acting_seat", absoluteActingSeat },
                { "prompt_family", promptFamily.ToString() },
                { "reason", reason ?? string.Empty },
                { "disposition", disposition ?? string.Empty },
                { "detail", detail ?? string.Empty },
            });
        }

        static string SerializeStrategicPromptLeaseEvent(
            string kind,
            LlmStrategicPromptLease lease,
            ulong currentRunEffectSeq,
            string reason,
            string disposition,
            string detail,
            DateTime utcNow)
        {
            Dictionary<string, object> data = BuildStrategicPromptLeaseBase(
                kind, lease, currentRunEffectSeq, utcNow);
            if (reason != null)
            {
                data["reason"] = reason;
            }
            if (disposition != null)
            {
                data["disposition"] = disposition;
            }
            if (detail != null)
            {
                data["detail"] = detail;
            }
            return MiniJSON.Json.Serialize(data);
        }

        static Dictionary<string, object> BuildStrategicPromptLeaseBase(
            string kind,
            LlmStrategicPromptLease lease,
            ulong currentRunEffectSeq,
            DateTime utcNow)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", kind },
                { "origin_run_effect_seq", lease == null ? 0L : (long)lease.RunEffectSeq },
                { "current_run_effect_seq", (long)currentRunEffectSeq },
                { "absolute_acting_seat", lease == null ? -1 : lease.AbsoluteActingSeat },
                { "prompt_family", lease == null ? null : lease.PromptFamily.ToString() },
                { "duel_generation", lease == null ? 0 : lease.DuelGeneration },
                { "elapsed_ms", lease == null ? 0L : (long)lease.ElapsedMs(utcNow) },
                { "state", lease == null ? null : lease.State.ToString() },
            };
            if (lease != null && lease.DeadlineUtc != default(DateTime))
            {
                data["deadline_utc"] = lease.DeadlineUtc.ToString("o");
            }
            return data;
        }

        static Dictionary<string, object> SerializeCardMetadata(LlmCardMetadata card)
        {
            if (card == null)
            {
                return null;
            }

            return new Dictionary<string, object>()
            {
                { "card_id", card.CardId },
                { "name", card.Name },
                { "text", card.Text },
                { "kind", card.Kind },
                { "attribute", card.Attribute },
                { "level", card.Level },
                { "atk", card.Atk },
                { "def", card.Def },
                { "scale", card.Scale },
            };
        }

        static string GetKindName(LegalActionKind kind)
        {
            switch (kind)
            {
                case LegalActionKind.MovePhase:
                    return "move_phase";
                case LegalActionKind.Command:
                    return "command";
                case LegalActionKind.DialogResult:
                    return "dialog_result";
                case LegalActionKind.ListIndex:
                    return "list_index";
                case LegalActionKind.Cancel:
                    return "cancel";
                default:
                    return kind.ToString();
            }
        }
    }
}

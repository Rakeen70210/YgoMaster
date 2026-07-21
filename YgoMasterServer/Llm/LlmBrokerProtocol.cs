using System;
using System.Collections;
using System.Collections.Generic;

namespace YgoMaster
{
    class LlmBrokerIntendedFollowup
    {
        public string ActionFamily { get; set; }
        public int CardId { get; set; }
        public string CardName { get; set; }
        public string Description { get; set; }
    }

    class LlmBrokerDecisionResponse
    {
        public ulong RunEffectSeq { get; set; }
        public int ActionId { get; set; }
        public string Reason { get; set; }
        public double? Confidence { get; set; }
        public string Plan { get; set; }
        public string WhyNow { get; set; }
        public List<string> AlternativesConsidered { get; set; }
        public string Risk { get; set; }
        public string OpponentBoardAssessment { get; set; }
        /// <summary>Schema v4: assessment of opponent public actions/history.</summary>
        public string OpponentActionAssessment { get; set; }
        /// <summary>
        /// Schema v4: detailed request-event ids cited. Required present on provider JSON
        /// (may be empty). Defaults to empty for in-process constructed responses; parse
        /// rejects omitted JSON keys before constructing a response.
        /// Explicit null is treated as missing during validation.
        /// </summary>
        public List<long> HistoryEventIdsUsed { get; set; } = new List<long>();
        public List<LlmBrokerIntendedFollowup> IntendedFollowups { get; set; } =
            new List<LlmBrokerIntendedFollowup>();
    }

    class LlmBrokerValidationResult
    {
        public bool IsValid { get; private set; }
        public string Error { get; private set; }
        public LegalAction Action { get; private set; }

        public static LlmBrokerValidationResult Valid(LegalAction action)
        {
            return new LlmBrokerValidationResult()
            {
                IsValid = true,
                Action = action,
            };
        }

        public static LlmBrokerValidationResult Invalid(string error)
        {
            return Invalid(error, null);
        }

        public static LlmBrokerValidationResult Invalid(string error, LegalAction action)
        {
            return new LlmBrokerValidationResult()
            {
                IsValid = false,
                Error = error,
                Action = action,
            };
        }
    }

    static class LlmBrokerProtocol
    {
        public const int SchemaVersion = 4;
        const double MinimumProviderConfidence = 0.5;
        const long MaximumStrategicLatencyMs = 45000;

        public static string SerializeDecisionRequest(DecisionSnapshot snapshot)
        {
            Dictionary<string, object> data = BuildDecisionRequestData(snapshot);
            return MiniJSON.Json.Serialize(data);
        }

        static Dictionary<string, object> BuildDecisionRequestData(DecisionSnapshot snapshot)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "decision_request" },
                { "schema_version", SchemaVersion },
                { "run_effect_seq", (long)snapshot.RunEffectSeq },
                { "view_type", snapshot.ViewType.ToString() },
                { "view_type_id", (int)snapshot.ViewType },
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
                { "public_state", LlmDecisionLogSerializer.SerializePublicState(snapshot.PublicState) },
                { "board_context", LlmDecisionLogSerializer.SerializeBoardContext(snapshot.PublicState) },
                { "opponent_context", LlmDecisionLogSerializer.SerializeOpponentContext(
                    snapshot.PublicState,
                    snapshot.ControlledPlayer) },
                { "turn_memory", LlmDecisionLogSerializer.SerializeTurnMemory(snapshot.TurnMemory) },
                { "duel_history", LlmDecisionLogSerializer.SerializeDuelHistory(snapshot.DuelHistory) },
                { "legal_actions", LlmDecisionLogSerializer.SerializeLegalActions(snapshot.LegalActions) },
            };
            // Additive optional request fact (schema v4 compatible; Slice 2H).
            LlmDuelCapabilities capabilities = snapshot.DuelCapabilities ??
                LlmDuelCapabilitiesProjector.Project(snapshot);
            if (capabilities != null)
            {
                data["duel_capabilities"] = capabilities.ToDictionary();
            }
            if (snapshot.AttackTargetContext != null)
            {
                AttackTargetContext origin = snapshot.AttackTargetContext;
                data["interaction_origin"] = new Dictionary<string, object>()
                {
                    { "kind", "attack" },
                    { "run_effect_seq", (long)origin.OriginRunEffectSeq },
                    { "duel_generation", origin.OriginDuelGeneration },
                    { "attacker_player", origin.AttackingPlayer },
                    { "attacker_position", origin.AttackerPosition },
                    { "attacker_card_id", origin.AttackerCardId },
                    { "attacker_unique_id", origin.AttackerUniqueId },
                    { "action_label", origin.OriginActionLabel },
                    { "reason", origin.OriginReason },
                    { "plan", origin.OriginPlan },
                };
            }
            return data;
        }

        /// <summary>
        /// Explicit schema-v3 → v4 replay/fixture migration only. Never used by the live path.
        /// </summary>
        public static bool TryMigrateSchemaV3Request(
            string schemaV3Json,
            out string schemaV4Json,
            out string error)
        {
            schemaV4Json = null;
            error = null;

            if (schemaV3Json == null)
            {
                error = "missing_schema_v3_json";
                return false;
            }
            if (string.IsNullOrWhiteSpace(schemaV3Json))
            {
                error = "empty_schema_v3_json";
                return false;
            }

            Dictionary<string, object> data =
                MiniJSON.Json.Deserialize(schemaV3Json) as Dictionary<string, object>;
            if (data == null)
            {
                error = "invalid_json";
                return false;
            }

            object rawVersion;
            if (!data.TryGetValue("schema_version", out rawVersion) || rawVersion == null)
            {
                error = "missing_schema_version";
                return false;
            }

            int schemaVersion;
            try
            {
                schemaVersion = Convert.ToInt32(rawVersion);
            }
            catch
            {
                error = "invalid_schema_version";
                return false;
            }

            if (schemaVersion == SchemaVersion)
            {
                error = "already_schema_v4";
                return false;
            }
            if (schemaVersion != 3)
            {
                error = "unsupported_schema_version";
                return false;
            }

            object legalActions;
            if (!data.TryGetValue("legal_actions", out legalActions) || legalActions == null)
            {
                error = "missing_legal_actions";
                return false;
            }
            if (!(legalActions is IList))
            {
                error = "invalid_legal_actions";
                return false;
            }

            object runEffectSeq;
            if (!data.TryGetValue("run_effect_seq", out runEffectSeq) || runEffectSeq == null)
            {
                error = "missing_run_effect_seq";
                return false;
            }

            // Deep-clone public fields; do not mutate the input string or original graph.
            Dictionary<string, object> migrated = LlmDuelHistoryState.DeepCloneDict(data);
            migrated["schema_version"] = SchemaVersion;
            // Always replace with canonical empty history (v3 had none; do not invent events).
            migrated["duel_history"] = LlmDuelHistoryTracker.CreateEmptyDuelHistoryObject();

            schemaV4Json = MiniJSON.Json.Serialize(migrated);
            return true;
        }

        public static bool TryParseDecisionResponse(
            string json,
            out LlmBrokerDecisionResponse response,
            out string error)
        {
            response = null;
            error = null;

            Dictionary<string, object> data = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
            if (data == null)
            {
                error = "invalid_json";
                return false;
            }

            ulong runEffectSeq;
            if (!TryGetUInt64(data, "run_effect_seq", out runEffectSeq, out error))
            {
                return false;
            }

            int actionId;
            if (!TryGetInt32(data, "action_id", out actionId, out error))
            {
                return false;
            }

            double confidence;
            bool hasConfidence;
            if (!TryGetOptionalDouble(data, "confidence", out confidence, out hasConfidence, out error))
            {
                return false;
            }

            List<string> alternativesConsidered;
            if (!TryGetOptionalStringList(data, "alternatives_considered", out alternativesConsidered, out error))
            {
                return false;
            }

            List<LlmBrokerIntendedFollowup> intendedFollowups;
            if (!TryGetOptionalIntendedFollowups(
                data, out intendedFollowups, out error))
            {
                return false;
            }

            // history_event_ids_used is required to be present in schema-v4 payloads.
            // Empty list is valid; missing key is not.
            List<long> historyEventIdsUsed;
            if (!TryGetRequiredHistoryEventIdsUsed(data, out historyEventIdsUsed, out error))
            {
                return false;
            }

            response = new LlmBrokerDecisionResponse()
            {
                RunEffectSeq = runEffectSeq,
                ActionId = actionId,
                Reason = GetOptionalString(data, "reason"),
                Confidence = hasConfidence ? (double?)confidence : null,
                Plan = GetOptionalString(data, "plan"),
                WhyNow = GetOptionalString(data, "why_now"),
                AlternativesConsidered = alternativesConsidered,
                Risk = GetOptionalString(data, "risk"),
                OpponentBoardAssessment = GetOptionalString(data, "opponent_board_assessment"),
                OpponentActionAssessment = GetOptionalString(data, "opponent_action_assessment"),
                HistoryEventIdsUsed = historyEventIdsUsed,
                IntendedFollowups = intendedFollowups,
            };
            return true;
        }

        public static bool TryParseErrorResponse(string json, out string error)
        {
            string detail;
            return TryParseErrorResponse(json, out error, out detail);
        }

        public static bool TryParseErrorResponse(string json, out string error, out string detail)
        {
            error = null;
            detail = null;
            Dictionary<string, object> data = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
            if (data == null)
            {
                return false;
            }

            object raw;
            if (!data.TryGetValue("error", out raw) || raw == null)
            {
                return false;
            }

            error = raw.ToString();
            if (string.IsNullOrEmpty(error))
            {
                return false;
            }

            detail = GetOptionalString(data, "detail");
            return true;
        }

        public static LlmBrokerValidationResult ValidateResponse(
            DecisionSnapshot snapshot,
            LlmBrokerDecisionResponse response)
        {
            return ValidateResponse(snapshot, response, null);
        }

        public static LlmBrokerValidationResult ValidateResponse(
            DecisionSnapshot snapshot,
            LlmBrokerDecisionResponse response,
            LegalAction expectedAction)
        {
            return ValidateResponse(snapshot, response, expectedAction, null);
        }

        public static LlmBrokerValidationResult ValidateResponse(
            DecisionSnapshot snapshot,
            LlmBrokerDecisionResponse response,
            LegalAction expectedAction,
            long? latencyMs)
        {
            if (snapshot == null)
            {
                return LlmBrokerValidationResult.Invalid("missing_snapshot");
            }
            if (response == null)
            {
                return LlmBrokerValidationResult.Invalid("missing_response");
            }
            if (response.RunEffectSeq != snapshot.RunEffectSeq)
            {
                return LlmBrokerValidationResult.Invalid("stale_run_effect_seq");
            }
            if (latencyMs.HasValue &&
                snapshot.IsStrategicWindow &&
                latencyMs.Value > MaximumStrategicLatencyMs)
            {
                return LlmBrokerValidationResult.Invalid("provider_latency_exceeded");
            }
            if (response.Confidence.HasValue && response.Confidence.Value < MinimumProviderConfidence)
            {
                return LlmBrokerValidationResult.Invalid("low_confidence");
            }
            if (IsGenericReason(response.Reason))
            {
                return LlmBrokerValidationResult.Invalid("generic_reason");
            }

            LlmBrokerValidationResult historyResult = ValidateHistoryGrounding(snapshot, response);
            if (!historyResult.IsValid)
            {
                return historyResult;
            }

            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action.ActionId == response.ActionId)
                {
                    if (expectedAction != null && !IsSameAction(action, expectedAction))
                    {
                        return LlmBrokerValidationResult.Invalid("action_changed");
                    }
                    LlmBrokerValidationResult quality = ValidateActionQuality(snapshot, action);
                    if (!quality.IsValid)
                    {
                        // Preserve the matched legal action so recovery can reuse provider intent
                        // after a quality soft-fail (e.g. early_end_phase).
                        return LlmBrokerValidationResult.Invalid(quality.Error, action);
                    }
                    return LlmBrokerValidationResult.Valid(action);
                }
            }

            return LlmBrokerValidationResult.Invalid("unknown_action_id");
        }

        /// <summary>
        /// Schema-v4 grounding: opponent assessment when opponent history is visible, and
        /// history_event_ids_used citing only detailed events on the exact request snapshot.
        /// </summary>
        static LlmBrokerValidationResult ValidateHistoryGrounding(
            DecisionSnapshot snapshot,
            LlmBrokerDecisionResponse response)
        {
            if (response.HistoryEventIdsUsed == null)
            {
                return LlmBrokerValidationResult.Invalid("missing_history_event_ids_used");
            }

            if (HasOpponentAuthoredHistory(snapshot))
            {
                if (string.IsNullOrWhiteSpace(response.OpponentActionAssessment))
                {
                    return LlmBrokerValidationResult.Invalid("missing_opponent_action_assessment");
                }
            }

            HashSet<long> detailedIds = CollectDetailedEventIds(snapshot);
            HashSet<long> seen = new HashSet<long>();
            foreach (long eventId in response.HistoryEventIdsUsed)
            {
                if (eventId <= 0)
                {
                    return LlmBrokerValidationResult.Invalid("invalid_history_event_id");
                }
                if (!seen.Add(eventId))
                {
                    return LlmBrokerValidationResult.Invalid("duplicate_history_event_id");
                }
                // Citations may only reference detailed request events — never summary ranges.
                if (!detailedIds.Contains(eventId))
                {
                    return LlmBrokerValidationResult.Invalid("unknown_history_event_id");
                }
            }

            return LlmBrokerValidationResult.Valid(null);
        }

        static HashSet<long> CollectDetailedEventIds(DecisionSnapshot snapshot)
        {
            HashSet<long> ids = new HashSet<long>();
            if (snapshot == null || snapshot.DuelHistory == null || snapshot.DuelHistory.Events == null)
            {
                return ids;
            }
            foreach (LlmPublicDuelEvent evt in snapshot.DuelHistory.Events)
            {
                if (evt != null && evt.EventId > 0)
                {
                    ids.Add((long)evt.EventId);
                }
            }
            return ids;
        }

        /// <summary>
        /// Opponent-authored history is visible when detailed events or compacted summary
        /// actor_players include a seat other than the controlled player.
        /// </summary>
        public static bool HasOpponentAuthoredHistory(DecisionSnapshot snapshot)
        {
            if (snapshot == null || snapshot.DuelHistory == null)
            {
                return false;
            }

            int controlled = snapshot.ControlledPlayer;
            foreach (LlmPublicDuelEvent evt in snapshot.DuelHistory.Events)
            {
                if (evt != null && evt.ActorPlayer >= 0 && evt.ActorPlayer != controlled)
                {
                    return true;
                }
            }

            foreach (Dictionary<string, object> summary in snapshot.DuelHistory.PriorTurnSummaries)
            {
                if (summary == null)
                {
                    continue;
                }
                object actorsRaw;
                if (!summary.TryGetValue("actor_players", out actorsRaw) || actorsRaw == null)
                {
                    continue;
                }
                IEnumerable actors = actorsRaw as IEnumerable;
                if (actors == null || actorsRaw is string)
                {
                    continue;
                }
                foreach (object actor in actors)
                {
                    if (actor == null)
                    {
                        continue;
                    }
                    try
                    {
                        int seat = Convert.ToInt32(actor);
                        if (seat >= 0 && seat != controlled)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Errors where recovery may commit a still-legal action. Stale seq must retry
        /// on the new window instead of recovering the old one.
        /// </summary>
        public static bool IsRecoverableQualityError(string error)
        {
            if (string.IsNullOrEmpty(error) || LlmBrokerRecovery.IsStaleError(error))
            {
                return false;
            }
            switch (error)
            {
                case "early_end_phase":
                case "low_confidence":
                case "generic_reason":
                case "mechanical_action":
                case "cardless_strategic_action":
                case "provider_latency_exceeded":
                case "unknown_action_id":
                case "provider_error":
                case "broker_timeout":
                case "invalid_json":
                case "missing_result":
                case "effect_applicability_contradiction":
                    return true;
                default:
                    return false;
            }
        }

        public static bool TrySelectDeterministicRecoveryAction(
            DecisionSnapshot snapshot,
            out LegalAction action)
        {
            return TrySelectHeuristicBestAction(snapshot, out action);
        }

        public static bool TrySelectHeuristicBestAction(
            DecisionSnapshot snapshot,
            out LegalAction action)
        {
            action = null;
            if (snapshot == null || snapshot.LegalActions == null || snapshot.LegalActions.Count == 0)
            {
                return false;
            }

            LegalAction bestAction = null;
            int? bestScore = null;
            foreach (LegalAction candidate in snapshot.LegalActions)
            {
                LlmBrokerValidationResult quality = ValidateActionQuality(snapshot, candidate);
                if (!quality.IsValid)
                {
                    continue;
                }

                int? score = ScoreHeuristicRecoveryAction(snapshot, candidate);
                if (!score.HasValue)
                {
                    continue;
                }

                if (!bestScore.HasValue || score.Value > bestScore.Value)
                {
                    bestAction = candidate;
                    bestScore = score;
                }
            }

            if (bestAction == null)
            {
                return false;
            }

            action = bestAction;
            return true;
        }

        static int? ScoreHeuristicRecoveryAction(DecisionSnapshot snapshot, LegalAction action)
        {
            if (action == null)
            {
                return null;
            }
            if (action.IsMechanical)
            {
                return -5;
            }

            if (action.Kind == LegalActionKind.MovePhase)
            {
                if (action.Phase == DuelPhase.End)
                {
                    return HasPlayableCommand(snapshot) ? -3 : 0;
                }
                if (action.Phase == DuelPhase.Battle)
                {
                    return 1;
                }
                return 0;
            }

            if (action.Kind != LegalActionKind.Command)
            {
                return -2;
            }

            if (action.Command == DuelCommandType.Look ||
                action.Command == DuelCommandType.Surrender ||
                action.Command == DuelCommandType.Draw ||
                action.Command == DuelCommandType.Decide)
            {
                return -5;
            }
            if (action.Command == DuelCommandType.Summon ||
                action.Command == DuelCommandType.SummonSp ||
                action.Command == DuelCommandType.Pendulum)
            {
                return action.Card != null && !string.IsNullOrEmpty(action.Card.Name) ? 5 : 2;
            }
            if (action.Command == DuelCommandType.Action)
            {
                return action.Card != null && !string.IsNullOrEmpty(action.Card.Name) ? 4 : 1;
            }
            if (action.Command == DuelCommandType.Set ||
                action.Command == DuelCommandType.SetMonst)
            {
                return action.Card != null && !string.IsNullOrEmpty(action.Card.Name) ? 3 : 1;
            }
            if (action.Command == DuelCommandType.Attack)
            {
                return 3;
            }
            if (action.Command == DuelCommandType.Reverse ||
                action.Command == DuelCommandType.TurnAtk ||
                action.Command == DuelCommandType.TurnDef)
            {
                return 1;
            }
            return 0;
        }

        static bool HasPlayableCommand(DecisionSnapshot snapshot)
        {
            foreach (LegalAction candidate in snapshot.LegalActions)
            {
                if (candidate.Kind == LegalActionKind.Command && !candidate.IsMechanical)
                {
                    return true;
                }
            }
            return false;
        }

        public static LlmBrokerValidationResult ValidateActionQuality(DecisionSnapshot snapshot, LegalAction action)
        {
            if (action == null)
            {
                return LlmBrokerValidationResult.Invalid("missing_action");
            }
            if (action.IsMechanical)
            {
                return LlmBrokerValidationResult.Invalid("mechanical_action");
            }
            if (IsCardlessStrategicCommand(action))
            {
                return LlmBrokerValidationResult.Invalid("cardless_strategic_action");
            }
            if (IsEarlyEndPhase(snapshot, action))
            {
                return LlmBrokerValidationResult.Invalid("early_end_phase");
            }
            if (LlmEffectApplicabilityAnalyzer.IsHardBlocked(action))
            {
                return LlmBrokerValidationResult.Invalid(
                    LlmEffectApplicabilityAnalyzer.ErrorEffectApplicabilityContradiction,
                    action);
            }
            return LlmBrokerValidationResult.Valid(action);
        }

        static bool IsCardlessStrategicCommand(LegalAction action)
        {
            if (action.Kind != LegalActionKind.Command || action.IsMechanical)
            {
                return false;
            }
            if (action.Command == DuelCommandType.Attack ||
                action.Command == DuelCommandType.TurnAtk ||
                action.Command == DuelCommandType.TurnDef ||
                action.Command == DuelCommandType.Reverse)
            {
                return false;
            }
            if (action.Command == DuelCommandType.Summon ||
                action.Command == DuelCommandType.SummonSp ||
                action.Command == DuelCommandType.SetMonst ||
                action.Command == DuelCommandType.Set ||
                action.Command == DuelCommandType.Action ||
                action.Command == DuelCommandType.Pendulum)
            {
                return action.CardId <= 0 || action.Card == null ||
                    string.IsNullOrEmpty(action.Card.Name);
            }
            return false;
        }

        static bool IsEarlyEndPhase(DecisionSnapshot snapshot, LegalAction action)
        {
            if (action.Kind != LegalActionKind.MovePhase || action.Phase != DuelPhase.End)
            {
                return false;
            }
            foreach (LegalAction candidate in snapshot.LegalActions)
            {
                if (candidate.Kind == LegalActionKind.Command && !candidate.IsMechanical)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsSameAction(LegalAction current, LegalAction expected)
        {
            if (current == null || expected == null)
            {
                return false;
            }
            if (current.Kind != expected.Kind)
            {
                return false;
            }
            if (current.Kind == LegalActionKind.MovePhase)
            {
                return current.Phase == expected.Phase;
            }
            if (current.Kind == LegalActionKind.DialogResult)
            {
                return current.DialogResult == expected.DialogResult &&
                    current.DialogTextId == expected.DialogTextId &&
                    current.DialogIsYesNoPrompt == expected.DialogIsYesNoPrompt;
            }
            if (current.Kind == LegalActionKind.ListIndex)
            {
                return current.Index == expected.Index &&
                    current.ListItemAttribute == expected.ListItemAttribute &&
                    current.ListItemFrom == expected.ListItemFrom &&
                    current.ListItemId == expected.ListItemId &&
                    current.ListItemMsg == expected.ListItemMsg &&
                    current.ListItemTargetUniqueId == expected.ListItemTargetUniqueId &&
                    current.ListItemUniqueId == expected.ListItemUniqueId &&
                    current.ListSelectMax == expected.ListSelectMax &&
                    current.ListSelectMin == expected.ListSelectMin &&
                    current.ListIsMultiMode == expected.ListIsMultiMode;
            }
            if (current.Kind == LegalActionKind.Cancel)
            {
                return current.CancelDecide == expected.CancelDecide;
            }

            return current.Player == expected.Player &&
                current.Position == expected.Position &&
                current.Index == expected.Index &&
                current.Command == expected.Command &&
                current.CardUniqueId == expected.CardUniqueId &&
                current.TargetToken == expected.TargetToken;
        }

        static bool IsGenericReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            string lowered = reason.Trim().ToLowerInvariant();
            if (lowered.Length < 8)
            {
                return true;
            }
            if (lowered == "summon" ||
                lowered == "play card" ||
                lowered == "provider" ||
                lowered == "valid")
            {
                return true;
            }
            return lowered.Contains("first option") ||
                lowered.Contains("select first") ||
                lowered.Contains("first legal");
        }

        static string GetOptionalString(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null)
            {
                return null;
            }
            return value.ToString();
        }

        static bool TryGetUInt64(
            Dictionary<string, object> data,
            string key,
            out ulong value,
            out string error)
        {
            value = 0;
            error = null;
            object raw;
            if (!data.TryGetValue(key, out raw) || raw == null)
            {
                error = "missing_" + key;
                return false;
            }

            try
            {
                value = Convert.ToUInt64(raw);
                return true;
            }
            catch
            {
                error = "invalid_" + key;
                return false;
            }
        }

        static bool TryGetInt32(
            Dictionary<string, object> data,
            string key,
            out int value,
            out string error)
        {
            value = 0;
            error = null;
            object raw;
            if (!data.TryGetValue(key, out raw) || raw == null)
            {
                error = "missing_" + key;
                return false;
            }

            try
            {
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                error = "invalid_" + key;
                return false;
            }
        }

        static bool TryGetOptionalDouble(
            Dictionary<string, object> data,
            string key,
            out double value,
            out bool hasValue,
            out string error)
        {
            value = 0;
            hasValue = false;
            error = null;
            object raw;
            if (!data.TryGetValue(key, out raw) || raw == null)
            {
                return true;
            }

            try
            {
                value = Convert.ToDouble(raw);
                hasValue = true;
                return true;
            }
            catch
            {
                error = "invalid_" + key;
                return false;
            }
        }

        static bool TryGetOptionalStringList(
            Dictionary<string, object> data,
            string key,
            out List<string> value,
            out string error)
        {
            value = new List<string>();
            error = null;
            object raw;
            if (!data.TryGetValue(key, out raw) || raw == null)
            {
                return true;
            }

            List<object> list = raw as List<object>;
            if (list == null)
            {
                error = "invalid_" + key;
                return false;
            }

            foreach (object item in list)
            {
                string stringItem = item as string;
                if (stringItem == null)
                {
                    error = "invalid_" + key;
                    return false;
                }
                value.Add(stringItem);
            }
            return true;
        }

        static bool TryGetOptionalIntendedFollowups(
            Dictionary<string, object> data,
            out List<LlmBrokerIntendedFollowup> value,
            out string error)
        {
            value = new List<LlmBrokerIntendedFollowup>();
            error = null;
            object raw;
            if (!data.TryGetValue("intended_followups", out raw) || raw == null)
            {
                return true;
            }
            List<object> list = raw as List<object>;
            if (list == null)
            {
                error = "invalid_intended_followups";
                return false;
            }
            foreach (object item in list)
            {
                Dictionary<string, object> bag = item as Dictionary<string, object>;
                if (bag == null)
                {
                    error = "invalid_intended_followups";
                    return false;
                }
                string family = GetOptionalString(bag, "action_family");
                if (family != "effect_activation"
                    && family != "attack"
                    && family != "move_phase")
                {
                    error = "invalid_intended_followup_family";
                    return false;
                }
                int cardId = 0;
                object rawCardId;
                if (bag.TryGetValue("card_id", out rawCardId) && rawCardId != null)
                {
                    try
                    {
                        cardId = Convert.ToInt32(rawCardId);
                    }
                    catch
                    {
                        error = "invalid_intended_followup_card_id";
                        return false;
                    }
                    if (cardId < 0)
                    {
                        error = "invalid_intended_followup_card_id";
                        return false;
                    }
                }
                value.Add(new LlmBrokerIntendedFollowup()
                {
                    ActionFamily = family,
                    CardId = cardId,
                    CardName = GetOptionalString(bag, "card_name"),
                    Description = GetOptionalString(bag, "description"),
                });
            }
            return true;
        }

        /// <summary>
        /// history_event_ids_used must be present. Empty list is valid. Missing key is not.
        /// Duplicate/zero/negative rejected here so parse and validate share error families.
        /// </summary>
        static bool TryGetRequiredHistoryEventIdsUsed(
            Dictionary<string, object> data,
            out List<long> value,
            out string error)
        {
            value = null;
            error = null;
            object raw;
            if (!data.TryGetValue("history_event_ids_used", out raw) || raw == null)
            {
                error = "missing_history_event_ids_used";
                return false;
            }

            List<object> list = raw as List<object>;
            if (list == null)
            {
                error = "invalid_history_event_ids_used";
                return false;
            }

            value = new List<long>();
            HashSet<long> seen = new HashSet<long>();
            foreach (object item in list)
            {
                if (item == null || item is bool)
                {
                    error = "invalid_history_event_id";
                    return false;
                }

                long eventId;
                try
                {
                    // Reject non-integral floats (1.5) while accepting integral MiniJSON numbers.
                    if (item is double)
                    {
                        double asDouble = (double)item;
                        if (Math.Abs(asDouble - Math.Round(asDouble)) > double.Epsilon)
                        {
                            error = "invalid_history_event_id";
                            return false;
                        }
                        eventId = Convert.ToInt64(asDouble);
                    }
                    else
                    {
                        eventId = Convert.ToInt64(item);
                    }
                }
                catch
                {
                    error = "invalid_history_event_id";
                    return false;
                }

                if (eventId <= 0)
                {
                    error = "invalid_history_event_id";
                    return false;
                }
                if (!seen.Add(eventId))
                {
                    error = "duplicate_history_event_id";
                    return false;
                }
                value.Add(eventId);
            }

            return true;
        }
    }
}

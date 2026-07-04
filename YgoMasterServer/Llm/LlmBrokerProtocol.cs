using System;
using System.Collections.Generic;

namespace YgoMaster
{
    class LlmBrokerDecisionResponse
    {
        public ulong RunEffectSeq { get; set; }
        public int ActionId { get; set; }
        public string Reason { get; set; }
        public double? Confidence { get; set; }
        public string Plan { get; set; }
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
            return new LlmBrokerValidationResult()
            {
                IsValid = false,
                Error = error,
            };
        }
    }

    static class LlmBrokerProtocol
    {
        public const int SchemaVersion = 3;

        public static string SerializeDecisionRequest(DecisionSnapshot snapshot)
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
                { "public_state", LlmDecisionLogSerializer.SerializePublicState(snapshot.PublicState) },
                { "legal_actions", LlmDecisionLogSerializer.SerializeLegalActions(snapshot.LegalActions) },
            };
            return MiniJSON.Json.Serialize(data);
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

            response = new LlmBrokerDecisionResponse()
            {
                RunEffectSeq = runEffectSeq,
                ActionId = actionId,
                Reason = GetOptionalString(data, "reason"),
                Confidence = hasConfidence ? (double?)confidence : null,
                Plan = GetOptionalString(data, "plan"),
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

            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action.ActionId == response.ActionId)
                {
                    if (expectedAction != null && !IsSameAction(action, expectedAction))
                    {
                        return LlmBrokerValidationResult.Invalid("action_changed");
                    }
                    return LlmBrokerValidationResult.Valid(action);
                }
            }

            return LlmBrokerValidationResult.Invalid("unknown_action_id");
        }

        static bool IsSameAction(LegalAction current, LegalAction expected)
        {
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
                    current.DialogTextId == expected.DialogTextId;
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

            return current.Player == expected.Player &&
                current.Position == expected.Position &&
                current.Index == expected.Index &&
                current.Command == expected.Command &&
                current.CardUniqueId == expected.CardUniqueId;
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
    }
}

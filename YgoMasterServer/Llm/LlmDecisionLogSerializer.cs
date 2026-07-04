using System.Collections.Generic;

namespace YgoMaster
{
    static class LlmDecisionLogSerializer
    {
        public static string SerializeDecisionWindow(DecisionSnapshot snapshot)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "decision_window" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
                { "run_effect_seq", (long)snapshot.RunEffectSeq },
                { "view_type", snapshot.ViewType.ToString() },
                { "view_type_id", (int)snapshot.ViewType },
                { "acting_player", snapshot.ActingPlayer },
                { "controlled_player", snapshot.ControlledPlayer },
                { "turn", snapshot.Turn },
                { "turn_player", snapshot.TurnPlayer },
                { "current_phase", snapshot.CurrentPhase },
                { "current_step", snapshot.CurrentStep },
                { "public_state", SerializePublicState(snapshot.PublicState) },
                { "legal_actions", SerializeLegalActions(snapshot.LegalActions) },
            };
            return MiniJSON.Json.Serialize(data);
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
                { "action", SerializeLegalAction(action) },
            };
            if (action != null)
            {
                data["action_type"] = GetKindName(action.Kind);
            }
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
            foreach (PublicKnownCard card in knownCards)
            {
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
            };
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
            return actionData;
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
                default:
                    return kind.ToString();
            }
        }
    }
}

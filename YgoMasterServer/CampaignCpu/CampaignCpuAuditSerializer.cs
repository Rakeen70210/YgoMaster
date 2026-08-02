using System;
using System.Collections.Generic;
using System.Globalization;

namespace YgoMaster
{
    /// <summary>
    /// Best-effort JSONL line builder for CampaignCpu audit events. Never throws to callers
    /// when used via TrySerialize.
    /// </summary>
    static class CampaignCpuAuditSerializer
    {
        /// <summary>
        /// Common ownership/generation contract for lifecycle rows.
        /// Event-specific fields may be added by the controller before serialization.
        /// </summary>
        public static Dictionary<string, object> CreateLifecycleContext(
            int myId,
            int ownedSeat,
            long duelGeneration)
        {
            return new Dictionary<string, object>
            {
                { "my_id", myId },
                { "owned_seat", ownedSeat },
                { "duel_generation", duelGeneration },
            };
        }

        public static string Serialize(string eventName, Dictionary<string, object> fields)
        {
            var map = new Dictionary<string, object>();
            map["event"] = eventName ?? "unknown";
            map["ts"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            if (fields != null)
            {
                foreach (KeyValuePair<string, object> kv in fields)
                {
                    if (kv.Key != null)
                    {
                        map[kv.Key] = kv.Value;
                    }
                }
            }
            return MiniJSON.Json.Serialize(map);
        }

        public static bool TrySerialize(
            string eventName,
            Dictionary<string, object> fields,
            out string line)
        {
            line = null;
            try
            {
                line = Serialize(eventName, fields);
                return !string.IsNullOrEmpty(line);
            }
            catch
            {
                line = null;
                return false;
            }
        }

        public static string SerializeDecision(
            ulong viewSeq,
            CampaignCpuDecision decision,
            CampaignCpuObservation observation)
        {
            return SerializeDecision(viewSeq, decision, observation, shadowOnly: false);
        }

        /// <summary>
        /// Decision audit for PR4b commits and PR3 shadow capture.
        /// Includes full legal menu + hand context so offline goldens can be mined from jsonl.
        /// </summary>
        public static string SerializeDecision(
            ulong viewSeq,
            CampaignCpuDecision decision,
            CampaignCpuObservation observation,
            bool shadowOnly)
        {
            var fields = new Dictionary<string, object>
            {
                { "view_seq", viewSeq },
                { "route", decision != null ? decision.Route.ToString() : "null" },
                { "reason", decision != null ? decision.Reason : null },
                { "rule_id", decision != null ? decision.RuleId : null },
                { "score", decision != null ? decision.Score : 0 },
                { "matched", decision != null && decision.Matched },
                { "shadow_only", shadowOnly },
            };
            if (observation != null)
            {
                fields["chapter_id"] = observation.ChapterId;
                fields["window_class"] = observation.WindowClass;
                fields["acting_player"] = observation.ActingPlayer;
                fields["owned_seat"] = observation.OwnedSeat;
                // Production S10 ownership + multi-duel segmentation (PR4b handoff).
                fields["my_id"] = observation.MyId;
                fields["duel_generation"] = observation.DuelGeneration;
                fields["turn"] = observation.Turn;
                fields["turn_player"] = observation.TurnPlayer;
                fields["phase"] = observation.Phase;
                fields["self_lp"] = observation.SelfLp;
                fields["opp_lp"] = observation.OppLp;
                fields["is_main_phase_wait_input"] = observation.IsMainPhaseWaitInput;
                fields["is_multi_select"] = observation.IsMultiSelectList;
                fields["legal_count"] = observation.LegalActions != null
                    ? observation.LegalActions.Count
                    : 0;
                fields["legal_fingerprint"] = CampaignCpuObservation.FingerprintLegalActions(
                    observation.LegalActions);
                fields["self_hand_card_ids"] = CopyIntList(observation.SelfHandCardIds);
                fields["self_field_face_up_card_ids"] = CopyIntList(observation.SelfFieldFaceUpCardIds);
                fields["opp_field_face_up_card_ids"] = CopyIntList(observation.OppFieldFaceUpCardIds);
                fields["legal_actions"] = SerializeLegalActions(observation.LegalActions);
                fields["tactical_monsters"] =
                    SerializeTacticalMonsters(observation.TacticalMonsters);
            }
            if (decision != null && decision.Action != null)
            {
                fields["action_id"] = decision.Action.ActionId;
                fields["action_identity"] = decision.Action.CanonicalIdentity;
                fields["command"] = decision.Action.Command.ToString();
                fields["card_id"] = decision.Action.CardId;
                fields["chosen"] = SerializeOneLegalAction(decision.Action);
            }
            if (decision != null)
            {
                fields["tactical_filter_reason"] =
                    decision.TacticalFilterReason;
                fields["tactical_filtered_action_identities"] =
                    CopyStringList(decision.TacticalFilteredActionIdentities);
                fields["legal_after_tactical_fingerprint"] =
                    decision.LegalAfterTacticalFingerprint;
                fields["replaced_action_identity"] =
                    decision.ReplacedActionIdentity;
            }
            return Serialize("campaign_cpu_decision", fields);
        }

        static List<object> CopyIntList(IList<int> source)
        {
            var list = new List<object>();
            if (source == null)
            {
                return list;
            }
            for (int i = 0; i < source.Count; i++)
            {
                list.Add(source[i]);
            }
            return list;
        }

        static List<object> SerializeLegalActions(IList<CampaignCpuLegalAction> actions)
        {
            var list = new List<object>();
            if (actions == null)
            {
                return list;
            }
            for (int i = 0; i < actions.Count; i++)
            {
                CampaignCpuLegalAction a = actions[i];
                if (a == null)
                {
                    continue;
                }
                list.Add(SerializeOneLegalAction(a));
            }
            return list;
        }

        static List<object> CopyStringList(IList<string> source)
        {
            var list = new List<object>();
            if (source == null)
            {
                return list;
            }
            for (int i = 0; i < source.Count; i++)
            {
                list.Add(source[i] ?? string.Empty);
            }
            return list;
        }

        static List<object> SerializeTacticalMonsters(
            IList<CampaignCpuMonsterTacticalState> monsters)
        {
            var list = new List<object>();
            if (monsters == null)
            {
                return list;
            }
            for (int i = 0; i < monsters.Count; i++)
            {
                CampaignCpuMonsterTacticalState m = monsters[i];
                if (m == null)
                {
                    continue;
                }
                list.Add(new Dictionary<string, object>
                {
                    { "player", m.Player },
                    { "position", m.Position },
                    { "index", m.Index },
                    { "unique_id", m.UniqueId },
                    { "card_id", m.CardId },
                    { "face_known", m.FaceKnown },
                    { "face_up", m.FaceUp },
                    { "turn_known", m.TurnKnown },
                    { "turn_raw", m.TurnKnown ? (object)m.TurnRaw : null },
                    { "is_attack", m.TurnKnown ? (object)m.IsAttack : null },
                    { "is_defense", m.TurnKnown ? (object)m.IsDefense : null },
                    { "atk", m.HasAtk ? (object)m.Atk : null },
                    { "def", m.HasDef ? (object)m.Def : null },
                });
            }
            return list;
        }

        static Dictionary<string, object> SerializeOneLegalAction(CampaignCpuLegalAction a)
        {
            return new Dictionary<string, object>
            {
                { "action_id", a.ActionId },
                { "identity", a.CanonicalIdentity },
                { "kind", a.Kind.ToString() },
                { "command", a.Command.ToString() },
                { "phase", a.Phase.ToString() },
                { "card_id", a.CardId },
                { "position", a.Position },
                { "index", a.Index },
                { "dialog_result", a.DialogResult },
                { "cancel_decide", a.CancelDecide },
                { "label", a.Label ?? string.Empty },
                { "is_mechanical", a.IsMechanical },
                { "target_scope", a.TargetScope ?? string.Empty },
                { "player", a.Player },
                { "basic_level", a.BasicLevelKnown ? (object)a.BasicLevel : null },
                { "basic_atk", a.BasicAtkKnown ? (object)a.BasicAtk : null },
                { "basic_def", a.BasicDefKnown ? (object)a.BasicDef : null },
            };
        }
    }
}

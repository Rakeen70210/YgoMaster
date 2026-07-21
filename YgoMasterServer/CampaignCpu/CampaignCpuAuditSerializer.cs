using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Best-effort JSONL line builder for CampaignCpu audit events. Never throws to callers
    /// when used via TrySerialize.
    /// </summary>
    static class CampaignCpuAuditSerializer
    {
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
            var fields = new Dictionary<string, object>
            {
                { "view_seq", viewSeq },
                { "route", decision != null ? decision.Route.ToString() : "null" },
                { "reason", decision != null ? decision.Reason : null },
                { "rule_id", decision != null ? decision.RuleId : null },
                { "score", decision != null ? decision.Score : 0 },
                { "matched", decision != null && decision.Matched },
            };
            if (observation != null)
            {
                fields["chapter_id"] = observation.ChapterId;
                fields["window_class"] = observation.WindowClass;
                fields["acting_player"] = observation.ActingPlayer;
                fields["owned_seat"] = observation.OwnedSeat;
                fields["turn"] = observation.Turn;
                fields["phase"] = observation.Phase;
                fields["legal_count"] = observation.LegalActions != null
                    ? observation.LegalActions.Count
                    : 0;
            }
            if (decision != null && decision.Action != null)
            {
                fields["action_id"] = decision.Action.ActionId;
                fields["action_identity"] = decision.Action.CanonicalIdentity;
                fields["command"] = decision.Action.Command.ToString();
                fields["card_id"] = decision.Action.CardId;
            }
            return Serialize("campaign_cpu_decision", fields);
        }
    }
}

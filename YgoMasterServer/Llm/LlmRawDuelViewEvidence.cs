using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Compact raw DuelView audit evidence (Slice 3). Audit-only — never enters broker history.
    /// RawEvidenceId is PvP-owned monotonic; identical consecutive views get distinct ids.
    /// </summary>
    class LlmRawDuelViewEvidence
    {
        public ulong RawEvidenceId;
        public ulong RunEffectSeq;
        public int ViewTypeId;
        public string ViewTypeName;
        public int Param1;
        public int Param2;
        public int Param3;
        public int Turn;
        public int Phase;
        public string SourceSignature;

        public static LlmRawDuelViewEvidence Create(
            ulong rawEvidenceId,
            ulong runEffectSeq,
            DuelViewType viewType,
            int param1,
            int param2,
            int param3,
            int turn,
            int phase)
        {
            return new LlmRawDuelViewEvidence()
            {
                RawEvidenceId = rawEvidenceId,
                RunEffectSeq = runEffectSeq,
                ViewTypeId = (int)viewType,
                ViewTypeName = viewType.ToString(),
                Param1 = param1,
                Param2 = param2,
                Param3 = param3,
                Turn = turn,
                Phase = phase,
                SourceSignature = BuildSourceSignature(rawEvidenceId, runEffectSeq, viewType, param1, param2, param3),
            };
        }

        public static string BuildSourceSignature(
            ulong rawEvidenceId,
            ulong runEffectSeq,
            DuelViewType viewType,
            int param1,
            int param2,
            int param3)
        {
            return "raw_view:id:" + rawEvidenceId + ":seq:" + runEffectSeq + ":" + (int)viewType + ":" +
                param1 + ":" + param2 + ":" + param3;
        }

        public Dictionary<string, object> ToAuditDictionary()
        {
            return new Dictionary<string, object>()
            {
                { "kind", "llm_raw_duel_view" },
                { "raw_evidence_id", (long)RawEvidenceId },
                { "run_effect_seq", (long)RunEffectSeq },
                { "view_type", ViewTypeName },
                { "view_type_id", ViewTypeId },
                { "param1", Param1 },
                { "param2", Param2 },
                { "param3", Param3 },
                { "turn", Turn },
                { "phase", Phase },
                { "source_signature", SourceSignature },
                { "audit_only", true },
                { "broker_history", false },
            };
        }
    }

    /// <summary>
    /// Identity-free face probe for live DLL face validation. Never includes card id/name/unique id.
    /// </summary>
    class LlmFaceProbeEvidence
    {
        public int Player;
        public int Position;
        public int Index;
        public int RawFace;
        public ulong RunEffectSeq;
        public ulong ProbeId;

        public Dictionary<string, object> ToAuditDictionary()
        {
            return new Dictionary<string, object>()
            {
                { "kind", "llm_face_probe" },
                { "probe_id", (long)ProbeId },
                { "run_effect_seq", (long)RunEffectSeq },
                { "player", Player },
                { "position", Position },
                { "index", Index },
                { "raw_face", RawFace },
                { "audit_only", true },
                { "broker_history", false },
            };
        }

        public static LlmFaceProbeEvidence Create(
            ulong probeId,
            ulong runEffectSeq,
            int player,
            int position,
            int index,
            int rawFace)
        {
            return new LlmFaceProbeEvidence()
            {
                ProbeId = probeId,
                RunEffectSeq = runEffectSeq,
                Player = player,
                Position = position,
                Index = index,
                RawFace = rawFace,
            };
        }
    }

    /// <summary>
    /// Identity-free field face probes for audit-only live validation (never broker history).
    /// </summary>
    static class LlmFaceProbeCapture
    {
        public static List<LlmFaceProbeEvidence> LastProbes = new List<LlmFaceProbeEvidence>();

        public delegate int CardNumQuery(int player, int position);
        public delegate int FaceQuery(int player, int position, int index);

        public static List<LlmFaceProbeEvidence> CaptureFieldProbes(
            ulong runEffectSeq,
            ref ulong nextProbeId,
            CardNumQuery cardNum,
            FaceQuery faceQuery)
        {
            List<LlmFaceProbeEvidence> probes = new List<LlmFaceProbeEvidence>();
            if (cardNum == null || faceQuery == null)
            {
                return probes;
            }
            for (int player = 0; player < 2; player++)
            {
                foreach (int pos in LlmAbsolutePublicVisibility.FaceProbeFieldPositions)
                {
                    int num;
                    try
                    {
                        num = cardNum(player, pos);
                    }
                    catch
                    {
                        continue;
                    }
                    if (num < 0)
                    {
                        num = 0;
                    }
                    for (int index = 0; index < num && index < 16; index++)
                    {
                        int rawFace;
                        try
                        {
                            rawFace = faceQuery(player, pos, index);
                        }
                        catch
                        {
                            continue;
                        }
                        nextProbeId++;
                        probes.Add(LlmFaceProbeEvidence.Create(
                            nextProbeId,
                            runEffectSeq,
                            player,
                            pos,
                            index,
                            rawFace));
                    }
                }
            }
            return probes;
        }
    }

    static class LlmLiveViewEvidenceReport
    {
        public static string FormatMappingRow(LlmRawDuelViewEvidence evidence, string uiObservation)
        {
            if (evidence == null)
            {
                return string.Empty;
            }
            return "live_view_map\t" +
                evidence.ViewTypeName + "\t" +
                evidence.ViewTypeId + "\t" +
                evidence.Param1 + "\t" +
                evidence.Param2 + "\t" +
                evidence.Param3 + "\t" +
                evidence.RunEffectSeq + "\t" +
                evidence.RawEvidenceId + "\t" +
                (uiObservation ?? string.Empty);
        }

        public static Dictionary<string, object> BuildAnalyzerEnvelope(
            IEnumerable<LlmRawDuelViewEvidence> rawEvents,
            IEnumerable<LlmPublicDuelEvent> outcomeEvents)
        {
            return BuildAnalyzerEnvelope(rawEvents, outcomeEvents, null);
        }

        public static Dictionary<string, object> BuildAnalyzerEnvelope(
            IEnumerable<LlmRawDuelViewEvidence> rawEvents,
            IEnumerable<LlmPublicDuelEvent> outcomeEvents,
            IEnumerable<LlmFaceProbeEvidence> faceProbes)
        {
            List<object> raw = new List<object>();
            if (rawEvents != null)
            {
                foreach (LlmRawDuelViewEvidence evidence in rawEvents)
                {
                    if (evidence != null)
                    {
                        raw.Add(evidence.ToAuditDictionary());
                    }
                }
            }
            List<object> outcomes = new List<object>();
            if (outcomeEvents != null)
            {
                foreach (LlmPublicDuelEvent evt in outcomeEvents)
                {
                    outcomes.Add(LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(evt));
                }
            }
            List<object> probes = new List<object>();
            if (faceProbes != null)
            {
                foreach (LlmFaceProbeEvidence probe in faceProbes)
                {
                    if (probe != null)
                    {
                        probes.Add(probe.ToAuditDictionary());
                    }
                }
            }
            return new Dictionary<string, object>()
            {
                { "kind", "llm_live_view_evidence_report" },
                { "raw_views", raw },
                { "normalized_outcomes", outcomes },
                { "face_probes", probes },
                { "note", "Map raw view_type/params and face probes (player/position/index/raw_face) to visible UI after paired deployment; do not promote unproven families." },
            };
        }
    }
}

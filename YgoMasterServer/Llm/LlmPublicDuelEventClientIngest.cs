namespace YgoMaster
{
    /// <summary>
    /// Client ingestion helper for authoritative public events. Separates append status
    /// from audit emission so duplicate/reordered deliveries never double-log.
    /// Optional name enrichment runs before tracker append (production wires Ydk catalog).
    /// </summary>
    static class LlmPublicDuelEventClientIngest
    {
        /// <summary>
        /// Production clients may assign a catalog-backed resolver (e.g. YdkLlmCardCatalog).
        /// Harness/tests leave null or pass an explicit resolver to TryIngestForAudit.
        /// </summary>
        public static LlmPublicHistoryCardNameEnrichment.ResolveName DefaultNameResolver
        {
            get;
            set;
        }

        /// <summary>
        /// Appends the event if new. Returns true only when the event was newly appended
        /// and should be written as a single llm_public_duel_event audit line.
        /// </summary>
        public static bool TryIngestForAudit(
            LlmDuelHistoryTracker tracker,
            LlmPublicDuelEvent publicEvent,
            out string error,
            out string auditJson)
        {
            return TryIngestForAudit(
                tracker,
                publicEvent,
                DefaultNameResolver,
                out error,
                out auditJson);
        }

        /// <summary>
        /// Testable ingest path with an explicit name resolver.
        /// </summary>
        public static bool TryIngestForAudit(
            LlmDuelHistoryTracker tracker,
            LlmPublicDuelEvent publicEvent,
            LlmPublicHistoryCardNameEnrichment.ResolveName nameResolver,
            out string error,
            out string auditJson)
        {
            error = null;
            auditJson = null;
            if (tracker == null)
            {
                error = "null_tracker";
                return false;
            }

            LlmPublicHistoryCardNameEnrichment.TryEnrich(publicEvent, nameResolver);

            LlmDuelHistoryAppendResult result = tracker.TryAppendPublicEvent(publicEvent);
            error = result.Error;
            if (!result.NewlyAppended)
            {
                return false;
            }

            // publicEvent.DuelGeneration is stamped by the tracker on accepted append.
            // Serialize that accepted object so live audit lines match projection generation.
            auditJson = LlmDecisionLogSerializer.SerializePublicDuelEvent(publicEvent);
            return true;
        }

        /// <summary>
        /// Accepts a face probe for audit when new (probe_id dedupe). Never appends history.
        /// Returns true only when a single llm_face_probe audit line should be written.
        /// </summary>
        public static bool TryIngestFaceProbeForAudit(
            LlmDuelHistoryTracker tracker,
            LlmFaceProbeEvidence probe,
            out string auditJson)
        {
            auditJson = null;
            if (tracker == null || probe == null)
            {
                return false;
            }
            if (!tracker.TryAcceptFaceProbe(probe))
            {
                return false;
            }
            auditJson = MiniJSON.Json.Serialize(probe.ToAuditDictionary());
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
// Convert used by shuffle invalidation path

namespace YgoMaster
{
    /// <summary>
    /// Client-side authoritative public history tracker. Full uncompacted history is always
    /// retained; CreateRequestProjection builds the budget-limited broker-facing payload.
    /// </summary>
    class LlmDuelHistoryTracker
    {
        readonly LlmDuelHistoryTrackerOptions options;
        readonly List<LlmPublicDuelEvent> events = new List<LlmPublicDuelEvent>();
        readonly List<Dictionary<string, object>> revealedCardContext = new List<Dictionary<string, object>>();
        readonly List<string> gapWarnings = new List<string>();
        readonly HashSet<int> revealedCardIds = new HashSet<int>();
        readonly HashSet<string> sourceSignatures = new HashSet<string>();
        readonly HashSet<ulong> rawEvidenceIds = new HashSet<ulong>();
        readonly HashSet<ulong> faceProbeIds = new HashSet<ulong>();
        int duelGeneration;
        int historyVersion = 1;
        ulong lastEventId;

        public LlmDuelHistoryTracker(LlmDuelHistoryTrackerOptions options)
        {
            this.options = options ?? new LlmDuelHistoryTrackerOptions();
        }

        public int DuelGeneration
        {
            get { return duelGeneration; }
        }

        public void ResetForNewDuel(int newDuelGeneration)
        {
            duelGeneration = newDuelGeneration;
            events.Clear();
            revealedCardContext.Clear();
            revealedCardIds.Clear();
            sourceSignatures.Clear();
            rawEvidenceIds.Clear();
            faceProbeIds.Clear();
            gapWarnings.Clear();
            lastEventId = 0;
            historyVersion = 1;
        }

        public LlmDuelHistoryAppendResult TryAppendPublicEvent(LlmPublicDuelEvent evt)
        {
            if (evt == null)
            {
                return LlmDuelHistoryAppendResult.Rejected("null_event");
            }

            if (evt.EventId == 0)
            {
                return LlmDuelHistoryAppendResult.Rejected("invalid_event_id");
            }

            if (!string.IsNullOrEmpty(evt.SourceSignature) &&
                sourceSignatures.Contains(evt.SourceSignature))
            {
                return LlmDuelHistoryAppendResult.IgnoredDelivery("duplicate_source_signature");
            }

            if (ContainsEventId(evt.EventId))
            {
                return LlmDuelHistoryAppendResult.IgnoredDelivery("duplicate_event_id");
            }

            if (evt.EventId <= lastEventId)
            {
                return LlmDuelHistoryAppendResult.IgnoredDelivery("reordered_event_id");
            }

            if (lastEventId > 0 && evt.EventId > lastEventId + 1)
            {
                gapWarnings.Add(
                    "gap_event_ids:" + (lastEventId + 1) + "-" + (evt.EventId - 1));
            }

            LlmPublicDuelEvent stored = LlmDuelHistoryState.CloneEvent(evt);
            // Stamp the tracker's generation onto both the stored clone and the caller's event.
            // Live TryIngestForAudit serializes the original publicEvent after append; without
            // stamping the input object, auditJson would still emit duel_generation=0.
            stored.DuelGeneration = duelGeneration;
            evt.DuelGeneration = duelGeneration;
            events.Add(stored);
            lastEventId = evt.EventId;
            if (!string.IsNullOrEmpty(stored.SourceSignature))
            {
                sourceSignatures.Add(stored.SourceSignature);
            }
            if (stored.Kind == LlmPublicDuelEventKind.HandShuffled ||
                stored.Kind == LlmPublicDuelEventKind.DeckShuffled)
            {
                // Shuffle invalidates hidden-location associations without exposing indices.
                InvalidateHiddenLocationAssociations();
            }
            MaybeRecordRevealedCard(stored);
            return LlmDuelHistoryAppendResult.Appended();
        }

        /// <summary>
        /// Dedupe raw view evidence by PvP-owned RawEvidenceId (network redelivery only).
        /// Identical consecutive views with distinct ids are both accepted/logged.
        /// Raw evidence never enters the public event history list.
        /// </summary>
        public bool TryAcceptRawViewEvidence(LlmRawDuelViewEvidence evidence)
        {
            if (evidence == null || evidence.RawEvidenceId == 0)
            {
                return false;
            }
            if (rawEvidenceIds.Contains(evidence.RawEvidenceId))
            {
                return false;
            }
            rawEvidenceIds.Add(evidence.RawEvidenceId);
            return true;
        }

        /// <summary>
        /// Dedupe identity-free face probes by PvP-owned ProbeId (network redelivery only).
        /// Face probes never enter the public event history / request projection.
        /// </summary>
        public bool TryAcceptFaceProbe(LlmFaceProbeEvidence probe)
        {
            if (probe == null || probe.ProbeId == 0)
            {
                return false;
            }
            if (faceProbeIds.Contains(probe.ProbeId))
            {
                return false;
            }
            faceProbeIds.Add(probe.ProbeId);
            return true;
        }

        void InvalidateHiddenLocationAssociations()
        {
            // Clear revealed cards that are only meaningful as hidden-zone associations.
            // Public field/grave identities remain; hand/deck associations are dropped.
            List<Dictionary<string, object>> kept = new List<Dictionary<string, object>>();
            revealedCardIds.Clear();
            foreach (Dictionary<string, object> card in revealedCardContext)
            {
                // Without zone metadata on revealed cards, conservatively keep all public
                // identities that already passed absolute-public policy at append time.
                // Hidden associations are not stored as indices; nothing index-based remains.
                if (card != null && card.ContainsKey("card_id"))
                {
                    int id = Convert.ToInt32(card["card_id"]);
                    revealedCardIds.Add(id);
                    kept.Add(card);
                }
            }
            revealedCardContext.Clear();
            revealedCardContext.AddRange(kept);
        }

        /// <summary>
        /// Full uncompacted history for audit / tracker inspection only.
        /// Not attached to DecisionSnapshot (broker payload uses CreateRequestProjection).
        /// </summary>
        public LlmDuelHistoryState CreateSnapshot()
        {
            ulong firstDetailed = events.Count > 0 ? events[0].EventId : 1UL;
            return new LlmDuelHistoryState(
                historyVersion,
                lastEventId,
                firstDetailed,
                false,
                events,
                new List<Dictionary<string, object>>(),
                revealedCardContext,
                gapWarnings,
                "ok",
                null,
                duelGeneration);
        }

        /// <summary>
        /// Budget-limited request projection for DecisionSnapshot.DuelHistory / future broker JSON.
        /// Never mutates full in-memory history. Protects current + previous turn.
        /// </summary>
        public LlmDuelHistoryState CreateRequestProjection(int? controlledPlayer = null)
        {
            List<LlmPublicDuelEvent> detailed = new List<LlmPublicDuelEvent>();
            foreach (LlmPublicDuelEvent evt in events)
            {
                detailed.Add(LlmDuelHistoryState.CloneEvent(evt));
            }

            List<Dictionary<string, object>> summaries = new List<Dictionary<string, object>>();
            int currentTurn = detailed.Count > 0 ? detailed[detailed.Count - 1].Turn : 0;
            int previousTurn = currentTurn > 0 ? currentTurn - 1 : -1;
            int? opponent = null;
            if (controlledPlayer.HasValue)
            {
                opponent = controlledPlayer.Value == 0 ? 1 : 0;
            }

            while (NeedsCompaction(detailed, summaries) &&
                TryCompactOldestEligibleTurn(detailed, summaries, currentTurn, previousTurn, opponent))
            {
            }

            // Sort summaries by turn then first_event_id for deterministic serialization.
            summaries.Sort(CompareSummaries);

            ulong firstDetailed = detailed.Count > 0 ? detailed[0].EventId : (lastEventId + 1);
            List<Dictionary<string, object>> projectionCards = BuildDetailedOnlyRevealedContext(detailed);

            string budgetStatus = "ok";
            string budgetReason = null;
            if (NeedsCompaction(detailed, summaries))
            {
                // Cannot compact further without dropping protected turns.
                budgetStatus = "over_budget_protected_detail";
                budgetReason =
                    "protected_current_and_previous_turn_detail_exceeds_budget;" +
                    "detailed_events=" + detailed.Count +
                    ";summaries=" + summaries.Count +
                    ";max_detailed_events=" + (options.MaxDetailedEvents > 0 ? options.MaxDetailedEvents : 128) +
                    ";max_serialized_bytes=" + (options.MaxSerializedBytes > 0 ? options.MaxSerializedBytes : 32 * 1024);
            }

            return new LlmDuelHistoryState(
                historyVersion,
                lastEventId,
                firstDetailed,
                summaries.Count > 0,
                detailed,
                summaries,
                projectionCards,
                gapWarnings,
                budgetStatus,
                budgetReason,
                duelGeneration);
        }

        public string SerializeRequestProjection()
        {
            return SerializeRequestProjection(null);
        }

        public string SerializeRequestProjection(int? controlledPlayer)
        {
            return SerializeProjectionJson(CreateRequestProjection(controlledPlayer));
        }

        public static string SerializeProjectionJson(LlmDuelHistoryState projection)
        {
            Dictionary<string, object> root = new Dictionary<string, object>()
            {
                { "duel_history", SerializeDuelHistoryObject(projection) },
            };
            return MiniJSON.Json.Serialize(root);
        }

        /// <summary>
        /// Broker/audit-facing duel_history object. Uses absolute-public event projection only.
        /// </summary>
        public static Dictionary<string, object> SerializeDuelHistoryObject(LlmDuelHistoryState projection)
        {
            if (projection == null)
            {
                projection = new LlmDuelHistoryState();
            }

            List<object> eventObjects = new List<object>();
            foreach (LlmPublicDuelEvent evt in projection.Events)
            {
                eventObjects.Add(LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(evt));
            }

            List<object> summaries = new List<object>();
            foreach (Dictionary<string, object> summary in projection.PriorTurnSummaries)
            {
                summaries.Add(LlmDuelHistoryState.DeepCloneDict(summary));
            }

            List<object> cards = new List<object>();
            foreach (Dictionary<string, object> card in projection.RevealedCardContext)
            {
                cards.Add(LlmDuelHistoryState.DeepCloneDict(card));
            }

            return new Dictionary<string, object>()
            {
                { "history_version", projection.HistoryVersion },
                { "duel_generation", projection.DuelGeneration },
                { "last_event_id", (long)projection.LastEventId },
                { "first_detailed_event_id", (long)projection.FirstDetailedEventId },
                { "history_compacted", projection.HistoryCompacted },
                { "budget_status", projection.BudgetStatus ?? "ok" },
                { "budget_reason", projection.BudgetReason },
                { "events", eventObjects },
                { "prior_turn_summaries", summaries },
                { "revealed_card_context", cards },
                { "gap_warnings", new List<string>(projection.GapWarnings) },
            };
        }

        /// <summary>
        /// Canonical empty duel_history for schema-v3 → v4 migration (deterministic).
        /// </summary>
        public static Dictionary<string, object> CreateEmptyDuelHistoryObject()
        {
            return SerializeDuelHistoryObject(new LlmDuelHistoryState());
        }

        bool NeedsCompaction(
            List<LlmPublicDuelEvent> detailed,
            List<Dictionary<string, object>> summaries)
        {
            int maxEvents = options.MaxDetailedEvents > 0 ? options.MaxDetailedEvents : 128;
            if (detailed.Count > maxEvents)
            {
                return true;
            }

            int maxBytes = options.MaxSerializedBytes > 0 ? options.MaxSerializedBytes : 32 * 1024;
            return MeasureProjectionBytes(detailed, summaries) > maxBytes;
        }

        int MeasureProjectionBytes(
            List<LlmPublicDuelEvent> detailed,
            List<Dictionary<string, object>> summaries)
        {
            LlmDuelHistoryState probe = new LlmDuelHistoryState(
                historyVersion,
                lastEventId,
                detailed.Count > 0 ? detailed[0].EventId : 1UL,
                summaries.Count > 0,
                detailed,
                summaries,
                BuildDetailedOnlyRevealedContext(detailed),
                gapWarnings,
                "ok",
                null,
                duelGeneration);
            string json = SerializeProjectionJson(probe);
            return Encoding.UTF8.GetByteCount(json);
        }

        bool TryCompactOldestEligibleTurn(
            List<LlmPublicDuelEvent> detailed,
            List<Dictionary<string, object>> summaries,
            int currentTurn,
            int previousTurn,
            int? opponentPlayer)
        {
            if (detailed.Count == 0)
            {
                return false;
            }

            int oldestTurn = int.MaxValue;
            foreach (LlmPublicDuelEvent evt in detailed)
            {
                if (IsProtectedEvent(evt, currentTurn, previousTurn))
                {
                    continue;
                }
                if (evt.Turn < oldestTurn)
                {
                    oldestTurn = evt.Turn;
                }
            }

            if (oldestTurn == int.MaxValue)
            {
                return false;
            }

            List<LlmPublicDuelEvent> turnEvents = new List<LlmPublicDuelEvent>();
            foreach (LlmPublicDuelEvent evt in detailed)
            {
                if (evt.Turn == oldestTurn)
                {
                    turnEvents.Add(evt);
                }
            }
            if (turnEvents.Count == 0)
            {
                return false;
            }

            summaries.Add(BuildTurnSummary(turnEvents));
            detailed.RemoveAll(evt => evt.Turn == oldestTurn);
            return true;
        }

        static bool IsProtectedEvent(LlmPublicDuelEvent evt, int currentTurn, int previousTurn)
        {
            if (evt.Turn == currentTurn)
            {
                return true;
            }
            if (previousTurn >= 0 && evt.Turn == previousTurn)
            {
                return true;
            }
            return false;
        }

        static int CompareSummaries(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            int turnA = a != null && a.ContainsKey("turn") ? Convert.ToInt32(a["turn"]) : 0;
            int turnB = b != null && b.ContainsKey("turn") ? Convert.ToInt32(b["turn"]) : 0;
            int cmp = turnA.CompareTo(turnB);
            if (cmp != 0)
            {
                return cmp;
            }
            long firstA = a != null && a.ContainsKey("first_event_id") ? Convert.ToInt64(a["first_event_id"]) : 0;
            long firstB = b != null && b.ContainsKey("first_event_id") ? Convert.ToInt64(b["first_event_id"]) : 0;
            return firstA.CompareTo(firstB);
        }

        static Dictionary<string, object> BuildTurnSummary(List<LlmPublicDuelEvent> turnEvents)
        {
            ulong firstId = turnEvents[0].EventId;
            ulong lastId = turnEvents[0].EventId;
            int turn = turnEvents[0].Turn;
            int summonCount = 0;
            int activationCount = 0;
            int attackCount = 0;
            int moveCount = 0;
            Dictionary<string, int> kindCountMap = new Dictionary<string, int>();
            Dictionary<int, string> cardNamesById = new Dictionary<int, string>();
            HashSet<int> actors = new HashSet<int>();

            foreach (LlmPublicDuelEvent evt in turnEvents)
            {
                if (evt.EventId < firstId)
                {
                    firstId = evt.EventId;
                }
                if (evt.EventId > lastId)
                {
                    lastId = evt.EventId;
                }
                actors.Add(evt.ActorPlayer);
                string kindName = LlmPublicHistoryRedactionPolicy.KindName(evt.Kind);
                if (!kindCountMap.ContainsKey(kindName))
                {
                    kindCountMap[kindName] = 0;
                }
                kindCountMap[kindName]++;

                switch (evt.Kind)
                {
                    case LlmPublicDuelEventKind.NormalSummon:
                    case LlmPublicDuelEventKind.SpecialSummon:
                    case LlmPublicDuelEventKind.PendulumSummon:
                    case LlmPublicDuelEventKind.FlipSummon:
                        summonCount++;
                        break;
                    case LlmPublicDuelEventKind.ActivateEffect:
                        activationCount++;
                        break;
                    case LlmPublicDuelEventKind.AttackDeclare:
                        attackCount++;
                        break;
                    case LlmPublicDuelEventKind.PositionChange:
                    case LlmPublicDuelEventKind.MovePhase:
                        moveCount++;
                        break;
                    default:
                        if (!string.IsNullOrEmpty(evt.DestinationZone) || !string.IsNullOrEmpty(evt.SourceZone))
                        {
                            moveCount++;
                        }
                        break;
                }

                if (evt.CardId.HasValue &&
                    evt.CardId.Value > 0 &&
                    !LlmPublicHistoryRedactionPolicy.IsAbsolutePublicSetKind(evt.Kind))
                {
                    int id = evt.CardId.Value;
                    if (!cardNamesById.ContainsKey(id))
                    {
                        cardNamesById[id] = evt.CardName ?? string.Empty;
                    }
                }
            }

            List<int> actorList = new List<int>(actors);
            actorList.Sort();

            List<string> kindKeys = new List<string>(kindCountMap.Keys);
            kindKeys.Sort(StringComparer.Ordinal);
            List<object> kindCounts = new List<object>();
            foreach (string key in kindKeys)
            {
                kindCounts.Add(new Dictionary<string, object>()
                {
                    { "kind", key },
                    { "count", kindCountMap[key] },
                });
            }

            List<int> cardIds = new List<int>(cardNamesById.Keys);
            cardIds.Sort();
            List<object> cards = new List<object>();
            List<string> cardNames = new List<string>();
            foreach (int id in cardIds)
            {
                string name = cardNamesById[id];
                cards.Add(new Dictionary<string, object>()
                {
                    { "card_id", id },
                    { "name", name },
                });
                cardNames.Add(name);
            }

            // Insertion order of keys is fixed below for deterministic MiniJSON output.
            return new Dictionary<string, object>()
            {
                { "turn", turn },
                { "first_event_id", (long)firstId },
                { "last_event_id", (long)lastId },
                { "event_count", turnEvents.Count },
                { "actor_players", actorList },
                { "kind_counts", kindCounts },
                { "cards", cards },
                { "card_ids", cardIds },
                { "card_names", cardNames },
                { "summon_count", summonCount },
                { "activation_count", activationCount },
                { "attack_count", attackCount },
                { "move_count", moveCount },
                { "lp_delta", null },
            };
        }

        void MaybeRecordRevealedCard(LlmPublicDuelEvent evt)
        {
            if (evt == null ||
                LlmPublicHistoryRedactionPolicy.IsAbsolutePublicSetKind(evt.Kind) ||
                !evt.CardId.HasValue ||
                evt.CardId.Value <= 0)
            {
                return;
            }
            if (!revealedCardIds.Add(evt.CardId.Value))
            {
                return;
            }

            revealedCardContext.Add(new Dictionary<string, object>()
            {
                { "card_id", evt.CardId.Value },
                { "name", evt.CardName ?? string.Empty },
                { "text", string.Empty },
            });
            revealedCardContext.Sort(CompareRevealedCards);
        }

        static int CompareRevealedCards(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            int idA = a != null && a.ContainsKey("card_id") ? Convert.ToInt32(a["card_id"]) : 0;
            int idB = b != null && b.ContainsKey("card_id") ? Convert.ToInt32(b["card_id"]) : 0;
            return idA.CompareTo(idB);
        }

        /// <summary>
        /// Projection revealed-card context is built only from remaining detailed events
        /// so compacted old card metadata cannot defeat the byte budget.
        /// </summary>
        static List<Dictionary<string, object>> BuildDetailedOnlyRevealedContext(
            List<LlmPublicDuelEvent> detailed)
        {
            Dictionary<int, string> namesById = new Dictionary<int, string>();
            foreach (LlmPublicDuelEvent evt in detailed)
            {
                if (evt == null ||
                    LlmPublicHistoryRedactionPolicy.IsAbsolutePublicSetKind(evt.Kind) ||
                    !evt.CardId.HasValue ||
                    evt.CardId.Value <= 0)
                {
                    continue;
                }
                int id = evt.CardId.Value;
                if (!namesById.ContainsKey(id))
                {
                    namesById[id] = evt.CardName ?? string.Empty;
                }
            }

            List<int> ids = new List<int>(namesById.Keys);
            ids.Sort();
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            foreach (int id in ids)
            {
                result.Add(new Dictionary<string, object>()
                {
                    { "card_id", id },
                    { "name", namesById[id] },
                    { "text", string.Empty },
                });
            }
            return result;
        }

        bool ContainsEventId(ulong eventId)
        {
            foreach (LlmPublicDuelEvent evt in events)
            {
                if (evt.EventId == eventId)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Attaches the budgeted request projection (not full history) onto DecisionSnapshot.
    /// Full uncompacted history remains available only via tracker.CreateSnapshot / audit logs.
    /// </summary>
    static class LlmDecisionSnapshotHistory
    {
        public static void Attach(DecisionSnapshot snapshot, LlmDuelHistoryTracker tracker)
        {
            if (snapshot == null)
            {
                return;
            }
            if (tracker == null)
            {
                snapshot.DuelHistory = new LlmDuelHistoryState();
                return;
            }
            snapshot.DuelHistory = tracker.CreateRequestProjection(snapshot.ControlledPlayer);
        }
    }
}

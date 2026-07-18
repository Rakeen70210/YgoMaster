using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-004 Slice 4 RED contract: schema v4 request projection of duel_history,
    /// provider citation / opponent_action_assessment validation against the exact request
    /// snapshot, and explicit schema-v3 compatibility only (no silent live upgrade).
    /// Tests-only phase — production remains schema 3 until the green slice.
    /// </summary>
    static class Llm004Slice4Tests
    {
        const string SentinelOpponentHandName = "SENTINEL_OPP_HAND_LEAK_99401";
        const string SentinelSetName = "SENTINEL_FACEDOWN_SET_LEAK_99402";
        const string SentinelDeckName = "SENTINEL_DECK_LEAK_99403";
        const string SentinelExtraName = "SENTINEL_EXTRA_LEAK_99404";
        const int SentinelCardId = 99402991;
        const int SentinelUniqueId = 88402881;
        const int SentinelHandIndex = 7;

        public static void RunAll()
        {
            ProtocolSchemaVersionIsFour();
            LiveSerializeDecisionRequestIncludesDeterministicDuelHistory();
            DuelHistorySerializationPreservesOrderCompactionSourceAndEvidence();
            DuelHistoryRequestAndLogSinksOmitSentinelHiddenIdentities();
            DecisionWindowAuditSerializesAttachedDuelHistory();
            ResponseContractExposesOpponentActionAssessmentAndHistoryEventIdsUsed();
            ParsePreservesOpponentActionAssessmentAndHistoryEventIdsUsed();
            ParseRejectsMissingHistoryEventIdsUsedInSchemaV4Response();
            ValidateRequiresNonemptyOpponentActionAssessmentWhenOpponentHistoryExists();
            ValidateRequiresNonemptyOpponentActionAssessmentWhenOpponentOnlyInCompactedSummary();
            ValidateAllowsEmptyAssessmentWhenNoOpponentHistoryButListPresent();
            ValidateAcceptsEmptyCitationList();
            ValidateAcceptsValidUniqueCitations();
            ValidateRejectsDuplicateCitations();
            ValidateRejectsZeroOrNegativeCitations();
            ParseOrValidateRejectsDuplicateZeroAndNegativeHistoryEventIdsWithExactErrorFamilies();
            ValidateRejectsCitationsAbsentFromExactRequestSnapshot();
            ValidateRejectsCitationsOnlyPresentInCompactedSummaryRanges();
            RetrySnapshotValidatesCitationsOnlyAgainstOwnImmutableHistory();
            TryMigrateSchemaV3RequestMigratesValidFixtureDeterministically();
            TryMigrateSchemaV3RequestRejectsInvalidAndNonV3Inputs();
            LiveSerializeDecisionRequestIsV4OnlyAndNeverImplicitlyMigrates();
        }

        static void ProtocolSchemaVersionIsFour()
        {
            AssertEqual(4, LlmBrokerProtocol.SchemaVersion, "SchemaVersion must be 4");
        }

        static void LiveSerializeDecisionRequestIncludesDeterministicDuelHistory()
        {
            DecisionSnapshot snapshot = CreateStrategicSnapshot(controlledPlayer: 1, runEffectSeq: 240);
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 18, 2, 0, LlmPublicDuelEventKind.NormalSummon, 1234, "Public Monster");
            AppendPublic(tracker, 19, 2, 0, LlmPublicDuelEventKind.SetSpellTrap, 0, null);
            AppendPublic(tracker, 20, 2, 0, LlmPublicDuelEventKind.ActivateEffect, 1234, "Public Monster");
            AppendOutcome(tracker, 21, 2, 0, LlmPublicDuelEventKind.CardMovedPublic, 5678, "Public Grave Card",
                "deck", "graveyard");
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);

            string requestJson = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            Dictionary<string, object> data = DeserializeObject(requestJson);

            AssertEqual((long)4, data["schema_version"], "request schema_version");
            AssertTrue(data.ContainsKey("duel_history"), "request includes duel_history");
            AssertTrue(data.ContainsKey("turn_memory"), "turn_memory retained");

            Dictionary<string, object> history = (Dictionary<string, object>)data["duel_history"];
            AssertEqual((long)1, history["history_version"], "history_version");
            AssertEqual((long)21, history["last_event_id"], "last_event_id");
            AssertEqual(false, history["history_compacted"], "history_compacted");
            AssertTrue(history.ContainsKey("first_detailed_event_id"), "first_detailed_event_id");
            AssertTrue(history.ContainsKey("prior_turn_summaries"), "prior_turn_summaries");
            AssertTrue(history.ContainsKey("revealed_card_context"), "revealed_card_context");
            AssertTrue(history.ContainsKey("events"), "events");

            List<object> events = (List<object>)history["events"];
            AssertEqual(4, events.Count, "events count");
            AssertEqual((long)18, ((Dictionary<string, object>)events[0])["event_id"], "event 0 id");
            AssertEqual((long)19, ((Dictionary<string, object>)events[1])["event_id"], "event 1 id");
            AssertEqual((long)20, ((Dictionary<string, object>)events[2])["event_id"], "event 2 id");
            AssertEqual((long)21, ((Dictionary<string, object>)events[3])["event_id"], "event 3 id");

            // Deterministic: same snapshot serializes identically.
            string again = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            AssertEqual(requestJson, again, "SerializeDecisionRequest deterministic");
        }

        static void DuelHistorySerializationPreservesOrderCompactionSourceAndEvidence()
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 3,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            // Older protected-ineligible turns to force compaction when budgets permit.
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 100, "CardT1");
            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 100, "CardT1");
            AppendPublic(tracker, 3, 2, 1, LlmPublicDuelEventKind.NormalSummon, 200, "CardT2");
            AppendPublic(tracker, 4, 3, 0, LlmPublicDuelEventKind.NormalSummon, 300, "CardT3");
            AppendPublic(tracker, 5, 3, 0, LlmPublicDuelEventKind.ActivateEffect, 300, "CardT3");

            DecisionSnapshot snapshot = CreateStrategicSnapshot(controlledPlayer: 1, runEffectSeq: 50);
            snapshot.Turn = 3;
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);
            Dictionary<string, object> data = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));
            Dictionary<string, object> history = (Dictionary<string, object>)data["duel_history"];

            AssertTrue(history.ContainsKey("history_compacted"), "compaction metadata present");
            AssertTrue(history.ContainsKey("prior_turn_summaries"), "prior_turn_summaries present");
            AssertTrue(history.ContainsKey("first_detailed_event_id"), "first_detailed_event_id present");
            AssertTrue(history.ContainsKey("budget_status"), "budget_status present");

            List<object> events = (List<object>)history["events"];
            AssertTrue(events.Count > 0, "detailed events present");
            long previousId = 0;
            foreach (object raw in events)
            {
                Dictionary<string, object> evt = (Dictionary<string, object>)raw;
                long eventId = Convert.ToInt64(evt["event_id"]);
                AssertTrue(eventId > previousId, "events ordered by event_id");
                previousId = eventId;
                AssertTrue(evt.ContainsKey("evidence"), "evidence field");
                AssertTrue(evt.ContainsKey("kind"), "kind field");
                AssertTrue(evt.ContainsKey("actor_player"), "actor_player field");
                string evidence = Convert.ToString(evt["evidence"]);
                AssertTrue(
                    evidence == "accepted_command" || evidence == "public_state_delta",
                    "evidence is accepted_command or public_state_delta");
            }
        }

        static void DuelHistoryRequestAndLogSinksOmitSentinelHiddenIdentities()
        {
            DecisionSnapshot snapshot = CreateStrategicSnapshot(controlledPlayer: 1, runEffectSeq: 60);
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);

            // Absolute-public set: identity must never appear even if omniscient inputs existed.
            LlmPublicDuelEvent setMonster = new LlmPublicDuelEvent()
            {
                EventId = 1,
                RunEffectSeq = 1,
                Turn = 1,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = 0,
                Kind = LlmPublicDuelEventKind.SetMonster,
                CardId = SentinelCardId,
                CardName = SentinelSetName,
                DestinationZone = "monster_zone",
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
                SourceSignature = "set_monster_sink",
            };
            AssertTrue(tracker.TryAppendPublicEvent(setMonster).NewlyAppended, "append set");

            LlmPublicDuelEvent setSpell = new LlmPublicDuelEvent()
            {
                EventId = 2,
                RunEffectSeq = 2,
                Turn = 1,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = 0,
                Kind = LlmPublicDuelEventKind.SetSpellTrap,
                CardId = SentinelCardId,
                CardName = SentinelOpponentHandName,
                DestinationZone = "spell_trap_zone",
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
                SourceSignature = "set_spell_sink",
            };
            AssertTrue(tracker.TryAppendPublicEvent(setSpell).NewlyAppended, "append set spell");

            AppendPublic(tracker, 3, 1, 0, LlmPublicDuelEventKind.NormalSummon, 1234, "Public Monster");
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);

            string requestJson = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            string windowJson = LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot);
            string projectionJson = LlmDuelHistoryTracker.SerializeProjectionJson(snapshot.DuelHistory);

            string[] sinks = new[] { requestJson, windowJson, projectionJson };
            string[] forbidden = new[]
            {
                SentinelSetName,
                SentinelOpponentHandName,
                SentinelDeckName,
                SentinelExtraName,
                SentinelCardId.ToString(),
                SentinelUniqueId.ToString(),
                SentinelHandIndex.ToString(),
            };
            foreach (string sink in sinks)
            {
                foreach (string token in forbidden)
                {
                    AssertFalse(
                        sink.Contains(token),
                        "sink must omit sentinel '" + token + "'");
                }
            }

            // Set events remain present without identity.
            Dictionary<string, object> data = DeserializeObject(requestJson);
            Dictionary<string, object> history = (Dictionary<string, object>)data["duel_history"];
            List<object> events = (List<object>)history["events"];
            bool sawSet = false;
            foreach (object raw in events)
            {
                Dictionary<string, object> evt = (Dictionary<string, object>)raw;
                string kind = Convert.ToString(evt["kind"]);
                if (kind == "set_monster" || kind == "set_spell_trap")
                {
                    sawSet = true;
                    AssertFalse(evt.ContainsKey("card_id"), "set event omits card_id");
                    AssertFalse(evt.ContainsKey("card_name"), "set event omits card_name");
                }
            }
            AssertTrue(sawSet, "set events present without identity");
        }

        static void DecisionWindowAuditSerializesAttachedDuelHistory()
        {
            DecisionSnapshot snapshot = CreateStrategicSnapshot(controlledPlayer: 1, runEffectSeq: 70);
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 9, 2, 0, LlmPublicDuelEventKind.NormalSummon, 55, "Audit Public");
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);

            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot));
            AssertEqual((long)4, data["schema_version"], "decision_window schema_version");
            AssertTrue(data.ContainsKey("duel_history"), "decision_window includes duel_history");
            Dictionary<string, object> history = (Dictionary<string, object>)data["duel_history"];
            List<object> events = (List<object>)history["events"];
            AssertEqual(1, events.Count, "decision_window history events");
            AssertEqual((long)9, ((Dictionary<string, object>)events[0])["event_id"], "window event id");
        }

        static void ResponseContractExposesOpponentActionAssessmentAndHistoryEventIdsUsed()
        {
            PropertyInfo assessment = typeof(LlmBrokerDecisionResponse).GetProperty(
                "OpponentActionAssessment");
            PropertyInfo citations = typeof(LlmBrokerDecisionResponse).GetProperty(
                "HistoryEventIdsUsed");
            AssertTrue(
                assessment != null,
                "LlmBrokerDecisionResponse.OpponentActionAssessment required");
            AssertTrue(
                citations != null,
                "LlmBrokerDecisionResponse.HistoryEventIdsUsed required");
            AssertTrue(
                assessment.PropertyType == typeof(string),
                "OpponentActionAssessment is string");
            AssertTrue(
                typeof(IList).IsAssignableFrom(citations.PropertyType) ||
                    citations.PropertyType.IsGenericType,
                "HistoryEventIdsUsed is a list type");
        }

        static void ParsePreservesOpponentActionAssessmentAndHistoryEventIdsUsed()
        {
            LlmBrokerDecisionResponse response;
            string error;
            bool parsed = LlmBrokerProtocol.TryParseDecisionResponse(
                "{" +
                "\"run_effect_seq\":240," +
                "\"action_id\":0," +
                "\"reason\":\"Public Monster established pressure; answer from history.\"," +
                "\"opponent_board_assessment\":\"One public monster.\"," +
                "\"opponent_action_assessment\":\"Opponent used event 18 to establish a monster and event 20 for its effect.\"," +
                "\"history_event_ids_used\":[18,20]" +
                "}",
                out response,
                out error);

            AssertTrue(parsed, "parsed schema-v4 response: " + error);
            AssertEqual(null, error, "parse error");
            string assessment = GetStringProperty(response, "OpponentActionAssessment");
            AssertTrue(
                !string.IsNullOrEmpty(assessment) && assessment.Contains("event 18"),
                "OpponentActionAssessment preserved");
            List<long> used = GetHistoryEventIdsUsed(response);
            AssertTrue(used != null, "HistoryEventIdsUsed explicitly present");
            AssertEqual(2, used.Count, "citation count");
            AssertEqual(18L, used[0], "citation[0]");
            AssertEqual(20L, used[1], "citation[1]");
        }

        static void ParseRejectsMissingHistoryEventIdsUsedInSchemaV4Response()
        {
            LlmBrokerDecisionResponse response;
            string error;
            bool parsed = LlmBrokerProtocol.TryParseDecisionResponse(
                "{" +
                "\"run_effect_seq\":240," +
                "\"action_id\":0," +
                "\"reason\":\"Develop from public board.\"," +
                "\"opponent_board_assessment\":\"visible\"," +
                "\"opponent_action_assessment\":\"Opponent summoned publicly.\"" +
                "}",
                out response,
                out error);

            if (parsed)
            {
                // Parse may succeed only if validation then rejects missing list.
                DecisionSnapshot snapshot = SnapshotWithOpponentHistory();
                LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(
                    snapshot,
                    response);
                AssertFalse(result.IsValid, "missing history_event_ids_used must fail validate");
                AssertTrue(
                    result.Error != null &&
                        result.Error.IndexOf("history_event", StringComparison.OrdinalIgnoreCase) >= 0,
                    "error names history_event_ids_used: " + result.Error);
            }
            else
            {
                AssertTrue(
                    error != null &&
                        error.IndexOf("history_event", StringComparison.OrdinalIgnoreCase) >= 0,
                    "parse error names history_event_ids_used: " + error);
            }
        }

        static void ValidateRequiresNonemptyOpponentActionAssessmentWhenOpponentHistoryExists()
        {
            DecisionSnapshot snapshot = SnapshotWithOpponentHistory();
            LlmBrokerDecisionResponse response = BaseValidResponse(snapshot.RunEffectSeq, 0);
            SetStringProperty(response, "OpponentActionAssessment", null);
            SetHistoryEventIdsUsed(response, new List<long>());

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertFalse(result.IsValid, "missing assessment with opponent history");
            AssertErrorFamily(
                result.Error,
                "missing_opponent_action_assessment",
                "opponent_action");

            SetStringProperty(response, "OpponentActionAssessment", "   ");
            result = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertFalse(result.IsValid, "whitespace assessment with opponent history");
            AssertErrorFamily(
                result.Error,
                "missing_opponent_action_assessment",
                "opponent_action");
        }

        static void ValidateRequiresNonemptyOpponentActionAssessmentWhenOpponentOnlyInCompactedSummary()
        {
            DecisionSnapshot snapshot = SnapshotWithOpponentHistoryOnlyInCompactedSummary();
            AssertTrue(snapshot.DuelHistory.HistoryCompacted, "fixture is compacted");
            AssertTrue(snapshot.DuelHistory.PriorTurnSummaries.Count > 0, "prior summaries present");

            bool sawOpponentInSummary = false;
            foreach (Dictionary<string, object> summary in snapshot.DuelHistory.PriorTurnSummaries)
            {
                object actorsRaw;
                if (!summary.TryGetValue("actor_players", out actorsRaw) || actorsRaw == null)
                {
                    continue;
                }
                foreach (object actor in (IEnumerable)actorsRaw)
                {
                    if (Convert.ToInt32(actor) == 0)
                    {
                        sawOpponentInSummary = true;
                    }
                }
            }
            AssertTrue(sawOpponentInSummary, "compacted summary includes opponent actor_player 0");

            foreach (LlmPublicDuelEvent evt in snapshot.DuelHistory.Events)
            {
                AssertEqual(1, evt.ActorPlayer, "detailed events are controlled-player only");
            }

            LlmBrokerDecisionResponse response = BaseValidResponse(snapshot.RunEffectSeq, 0);
            SetStringProperty(response, "OpponentActionAssessment", "");
            SetHistoryEventIdsUsed(response, new List<long>());

            LlmBrokerValidationResult empty = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertFalse(empty.IsValid, "empty assessment rejected when opponent only in compacted summary");
            AssertErrorFamily(
                empty.Error,
                "missing_opponent_action_assessment",
                "opponent_action");

            SetStringProperty(
                response,
                "OpponentActionAssessment",
                "Opponent authored earlier turns (compacted summary) before my current development.");
            LlmBrokerValidationResult ok = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertTrue(ok.IsValid, "nonempty assessment accepted with compacted opponent history: " + ok.Error);
        }

        static void ValidateAllowsEmptyAssessmentWhenNoOpponentHistoryButListPresent()
        {
            DecisionSnapshot snapshot = CreateStrategicSnapshot(controlledPlayer: 1, runEffectSeq: 100);
            // Only controlled-player (seat 1) authored public history — no opponent events.
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 1, 1, 1, LlmPublicDuelEventKind.NormalSummon, 10, "Own Public");
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);

            LlmBrokerDecisionResponse response = BaseValidResponse(snapshot.RunEffectSeq, 0);
            SetStringProperty(response, "OpponentActionAssessment", "");
            SetHistoryEventIdsUsed(response, new List<long>());

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertTrue(result.IsValid, "empty assessment ok without opponent history: " + result.Error);
            AssertTrue(
                GetHistoryEventIdsUsed(response) != null,
                "history_event_ids_used list remains present");
        }

        static void ValidateAcceptsEmptyCitationList()
        {
            DecisionSnapshot snapshot = SnapshotWithOpponentHistory();
            LlmBrokerDecisionResponse response = BaseValidResponse(snapshot.RunEffectSeq, 0);
            SetStringProperty(
                response,
                "OpponentActionAssessment",
                "Opponent established a public monster; decision uses current board only.");
            SetHistoryEventIdsUsed(response, new List<long>());

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertTrue(result.IsValid, "empty citation list accepted: " + result.Error);
        }

        static void ValidateAcceptsValidUniqueCitations()
        {
            DecisionSnapshot snapshot = SnapshotWithOpponentHistory();
            LlmBrokerDecisionResponse response = BaseValidResponse(snapshot.RunEffectSeq, 0);
            SetStringProperty(
                response,
                "OpponentActionAssessment",
                "Events 18 and 20 drove the answer.");
            SetHistoryEventIdsUsed(response, new List<long>() { 18, 20 });

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertTrue(result.IsValid, "valid unique citations: " + result.Error);
        }

        static void ValidateRejectsDuplicateCitations()
        {
            DecisionSnapshot snapshot = SnapshotWithOpponentHistory();
            LlmBrokerDecisionResponse response = BaseValidResponse(snapshot.RunEffectSeq, 0);
            SetStringProperty(response, "OpponentActionAssessment", "Duplicate citation test.");
            SetHistoryEventIdsUsed(response, new List<long>() { 18, 18 });

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertFalse(result.IsValid, "duplicate citations rejected");
            AssertErrorFamily(result.Error, "duplicate_history_event_id", "duplicate");
        }

        static void ValidateRejectsZeroOrNegativeCitations()
        {
            DecisionSnapshot snapshot = SnapshotWithOpponentHistory();
            LlmBrokerDecisionResponse response = BaseValidResponse(snapshot.RunEffectSeq, 0);
            SetStringProperty(response, "OpponentActionAssessment", "Invalid id test.");
            SetHistoryEventIdsUsed(response, new List<long>() { 0 });

            LlmBrokerValidationResult zero = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertFalse(zero.IsValid, "zero citation rejected");
            AssertErrorFamily(zero.Error, "invalid_history_event_id", "invalid");

            SetHistoryEventIdsUsed(response, new List<long>() { -3 });
            LlmBrokerValidationResult negative = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertFalse(negative.IsValid, "negative citation rejected");
            AssertErrorFamily(negative.Error, "invalid_history_event_id", "invalid");
        }

        static void ParseOrValidateRejectsDuplicateZeroAndNegativeHistoryEventIdsWithExactErrorFamilies()
        {
            DecisionSnapshot snapshot = SnapshotWithOpponentHistory();

            // Empty list remains valid at parse and validate.
            LlmBrokerDecisionResponse emptyListResponse;
            string emptyListError;
            bool emptyParsed = LlmBrokerProtocol.TryParseDecisionResponse(
                BuildSchemaV4ResponseJson(snapshot.RunEffectSeq, 0, new long[0], "Opponent public history noted."),
                out emptyListResponse,
                out emptyListError);
            if (emptyParsed)
            {
                LlmBrokerValidationResult emptyOk = LlmBrokerProtocol.ValidateResponse(
                    snapshot,
                    emptyListResponse);
                AssertTrue(emptyOk.IsValid, "empty history_event_ids_used valid: " + emptyOk.Error);
            }
            else
            {
                throw new Exception(
                    "empty history_event_ids_used must parse successfully, got: " + emptyListError);
            }

            AssertParseOrValidateHistoryCitationError(
                snapshot,
                "[18,18]",
                "duplicate_history_event_id",
                "duplicate");
            AssertParseOrValidateHistoryCitationError(
                snapshot,
                "[0]",
                "invalid_history_event_id",
                "invalid");
            AssertParseOrValidateHistoryCitationError(
                snapshot,
                "[-1]",
                "invalid_history_event_id",
                "invalid");
            AssertParseOrValidateHistoryCitationError(
                snapshot,
                "[18,0]",
                "invalid_history_event_id",
                "invalid");
        }

        static void ValidateRejectsCitationsAbsentFromExactRequestSnapshot()
        {
            DecisionSnapshot snapshot = SnapshotWithOpponentHistory();
            LlmBrokerDecisionResponse response = BaseValidResponse(snapshot.RunEffectSeq, 0);
            SetStringProperty(response, "OpponentActionAssessment", "Cites unknown event.");
            SetHistoryEventIdsUsed(response, new List<long>() { 18, 99999 });

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertFalse(result.IsValid, "absent citation rejected");
            AssertErrorFamily(result.Error, "unknown_history_event_id", "unknown");
        }

        static void ValidateRejectsCitationsOnlyPresentInCompactedSummaryRanges()
        {
            DecisionSnapshot snapshot = SnapshotWithOpponentHistoryOnlyInCompactedSummary();
            AssertTrue(snapshot.DuelHistory.HistoryCompacted, "compacted fixture");

            ulong compactedFirst = 0;
            ulong compactedLast = 0;
            bool foundRange = false;
            foreach (Dictionary<string, object> summary in snapshot.DuelHistory.PriorTurnSummaries)
            {
                if (!summary.ContainsKey("first_event_id") || !summary.ContainsKey("last_event_id"))
                {
                    continue;
                }
                compactedFirst = Convert.ToUInt64(summary["first_event_id"]);
                compactedLast = Convert.ToUInt64(summary["last_event_id"]);
                foundRange = true;
                break;
            }
            AssertTrue(foundRange, "summary exposes first_event_id/last_event_id");
            AssertTrue(compactedFirst > 0, "compacted first_event_id");

            HashSet<ulong> detailedIds = new HashSet<ulong>();
            foreach (LlmPublicDuelEvent evt in snapshot.DuelHistory.Events)
            {
                detailedIds.Add(evt.EventId);
            }
            AssertFalse(
                detailedIds.Contains(compactedFirst),
                "compacted-away first_event_id must not appear in detailed events");
            AssertTrue(detailedIds.Count > 0, "detailed events remain");

            ulong detailedId = 0;
            foreach (ulong id in detailedIds)
            {
                detailedId = id;
                break;
            }

            LlmBrokerDecisionResponse response = BaseValidResponse(snapshot.RunEffectSeq, 0);
            SetStringProperty(
                response,
                "OpponentActionAssessment",
                "Must not cite compacted-away event detail.");
            SetHistoryEventIdsUsed(response, new List<long>() { (long)compactedFirst });

            LlmBrokerValidationResult compactedOnly = LlmBrokerProtocol.ValidateResponse(
                snapshot,
                response);
            AssertFalse(
                compactedOnly.IsValid,
                "citation of compacted-away event id rejected (not in detailed request events)");
            AssertErrorFamily(compactedOnly.Error, "unknown_history_event_id", "unknown");

            // Mid-range compacted id (when range spans more than one event) also rejected.
            if (compactedLast > compactedFirst)
            {
                SetHistoryEventIdsUsed(response, new List<long>() { (long)compactedLast });
                LlmBrokerValidationResult compactedLastOnly = LlmBrokerProtocol.ValidateResponse(
                    snapshot,
                    response);
                AssertFalse(compactedLastOnly.IsValid, "last_event_id from summary alone rejected");
                AssertErrorFamily(compactedLastOnly.Error, "unknown_history_event_id", "unknown");
            }

            SetHistoryEventIdsUsed(response, new List<long>() { (long)detailedId });
            LlmBrokerValidationResult detailedOk = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertTrue(
                detailedOk.IsValid,
                "detailed event id accepted: " + detailedOk.Error);
        }

        static void RetrySnapshotValidatesCitationsOnlyAgainstOwnImmutableHistory()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 10, 1, 0, LlmPublicDuelEventKind.NormalSummon, 1, "Older Public");
            DecisionSnapshot older = CreateStrategicSnapshot(controlledPlayer: 1, runEffectSeq: 10);
            LlmDecisionSnapshotHistory.Attach(older, tracker);

            AppendPublic(tracker, 20, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 1, "Newer Public");
            DecisionSnapshot newer = CreateStrategicSnapshot(controlledPlayer: 1, runEffectSeq: 10);
            LlmDecisionSnapshotHistory.Attach(newer, tracker);

            // older snapshot frozen at event 10 only
            AssertEqual(1, older.DuelHistory.Events.Count, "older history frozen");
            AssertEqual(2, newer.DuelHistory.Events.Count, "newer history has both");

            LlmBrokerDecisionResponse response = BaseValidResponse(older.RunEffectSeq, 0);
            SetStringProperty(
                response,
                "OpponentActionAssessment",
                "Cites an event from a different snapshot generation.");
            SetHistoryEventIdsUsed(response, new List<long>() { 20 });

            LlmBrokerValidationResult againstOlder = LlmBrokerProtocol.ValidateResponse(older, response);
            AssertFalse(againstOlder.IsValid, "newer id invalid against older snapshot");
            AssertErrorFamily(againstOlder.Error, "unknown_history_event_id", "unknown");

            SetHistoryEventIdsUsed(response, new List<long>() { 10 });
            LlmBrokerValidationResult olderOk = LlmBrokerProtocol.ValidateResponse(older, response);
            AssertTrue(olderOk.IsValid, "own-snapshot citation accepted: " + olderOk.Error);

            // Newer snapshot does not accept a fabricated future id either.
            SetHistoryEventIdsUsed(response, new List<long>() { 30 });
            LlmBrokerValidationResult againstNewer = LlmBrokerProtocol.ValidateResponse(newer, response);
            AssertFalse(againstNewer.IsValid, "absent id invalid against newer snapshot");
            AssertErrorFamily(againstNewer.Error, "unknown_history_event_id", "unknown");
        }

        static void TryMigrateSchemaV3RequestMigratesValidFixtureDeterministically()
        {
            string schemaV3Json =
                "{" +
                "\"kind\":\"decision_request\"," +
                "\"schema_version\":3," +
                "\"run_effect_seq\":44," +
                "\"view_type\":\"WaitInput\"," +
                "\"view_type_id\":1," +
                "\"acting_player\":1," +
                "\"controlled_player\":1," +
                "\"turn\":2," +
                "\"turn_player\":1," +
                "\"current_phase\":3," +
                "\"current_step\":0," +
                "\"is_strategic_window\":true," +
                "\"strategic_window_reason\":\"strategic_choices\"," +
                "\"strategic_action_count\":1," +
                "\"mechanical_action_count\":0," +
                "\"public_state\":{\"players\":[{\"player\":0,\"life_points\":8000},{\"player\":1,\"life_points\":6200}]}," +
                "\"board_context\":{\"players\":[]}," +
                "\"opponent_context\":{\"opponent_player\":0,\"field_count\":1}," +
                "\"turn_memory\":{\"phase_plan\":\"develop_board\",\"recent_actions\":[]}," +
                "\"legal_actions\":[" +
                "{\"action_id\":0,\"kind\":\"command\",\"command\":\"Summon\",\"command_id\":0," +
                "\"action_label\":\"Summon Public Monster\",\"is_mechanical\":false}" +
                "]" +
                "}";

            // Ensure input lacks duel_history and is not mutated in place by migration.
            AssertFalse(schemaV3Json.Contains("duel_history"), "v3 fixture omits duel_history");
            string originalV3 = schemaV3Json;

            string schemaV4Json;
            string error;
            bool ok = InvokeTryMigrateSchemaV3Request(schemaV3Json, out schemaV4Json, out error);

            AssertTrue(ok, "TryMigrateSchemaV3Request succeeds for valid v3: " + error);
            AssertTrue(string.IsNullOrEmpty(error), "error null/empty on success: " + error);
            AssertTrue(!string.IsNullOrEmpty(schemaV4Json), "schemaV4Json produced");
            AssertEqual(originalV3, schemaV3Json, "input schemaV3Json not mutated");

            Dictionary<string, object> v4 = DeserializeObject(schemaV4Json);
            AssertEqual((long)4, v4["schema_version"], "migrated schema_version");
            AssertEqual((long)44, v4["run_effect_seq"], "preserved run_effect_seq");
            AssertEqual((long)1, v4["controlled_player"], "preserved controlled_player");
            AssertEqual((long)1, v4["acting_player"], "preserved acting_player");
            AssertTrue(v4.ContainsKey("public_state"), "preserved public_state");
            AssertTrue(v4.ContainsKey("legal_actions"), "preserved legal_actions");
            AssertEqual(1, ((List<object>)v4["legal_actions"]).Count, "legal_actions count preserved");
            AssertTrue(v4.ContainsKey("turn_memory"), "preserved turn_memory");
            AssertTrue(v4.ContainsKey("opponent_context"), "preserved opponent_context");

            AssertTrue(v4.ContainsKey("duel_history"), "migrated duel_history present");
            Dictionary<string, object> history = (Dictionary<string, object>)v4["duel_history"];
            AssertEqual((long)1, history["history_version"], "empty history_version");
            AssertEqual((long)0, history["last_event_id"], "empty last_event_id");
            AssertEqual((long)1, history["first_detailed_event_id"], "empty first_detailed_event_id");
            AssertEqual(false, history["history_compacted"], "empty history_compacted");
            AssertTrue(history.ContainsKey("events"), "events list present");
            AssertEqual(0, ((List<object>)history["events"]).Count, "events empty");
            AssertTrue(history.ContainsKey("prior_turn_summaries"), "prior_turn_summaries present");
            AssertEqual(0, ((List<object>)history["prior_turn_summaries"]).Count, "prior summaries empty");
            AssertTrue(history.ContainsKey("revealed_card_context"), "revealed_card_context present");
            AssertEqual(0, ((List<object>)history["revealed_card_context"]).Count, "revealed empty");

            // No hidden-data widening / sentinel injection during migration.
            AssertFalse(schemaV4Json.Contains(SentinelOpponentHandName), "no hand sentinel");
            AssertFalse(schemaV4Json.Contains(SentinelSetName), "no set sentinel");
            AssertFalse(schemaV4Json.Contains(SentinelDeckName), "no deck sentinel");
            AssertFalse(schemaV4Json.Contains(SentinelExtraName), "no extra sentinel");
            AssertFalse(schemaV4Json.Contains(SentinelCardId.ToString()), "no sentinel card id");
            AssertFalse(schemaV4Json.Contains("\"unique_id\""), "no unique_id widening");

            string again;
            string error2;
            bool ok2 = InvokeTryMigrateSchemaV3Request(originalV3, out again, out error2);
            AssertTrue(ok2, "repeat migrate succeeds: " + error2);
            AssertEqual(schemaV4Json, again, "migration deterministic byte-identical output");
        }

        static void TryMigrateSchemaV3RequestRejectsInvalidAndNonV3Inputs()
        {
            string schemaV4Json;
            string error;

            AssertFalse(
                InvokeTryMigrateSchemaV3Request(null, out schemaV4Json, out error),
                "null input fails");
            AssertTrue(!string.IsNullOrEmpty(error), "null input has explicit error");

            AssertFalse(
                InvokeTryMigrateSchemaV3Request("", out schemaV4Json, out error),
                "empty input fails");
            AssertTrue(!string.IsNullOrEmpty(error), "empty input has explicit error");

            AssertFalse(
                InvokeTryMigrateSchemaV3Request("not-json", out schemaV4Json, out error),
                "invalid json fails");
            AssertTrue(!string.IsNullOrEmpty(error), "invalid json has explicit error");

            AssertFalse(
                InvokeTryMigrateSchemaV3Request("{\"schema_version\":2,\"run_effect_seq\":1}", out schemaV4Json, out error),
                "schema v2 fails");
            AssertTrue(!string.IsNullOrEmpty(error), "schema v2 has explicit error");
            AssertTrue(
                error.IndexOf("schema", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    error.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0,
                "schema v2 error mentions schema/version: " + error);

            AssertFalse(
                InvokeTryMigrateSchemaV3Request(
                    "{\"kind\":\"decision_request\",\"schema_version\":4,\"run_effect_seq\":1,\"legal_actions\":[]}",
                    out schemaV4Json,
                    out error),
                "already-v4 fails migration path");
            AssertTrue(!string.IsNullOrEmpty(error), "already-v4 has explicit error");

            AssertFalse(
                InvokeTryMigrateSchemaV3Request(
                    "{\"schema_version\":3,\"run_effect_seq\":1}",
                    out schemaV4Json,
                    out error),
                "v3 missing required decision fields fails");
            AssertTrue(!string.IsNullOrEmpty(error), "incomplete v3 has explicit error");
        }

        static void LiveSerializeDecisionRequestIsV4OnlyAndNeverImplicitlyMigrates()
        {
            AssertEqual(4, LlmBrokerProtocol.SchemaVersion, "live protocol is schema 4");

            DecisionSnapshot snapshot = CreateStrategicSnapshot(1, 99);
            string liveJson = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            Dictionary<string, object> live = DeserializeObject(liveJson);
            AssertEqual((long)4, live["schema_version"], "live request path is schema 4");
            AssertTrue(live.ContainsKey("duel_history"), "live request path includes duel_history");

            // Live path never silently treats schema-v3 payloads as v4: SerializeDecisionRequest
            // only accepts DecisionSnapshot and always emits the live SchemaVersion.
            AssertFalse(
                liveJson.Contains("\"schema_version\":3"),
                "live serialize never emits schema 3");

            // Migration is opt-in only — live serialize does not require or invoke migration
            // for empty/default history snapshots.
            AssertTrue(live.ContainsKey("duel_history"), "default empty history still serialized");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        static DecisionSnapshot SnapshotWithOpponentHistory()
        {
            DecisionSnapshot snapshot = CreateStrategicSnapshot(controlledPlayer: 1, runEffectSeq: 240);
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 18, 2, 0, LlmPublicDuelEventKind.NormalSummon, 1234, "Public Monster");
            AppendPublic(tracker, 19, 2, 0, LlmPublicDuelEventKind.SetSpellTrap, 0, null);
            AppendPublic(tracker, 20, 2, 0, LlmPublicDuelEventKind.ActivateEffect, 1234, "Public Monster");
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);
            return snapshot;
        }

        /// <summary>
        /// Request projection where an older opponent-authored turn is compacted into
        /// prior_turn_summaries and detailed events are only the controlled player's.
        /// </summary>
        static DecisionSnapshot SnapshotWithOpponentHistoryOnlyInCompactedSummary()
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 3,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            // Turn 1 (opponent seat 0) — eligible for compaction (not current/previous).
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 100, "Opp Compact A");
            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 100, "Opp Compact A");
            // Turn 2 (previous, controlled) — protected detail.
            AppendPublic(tracker, 3, 2, 1, LlmPublicDuelEventKind.NormalSummon, 200, "Own Prev");
            // Turn 3 (current, controlled) — protected detail.
            AppendPublic(tracker, 4, 3, 1, LlmPublicDuelEventKind.ActivateEffect, 200, "Own Curr");
            AppendPublic(tracker, 5, 3, 1, LlmPublicDuelEventKind.NormalSummon, 201, "Own Curr B");

            DecisionSnapshot snapshot = CreateStrategicSnapshot(controlledPlayer: 1, runEffectSeq: 300);
            snapshot.Turn = 3;
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);

            AssertTrue(snapshot.DuelHistory != null, "history attached");
            AssertTrue(snapshot.DuelHistory.HistoryCompacted, "older opponent turn compacted");
            AssertTrue(
                snapshot.DuelHistory.PriorTurnSummaries.Count >= 1,
                "prior_turn_summaries non-empty");
            AssertTrue(snapshot.DuelHistory.Events.Count > 0, "detailed events remain");
            foreach (LlmPublicDuelEvent evt in snapshot.DuelHistory.Events)
            {
                AssertEqual(1, evt.ActorPlayer, "detailed events controlled-only in fixture");
                AssertTrue(evt.EventId >= 3, "detailed ids are post-compaction range");
            }
            return snapshot;
        }

        static DecisionSnapshot CreateStrategicSnapshot(int controlledPlayer, ulong runEffectSeq)
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = runEffectSeq,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = controlledPlayer,
                ControlledPlayer = controlledPlayer,
                Turn = 2,
                TurnPlayer = controlledPlayer,
                CurrentPhase = (int)DuelPhase.Main1,
                IsStrategicWindow = true,
                StrategicWindowReason = "strategic_choices",
                StrategicActionCount = 1,
                MechanicalActionCount = 0,
            };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Player = controlledPlayer,
                Position = 13,
                Index = 0,
                Command = DuelCommandType.Summon,
                CardId = 1234,
                CardUniqueId = 5001,
                Card = new LlmCardMetadata()
                {
                    CardId = 1234,
                    Name = "Public Monster",
                    Text = "Synthetic public monster text.",
                },
                ActionLabel = "Summon Public Monster",
                ActionGroup = "summon",
                IsMechanical = false,
                StrategicRole = "board_development",
                ConsequenceHint = "normal_summon_consumes_turn_summon",
            });
            return snapshot;
        }

        static LlmBrokerDecisionResponse BaseValidResponse(ulong runEffectSeq, int actionId)
        {
            return new LlmBrokerDecisionResponse()
            {
                RunEffectSeq = runEffectSeq,
                ActionId = actionId,
                Reason = "Normal Summon Public Monster to develop from history.",
                Confidence = 0.8,
                Plan = "Develop using public events.",
                WhyNow = "Main Phase is open and history supports development.",
                AlternativesConsidered = new List<string>() { "Ending phase gives up tempo." },
                Risk = "Opponent may answer the summon.",
                OpponentBoardAssessment = "Opponent has public history-backed threats.",
            };
        }

        static void AppendPublic(
            LlmDuelHistoryTracker tracker,
            ulong eventId,
            int turn,
            int actor,
            LlmPublicDuelEventKind kind,
            int cardId,
            string cardName)
        {
            LlmPublicDuelEvent evt = new LlmPublicDuelEvent()
            {
                EventId = eventId,
                RunEffectSeq = eventId,
                Turn = turn,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = actor,
                TargetPlayer = -1,
                CardPlayer = -1,
                Kind = kind,
                CardId = cardId > 0 ? (int?)cardId : null,
                CardName = cardName,
                DestinationZone = kind == LlmPublicDuelEventKind.SetSpellTrap
                    ? "spell_trap_zone"
                    : "monster_zone",
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
                SourceSignature = "slice4_evt_" + eventId,
            };
            LlmDuelHistoryAppendResult result = tracker.TryAppendPublicEvent(evt);
            if (!result.Accepted || !result.NewlyAppended)
            {
                throw new Exception("append failed: " + (result.Error ?? "not newly appended"));
            }
        }

        static void AppendOutcome(
            LlmDuelHistoryTracker tracker,
            ulong eventId,
            int turn,
            int actor,
            LlmPublicDuelEventKind kind,
            int cardId,
            string cardName,
            string sourceZone,
            string destinationZone)
        {
            LlmPublicDuelEvent evt = new LlmPublicDuelEvent()
            {
                EventId = eventId,
                RunEffectSeq = eventId,
                Turn = turn,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = actor,
                TargetPlayer = -1,
                CardPlayer = actor,
                Kind = kind,
                CardId = cardId,
                CardName = cardName,
                SourceZone = sourceZone,
                DestinationZone = destinationZone,
                Evidence = LlmPublicHistoryEvidence.PublicStateDelta,
                SourceSignature = "slice4_outcome_" + eventId,
            };
            LlmDuelHistoryAppendResult result = tracker.TryAppendPublicEvent(evt);
            if (!result.Accepted || !result.NewlyAppended)
            {
                throw new Exception("append outcome failed: " + (result.Error ?? "not newly appended"));
            }
        }

        static string GetStringProperty(object target, string name)
        {
            PropertyInfo prop = target.GetType().GetProperty(name);
            AssertTrue(prop != null, "property " + name + " required");
            object value = prop.GetValue(target, null);
            return value as string;
        }

        static void SetStringProperty(object target, string name, string value)
        {
            PropertyInfo prop = target.GetType().GetProperty(name);
            AssertTrue(prop != null, "property " + name + " required");
            prop.SetValue(target, value, null);
        }

        static List<long> GetHistoryEventIdsUsed(LlmBrokerDecisionResponse response)
        {
            PropertyInfo prop = typeof(LlmBrokerDecisionResponse).GetProperty("HistoryEventIdsUsed");
            AssertTrue(prop != null, "HistoryEventIdsUsed property required");
            object value = prop.GetValue(response, null);
            if (value == null)
            {
                return null;
            }
            List<long> ids = new List<long>();
            foreach (object item in (IEnumerable)value)
            {
                ids.Add(Convert.ToInt64(item));
            }
            return ids;
        }

        static void SetHistoryEventIdsUsed(LlmBrokerDecisionResponse response, List<long> ids)
        {
            PropertyInfo prop = typeof(LlmBrokerDecisionResponse).GetProperty("HistoryEventIdsUsed");
            AssertTrue(prop != null, "HistoryEventIdsUsed property required");
            object value = CoerceHistoryEventIdsList(prop.PropertyType, ids);
            prop.SetValue(response, value, null);
        }

        static object CoerceHistoryEventIdsList(Type listType, List<long> ids)
        {
            if (listType == typeof(List<long>) || listType == typeof(IList<long>))
            {
                return new List<long>(ids);
            }
            if (listType == typeof(List<ulong>) || listType == typeof(IList<ulong>))
            {
                List<ulong> converted = new List<ulong>();
                foreach (long id in ids)
                {
                    converted.Add(id < 0 ? 0UL : (ulong)id);
                }
                return converted;
            }
            if (listType == typeof(List<int>) || listType == typeof(IList<int>))
            {
                List<int> converted = new List<int>();
                foreach (long id in ids)
                {
                    converted.Add((int)id);
                }
                return converted;
            }
            if (listType == typeof(long[]))
            {
                return ids.ToArray();
            }
            if (listType == typeof(ulong[]))
            {
                ulong[] converted = new ulong[ids.Count];
                for (int i = 0; i < ids.Count; i++)
                {
                    converted[i] = ids[i] < 0 ? 0UL : (ulong)ids[i];
                }
                return converted;
            }

            // Fallback: construct List<T> via reflection when T is numeric.
            if (listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type elementType = listType.GetGenericArguments()[0];
                object list = Activator.CreateInstance(listType);
                MethodInfo add = listType.GetMethod("Add");
                foreach (long id in ids)
                {
                    object boxed = Convert.ChangeType(id, elementType);
                    add.Invoke(list, new object[] { boxed });
                }
                return list;
            }

            throw new Exception("unsupported HistoryEventIdsUsed type: " + listType.FullName);
        }

        static Dictionary<string, object> DeserializeObject(string json)
        {
            Dictionary<string, object> data = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
            if (data == null)
            {
                throw new Exception("failed to deserialize: " + json);
            }
            return data;
        }

        /// <summary>
        /// Invokes the exact public API:
        /// public static bool TryMigrateSchemaV3Request(string schemaV3Json, out string schemaV4Json, out string error)
        /// via reflection so this RED suite compiles before production green lands.
        /// </summary>
        static bool InvokeTryMigrateSchemaV3Request(
            string schemaV3Json,
            out string schemaV4Json,
            out string error)
        {
            schemaV4Json = null;
            error = null;
            MethodInfo method = typeof(LlmBrokerProtocol).GetMethod(
                "TryMigrateSchemaV3Request",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[]
                {
                    typeof(string),
                    typeof(string).MakeByRefType(),
                    typeof(string).MakeByRefType(),
                },
                null);
            AssertTrue(
                method != null,
                "public static bool TryMigrateSchemaV3Request(string schemaV3Json, out string schemaV4Json, out string error) required");
            AssertEqual(typeof(bool), method.ReturnType, "TryMigrateSchemaV3Request returns bool");

            object[] args = new object[] { schemaV3Json, null, null };
            object raw = method.Invoke(null, args);
            schemaV4Json = args[1] as string;
            error = args[2] as string;
            return raw is bool && (bool)raw;
        }

        static string BuildSchemaV4ResponseJson(
            ulong runEffectSeq,
            int actionId,
            long[] historyEventIdsUsed,
            string opponentActionAssessment)
        {
            System.Text.StringBuilder ids = new System.Text.StringBuilder();
            ids.Append('[');
            for (int i = 0; i < historyEventIdsUsed.Length; i++)
            {
                if (i > 0)
                {
                    ids.Append(',');
                }
                ids.Append(historyEventIdsUsed[i]);
            }
            ids.Append(']');
            return
                "{" +
                "\"run_effect_seq\":" + runEffectSeq + "," +
                "\"action_id\":" + actionId + "," +
                "\"reason\":\"Normal Summon Public Monster to develop from history.\"," +
                "\"confidence\":0.8," +
                "\"plan\":\"Develop using public events.\"," +
                "\"why_now\":\"Main Phase is open.\"," +
                "\"alternatives_considered\":[\"Ending phase gives up tempo.\"]," +
                "\"risk\":\"Opponent may answer.\"," +
                "\"opponent_board_assessment\":\"Public threats noted.\"," +
                "\"opponent_action_assessment\":\"" + opponentActionAssessment + "\"," +
                "\"history_event_ids_used\":" + ids +
                "}";
        }

        static void AssertParseOrValidateHistoryCitationError(
            DecisionSnapshot snapshot,
            string historyEventIdsUsedJsonArray,
            string exactFamily,
            string familyToken)
        {
            string json =
                "{" +
                "\"run_effect_seq\":" + snapshot.RunEffectSeq + "," +
                "\"action_id\":0," +
                "\"reason\":\"Normal Summon Public Monster to develop from history.\"," +
                "\"confidence\":0.8," +
                "\"plan\":\"Develop using public events.\"," +
                "\"why_now\":\"Main Phase is open.\"," +
                "\"alternatives_considered\":[\"Ending phase gives up tempo.\"]," +
                "\"risk\":\"Opponent may answer.\"," +
                "\"opponent_board_assessment\":\"Public threats noted.\"," +
                "\"opponent_action_assessment\":\"Opponent public history informed the choice.\"," +
                "\"history_event_ids_used\":" + historyEventIdsUsedJsonArray +
                "}";

            LlmBrokerDecisionResponse response;
            string parseError;
            bool parsed = LlmBrokerProtocol.TryParseDecisionResponse(json, out response, out parseError);
            if (!parsed)
            {
                AssertErrorFamily(parseError, exactFamily, familyToken);
                return;
            }

            LlmBrokerValidationResult result = LlmBrokerProtocol.ValidateResponse(snapshot, response);
            AssertFalse(
                result.IsValid,
                "history_event_ids_used " + historyEventIdsUsedJsonArray + " must fail validate");
            AssertErrorFamily(result.Error, exactFamily, familyToken);
        }

        static void AssertErrorFamily(string error, string exactFamily, string familyToken)
        {
            AssertTrue(!string.IsNullOrEmpty(error), "error present for family " + exactFamily);
            string lowered = error.ToLowerInvariant();
            bool matchesExact = string.Equals(error, exactFamily, StringComparison.OrdinalIgnoreCase);
            bool matchesToken = lowered.IndexOf(familyToken.ToLowerInvariant(), StringComparison.Ordinal) >= 0;
            AssertTrue(
                matchesExact || matchesToken,
                "error family " + exactFamily + " (token '" + familyToken + "'): got " + error);
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value)
            {
                throw new Exception(message);
            }
        }

        static void AssertFalse(bool value, string message)
        {
            if (value)
            {
                throw new Exception(message);
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + ": expected " + expected + ", got " + actual);
            }
        }
    }
}

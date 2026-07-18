using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-004 Slice 5 RED: audit serialization of full public history,
    /// request/decision_window compaction metadata, and preservation of schema-v4
    /// opponent_action_assessment + history_event_ids_used on broker response/commit/recovery.
    /// Tests-only — production audit serializers remain incomplete until GREEN.
    /// </summary>
    static class Llm004Slice5Tests
    {
        const string SentinelOpponentHandName = "SENTINEL_OPP_HAND_LEAK_99501";
        const string SentinelSetName = "SENTINEL_FACEDOWN_SET_LEAK_99502";
        const int SentinelCardId = 99502991;

        public static void RunAll()
        {
            DecisionWindowAuditIncludesCompactionMetadataFromAttachedHistory();
            RequestProjectionAndDecisionWindowShareDuelHistoryShape();
            FullPublicEventsRemainInspectableInAuditSerialization();
            PublicEventAuditAndProjectionsExposeDuelGeneration();
            LiveTryIngestForAuditEmitsTrackerDuelGeneration();
            DuelGenerationSurvivesLifecycleResetOnProjectionsAndAudit();
            BrokerResponseAuditPreservesAssessmentAndCitationsIncludingEmptyList();
            BrokerCommittedAuditPreservesAssessmentAndCitationsIncludingEmptyList();
            BrokerRecoveredAuditPreservesAssessmentAndCitationsIncludingEmptyList();
            AuditSinksNeverEmitSentinelHiddenIdentitiesFromSetEvents();
        }

        static void DecisionWindowAuditIncludesCompactionMetadataFromAttachedHistory()
        {
            DecisionSnapshot snapshot = CreateStrategicSnapshot(controlledPlayer: 1, runEffectSeq: 500);
            snapshot.Turn = 3;
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 3,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 100, "OppOld");
            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 100, "OppOld");
            AppendPublic(tracker, 3, 2, 1, LlmPublicDuelEventKind.NormalSummon, 200, "OwnPrev");
            AppendPublic(tracker, 4, 3, 1, LlmPublicDuelEventKind.ActivateEffect, 200, "OwnCurr");
            AppendPublic(tracker, 5, 3, 1, LlmPublicDuelEventKind.NormalSummon, 201, "OwnCurrB");
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);

            Dictionary<string, object> window = DeserializeObject(
                LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot));
            AssertEqual((long)4, window["schema_version"], "decision_window schema_version");
            AssertTrue(window.ContainsKey("duel_history"), "decision_window has duel_history");

            Dictionary<string, object> history = (Dictionary<string, object>)window["duel_history"];
            AssertTrue(history.ContainsKey("history_compacted"), "history_compacted");
            AssertTrue(history.ContainsKey("first_detailed_event_id"), "first_detailed_event_id");
            AssertTrue(history.ContainsKey("last_event_id"), "last_event_id");
            AssertTrue(history.ContainsKey("budget_status"), "budget_status");
            AssertTrue(history.ContainsKey("prior_turn_summaries"), "prior_turn_summaries");
            AssertTrue(history.ContainsKey("events"), "events");
            AssertTrue(history.ContainsKey("revealed_card_context"), "revealed_card_context");
            AssertEqual(true, history["history_compacted"], "fixture is compacted");
            AssertTrue(
                ((List<object>)history["prior_turn_summaries"]).Count >= 1,
                "compacted summaries present in audit");
        }

        static void RequestProjectionAndDecisionWindowShareDuelHistoryShape()
        {
            DecisionSnapshot snapshot = CreateStrategicSnapshot(1, 501);
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 10, 1, 0, LlmPublicDuelEventKind.NormalSummon, 11, "Public A");
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);

            Dictionary<string, object> request = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));
            Dictionary<string, object> window = DeserializeObject(
                LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot));

            string requestHistory = MiniJSON.Json.Serialize(request["duel_history"]);
            string windowHistory = MiniJSON.Json.Serialize(window["duel_history"]);
            AssertEqual(requestHistory, windowHistory, "request and decision_window duel_history identical");
        }

        static void FullPublicEventsRemainInspectableInAuditSerialization()
        {
            LlmPublicDuelEvent evt = new LlmPublicDuelEvent()
            {
                EventId = 18,
                RunEffectSeq = 126,
                Turn = 2,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = 0,
                TargetPlayer = -1,
                CardPlayer = -1,
                Kind = LlmPublicDuelEventKind.NormalSummon,
                CardId = 1234,
                CardName = "Public Monster",
                SourceZone = "hand",
                DestinationZone = "monster_zone",
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
                SourceSignature = "slice5_audit_18",
            };

            Dictionary<string, object> audit = DeserializeObject(
                LlmDecisionLogSerializer.SerializePublicDuelEvent(evt));
            AssertEqual("llm_public_duel_event", audit["kind"], "audit envelope kind");
            AssertEqual((long)18, audit["event_id"], "event_id");
            AssertEqual((long)126, audit["run_effect_seq"], "run_effect_seq");
            AssertEqual((long)0, audit["actor_player"], "actor_player");
            // Envelope kind is llm_public_duel_event; history kind must remain separately inspectable.
            AssertTrue(
                audit.ContainsKey("public_event_kind"),
                "public_event_kind required so normal_summon remains inspectable after envelope kind overwrite");
            AssertEqual("normal_summon", Convert.ToString(audit["public_event_kind"]), "public_event_kind");
            AssertEqual("accepted_command", Convert.ToString(audit["evidence"]), "evidence");
            AssertEqual((long)1234, Convert.ToInt64(audit["card_id"]), "public card_id");
            AssertEqual("Public Monster", Convert.ToString(audit["card_name"]), "public card_name");
            AssertEqual("hand", Convert.ToString(audit["source_zone"]), "source_zone");
            AssertEqual("monster_zone", Convert.ToString(audit["destination_zone"]), "destination_zone");
            // Multi-duel analyzers require explicit generation on the audit surface.
            AssertTrue(audit.ContainsKey("duel_generation"), "public event audit has duel_generation");
            AssertEqual((long)0, Convert.ToInt64(audit["duel_generation"]), "default generation");
        }

        static void PublicEventAuditAndProjectionsExposeDuelGeneration()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            tracker.ResetForNewDuel(4);
            AssertEqual(4, tracker.DuelGeneration, "tracker generation after reset");

            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 11, "Gen4 Public");
            DecisionSnapshot snapshot = CreateStrategicSnapshot(1, 510);
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);

            // Immutable projection must carry duel_generation so request/window audits do not guess.
            AssertTrue(
                snapshot.DuelHistory != null,
                "history attached");
            // Contract: LlmDuelHistoryState / projection exposes generation used by serializers.
            Dictionary<string, object> request = DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(snapshot));
            Dictionary<string, object> window = DeserializeObject(
                LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot));
            Dictionary<string, object> reqHistory = (Dictionary<string, object>)request["duel_history"];
            Dictionary<string, object> winHistory = (Dictionary<string, object>)window["duel_history"];
            AssertTrue(reqHistory.ContainsKey("duel_generation"), "request duel_history.duel_generation");
            AssertTrue(winHistory.ContainsKey("duel_generation"), "window duel_history.duel_generation");
            AssertEqual((long)4, Convert.ToInt64(reqHistory["duel_generation"]), "request generation");
            AssertEqual((long)4, Convert.ToInt64(winHistory["duel_generation"]), "window generation");

            LlmPublicDuelEvent evt = snapshot.DuelHistory.Events[0];
            Dictionary<string, object> eventAudit = DeserializeObject(
                LlmDecisionLogSerializer.SerializePublicDuelEvent(evt));
            // Full public-event audit uses the same generation as the projection.
            AssertTrue(eventAudit.ContainsKey("duel_generation"), "event audit duel_generation");
            AssertEqual((long)4, Convert.ToInt64(eventAudit["duel_generation"]), "event audit generation");
        }

        /// <summary>
        /// Live production path: TryIngestForAudit must emit duel_generation from the tracker
        /// after ResetForNewDuel, not leave generation 0 on the pre-append event object.
        /// </summary>
        static void LiveTryIngestForAuditEmitsTrackerDuelGeneration()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            tracker.ResetForNewDuel(4);

            LlmPublicDuelEvent publicEvent = new LlmPublicDuelEvent()
            {
                EventId = 1,
                RunEffectSeq = 900,
                Turn = 1,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = 0,
                TargetPlayer = -1,
                CardPlayer = -1,
                Kind = LlmPublicDuelEventKind.NormalSummon,
                CardId = 42,
                CardName = "Live Audit Public",
                DestinationZone = "monster_zone",
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
                SourceSignature = "live_ingest_gen4_1",
                // Intentionally leave DuelGeneration at default 0 — production must stamp tracker gen.
            };
            AssertEqual(0, publicEvent.DuelGeneration, "pre-ingest generation unset");

            string error;
            string auditJson;
            bool newlyAppended = LlmPublicDuelEventClientIngest.TryIngestForAudit(
                tracker,
                publicEvent,
                out error,
                out auditJson);

            AssertTrue(newlyAppended, "first ingest newly appended: " + error);
            AssertTrue(!string.IsNullOrEmpty(auditJson), "auditJson emitted");
            Dictionary<string, object> audit = DeserializeObject(auditJson);
            AssertEqual("llm_public_duel_event", audit["kind"], "envelope kind");
            AssertEqual("normal_summon", Convert.ToString(audit["public_event_kind"]), "public_event_kind");
            AssertEqual((long)1, Convert.ToInt64(audit["event_id"]), "event_id");
            AssertTrue(audit.ContainsKey("duel_generation"), "live audit has duel_generation");
            AssertEqual(
                (long)4,
                Convert.ToInt64(audit["duel_generation"]),
                "live TryIngestForAudit auditJson must use tracker generation after ResetForNewDuel(4)");

            // Stored projection also frozen at gen 4.
            LlmDuelHistoryState snap = tracker.CreateSnapshot();
            AssertEqual(4, snap.DuelGeneration, "snapshot generation");
            AssertEqual(4, snap.Events[0].DuelGeneration, "stored event generation");

            // Duplicate delivery: no second audit line.
            string error2;
            string auditJson2;
            bool again = LlmPublicDuelEventClientIngest.TryIngestForAudit(
                tracker,
                publicEvent,
                out error2,
                out auditJson2);
            AssertFalse(again, "duplicate ingest not newly appended");
            AssertTrue(string.IsNullOrEmpty(auditJson2), "duplicate ingest emits no auditJson");

            // Rejected invalid event (event_id 0): no audit line.
            LlmPublicDuelEvent rejected = new LlmPublicDuelEvent()
            {
                EventId = 0,
                RunEffectSeq = 901,
                Kind = LlmPublicDuelEventKind.ActivateEffect,
                SourceSignature = "live_ingest_reject_0",
            };
            string error3;
            string auditJson3;
            bool rejectedOk = LlmPublicDuelEventClientIngest.TryIngestForAudit(
                tracker,
                rejected,
                out error3,
                out auditJson3);
            AssertFalse(rejectedOk, "invalid event_id rejected");
            AssertTrue(string.IsNullOrEmpty(auditJson3), "rejected ingest emits no auditJson");
        }

        static void DuelGenerationSurvivesLifecycleResetOnProjectionsAndAudit()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            tracker.ResetForNewDuel(1);
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 1, "Gen1");
            DecisionSnapshot gen1 = CreateStrategicSnapshot(1, 511);
            LlmDecisionSnapshotHistory.Attach(gen1, tracker);
            Dictionary<string, object> h1 = (Dictionary<string, object>)DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(gen1))["duel_history"];
            AssertEqual((long)1, Convert.ToInt64(h1["duel_generation"]), "gen1 projection");

            // Lifecycle reset restarts event ids; generation must advance and appear on new surfaces.
            int next = LlmDuelHistoryLifecycle.ResetForBoundary(tracker, tracker.DuelGeneration);
            AssertEqual(2, next, "lifecycle next generation");
            AssertEqual(2, tracker.DuelGeneration, "tracker after boundary");
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 2, "Gen2");
            DecisionSnapshot gen2 = CreateStrategicSnapshot(1, 512);
            LlmDecisionSnapshotHistory.Attach(gen2, tracker);
            Dictionary<string, object> h2 = (Dictionary<string, object>)DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(gen2))["duel_history"];
            AssertEqual((long)2, Convert.ToInt64(h2["duel_generation"]), "gen2 projection");
            AssertEqual(1, ((List<object>)h2["events"]).Count, "gen2 has restarted event stream");
            AssertEqual(
                (long)1,
                Convert.ToInt64(((Dictionary<string, object>)((List<object>)h2["events"])[0])["event_id"]),
                "event ids restart at 1 after generation boundary");

            // Prior generation snapshot remains frozen and still reports gen1.
            Dictionary<string, object> h1Again = (Dictionary<string, object>)DeserializeObject(
                LlmBrokerProtocol.SerializeDecisionRequest(gen1))["duel_history"];
            AssertEqual((long)1, Convert.ToInt64(h1Again["duel_generation"]), "frozen gen1 unchanged");
        }

        static void BrokerResponseAuditPreservesAssessmentAndCitationsIncludingEmptyList()
        {
            DecisionSnapshot snapshot = CreateStrategicSnapshot(1, 502);
            LlmBrokerDecisionResponse response = BuildV4Response(
                502,
                0,
                "Opponent used public events 18 and 20.",
                new List<long>() { 18, 20 });
            LlmBrokerDecisionResult success = LlmBrokerDecisionResult.Success(
                "{\"schema_version\":4,\"duel_history\":{}}",
                "{\"action_id\":0,\"history_event_ids_used\":[18,20]}",
                response,
                snapshot.LegalActions[0],
                120);

            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerResponse(502, 502, success));

            AssertEqual("llm_broker_response", data["kind"], "kind");
            AssertEqual(true, data["success"], "success");
            AssertTrue(data.ContainsKey("opponent_action_assessment"), "response has opponent_action_assessment");
            AssertEqual(
                "Opponent used public events 18 and 20.",
                Convert.ToString(data["opponent_action_assessment"]),
                "assessment preserved");
            AssertTrue(data.ContainsKey("history_event_ids_used"), "response has history_event_ids_used");
            List<object> ids = (List<object>)data["history_event_ids_used"];
            AssertEqual(2, ids.Count, "citation count");
            AssertEqual(18L, Convert.ToInt64(ids[0]), "citation[0]");
            AssertEqual(20L, Convert.ToInt64(ids[1]), "citation[1]");

            // Explicit empty list must remain present (not omitted).
            response.HistoryEventIdsUsed = new List<long>();
            response.OpponentActionAssessment = "No material events cited.";
            data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerResponse(
                    502,
                    502,
                    LlmBrokerDecisionResult.Success("{}", "{}", response, snapshot.LegalActions[0])));
            AssertTrue(data.ContainsKey("history_event_ids_used"), "empty list key present");
            AssertEqual(0, ((List<object>)data["history_event_ids_used"]).Count, "empty list preserved");
            AssertEqual(
                "No material events cited.",
                Convert.ToString(data["opponent_action_assessment"]),
                "empty-citation assessment preserved");
        }

        static void BrokerCommittedAuditPreservesAssessmentAndCitationsIncludingEmptyList()
        {
            DecisionSnapshot snapshot = CreateStrategicSnapshot(1, 503);
            LlmBrokerDecisionResponse response = BuildV4Response(
                503,
                0,
                "Committed with history citations.",
                new List<long>() { 18 });

            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerCommittedAction(
                    503,
                    504,
                    response,
                    snapshot.LegalActions[0]));

            AssertEqual("llm_broker_committed", data["kind"], "kind");
            AssertTrue(data.ContainsKey("opponent_action_assessment"), "committed has assessment");
            AssertEqual(
                "Committed with history citations.",
                Convert.ToString(data["opponent_action_assessment"]),
                "committed assessment");
            AssertTrue(data.ContainsKey("history_event_ids_used"), "committed has citations");
            AssertEqual(1, ((List<object>)data["history_event_ids_used"]).Count, "committed citation count");
            AssertEqual(18L, Convert.ToInt64(((List<object>)data["history_event_ids_used"])[0]), "committed citation");

            response.HistoryEventIdsUsed = new List<long>();
            data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerCommittedAction(
                    503,
                    504,
                    response,
                    snapshot.LegalActions[0]));
            AssertTrue(data.ContainsKey("history_event_ids_used"), "committed empty list present");
            AssertEqual(0, ((List<object>)data["history_event_ids_used"]).Count, "committed empty list");
        }

        static void BrokerRecoveredAuditPreservesAssessmentAndCitationsIncludingEmptyList()
        {
            DecisionSnapshot snapshot = CreateStrategicSnapshot(1, 505);
            LlmBrokerDecisionResponse response = BuildV4Response(
                505,
                0,
                "Recovered after quality soft-fail.",
                new List<long>() { 20, 21 });

            Dictionary<string, object> data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerRecoveredAction(
                    505,
                    506,
                    "early_end_phase",
                    "reuse_provider_intent",
                    snapshot.LegalActions[0],
                    response));

            AssertEqual("llm_broker_recovered", data["kind"], "kind");
            AssertTrue(data.ContainsKey("opponent_action_assessment"), "recovered has assessment");
            AssertEqual(
                "Recovered after quality soft-fail.",
                Convert.ToString(data["opponent_action_assessment"]),
                "recovered assessment");
            AssertTrue(data.ContainsKey("history_event_ids_used"), "recovered has citations");
            List<object> ids = (List<object>)data["history_event_ids_used"];
            AssertEqual(2, ids.Count, "recovered citation count");
            AssertEqual(20L, Convert.ToInt64(ids[0]), "recovered[0]");
            AssertEqual(21L, Convert.ToInt64(ids[1]), "recovered[1]");

            response.HistoryEventIdsUsed = new List<long>();
            data = DeserializeObject(
                LlmDecisionLogSerializer.SerializeBrokerRecoveredAction(
                    505,
                    506,
                    "early_end_phase",
                    "reuse_provider_intent",
                    snapshot.LegalActions[0],
                    response));
            AssertTrue(data.ContainsKey("history_event_ids_used"), "recovered empty list present");
            AssertEqual(0, ((List<object>)data["history_event_ids_used"]).Count, "recovered empty list");
        }

        static void AuditSinksNeverEmitSentinelHiddenIdentitiesFromSetEvents()
        {
            LlmPublicDuelEvent setEvt = new LlmPublicDuelEvent()
            {
                EventId = 7,
                RunEffectSeq = 7,
                Turn = 1,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = 0,
                Kind = LlmPublicDuelEventKind.SetMonster,
                CardId = SentinelCardId,
                CardName = SentinelSetName,
                DestinationZone = "monster_zone",
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
                SourceSignature = "slice5_set",
            };

            string eventAudit = LlmDecisionLogSerializer.SerializePublicDuelEvent(setEvt);
            DecisionSnapshot snapshot = CreateStrategicSnapshot(1, 507);
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AssertTrue(tracker.TryAppendPublicEvent(setEvt).NewlyAppended, "append set");
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);
            string window = LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot);
            string request = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);

            string[] sinks = new[] { eventAudit, window, request };
            foreach (string sink in sinks)
            {
                AssertFalse(sink.Contains(SentinelSetName), "no set sentinel name");
                AssertFalse(sink.Contains(SentinelOpponentHandName), "no hand sentinel");
                AssertFalse(sink.Contains(SentinelCardId.ToString()), "no sentinel card id");
            }
        }

        // ------------------------------------------------------------------

        static LlmBrokerDecisionResponse BuildV4Response(
            ulong runEffectSeq,
            int actionId,
            string opponentActionAssessment,
            List<long> historyEventIdsUsed)
        {
            return new LlmBrokerDecisionResponse()
            {
                RunEffectSeq = runEffectSeq,
                ActionId = actionId,
                Reason = "Normal Summon Public Monster grounded in history.",
                Confidence = 0.8,
                Plan = "Develop using public events.",
                WhyNow = "Main Phase is open.",
                AlternativesConsidered = new List<string>() { "Ending phase gives up tempo." },
                Risk = "Opponent may answer.",
                OpponentBoardAssessment = "Public threats noted.",
                OpponentActionAssessment = opponentActionAssessment,
                HistoryEventIdsUsed = historyEventIdsUsed ?? new List<long>(),
            };
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
            });
            return snapshot;
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
                DestinationZone = "monster_zone",
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
                SourceSignature = "slice5_evt_" + eventId,
            };
            LlmDuelHistoryAppendResult result = tracker.TryAppendPublicEvent(evt);
            if (!result.Accepted || !result.NewlyAppended)
            {
                throw new Exception("append failed: " + (result.Error ?? "not newly appended"));
            }
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

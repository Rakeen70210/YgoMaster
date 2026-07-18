using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-004 Slice 2 (+ remediation): immutable request-projection attachment,
    /// strict byte budget, deep immutability, detailed-only revealed context, deterministic summaries.
    /// </summary>
    static class Llm004Slice2Tests
    {
        const string SentinelSetName = "SENTINEL_SLICE2_SET_LEAK_88221";
        const int SentinelSetCardId = 88221001;

        public static void RunAll()
        {
            FullSnapshotIsImmutableAndDetachedFromTracker();
            SnapshotEventFieldMutationDoesNotAffectSubsequentReads();
            SnapshotNestedSummaryMutationDoesNotAffectSubsequentReads();
            RequestProjectionCompactsByEventCountLimit();
            RequestProjectionMeetsByteLimitWhenProtectedDetailFits();
            RequestProjectionReportsBudgetMetadataWhenProtectedDetailCannotFit();
            RequestProjectionIsDeterministicAcrossRepeatedCalls();
            SameEventStreamDifferentInsertionPathsSerializeIdentically();
            CompactionPreservesCurrentAndImmediatelyPrecedingTurn();
            CompactionReportsEventIdRangesAndStructuredCards();
            FullHistoryUnchangedAfterRequestProjection();
            RevealedCardContextDedupesPublicIdentitiesDeterministically();
            ProjectionRevealedContextOnlyIncludesDetailedEvents();
            CraftedSetIdentityNeverEntersRevealedCardContextOrProjection();
            DecisionSnapshotAttachesRequestProjectionNotFullHistory();
            SameAttachedSnapshotPreservedWhenSnapshotReused();
            NewlyExtractedAttachmentSeesCurrentHistory();
            BrokerRequestJsonIncludesSchemaV4DuelHistoryField();
        }

        static void FullSnapshotIsImmutableAndDetachedFromTracker()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 100, "A");
            LlmDuelHistoryState snap = tracker.CreateSnapshot();
            AssertEqual(1, snap.Events.Count, "snapshot event count");
            AssertEqual(false, snap.HistoryCompacted, "full snapshot not compacted");

            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 100, "A");
            AssertEqual(1, snap.Events.Count, "snapshot detached from later appends");
            AssertEqual(2, tracker.CreateSnapshot().Events.Count, "tracker has both events");

            try
            {
                snap.Events.Add(new LlmPublicDuelEvent() { EventId = 99 });
                throw new Exception("snapshot Events must be read-only");
            }
            catch (NotSupportedException)
            {
            }
            catch (Exception e)
            {
                if (e.Message == "snapshot Events must be read-only")
                {
                    throw;
                }
            }
            AssertEqual(1, snap.Events.Count, "read-only Events not mutated");
        }

        static void SnapshotEventFieldMutationDoesNotAffectSubsequentReads()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 100, "Alpha");
            LlmDuelHistoryState snap = tracker.CreateSnapshot();
            LlmPublicDuelEvent first = snap.Events[0];
            first.CardName = "MUTATED";
            first.CardId = 999999;
            first.EventId = 777;
            AssertEqual("Alpha", snap.Events[0].CardName, "event name deep-immutable");
            AssertEqual(100, snap.Events[0].CardId.Value, "event id deep-immutable");
            AssertEqual((ulong)1, snap.Events[0].EventId, "event_id deep-immutable");
        }

        static void SnapshotNestedSummaryMutationDoesNotAffectSubsequentReads()
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 2,
                MaxSerializedBytes = 1024 * 1024,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 11, "A");
            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 12, "B");
            AppendPublic(tracker, 3, 2, 1, LlmPublicDuelEventKind.NormalSummon, 13, "C");
            AppendPublic(tracker, 4, 3, 0, LlmPublicDuelEventKind.NormalSummon, 14, "D");

            LlmDuelHistoryState projection = tracker.CreateRequestProjection();
            AssertTrue(projection.PriorTurnSummaries.Count > 0, "has summaries for nested mutation test");
            Dictionary<string, object> summary = projection.PriorTurnSummaries[0];
            summary["turn"] = 999;
            IList kindCounts = summary["kind_counts"] as IList;
            AssertTrue(kindCounts != null && kindCounts.Count > 0, "kind_counts list present");
            Dictionary<string, object> firstKind = kindCounts[0] as Dictionary<string, object>;
            AssertTrue(firstKind != null, "kind entry dict");
            firstKind["count"] = 9999;
            IList cards = summary["cards"] as IList;
            if (cards != null && cards.Count > 0)
            {
                Dictionary<string, object> card0 = cards[0] as Dictionary<string, object>;
                if (card0 != null)
                {
                    card0["name"] = "MUTATED_CARD";
                }
            }

            Dictionary<string, object> again = projection.PriorTurnSummaries[0];
            AssertEqual(true, Convert.ToInt32(again["turn"]) != 999, "summary turn not mutated");
            IList kindAgain = again["kind_counts"] as IList;
            Dictionary<string, object> kind0 = kindAgain[0] as Dictionary<string, object>;
            AssertEqual(true, Convert.ToInt32(kind0["count"]) != 9999, "nested kind count not mutated");
        }

        static void RequestProjectionCompactsByEventCountLimit()
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 4,
                MaxSerializedBytes = 1024 * 1024,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            ulong id = 1;
            for (int turn = 1; turn <= 4; turn++)
            {
                for (int i = 0; i < 3; i++)
                {
                    AppendPublic(tracker, id++, turn, turn % 2, LlmPublicDuelEventKind.NormalSummon, 1000 + turn, "T" + turn);
                }
            }

            LlmDuelHistoryState projection = tracker.CreateRequestProjection(controlledPlayer: 1);
            AssertTrue(projection.HistoryCompacted, "event-count projection compacted");
            AssertTrue(projection.PriorTurnSummaries.Count > 0, "has turn summaries");
            AssertTrue(projection.Events.Count <= options.MaxDetailedEvents ||
                AllEventsOnProtectedTurns(projection, 4, 3),
                "detailed within limit or only protected turns remain");
            AssertTrue(projection.FirstDetailedEventId >= 1, "first_detailed_event_id set");
        }

        static void RequestProjectionMeetsByteLimitWhenProtectedDetailFits()
        {
            // Compactable old turns: many long-name detailed events.
            // After turn summaries (deduped cards) + small protected turns, must fit.
            // Compacted size is ~2.6KiB with this fixture; uncompacted is much larger.
            const int maxBytes = 2800;
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 1000,
                MaxSerializedBytes = maxBytes,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            ulong id = 1;
            string bulky = new string('X', 60);
            for (int turn = 1; turn <= 3; turn++)
            {
                for (int i = 0; i < 15; i++)
                {
                    AppendPublic(
                        tracker,
                        id++,
                        turn,
                        0,
                        LlmPublicDuelEventKind.ActivateEffect,
                        100 + turn,
                        "OldT" + turn + bulky);
                }
            }
            AppendPublic(tracker, id++, 4, 1, LlmPublicDuelEventKind.NormalSummon, 1, "P");
            AppendPublic(tracker, id++, 4, 0, LlmPublicDuelEventKind.ActivateEffect, 2, "Q");
            AppendPublic(tracker, id++, 5, 1, LlmPublicDuelEventKind.NormalSummon, 3, "R");
            AppendPublic(tracker, id++, 5, 0, LlmPublicDuelEventKind.AttackDeclare, 4, "S");

            LlmDuelHistoryState projection = tracker.CreateRequestProjection(controlledPlayer: 1);
            string json = LlmDuelHistoryTracker.SerializeProjectionJson(projection);
            int bytes = Encoding.UTF8.GetByteCount(json);

            // Uncompacted full-detail projection of same events (no byte limit) exceeds budget.
            LlmDuelHistoryTracker fat = new LlmDuelHistoryTracker(new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 100000,
                MaxSerializedBytes = 50 * 1024 * 1024,
            });
            id = 1;
            for (int turn = 1; turn <= 3; turn++)
            {
                for (int i = 0; i < 15; i++)
                {
                    AppendPublic(fat, id++, turn, 0, LlmPublicDuelEventKind.ActivateEffect, 100 + turn, "OldT" + turn + bulky);
                }
            }
            AppendPublic(fat, id++, 4, 1, LlmPublicDuelEventKind.NormalSummon, 1, "P");
            AppendPublic(fat, id++, 4, 0, LlmPublicDuelEventKind.ActivateEffect, 2, "Q");
            AppendPublic(fat, id++, 5, 1, LlmPublicDuelEventKind.NormalSummon, 3, "R");
            AppendPublic(fat, id++, 5, 0, LlmPublicDuelEventKind.AttackDeclare, 4, "S");
            int uncompactedBytes = Encoding.UTF8.GetByteCount(
                LlmDuelHistoryTracker.SerializeProjectionJson(fat.CreateSnapshot()));
            AssertTrue(uncompactedBytes > maxBytes,
                "fixture uncompacted exceeds budget (" + uncompactedBytes + ")");

            AssertTrue(projection.HistoryCompacted, "old turns compacted for byte budget");
            AssertEqual("ok", projection.BudgetStatus,
                "budget status ok when protected fits (bytes=" + bytes + ", reason=" + projection.BudgetReason + ")");
            AssertTrue(bytes <= maxBytes,
                "serialized projection <= MaxSerializedBytes (got " + bytes + ")");
            AssertTrue(CountEventsOnTurns(projection, 4, 5) >= 4, "protected turns retained");
        }

        static void RequestProjectionReportsBudgetMetadataWhenProtectedDetailCannotFit()
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 1000,
                MaxSerializedBytes = 200,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            string huge = new string('Z', 300);
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 1, "HugePrev" + huge);
            AppendPublic(tracker, 2, 1, 1, LlmPublicDuelEventKind.ActivateEffect, 2, "HugePrev2" + huge);
            AppendPublic(tracker, 3, 2, 0, LlmPublicDuelEventKind.NormalSummon, 3, "HugeCur" + huge);
            AppendPublic(tracker, 4, 2, 1, LlmPublicDuelEventKind.ActivateEffect, 4, "HugeCur2" + huge);

            LlmDuelHistoryState projection = tracker.CreateRequestProjection(1);
            AssertEqual("over_budget_protected_detail", projection.BudgetStatus, "explicit over-budget status");
            AssertTrue(
                !string.IsNullOrEmpty(projection.BudgetReason) &&
                projection.BudgetReason.Contains("protected"),
                "budget reason explains protected detail");
            // Protected detail preserved (not silently dropped).
            AssertTrue(projection.Events.Count >= 2, "protected events still present");
            string json = LlmDuelHistoryTracker.SerializeProjectionJson(projection);
            AssertTrue(json.Contains("over_budget_protected_detail"), "json exposes budget_status");
            AssertTrue(json.Contains("budget_reason"), "json exposes budget_reason");
        }

        static void RequestProjectionIsDeterministicAcrossRepeatedCalls()
        {
            LlmDuelHistoryTracker tracker = BuildMultiTurnTracker(5, 4);
            string a = tracker.SerializeRequestProjection(0);
            string b = tracker.SerializeRequestProjection(0);
            AssertEqual(a, b, "request projection deterministic");
            LlmDuelHistoryState s1 = tracker.CreateRequestProjection(0);
            LlmDuelHistoryState s2 = tracker.CreateRequestProjection(0);
            AssertEqual(s1.Events.Count, s2.Events.Count, "event count stable");
            AssertEqual(s1.PriorTurnSummaries.Count, s2.PriorTurnSummaries.Count, "summary count stable");
            AssertEqual(s1.FirstDetailedEventId, s2.FirstDetailedEventId, "first_detailed stable");
        }

        static void SameEventStreamDifferentInsertionPathsSerializeIdentically()
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 3,
                MaxSerializedBytes = 1024 * 1024,
            };

            LlmDuelHistoryTracker pathA = new LlmDuelHistoryTracker(options);
            AppendPublic(pathA, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 50, "Alpha");
            AppendPublic(pathA, 2, 1, 1, LlmPublicDuelEventKind.ActivateEffect, 40, "Beta");
            AppendPublic(pathA, 3, 1, 0, LlmPublicDuelEventKind.AttackDeclare, 60, "Gamma");
            AppendPublic(pathA, 4, 2, 1, LlmPublicDuelEventKind.NormalSummon, 50, "Alpha");
            AppendPublic(pathA, 5, 3, 0, LlmPublicDuelEventKind.NormalSummon, 70, "Delta");

            // Same ordered stream via identical append sequence on a second tracker.
            LlmDuelHistoryTracker pathB = new LlmDuelHistoryTracker(options);
            LlmPublicDuelEvent[] stream = new[]
            {
                MakeEvent(1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 50, "Alpha"),
                MakeEvent(2, 1, 1, LlmPublicDuelEventKind.ActivateEffect, 40, "Beta"),
                MakeEvent(3, 1, 0, LlmPublicDuelEventKind.AttackDeclare, 60, "Gamma"),
                MakeEvent(4, 2, 1, LlmPublicDuelEventKind.NormalSummon, 50, "Alpha"),
                MakeEvent(5, 3, 0, LlmPublicDuelEventKind.NormalSummon, 70, "Delta"),
            };
            foreach (LlmPublicDuelEvent evt in stream)
            {
                AssertTrue(pathB.TryAppendPublicEvent(evt).NewlyAppended, "pathB append");
            }

            AssertEqual(
                pathA.SerializeRequestProjection(1),
                pathB.SerializeRequestProjection(1),
                "identical ordered streams serialize identically");
        }

        static void CompactionPreservesCurrentAndImmediatelyPrecedingTurn()
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 3,
                MaxSerializedBytes = 1024 * 1024,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            ulong id = 1;
            for (int turn = 1; turn <= 4; turn++)
            {
                AppendPublic(tracker, id++, turn, 0, LlmPublicDuelEventKind.NormalSummon, 10 + turn, "C" + turn);
                AppendPublic(tracker, id++, turn, 1, LlmPublicDuelEventKind.ActivateEffect, 20 + turn, "O" + turn);
            }

            LlmDuelHistoryState projection = tracker.CreateRequestProjection(controlledPlayer: 1);
            AssertTrue(CountEventsOnTurns(projection, 4) >= 2, "current turn fully detailed");
            AssertTrue(CountEventsOnTurns(projection, 3) >= 2, "previous turn fully detailed");
            AssertTrue(projection.HistoryCompacted, "old turns compacted under tight event limit");
            bool foundOldSummary = false;
            foreach (Dictionary<string, object> summary in projection.PriorTurnSummaries)
            {
                int turn = Convert.ToInt32(summary["turn"]);
                if (turn <= 2)
                {
                    foundOldSummary = true;
                }
                AssertTrue(turn < 3, "summaries only for turns older than previous");
            }
            AssertTrue(foundOldSummary, "turn 1/2 summarized");
        }

        static void CompactionReportsEventIdRangesAndStructuredCards()
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 2,
                MaxSerializedBytes = 1024 * 1024,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 1, "A");
            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 2, "B");
            AppendPublic(tracker, 3, 1, 0, LlmPublicDuelEventKind.AttackDeclare, 3, "C");
            AppendPublic(tracker, 4, 2, 1, LlmPublicDuelEventKind.NormalSummon, 4, "D");
            AppendPublic(tracker, 5, 3, 0, LlmPublicDuelEventKind.NormalSummon, 5, "E");

            LlmDuelHistoryState projection = tracker.CreateRequestProjection();
            AssertTrue(projection.PriorTurnSummaries.Count > 0, "has summaries");
            Dictionary<string, object> summary = null;
            foreach (Dictionary<string, object> s in projection.PriorTurnSummaries)
            {
                if (Convert.ToInt32(s["turn"]) == 1)
                {
                    summary = s;
                    break;
                }
            }
            AssertTrue(summary != null, "turn 1 summary present");
            AssertEqual(1L, Convert.ToInt64(summary["first_event_id"]), "summary first_event_id");
            AssertEqual(3L, Convert.ToInt64(summary["last_event_id"]), "summary last_event_id");
            AssertEqual(3, Convert.ToInt32(summary["event_count"]), "summary event_count");
            AssertTrue(summary.ContainsKey("kind_counts"), "kind_counts present");
            AssertTrue(summary.ContainsKey("cards"), "structured cards present");
            IList cards = summary["cards"] as IList;
            AssertTrue(cards != null && cards.Count == 3, "three associated cards");
            Dictionary<string, object> card0 = cards[0] as Dictionary<string, object>;
            AssertEqual(1, Convert.ToInt32(card0["card_id"]), "cards sorted by id");
            AssertEqual("A", Convert.ToString(card0["name"]), "card id/name association");
            AssertTrue(summary.ContainsKey("summon_count"), "summon_count present");
            AssertTrue(summary.ContainsKey("lp_delta"), "lp_delta placeholder present");
            AssertEqual(true, summary["lp_delta"] == null, "lp_delta unknown is null");
        }

        static void FullHistoryUnchangedAfterRequestProjection()
        {
            LlmDuelHistoryTracker tracker = BuildMultiTurnTracker(4, 5);
            int fullBefore = tracker.CreateSnapshot().Events.Count;
            LlmDuelHistoryState projection = tracker.CreateRequestProjection();
            AssertTrue(projection.HistoryCompacted || projection.Events.Count <= fullBefore,
                "projection may compact");
            LlmDuelHistoryState fullAfter = tracker.CreateSnapshot();
            AssertEqual(fullBefore, fullAfter.Events.Count, "full history count unchanged");
            AssertEqual(false, fullAfter.HistoryCompacted, "full history never compacted flag");
            AssertEqual(0, fullAfter.PriorTurnSummaries.Count, "full history has no summaries");
        }

        static void RevealedCardContextDedupesPublicIdentitiesDeterministically()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 50, "Alpha");
            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 50, "Alpha");
            AppendPublic(tracker, 3, 1, 1, LlmPublicDuelEventKind.NormalSummon, 40, "Beta");
            AppendPublic(tracker, 4, 1, 1, LlmPublicDuelEventKind.NormalSummon, 60, "Gamma");

            LlmDuelHistoryState full = tracker.CreateSnapshot();
            AssertEqual(3, full.RevealedCardContext.Count, "deduped revealed cards");
            AssertEqual(40, Convert.ToInt32(full.RevealedCardContext[0]["card_id"]), "sorted card 0");
            AssertEqual(50, Convert.ToInt32(full.RevealedCardContext[1]["card_id"]), "sorted card 1");
            AssertEqual(60, Convert.ToInt32(full.RevealedCardContext[2]["card_id"]), "sorted card 2");
        }

        static void ProjectionRevealedContextOnlyIncludesDetailedEvents()
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 2,
                MaxSerializedBytes = 1024 * 1024,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            // Old turn cards that will be compacted away.
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 111, "OldOne");
            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 222, "OldTwo");
            AppendPublic(tracker, 3, 1, 0, LlmPublicDuelEventKind.AttackDeclare, 333, "OldThree");
            // Protected previous + current with different cards.
            AppendPublic(tracker, 4, 2, 1, LlmPublicDuelEventKind.NormalSummon, 444, "Prev");
            AppendPublic(tracker, 5, 3, 0, LlmPublicDuelEventKind.NormalSummon, 555, "Curr");

            LlmDuelHistoryState full = tracker.CreateSnapshot();
            AssertTrue(full.RevealedCardContext.Count >= 5, "full context keeps all public cards");

            LlmDuelHistoryState projection = tracker.CreateRequestProjection(1);
            AssertTrue(projection.HistoryCompacted, "old turn compacted");
            HashSet<int> projectionIds = new HashSet<int>();
            foreach (Dictionary<string, object> card in projection.RevealedCardContext)
            {
                projectionIds.Add(Convert.ToInt32(card["card_id"]));
            }
            AssertFalse(projectionIds.Contains(111), "old card 111 not in projection context");
            AssertFalse(projectionIds.Contains(222), "old card 222 not in projection context");
            AssertFalse(projectionIds.Contains(333), "old card 333 not in projection context");
            AssertTrue(projectionIds.Contains(444), "prev detailed card retained");
            AssertTrue(projectionIds.Contains(555), "current detailed card retained");
        }

        static void CraftedSetIdentityNeverEntersRevealedCardContextOrProjection()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmPublicDuelEvent setEvent = new LlmPublicDuelEvent()
            {
                EventId = 1,
                RunEffectSeq = 1,
                Turn = 1,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = 0,
                Kind = LlmPublicDuelEventKind.SetMonster,
                CardId = SentinelSetCardId,
                CardName = SentinelSetName,
                DestinationZone = "monster_zone",
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
            };
            AssertTrue(tracker.TryAppendPublicEvent(setEvent).NewlyAppended, "set appended");
            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.NormalSummon, 9, "PublicNine");

            LlmDuelHistoryState full = tracker.CreateSnapshot();
            string fullJson = LlmDuelHistoryTracker.SerializeProjectionJson(full);
            AssertFalse(fullJson.Contains(SentinelSetName), "full projection leaks set name");
            AssertFalse(fullJson.Contains(SentinelSetCardId.ToString()), "full projection leaks set id");
            foreach (Dictionary<string, object> card in full.RevealedCardContext)
            {
                AssertFalse(Convert.ToInt32(card["card_id"]) == SentinelSetCardId, "revealed set id");
                AssertFalse(Convert.ToString(card["name"]) == SentinelSetName, "revealed set name");
            }

            string requestJson = tracker.SerializeRequestProjection();
            AssertFalse(requestJson.Contains(SentinelSetName), "request projection set name");
            AssertFalse(requestJson.Contains(SentinelSetCardId.ToString()), "request projection set id");
        }

        static void DecisionSnapshotAttachesRequestProjectionNotFullHistory()
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 2,
                MaxSerializedBytes = 1024 * 1024,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            ulong id = 1;
            for (int turn = 1; turn <= 4; turn++)
            {
                AppendPublic(tracker, id++, turn, 0, LlmPublicDuelEventKind.NormalSummon, 10 + turn, "T" + turn);
                AppendPublic(tracker, id++, turn, 1, LlmPublicDuelEventKind.ActivateEffect, 20 + turn, "U" + turn);
            }

            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 10,
                Turn = 4,
                ControlledPlayer = 1,
            };
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);

            AssertTrue(snapshot.DuelHistory != null, "duel history attached");
            AssertTrue(snapshot.DuelHistory.HistoryCompacted, "attached history is request projection");
            AssertTrue(snapshot.DuelHistory.PriorTurnSummaries.Count > 0, "attached projection has summaries");
            AssertTrue(
                snapshot.DuelHistory.Events.Count < tracker.CreateSnapshot().Events.Count,
                "attached projection has fewer detailed events than full history");
            AssertEqual(
                tracker.CreateSnapshot().Events.Count,
                8,
                "full tracker history remains intact");
            AssertEqual(false, tracker.CreateSnapshot().HistoryCompacted, "full snapshot uncompacted");
        }

        static void SameAttachedSnapshotPreservedWhenSnapshotReused()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 1, "One");
            DecisionSnapshot snapshot = new DecisionSnapshot() { RunEffectSeq = 1, Turn = 1, ControlledPlayer = 1 };
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);
            LlmDuelHistoryState first = snapshot.DuelHistory;
            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 2, "Two");
            AssertTrue(object.ReferenceEquals(first, snapshot.DuelHistory), "same snapshot reference");
            AssertEqual(1, snapshot.DuelHistory.Events.Count, "request snapshot frozen");
        }

        static void NewlyExtractedAttachmentSeesCurrentHistory()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 1, 1, 0, LlmPublicDuelEventKind.NormalSummon, 1, "One");
            DecisionSnapshot first = new DecisionSnapshot() { RunEffectSeq = 1, Turn = 1, ControlledPlayer = 1 };
            LlmDecisionSnapshotHistory.Attach(first, tracker);

            AppendPublic(tracker, 2, 1, 0, LlmPublicDuelEventKind.ActivateEffect, 2, "Two");
            DecisionSnapshot retry = new DecisionSnapshot() { RunEffectSeq = 1, Turn = 1, ControlledPlayer = 1 };
            LlmDecisionSnapshotHistory.Attach(retry, tracker);
            AssertEqual(2, retry.DuelHistory.Events.Count, "retry extraction gets current history");
            AssertEqual(1, first.DuelHistory.Events.Count, "original request snapshot unchanged");
        }

        static void BrokerRequestJsonIncludesSchemaV4DuelHistoryField()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 42,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                Turn = 2,
            };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
            });
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendPublic(tracker, 1, 2, 0, LlmPublicDuelEventKind.NormalSummon, 3, "Three");
            LlmDecisionSnapshotHistory.Attach(snapshot, tracker);

            string requestJson = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            AssertEqual(4, LlmBrokerProtocol.SchemaVersion, "schema v4");
            AssertTrue(requestJson.Contains("\"schema_version\":4") || requestJson.Contains("schema_version"),
                "request has schema_version");
            AssertTrue(requestJson.Contains("duel_history"), "schema v4 serializes duel_history in request");
            AssertTrue(requestJson.Contains("turn_memory"), "turn_memory still present");
            AssertTrue(snapshot.DuelHistory != null && snapshot.DuelHistory.Events.Count == 1,
                "history still attached on snapshot object");
            // Projection content must match the attached snapshot (event 1 / Public Three).
            AssertTrue(requestJson.Contains("\"event_id\":1") || requestJson.Contains("\"event_id\": 1"),
                "request duel_history includes attached event_id");
        }

        static LlmDuelHistoryTracker BuildMultiTurnTracker(int turns, int eventsPerTurn)
        {
            LlmDuelHistoryTrackerOptions options = new LlmDuelHistoryTrackerOptions()
            {
                MaxDetailedEvents = 5,
                MaxSerializedBytes = 32 * 1024,
            };
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(options);
            ulong id = 1;
            for (int turn = 1; turn <= turns; turn++)
            {
                for (int i = 0; i < eventsPerTurn; i++)
                {
                    AppendPublic(
                        tracker,
                        id++,
                        turn,
                        i % 2,
                        LlmPublicDuelEventKind.NormalSummon,
                        100 + turn,
                        "Card" + turn);
                }
            }
            return tracker;
        }

        static LlmPublicDuelEvent MakeEvent(
            ulong eventId,
            int turn,
            int actor,
            LlmPublicDuelEventKind kind,
            int cardId,
            string cardName)
        {
            return new LlmPublicDuelEvent()
            {
                EventId = eventId,
                RunEffectSeq = eventId,
                Turn = turn,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = actor,
                Kind = kind,
                CardId = cardId,
                CardName = cardName,
                DestinationZone = "monster_zone",
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
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
            LlmDuelHistoryAppendResult result = tracker.TryAppendPublicEvent(
                MakeEvent(eventId, turn, actor, kind, cardId, cardName));
            if (!result.Accepted || !result.NewlyAppended)
            {
                throw new Exception("append failed: " + (result.Error ?? "not newly appended"));
            }
        }

        static int CountEventsOnTurns(LlmDuelHistoryState state, params int[] turns)
        {
            HashSet<int> set = new HashSet<int>(turns);
            int count = 0;
            foreach (LlmPublicDuelEvent evt in state.Events)
            {
                if (set.Contains(evt.Turn))
                {
                    count++;
                }
            }
            return count;
        }

        static bool AllEventsOnProtectedTurns(LlmDuelHistoryState state, int current, int previous)
        {
            foreach (LlmPublicDuelEvent evt in state.Events)
            {
                if (evt.Turn != current && evt.Turn != previous)
                {
                    return false;
                }
            }
            return true;
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

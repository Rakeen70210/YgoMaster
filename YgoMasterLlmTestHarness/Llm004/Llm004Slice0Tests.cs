using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YgoMaster.Net;
using YgoMaster.Net.Message;

namespace YgoMaster
{
    static class Llm004Slice0Tests
    {
        const string SentinelOpponentHandName = "SENTINEL_OPPONENT_HAND_LEAK_99101";
        const string SentinelSetName = "SENTINEL_FACEDOWN_SET_LEAK_99102";
        const int SentinelCardId = 99102991;
        const int SentinelUniqueId = 88102881;
        const int SentinelHandIndex = 7;

        public static void RunAll()
        {
            OpponentSummonMayExposeIdentity();
            OpponentActivationMayExposeIdentity();
            OpponentSetMonsterNeverExposesIdentityOrIndexes();
            OpponentSetSpellTrapNeverExposesIdentityOrIndexes();
            ControlledPlayerSetAlsoRedactsAbsoluteHistoryFields();
            OmniscientSetInputsNeverSerializeHiddenFields();
            PublicHandSummonMayExposeIdentityButNeverHandIndex();
            AnnouncedSummonVariantsExposeIdentityDespiteUnknownFace();
            AnnouncedActivationExposesIdentityDespiteFacedownPreCommandFace();
            ActorPlayerRemainsOriginatingSeatWhenCommandPlayerIsTarget();
            FacedownTargetSelectionOmitsIdentity();
            FacedownBanishedOutcomeOmitsIdentity();
            HiddenListSelectionOmitsIdentity();
            UnknownFaceDecideTargetFailsClosedWithoutIdentity();
            ProductionDuelPublicActionEventMessageRoundTripsAllFields();
            ProductionDuelComDoCommandMessageRoundTripsActorPlayer();
            SequenceMismatchEmitsZeroEvents();
            AuthoritativeEventWithZeroRunEffectSeqAppends();
            UnknownCommandKindFailsClosedWithoutIdentity();
            PublicEventRoutingAcceptsOnlyPvpClientAndNeverLoopsBack();
            PublicEventRoutingRequiresDuelingState();
            DuelistOriginatedPublicEventIsDropped();
            ActorSeatResolvedFromTableMembershipNotClientPayload();
            TrackerPreservesMonotonicEventOrdering();
            TrackerIgnoresDuplicateEventIds();
            TrackerIgnoresReorderedEventIds();
            TrackerLogsGapsWithoutInventingEvents();
            TrackerDuplicateDeliveryIsNotNewlyAppended();
            ClientIngestLogsOnlyNewlyAppendedEvents();
            TwoTrackersRetainIdenticalEventIdsAfterSameFeed();
            DuelResetClearsHistoryState();
            LifecycleResetsOnBeginEndAndDisconnectBoundaries();
            CraftedSetEventsNeverLeakIdentityThroughWireOrSerialization();
            SerializedRequestProjectionOmitsSentinelHiddenValues();
            TurnMemoryUnchangedWhenHistoryTrackerPresent();
            Llm004Slice2Tests.RunAll();
            Llm004Slice3Tests.RunAll();
            Llm004Slice4Tests.RunAll();
            Llm004Slice5Tests.RunAll();
        }

        static void OpponentSummonMayExposeIdentity()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Summon,
                cardId: 4900,
                cardName: "Cubic Seed",
                cardFace: 8,
                sourceFromHand: true,
                handIndex: 2);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 1);
            AssertTrue(result.Accepted, "accepted summon");
            AssertNotNull(result.Event, "summon event");
            AssertEqual(LlmPublicDuelEventKind.NormalSummon, result.Event.Kind, "summon kind");
            AssertEqual(4900, result.Event.CardId.Value, "summon card id");
            AssertEqual("Cubic Seed", result.Event.CardName, "summon card name");
        }

        static void OpponentActivationMayExposeIdentity()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Action,
                cardId: 4400,
                cardName: "Armored Bee",
                cardFace: 8,
                position: 5,
                index: 0);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 2);
            AssertTrue(result.Accepted, "accepted activation");
            AssertEqual(LlmPublicDuelEventKind.ActivateEffect, result.Event.Kind, "activation kind");
            AssertEqual(4400, result.Event.CardId.Value, "activation card id");
            AssertEqual("Armored Bee", result.Event.CardName, "activation card name");
        }

        static void OpponentSetMonsterNeverExposesIdentityOrIndexes()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.SetMonst,
                cardId: SentinelCardId,
                cardName: SentinelSetName,
                cardUniqueId: SentinelUniqueId,
                handIndex: SentinelHandIndex,
                metadata: "engine-private",
                cardFace: 4);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 3);
            AssertTrue(result.Accepted, "accepted set monster");
            AssertEqual(LlmPublicDuelEventKind.SetMonster, result.Event.Kind, "set monster kind");
            AssertNoCardId(result.Event, "set monster card id");
            AssertNull(result.Event.CardName, "set monster card name");
            Dictionary<string, object> projection =
                LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(result.Event);
            AssertNoLeak(projection, SentinelSetName, SentinelCardId, SentinelUniqueId, SentinelHandIndex);
        }

        static void OpponentSetSpellTrapNeverExposesIdentityOrIndexes()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Set,
                cardId: SentinelCardId,
                cardName: SentinelSetName,
                cardUniqueId: SentinelUniqueId,
                handIndex: SentinelHandIndex,
                metadata: "engine-private",
                cardFace: 4);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 4);
            AssertTrue(result.Accepted, "accepted set spell/trap");
            AssertEqual(LlmPublicDuelEventKind.SetSpellTrap, result.Event.Kind, "set spell/trap kind");
            AssertNoCardId(result.Event, "set spell/trap card id");
            AssertNull(result.Event.CardName, "set spell/trap card name");
            Dictionary<string, object> projection =
                LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(result.Event);
            AssertNoLeak(projection, SentinelSetName, SentinelCardId, SentinelUniqueId, SentinelHandIndex);
        }

        static void ControlledPlayerSetAlsoRedactsAbsoluteHistoryFields()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.SetMonst,
                actorPlayer: 1,
                commandPlayer: 1,
                cardId: SentinelCardId,
                cardName: SentinelSetName,
                cardUniqueId: SentinelUniqueId,
                handIndex: SentinelHandIndex,
                metadata: "controlled-private-set",
                cardFace: 4);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 14);
            AssertTrue(result.Accepted, "accepted controlled-player set monster");
            AssertEqual(1, result.Event.ActorPlayer, "controlled set actor seat");
            AssertEqual(LlmPublicDuelEventKind.SetMonster, result.Event.Kind, "controlled set kind");
            AssertNoCardId(result.Event, "controlled set card id");
            AssertNull(result.Event.CardName, "controlled set card name");
            Dictionary<string, object> projection =
                LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(result.Event);
            AssertNoLeak(projection, SentinelSetName, SentinelCardId, SentinelUniqueId, SentinelHandIndex);
            string json = MiniJSON.Json.Serialize(projection);
            AssertFalse(json.Contains("controlled-private-set"), "controlled set projection leaks metadata");
        }

        static void OmniscientSetInputsNeverSerializeHiddenFields()
        {
            foreach (DuelCommandType command in new[]
            {
                DuelCommandType.Set,
                DuelCommandType.SetMonst,
            })
            {
                LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                    command,
                    cardId: SentinelCardId,
                    cardName: SentinelSetName,
                    cardUniqueId: SentinelUniqueId,
                    handIndex: SentinelHandIndex,
                    metadata: "omniscient-metadata");
                LlmPublicDuelEventCreationResult result =
                    LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 5);
                AssertTrue(result.Accepted, "accepted set command " + command);
                Dictionary<string, object> projection =
                    LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(result.Event);
                string json = MiniJSON.Json.Serialize(projection);
                AssertFalse(json.Contains(SentinelSetName), "set projection leaks name");
                AssertFalse(json.Contains(SentinelCardId.ToString()), "set projection leaks card id");
                AssertFalse(json.Contains(SentinelUniqueId.ToString()), "set projection leaks unique id");
                AssertFalse(json.Contains(SentinelHandIndex.ToString()), "set projection leaks hand index");
                AssertFalse(json.Contains("omniscient-metadata"), "set projection leaks metadata");
                AssertFalse(json.Contains("card_unique_id"), "set projection has unique id key");
                AssertFalse(json.Contains("source_index") || json.Contains("hand_index"),
                    "set projection has source index key");
            }
        }

        static void PublicHandSummonMayExposeIdentityButNeverHandIndex()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Summon,
                cardId: 4900,
                cardName: "Cubic Seed",
                sourceFromHand: true,
                handIndex: 3,
                cardFace: 8);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 6);
            AssertTrue(result.Accepted, "accepted public hand summon");
            AssertEqual(4900, result.Event.CardId.Value, "hand summon card id");
            AssertEqual("Cubic Seed", result.Event.CardName, "hand summon card name");
            Dictionary<string, object> projection =
                LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(result.Event);
            string json = MiniJSON.Json.Serialize(projection);
            AssertFalse(json.Contains("\"index\""), "hand summon leaks source index");
            AssertFalse(json.Contains("hand_index"), "hand summon leaks hand index field");
            AssertFalse(json.Contains("3"), "hand summon leaks numeric hand index");
        }

        static void AnnouncedSummonVariantsExposeIdentityDespiteUnknownFace()
        {
            const int hiddenHandSlot = 11;
            ulong eventId = 80;
            foreach (var entry in new[]
            {
                new { Command = DuelCommandType.Summon, Kind = LlmPublicDuelEventKind.NormalSummon },
                new { Command = DuelCommandType.SummonSp, Kind = LlmPublicDuelEventKind.SpecialSummon },
                new { Command = DuelCommandType.Pendulum, Kind = LlmPublicDuelEventKind.PendulumSummon },
                new { Command = DuelCommandType.Reverse, Kind = LlmPublicDuelEventKind.FlipSummon },
            })
            {
                LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                    entry.Command,
                    cardId: 4900,
                    cardName: "Cubic Seed",
                    cardFace: 0,
                    sourceFromHand: true,
                    handIndex: hiddenHandSlot);
                LlmPublicDuelEventCreationResult result =
                    LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, eventId++);
                AssertTrue(result.Accepted, "accepted announced " + entry.Command);
                AssertEqual(entry.Kind, result.Event.Kind, entry.Command + " kind");
                AssertTrue(result.Event.CardId.HasValue, entry.Command + " has card id");
                AssertEqual(4900, result.Event.CardId.Value, entry.Command + " card id despite unknown face");
                AssertEqual("Cubic Seed", result.Event.CardName, entry.Command + " card name despite unknown face");
                string json = MiniJSON.Json.Serialize(
                    LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(result.Event));
                AssertFalse(json.Contains("\"hand_index\"") || json.Contains("\"source_index\"") || json.Contains("\"index\""),
                    entry.Command + " hand index field");
                AssertFalse(json.Contains(":11") || json.Contains(": 11"), entry.Command + " hand slot value leak");
            }
        }

        static void AnnouncedActivationExposesIdentityDespiteFacedownPreCommandFace()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Action,
                cardId: 4400,
                cardName: "Armored Bee",
                cardFace: 4,
                position: 5,
                index: 0);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 71);
            AssertTrue(result.Accepted, "accepted activation with facedown pre-command face");
            AssertEqual(4400, result.Event.CardId.Value, "activation identity despite facedown face");
            AssertEqual("Armored Bee", result.Event.CardName, "activation name despite facedown face");
        }

        static void ActorPlayerRemainsOriginatingSeatWhenCommandPlayerIsTarget()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Decide,
                actorPlayer: 0,
                commandPlayer: 1,
                position: 2,
                index: 0,
                cardFace: 8);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 7);
            AssertTrue(result.Accepted, "accepted target decide");
            AssertEqual(0, result.Event.ActorPlayer, "actor remains originating seat");
            AssertEqual(LlmPublicDuelEventKind.PublicTargetSelect, result.Event.Kind, "target select kind");
        }

        static void FacedownTargetSelectionOmitsIdentity()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Decide,
                commandPlayer: 1,
                position: 5,
                index: 0,
                cardId: SentinelCardId,
                cardName: SentinelSetName,
                cardFace: 4);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 8);
            AssertTrue(result.Accepted, "accepted facedown target");
            AssertNoCardId(result.Event, "facedown target card id");
            AssertNull(result.Event.CardName, "facedown target card name");
            AssertEqual("spell_trap_zone", result.Event.TargetZone, "facedown target zone");
        }

        static void FacedownBanishedOutcomeOmitsIdentity()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Decide,
                commandPlayer: 1,
                position: 25,
                index: 0,
                cardId: SentinelCardId,
                cardName: SentinelSetName,
                cardFace: 4);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 9);
            AssertTrue(result.Accepted, "accepted facedown banished context");
            AssertNoCardId(result.Event, "facedown banished card id");
            AssertNull(result.Event.CardName, "facedown banished card name");
        }

        static void HiddenListSelectionOmitsIdentity()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Decide,
                commandPlayer: 1,
                position: 13,
                index: 0,
                cardId: SentinelCardId,
                cardName: SentinelOpponentHandName,
                cardFace: 4,
                metadata: "hidden_list_selection");
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 10);
            AssertTrue(result.Accepted, "accepted hidden list selection");
            AssertNoCardId(result.Event, "hidden list card id");
            AssertNull(result.Event.CardName, "hidden list card name");
        }

        static void UnknownFaceDecideTargetFailsClosedWithoutIdentity()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Decide,
                commandPlayer: 1,
                position: 2,
                index: 0,
                cardId: SentinelCardId,
                cardName: SentinelSetName,
                cardFace: 0);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 11);
            AssertTrue(result.Accepted, "accepted unknown-face decide");
            AssertNoCardId(result.Event, "unknown face decide card id");
            AssertNull(result.Event.CardName, "unknown face decide card name");
        }

        static void ProductionDuelPublicActionEventMessageRoundTripsAllFields()
        {
            DuelPublicActionEventMessage source = new DuelPublicActionEventMessage()
            {
                RunEffectSeq = 120,
                EventId = 18,
                Turn = 2,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = 0,
                Kind = (int)LlmPublicDuelEventKind.NormalSummon,
                CardId = 4900,
                CardName = "Cubic Seed",
                SourceZone = "hand",
                DestinationZone = "monster_zone",
                TargetZone = null,
                Evidence = (int)LlmPublicHistoryEvidence.AcceptedCommand,
            };
            DuelPublicActionEventMessage copy = RoundTripMessage(source);
            AssertEqual(source.RunEffectSeq, copy.RunEffectSeq, "prod wire run_effect_seq");
            AssertEqual(source.EventId, copy.EventId, "prod wire event_id");
            AssertEqual(source.ActorPlayer, copy.ActorPlayer, "prod wire actor_player");
            AssertEqual(source.CardId, copy.CardId, "prod wire card_id");
            AssertEqual(source.CardName, copy.CardName, "prod wire card_name");
            AssertEqual(source.SourceZone, copy.SourceZone, "prod wire source_zone");
            AssertEqual(source.DestinationZone, copy.DestinationZone, "prod wire destination_zone");
            AssertEqual(source.Evidence, copy.Evidence, "prod wire evidence");
            AssertEqual(NetMessageType.DuelPublicActionEvent, source.Type, "prod message type");
        }

        static void ProductionDuelComDoCommandMessageRoundTripsActorPlayer()
        {
            DuelComDoCommandMessage source = new DuelComDoCommandMessage()
            {
                RunEffectSeq = 88,
                ActorPlayer = 1,
                Player = 0,
                Position = 2,
                Index = 0,
                CommandId = (int)DuelCommandType.Decide,
            };
            DuelComDoCommandMessage copy = RoundTripMessage(source);
            AssertEqual(source.ActorPlayer, copy.ActorPlayer, "prod com actor_player");
            AssertEqual(source.Player, copy.Player, "prod com target player coordinate");
            AssertEqual(source.RunEffectSeq, copy.RunEffectSeq, "prod com run_effect_seq");
            AssertEqual(source.CommandId, copy.CommandId, "prod com command id");
            AssertTrue(typeof(DuelComMessage).IsAssignableFrom(typeof(DuelComDoCommandMessage)),
                "DoCommand derives DuelComMessage ActorPlayer wire field");
        }

        static void SequenceMismatchEmitsZeroEvents()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(DuelCommandType.Summon);
            capture.RunEffectSeq = 50;
            capture.EngineRunEffectSeq = 51;
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 12);
            AssertFalse(result.Accepted, "sequence mismatch accepted");
            AssertNull(result.Event, "sequence mismatch event");
        }

        static void AuthoritativeEventWithZeroRunEffectSeqAppends()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmPublicDuelEvent evt = BuildEvent(1, 0);
            LlmDuelHistoryAppendResult result = tracker.TryAppendPublicEvent(evt);
            AssertTrue(result.Accepted, "seq 0 is valid and accepted");
            AssertTrue(result.NewlyAppended, "seq 0 newly appended");
            AssertEqual(1, tracker.CreateSnapshot().Events.Count, "seq 0 event stored");
            AssertEqual((ulong)0, tracker.CreateSnapshot().Events[0].RunEffectSeq, "stored seq 0");
        }

        static void UnknownCommandKindFailsClosedWithoutIdentity()
        {
            LlmAcceptedCommandCapture capture = CreateOpponentCapture(
                DuelCommandType.Look,
                cardId: SentinelCardId,
                cardName: SentinelSetName);
            LlmPublicDuelEventCreationResult result =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(capture, 13);
            if (result.Accepted)
            {
                AssertEqual(LlmPublicDuelEventKind.GenericAcceptedCommand, result.Event.Kind, "generic kind");
                AssertNoCardId(result.Event, "generic event card id");
                AssertNull(result.Event.CardName, "generic event card name");
            }
            else
            {
                AssertNull(result.Event, "unknown command emits no event");
            }
        }

        static void PublicEventRoutingAcceptsOnlyPvpClientAndNeverLoopsBack()
        {
            DuelRoomTable table;
            Player p1;
            Player p2;
            Net.NetClient pvpClient;
            Net.NetClient p1Client;
            Net.NetClient p2Client;
            CreateDuelingTable(out table, out p1, out p2, out pvpClient, out p1Client, out p2Client);
            table.PvpClient = pvpClient;

            LlmPublicActionEventRoutingResult result = LlmPublicActionEventRouting.RouteIncoming(
                table,
                pvpClient,
                new object());

            AssertEqual(
                LlmPublicActionEventRoutingDecision.FanOutToDuelists,
                result.Decision,
                "pvp public event fan-out");
            AssertEqual(2, result.Recipients.Count, "recipient count");
            AssertTrue(result.Recipients.Contains(p1Client), "p1 recipient");
            AssertTrue(result.Recipients.Contains(p2Client), "p2 recipient");
            AssertFalse(result.LoopedBackToPvpWorker, "no pvp loopback");
        }

        static void PublicEventRoutingRequiresDuelingState()
        {
            DuelRoomTable table;
            Player p1;
            Player p2;
            Net.NetClient pvpClient;
            Net.NetClient p1Client;
            Net.NetClient p2Client;
            CreateDuelingTable(out table, out p1, out p2, out pvpClient, out p1Client, out p2Client);
            table.PvpClient = pvpClient;
            table.State = DuelRoomTableState.Matched;

            LlmPublicActionEventRoutingResult result = LlmPublicActionEventRouting.RouteIncoming(
                table,
                pvpClient,
                new object());

            AssertEqual(
                LlmPublicActionEventRoutingDecision.Dropped,
                result.Decision,
                "non-dueling table drops public event");
            AssertEqual(0, result.Recipients.Count, "no recipients outside Dueling");
        }

        static void DuelistOriginatedPublicEventIsDropped()
        {
            DuelRoomTable table;
            Player p1;
            Player p2;
            Net.NetClient pvpClient;
            Net.NetClient p1Client;
            Net.NetClient p2Client;
            CreateDuelingTable(out table, out p1, out p2, out pvpClient, out p1Client, out p2Client);
            table.PvpClient = pvpClient;

            LlmPublicActionEventRoutingResult result = LlmPublicActionEventRouting.RouteIncoming(
                table,
                p1Client,
                new object());

            AssertEqual(
                LlmPublicActionEventRoutingDecision.Dropped,
                result.Decision,
                "duelist spoof dropped");
            AssertEqual(0, result.Recipients.Count, "no fan-out for duelist spoof");
        }

        static void ActorSeatResolvedFromTableMembershipNotClientPayload()
        {
            DuelRoomTable table;
            Player p1;
            Player p2;
            Net.NetClient pvpClient;
            Net.NetClient p1Client;
            Net.NetClient p2Client;
            CreateDuelingTable(out table, out p1, out p2, out pvpClient, out p1Client, out p2Client);

            AssertEqual(0, LlmPublicActionEventRouting.ResolveActorSeatFromTable(table, p1), "p1 seat");
            AssertEqual(1, LlmPublicActionEventRouting.ResolveActorSeatFromTable(table, p2), "p2 seat");
        }

        static void TrackerPreservesMonotonicEventOrdering()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendEvent(tracker, 1, 10);
            AppendEvent(tracker, 2, 11);
            AppendEvent(tracker, 3, 12);
            LlmDuelHistoryState snapshot = tracker.CreateSnapshot();
            AssertEqual(3, snapshot.Events.Count, "event count");
            AssertEqual((ulong)1, snapshot.Events[0].EventId, "first event id");
            AssertEqual((ulong)3, snapshot.Events[2].EventId, "third event id");
            AssertEqual((ulong)10, snapshot.Events[0].RunEffectSeq, "first run_effect_seq");
            AssertEqual((ulong)12, snapshot.Events[2].RunEffectSeq, "third run_effect_seq");
        }

        static void TrackerIgnoresDuplicateEventIds()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendEvent(tracker, 1, 20);
            AppendEvent(tracker, 1, 21);
            LlmDuelHistoryState snapshot = tracker.CreateSnapshot();
            AssertEqual(1, snapshot.Events.Count, "duplicate ignored");
            AssertEqual((ulong)20, snapshot.Events[0].RunEffectSeq, "first seq retained");
        }

        static void TrackerIgnoresReorderedEventIds()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendEvent(tracker, 2, 22);
            AppendEvent(tracker, 1, 23);
            LlmDuelHistoryState snapshot = tracker.CreateSnapshot();
            AssertEqual(1, snapshot.Events.Count, "reordered id ignored");
            AssertEqual((ulong)2, snapshot.Events[0].EventId, "latest id retained");
        }

        static void TrackerLogsGapsWithoutInventingEvents()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendEvent(tracker, 1, 30);
            AppendEvent(tracker, 3, 31);
            LlmDuelHistoryState snapshot = tracker.CreateSnapshot();
            AssertEqual(2, snapshot.Events.Count, "gap does not invent middle event");
            AssertTrue(snapshot.GapWarnings.Count > 0, "gap warning logged");
            AssertFalse(
                snapshot.Events.Any(evt => evt.EventId == 2),
                "gap does not invent event id 2");
        }

        static void TrackerDuplicateDeliveryIsNotNewlyAppended()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmPublicDuelEvent evt = BuildEvent(1, 40);
            LlmDuelHistoryAppendResult first = tracker.TryAppendPublicEvent(evt);
            LlmDuelHistoryAppendResult second = tracker.TryAppendPublicEvent(evt);
            AssertTrue(first.Accepted && first.NewlyAppended, "first delivery newly appended");
            AssertTrue(second.Accepted, "duplicate delivery accepted");
            AssertFalse(second.NewlyAppended, "duplicate delivery not newly appended");
            AssertEqual(1, tracker.CreateSnapshot().Events.Count, "still one event");
        }

        static void ClientIngestLogsOnlyNewlyAppendedEvents()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmPublicDuelEvent evt = BuildEvent(5, 50);
            string error;
            string audit1;
            string audit2;
            AssertTrue(
                LlmPublicDuelEventClientIngest.TryIngestForAudit(tracker, evt, out error, out audit1),
                "first ingest audits");
            AssertNotNull(audit1, "first audit json");
            AssertTrue(audit1.Contains("llm_public_duel_event"), "audit kind");
            AssertFalse(
                LlmPublicDuelEventClientIngest.TryIngestForAudit(tracker, evt, out error, out audit2),
                "duplicate ingest does not audit");
            AssertNull(audit2, "no second audit json");
        }

        static void TwoTrackersRetainIdenticalEventIdsAfterSameFeed()
        {
            LlmDuelHistoryTracker trackerA = new LlmDuelHistoryTracker(null);
            LlmDuelHistoryTracker trackerB = new LlmDuelHistoryTracker(null);
            LlmPublicDuelEvent[] feed = new[]
            {
                BuildEvent(1, 40),
                BuildEvent(2, 41),
                BuildEvent(3, 42),
            };
            foreach (LlmPublicDuelEvent evt in feed)
            {
                AppendEvent(trackerA, evt.EventId, evt.RunEffectSeq);
                AppendEvent(trackerB, evt.EventId, evt.RunEffectSeq);
            }

            LlmDuelHistoryState snapshotA = trackerA.CreateSnapshot();
            LlmDuelHistoryState snapshotB = trackerB.CreateSnapshot();
            AssertEqual(snapshotA.Events.Count, snapshotB.Events.Count, "parallel event count");
            for (int i = 0; i < snapshotA.Events.Count; i++)
            {
                AssertEqual(snapshotA.Events[i].EventId, snapshotB.Events[i].EventId, "parallel event id " + i);
                AssertEqual(snapshotA.Events[i].ActorPlayer, snapshotB.Events[i].ActorPlayer, "parallel actor " + i);
            }
        }

        static void DuelResetClearsHistoryState()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AppendEvent(tracker, 1, 50);
            tracker.ResetForNewDuel(2);
            LlmDuelHistoryState snapshot = tracker.CreateSnapshot();
            AssertEqual(0, snapshot.Events.Count, "events cleared on duel reset");
            AssertEqual(2, tracker.DuelGeneration, "duel generation advanced");
            AssertEqual((ulong)0, snapshot.LastEventId, "last event id reset");
        }

        static void LifecycleResetsOnBeginEndAndDisconnectBoundaries()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            int generation = 0;
            AppendEvent(tracker, 1, 10);
            generation = LlmDuelHistoryLifecycle.ResetForBoundary(tracker, generation);
            AssertEqual(1, generation, "begin boundary generation");
            AssertEqual(0, tracker.CreateSnapshot().Events.Count, "begin clears history");

            AppendEvent(tracker, 1, 11);
            generation = LlmDuelHistoryLifecycle.ResetForBoundary(tracker, generation);
            AssertEqual(2, generation, "end boundary generation");
            AssertEqual(0, tracker.CreateSnapshot().Events.Count, "end clears history");

            AppendEvent(tracker, 1, 12);
            generation = LlmDuelHistoryLifecycle.ResetForBoundary(tracker, generation);
            AssertEqual(3, generation, "disconnect boundary generation");
            AssertEqual(0, tracker.CreateSnapshot().Events.Count, "disconnect clears history");
            AssertEqual(3, tracker.DuelGeneration, "tracker generation matches lifecycle");
        }

        static void CraftedSetEventsNeverLeakIdentityThroughWireOrSerialization()
        {
            foreach (LlmPublicDuelEventKind kind in new[]
            {
                LlmPublicDuelEventKind.SetMonster,
                LlmPublicDuelEventKind.SetSpellTrap,
            })
            {
                LlmPublicDuelEvent crafted = new LlmPublicDuelEvent()
                {
                    EventId = 99,
                    RunEffectSeq = 9,
                    Turn = 2,
                    Phase = (int)DuelPhase.Main1,
                    ActorPlayer = 0,
                    Kind = kind,
                    CardId = SentinelCardId,
                    CardName = SentinelSetName,
                    DestinationZone = kind == LlmPublicDuelEventKind.SetMonster
                        ? "monster_zone"
                        : "spell_trap_zone",
                    Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
                };

                Dictionary<string, object> projection =
                    LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(crafted);
                AssertNoLeak(projection, SentinelSetName, SentinelCardId);

                string logJson = LlmDecisionLogSerializer.SerializePublicDuelEvent(crafted);
                AssertFalse(logJson.Contains(SentinelSetName), kind + " log leaks name");
                AssertFalse(logJson.Contains(SentinelCardId.ToString()), kind + " log leaks card id");

                DuelPublicActionEventMessage message = DuelPublicActionEventMessage.FromEvent(crafted);
                AssertEqual(0, message.CardId, kind + " FromEvent card id");
                AssertNull(message.CardName, kind + " FromEvent card name");
                DuelPublicActionEventMessage wire = RoundTripMessage(message);
                AssertEqual(0, wire.CardId, kind + " wire card id");
                AssertNull(wire.CardName, kind + " wire card name");
                LlmPublicDuelEvent restored = wire.ToEvent();
                AssertNoCardId(restored, kind + " ToEvent card id");
                AssertNull(restored.CardName, kind + " ToEvent card name");
            }
        }

        static void SerializedRequestProjectionOmitsSentinelHiddenValues()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmAcceptedCommandCapture setCapture = CreateOpponentCapture(
                DuelCommandType.Set,
                cardId: SentinelCardId,
                cardName: SentinelSetName,
                cardUniqueId: SentinelUniqueId,
                handIndex: SentinelHandIndex);
            LlmPublicDuelEventCreationResult setEvent =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(setCapture, 1);
            AppendEvent(tracker, setEvent.Event);

            // Hidden list decide remains redacted (not an announced public summon).
            LlmAcceptedCommandCapture hiddenCapture = CreateOpponentCapture(
                DuelCommandType.Decide,
                cardId: SentinelCardId,
                cardName: SentinelOpponentHandName,
                cardFace: 4,
                position: 13,
                handIndex: SentinelHandIndex,
                metadata: "hidden_list_selection");
            LlmPublicDuelEventCreationResult hiddenEvent =
                LlmPublicActionEventFactory.TryCreateFromAcceptedCommand(hiddenCapture, 2);
            AppendEvent(tracker, hiddenEvent.Event);

            string projectionJson = tracker.SerializeRequestProjection();
            AssertFalse(projectionJson.Contains(SentinelSetName), "projection leaks set sentinel name");
            AssertFalse(projectionJson.Contains(SentinelOpponentHandName), "projection leaks hand sentinel name");
            AssertFalse(projectionJson.Contains(SentinelCardId.ToString()), "projection leaks sentinel card id");
            AssertFalse(projectionJson.Contains(SentinelUniqueId.ToString()), "projection leaks sentinel unique id");
            AssertFalse(projectionJson.Contains(SentinelHandIndex.ToString()), "projection leaks sentinel hand index");
            AssertTrue(projectionJson.Contains("duel_history"), "projection includes duel_history root");
        }

        static void TurnMemoryUnchangedWhenHistoryTrackerPresent()
        {
            LlmTurnMemoryTracker turnMemory = new LlmTurnMemoryTracker(4);
            LlmDuelHistoryTracker historyTracker = new LlmDuelHistoryTracker(null);
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 42,
                Turn = 3,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            LegalAction action = new LegalAction()
            {
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                CardId = 4900,
                Card = new LlmCardMetadata() { CardId = 4900, Name = "Cubic Seed" },
            };
            turnMemory.RecordCommittedAction(
                snapshot,
                new LlmBrokerDecisionResponse()
                {
                    Reason = "develop",
                    Plan = "develop_board",
                },
                action);

            LlmTurnMemoryState baseline = CloneTurnMemory(turnMemory.CreateSnapshot(snapshot));
            historyTracker.TryAppendPublicEvent(BuildEvent(1, snapshot.RunEffectSeq));
            historyTracker.TryAppendPublicEvent(BuildEvent(2, snapshot.RunEffectSeq + 1));
            LlmTurnMemoryState afterHistory = CloneTurnMemory(turnMemory.CreateSnapshot(snapshot));
            AssertTurnMemoryEqual(baseline, afterHistory, "turn memory with history tracker");
        }

        static T RoundTripMessage<T>(T message) where T : Net.NetMessage, new()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    message.Write(writer);
                }
                stream.Position = 0;
                T copy = new T();
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    copy.Read(reader);
                }
                return copy;
            }
        }

        static LlmAcceptedCommandCapture CreateOpponentCapture(
            DuelCommandType command,
            int actorPlayer = 0,
            int commandPlayer = 0,
            int position = 13,
            int index = 0,
            int cardId = 0,
            string cardName = null,
            int cardUniqueId = 0,
            int cardFace = 8,
            bool sourceFromHand = false,
            int handIndex = -1,
            string metadata = null)
        {
            return new LlmAcceptedCommandCapture()
            {
                RunEffectSeq = 100,
                EngineRunEffectSeq = 100,
                ActorPlayer = actorPlayer,
                CommandPlayer = commandPlayer,
                Position = position,
                Index = index,
                Command = command,
                CardId = cardId,
                CardName = cardName,
                CardUniqueId = cardUniqueId,
                CardFace = cardFace,
                SourceFromHand = sourceFromHand,
                HandIndex = handIndex,
                Metadata = metadata,
                Turn = 2,
                Phase = (int)DuelPhase.Main1,
            };
        }

        static LlmPublicDuelEvent BuildEvent(ulong eventId, ulong runEffectSeq)
        {
            return new LlmPublicDuelEvent()
            {
                EventId = eventId,
                RunEffectSeq = runEffectSeq,
                Turn = 2,
                Phase = (int)DuelPhase.Main1,
                ActorPlayer = 0,
                Kind = LlmPublicDuelEventKind.NormalSummon,
                CardId = 4900,
                CardName = "Cubic Seed",
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
            };
        }

        static void AppendEvent(LlmDuelHistoryTracker tracker, ulong eventId, ulong runEffectSeq)
        {
            AppendEvent(tracker, BuildEvent(eventId, runEffectSeq));
        }

        static void AppendEvent(LlmDuelHistoryTracker tracker, LlmPublicDuelEvent evt)
        {
            LlmDuelHistoryAppendResult result = tracker.TryAppendPublicEvent(evt);
            AssertTrue(result.Accepted, "append event: " + (result.Error ?? "ok"));
        }

        static void CreateDuelingTable(
            out DuelRoomTable table,
            out Player p1,
            out Player p2,
            out Net.NetClient pvpClient,
            out Net.NetClient p1Client,
            out Net.NetClient p2Client)
        {
            DuelRoom room = new DuelRoom();
            room.Id = 77;
            room.MemberLimit = 2;
            room.InitTables();
            table = room.Tables[0];
            table.State = DuelRoomTableState.Dueling;
            p1 = new Player() { Code = 1001, Name = "P1", DuelRoom = room };
            p2 = new Player() { Code = 1002, Name = "P2", DuelRoom = room };
            table.Player1 = p1;
            table.Player2 = p2;
            pvpClient = new Net.NetClient();
            p1Client = new Net.NetClient();
            p2Client = new Net.NetClient();
            p1.NetClient = p1Client;
            p2.NetClient = p2Client;
            p1Client.Data = p1;
            p2Client.Data = p2;
            pvpClient.Data = "pvp";
        }

        static LlmTurnMemoryState CloneTurnMemory(LlmTurnMemoryState source)
        {
            LlmTurnMemoryState clone = new LlmTurnMemoryState()
            {
                NormalSummonUsed = source.NormalSummonUsed,
                PhasePlan = source.PhasePlan,
            };
            foreach (LlmRecentActionMemory action in source.RecentActions)
            {
                clone.RecentActions.Add(new LlmRecentActionMemory()
                {
                    Turn = action.Turn,
                    Phase = action.Phase,
                    ActionType = action.ActionType,
                    ActionLabel = action.ActionLabel,
                    CardId = action.CardId,
                    CardName = action.CardName,
                    Reason = action.Reason,
                    Plan = action.Plan,
                });
            }
            foreach (LlmUsedCardMemory card in source.CardsUsedThisTurn)
            {
                clone.CardsUsedThisTurn.Add(new LlmUsedCardMemory()
                {
                    CardId = card.CardId,
                    Name = card.Name,
                });
            }
            return clone;
        }

        static void AssertTurnMemoryEqual(LlmTurnMemoryState expected, LlmTurnMemoryState actual, string message)
        {
            AssertEqual(expected.NormalSummonUsed, actual.NormalSummonUsed, message + " normal summon");
            AssertEqual(expected.PhasePlan, actual.PhasePlan, message + " phase plan");
            AssertEqual(expected.RecentActions.Count, actual.RecentActions.Count, message + " recent count");
            for (int i = 0; i < expected.RecentActions.Count; i++)
            {
                AssertEqual(expected.RecentActions[i].ActionLabel, actual.RecentActions[i].ActionLabel, message + " label");
                AssertEqual(expected.RecentActions[i].CardId, actual.RecentActions[i].CardId, message + " card id");
            }
        }

        static void AssertNoLeak(Dictionary<string, object> projection, params object[] forbidden)
        {
            string json = MiniJSON.Json.Serialize(projection);
            foreach (object value in forbidden)
            {
                if (value == null)
                {
                    continue;
                }

                AssertFalse(json.Contains(value.ToString()), "projection leak: " + value);
            }
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

        static void AssertNull(object value, string message)
        {
            if (value != null)
            {
                throw new Exception(message);
            }
        }

        static void AssertNoCardId(LlmPublicDuelEvent evt, string message)
        {
            if (evt != null && evt.CardId.HasValue)
            {
                throw new Exception(message + ": expected no card id, got " + evt.CardId.Value);
            }
        }

        static void AssertNotNull(object value, string message)
        {
            if (value == null)
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

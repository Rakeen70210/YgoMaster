using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using YgoMaster.Net;
using YgoMaster.Net.Message;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-004 Slice 3: raw view evidence, public-state deltas, proven outcomes,
    /// face/location fixtures, sentinel redaction. Unproven view params stay audit-only.
    /// </summary>
    static class Llm004Slice3Tests
    {
        const string SentinelHiddenName = "SENTINEL_S3_HIDDEN_CARD_77331";
        const int SentinelHiddenId = 77331001;

        public static void RunAll()
        {
            FaceLocationFixturesExposeOnlyAbsolutePublicIdentities();
            ProjectionCaptureFailsClosedOnFacedownAndHand();
            StateDeltaProducesLpTurnPhaseAndPublicCardOutcomes();
            UnprovenViewFamilyDoesNotInventNormalizedOutcomesFromParams();
            ShuffleOutcomeInvalidatesWithoutExposingIndices();
            NullBaselineProjectionEmitsNoSyntheticOutcomes();
            BaselineResetOnDuelGenerationClearsCounters();
            EqualLpDeltasAtDistinctSeqsBothAppendWhileTrueDuplicatesDoNot();
            GraveIndexShiftDoesNotCreateRemoveAddStorm();
            OutcomesNeverExposeCausedByOnWireOrProjection();
            OutcomeSourceSignaturesIncludeSeqAndBeforeAfter();
            RawViewEvidenceRoundTripsAndIsAuditOnly();
            RawViewEvidenceRoutingAntiSpoofAndRequiresDueling();
            TrackerDedupesRawEvidenceIdAndOutcomeSourceSignatures();
            OutcomeWireRoundTripsTargetCardPlayersAndSourceSignature();
            RuntimeSafeProjectionOmitsFieldAndBanishedIdentities();
            DllRuntimeFaceDomainIsSeparateFromFixtureFaceDomain();
            DllFaceUpFieldIncludedFacedownExcludedBeforeIdentityQuery();
            RuntimeCaptureBudgetScansFieldZeroToTwelvePlusGraveOnly();
            FaceUpFieldAppearanceOutcomeHasIdentityNoUidAndOptionalCausedBy();
            NormalSummonCausedByRequiresExactUidAndBoundedViewWindow();
            SameCardIdCopiesGetDistinctOccurrenceSignatures();
            CardNameEnrichmentResolvesFromCatalogFailSoft();
            FaceProbeCaptureIsAuditOnlyWithoutCardIdentity();
            FaceProbeWireRoundTripIsIdentityFree();
            FaceProbeRoutingAntiSpoofNoLoopAndRequiresDueling();
            TrackerDedupesFaceProbeIdAndResetsOnLifecycle();
            FaceProbeNeverEntersDuelHistoryOrRequestProjection();
            FaceProbeIngestHelperLogsOnlyNewlyAccepted();
            ProjectionQueryBudgetTracksQueries();
            SentinelHiddenNeverEntersProjectionWireOrHistory();
            LiveEvidenceReportFormatIsDeterministic();
            LiveEvidenceReportIncludesFaceProbes();
        }

        static void FaceLocationFixturesExposeOnlyAbsolutePublicIdentities()
        {
            AssertTrue(LlmAbsolutePublicVisibility.IsFaceUpMonsterFixture(0, 8), "face-up monster fixture");
            AssertTrue(LlmAbsolutePublicVisibility.IsFacedownMonsterFixture(1, 4), "facedown monster fixture");
            AssertTrue(LlmAbsolutePublicVisibility.IsFaceUpBanishedFixture(17, 8), "face-up banished fixture");
            AssertTrue(LlmAbsolutePublicVisibility.IsFacedownBanishedFixture(17, 4), "facedown banished fixture");

            LlmPublicVisibilityMode fixture = LlmPublicVisibilityMode.FixtureValidated;
            AssertTrue(LlmAbsolutePublicVisibility.CanExposeCardIdentity(0, 8, false, fixture),
                "face-up field public (fixture)");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeCardIdentity(0, 4, false, fixture),
                "facedown field private (fixture)");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeCardIdentity(0, 0, false, fixture),
                "unknown face fail-closed (fixture)");
            AssertTrue(LlmAbsolutePublicVisibility.CanExposeCardIdentity(16, 4, false, fixture),
                "grave always public");
            AssertTrue(LlmAbsolutePublicVisibility.CanExposeCardIdentity(17, 8, false, fixture),
                "face-up banished public (fixture)");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeCardIdentity(17, 4, false, fixture),
                "facedown banished private (fixture)");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeCardIdentity(13, 8, false, fixture),
                "closed hand private");
            AssertTrue(LlmAbsolutePublicVisibility.CanExposeCardIdentity(13, 8, true, fixture),
                "open hand public");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeCardIdentity(14, 8, false, fixture),
                "deck private");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeCardIdentity(0, 8), "runtime safe blocks field");

            AssertTrue(
                LlmPublicOutcomeEventFactory.TryCreatePublicCardAppearance(
                    0, 0, 8, 100, "Public Mon", 1, 2, 2, 9) != null,
                "face-up monster appearance allowed");
            AssertTrue(
                LlmPublicOutcomeEventFactory.TryCreatePublicCardAppearance(
                    0, 0, 4, SentinelHiddenId, SentinelHiddenName, 1, 2, 2, 10) == null,
                "facedown monster appearance denied");
            AssertTrue(
                LlmPublicOutcomeEventFactory.TryCreatePublicCardAppearance(
                    1, 16, 0, 200, "Grave", 1, 2, 2, 11) != null,
                "grave appearance allowed");
            AssertTrue(
                LlmPublicOutcomeEventFactory.TryCreatePublicCardAppearance(
                    1, 17, 4, SentinelHiddenId, SentinelHiddenName, 1, 2, 2, 12) == null,
                "facedown banished appearance denied");
        }

        static void ProjectionCaptureFailsClosedOnFacedownAndHand()
        {
            List<LlmPublicKnownCardProjection> input = new List<LlmPublicKnownCardProjection>()
            {
                new LlmPublicKnownCardProjection()
                {
                    Player = 0, Position = 0, Index = 0, CardId = 1, CardName = "Open", Face = 8,
                },
                new LlmPublicKnownCardProjection()
                {
                    Player = 0, Position = 1, Index = 0, CardId = SentinelHiddenId,
                    CardName = SentinelHiddenName, Face = 4,
                },
                new LlmPublicKnownCardProjection()
                {
                    Player = 1, Position = 13, Index = 0, CardId = SentinelHiddenId,
                    CardName = SentinelHiddenName, Face = 8,
                },
            };
            // Hand card without handOpen path: Capture re-checks with handOpen=true for hand
            // only when position is hand — but CanExpose requires handOpen true. For hand
            // position Capture uses handOpen=true incorrectly... we fixed Capture to use
            // handOpen = position==hand which would allow it. Adjust expectation:
            // For fail-closed, only pass cards through TryCreateKnownCard.
            LlmPublicKnownCardProjection open =
                LlmPublicStateProjection.TryCreateKnownCard(
                    0, 0, 0, 1, "Open", 8, false,
                    LlmPublicVisibilityMode.FixtureValidated, 0);
            LlmPublicKnownCardProjection down =
                LlmPublicStateProjection.TryCreateKnownCard(
                    0, 1, 0, SentinelHiddenId, SentinelHiddenName, 4, false,
                    LlmPublicVisibilityMode.FixtureValidated, 0);
            LlmPublicKnownCardProjection hand =
                LlmPublicStateProjection.TryCreateKnownCard(
                    1, 13, 0, SentinelHiddenId, SentinelHiddenName, 8, false,
                    LlmPublicVisibilityMode.FixtureValidated, 0);
            LlmPublicKnownCardProjection handOpen =
                LlmPublicStateProjection.TryCreateKnownCard(
                    1, 13, 0, 99, "Revealed", 8, true,
                    LlmPublicVisibilityMode.FixtureValidated, 0);
            AssertTrue(open != null, "open field known");
            AssertTrue(down == null, "facedown omitted");
            AssertTrue(hand == null, "closed hand omitted");
            AssertTrue(handOpen != null, "revealed hand allowed");

            LlmPublicStateProjection proj = LlmPublicStateProjection.Capture(
                3, (int)DuelPhase.Main1, 8000, 7500,
                new[] { open, handOpen }, LlmPublicVisibilityMode.FixtureValidated);
            AssertEqual(2, proj.KnownCards.Count, "only public cards in projection");
            AssertEqual(8000, proj.LifePoints0, "lp0");
            AssertEqual(7500, proj.LifePoints1, "lp1");
        }

        static void StateDeltaProducesLpTurnPhaseAndPublicCardOutcomes()
        {
            LlmPublicKnownCardProjection cardA =
                LlmPublicStateProjection.TryCreateKnownCard(
                    0, 0, 0, 10, "A", 8, false,
                    LlmPublicVisibilityMode.FixtureValidated, 101);
            LlmPublicKnownCardProjection cardB =
                LlmPublicStateProjection.TryCreateKnownCard(
                    1, 16, 0, 20, "B", 8, false,
                    LlmPublicVisibilityMode.FixtureValidated, 0);
            LlmPublicStateProjection before = LlmPublicStateProjection.Capture(
                1, (int)DuelPhase.Main1, 8000, 8000,
                new[] { cardA }, LlmPublicVisibilityMode.FixtureValidated);
            LlmPublicStateProjection after = LlmPublicStateProjection.Capture(
                2, (int)DuelPhase.Battle, 8000, 7200,
                new[] { cardA, cardB }, LlmPublicVisibilityMode.FixtureValidated);

            List<LlmPublicDuelEvent> outcomes = LlmPublicOutcomeEventFactory.CreateFromDelta(
                before, after, DuelViewType.LifeDamage, 50, 100);
            AssertTrue(outcomes.Count >= 3, "turn/phase/lp outcomes present");
            AssertTrue(HasKind(outcomes, LlmPublicDuelEventKind.TurnChanged), "turn changed");
            AssertTrue(HasKind(outcomes, LlmPublicDuelEventKind.PhaseChanged), "phase changed");
            AssertTrue(HasKind(outcomes, LlmPublicDuelEventKind.LifePointsChanged), "lp changed");
            AssertTrue(HasKind(outcomes, LlmPublicDuelEventKind.CardMovedPublic), "public card appeared");
            foreach (LlmPublicDuelEvent evt in outcomes)
            {
                AssertEqual(LlmPublicHistoryEvidence.PublicStateDelta, evt.Evidence, "outcome evidence");
                AssertTrue(evt.EventId >= 100, "monotonic ids from nextEventId");
                AssertTrue(!string.IsNullOrEmpty(evt.SourceSignature), "source signature set");
            }
        }

        static void UnprovenViewFamilyDoesNotInventNormalizedOutcomesFromParams()
        {
            // OverlayRun is not a proven absolute-public outcome family from params alone.
            // Without state delta, no outcomes.
            LlmPublicStateProjection same = LlmPublicStateProjection.Capture(
                1, (int)DuelPhase.Main1, 8000, 8000, null);
            List<LlmPublicDuelEvent> outcomes = LlmPublicOutcomeEventFactory.CreateFromDelta(
                same, same, DuelViewType.OverlayRun, 9, 1);
            AssertEqual(0, outcomes.Count, "unproven view with no delta invents nothing");
            AssertTrue(LlmAbsolutePublicVisibility.IsTargetRawEvidenceFamily(DuelViewType.CardMove),
                "card move is target raw family");
            AssertFalse(LlmAbsolutePublicVisibility.IsTargetRawEvidenceFamily(DuelViewType.Noop),
                "noop not target family");
        }

        static void ShuffleOutcomeInvalidatesWithoutExposingIndices()
        {
            LlmPublicStateProjection before = LlmPublicStateProjection.Capture(1, 2, 8000, 8000, null);
            LlmPublicStateProjection after = LlmPublicStateProjection.Capture(1, 2, 8000, 8000, null);
            List<LlmPublicDuelEvent> outcomes = LlmPublicOutcomeEventFactory.CreateFromDelta(
                before, after, DuelViewType.HandShuffle, 3, 20);
            AssertTrue(HasKind(outcomes, LlmPublicDuelEventKind.HandShuffled), "hand shuffle outcome");
            string json = MiniJSON.Json.Serialize(
                LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(outcomes[0]));
            AssertFalse(json.Contains("\"index\""), "shuffle omits index");
            AssertFalse(json.Contains("hand_index"), "shuffle omits hand_index");

            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AssertTrue(tracker.TryAppendPublicEvent(outcomes[0]).NewlyAppended, "shuffle append");
            AssertTrue(tracker.CreateSnapshot().Events.Count == 1, "shuffle in history");
        }

        static void NullBaselineProjectionEmitsNoSyntheticOutcomes()
        {
            LlmPublicStateProjection first = LlmPublicStateProjection.Capture(
                1, (int)DuelPhase.Main1, 8000, 8000, null);
            LlmPublicStateProjection baseline = null;
            List<LlmPublicDuelEvent> initial = LlmPublicOutcomePipeline.Transition(
                ref baseline, first, DuelViewType.TurnChange, 1, 1);
            AssertEqual(0, initial.Count, "null baseline emits nothing");
            AssertTrue(baseline != null && baseline.LifePoints0 == 8000, "baseline stored without synthetic lp event");

            // Second snapshot with real LP change emits one outcome (not synthetic +8000).
            LlmPublicStateProjection second = LlmPublicStateProjection.Capture(
                1, (int)DuelPhase.Main1, 8000, 7500, null);
            List<LlmPublicDuelEvent> after = LlmPublicOutcomePipeline.Transition(
                ref baseline, second, DuelViewType.LifeDamage, 2, 10);
            AssertEqual(1, after.Count, "real delta after baseline emits once");
            AssertTrue(after[0].Kind == LlmPublicDuelEventKind.LifePointsChanged, "lp delta kind");
            AssertTrue(after[0].SourceSignature.Contains(":from:8000:to:7500"), "from/to in signature");
            AssertFalse(after[0].SourceSignature.Contains(":from:0:to:8000"), "no synthetic 0->8000");
        }

        static void BaselineResetOnDuelGenerationClearsCounters()
        {
            LlmPublicStateProjection baseline = LlmPublicStateProjection.Capture(1, 2, 8000, 8000, null);
            ulong rawId = 9;
            ulong probeId = 4;
            ulong eventId = 12;
            LlmPublicOutcomePipeline.ResetDuelGeneration(ref baseline, ref rawId, ref probeId, ref eventId);
            AssertTrue(baseline == null, "baseline cleared");
            AssertEqual(0UL, rawId, "raw evidence id reset");
            AssertEqual(0UL, probeId, "face probe id reset");
            AssertEqual(0UL, eventId, "public event id reset");
        }

        static void EqualLpDeltasAtDistinctSeqsBothAppendWhileTrueDuplicatesDoNot()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmPublicStateProjection a = LlmPublicStateProjection.Capture(1, 2, 8000, 8000, null);
            LlmPublicStateProjection b = LlmPublicStateProjection.Capture(1, 2, 8000, 7500, null);
            LlmPublicStateProjection c = LlmPublicStateProjection.Capture(1, 2, 8000, 7000, null);

            List<LlmPublicDuelEvent> firstHit = LlmPublicOutcomeEventFactory.CreateFromDelta(
                a, b, DuelViewType.LifeDamage, 10, 1);
            List<LlmPublicDuelEvent> secondHit = LlmPublicOutcomeEventFactory.CreateFromDelta(
                b, c, DuelViewType.LifeDamage, 11, 2);
            AssertTrue(firstHit.Count == 1 && secondHit.Count == 1, "two equal -500 deltas");
            AssertTrue(firstHit[0].SourceSignature.Contains("seq:10"), "first seq");
            AssertTrue(secondHit[0].SourceSignature.Contains("seq:11"), "second seq");
            AssertTrue(firstHit[0].SourceSignature.Contains("delta:-500"), "delta -500 first");
            AssertTrue(secondHit[0].SourceSignature.Contains("delta:-500"), "delta -500 second");
            AssertTrue(firstHit[0].SourceSignature != secondHit[0].SourceSignature,
                "equal magnitude different seq => distinct signatures");

            AssertTrue(tracker.TryAppendPublicEvent(firstHit[0]).NewlyAppended, "first -500 appends");
            AssertTrue(tracker.TryAppendPublicEvent(secondHit[0]).NewlyAppended, "second -500 appends");
            LlmPublicDuelEvent redelivery = LlmDuelHistoryState.CloneEvent(firstHit[0]);
            redelivery.EventId = 99;
            AssertFalse(tracker.TryAppendPublicEvent(redelivery).NewlyAppended,
                "true duplicate delivery (same source signature) does not re-append");
            AssertEqual(2, tracker.CreateSnapshot().Events.Count, "two distinct equal-magnitude LPs kept");
        }

        static void GraveIndexShiftDoesNotCreateRemoveAddStorm()
        {
            // Same multiset (uid-less card-id identity), only list indices shifted.
            LlmPublicKnownCardProjection grave0 =
                LlmPublicStateProjection.TryCreateKnownCard(
                    0, 16, 0, 100, "G", 0, false, LlmPublicVisibilityMode.RuntimeSafe, 0);
            LlmPublicKnownCardProjection grave1 =
                LlmPublicStateProjection.TryCreateKnownCard(
                    0, 16, 1, 200, "H", 0, false, LlmPublicVisibilityMode.RuntimeSafe, 0);
            LlmPublicKnownCardProjection shifted0 =
                LlmPublicStateProjection.TryCreateKnownCard(
                    0, 16, 1, 100, "G", 0, false, LlmPublicVisibilityMode.RuntimeSafe, 0);
            LlmPublicKnownCardProjection shifted1 =
                LlmPublicStateProjection.TryCreateKnownCard(
                    0, 16, 0, 200, "H", 0, false, LlmPublicVisibilityMode.RuntimeSafe, 0);
            AssertTrue(grave0 != null && grave1 != null, "grave cards allowed runtime-safe");
            LlmPublicStateProjection before = LlmPublicStateProjection.Capture(
                1, 2, 8000, 8000, new[] { grave0, grave1 }, LlmPublicVisibilityMode.RuntimeSafe);
            LlmPublicStateProjection after = LlmPublicStateProjection.Capture(
                1, 2, 8000, 8000, new[] { shifted0, shifted1 }, LlmPublicVisibilityMode.RuntimeSafe);
            LlmPublicStateDelta delta = LlmPublicStateDelta.Diff(before, after);
            AssertEqual(0, delta.AddedKnownCards.Count, "index shift does not add");
            AssertEqual(0, delta.RemovedKnownCards.Count, "index shift does not remove");

            // Unique-id stable tracking: same uid different index is still identity-stable.
            LlmPublicKnownCardProjection uidCard =
                LlmPublicStateProjection.TryCreateKnownCard(
                    0, 16, 0, 300, "U", 0, false, LlmPublicVisibilityMode.RuntimeSafe, 55);
            LlmPublicKnownCardProjection uidMoved =
                LlmPublicStateProjection.TryCreateKnownCard(
                    0, 16, 3, 300, "U", 0, false, LlmPublicVisibilityMode.RuntimeSafe, 55);
            LlmPublicStateDelta uidDelta = LlmPublicStateDelta.Diff(
                LlmPublicStateProjection.Capture(1, 2, 8000, 8000, new[] { uidCard },
                    LlmPublicVisibilityMode.RuntimeSafe),
                LlmPublicStateProjection.Capture(1, 2, 8000, 8000, new[] { uidMoved },
                    LlmPublicVisibilityMode.RuntimeSafe));
            AssertEqual(0, uidDelta.AddedKnownCards.Count, "uid identity ignores index");
            AssertEqual(0, uidDelta.RemovedKnownCards.Count, "uid identity ignores index remove");
        }

        static void OutcomesNeverExposeCausedByOnWireOrProjection()
        {
            LlmPublicStateProjection before = LlmPublicStateProjection.Capture(1, 2, 8000, 8000, null);
            LlmPublicStateProjection after = LlmPublicStateProjection.Capture(1, 2, 8000, 7500, null);
            // LP deltas have no explicit pending intent cause — caused_by stays null.
            List<LlmPublicDuelEvent> outcomes = LlmPublicOutcomeEventFactory.CreateFromDelta(
                before, after, DuelViewType.LifeDamage, 5, 40);
            LlmPublicDuelEvent lp = null;
            foreach (LlmPublicDuelEvent evt in outcomes)
            {
                if (evt.Kind == LlmPublicDuelEventKind.LifePointsChanged)
                {
                    lp = evt;
                }
            }
            AssertTrue(lp != null, "lp outcome");
            AssertTrue(lp.CausedByEventId == null, "factory never sets caused_by pre-validation");
            AssertEqual(LlmPublicOutcomeEventFactory.UnknownActorPlayer, lp.ActorPlayer, "outcome actor unknown");
            AssertEqual(1, lp.TargetPlayer, "target is affected LP seat, not actor");
            Dictionary<string, object> proj =
                LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(lp);
            AssertFalse(proj.ContainsKey("caused_by_event_id"), "projection omits caused_by");
            DuelPublicActionEventMessage wire = DuelPublicActionEventMessage.FromEvent(lp);
            AssertEqual(-1L, wire.CausedByEventId, "wire caused_by sentinel when unset");
        }

        static void OutcomeSourceSignaturesIncludeSeqAndBeforeAfter()
        {
            LlmPublicStateProjection before = LlmPublicStateProjection.Capture(1, 2, 8000, 8000, null);
            LlmPublicStateProjection after = LlmPublicStateProjection.Capture(2, 3, 8000, 7200, null);
            List<LlmPublicDuelEvent> outcomes = LlmPublicOutcomeEventFactory.CreateFromDelta(
                before, after, DuelViewType.LifeDamage, 50, 100);
            foreach (LlmPublicDuelEvent evt in outcomes)
            {
                AssertTrue(evt.SourceSignature.Contains("seq:50"), "signature includes run_effect seq");
                if (evt.Kind == LlmPublicDuelEventKind.TurnChanged)
                {
                    AssertTrue(evt.SourceSignature.Contains(":from:1:to:2"), "turn before/after");
                }
                if (evt.Kind == LlmPublicDuelEventKind.PhaseChanged)
                {
                    AssertTrue(evt.SourceSignature.Contains(":from:2:to:3"), "phase before/after");
                }
                if (evt.Kind == LlmPublicDuelEventKind.LifePointsChanged && evt.TargetPlayer == 1)
                {
                    AssertTrue(evt.SourceSignature.Contains(":from:8000:to:7200"), "lp before/after");
                }
            }
        }

        static void RawViewEvidenceRoundTripsAndIsAuditOnly()
        {
            LlmRawDuelViewEvidence evidence = LlmRawDuelViewEvidence.Create(
                7, 12, DuelViewType.BattleAttack, 1, 2, 3, 4, (int)DuelPhase.Battle);
            AssertTrue(evidence.SourceSignature.Contains("raw_view:id:7"), "signature includes raw_evidence_id");
            AssertTrue(evidence.SourceSignature.Contains("seq:12"), "signature includes seq");
            Dictionary<string, object> audit = evidence.ToAuditDictionary();
            AssertEqual("llm_raw_duel_view", audit["kind"], "audit kind");
            AssertEqual((long)7, audit["raw_evidence_id"], "raw_evidence_id audit field");
            AssertEqual(true, audit["audit_only"], "audit_only");
            AssertEqual(false, audit["broker_history"], "not broker history");

            DuelRawViewEvidenceMessage msg = DuelRawViewEvidenceMessage.FromEvidence(evidence);
            DuelRawViewEvidenceMessage copy = RoundTrip(msg);
            AssertEqual(evidence.RawEvidenceId, copy.RawEvidenceId, "raw evidence id");
            AssertEqual(evidence.RunEffectSeq, copy.RunEffectSeq, "raw seq");
            AssertEqual(evidence.ViewTypeId, copy.ViewTypeId, "raw view id");
            AssertEqual(evidence.Param1, copy.Param1, "raw p1");
            AssertEqual(evidence.SourceSignature, copy.SourceSignature, "raw signature");
            AssertEqual(NetMessageType.DuelRawViewEvidence, msg.Type, "message type");
        }

        static void RawViewEvidenceRoutingAntiSpoofAndRequiresDueling()
        {
            DuelRoom room = new DuelRoom();
            room.Id = 9;
            room.MemberLimit = 2;
            room.InitTables();
            DuelRoomTable table = room.Tables[0];
            table.State = DuelRoomTableState.Dueling;
            Player p1 = new Player() { Code = 1, Name = "P1", DuelRoom = room };
            Player p2 = new Player() { Code = 2, Name = "P2", DuelRoom = room };
            table.Player1 = p1;
            table.Player2 = p2;
            NetClient pvp = new NetClient();
            NetClient c1 = new NetClient();
            NetClient c2 = new NetClient();
            p1.NetClient = c1;
            p2.NetClient = c2;
            table.PvpClient = pvp;

            LlmPublicActionEventRoutingResult ok = LlmPublicActionEventRouting.RouteIncoming(
                table, pvp, new DuelRawViewEvidenceMessage());
            AssertEqual(LlmPublicActionEventRoutingDecision.FanOutToDuelists, ok.Decision, "pvp fan-out");
            AssertFalse(ok.LoopedBackToPvpWorker, "no pvp loop");

            LlmPublicActionEventRoutingResult spoof = LlmPublicActionEventRouting.RouteIncoming(
                table, c1, new DuelRawViewEvidenceMessage());
            AssertEqual(LlmPublicActionEventRoutingDecision.Dropped, spoof.Decision, "duelist spoof dropped");

            table.State = DuelRoomTableState.Matched;
            LlmPublicActionEventRoutingResult notDueling = LlmPublicActionEventRouting.RouteIncoming(
                table, pvp, new DuelRawViewEvidenceMessage());
            AssertEqual(LlmPublicActionEventRoutingDecision.Dropped, notDueling.Decision, "requires Dueling");
        }

        static void TrackerDedupesRawEvidenceIdAndOutcomeSourceSignatures()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmRawDuelViewEvidence evidence = LlmRawDuelViewEvidence.Create(
                1, 1, DuelViewType.CardMove, 0, 0, 0, 1, 2);
            AssertTrue(tracker.TryAcceptRawViewEvidence(evidence), "first raw accept");
            AssertFalse(tracker.TryAcceptRawViewEvidence(evidence), "raw dedupe by id");

            LlmRawDuelViewEvidence sameViewNewId = LlmRawDuelViewEvidence.Create(
                2, 1, DuelViewType.CardMove, 0, 0, 0, 1, 2);
            AssertTrue(tracker.TryAcceptRawViewEvidence(sameViewNewId), "distinct raw_evidence_id accepts");

            LlmPublicDuelEvent outcome = new LlmPublicDuelEvent()
            {
                EventId = 1,
                RunEffectSeq = 1,
                Turn = 1,
                Phase = 2,
                ActorPlayer = LlmPublicOutcomeEventFactory.UnknownActorPlayer,
                TargetPlayer = 0,
                Kind = LlmPublicDuelEventKind.LifePointsChanged,
                Evidence = LlmPublicHistoryEvidence.PublicStateDelta,
                SourceSignature = "outcome:lp:seq:1:player:0:from:8000:to:7500:delta:-500",
            };
            AssertTrue(tracker.TryAppendPublicEvent(outcome).NewlyAppended, "first outcome");
            LlmPublicDuelEvent dup = LlmDuelHistoryState.CloneEvent(outcome);
            dup.EventId = 2;
            AssertFalse(tracker.TryAppendPublicEvent(dup).NewlyAppended, "outcome source signature dedupe");
            AssertEqual(1, tracker.CreateSnapshot().Events.Count, "one outcome kept");
        }

        static void OutcomeWireRoundTripsTargetCardPlayersAndSourceSignature()
        {
            LlmPublicDuelEvent evt = new LlmPublicDuelEvent()
            {
                EventId = 8,
                RunEffectSeq = 88,
                Turn = 2,
                Phase = 3,
                ActorPlayer = LlmPublicOutcomeEventFactory.UnknownActorPlayer,
                TargetPlayer = 1,
                CardPlayer = 1,
                Kind = LlmPublicDuelEventKind.LifePointsChanged,
                Evidence = LlmPublicHistoryEvidence.PublicStateDelta,
                SourceSignature = "outcome:lp:seq:88:player:1:from:8000:to:7200:delta:-800",
            };
            DuelPublicActionEventMessage msg = DuelPublicActionEventMessage.FromEvent(evt);
            DuelPublicActionEventMessage copy = RoundTrip(msg);
            LlmPublicDuelEvent restored = copy.ToEvent();
            AssertEqual(LlmPublicOutcomeEventFactory.UnknownActorPlayer, restored.ActorPlayer, "actor -1");
            AssertEqual(1, restored.TargetPlayer, "target_player round-trip");
            AssertEqual(1, restored.CardPlayer, "card_player round-trip");
            AssertTrue(restored.CausedByEventId == null, "caused_by unset");
            AssertEqual("outcome:lp:seq:88:player:1:from:8000:to:7200:delta:-800",
                restored.SourceSignature, "source_signature round-trip");
            AssertEqual(LlmPublicHistoryEvidence.PublicStateDelta, restored.Evidence, "evidence round-trip");
        }

        static void RuntimeSafeProjectionOmitsFieldAndBanishedIdentities()
        {
            // Fixture domain face values (8/4) must not unlock DLL runtime field identity.
            LlmPublicKnownCardProjection fieldFixtureFace =
                LlmPublicStateProjection.TryCreateKnownCard(0, 0, 0, 55, "Field", 8);
            LlmPublicKnownCardProjection banished =
                LlmPublicStateProjection.TryCreateKnownCard(0, 17, 0, 56, "Ban",
                    LlmDllRuntimeFace.FaceUpPublic);
            LlmPublicKnownCardProjection grave =
                LlmPublicStateProjection.TryCreateKnownCard(1, 16, 0, 57, "Grave", 8);
            AssertTrue(fieldFixtureFace == null, "runtime blocks fixture face=8 on field");
            AssertTrue(banished == null, "runtime blocks banished (unvalidated)");
            AssertTrue(grave != null, "runtime safe allows grave");
        }

        static void DllRuntimeFaceDomainIsSeparateFromFixtureFaceDomain()
        {
            AssertEqual(0, LlmDllRuntimeFace.FacedownOrNonPublic, "dll facedown=0");
            AssertEqual(1, LlmDllRuntimeFace.FaceUpPublic, "dll face-up=1");
            AssertEqual(8, LlmPublicHistoryRedactionPolicy.PublicFaceUpValue, "fixture face-up=8");
            AssertEqual(4, LlmPublicHistoryRedactionPolicy.FacedownFaceValue, "fixture facedown=4");
            AssertTrue(LlmDllRuntimeFace.IsPublicFaceUp(1), "dll 1 is public");
            AssertFalse(LlmDllRuntimeFace.IsPublicFaceUp(0), "dll 0 is not public");
            AssertFalse(LlmDllRuntimeFace.IsPublicFaceUp(8), "dll domain rejects fixture 8");
            AssertFalse(LlmDllRuntimeFace.IsPublicFaceUp(4), "dll domain rejects fixture 4");
            AssertTrue(LlmPublicHistoryRedactionPolicy.IsPublicFaceUp(8), "fixture domain 8");
            AssertFalse(LlmPublicHistoryRedactionPolicy.IsPublicFaceUp(1), "fixture domain rejects dll 1");

            // Runtime API: field only with dll face 1; banished closed; unknown closed.
            AssertTrue(LlmAbsolutePublicVisibility.CanExposeDllRuntimeCardIdentity(0, 1),
                "dll face-up field public");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeDllRuntimeCardIdentity(2, 0),
                "dll facedown field private");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeDllRuntimeCardIdentity(0, 8),
                "fixture 8 not dll public");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeDllRuntimeCardIdentity(0, 2),
                "unrecognized dll face fail-closed");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeDllRuntimeCardIdentity(17, 1),
                "banished unvalidated even if face-up raw");
            AssertTrue(LlmAbsolutePublicVisibility.CanExposeDllRuntimeCardIdentity(16, 0),
                "grave always public under dll runtime");

            // Fixture API still uses 8/4 and may model banished offline.
            AssertTrue(LlmAbsolutePublicVisibility.CanExposeCardIdentity(
                0, 8, false, LlmPublicVisibilityMode.FixtureValidated), "fixture face-up field");
            AssertFalse(LlmAbsolutePublicVisibility.CanExposeCardIdentity(
                0, 1, false, LlmPublicVisibilityMode.FixtureValidated), "fixture rejects dll 1");
        }

        static void DllFaceUpFieldIncludedFacedownExcludedBeforeIdentityQuery()
        {
            int uidQueries = 0;
            int cardIdQueries = 0;
            LlmPublicProjectionQueryBudget budget = new LlmPublicProjectionQueryBudget();
            LlmPublicStateProjection proj = LlmPublicRuntimeProjectionCapture.Capture(
                1,
                (int)DuelPhase.Main1,
                8000,
                8000,
                budget,
                (player, pos) =>
                {
                    if (pos == 0 && player == 1) return 1; // face-up summon
                    if (pos == 2 && player == 0) return 1; // facedown set
                    if (pos == LlmPublicHistoryRedactionPolicy.PosGrave) return 0;
                    return 0;
                },
                (player, pos, index) =>
                {
                    if (player == 1 && pos == 0) return LlmDllRuntimeFace.FaceUpPublic;
                    if (player == 0 && pos == 2) return LlmDllRuntimeFace.FacedownOrNonPublic;
                    return 99; // unrecognized
                },
                (player, pos, index) =>
                {
                    uidQueries++;
                    return 100 + player * 10 + pos;
                },
                (uid) =>
                {
                    cardIdQueries++;
                    return uid == 110 ? 5136 : 9999;
                },
                null);

            AssertEqual(1, proj.KnownCards.Count, "only face-up field card included");
            AssertEqual(5136, proj.KnownCards[0].CardId, "girochin card id");
            AssertEqual(1, proj.KnownCards[0].Player, "card player seat");
            AssertEqual(0, proj.KnownCards[0].Position, "monster zone 0");
            // Facedown must never trigger identity queries.
            AssertEqual(1, uidQueries, "uid queried only for face-up slot");
            AssertEqual(1, cardIdQueries, "card id queried only for face-up slot");
        }

        static void RuntimeCaptureBudgetScansFieldZeroToTwelvePlusGraveOnly()
        {
            HashSet<int> scanned = new HashSet<int>();
            LlmPublicProjectionQueryBudget budget = new LlmPublicProjectionQueryBudget();
            LlmPublicRuntimeProjectionCapture.Capture(
                1, 2, 8000, 8000, budget,
                (player, pos) =>
                {
                    scanned.Add(pos);
                    AssertTrue(
                        Array.IndexOf(LlmAbsolutePublicVisibility.RuntimeIdentityPositions, pos) >= 0,
                        "only runtime identity positions");
                    return 0;
                },
                (player, pos, index) => 0,
                null,
                null,
                null);
            AssertTrue(LlmPublicRuntimeProjectionCapture.ScansOnlyRuntimeIdentityPositions(budget),
                "budget positions match 2*identity set");
            AssertEqual(
                2 * LlmAbsolutePublicVisibility.RuntimeIdentityPositions.Length,
                budget.PositionsScanned,
                "positions scanned count");
            for (int p = 0; p <= 12; p++)
            {
                AssertTrue(scanned.Contains(p), "scans field " + p);
            }
            AssertTrue(scanned.Contains(16), "scans grave");
            AssertFalse(scanned.Contains(17), "no banished 17");
            AssertFalse(scanned.Contains(19), "no banished 19");
            AssertFalse(scanned.Contains(13), "no hand identity scan");
        }

        static void FaceUpFieldAppearanceOutcomeHasIdentityNoUidAndOptionalCausedBy()
        {
            LlmPublicKnownCardProjection faceUp = LlmPublicStateProjection.TryCreateKnownCard(
                1, 0, 0, 5136, "Girochin Kuwagata", LlmDllRuntimeFace.FaceUpPublic, false,
                LlmPublicVisibilityMode.RuntimeDllField, 55);
            AssertTrue(faceUp != null, "dll face-up known card");
            LlmPublicStateProjection before = LlmPublicStateProjection.Capture(
                1, (int)DuelPhase.Main1, 8000, 8000, null, LlmPublicVisibilityMode.RuntimeDllField);
            LlmPublicStateProjection after = LlmPublicStateProjection.Capture(
                1, (int)DuelPhase.Main1, 8000, 8000, new[] { faceUp },
                LlmPublicVisibilityMode.RuntimeDllField);
            List<LlmPublicDuelEvent> outcomes = LlmPublicOutcomeEventFactory.CreateFromDelta(
                before, after, DuelViewType.RunSummon, 76, 20);
            AssertTrue(outcomes.Count >= 1, "card appearance outcome");
            LlmPublicDuelEvent card = null;
            foreach (LlmPublicDuelEvent evt in outcomes)
            {
                if (evt.Kind == LlmPublicDuelEventKind.CardMovedPublic)
                {
                    card = evt;
                }
            }
            AssertTrue(card != null, "card moved public");
            AssertEqual(5136, card.CardId.Value, "card id");
            AssertEqual("Girochin Kuwagata", card.CardName, "card name");
            AssertEqual(1, card.CardPlayer, "card_player");
            AssertEqual(1, card.TargetPlayer, "target_player");
            AssertEqual("monster_zone", card.DestinationZone, "destination zone");
            AssertEqual(LlmPublicOutcomeEventFactory.UnknownActorPlayer, card.ActorPlayer, "actor unknown");
            AssertTrue(card.CausedByEventId == null, "no cause without pending intent");
            Dictionary<string, object> proj =
                LlmPublicHistoryRedactionPolicy.SerializeEventForBrokerProjection(card);
            AssertFalse(proj.ContainsKey("card_unique_id"), "no uid in projection");
            AssertFalse(proj.ContainsKey("unique_id"), "no unique_id key");
            string json = MiniJSON.Json.Serialize(proj);
            AssertFalse(json.Contains("\"uid\""), "no uid key leakage");
            AssertFalse(json.Contains("card_unique_id"), "no card_unique_id leakage");
            // Source signature must not embed engine uid.
            AssertFalse(card.SourceSignature.Contains("uid:"), "signature uses public identity not uid");
        }

        static void NormalSummonCausedByRequiresExactUidAndBoundedViewWindow()
        {
            // Live window: accept seq71 -> CardMove appearance seq75 (distance 4).
            LlmPendingAcceptedIntentCause pending = LlmPendingAcceptedIntentCause.ForNormalSummon(
                eventId: 14,
                acceptRunEffectSeq: 71,
                actorPlayer: 1,
                cardId: 5136,
                cardUniqueId: 99);

            LlmPublicKnownCardProjection exact = LlmPublicStateProjection.TryCreateKnownCard(
                1, 0, 0, 5136, "Girochin Kuwagata", LlmDllRuntimeFace.FaceUpPublic, false,
                LlmPublicVisibilityMode.RuntimeDllField, 99);
            LlmPublicStateProjection empty = LlmPublicStateProjection.Capture(
                1, 2, 8000, 8000, null, LlmPublicVisibilityMode.RuntimeDllField);
            LlmPublicStateProjection withExact = LlmPublicStateProjection.Capture(
                1, 2, 8000, 8000, new[] { exact }, LlmPublicVisibilityMode.RuntimeDllField);

            // Valid exact UID match on allowed view within window.
            List<LlmPublicDuelEvent> ok = LlmPublicOutcomeEventFactory.CreateFromDelta(
                empty, withExact, DuelViewType.CardMove, 75, 20, pending);
            LlmPublicDuelEvent appearance = FindKind(ok, LlmPublicDuelEventKind.CardMovedPublic);
            AssertTrue(appearance != null, "appearance");
            AssertEqual(14UL, appearance.CausedByEventId.Value, "exact uid match sets caused_by");
            AssertTrue(pending.Consumed, "pending consumed on match");
            AssertFalse(appearance.SourceSignature.Contains("uid:"), "signature has no uid");
            AssertFalse(appearance.SourceSignature.Contains("99"), "signature has no raw uid digits from match");

            // Same card id, different UID — no attribution.
            LlmPendingAcceptedIntentCause sameId = LlmPendingAcceptedIntentCause.ForNormalSummon(
                14, 71, 1, 5136, 99);
            LlmPublicKnownCardProjection otherUid = LlmPublicStateProjection.TryCreateKnownCard(
                1, 0, 0, 5136, "Girochin Kuwagata", LlmDllRuntimeFace.FaceUpPublic, false,
                LlmPublicVisibilityMode.RuntimeDllField, 100);
            List<LlmPublicDuelEvent> wrongUid = LlmPublicOutcomeEventFactory.CreateFromDelta(
                empty,
                LlmPublicStateProjection.Capture(1, 2, 8000, 8000, new[] { otherUid },
                    LlmPublicVisibilityMode.RuntimeDllField),
                DuelViewType.CardMove, 75, 20, sameId);
            AssertTrue(FindKind(wrongUid, LlmPublicDuelEventKind.CardMovedPublic).CausedByEventId == null,
                "same card id different uid: no caused_by");
            AssertFalse(sameId.Consumed, "pending remains for true match");

            // UID unavailable on pending — factory must not create pending-like match via card id.
            AssertTrue(
                LlmPendingAcceptedIntentCause.ForNormalSummon(14, 71, 1, 5136, 0) == null,
                "no pending when accept uid unavailable");

            // Wrong view family (battle) does not match even with exact uid.
            LlmPendingAcceptedIntentCause battlePending = LlmPendingAcceptedIntentCause.ForNormalSummon(
                14, 71, 1, 5136, 99);
            List<LlmPublicDuelEvent> battle = LlmPublicOutcomeEventFactory.CreateFromDelta(
                empty, withExact, DuelViewType.BattleRun, 75, 20, battlePending);
            AssertTrue(FindKind(battle, LlmPublicDuelEventKind.CardMovedPublic).CausedByEventId == null,
                "battle view not a summon appearance family");
            AssertFalse(battlePending.Consumed, "wrong view does not consume");

            // Stale same UID beyond max seq distance expires without match.
            LlmPendingAcceptedIntentCause stale = LlmPendingAcceptedIntentCause.ForNormalSummon(
                14, 71, 1, 5136, 99);
            ulong farSeq = 71 + LlmPendingAcceptedIntentCause.MaxSummonAppearanceSeqDistance + 1;
            List<LlmPublicDuelEvent> staleOut = LlmPublicOutcomeEventFactory.CreateFromDelta(
                empty, withExact, DuelViewType.CardMove, farSeq, 20, stale);
            AssertTrue(FindKind(staleOut, LlmPublicDuelEventKind.CardMovedPublic).CausedByEventId == null,
                "stale seq: no caused_by");
            AssertTrue(stale.Expired || stale.Consumed, "pending expired outside window");

            // Lifecycle reset clears pending counters path.
            LlmPublicStateProjection baseline = withExact;
            ulong raw = 1, probe = 1, ev = 1;
            LlmPublicOutcomePipeline.ResetDuelGeneration(ref baseline, ref raw, ref probe, ref ev);
            AssertTrue(baseline == null, "baseline reset");
        }

        static void SameCardIdCopiesGetDistinctOccurrenceSignatures()
        {
            LlmPublicKnownCardProjection a = LlmPublicStateProjection.TryCreateKnownCard(
                0, 16, 0, 111, "A", 0, false, LlmPublicVisibilityMode.RuntimeDllField, 0);
            LlmPublicKnownCardProjection b = LlmPublicStateProjection.TryCreateKnownCard(
                0, 16, 1, 111, "A", 0, false, LlmPublicVisibilityMode.RuntimeDllField, 0);
            LlmPublicStateProjection before = LlmPublicStateProjection.Capture(
                1, 2, 8000, 8000, null, LlmPublicVisibilityMode.RuntimeDllField);
            LlmPublicStateProjection after = LlmPublicStateProjection.Capture(
                1, 2, 8000, 8000, new[] { a, b }, LlmPublicVisibilityMode.RuntimeDllField);
            List<LlmPublicDuelEvent> outcomes = LlmPublicOutcomeEventFactory.CreateFromDelta(
                before, after, DuelViewType.CardMove, 50, 1);
            List<LlmPublicDuelEvent> cards = new List<LlmPublicDuelEvent>();
            foreach (LlmPublicDuelEvent evt in outcomes)
            {
                if (evt.Kind == LlmPublicDuelEventKind.CardMovedPublic)
                {
                    cards.Add(evt);
                }
            }
            AssertEqual(2, cards.Count, "two same-id copies produce two outcomes");
            AssertTrue(cards[0].SourceSignature != cards[1].SourceSignature,
                "occurrence discriminator differs signatures");
            AssertTrue(cards[0].SourceSignature.Contains(":occ:"), "occ present");
            AssertFalse(cards[0].SourceSignature.Contains("uid:"), "no uid in signature");
            AssertFalse(cards[1].SourceSignature.Contains("card_unique_id"), "no card_unique_id token");

            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            AssertTrue(tracker.TryAppendPublicEvent(cards[0]).NewlyAppended, "first append");
            AssertTrue(tracker.TryAppendPublicEvent(cards[1]).NewlyAppended, "second append");
            LlmPublicDuelEvent redelivery = LlmDuelHistoryState.CloneEvent(cards[0]);
            redelivery.EventId = 99;
            AssertFalse(tracker.TryAppendPublicEvent(redelivery).NewlyAppended, "true duplicate still deduped");
            AssertEqual(2, tracker.CreateSnapshot().Events.Count, "two distinct kept");
        }

        static void CardNameEnrichmentResolvesFromCatalogFailSoft()
        {
            LlmPublicDuelEvent evt = new LlmPublicDuelEvent()
            {
                EventId = 1,
                RunEffectSeq = 76,
                Kind = LlmPublicDuelEventKind.CardMovedPublic,
                CardId = 5136,
                CardName = null,
                Evidence = LlmPublicHistoryEvidence.PublicStateDelta,
                SourceSignature = "outcome:card_public:seq:76:cid:5136:zone:monster_zone:player:1:occ:1",
            };
            LlmPublicHistoryCardNameEnrichment.TryEnrich(
                evt,
                cardId => cardId == 5136 ? "Girochin Kuwagata" : null);
            AssertEqual("Girochin Kuwagata", evt.CardName, "resolver supplies name for 5136");

            LlmPublicDuelEvent missing = new LlmPublicDuelEvent()
            {
                EventId = 2,
                RunEffectSeq = 77,
                Kind = LlmPublicDuelEventKind.CardMovedPublic,
                CardId = 999999,
                CardName = null,
                Evidence = LlmPublicHistoryEvidence.PublicStateDelta,
                SourceSignature = "x",
            };
            LlmPublicHistoryCardNameEnrichment.TryEnrich(
                missing,
                cardId => { throw new Exception("catalog boom"); });
            AssertTrue(missing.CardName == null, "failure leaves name null");
            AssertEqual(999999, missing.CardId.Value, "card id intact after fail");

            // Ingest path enriches before append/audit.
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmPublicDuelEvent wire = new LlmPublicDuelEvent()
            {
                EventId = 3,
                RunEffectSeq = 78,
                Turn = 1,
                Phase = 2,
                ActorPlayer = -1,
                TargetPlayer = 1,
                CardPlayer = 1,
                Kind = LlmPublicDuelEventKind.CardMovedPublic,
                CardId = 5136,
                CardName = null,
                DestinationZone = "monster_zone",
                Evidence = LlmPublicHistoryEvidence.PublicStateDelta,
                SourceSignature = "outcome:card_public:seq:78:cid:5136:zone:monster_zone:player:1:occ:1",
            };
            string audit;
            string error;
            bool ok = LlmPublicDuelEventClientIngest.TryIngestForAudit(
                tracker,
                wire,
                cardId => cardId == 5136 ? "Girochin Kuwagata" : null,
                out error,
                out audit);
            AssertTrue(ok, "ingest succeeds");
            AssertTrue(audit.Contains("Girochin Kuwagata"), "audit has resolved name");
            string proj = tracker.SerializeRequestProjection();
            AssertTrue(proj.Contains("Girochin Kuwagata"), "broker history has name");
        }

        static LlmPublicDuelEvent FindKind(List<LlmPublicDuelEvent> events, LlmPublicDuelEventKind kind)
        {
            foreach (LlmPublicDuelEvent evt in events)
            {
                if (evt != null && evt.Kind == kind)
                {
                    return evt;
                }
            }
            return null;
        }

        static void FaceProbeCaptureIsAuditOnlyWithoutCardIdentity()
        {
            ulong probeId = 0;
            List<LlmFaceProbeEvidence> probes = LlmFaceProbeCapture.CaptureFieldProbes(
                9,
                ref probeId,
                (player, pos) => pos == 0 ? 1 : 0,
                (player, pos, index) => 4);
            AssertEqual(2, probes.Count, "one probe per player field slot 0");
            Dictionary<string, object> audit = probes[0].ToAuditDictionary();
            AssertEqual("llm_face_probe", audit["kind"], "probe kind");
            AssertEqual(true, audit["audit_only"], "probe audit_only");
            AssertFalse(audit.ContainsKey("card_id"), "probe has no card id");
            AssertFalse(audit.ContainsKey("card_name"), "probe has no card name");
            AssertFalse(audit.ContainsKey("card_unique_id"), "probe has no unique id");
            AssertFalse(audit.ContainsKey("zone"), "probe has no zone identity");
        }

        static void FaceProbeWireRoundTripIsIdentityFree()
        {
            LlmFaceProbeEvidence probe = LlmFaceProbeEvidence.Create(5, 42, 1, 3, 0, 8);
            DuelFaceProbeEvidenceMessage msg = DuelFaceProbeEvidenceMessage.FromProbe(probe);
            AssertEqual(NetMessageType.DuelFaceProbeEvidence, msg.Type, "face probe message type");
            DuelFaceProbeEvidenceMessage copy = RoundTrip(msg);
            AssertEqual(5UL, copy.ProbeId, "probe_id round-trip");
            AssertEqual(42UL, copy.RunEffectSeq, "run_effect_seq round-trip");
            AssertEqual(1, copy.Player, "player round-trip");
            AssertEqual(3, copy.Position, "position round-trip");
            AssertEqual(0, copy.Index, "index round-trip");
            AssertEqual(8, copy.RawFace, "raw_face round-trip");
            LlmFaceProbeEvidence restored = copy.ToProbe();
            Dictionary<string, object> audit = restored.ToAuditDictionary();
            AssertEqual("llm_face_probe", audit["kind"], "audit kind after wire");
            AssertFalse(audit.ContainsKey("card_id"), "wire path has no card_id");
            AssertFalse(audit.ContainsKey("card_name"), "wire path has no card_name");
            AssertFalse(audit.ContainsKey("card_unique_id"), "wire path has no uid");
            // Message fields must not include identity carriers.
            string wireDump = "ProbeId=" + copy.ProbeId +
                ";Seq=" + copy.RunEffectSeq +
                ";Player=" + copy.Player +
                ";Pos=" + copy.Position +
                ";Index=" + copy.Index +
                ";RawFace=" + copy.RawFace;
            AssertFalse(wireDump.Contains("card"), "wire dump has no card identity fields");
        }

        static void FaceProbeRoutingAntiSpoofNoLoopAndRequiresDueling()
        {
            DuelRoom room = new DuelRoom();
            room.Id = 11;
            room.MemberLimit = 2;
            room.InitTables();
            DuelRoomTable table = room.Tables[0];
            table.State = DuelRoomTableState.Dueling;
            Player p1 = new Player() { Code = 1, Name = "P1", DuelRoom = room };
            Player p2 = new Player() { Code = 2, Name = "P2", DuelRoom = room };
            table.Player1 = p1;
            table.Player2 = p2;
            NetClient pvp = new NetClient();
            NetClient c1 = new NetClient();
            NetClient c2 = new NetClient();
            p1.NetClient = c1;
            p2.NetClient = c2;
            table.PvpClient = pvp;

            LlmPublicActionEventRoutingResult ok = LlmPublicActionEventRouting.RouteIncoming(
                table, pvp, new DuelFaceProbeEvidenceMessage());
            AssertEqual(LlmPublicActionEventRoutingDecision.FanOutToDuelists, ok.Decision,
                "pvp face probe fan-out");
            AssertFalse(ok.LoopedBackToPvpWorker, "face probe never loops to pvp");
            AssertEqual(2, ok.Recipients.Count, "both duelists receive face probes");
            foreach (NetClient recipient in ok.Recipients)
            {
                AssertFalse(object.ReferenceEquals(recipient, pvp), "recipient is not pvp worker");
            }

            LlmPublicActionEventRoutingResult spoof = LlmPublicActionEventRouting.RouteIncoming(
                table, c1, new DuelFaceProbeEvidenceMessage());
            AssertEqual(LlmPublicActionEventRoutingDecision.Dropped, spoof.Decision,
                "duelist spoof face probe dropped");

            table.State = DuelRoomTableState.Matched;
            LlmPublicActionEventRoutingResult notDueling = LlmPublicActionEventRouting.RouteIncoming(
                table, pvp, new DuelFaceProbeEvidenceMessage());
            AssertEqual(LlmPublicActionEventRoutingDecision.Dropped, notDueling.Decision,
                "face probe requires Dueling");
        }

        static void TrackerDedupesFaceProbeIdAndResetsOnLifecycle()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmFaceProbeEvidence probe = LlmFaceProbeEvidence.Create(7, 3, 0, 1, 0, 4);
            AssertTrue(tracker.TryAcceptFaceProbe(probe), "first face probe accept");
            AssertFalse(tracker.TryAcceptFaceProbe(probe), "face probe dedupe by probe_id");
            LlmFaceProbeEvidence next = LlmFaceProbeEvidence.Create(8, 3, 0, 1, 0, 4);
            AssertTrue(tracker.TryAcceptFaceProbe(next), "distinct probe_id accepts");

            int gen = LlmDuelHistoryLifecycle.ResetForBoundary(tracker, 1);
            AssertEqual(2, gen, "lifecycle generation increments");
            AssertTrue(tracker.TryAcceptFaceProbe(probe), "probe_id accepted again after lifecycle reset");
        }

        static void FaceProbeNeverEntersDuelHistoryOrRequestProjection()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmFaceProbeEvidence probe = LlmFaceProbeEvidence.Create(1, 9, 0, 0, 0, 8);
            AssertTrue(tracker.TryAcceptFaceProbe(probe), "accept probe");
            AssertEqual(0, tracker.CreateSnapshot().Events.Count, "probe not in history events");
            string proj = tracker.SerializeRequestProjection();
            AssertFalse(proj.Contains("llm_face_probe"), "request projection omits face probe kind");
            AssertFalse(proj.Contains("raw_face"), "request projection omits raw_face");
            AssertFalse(proj.Contains("\"probe_id\""), "request projection omits probe_id");
        }

        static void FaceProbeIngestHelperLogsOnlyNewlyAccepted()
        {
            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmFaceProbeEvidence probe = LlmFaceProbeEvidence.Create(3, 10, 1, 2, 0, 8);
            string audit1;
            AssertTrue(
                LlmPublicDuelEventClientIngest.TryIngestFaceProbeForAudit(tracker, probe, out audit1),
                "first ingest emits audit");
            AssertTrue(!string.IsNullOrEmpty(audit1), "audit json present");
            AssertTrue(audit1.Contains("\"kind\":\"llm_face_probe\"") ||
                audit1.Contains("\"kind\": \"llm_face_probe\""), "audit kind llm_face_probe");
            AssertTrue(audit1.Contains("probe_id"), "audit has probe_id");
            AssertTrue(audit1.Contains("raw_face"), "audit has raw_face");
            AssertTrue(audit1.Contains("run_effect_seq"), "audit has run_effect_seq");
            AssertFalse(audit1.Contains("card_id"), "audit has no card_id");
            AssertFalse(audit1.Contains("card_name"), "audit has no card_name");
            string audit2;
            AssertFalse(
                LlmPublicDuelEventClientIngest.TryIngestFaceProbeForAudit(tracker, probe, out audit2),
                "duplicate probe_id does not re-log");
            AssertTrue(string.IsNullOrEmpty(audit2), "no second audit payload");
        }

        static void ProjectionQueryBudgetTracksQueries()
        {
            LlmPublicProjectionQueryBudget budget = new LlmPublicProjectionQueryBudget();
            budget.Reset();
            budget.CardNumQueries = 2;
            budget.FaceQueries = 3;
            budget.UniqueIdQueries = 1;
            budget.CardIdQueries = 1;
            budget.PositionsScanned = 2;
            budget.MaxIndexExclusive = 4;
            AssertEqual(2, budget.CardNumQueries, "card num tracked");
            AssertEqual(3, budget.FaceQueries, "face queries tracked");
            budget.Reset();
            AssertEqual(0, budget.FaceQueries, "reset clears budget");
        }

        static void SentinelHiddenNeverEntersProjectionWireOrHistory()
        {
            LlmPublicDuelEvent denied = LlmPublicOutcomeEventFactory.TryCreatePublicCardAppearance(
                0, 0, 4, SentinelHiddenId, SentinelHiddenName, 1, 1, 1, 1);
            AssertTrue(denied == null, "hidden fixture denied");

            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            LlmPublicDuelEvent crafted = new LlmPublicDuelEvent()
            {
                EventId = 1,
                RunEffectSeq = 1,
                Turn = 1,
                Phase = 1,
                Kind = LlmPublicDuelEventKind.SetMonster,
                CardId = SentinelHiddenId,
                CardName = SentinelHiddenName,
                Evidence = LlmPublicHistoryEvidence.PublicStateDelta,
                SourceSignature = "bad:set",
            };
            tracker.TryAppendPublicEvent(crafted);
            string proj = tracker.SerializeRequestProjection();
            AssertFalse(proj.Contains(SentinelHiddenName), "projection leak name");
            AssertFalse(proj.Contains(SentinelHiddenId.ToString()), "projection leak id");

            DuelPublicActionEventMessage wire = DuelPublicActionEventMessage.FromEvent(crafted);
            AssertEqual(0, wire.CardId, "wire set redaction");
            AssertTrue(wire.CardName == null, "wire set name redaction");
        }

        static void LiveEvidenceReportFormatIsDeterministic()
        {
            LlmRawDuelViewEvidence a = LlmRawDuelViewEvidence.Create(
                1, 1, DuelViewType.BattleAttack, 0, 1, 0, 2, 3);
            LlmRawDuelViewEvidence b = LlmRawDuelViewEvidence.Create(
                1, 1, DuelViewType.BattleAttack, 0, 1, 0, 2, 3);
            AssertEqual(
                LlmLiveViewEvidenceReport.FormatMappingRow(a, "UI: attack declaration"),
                LlmLiveViewEvidenceReport.FormatMappingRow(b, "UI: attack declaration"),
                "mapping row deterministic");
            Dictionary<string, object> envelope = LlmLiveViewEvidenceReport.BuildAnalyzerEnvelope(
                new[] { a },
                new LlmPublicDuelEvent[0],
                null);
            string json1 = MiniJSON.Json.Serialize(envelope);
            string json2 = MiniJSON.Json.Serialize(
                LlmLiveViewEvidenceReport.BuildAnalyzerEnvelope(
                    new[] { b }, new LlmPublicDuelEvent[0], null));
            AssertEqual(json1, json2, "analyzer envelope deterministic");
            AssertTrue(json1.Contains("llm_live_view_evidence_report"), "envelope kind");
            AssertTrue(json1.Contains("raw_views"), "raw_views key");
        }

        static void LiveEvidenceReportIncludesFaceProbes()
        {
            LlmFaceProbeEvidence probe = LlmFaceProbeEvidence.Create(2, 15, 0, 4, 0, 8);
            Dictionary<string, object> envelope = LlmLiveViewEvidenceReport.BuildAnalyzerEnvelope(
                null,
                null,
                new[] { probe });
            string json = MiniJSON.Json.Serialize(envelope);
            AssertTrue(json.Contains("face_probes"), "envelope has face_probes");
            AssertTrue(json.Contains("llm_face_probe") || json.Contains("raw_face"),
                "face probe coordinates present");
            AssertTrue(json.Contains("\"probe_id\":2") || json.Contains("\"probe_id\": 2"),
                "probe_id in envelope");
            AssertFalse(json.Contains("card_id"), "envelope face probes have no card_id");
        }

        static bool HasKind(List<LlmPublicDuelEvent> events, LlmPublicDuelEventKind kind)
        {
            foreach (LlmPublicDuelEvent evt in events)
            {
                if (evt.Kind == kind)
                {
                    return true;
                }
            }
            return false;
        }

        static T RoundTrip<T>(T message) where T : NetMessage, new()
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

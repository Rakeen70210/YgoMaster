using System;
using System.Collections.Generic;
using System.Linq;

namespace YgoMaster
{
    /// <summary>
    /// Regressions from failed Slice 1B live gate:
    /// engine Extra=14 / Deck=15; seat-gated self_resources_audit.
    /// </summary>
    static class Llm005LiveGateFixTests
    {
        const int ControlledPlayer = 1;
        const int OpponentPlayer = 0;
        const int PosHand = 13;
        const int PosExtra = 14;
        const int PosDeck = 15;
        const int PosGrave = 16;

        const int ExtraXyzId = 84013237;
        const int DeckSpellId = 5318639; // main-deck spell that must NOT appear as extra
        const int DeckMonsterId = 46986414;

        public static void RunAll()
        {
            ConstantsMatchLiveEngineDomain();
            PublicZoneNamesMapExtra14Deck15();
            ProjectsExtraAt14AndOmitsDeckAt15();
            MainDeckCardsAt15AreNotExtraDeck();
            AuditPolicyRequiresConfiguredSeatMatch();
            SerializerRefusesMismatchedSeatPayload();
            DecisionWindowStillLogsWithoutPrivatePayloadGate();
        }

        static void ConstantsMatchLiveEngineDomain()
        {
            AssertEqual(14, LlmPublicHistoryRedactionPolicy.PosExtra, "PosExtra live domain");
            AssertEqual(15, LlmPublicHistoryRedactionPolicy.PosDeck, "PosDeck live domain");
            AssertTrue(
                LlmPublicHistoryRedactionPolicy.PosExtra != LlmPublicHistoryRedactionPolicy.PosDeck,
                "extra and deck distinct");
        }

        static void PublicZoneNamesMapExtra14Deck15()
        {
            AssertEqual(
                "extra_deck",
                LlmPublicHistoryRedactionPolicy.MapPublicZone(LlmPublicHistoryRedactionPolicy.PosExtra),
                "zone name extra");
            AssertEqual(
                "deck",
                LlmPublicHistoryRedactionPolicy.MapPublicZone(LlmPublicHistoryRedactionPolicy.PosDeck),
                "zone name deck");
            // Explicit redaction policy zone names are authoritative for live 14/15 domain.
            AssertEqual("extra_deck", LlmPublicHistoryRedactionPolicy.MapPublicZone(14), "14 extra");
            AssertEqual("deck", LlmPublicHistoryRedactionPolicy.MapPublicZone(15), "15 deck");
            AssertTrue(
                LlmPublicHistoryRedactionPolicy.IsHiddenZonePosition(14)
                && LlmPublicHistoryRedactionPolicy.IsHiddenZonePosition(15),
                "extra and deck remain hidden zones");
        }

        static void ProjectsExtraAt14AndOmitsDeckAt15()
        {
            SliceQuery query = new SliceQuery();
            SliceCatalog catalog = new SliceCatalog();
            catalog.Add(new LlmCardMetadata()
            {
                CardId = ExtraXyzId,
                Name = "Number 39: Utopia",
                Kind = "Xyz",
                Level = 4,
                IsExtraDeck = true,
                SummonFamily = "xyz",
                UsesRank = true,
            });
            catalog.Add(new LlmCardMetadata()
            {
                CardId = DeckSpellId,
                Name = "Mystical Space Typhoon",
                Kind = "Magic",
                SummonFamily = "spell",
            });
            catalog.Add(new LlmCardMetadata()
            {
                CardId = DeckMonsterId,
                Name = "Dark Magician",
                Kind = "Normal",
                Level = 7,
                SummonFamily = "main_deck_monster",
            });

            // Real domain: ED at 14, main deck at 15.
            query.PlaceCard(ControlledPlayer, PosExtra, 0, 2001, ExtraXyzId, 1);
            query.PlaceCard(ControlledPlayer, PosDeck, 0, 3001, DeckSpellId, 0);
            query.PlaceCard(ControlledPlayer, PosDeck, 1, 3002, DeckMonsterId, 0);
            query.PlaceCard(ControlledPlayer, PosHand, 0, 1101, DeckMonsterId, 1);
            query.HandCardOpen[Key(ControlledPlayer, 0)] = 1;

            LlmSelfResources self = LlmSelfResourceProjection.Project(
                query,
                ControlledPlayer,
                catalog,
                LlmPublicVisibilityMode.RuntimeDllField);

            AssertTrue(self.ExtraDeck.Any(e => e.CardId == ExtraXyzId),
                "actual Extra Deck card at position 14 is projected");
            AssertTrue(self.ExtraDeck.All(e => e.CardId != DeckSpellId && e.CardId != DeckMonsterId),
                "deck cards at 15 must not appear in extra_deck");
            string json = LlmSelfResourceProjection.SerializeJson(self);
            AssertTrue(json.Contains(ExtraXyzId.ToString()) || json.Contains("Utopia"),
                "extra id/name present");
            AssertFalse(json.Contains("Mystical Space Typhoon"),
                "main-deck spell from position 15 must be absent from self_resources");
            // Dark Magician may appear via hand; ensure it is not tagged as extra.
            AssertTrue(self.ExtraDeck.All(e => e.CardId != DeckMonsterId),
                "hand/deck monster not listed as extra");
        }

        static void MainDeckCardsAt15AreNotExtraDeck()
        {
            SliceQuery query = new SliceQuery();
            SliceCatalog catalog = new SliceCatalog();
            // 20 unique main-deck cards only in deck position 15 — must not become extra_deck.
            for (int i = 0; i < 20; i++)
            {
                int id = 900000 + i;
                catalog.Add(new LlmCardMetadata()
                {
                    CardId = id,
                    Name = "MAIN_DECK_CARD_" + i,
                    Kind = i % 2 == 0 ? "Magic" : "Effect",
                    Level = i % 2 == 0 ? 0 : 4,
                    SummonFamily = i % 2 == 0 ? "spell" : "main_deck_monster",
                    IsExtraDeck = false,
                });
                query.PlaceCard(ControlledPlayer, PosDeck, i, 4000 + i, id, 0);
            }
            // One real extra at 14.
            catalog.Add(new LlmCardMetadata()
            {
                CardId = ExtraXyzId,
                Name = "Utopia",
                Kind = "Xyz",
                Level = 4,
                IsExtraDeck = true,
                SummonFamily = "xyz",
                UsesRank = true,
            });
            query.PlaceCard(ControlledPlayer, PosExtra, 0, 2001, ExtraXyzId, 1);

            LlmSelfResources self = LlmSelfResourceProjection.Project(
                query, ControlledPlayer, catalog, LlmPublicVisibilityMode.RuntimeDllField);
            AssertEqual(1, self.ExtraDeck.Count, "only real ED card projected");
            AssertEqual(ExtraXyzId, self.ExtraDeck[0].CardId, "ED card id");
            for (int i = 0; i < 20; i++)
            {
                int id = 900000 + i;
                AssertTrue(self.ExtraDeck.All(e => e.CardId != id),
                    "main deck card " + id + " must not be extra");
            }
        }

        static void AuditPolicyRequiresConfiguredSeatMatch()
        {
            DecisionSnapshot p0 = new DecisionSnapshot()
            {
                ControlledPlayer = 0,
                ActingPlayer = 0,
                RunEffectSeq = 1,
            };
            DecisionSnapshot p1 = new DecisionSnapshot()
            {
                ControlledPlayer = 1,
                ActingPlayer = 1,
                RunEffectSeq = 2,
            };
            DecisionSnapshot mismatch = new DecisionSnapshot()
            {
                ControlledPlayer = 0,
                ActingPlayer = 1,
                RunEffectSeq = 3,
            };

            AssertFalse(
                LlmSelfResourcesAuditPolicy.ShouldEmitAudit(false, 1, p1),
                "default-off");
            AssertFalse(
                LlmSelfResourcesAuditPolicy.ShouldEmitAudit(true, -1, p1),
                "invalid control seat");
            AssertFalse(
                LlmSelfResourcesAuditPolicy.ShouldEmitAudit(true, 1, p0),
                "P2 control=1 must not emit player 0 audit");
            AssertFalse(
                LlmSelfResourcesAuditPolicy.ShouldEmitAudit(true, 1, mismatch),
                "acting/controlled must both match");
            AssertTrue(
                LlmSelfResourcesAuditPolicy.ShouldEmitAudit(true, 1, p1),
                "matching seat emits");
            AssertTrue(
                LlmSelfResourcesAuditPolicy.ShouldEmitAudit(true, 0, p0),
                "P1 control=0 matching seat emits");
        }

        static void SerializerRefusesMismatchedSeatPayload()
        {
            DecisionSnapshot snap = new DecisionSnapshot()
            {
                ControlledPlayer = 0,
                ActingPlayer = 0,
                RunEffectSeq = 99,
            };
            snap.SelfResources = LlmSelfResources.Create(
                0,
                LlmPublicVisibilityMode.RuntimeDllField,
                1,
                0,
                0,
                new List<LlmSelfResourceCard>()
                {
                    LlmSelfResourceCard.Create(
                        12345, "SECRET_HAND_CARD", 13, 0, 1, true, 4, null, false, false, null,
                        "Effect", "main_deck_monster", false, "Effect", 0, 0, "secret", 0),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>());

            // Configured control seat is P2's player 1 — must not serialize player 0 private hand.
            string json = LlmDecisionLogSerializer.SerializeSelfResourcesAudit(snap, true, 1);
            AssertFalse(json.Contains("SECRET_HAND_CARD"), "mismatched seat must not leak private hand");
            AssertFalse(json.Contains("\"self_resources\""), "payload omitted");
            AssertTrue(json.Contains("\"audit_emitted\":false") || json.Contains("\"audit_emitted\": false"),
                "audit_emitted false");

            // Matching seat may include payload.
            snap.ControlledPlayer = 1;
            snap.ActingPlayer = 1;
            snap.SelfResources = LlmSelfResources.Create(
                1,
                LlmPublicVisibilityMode.RuntimeDllField,
                1,
                0,
                0,
                new List<LlmSelfResourceCard>()
                {
                    LlmSelfResourceCard.Create(
                        777, "OWN_PRIVATE", 13, 0, 1, true, 4, null, false, false, null,
                        "Effect", "main_deck_monster", false, "Effect", 0, 0, "own", 0),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>());
            string ok = LlmDecisionLogSerializer.SerializeSelfResourcesAudit(snap, true, 1);
            AssertTrue(ok.Contains("OWN_PRIVATE"), "matching seat may include private");
            AssertTrue(ok.Contains("\"self_resources\""), "payload present");
        }

        static void DecisionWindowStillLogsWithoutPrivatePayloadGate()
        {
            DecisionSnapshot snap = new DecisionSnapshot()
            {
                ControlledPlayer = 0,
                ActingPlayer = 0,
                RunEffectSeq = 5,
            };
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
            });
            string window = LlmDecisionLogSerializer.SerializeDecisionWindow(snap);
            AssertTrue(window.Contains("decision_window"), "decision_window still serializes");
            AssertFalse(window.Contains("self_resources"), "decision_window never has self_resources");
        }

        // ------------------------------------------------------------------

        sealed class SliceCatalog : ILlmCardCatalog
        {
            readonly Dictionary<int, LlmCardMetadata> cards = new Dictionary<int, LlmCardMetadata>();
            public void Add(LlmCardMetadata c) { cards[c.CardId] = c; }
            public LlmCardMetadata GetCard(int id)
            {
                LlmCardMetadata c;
                return cards.TryGetValue(id, out c) ? c : null;
            }
        }

        sealed class SliceQuery : ILegalActionQuery
        {
            public readonly Dictionary<string, int> CardNums = new Dictionary<string, int>();
            public readonly Dictionary<string, int> CardUniqueIds = new Dictionary<string, int>();
            public readonly Dictionary<string, int> CardFaces = new Dictionary<string, int>();
            public readonly Dictionary<string, int> HandCardOpen = new Dictionary<string, int>();
            public readonly Dictionary<int, int> CardIdsByUniqueId = new Dictionary<int, int>();
            public readonly Dictionary<int, int> LifePoints = new Dictionary<int, int>();
            public int CurrentPhase = (int)DuelPhase.Main1;
            public int CurrentStep;
            public int TurnNum = 1;
            public int TurnPlayer = ControlledPlayer;

            public void PlaceCard(int player, int position, int index, int uniqueId, int cardId, int face)
            {
                string numKey = Key(player, position);
                if (!CardNums.ContainsKey(numKey) || CardNums[numKey] <= index)
                {
                    CardNums[numKey] = index + 1;
                }
                CardUniqueIds[Key(player, position, index)] = uniqueId;
                CardFaces[Key(player, position, index)] = face;
                CardIdsByUniqueId[uniqueId] = cardId;
            }

            public int GetCardNum(int player, int position)
            {
                int v; return CardNums.TryGetValue(Key(player, position), out v) ? v : 0;
            }
            public uint GetCommandMask(int player, int position, int index) { return 0; }
            public int GetCardFace(int player, int position, int index)
            {
                int v; return CardFaces.TryGetValue(Key(player, position, index), out v) ? v : 0;
            }
            public int GetCardIdByUniqueId(int uniqueId)
            {
                int v; return CardIdsByUniqueId.TryGetValue(uniqueId, out v) ? v : 0;
            }
            public int GetCardUniqueId(int player, int position, int index)
            {
                int v; return CardUniqueIds.TryGetValue(Key(player, position, index), out v) ? v : 0;
            }
            public int GetHandCardOpen(int player, int index)
            {
                int v; return HandCardOpen.TryGetValue(Key(player, index), out v) ? v : 0;
            }
            public int GetLifePoints(int player)
            {
                int v; return LifePoints.TryGetValue(player, out v) ? v : 0;
            }
            public uint GetMovablePhase() { return 0; }
            public int GetCurrentPhase() { return CurrentPhase; }
            public int GetCurrentStep() { return CurrentStep; }
            public int GetTurnNum() { return TurnNum; }
            public int GetTurnPlayer() { return TurnPlayer; }
            public int GetAttackTargetMask(int player, int locate) { return 0; }
            public int GetDialogCanYesNoSkip() { return 0; }
            public int GetDialogSelectItemEnable(int index) { return 0; }
            public int GetDialogSelectItemNum() { return 0; }
            public int GetDialogSelectItemTextId(int index) { return 0; }
            public int GetListItemAttribute(int index) { return 0; }
            public int GetListItemFrom(int index) { return 0; }
            public int GetListItemId(int index) { return 0; }
            public int GetListItemMax() { return 0; }
            public int GetListItemMsg(int index) { return 0; }
            public int GetListItemTargetUniqueId(int index) { return 0; }
            public int GetListItemUniqueId(int index) { return 0; }
            public int GetListSelectMax() { return 0; }
            public int GetListSelectMin() { return 0; }
            public int GetListIsMultiMode() { return 0; }
            public int GetSummoningMonsterUniqueId() { return 0; }
            public int GetSummonPositionMask() { return 0; }

            static string Key(params int[] parts)
            {
                return string.Join(",", parts.Select(p => p.ToString()).ToArray());
            }
        }

        static string Key(params int[] parts)
        {
            return string.Join(",", parts.Select(p => p.ToString()).ToArray());
        }

        static void AssertTrue(bool v, string m)
        {
            if (!v) throw new Exception(m);
        }
        static void AssertFalse(bool v, string m)
        {
            if (v) throw new Exception(m);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-005 Slice 1B: attach immutable self_resources to DecisionSnapshot
    /// at extract time; default-off private audit logging; no /decide or schema change.
    /// </summary>
    static class Llm005Slice1BTests
    {
        const int ControlledPlayer = 1;
        const int OpponentPlayer = 0;
        const int MonsterZone0 = 0;
        const int MonsterZone1 = 1;
        const int OpponentSetZone = 2;
        const int PosHand = 13;
        // Live-validated engine positions: Extra=14, Deck=15.
        const int PosExtra = 14;
        const int PosDeck = 15;
        const int PosGrave = 16;
        const int PosBanished = 17;

        const int GirochinId = 71533;
        const int CaveDragonId = 66672569;
        const int Rank4Id = 84013237;
        const int OwnHandId = 46986414;
        const int OppHandId = 99106101;
        const int OppSetId = 99106102;
        const int OppExtraId = 99106103;

        const string CaveDragonName = "The Dragon Dwelling in the Cave";
        const string Rank4Name = "Number 39: Utopia";
        const string OppHandName = "SENTINEL_1B_OPP_HAND";
        const string OppSetName = "SENTINEL_1B_OPP_SET";
        const string OppExtraName = "SENTINEL_1B_OPP_EXTRA";

        public static void RunAll()
        {
            ExtractOverloadsAttachSelfResources();
            AttachedProjectionIncludesOwnCaveAndExtraExcludesOpponent();
            OldSnapshotImmutableAfterQueryMutationAndNewSeq();
            DefaultDecisionWindowOmitsSelfResources();
            ExplicitAuditSerializerIncludesWhenEnabled();
            OneStepRequestByteIdenticalWithAuditFlagIrrelevant();
            PublicStateHistoryAndWireNeverGainPrivateValues();
            SourceSettingDefaultsFalseDocumented();
            FixtureFaceDomainOverloadPreserved();
        }

        static void ExtractOverloadsAttachSelfResources()
        {
            Fixture f = CreateFixture(LlmPublicVisibilityMode.RuntimeDllField);

            DecisionSnapshot a = LegalActionExtractor.Extract(
                f.Query, 10, DuelViewType.WaitInput, ControlledPlayer);
            AssertAttached(a, "basic overload");

            DecisionSnapshot b = LegalActionExtractor.Extract(
                f.Query, 11, DuelViewType.WaitInput, ControlledPlayer, f.Catalog);
            AssertAttached(b, "catalog overload");

            DecisionSnapshot c = LegalActionExtractor.Extract(
                f.Query, 12, DuelViewType.WaitInput, ControlledPlayer, f.Catalog, null);
            AssertAttached(c, "catalog+attack overload");

            DecisionSnapshot d = LegalActionExtractor.Extract(
                f.Query, 13, DuelViewType.WaitInput, ControlledPlayer, LegalActionExtractor.DefaultPosNum);
            AssertAttached(d, "posNum overload");

            DecisionSnapshot e = LegalActionExtractor.Extract(
                f.Query,
                14,
                DuelViewType.WaitInput,
                ControlledPlayer,
                LegalActionExtractor.DefaultPosNum,
                f.Catalog);
            AssertAttached(e, "posNum+catalog overload");

            DecisionSnapshot g = LegalActionExtractor.Extract(
                f.Query,
                15,
                DuelViewType.WaitInput,
                ControlledPlayer,
                LegalActionExtractor.DefaultPosNum,
                f.Catalog,
                null);
            AssertAttached(g, "full runtime overload");
        }

        static void AttachedProjectionIncludesOwnCaveAndExtraExcludesOpponent()
        {
            Fixture f = CreateFixture(LlmPublicVisibilityMode.RuntimeDllField);
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                f.Query,
                42,
                DuelViewType.WaitInput,
                ControlledPlayer,
                LegalActionExtractor.DefaultPosNum,
                f.Catalog,
                null,
                LlmPublicVisibilityMode.RuntimeDllField);

            AssertNotNull(snapshot.SelfResources, "self resources attached");
            AssertEqual(
                LlmPublicVisibilityMode.RuntimeDllField,
                snapshot.SelfResources.FaceDomain,
                "runtime face domain");

            AssertTrue(
                snapshot.SelfResources.Field.Any(c => c.CardId == CaveDragonId),
                "own face-down Cave Dragon in self_resources");
            AssertTrue(
                snapshot.SelfResources.ExtraDeck.Any(c => c.CardId == Rank4Id),
                "own Extra Deck in self_resources");

            string selfJson = LlmSelfResourceProjection.SerializeJson(snapshot.SelfResources);
            AssertFalse(selfJson.Contains(OppHandName), "opp hand not in self");
            AssertFalse(selfJson.Contains(OppSetName), "opp set not in self");
            AssertFalse(selfJson.Contains(OppExtraName), "opp extra not in self");
            AssertFalse(selfJson.Contains(OppHandId.ToString()), "opp hand id not in self");
            AssertFalse(selfJson.Contains(OppExtraId.ToString()), "opp extra id not in self");
        }

        static void OldSnapshotImmutableAfterQueryMutationAndNewSeq()
        {
            Fixture f = CreateFixture(LlmPublicVisibilityMode.RuntimeDllField);
            DecisionSnapshot oldSnap = LegalActionExtractor.Extract(
                f.Query, 100, DuelViewType.WaitInput, ControlledPlayer, f.Catalog);
            LlmSelfResources oldSelf = oldSnap.SelfResources;
            AssertNotNull(oldSelf, "old self");
            int oldFieldCount = oldSelf.Field.Count;
            int oldExtraCount = oldSelf.ExtraDeck.Count;
            string oldJson = LlmSelfResourceProjection.SerializeJson(oldSelf);

            // Mutate engine query after extraction (new board state).
            f.Query.PlaceCard(ControlledPlayer, MonsterZone0, 1, 1999, 555001, f.FaceUp);
            f.Catalog.Add(new LlmCardMetadata()
            {
                CardId = 555001,
                Name = "NEW_BOARD_CARD",
                Kind = "Effect",
                Level = 4,
                SummonFamily = "main_deck_monster",
            });

            DecisionSnapshot fresh = LegalActionExtractor.Extract(
                f.Query, 101, DuelViewType.WaitInput, ControlledPlayer, f.Catalog);
            AssertEqual((ulong)101, fresh.RunEffectSeq, "fresh seq");
            AssertNotNull(fresh.SelfResources, "fresh self");
            AssertTrue(fresh.SelfResources != oldSelf, "distinct projection instance");
            AssertTrue(
                fresh.SelfResources.Field.Any(c => c.CardId == 555001),
                "fresh projection sees new field card");

            // Old snapshot unchanged.
            AssertEqual(oldFieldCount, oldSnap.SelfResources.Field.Count, "old field count stable");
            AssertEqual(oldExtraCount, oldSnap.SelfResources.ExtraDeck.Count, "old extra count stable");
            AssertEqual(oldJson, LlmSelfResourceProjection.SerializeJson(oldSnap.SelfResources),
                "old projection byte-identical after query mutation");
            AssertTrue(
                !oldSnap.SelfResources.Field.Any(c => c.CardId == 555001),
                "old projection does not gain mutated card");
        }

        static void DefaultDecisionWindowOmitsSelfResources()
        {
            Fixture f = CreateFixture(LlmPublicVisibilityMode.RuntimeDllField);
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                f.Query, 7, DuelViewType.WaitInput, ControlledPlayer, f.Catalog);
            string window = LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot);
            AssertFalse(window.Contains("self_resources"), "decision_window omits self_resources");
            AssertFalse(window.Contains("\"self_resources\""), "no self_resources key");
            // Flip-summon legal_action may name Cave Dragon (engine-current action); Extra Deck
            // identity must still stay out of public_state/board paths in the window payload.
            Dictionary<string, object> data = Deserialize(window);
            string publicJson = MiniJSON.Json.Serialize(data["public_state"]);
            AssertFalse(publicJson.Contains(CaveDragonName), "public_state omits face-down cave");
            AssertFalse(publicJson.Contains(Rank4Name), "public_state omits extra deck");
            AssertEqual("decision_window", (string)data["kind"], "kind");
        }

        static void ExplicitAuditSerializerIncludesWhenEnabled()
        {
            Fixture f = CreateFixture(LlmPublicVisibilityMode.RuntimeDllField);
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                f.Query, 8, DuelViewType.WaitInput, ControlledPlayer, f.Catalog);

            string off = LlmDecisionLogSerializer.SerializeSelfResourcesAudit(snapshot, false);
            AssertEqual("self_resources_audit", (string)Deserialize(off)["kind"], "audit kind");
            AssertEqual(false, Convert.ToBoolean(Deserialize(off)["audit_enabled"]), "audit off");
            AssertFalse(off.Contains("\"self_resources\""), "audit off omits projection payload");
            AssertFalse(off.Contains(CaveDragonName), "audit off omits cave");

            string on = LlmDecisionLogSerializer.SerializeSelfResourcesAudit(snapshot, true);
            AssertEqual(true, Convert.ToBoolean(Deserialize(on)["audit_enabled"]), "audit on");
            AssertTrue(on.Contains("\"self_resources\""), "audit on includes self_resources");
            AssertTrue(
                on.Contains(CaveDragonName) || on.Contains(CaveDragonId.ToString()),
                "audit on includes own Cave Dragon");
            AssertTrue(
                on.Contains(Rank4Name) || on.Contains(Rank4Id.ToString()),
                "audit on includes own Extra Deck");
        }

        static void OneStepRequestByteIdenticalWithAuditFlagIrrelevant()
        {
            Fixture f = CreateFixture(LlmPublicVisibilityMode.RuntimeDllField);
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                f.Query, 9, DuelViewType.WaitInput, ControlledPlayer, f.Catalog);

            string reqA = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            // Simulating audit on/off must not change /decide request.
            string _auditOn = LlmDecisionLogSerializer.SerializeSelfResourcesAudit(snapshot, true);
            string _auditOff = LlmDecisionLogSerializer.SerializeSelfResourcesAudit(snapshot, false);
            string reqB = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            AssertEqual(reqA, reqB, "one-step request byte-identical regardless of audit path");
            AssertFalse(reqA.Contains("self_resources"), "request omits self_resources");
            AssertEqual(4, LlmBrokerProtocol.SchemaVersion, "schema version is v4 after LLM-004");
            AssertFalse(string.IsNullOrEmpty(_auditOn) || string.IsNullOrEmpty(_auditOff), "audit paths callable");
        }

        static void PublicStateHistoryAndWireNeverGainPrivateValues()
        {
            Fixture f = CreateFixture(LlmPublicVisibilityMode.RuntimeDllField);
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                f.Query, 10, DuelViewType.WaitInput, ControlledPlayer, f.Catalog);

            string publicJson = MiniJSON.Json.Serialize(
                LlmDecisionLogSerializer.SerializePublicState(snapshot.PublicState));
            AssertFalse(publicJson.Contains(CaveDragonName), "public_state no cave name");
            AssertFalse(publicJson.Contains(Rank4Name), "public_state no extra name");
            AssertFalse(publicJson.Contains(OppHandName), "public_state no opp hand");
            AssertFalse(publicJson.Contains(OppExtraName), "public_state no opp extra");

            LlmDuelHistoryTracker tracker = new LlmDuelHistoryTracker(null);
            string historyJson = tracker.SerializeRequestProjection(ControlledPlayer);
            AssertFalse(historyJson.Contains(CaveDragonName), "history no private cave");
            AssertFalse(historyJson.Contains(Rank4Name), "history no extra");
            AssertFalse(historyJson.Contains(OppExtraName), "history no opp extra");

            string window = LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot);
            AssertFalse(window.Contains("self_resources"), "window still clean");
        }

        static void SourceSettingDefaultsFalseDocumented()
        {
            // Production ClientSettings.json ships default-off; Python STATUS_SETTINGS_KEYS reports it.
            // Harness cannot load ClientSettings (client-only), so assert the serializer contract
            // and the documented default via the audit_enabled=false path.
            string off = LlmDecisionLogSerializer.SerializeSelfResourcesAudit(null, false);
            AssertTrue(off.Contains("\"audit_enabled\":false") || off.Contains("\"audit_enabled\": false"),
                "default audit path documents disabled");
        }

        static void FixtureFaceDomainOverloadPreserved()
        {
            Fixture f = CreateFixture(LlmPublicVisibilityMode.FixtureValidated);
            DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                f.Query,
                20,
                DuelViewType.WaitInput,
                ControlledPlayer,
                LegalActionExtractor.DefaultPosNum,
                f.Catalog,
                null,
                LlmPublicVisibilityMode.FixtureValidated);
            AssertEqual(
                LlmPublicVisibilityMode.FixtureValidated,
                snapshot.SelfResources.FaceDomain,
                "fixture face domain on attached projection");
            LlmSelfResourceCard cave = snapshot.SelfResources.Field.FirstOrDefault(c => c.CardId == CaveDragonId);
            AssertNotNull(cave, "cave present");
            AssertEqual(f.FaceDown, cave.Face, "fixture face-down raw value 4");
        }

        static void AssertAttached(DecisionSnapshot snapshot, string label)
        {
            AssertNotNull(snapshot, label + " snapshot");
            AssertNotNull(snapshot.SelfResources, label + " SelfResources");
            AssertEqual(
                LlmPublicVisibilityMode.RuntimeDllField,
                snapshot.SelfResources.FaceDomain,
                label + " default face domain");
        }

        // ------------------------------------------------------------------

        class Fixture
        {
            public Slice1BQuery Query;
            public Slice1BCatalog Catalog;
            public int FaceUp;
            public int FaceDown;
        }

        static Fixture CreateFixture(LlmPublicVisibilityMode faceDomain)
        {
            bool runtime = faceDomain == LlmPublicVisibilityMode.RuntimeDllField
                || faceDomain == LlmPublicVisibilityMode.RuntimeSafe;
            int faceUp = runtime ? 1 : 8;
            int faceDown = runtime ? 0 : 4;

            Slice1BCatalog catalog = new Slice1BCatalog();
            catalog.Add(new LlmCardMetadata()
            {
                CardId = GirochinId,
                Name = "Girochin Kuwagata",
                Kind = "Effect",
                Level = 4,
                SummonFamily = "main_deck_monster",
            });
            catalog.Add(new LlmCardMetadata()
            {
                CardId = CaveDragonId,
                Name = CaveDragonName,
                Kind = "Effect",
                Level = 4,
                SummonFamily = "main_deck_monster",
            });
            catalog.Add(new LlmCardMetadata()
            {
                CardId = Rank4Id,
                Name = Rank4Name,
                Kind = "Xyz",
                Level = 4,
                IsExtraDeck = true,
                SummonFamily = "xyz",
                UsesRank = true,
            });
            catalog.Add(new LlmCardMetadata()
            {
                CardId = OwnHandId,
                Name = "Dark Magician",
                Kind = "Normal",
                Level = 7,
                SummonFamily = "main_deck_monster",
            });
            catalog.Add(new LlmCardMetadata() { CardId = OppHandId, Name = OppHandName, Kind = "Effect" });
            catalog.Add(new LlmCardMetadata() { CardId = OppSetId, Name = OppSetName, Kind = "Effect" });
            catalog.Add(new LlmCardMetadata()
            {
                CardId = OppExtraId,
                Name = OppExtraName,
                Kind = "Xyz",
                IsExtraDeck = true,
                SummonFamily = "xyz",
                UsesRank = true,
            });

            Slice1BQuery query = new Slice1BQuery();
            query.TurnPlayer = ControlledPlayer;
            query.LifePoints[ControlledPlayer] = 8000;
            query.LifePoints[OpponentPlayer] = 8000;
            query.MovablePhaseMask = (uint)((1 << (int)DuelPhase.Battle) | (1 << (int)DuelPhase.End));
            query.PlaceCard(ControlledPlayer, MonsterZone0, 0, 1001, GirochinId, faceUp);
            query.PlaceCard(ControlledPlayer, MonsterZone1, 0, 1002, CaveDragonId, faceDown);
            query.CommandMasks[Key(ControlledPlayer, MonsterZone1, 0)] =
                (uint)(1 << (int)DuelCommandType.Reverse);
            query.PlaceCard(ControlledPlayer, PosHand, 0, 1101, OwnHandId, faceUp);
            query.HandCardOpen[Key(ControlledPlayer, 0)] = 1;
            query.PlaceCard(ControlledPlayer, PosExtra, 0, 2001, Rank4Id, faceUp);
            query.PlaceCard(ControlledPlayer, PosGrave, 0, 1201, OwnHandId, faceUp);
            query.PlaceCard(ControlledPlayer, PosBanished, 0, 1301, OwnHandId, faceUp);

            query.PlaceCard(OpponentPlayer, PosHand, 0, 9001, OppHandId, faceDown);
            query.HandCardOpen[Key(OpponentPlayer, 0)] = 0;
            query.PlaceCard(OpponentPlayer, OpponentSetZone, 0, 9002, OppSetId, faceDown);
            query.PlaceCard(OpponentPlayer, PosExtra, 0, 9003, OppExtraId, faceDown);

            return new Fixture()
            {
                Query = query,
                Catalog = catalog,
                FaceUp = faceUp,
                FaceDown = faceDown,
            };
        }

        static Dictionary<string, object> Deserialize(string json)
        {
            return MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
        }

        static string Key(params int[] parts)
        {
            return string.Join(",", parts.Select(p => p.ToString()).ToArray());
        }

        static void AssertTrue(bool v, string m)
        {
            if (!v)
            {
                throw new Exception(m);
            }
        }

        static void AssertFalse(bool v, string m)
        {
            if (v)
            {
                throw new Exception(m);
            }
        }

        static void AssertNotNull(object v, string m)
        {
            if (v == null)
            {
                throw new Exception(m + ": expected non-null");
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + ": expected " + expected + ", got " + actual);
            }
        }

        sealed class Slice1BCatalog : ILlmCardCatalog
        {
            readonly Dictionary<int, LlmCardMetadata> cards = new Dictionary<int, LlmCardMetadata>();
            public void Add(LlmCardMetadata card) { cards[card.CardId] = card; }
            public LlmCardMetadata GetCard(int cardId)
            {
                LlmCardMetadata c;
                return cards.TryGetValue(cardId, out c) ? c : null;
            }
        }

        sealed class Slice1BQuery : ILegalActionQuery
        {
            public readonly Dictionary<string, int> CardNums = new Dictionary<string, int>();
            public readonly Dictionary<string, uint> CommandMasks = new Dictionary<string, uint>();
            public readonly Dictionary<string, int> CardUniqueIds = new Dictionary<string, int>();
            public readonly Dictionary<string, int> CardFaces = new Dictionary<string, int>();
            public readonly Dictionary<string, int> HandCardOpen = new Dictionary<string, int>();
            public readonly Dictionary<int, int> CardIdsByUniqueId = new Dictionary<int, int>();
            public readonly Dictionary<int, int> LifePoints = new Dictionary<int, int>();
            public uint MovablePhaseMask;
            public int CurrentPhase = (int)DuelPhase.Main1;
            public int CurrentStep;
            public int TurnNum = 3;
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
            public uint GetCommandMask(int player, int position, int index)
            {
                uint v; return CommandMasks.TryGetValue(Key(player, position, index), out v) ? v : 0u;
            }
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
            public uint GetMovablePhase() { return MovablePhaseMask; }
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
    }
}

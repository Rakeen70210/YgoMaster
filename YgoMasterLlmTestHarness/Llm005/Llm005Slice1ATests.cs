using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-005 Slice 1A: controlled self-resource projection (hidden-information-safe).
    /// Runs before Slice 0 so the default harness proves 1A green, then advances RED to Slice 2A.
    /// </summary>
    static class Llm005Slice1ATests
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
        const int Rank4AId = 84013237;
        const int Rank4BId = 84013237; // duplicate Extra Deck copy
        const int Rank4OtherId = 23995346;
        const int OwnHandId = 46986414;
        const int OwnGyId = 83764718;
        const int OwnBanishedId = 81439173;
        const int OwnDeckTopId = 11111111;
        const int TunerId = 63977008;
        const int OppHandId = 99105001;
        const int OppSetId = 99105002;
        const int OppExtraId = 99105003;
        const int OppBanishedFacedownId = 99105004;

        const string GirochinName = "Girochin Kuwagata";
        const string CaveDragonName = "The Dragon Dwelling in the Cave";
        const string Rank4Name = "Number 39: Utopia";
        const string Rank4OtherName = "Number 32: Shark Drake";
        const string OwnHandName = "Dark Magician";
        const string OwnGyName = "Monster Reborn";
        const string OwnBanishedName = "Foolish Burial";
        const string TunerName = "Junk Synchron";
        const string OppHandName = "SENTINEL_OPP_HAND_1A";
        const string OppSetName = "SENTINEL_OPP_SET_1A";
        const string OppExtraName = "SENTINEL_OPP_EXTRA_1A";

        public static void RunAll()
        {
            ProjectsOwnFaceDownCaveDragon();
            ProjectsOwnHandFieldGyAndFaceUpBanished();
            ProjectsOwnExtraDeckWithDuplicateAggregation();
            RedactsOpponentHandSetAndExtraDeck();
            OmitsFaceDownBanishedAndOwnDeckOrder();
            StructuredXyzAndTunerMetadataFromCatalog();
            MissingLinkRatingIsNullNotInferred();
            StableOrderingAcrossRepeatedProjection();
            ProjectionIsImmutable();
            SerializerHonorsByteBudgetAndExtraDeckMetadataBias();
            SerializerByteBudgetThresholdSweepNeverExceedsMax();
            SerializerRejectsImpossibleBudgetBelowMinimum();
            ExtraDeckOptInTextSerializesRetainedSourceText();
            YdkMappingResolveSummonFamilyAndTunerKinds();
            LlmCardCatalogNormalizationPreservesStructuredFacts();
            FaceDomainDeclaredOnProjection();
            DoesNotWidenPublicStateOrDecisionRequest();
        }

        static void ProjectsOwnFaceDownCaveDragon()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            LlmSelfResources self = Project(f);
            LlmSelfResourceCard cave = FindCard(self.Field, CaveDragonId);
            AssertNotNull(cave, "own face-down Cave Dragon on field");
            AssertEqual(CaveDragonName, cave.Name, "cave name");
            AssertEqual(MonsterZone1, cave.Zone, "cave zone");
            AssertEqual(false, cave.IsFaceUp, "cave face-down");
            AssertEqual(f.FaceDown, cave.Face, "cave face raw");
        }

        static void ProjectsOwnHandFieldGyAndFaceUpBanished()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            LlmSelfResources self = Project(f);

            AssertNotNull(FindCard(self.Field, GirochinId), "own face-up Girochin");
            AssertNotNull(FindCard(self.Hand, OwnHandId), "own hand");
            AssertEqual(OwnHandName, FindCard(self.Hand, OwnHandId).Name, "hand name");
            AssertNotNull(FindCard(self.Graveyard, OwnGyId), "own GY");
            AssertEqual(OwnGyName, FindCard(self.Graveyard, OwnGyId).Name, "gy name");
            AssertNotNull(FindCard(self.Banished, OwnBanishedId), "own face-up banished");
            AssertEqual(true, FindCard(self.Banished, OwnBanishedId).IsFaceUp, "banished face-up");
        }

        static void ProjectsOwnExtraDeckWithDuplicateAggregation()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            // Two copies of Utopia + one Shark Drake
            f.Query.PlaceCard(ControlledPlayer, PosExtra, 0, 2001, Rank4AId, f.FaceUp);
            f.Query.PlaceCard(ControlledPlayer, PosExtra, 1, 2002, Rank4BId, f.FaceUp);
            f.Query.PlaceCard(ControlledPlayer, PosExtra, 2, 2003, Rank4OtherId, f.FaceUp);
            f.Catalog.Add(Meta(Rank4AId, Rank4Name, "Xyz", level: 4, isExtra: true, family: "xyz", usesRank: true));
            f.Catalog.Add(Meta(Rank4OtherId, Rank4OtherName, "Xyz", level: 4, isExtra: true, family: "xyz", usesRank: true));

            LlmSelfResources self = Project(f);
            AssertEqual(2, self.ExtraDeck.Count, "extra deck aggregated entries");
            LlmSelfResourceExtraDeckEntry utopia = self.ExtraDeck.FirstOrDefault(e => e.CardId == Rank4AId);
            AssertNotNull(utopia, "utopia entry");
            AssertEqual(2, utopia.DuplicateCount, "utopia duplicate count");
            AssertEqual(Rank4Name, utopia.Name, "utopia name");
            AssertEqual("xyz", utopia.SummonFamily, "utopia family");
            AssertEqual(4, utopia.Rank.GetValueOrDefault(), "utopia rank");

            LlmSelfResourceExtraDeckEntry shark = self.ExtraDeck.FirstOrDefault(e => e.CardId == Rank4OtherId);
            AssertNotNull(shark, "shark entry");
            AssertEqual(1, shark.DuplicateCount, "shark count");

            // Stable order by card_id ascending
            AssertTrue(self.ExtraDeck[0].CardId < self.ExtraDeck[1].CardId, "extra deck sorted by card_id");
        }

        static void RedactsOpponentHandSetAndExtraDeck()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            LlmSelfResources self = Project(f);
            string json = LlmSelfResourceProjection.SerializeJson(self);
            AssertFalse(json.Contains(OppHandName), "opp hand name leak");
            AssertFalse(json.Contains(OppHandId.ToString()), "opp hand id leak");
            AssertFalse(json.Contains(OppSetName), "opp set name leak");
            AssertFalse(json.Contains(OppSetId.ToString()), "opp set id leak");
            AssertFalse(json.Contains(OppExtraName), "opp extra name leak");
            AssertFalse(json.Contains(OppExtraId.ToString()), "opp extra id leak");
            AssertTrue(FindCard(self.Hand, OppHandId) == null, "opp hand not in self hand");
            AssertTrue(FindCard(self.Field, OppSetId) == null, "opp set not in self field");
            AssertTrue(self.ExtraDeck.All(e => e.CardId != OppExtraId), "opp extra not in self ED");
        }

        static void OmitsFaceDownBanishedAndOwnDeckOrder()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            // Own face-down banished must be omitted.
            f.Query.PlaceCard(ControlledPlayer, PosBanished, 1, 1399, 55555001, f.FaceDown);
            f.Catalog.Add(Meta(55555001, "OWN_FACEDOWN_BANISHED", "Effect", level: 4));
            // Own deck order must never appear.
            f.Query.PlaceCard(ControlledPlayer, PosDeck, 0, 1401, OwnDeckTopId, f.FaceDown);
            f.Catalog.Add(Meta(OwnDeckTopId, "OWN_DECK_TOP_SECRET", "Effect", level: 4));

            LlmSelfResources self = Project(f);
            AssertTrue(FindCard(self.Banished, 55555001) == null, "own face-down banished omitted");
            string json = LlmSelfResourceProjection.SerializeJson(self);
            AssertFalse(json.Contains("OWN_DECK_TOP_SECRET"), "own deck order leak");
            AssertFalse(json.Contains(OwnDeckTopId.ToString()), "own deck id leak");
            AssertFalse(json.Contains("OWN_FACEDOWN_BANISHED"), "own face-down banished leak");
        }

        static void StructuredXyzAndTunerMetadataFromCatalog()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            f.Query.PlaceCard(ControlledPlayer, MonsterZone0, 1, 1011, TunerId, f.FaceUp);
            f.Catalog.Add(Meta(TunerId, TunerName, "Tuner", level: 3, isTuner: true, family: "main_deck_monster"));
            f.Catalog.Add(Meta(Rank4AId, Rank4Name, "Xyz", level: 4, isExtra: true, family: "xyz", usesRank: true));
            f.Query.PlaceCard(ControlledPlayer, PosExtra, 0, 2001, Rank4AId, f.FaceUp);

            LlmSelfResources self = Project(f);
            LlmSelfResourceCard tuner = FindCard(self.Field, TunerId);
            AssertNotNull(tuner, "tuner on field");
            AssertEqual(true, tuner.IsTuner.GetValueOrDefault(), "is_tuner");
            AssertEqual(3, tuner.Level.GetValueOrDefault(), "tuner level");
            AssertEqual("main_deck_monster", tuner.SummonFamily, "tuner family");

            LlmSelfResourceExtraDeckEntry xyz = self.ExtraDeck.FirstOrDefault(e => e.CardId == Rank4AId);
            AssertNotNull(xyz, "xyz extra");
            AssertEqual(true, xyz.IsExtraDeck, "xyz is_extra_deck");
            AssertEqual("xyz", xyz.SummonFamily, "xyz summon family");
            AssertEqual(4, xyz.Rank.GetValueOrDefault(), "xyz rank from level field");
            AssertEqual(true, xyz.UsesRank, "xyz uses rank semantics");
        }

        static void MissingLinkRatingIsNullNotInferred()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            // Catalog has no grounded link rating (null) even if DEF looks numeric.
            f.Catalog.Add(Meta(111222, "Link Cookie", "Link", level: 2, atk: 1000, def: 2,
                isExtra: true, family: "link", linkRating: null));
            f.Query.PlaceCard(ControlledPlayer, PosExtra, 0, 2100, 111222, f.FaceUp);

            LlmSelfResources self = Project(f);
            LlmSelfResourceExtraDeckEntry link = self.ExtraDeck.FirstOrDefault(e => e.CardId == 111222);
            AssertNotNull(link, "link entry");
            AssertEqual(true, !link.LinkRating.HasValue, "link rating must be null when ungrounded");
            string json = LlmSelfResourceProjection.SerializeJson(self);
            // Must not invent a link_rating field with DEF-derived value.
            AssertFalse(json.Contains("\"link_rating\":2"), "must not infer link_rating from DEF");
        }

        static void StableOrderingAcrossRepeatedProjection()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            f.Query.PlaceCard(ControlledPlayer, PosExtra, 0, 2002, Rank4OtherId, f.FaceUp);
            f.Query.PlaceCard(ControlledPlayer, PosExtra, 1, 2001, Rank4AId, f.FaceUp);
            f.Catalog.Add(Meta(Rank4AId, Rank4Name, "Xyz", level: 4, isExtra: true, family: "xyz", usesRank: true));
            f.Catalog.Add(Meta(Rank4OtherId, Rank4OtherName, "Xyz", level: 4, isExtra: true, family: "xyz", usesRank: true));

            string a = LlmSelfResourceProjection.SerializeJson(Project(f));
            string b = LlmSelfResourceProjection.SerializeJson(Project(f));
            AssertEqual(a, b, "projection serialize must be deterministic");
        }

        static void ProjectionIsImmutable()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            LlmSelfResources self = Project(f);
            try
            {
                self.Field.Add(null);
                throw new Exception("Field list must be immutable");
            }
            catch (NotSupportedException)
            {
            }
            try
            {
                self.Hand.Add(null);
                throw new Exception("Hand list must be immutable");
            }
            catch (NotSupportedException)
            {
            }
            try
            {
                self.ExtraDeck.Add(null);
                throw new Exception("ExtraDeck list must be immutable");
            }
            catch (NotSupportedException)
            {
            }
        }

        static void SerializerHonorsByteBudgetAndExtraDeckMetadataBias()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            f.Catalog.Add(Meta(Rank4AId, Rank4Name, "Xyz", level: 4, isExtra: true, family: "xyz", usesRank: true,
                text: new string('X', 2000)));
            f.Query.PlaceCard(ControlledPlayer, PosExtra, 0, 2001, Rank4AId, f.FaceUp);
            // Long field text should be bounded under tight budget.
            f.Catalog.Add(Meta(GirochinId, GirochinName, "Effect", level: 4,
                text: new string('G', 2000)));

            LlmSelfResources self = Project(f);
            LlmSelfResourceSerializeOptions tight = new LlmSelfResourceSerializeOptions()
            {
                MaxSerializedBytes = 900,
                MaxTextLength = 80,
                ExtraDeckMetadataOnly = true,
            };
            string json = LlmSelfResourceProjection.SerializeJson(self, tight);
            AssertTrue(Encoding.UTF8.GetByteCount(json) <= tight.MaxSerializedBytes,
                "serialized self_resources must respect MaxSerializedBytes");
            AssertTrue(json.Contains("extra_deck") || json.Contains("ExtraDeck") || json.Contains(Rank4Name)
                || json.Contains(Rank4AId.ToString()),
                "extra deck metadata still present under budget");
            // Default Extra Deck projection should not dump full multi-KB text.
            AssertFalse(json.Contains(new string('X', 500)), "extra deck full text must be bounded/omitted");
        }

        static void SerializerByteBudgetThresholdSweepNeverExceedsMax()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            f.Catalog.Add(Meta(Rank4AId, Rank4Name, "Xyz", level: 4, isExtra: true, family: "xyz", usesRank: true,
                text: new string('X', 4000)));
            f.Query.PlaceCard(ControlledPlayer, PosExtra, 0, 2001, Rank4AId, f.FaceUp);
            f.Catalog.Add(Meta(GirochinId, GirochinName, "Effect", level: 4, text: new string('G', 4000)));
            f.Catalog.Add(Meta(CaveDragonId, CaveDragonName, "Effect", level: 4, text: new string('C', 4000)));
            LlmSelfResources self = Project(f);

            int min = LlmSelfResourceSerializeOptions.MinimumSupportedSerializedBytes;
            // Sweep supported tight budgets, including ones that force budget_trimmed markers.
            int[] budgets = new int[]
            {
                min, min + 1, min + 8, 220, 256, 300, 400, 512, 640, 768, 900, 1024, 1500, 2048, 4096
            };
            foreach (int maxBytes in budgets)
            {
                LlmSelfResourceSerializeOptions options = new LlmSelfResourceSerializeOptions()
                {
                    MaxSerializedBytes = maxBytes,
                    MaxTextLength = 200,
                    ExtraDeckMetadataOnly = true,
                };
                string json = LlmSelfResourceProjection.SerializeJson(self, options);
                int utf8 = Encoding.UTF8.GetByteCount(json);
                AssertTrue(utf8 <= maxBytes,
                    "budget sweep MaxSerializedBytes=" + maxBytes + " got utf8=" + utf8
                    + " (marker must be measured in final object)");
            }
        }

        static void SerializerRejectsImpossibleBudgetBelowMinimum()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            LlmSelfResources self = Project(f);
            int illegal = LlmSelfResourceSerializeOptions.MinimumSupportedSerializedBytes - 1;
            LlmSelfResourceSerializeOptions options = new LlmSelfResourceSerializeOptions()
            {
                MaxSerializedBytes = illegal,
            };
            bool threw = false;
            try
            {
                LlmSelfResourceProjection.SerializeJson(self, options);
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }
            AssertTrue(threw,
                "MaxSerializedBytes below MinimumSupportedSerializedBytes must fail closed");
        }

        static void ExtraDeckOptInTextSerializesRetainedSourceText()
        {
            const string edText = "2 Level 4 monsters. Once per turn: detach to protect.";
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            f.Catalog.Add(Meta(Rank4AId, Rank4Name, "Xyz", level: 4, isExtra: true, family: "xyz",
                usesRank: true, text: edText));
            f.Query.PlaceCard(ControlledPlayer, PosExtra, 0, 2001, Rank4AId, f.FaceUp);

            LlmSelfResources self = Project(f);
            LlmSelfResourceExtraDeckEntry entry = self.ExtraDeck.FirstOrDefault(e => e.CardId == Rank4AId);
            AssertNotNull(entry, "extra deck entry");
            AssertEqual(edText, entry.Text, "Extra Deck retains immutable source text");

            // Default metadata-only omits text.
            string metaOnly = LlmSelfResourceProjection.SerializeJson(self, new LlmSelfResourceSerializeOptions()
            {
                MaxSerializedBytes = 8192,
                ExtraDeckMetadataOnly = true,
                MaxTextLength = 200,
            });
            AssertFalse(metaOnly.Contains(edText), "metadata-only Extra Deck must omit text");

            // Opt-in includes capped text.
            string withText = LlmSelfResourceProjection.SerializeJson(self, new LlmSelfResourceSerializeOptions()
            {
                MaxSerializedBytes = 8192,
                ExtraDeckMetadataOnly = false,
                MaxTextLength = 24,
            });
            AssertTrue(withText.Contains("\"text\""), "opt-in Extra Deck must include text key");
            AssertTrue(withText.Contains("2 Level 4 monsters"), "opt-in text prefix present");
            AssertFalse(withText.Contains(edText), "opt-in text must respect MaxTextLength cap");
        }

        static void YdkMappingResolveSummonFamilyAndTunerKinds()
        {
            AssertEqual("xyz", YdkLlmCardCatalog.ResolveSummonFamily(CardFrame.Xyz), "Xyz family");
            AssertEqual("xyz", YdkLlmCardCatalog.ResolveSummonFamily(CardFrame.XyzPend), "XyzPend family");
            AssertEqual("synchro", YdkLlmCardCatalog.ResolveSummonFamily(CardFrame.Sync), "Sync family");
            AssertEqual("link", YdkLlmCardCatalog.ResolveSummonFamily(CardFrame.Link), "Link family");
            AssertEqual("fusion", YdkLlmCardCatalog.ResolveSummonFamily(CardFrame.Fusion), "Fusion family");
            AssertEqual("ritual", YdkLlmCardCatalog.ResolveSummonFamily(CardFrame.Ritual), "Ritual family");
            AssertEqual("spell", YdkLlmCardCatalog.ResolveSummonFamily(CardFrame.Magic), "Magic->spell");
            AssertEqual("trap", YdkLlmCardCatalog.ResolveSummonFamily(CardFrame.Trap), "Trap family");
            AssertEqual("main_deck_monster", YdkLlmCardCatalog.ResolveSummonFamily(CardFrame.Effect),
                "Effect main deck");
            AssertEqual("main_deck_monster", YdkLlmCardCatalog.ResolveSummonFamily(CardFrame.Normal),
                "Normal main deck");

            AssertEqual(true, YdkLlmCardCatalog.IsTunerKind(CardKind.Tuner), "Tuner");
            AssertEqual(true, YdkLlmCardCatalog.IsTunerKind(CardKind.TunerFx), "TunerFx");
            AssertEqual(true, YdkLlmCardCatalog.IsTunerKind(CardKind.SyncTuner), "SyncTuner");
            AssertEqual(true, YdkLlmCardCatalog.IsTunerKind(CardKind.FlipTuner), "FlipTuner");
            AssertEqual(false, YdkLlmCardCatalog.IsTunerKind(CardKind.Effect), "Effect not tuner");
            AssertEqual(false, YdkLlmCardCatalog.IsTunerKind(CardKind.Xyz), "Xyz not tuner");
            AssertEqual(false, YdkLlmCardCatalog.IsTunerKind(CardKind.Link), "Link not tuner");
            AssertEqual(false, YdkLlmCardCatalog.IsTunerKind(CardKind.Magic), "Magic not tuner");
        }

        static void LlmCardCatalogNormalizationPreservesStructuredFacts()
        {
            Dictionary<int, LlmCardMetadata> raw = new Dictionary<int, LlmCardMetadata>();
            raw[1] = new LlmCardMetadata()
            {
                CardId = 1,
                Name = "  Utopia  ",
                Text = "  2 Level 4  ",
                Kind = "Xyz",
                Attribute = "LIGHT",
                Level = 4,
                Atk = 2500,
                Def = 2000,
                Frame = "Xyz",
                SummonFamily = "xyz",
                IsExtraDeck = true,
                IsTuner = false,
                UsesRank = true,
                LinkRating = null,
            };
            raw[2] = new LlmCardMetadata()
            {
                CardId = 2,
                Name = "Junk Synchron",
                Text = "tuner text",
                Kind = "Tuner",
                Level = 3,
                Frame = "Normal",
                SummonFamily = "main_deck_monster",
                IsExtraDeck = false,
                IsTuner = true,
                UsesRank = false,
                LinkRating = null,
            };
            raw[3] = new LlmCardMetadata()
            {
                CardId = 3,
                Name = "Decode Talker",
                Text = "link text",
                Kind = "Link",
                Level = 3,
                Atk = 2300,
                Def = 3, // DEF must not become link_rating
                Frame = "Link",
                SummonFamily = "link",
                IsExtraDeck = true,
                IsTuner = false,
                UsesRank = false,
                LinkRating = null,
            };
            raw[4] = new LlmCardMetadata()
            {
                CardId = 4,
                Name = "Monster Reborn",
                Kind = "Magic",
                Frame = "Magic",
                SummonFamily = "spell",
                IsExtraDeck = false,
                LinkRating = null,
            };
            raw[5] = new LlmCardMetadata()
            {
                CardId = 5,
                Name = "Mirror Force",
                Kind = "Trap",
                Frame = "Trap",
                SummonFamily = "trap",
                IsExtraDeck = false,
                LinkRating = null,
            };

            LlmCardCatalog catalog = new LlmCardCatalog(() => raw, maxTextLength: 1000);
            LlmCardMetadata xyz = catalog.GetCard(1);
            AssertEqual("Utopia", xyz.Name, "normalized name");
            AssertEqual("Xyz", xyz.Frame, "xyz frame preserved");
            AssertEqual("xyz", xyz.SummonFamily, "xyz family preserved");
            AssertEqual(true, xyz.IsExtraDeck, "xyz is_extra_deck");
            AssertEqual(false, xyz.IsTuner, "xyz is_tuner");
            AssertEqual(true, xyz.UsesRank, "xyz uses_rank");
            AssertEqual(true, !xyz.LinkRating.HasValue, "xyz null link rating");
            AssertEqual(4, xyz.Level, "xyz level field (rank semantics via UsesRank)");

            LlmCardMetadata tuner = catalog.GetCard(2);
            AssertEqual(true, tuner.IsTuner, "tuner IsTuner preserved");
            AssertEqual("main_deck_monster", tuner.SummonFamily, "tuner family");
            AssertEqual(false, tuner.IsExtraDeck, "tuner not extra");
            AssertEqual(true, !tuner.LinkRating.HasValue, "tuner null link rating");

            LlmCardMetadata link = catalog.GetCard(3);
            AssertEqual("link", link.SummonFamily, "link family");
            AssertEqual(true, link.IsExtraDeck, "link is extra");
            AssertEqual(true, !link.LinkRating.HasValue, "link rating stays null (not DEF)");
            AssertEqual(3, link.Def, "def preserved separately from link rating");

            LlmCardMetadata spell = catalog.GetCard(4);
            AssertEqual("spell", spell.SummonFamily, "spell family");
            AssertEqual("Magic", spell.Frame, "spell frame");

            LlmCardMetadata trap = catalog.GetCard(5);
            AssertEqual("trap", trap.SummonFamily, "trap family");
            AssertEqual("Trap", trap.Frame, "trap frame");
        }

        static void FaceDomainDeclaredOnProjection()
        {
            Fixture runtime = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            LlmSelfResources selfRuntime = Project(runtime);
            AssertEqual(LlmPublicVisibilityMode.RuntimeDllField, selfRuntime.FaceDomain, "runtime face domain");

            Fixture fixture = CreateGirochinCaveFixture(LlmPublicVisibilityMode.FixtureValidated);
            LlmSelfResources selfFixture = Project(fixture);
            AssertEqual(LlmPublicVisibilityMode.FixtureValidated, selfFixture.FaceDomain, "fixture face domain");
            AssertEqual(fixture.FaceDown, FindCard(selfFixture.Field, CaveDragonId).Face, "fixture face raw 4");
        }

        static void DoesNotWidenPublicStateOrDecisionRequest()
        {
            Fixture f = CreateGirochinCaveFixture(LlmPublicVisibilityMode.RuntimeDllField);
            LlmSelfResources self = Project(f);
            AssertTrue(FindCard(self.Field, CaveDragonId) != null, "self has cave");

            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 1,
                ControlledPlayer = ControlledPlayer,
                ActingPlayer = ControlledPlayer,
            };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
            });
            string request = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            AssertFalse(request.Contains("self_resources"), "decision request must not include self_resources yet");
            AssertFalse(request.Contains(CaveDragonName), "request must not leak private face-down");
            AssertEqual(4, LlmBrokerProtocol.SchemaVersion, "SchemaVersion is v4 after LLM-004");
        }

        // ------------------------------------------------------------------
        // Fixture helpers
        // ------------------------------------------------------------------

        class Fixture
        {
            public Slice1AQuery Query;
            public Slice1ACatalog Catalog;
            public LlmPublicVisibilityMode FaceDomain;
            public int FaceUp;
            public int FaceDown;
        }

        static LlmSelfResources Project(Fixture f)
        {
            return LlmSelfResourceProjection.Project(
                f.Query,
                ControlledPlayer,
                f.Catalog,
                f.FaceDomain);
        }

        static Fixture CreateGirochinCaveFixture(LlmPublicVisibilityMode faceDomain)
        {
            bool runtime = faceDomain == LlmPublicVisibilityMode.RuntimeDllField
                || faceDomain == LlmPublicVisibilityMode.RuntimeSafe;
            int faceUp = runtime ? 1 : 8;
            int faceDown = runtime ? 0 : 4;

            Slice1ACatalog catalog = new Slice1ACatalog();
            catalog.Add(Meta(GirochinId, GirochinName, "Effect", level: 4, atk: 1700, def: 1000,
                family: "main_deck_monster"));
            catalog.Add(Meta(CaveDragonId, CaveDragonName, "Effect", level: 4, atk: 1300, def: 2000,
                family: "main_deck_monster"));
            catalog.Add(Meta(OwnHandId, OwnHandName, "Normal", level: 7, atk: 2500, def: 2100,
                family: "main_deck_monster"));
            catalog.Add(Meta(OwnGyId, OwnGyName, "Magic", family: "spell"));
            catalog.Add(Meta(OwnBanishedId, OwnBanishedName, "Magic", family: "spell"));
            catalog.Add(Meta(OppHandId, OppHandName, "Effect", level: 4));
            catalog.Add(Meta(OppSetId, OppSetName, "Effect", level: 4));
            catalog.Add(Meta(OppExtraId, OppExtraName, "Xyz", level: 4, isExtra: true, family: "xyz", usesRank: true));

            Slice1AQuery query = new Slice1AQuery();
            query.LifePoints[ControlledPlayer] = 8000;
            query.LifePoints[OpponentPlayer] = 8000;
            query.TurnPlayer = ControlledPlayer;
            query.CurrentPhase = (int)DuelPhase.Main1;
            query.TurnNum = 3;

            query.PlaceCard(ControlledPlayer, MonsterZone0, 0, 1001, GirochinId, faceUp);
            query.PlaceCard(ControlledPlayer, MonsterZone1, 0, 1002, CaveDragonId, faceDown);
            query.PlaceCard(ControlledPlayer, PosHand, 0, 1101, OwnHandId, faceUp);
            query.HandCardOpen[Key(ControlledPlayer, 0)] = 1;
            query.PlaceCard(ControlledPlayer, PosGrave, 0, 1201, OwnGyId, faceUp);
            query.PlaceCard(ControlledPlayer, PosBanished, 0, 1301, OwnBanishedId, faceUp);

            // Opponent-hidden omniscient noise
            query.PlaceCard(OpponentPlayer, PosHand, 0, 9001, OppHandId, faceDown);
            query.HandCardOpen[Key(OpponentPlayer, 0)] = 0;
            query.PlaceCard(OpponentPlayer, OpponentSetZone, 0, 9002, OppSetId, faceDown);
            query.PlaceCard(OpponentPlayer, PosExtra, 0, 9003, OppExtraId, faceDown);
            query.PlaceCard(OpponentPlayer, PosBanished, 0, 9004, OppBanishedFacedownId, faceDown);

            return new Fixture()
            {
                Query = query,
                Catalog = catalog,
                FaceDomain = faceDomain,
                FaceUp = faceUp,
                FaceDown = faceDown,
            };
        }

        static LlmCardMetadata Meta(
            int id,
            string name,
            string kind,
            int level = 0,
            int atk = 0,
            int def = 0,
            bool isExtra = false,
            bool isTuner = false,
            string family = null,
            bool usesRank = false,
            int? linkRating = null,
            string text = null)
        {
            return new LlmCardMetadata()
            {
                CardId = id,
                Name = name,
                Kind = kind,
                Level = level,
                Atk = atk,
                Def = def,
                Text = text ?? (name + " text"),
                IsExtraDeck = isExtra,
                IsTuner = isTuner,
                SummonFamily = family,
                Frame = kind,
                UsesRank = usesRank,
                LinkRating = linkRating,
            };
        }

        static LlmSelfResourceCard FindCard(IList<LlmSelfResourceCard> cards, int cardId)
        {
            if (cards == null)
            {
                return null;
            }
            foreach (LlmSelfResourceCard card in cards)
            {
                if (card != null && card.CardId == cardId)
                {
                    return card;
                }
            }
            return null;
        }

        static string Key(params int[] parts)
        {
            return string.Join(",", parts.Select(p => p.ToString()).ToArray());
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

        static void AssertNotNull(object value, string message)
        {
            if (value == null)
            {
                throw new Exception(message + ": expected non-null");
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + ": expected " + expected + ", got " + actual);
            }
        }

        sealed class Slice1ACatalog : ILlmCardCatalog
        {
            readonly Dictionary<int, LlmCardMetadata> cards = new Dictionary<int, LlmCardMetadata>();

            public void Add(LlmCardMetadata card)
            {
                cards[card.CardId] = card;
            }

            public LlmCardMetadata GetCard(int cardId)
            {
                LlmCardMetadata card;
                return cards.TryGetValue(cardId, out card) ? card : null;
            }
        }

        sealed class Slice1AQuery : ILegalActionQuery
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
                int v;
                return CardNums.TryGetValue(Key(player, position), out v) ? v : 0;
            }

            public uint GetCommandMask(int player, int position, int index)
            {
                uint v;
                return CommandMasks.TryGetValue(Key(player, position, index), out v) ? v : 0u;
            }

            public int GetCardFace(int player, int position, int index)
            {
                int v;
                return CardFaces.TryGetValue(Key(player, position, index), out v) ? v : 0;
            }

            public int GetCardIdByUniqueId(int uniqueId)
            {
                int v;
                return CardIdsByUniqueId.TryGetValue(uniqueId, out v) ? v : 0;
            }

            public int GetCardUniqueId(int player, int position, int index)
            {
                int v;
                return CardUniqueIds.TryGetValue(Key(player, position, index), out v) ? v : 0;
            }

            public int GetHandCardOpen(int player, int index)
            {
                int v;
                return HandCardOpen.TryGetValue(Key(player, index), out v) ? v : 0;
            }

            public int GetLifePoints(int player)
            {
                int v;
                return LifePoints.TryGetValue(player, out v) ? v : 0;
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

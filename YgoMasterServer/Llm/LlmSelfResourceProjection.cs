using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Options for provider/audit serialization of controlled self resources.
    /// Full graph audit may retain richer data; this path is byte-budgeted.
    /// </summary>
    class LlmSelfResourceSerializeOptions
    {
        /// <summary>
        /// Smallest MaxSerializedBytes the serializer accepts. Below this the floor
        /// stub cannot be guaranteed, so Serialize fails closed instead of violating the limit.
        /// </summary>
        public const int MinimumSupportedSerializedBytes = 192;

        public int MaxSerializedBytes = 16 * 1024;
        public int MaxTextLength = 200;
        /// <summary>
        /// When true (default), Extra Deck entries omit full card text and favor
        /// id/name/family/rank-or-level/count metadata. When false, retained Extra Deck
        /// source text is serialized (still subject to MaxTextLength).
        /// </summary>
        public bool ExtraDeckMetadataOnly = true;
    }

    /// <summary>
    /// Immutable private self-resource projection: facts known to the controlled player only.
    /// Distinct from public_state and duel_history.
    /// </summary>
    class LlmSelfResources
    {
        public int ControlledPlayer { get; private set; }
        public LlmPublicVisibilityMode FaceDomain { get; private set; }
        public int Turn { get; private set; }
        public int CurrentPhase { get; private set; }
        public int CurrentStep { get; private set; }
        public IList<LlmSelfResourceCard> Hand { get; private set; }
        public IList<LlmSelfResourceCard> Field { get; private set; }
        public IList<LlmSelfResourceCard> Graveyard { get; private set; }
        public IList<LlmSelfResourceCard> Banished { get; private set; }
        public IList<LlmSelfResourceExtraDeckEntry> ExtraDeck { get; private set; }

        internal static LlmSelfResources Create(
            int controlledPlayer,
            LlmPublicVisibilityMode faceDomain,
            int turn,
            int currentPhase,
            int currentStep,
            List<LlmSelfResourceCard> hand,
            List<LlmSelfResourceCard> field,
            List<LlmSelfResourceCard> graveyard,
            List<LlmSelfResourceCard> banished,
            List<LlmSelfResourceExtraDeckEntry> extraDeck)
        {
            return new LlmSelfResources()
            {
                ControlledPlayer = controlledPlayer,
                FaceDomain = faceDomain,
                Turn = turn,
                CurrentPhase = currentPhase,
                CurrentStep = currentStep,
                Hand = new ReadOnlyCollection<LlmSelfResourceCard>(hand ?? new List<LlmSelfResourceCard>()),
                Field = new ReadOnlyCollection<LlmSelfResourceCard>(field ?? new List<LlmSelfResourceCard>()),
                Graveyard = new ReadOnlyCollection<LlmSelfResourceCard>(graveyard ?? new List<LlmSelfResourceCard>()),
                Banished = new ReadOnlyCollection<LlmSelfResourceCard>(banished ?? new List<LlmSelfResourceCard>()),
                ExtraDeck = new ReadOnlyCollection<LlmSelfResourceExtraDeckEntry>(
                    extraDeck ?? new List<LlmSelfResourceExtraDeckEntry>()),
            };
        }
    }

    class LlmSelfResourceCard
    {
        public int CardId { get; private set; }
        public string Name { get; private set; }
        public int Zone { get; private set; }
        public int Index { get; private set; }
        public int Face { get; private set; }
        public bool IsFaceUp { get; private set; }
        public int? Level { get; private set; }
        public int? Rank { get; private set; }
        public bool UsesRank { get; private set; }
        public bool? IsTuner { get; private set; }
        public int? LinkRating { get; private set; }
        public string Frame { get; private set; }
        public string SummonFamily { get; private set; }
        public bool IsExtraDeck { get; private set; }
        public string Kind { get; private set; }
        public int Atk { get; private set; }
        public int Def { get; private set; }
        public string Text { get; private set; }
        public uint CommandMask { get; private set; }

        internal static LlmSelfResourceCard Create(
            int cardId,
            string name,
            int zone,
            int index,
            int face,
            bool isFaceUp,
            int? level,
            int? rank,
            bool usesRank,
            bool? isTuner,
            int? linkRating,
            string frame,
            string summonFamily,
            bool isExtraDeck,
            string kind,
            int atk,
            int def,
            string text,
            uint commandMask)
        {
            return new LlmSelfResourceCard()
            {
                CardId = cardId,
                Name = name,
                Zone = zone,
                Index = index,
                Face = face,
                IsFaceUp = isFaceUp,
                Level = level,
                Rank = rank,
                UsesRank = usesRank,
                IsTuner = isTuner,
                LinkRating = linkRating,
                Frame = frame,
                SummonFamily = summonFamily,
                IsExtraDeck = isExtraDeck,
                Kind = kind,
                Atk = atk,
                Def = def,
                Text = text,
                CommandMask = commandMask,
            };
        }
    }

    class LlmSelfResourceExtraDeckEntry
    {
        public int CardId { get; private set; }
        public string Name { get; private set; }
        public string Frame { get; private set; }
        public string SummonFamily { get; private set; }
        public int? Level { get; private set; }
        public int? Rank { get; private set; }
        public bool UsesRank { get; private set; }
        public int? LinkRating { get; private set; }
        public bool IsExtraDeck { get { return true; } }
        public bool? IsTuner { get; private set; }
        public string Kind { get; private set; }
        public int Atk { get; private set; }
        public int Def { get; private set; }
        /// <summary>
        /// Immutable source text retained for candidate-driven opt-in serialization
        /// (ExtraDeckMetadataOnly=false). Default provider projection omits it.
        /// </summary>
        public string Text { get; private set; }
        public int DuplicateCount { get; private set; }

        internal static LlmSelfResourceExtraDeckEntry Create(
            int cardId,
            string name,
            string frame,
            string summonFamily,
            int? level,
            int? rank,
            bool usesRank,
            int? linkRating,
            bool? isTuner,
            string kind,
            int atk,
            int def,
            string text,
            int duplicateCount)
        {
            return new LlmSelfResourceExtraDeckEntry()
            {
                CardId = cardId,
                Name = name,
                Frame = frame,
                SummonFamily = summonFamily,
                Level = level,
                Rank = rank,
                UsesRank = usesRank,
                LinkRating = linkRating,
                IsTuner = isTuner,
                Kind = kind,
                Atk = atk,
                Def = def,
                Text = text,
                DuplicateCount = duplicateCount,
            };
        }
    }

    /// <summary>
    /// Projects controlled-player private resources from an engine query without
    /// widening public_state / duel_history. Opponent-hidden identities are ignored.
    /// </summary>
    static class LlmSelfResourceProjection
    {
        const int PosFieldMax = 12;
        const int PosHand = 13;
        // Live-validated: Extra Deck = 14, Main Deck = 15 (see LlmPublicHistoryRedactionPolicy).
        const int PosExtra = 14;
        const int PosDeck = 15;
        const int PosGrave = 16;
        const int PosBanished = 17;

        public static LlmSelfResources Project(
            ILegalActionQuery query,
            int controlledPlayer,
            ILlmCardCatalog catalog,
            LlmPublicVisibilityMode faceDomain)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            List<LlmSelfResourceCard> hand = new List<LlmSelfResourceCard>();
            List<LlmSelfResourceCard> field = new List<LlmSelfResourceCard>();
            List<LlmSelfResourceCard> graveyard = new List<LlmSelfResourceCard>();
            List<LlmSelfResourceCard> banished = new List<LlmSelfResourceCard>();
            List<LlmSelfResourceCard> extraRaw = new List<LlmSelfResourceCard>();

            // Field zones 0..12 (own face-down identities included).
            for (int position = 0; position <= PosFieldMax; position++)
            {
                CollectZoneCards(
                    query,
                    catalog,
                    controlledPlayer,
                    position,
                    faceDomain,
                    requireFaceUp: false,
                    field);
            }

            CollectZoneCards(
                query,
                catalog,
                controlledPlayer,
                PosHand,
                faceDomain,
                requireFaceUp: false,
                hand);

            // Own deck order is never projected.
            // PosDeck intentionally skipped.

            CollectZoneCards(
                query,
                catalog,
                controlledPlayer,
                PosGrave,
                faceDomain,
                requireFaceUp: false,
                graveyard);

            // Banished: only face-up identities (face-down banished omitted).
            CollectZoneCards(
                query,
                catalog,
                controlledPlayer,
                PosBanished,
                faceDomain,
                requireFaceUp: true,
                banished);

            CollectZoneCards(
                query,
                catalog,
                controlledPlayer,
                PosExtra,
                faceDomain,
                requireFaceUp: false,
                extraRaw);

            SortCards(field);
            SortCards(hand);
            SortCards(graveyard);
            SortCards(banished);
            List<LlmSelfResourceExtraDeckEntry> extraDeck = AggregateExtraDeck(extraRaw);

            return LlmSelfResources.Create(
                controlledPlayer,
                faceDomain,
                query.GetTurnNum(),
                query.GetCurrentPhase(),
                query.GetCurrentStep(),
                hand,
                field,
                graveyard,
                banished,
                extraDeck);
        }

        public static string SerializeJson(LlmSelfResources resources)
        {
            return SerializeJson(resources, null);
        }

        public static string SerializeJson(
            LlmSelfResources resources,
            LlmSelfResourceSerializeOptions options)
        {
            Dictionary<string, object> data = Serialize(resources, options);
            return MiniJSON.Json.Serialize(data);
        }

        public static Dictionary<string, object> Serialize(
            LlmSelfResources resources,
            LlmSelfResourceSerializeOptions options)
        {
            if (resources == null)
            {
                return new Dictionary<string, object>();
            }
            if (options == null)
            {
                options = new LlmSelfResourceSerializeOptions();
            }

            int maxBytes = options.MaxSerializedBytes > 0 ? options.MaxSerializedBytes : 16 * 1024;
            if (maxBytes < LlmSelfResourceSerializeOptions.MinimumSupportedSerializedBytes)
            {
                throw new ArgumentOutOfRangeException(
                    "MaxSerializedBytes",
                    maxBytes,
                    "MaxSerializedBytes must be >= LlmSelfResourceSerializeOptions.MinimumSupportedSerializedBytes ("
                    + LlmSelfResourceSerializeOptions.MinimumSupportedSerializedBytes
                    + "); impossible budgets fail closed rather than silently exceeding the limit.");
            }

            int maxText = options.MaxTextLength > 0 ? options.MaxTextLength : 200;
            bool extraMetadataOnly = options.ExtraDeckMetadataOnly;

            // Stage 0: full request (no trim marker).
            Dictionary<string, object> root = BuildSerializeRoot(
                resources, maxText, true, !extraMetadataOnly, extraMetadataOnly);
            if (FitsBudget(root, maxBytes))
            {
                return root;
            }

            // Stage 1: drop field/hand/GY/banished text. Measure AFTER budget_trimmed marker.
            root = WithTrimMarker(
                BuildSerializeRoot(resources, maxText, false, !extraMetadataOnly, extraMetadataOnly),
                "text");
            if (FitsBudget(root, maxBytes))
            {
                return root;
            }

            // Stage 2: force Extra Deck metadata-only + shorter text cap.
            int shortText = Math.Min(maxText, 40);
            root = WithTrimMarker(
                BuildSerializeRoot(resources, shortText, false, false, true),
                "text_and_extra");
            if (FitsBudget(root, maxBytes))
            {
                return root;
            }

            // Stage 3: no text anywhere; drop list tails while re-measuring with markers present.
            root = WithTrimMarker(
                BuildSerializeRoot(resources, 0, false, false, true),
                "aggressive");
            string[] listKeys = new[] { "field", "hand", "graveyard", "banished", "extra_deck" };
            int guard = 0;
            while (!FitsBudget(root, maxBytes) && guard < 256)
            {
                guard++;
                bool dropped = false;
                foreach (string key in listKeys)
                {
                    List<object> list = root.ContainsKey(key) ? root[key] as List<object> : null;
                    if (list == null || list.Count == 0)
                    {
                        continue;
                    }
                    list.RemoveAt(list.Count - 1);
                    root["budget_dropped_" + key + "_tail"] = true;
                    dropped = true;
                    break;
                }
                if (!dropped)
                {
                    break;
                }
            }
            if (FitsBudget(root, maxBytes))
            {
                return root;
            }

            // Absolute floor stub — size is bounded by MinimumSupportedSerializedBytes.
            root = new Dictionary<string, object>()
            {
                { "kind", "self_resources" },
                { "controlled_player", resources.ControlledPlayer },
                { "face_domain", resources.FaceDomain.ToString() },
                { "budget_exhausted", true },
            };
            if (!FitsBudget(root, maxBytes))
            {
                throw new InvalidOperationException(
                    "self_resources floor stub exceeds MaxSerializedBytes=" + maxBytes
                    + "; raise MaxSerializedBytes to at least MinimumSupportedSerializedBytes.");
            }
            return root;
        }

        static Dictionary<string, object> WithTrimMarker(Dictionary<string, object> root, string marker)
        {
            if (root == null)
            {
                root = new Dictionary<string, object>();
            }
            root["budget_trimmed"] = marker;
            return root;
        }

        static bool FitsBudget(Dictionary<string, object> root, int maxBytes)
        {
            string json = MiniJSON.Json.Serialize(root);
            return Encoding.UTF8.GetByteCount(json) <= maxBytes;
        }

        static Dictionary<string, object> BuildSerializeRoot(
            LlmSelfResources resources,
            int maxText,
            bool includeFieldText,
            bool includeExtraText,
            bool extraMetadataOnly)
        {
            Dictionary<string, object> root = new Dictionary<string, object>()
            {
                { "kind", "self_resources" },
                { "controlled_player", resources.ControlledPlayer },
                { "face_domain", resources.FaceDomain.ToString() },
                { "turn", resources.Turn },
                { "current_phase", resources.CurrentPhase },
                { "current_step", resources.CurrentStep },
                { "hand", SerializeCards(resources.Hand, maxText, includeFieldText) },
                { "field", SerializeCards(resources.Field, maxText, includeFieldText) },
                { "graveyard", SerializeCards(resources.Graveyard, maxText, includeFieldText) },
                { "banished", SerializeCards(resources.Banished, maxText, includeFieldText) },
                { "extra_deck", SerializeExtraDeck(resources.ExtraDeck, maxText, includeExtraText, extraMetadataOnly) },
            };
            return root;
        }

        static List<object> SerializeCards(
            IList<LlmSelfResourceCard> cards,
            int maxText,
            bool includeText)
        {
            List<object> list = new List<object>();
            if (cards == null)
            {
                return list;
            }
            foreach (LlmSelfResourceCard card in cards)
            {
                if (card == null)
                {
                    continue;
                }
                Dictionary<string, object> data = new Dictionary<string, object>()
                {
                    { "card_id", card.CardId },
                    { "name", card.Name },
                    { "zone", card.Zone },
                    { "index", card.Index },
                    { "face", card.Face },
                    { "is_face_up", card.IsFaceUp },
                    { "kind", card.Kind },
                    { "frame", card.Frame },
                    { "summon_family", card.SummonFamily },
                    { "is_extra_deck", card.IsExtraDeck },
                    { "atk", card.Atk },
                    { "def", card.Def },
                };
                if (card.Level.HasValue)
                {
                    data["level"] = card.Level.Value;
                }
                if (card.Rank.HasValue)
                {
                    data["rank"] = card.Rank.Value;
                }
                if (card.UsesRank)
                {
                    data["uses_rank"] = true;
                }
                if (card.IsTuner.HasValue)
                {
                    data["is_tuner"] = card.IsTuner.Value;
                }
                if (card.LinkRating.HasValue)
                {
                    data["link_rating"] = card.LinkRating.Value;
                }
                if (includeText && maxText > 0 && !string.IsNullOrEmpty(card.Text))
                {
                    data["text"] = CapText(card.Text, maxText);
                }
                // Native unique ids intentionally omitted from provider-visible JSON.
                list.Add(data);
            }
            return list;
        }

        static List<object> SerializeExtraDeck(
            IList<LlmSelfResourceExtraDeckEntry> entries,
            int maxText,
            bool includeText,
            bool metadataOnly)
        {
            List<object> list = new List<object>();
            if (entries == null)
            {
                return list;
            }
            foreach (LlmSelfResourceExtraDeckEntry entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }
                Dictionary<string, object> data = new Dictionary<string, object>()
                {
                    { "card_id", entry.CardId },
                    { "name", entry.Name },
                    { "summon_family", entry.SummonFamily },
                    { "frame", entry.Frame },
                    { "is_extra_deck", true },
                    { "duplicate_count", entry.DuplicateCount },
                };
                if (entry.Rank.HasValue)
                {
                    data["rank"] = entry.Rank.Value;
                }
                if (entry.Level.HasValue && !entry.UsesRank)
                {
                    data["level"] = entry.Level.Value;
                }
                if (entry.UsesRank)
                {
                    data["uses_rank"] = true;
                }
                if (entry.LinkRating.HasValue)
                {
                    data["link_rating"] = entry.LinkRating.Value;
                }
                if (entry.IsTuner.HasValue)
                {
                    data["is_tuner"] = entry.IsTuner.Value;
                }
                if (!metadataOnly)
                {
                    data["kind"] = entry.Kind;
                    data["atk"] = entry.Atk;
                    data["def"] = entry.Def;
                    if (includeText && maxText > 0 && !string.IsNullOrEmpty(entry.Text))
                    {
                        // Opt-in Extra Deck text from retained immutable source text.
                        data["text"] = CapText(entry.Text, maxText);
                    }
                }
                list.Add(data);
            }
            return list;
        }

        static void CollectZoneCards(
            ILegalActionQuery query,
            ILlmCardCatalog catalog,
            int controlledPlayer,
            int position,
            LlmPublicVisibilityMode faceDomain,
            bool requireFaceUp,
            List<LlmSelfResourceCard> sink)
        {
            int cardNum = query.GetCardNum(controlledPlayer, position);
            // Match LegalActionExtractor index domain: 0..cardNum inclusive.
            for (int index = 0; index <= cardNum; index++)
            {
                int uniqueId = query.GetCardUniqueId(controlledPlayer, position, index);
                if (uniqueId <= 0)
                {
                    continue;
                }
                int cardId = query.GetCardIdByUniqueId(uniqueId);
                if (cardId <= 0)
                {
                    continue;
                }
                int face = query.GetCardFace(controlledPlayer, position, index);
                bool isFaceUp = IsFaceUp(face, faceDomain);
                if (requireFaceUp && !isFaceUp)
                {
                    continue;
                }

                LlmCardMetadata meta = catalog != null ? catalog.GetCard(cardId) : null;
                int? level;
                int? rank;
                bool usesRank;
                ResolveLevelRank(meta, out level, out rank, out usesRank);

                sink.Add(LlmSelfResourceCard.Create(
                    cardId,
                    meta != null ? meta.Name : null,
                    position,
                    index,
                    face,
                    isFaceUp,
                    level,
                    rank,
                    usesRank,
                    meta != null ? (bool?)meta.IsTuner : null,
                    meta != null ? meta.LinkRating : null,
                    meta != null ? meta.Frame : null,
                    meta != null ? meta.SummonFamily : null,
                    meta != null && meta.IsExtraDeck,
                    meta != null ? meta.Kind : null,
                    meta != null ? meta.Atk : 0,
                    meta != null ? meta.Def : 0,
                    meta != null ? meta.Text : null,
                    query.GetCommandMask(controlledPlayer, position, index)));
            }
        }

        static void ResolveLevelRank(
            LlmCardMetadata meta,
            out int? level,
            out int? rank,
            out bool usesRank)
        {
            level = null;
            rank = null;
            usesRank = false;
            if (meta == null)
            {
                return;
            }
            usesRank = meta.UsesRank;
            if (usesRank)
            {
                if (meta.Level > 0)
                {
                    rank = meta.Level;
                }
            }
            else if (meta.Level > 0)
            {
                level = meta.Level;
            }
        }

        static List<LlmSelfResourceExtraDeckEntry> AggregateExtraDeck(List<LlmSelfResourceCard> raw)
        {
            Dictionary<int, LlmSelfResourceExtraDeckEntry> byId =
                new Dictionary<int, LlmSelfResourceExtraDeckEntry>();
            Dictionary<int, int> counts = new Dictionary<int, int>();
            if (raw != null)
            {
                foreach (LlmSelfResourceCard card in raw)
                {
                    if (card == null || card.CardId <= 0)
                    {
                        continue;
                    }
                    int count;
                    counts.TryGetValue(card.CardId, out count);
                    counts[card.CardId] = count + 1;
                    if (!byId.ContainsKey(card.CardId))
                    {
                        byId[card.CardId] = LlmSelfResourceExtraDeckEntry.Create(
                            card.CardId,
                            card.Name,
                            card.Frame,
                            card.SummonFamily,
                            card.Level,
                            card.Rank,
                            card.UsesRank,
                            card.LinkRating,
                            card.IsTuner,
                            card.Kind,
                            card.Atk,
                            card.Def,
                            card.Text,
                            1);
                    }
                }
            }

            List<int> ids = new List<int>(byId.Keys);
            ids.Sort();
            List<LlmSelfResourceExtraDeckEntry> result = new List<LlmSelfResourceExtraDeckEntry>();
            foreach (int id in ids)
            {
                LlmSelfResourceExtraDeckEntry prototype = byId[id];
                result.Add(LlmSelfResourceExtraDeckEntry.Create(
                    prototype.CardId,
                    prototype.Name,
                    prototype.Frame,
                    prototype.SummonFamily,
                    prototype.Level,
                    prototype.Rank,
                    prototype.UsesRank,
                    prototype.LinkRating,
                    prototype.IsTuner,
                    prototype.Kind,
                    prototype.Atk,
                    prototype.Def,
                    prototype.Text,
                    counts[id]));
            }
            return result;
        }

        static void SortCards(List<LlmSelfResourceCard> cards)
        {
            cards.Sort(delegate(LlmSelfResourceCard a, LlmSelfResourceCard b)
            {
                if (a.Zone != b.Zone)
                {
                    return a.Zone.CompareTo(b.Zone);
                }
                if (a.Index != b.Index)
                {
                    return a.Index.CompareTo(b.Index);
                }
                return a.CardId.CompareTo(b.CardId);
            });
        }

        static bool IsFaceUp(int face, LlmPublicVisibilityMode mode)
        {
            if (mode == LlmPublicVisibilityMode.FixtureValidated)
            {
                return face == LlmPublicHistoryRedactionPolicy.PublicFaceUpValue;
            }
            // RuntimeDllField / RuntimeSafe: live-validated 0/1 domain.
            return LlmDllRuntimeFace.IsPublicFaceUp(face);
        }

        static string CapText(string text, int maxText)
        {
            if (string.IsNullOrEmpty(text) || maxText <= 0)
            {
                return text;
            }
            if (text.Length <= maxText)
            {
                return text;
            }
            return text.Substring(0, maxText).TrimEnd() + "...";
        }
    }
}

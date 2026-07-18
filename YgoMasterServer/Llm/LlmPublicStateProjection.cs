using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Absolute-public state snapshot for delta extraction (Slice 3).
    /// Only independently safe facts: turn, phase, LP, and fail-closed known cards.
    /// CardUniqueId is internal for stable diff only and must never be serialized to brokers.
    /// </summary>
    class LlmPublicKnownCardProjection
    {
        public int Player;
        public int Position;
        public int Index;
        public int CardId;
        public string CardName;
        public int Face;
        public string Zone;
        /// <summary>Engine unique id for stable tracking; never serialize to history JSON.</summary>
        public int CardUniqueId;
    }

    class LlmPublicProjectionQueryBudget
    {
        public int CardNumQueries;
        public int FaceQueries;
        public int UniqueIdQueries;
        public int CardIdQueries;
        public int PositionsScanned;
        public int MaxIndexExclusive;

        public void Reset()
        {
            CardNumQueries = 0;
            FaceQueries = 0;
            UniqueIdQueries = 0;
            CardIdQueries = 0;
            PositionsScanned = 0;
            MaxIndexExclusive = 0;
        }
    }

    class LlmPublicStateProjection
    {
        public int Turn;
        public int Phase;
        public int LifePoints0;
        public int LifePoints1;
        public readonly List<LlmPublicKnownCardProjection> KnownCards = new List<LlmPublicKnownCardProjection>();

        public static LlmPublicStateProjection Capture(
            int turn,
            int phase,
            int lp0,
            int lp1,
            IEnumerable<LlmPublicKnownCardProjection> knownCards)
        {
            return Capture(turn, phase, lp0, lp1, knownCards, LlmAbsolutePublicVisibility.RuntimeMode);
        }

        public static LlmPublicStateProjection Capture(
            int turn,
            int phase,
            int lp0,
            int lp1,
            IEnumerable<LlmPublicKnownCardProjection> knownCards,
            LlmPublicVisibilityMode mode)
        {
            LlmPublicStateProjection projection = new LlmPublicStateProjection()
            {
                Turn = turn,
                Phase = phase,
                LifePoints0 = lp0,
                LifePoints1 = lp1,
            };
            if (knownCards != null)
            {
                foreach (LlmPublicKnownCardProjection card in knownCards)
                {
                    if (card == null || card.CardId <= 0)
                    {
                        continue;
                    }
                    bool handOpen = card.Position == LlmPublicHistoryRedactionPolicy.PosHand;
                    if (!LlmAbsolutePublicVisibility.CanExposeCardIdentity(
                        card.Position, card.Face, handOpen, mode))
                    {
                        continue;
                    }
                    projection.KnownCards.Add(CloneCard(card));
                }
            }
            return projection;
        }

        public static LlmPublicKnownCardProjection TryCreateKnownCard(
            int player,
            int position,
            int index,
            int cardId,
            string cardName,
            int face,
            bool handOpen = false)
        {
            return TryCreateKnownCard(
                player, position, index, cardId, cardName, face, handOpen,
                LlmAbsolutePublicVisibility.RuntimeMode, 0);
        }

        public static LlmPublicKnownCardProjection TryCreateKnownCard(
            int player,
            int position,
            int index,
            int cardId,
            string cardName,
            int face,
            bool handOpen,
            LlmPublicVisibilityMode mode,
            int cardUniqueId)
        {
            if (!LlmAbsolutePublicVisibility.CanExposeCardIdentity(position, face, handOpen, mode))
            {
                return null;
            }
            if (cardId <= 0)
            {
                return null;
            }
            return new LlmPublicKnownCardProjection()
            {
                Player = player,
                Position = position,
                Index = index,
                CardId = cardId,
                CardName = cardName,
                Face = face,
                Zone = LlmPublicHistoryRedactionPolicy.MapPublicZone(position),
                CardUniqueId = cardUniqueId,
            };
        }

        static LlmPublicKnownCardProjection CloneCard(LlmPublicKnownCardProjection card)
        {
            return new LlmPublicKnownCardProjection()
            {
                Player = card.Player,
                Position = card.Position,
                Index = card.Index,
                CardId = card.CardId,
                CardName = card.CardName,
                Face = card.Face,
                Zone = string.IsNullOrEmpty(card.Zone)
                    ? LlmPublicHistoryRedactionPolicy.MapPublicZone(card.Position)
                    : card.Zone,
                CardUniqueId = card.CardUniqueId,
            };
        }
    }

    class LlmPublicStateDelta
    {
        public bool TurnChanged;
        public int PreviousTurn;
        public int CurrentTurn;
        public bool PhaseChanged;
        public int PreviousPhase;
        public int CurrentPhase;
        public int PreviousLife0;
        public int PreviousLife1;
        public int CurrentLife0;
        public int CurrentLife1;
        public int LifeDelta0;
        public int LifeDelta1;
        public readonly List<LlmPublicKnownCardProjection> AddedKnownCards =
            new List<LlmPublicKnownCardProjection>();
        public readonly List<LlmPublicKnownCardProjection> RemovedKnownCards =
            new List<LlmPublicKnownCardProjection>();

        public static LlmPublicStateDelta Diff(
            LlmPublicStateProjection before,
            LlmPublicStateProjection after)
        {
            LlmPublicStateDelta delta = new LlmPublicStateDelta();
            // Null baseline: no synthetic first-snapshot outcomes (LP +8000, initial turn/phase).
            if (before == null || after == null)
            {
                return delta;
            }

            delta.PreviousTurn = before.Turn;
            delta.CurrentTurn = after.Turn;
            delta.TurnChanged = before.Turn != after.Turn;
            delta.PreviousPhase = before.Phase;
            delta.CurrentPhase = after.Phase;
            delta.PhaseChanged = before.Phase != after.Phase;
            delta.PreviousLife0 = before.LifePoints0;
            delta.PreviousLife1 = before.LifePoints1;
            delta.CurrentLife0 = after.LifePoints0;
            delta.CurrentLife1 = after.LifePoints1;
            delta.LifeDelta0 = after.LifePoints0 - before.LifePoints0;
            delta.LifeDelta1 = after.LifePoints1 - before.LifePoints1;

            // Prefer unique-id identity for stable tracking; fall back to card-id multiset
            // by player+zone (ignore list index shifts in grave/field).
            Dictionary<string, int> beforeCounts = BuildIdentityMultiset(before.KnownCards);
            Dictionary<string, int> afterCounts = BuildIdentityMultiset(after.KnownCards);
            Dictionary<string, LlmPublicKnownCardProjection> afterSamples =
                BuildIdentitySamples(after.KnownCards);
            Dictionary<string, LlmPublicKnownCardProjection> beforeSamples =
                BuildIdentitySamples(before.KnownCards);

            foreach (KeyValuePair<string, int> pair in afterCounts)
            {
                int beforeCount = 0;
                beforeCounts.TryGetValue(pair.Key, out beforeCount);
                int added = pair.Value - beforeCount;
                for (int i = 0; i < added; i++)
                {
                    LlmPublicKnownCardProjection sample;
                    if (afterSamples.TryGetValue(pair.Key, out sample))
                    {
                        delta.AddedKnownCards.Add(sample);
                    }
                }
            }
            foreach (KeyValuePair<string, int> pair in beforeCounts)
            {
                int afterCount = 0;
                afterCounts.TryGetValue(pair.Key, out afterCount);
                int removed = pair.Value - afterCount;
                for (int i = 0; i < removed; i++)
                {
                    LlmPublicKnownCardProjection sample;
                    if (beforeSamples.TryGetValue(pair.Key, out sample))
                    {
                        delta.RemovedKnownCards.Add(sample);
                    }
                }
            }
            return delta;
        }

        static Dictionary<string, int> BuildIdentityMultiset(List<LlmPublicKnownCardProjection> cards)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            if (cards == null)
            {
                return counts;
            }
            foreach (LlmPublicKnownCardProjection card in cards)
            {
                if (card == null || card.CardId <= 0)
                {
                    continue;
                }
                string key = IdentityKey(card);
                int count;
                counts.TryGetValue(key, out count);
                counts[key] = count + 1;
            }
            return counts;
        }

        static Dictionary<string, LlmPublicKnownCardProjection> BuildIdentitySamples(
            List<LlmPublicKnownCardProjection> cards)
        {
            Dictionary<string, LlmPublicKnownCardProjection> samples =
                new Dictionary<string, LlmPublicKnownCardProjection>();
            if (cards == null)
            {
                return samples;
            }
            foreach (LlmPublicKnownCardProjection card in cards)
            {
                if (card == null || card.CardId <= 0)
                {
                    continue;
                }
                string key = IdentityKey(card);
                if (!samples.ContainsKey(key))
                {
                    samples[key] = card;
                }
            }
            return samples;
        }

        /// <summary>
        /// Stable identity ignoring list index. Prefer engine unique id when present.
        /// </summary>
        public static string IdentityKey(LlmPublicKnownCardProjection card)
        {
            if (card == null)
            {
                return string.Empty;
            }
            if (card.CardUniqueId > 0)
            {
                return "uid:" + card.CardUniqueId;
            }
            // Multiset key without index — grave/field list shifts do not churn.
            return "cid:" + card.Player + ":" + (card.Zone ?? "") + ":" + card.CardId;
        }
    }

    /// <summary>
    /// Testable baseline transition helper: first capture only sets baseline, never emits.
    /// </summary>
    static class LlmPublicOutcomePipeline
    {
        public static List<LlmPublicDuelEvent> Transition(
            ref LlmPublicStateProjection baseline,
            LlmPublicStateProjection after,
            DuelViewType viewType,
            ulong runEffectSeq,
            ulong nextEventId)
        {
            return Transition(ref baseline, after, viewType, runEffectSeq, nextEventId, null);
        }

        public static List<LlmPublicDuelEvent> Transition(
            ref LlmPublicStateProjection baseline,
            LlmPublicStateProjection after,
            DuelViewType viewType,
            ulong runEffectSeq,
            ulong nextEventId,
            LlmPendingAcceptedIntentCause pendingCause)
        {
            List<LlmPublicDuelEvent> outcomes = new List<LlmPublicDuelEvent>();
            if (after == null)
            {
                return outcomes;
            }
            if (baseline == null)
            {
                baseline = after;
                return outcomes;
            }
            outcomes = LlmPublicOutcomeEventFactory.CreateFromDelta(
                baseline,
                after,
                viewType,
                runEffectSeq,
                nextEventId,
                pendingCause);
            baseline = after;
            return outcomes;
        }

        /// <summary>
        /// Reset PvP-owned projection/raw id counters for a new duel generation.
        /// </summary>
        public static void ResetDuelGeneration(
            ref LlmPublicStateProjection baseline,
            ref ulong nextRawEvidenceId,
            ref ulong nextFaceProbeId,
            ref ulong nextPublicEventId)
        {
            baseline = null;
            nextRawEvidenceId = 0;
            nextFaceProbeId = 0;
            nextPublicEventId = 0;
        }
    }

    /// <summary>
    /// Runtime public projection capture with a strict call budget.
    /// Scans field 0..12 + grave only. Identity query only after DLL face gate (raw_face==1).
    /// Card index loops always use index &lt; count (never &lt;=). Max 16 cards per position.
    /// </summary>
    static class LlmPublicRuntimeProjectionCapture
    {
        public const int MaxCardsPerPosition = 16;

        public delegate int CardNumQuery(int player, int position);
        public delegate int FaceQuery(int player, int position, int index);
        public delegate int UniqueIdQuery(int player, int position, int index);
        public delegate int CardIdByUniqueIdQuery(int uniqueId);
        public delegate int DirectCardIdQuery(int player, int position, int index);
        public delegate string CardNameQuery(int cardId);

        public static LlmPublicStateProjection Capture(
            int turn,
            int phase,
            int lp0,
            int lp1,
            LlmPublicProjectionQueryBudget budget,
            CardNumQuery cardNum,
            FaceQuery faceQuery,
            UniqueIdQuery uniqueIdQuery,
            CardIdByUniqueIdQuery cardIdByUniqueId,
            DirectCardIdQuery directCardId = null,
            CardNameQuery cardNameQuery = null)
        {
            if (budget == null)
            {
                budget = new LlmPublicProjectionQueryBudget();
            }
            budget.Reset();
            List<LlmPublicKnownCardProjection> known = new List<LlmPublicKnownCardProjection>();
            int[] identityPositions = LlmAbsolutePublicVisibility.RuntimeIdentityPositions;
            if (cardNum == null || faceQuery == null)
            {
                return LlmPublicStateProjection.Capture(
                    turn, phase, lp0, lp1, known, LlmPublicVisibilityMode.RuntimeDllField);
            }

            for (int player = 0; player < 2; player++)
            {
                foreach (int pos in identityPositions)
                {
                    budget.PositionsScanned++;
                    int num;
                    try
                    {
                        budget.CardNumQueries++;
                        num = cardNum(player, pos);
                    }
                    catch
                    {
                        continue;
                    }
                    if (num < 0)
                    {
                        num = 0;
                    }
                    if (num > budget.MaxIndexExclusive)
                    {
                        budget.MaxIndexExclusive = num;
                    }
                    // Critical: index < num, never index <= num.
                    for (int index = 0; index < num && index < MaxCardsPerPosition; index++)
                    {
                        try
                        {
                            budget.FaceQueries++;
                            int dllFace = faceQuery(player, pos, index);
                            // Gate BEFORE any identity query (uid/card id).
                            if (!LlmAbsolutePublicVisibility.CanExposeDllRuntimeCardIdentity(
                                pos, dllFace, false))
                            {
                                continue;
                            }
                            int uid = 0;
                            if (uniqueIdQuery != null)
                            {
                                budget.UniqueIdQueries++;
                                try { uid = uniqueIdQuery(player, pos, index); }
                                catch { uid = 0; }
                            }
                            int cardId = 0;
                            if (uid > 0 && cardIdByUniqueId != null)
                            {
                                budget.CardIdQueries++;
                                try { cardId = cardIdByUniqueId(uid); }
                                catch { cardId = 0; }
                            }
                            else if (directCardId != null)
                            {
                                budget.CardIdQueries++;
                                try { cardId = directCardId(player, pos, index); }
                                catch { cardId = 0; }
                            }
                            string cardName = null;
                            if (cardId > 0 && cardNameQuery != null)
                            {
                                try { cardName = cardNameQuery(cardId); }
                                catch { cardName = null; }
                            }
                            LlmPublicKnownCardProjection card =
                                LlmPublicStateProjection.TryCreateKnownCard(
                                    player, pos, index, cardId, cardName, dllFace, false,
                                    LlmPublicVisibilityMode.RuntimeDllField, uid);
                            if (card != null)
                            {
                                known.Add(card);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }

            return LlmPublicStateProjection.Capture(
                turn, phase, lp0, lp1, known, LlmPublicVisibilityMode.RuntimeDllField);
        }

        public static bool ScansOnlyRuntimeIdentityPositions(LlmPublicProjectionQueryBudget budget)
        {
            if (budget == null)
            {
                return false;
            }
            int expected = 2 * LlmAbsolutePublicVisibility.RuntimeIdentityPositions.Length;
            return budget.PositionsScanned == expected;
        }

        /// <summary>Legacy alias.</summary>
        public static bool ScansOnlyRuntimeSafeIdentityPositions(LlmPublicProjectionQueryBudget budget)
        {
            return ScansOnlyRuntimeIdentityPositions(budget);
        }
    }
}

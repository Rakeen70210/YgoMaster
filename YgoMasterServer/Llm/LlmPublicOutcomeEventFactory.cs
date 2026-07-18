using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Explicit pending accepted-intent cause for deterministic outcome correlation.
    /// Requires positive CardUniqueId equality (never card-id-only fallback).
    /// Bounded by allowed summon appearance views and max seq distance from accept.
    /// Internal UID is never serialized on outcomes.
    /// </summary>
    class LlmPendingAcceptedIntentCause
    {
        /// <summary>
        /// Live evidence: accept seq71 -&gt; CardMove appearance seq75 (distance 4).
        /// Allow a small explicit window; expire outside it.
        /// </summary>
        public const int MaxSummonAppearanceSeqDistance = 8;

        public ulong EventId;
        public ulong AcceptRunEffectSeq;
        public int ActorPlayer;
        public int CardId;
        /// <summary>Internal engine unique id for match only; never serialized.</summary>
        public int CardUniqueId;
        public LlmPublicDuelEventKind Kind;
        public bool Consumed;
        public bool Expired;

        /// <summary>
        /// Returns null when unique id is unavailable — caused_by must stay null.
        /// </summary>
        public static LlmPendingAcceptedIntentCause ForNormalSummon(
            ulong eventId,
            ulong acceptRunEffectSeq,
            int actorPlayer,
            int cardId,
            int cardUniqueId)
        {
            if (cardUniqueId <= 0 || cardId <= 0 || actorPlayer < 0 || eventId == 0)
            {
                return null;
            }
            return new LlmPendingAcceptedIntentCause()
            {
                EventId = eventId,
                AcceptRunEffectSeq = acceptRunEffectSeq,
                ActorPlayer = actorPlayer,
                CardId = cardId,
                CardUniqueId = cardUniqueId,
                Kind = LlmPublicDuelEventKind.NormalSummon,
                Consumed = false,
                Expired = false,
            };
        }

        public static bool IsAllowedSummonAppearanceView(DuelViewType viewType)
        {
            switch (viewType)
            {
                case DuelViewType.CardMove:
                case DuelViewType.CutinSummon:
                case DuelViewType.RunSummon:
                case DuelViewType.RunSpSummon:
                case DuelViewType.CutinReverse:
                    return true;
                default:
                    return false;
            }
        }

        public bool IsOutsideWindow(ulong outcomeRunEffectSeq)
        {
            if (outcomeRunEffectSeq <= AcceptRunEffectSeq)
            {
                return false;
            }
            return outcomeRunEffectSeq > AcceptRunEffectSeq + (ulong)MaxSummonAppearanceSeqDistance;
        }

        /// <summary>
        /// Deterministic match for a face-up field appearance after a matching normal summon.
        /// Exact positive CardUniqueId required; expires when past max seq distance.
        /// </summary>
        public bool TryMatchFaceUpFieldAppearance(
            LlmPublicKnownCardProjection card,
            ulong outcomeRunEffectSeq,
            DuelViewType viewType,
            out ulong causedByEventId)
        {
            causedByEventId = 0;
            if (Consumed || Expired || card == null)
            {
                return false;
            }
            if (Kind != LlmPublicDuelEventKind.NormalSummon)
            {
                return false;
            }
            if (CardUniqueId <= 0)
            {
                Expired = true;
                return false;
            }
            if (outcomeRunEffectSeq <= AcceptRunEffectSeq)
            {
                return false;
            }
            if (IsOutsideWindow(outcomeRunEffectSeq))
            {
                Expired = true;
                return false;
            }
            if (!IsAllowedSummonAppearanceView(viewType))
            {
                return false;
            }
            if (card.CardUniqueId <= 0 || card.CardUniqueId != CardUniqueId)
            {
                return false;
            }
            if (card.Player != ActorPlayer)
            {
                return false;
            }
            if (CardId > 0 && card.CardId > 0 && card.CardId != CardId)
            {
                return false;
            }
            if (card.Position < 0 || card.Position > LlmPublicHistoryRedactionPolicy.PosSpellTrapMax)
            {
                return false;
            }
            causedByEventId = EventId;
            Consumed = true;
            return true;
        }

        public bool IsTerminal
        {
            get { return Consumed || Expired; }
        }
    }

    /// <summary>
    /// Builds outcome history events only from proven absolute-public state deltas.
    /// Caused_by only via explicit LlmPendingAcceptedIntentCause match (not last-event guess).
    /// ActorPlayer is -1 when causative actor is unknown; TargetPlayer holds affected seat.
    /// </summary>
    static class LlmPublicOutcomeEventFactory
    {
        public const int UnknownActorPlayer = -1;

        public static List<LlmPublicDuelEvent> CreateFromDelta(
            LlmPublicStateProjection before,
            LlmPublicStateProjection after,
            DuelViewType viewType,
            ulong runEffectSeq,
            ulong nextEventId)
        {
            return CreateFromDelta(
                before, after, viewType, runEffectSeq, nextEventId,
                (LlmPendingAcceptedIntentCause)null);
        }

        /// <param name="pendingCause">
        /// Explicit pending accepted-intent match key. Never pass "last event id" alone.
        /// </param>
        public static List<LlmPublicDuelEvent> CreateFromDelta(
            LlmPublicStateProjection before,
            LlmPublicStateProjection after,
            DuelViewType viewType,
            ulong runEffectSeq,
            ulong nextEventId,
            LlmPendingAcceptedIntentCause pendingCause)
        {
            List<LlmPublicDuelEvent> outcomes = new List<LlmPublicDuelEvent>();
            if (before == null || after == null)
            {
                return outcomes;
            }

            // Expire stale pending even when there is no card delta.
            if (pendingCause != null &&
                !pendingCause.IsTerminal &&
                pendingCause.IsOutsideWindow(runEffectSeq))
            {
                pendingCause.Expired = true;
            }

            LlmPublicStateDelta delta = LlmPublicStateDelta.Diff(before, after);
            ulong eventId = nextEventId;
            if (delta.TurnChanged)
            {
                outcomes.Add(BaseOutcome(
                    ref eventId,
                    runEffectSeq,
                    after.Turn,
                    after.Phase,
                    LlmPublicDuelEventKind.TurnChanged,
                    "outcome:turn:seq:" + runEffectSeq +
                        ":from:" + delta.PreviousTurn + ":to:" + delta.CurrentTurn,
                    targetPlayer: -1));
            }

            if (delta.PhaseChanged)
            {
                outcomes.Add(BaseOutcome(
                    ref eventId,
                    runEffectSeq,
                    after.Turn,
                    after.Phase,
                    LlmPublicDuelEventKind.PhaseChanged,
                    "outcome:phase:seq:" + runEffectSeq +
                        ":from:" + delta.PreviousPhase + ":to:" + delta.CurrentPhase,
                    targetPlayer: -1));
            }

            if (delta.LifeDelta0 != 0)
            {
                outcomes.Add(BaseOutcome(
                    ref eventId,
                    runEffectSeq,
                    after.Turn,
                    after.Phase,
                    LlmPublicDuelEventKind.LifePointsChanged,
                    "outcome:lp:seq:" + runEffectSeq +
                        ":player:0:from:" + delta.PreviousLife0 + ":to:" + delta.CurrentLife0 +
                        ":delta:" + delta.LifeDelta0,
                    targetPlayer: 0));
            }
            if (delta.LifeDelta1 != 0)
            {
                outcomes.Add(BaseOutcome(
                    ref eventId,
                    runEffectSeq,
                    after.Turn,
                    after.Phase,
                    LlmPublicDuelEventKind.LifePointsChanged,
                    "outcome:lp:seq:" + runEffectSeq +
                        ":player:1:from:" + delta.PreviousLife1 + ":to:" + delta.CurrentLife1 +
                        ":delta:" + delta.LifeDelta1,
                    targetPlayer: 1));
            }

            if (LlmAbsolutePublicVisibility.IsShuffleView(viewType))
            {
                outcomes.Add(BaseOutcome(
                    ref eventId,
                    runEffectSeq,
                    after.Turn,
                    after.Phase,
                    viewType == DuelViewType.DeckShuffle
                        ? LlmPublicDuelEventKind.DeckShuffled
                        : LlmPublicDuelEventKind.HandShuffled,
                    "outcome:shuffle:seq:" + runEffectSeq + ":view:" + (int)viewType,
                    targetPlayer: -1));
            }

            // Public occurrence ordinal per player/zone/card-id in stable added order (no UID).
            Dictionary<string, int> occurrenceByPublicKey = new Dictionary<string, int>();
            foreach (LlmPublicKnownCardProjection card in delta.AddedKnownCards)
            {
                if (card == null || card.CardId <= 0)
                {
                    continue;
                }
                string publicKey = card.Player + ":" + (card.Zone ?? "") + ":" + card.CardId;
                int occ;
                occurrenceByPublicKey.TryGetValue(publicKey, out occ);
                occ++;
                occurrenceByPublicKey[publicKey] = occ;

                LlmPublicDuelEvent moved = BaseOutcome(
                    ref eventId,
                    runEffectSeq,
                    after.Turn,
                    after.Phase,
                    LlmPublicDuelEventKind.CardMovedPublic,
                    "outcome:card_public:seq:" + runEffectSeq +
                        ":cid:" + card.CardId +
                        ":zone:" + (card.Zone ?? "") +
                        ":player:" + card.Player +
                        ":occ:" + occ,
                    targetPlayer: card.Player);
                moved.CardId = card.CardId;
                moved.CardName = card.CardName;
                moved.DestinationZone = card.Zone;
                moved.CardPlayer = card.Player;
                if (pendingCause != null)
                {
                    ulong causedBy;
                    if (pendingCause.TryMatchFaceUpFieldAppearance(
                        card, runEffectSeq, viewType, out causedBy))
                    {
                        moved.CausedByEventId = causedBy;
                    }
                }
                outcomes.Add(moved);
            }

            return outcomes;
        }

        /// <summary>
        /// Fixture helper using FixtureValidated visibility (opt-in offline rules).
        /// </summary>
        public static LlmPublicDuelEvent TryCreatePublicCardAppearance(
            int player,
            int position,
            int face,
            int cardId,
            string cardName,
            ulong runEffectSeq,
            int turn,
            int phase,
            ulong eventId,
            bool handOpen = false)
        {
            return TryCreatePublicCardAppearance(
                player, position, face, cardId, cardName, runEffectSeq, turn, phase, eventId,
                handOpen, LlmPublicVisibilityMode.FixtureValidated);
        }

        public static LlmPublicDuelEvent TryCreatePublicCardAppearance(
            int player,
            int position,
            int face,
            int cardId,
            string cardName,
            ulong runEffectSeq,
            int turn,
            int phase,
            ulong eventId,
            bool handOpen,
            LlmPublicVisibilityMode mode)
        {
            if (!LlmAbsolutePublicVisibility.CanExposeCardIdentity(position, face, handOpen, mode))
            {
                return null;
            }
            if (cardId <= 0)
            {
                return null;
            }
            return new LlmPublicDuelEvent()
            {
                EventId = eventId,
                RunEffectSeq = runEffectSeq,
                Turn = turn,
                Phase = phase,
                ActorPlayer = UnknownActorPlayer,
                TargetPlayer = player,
                CardPlayer = player,
                Kind = LlmPublicDuelEventKind.CardMovedPublic,
                CardId = cardId,
                CardName = cardName,
                DestinationZone = LlmPublicHistoryRedactionPolicy.MapPublicZone(position),
                Evidence = LlmPublicHistoryEvidence.PublicStateDelta,
                SourceSignature = "fixture:public_card:seq:" + runEffectSeq +
                    ":player:" + player + ":pos:" + position + ":cid:" + cardId + ":occ:1",
                CausedByEventId = null,
            };
        }

        static LlmPublicDuelEvent BaseOutcome(
            ref ulong eventId,
            ulong runEffectSeq,
            int turn,
            int phase,
            LlmPublicDuelEventKind kind,
            string sourceSignature,
            int targetPlayer)
        {
            LlmPublicDuelEvent evt = new LlmPublicDuelEvent()
            {
                EventId = eventId,
                RunEffectSeq = runEffectSeq,
                Turn = turn,
                Phase = phase,
                ActorPlayer = UnknownActorPlayer,
                TargetPlayer = targetPlayer,
                CardPlayer = -1,
                Kind = kind,
                Evidence = LlmPublicHistoryEvidence.PublicStateDelta,
                SourceSignature = sourceSignature,
                CausedByEventId = null,
            };
            eventId++;
            return evt;
        }
    }

    /// <summary>
    /// Fail-soft card-name enrichment for public history events (authoritative card id already present).
    /// Used on client ingest so broker history receives names without PvP catalog hot-path cost.
    /// </summary>
    static class LlmPublicHistoryCardNameEnrichment
    {
        public delegate string ResolveName(int cardId);

        public static void TryEnrich(LlmPublicDuelEvent evt, ResolveName resolve)
        {
            if (evt == null || resolve == null)
            {
                return;
            }
            if (!evt.CardId.HasValue || evt.CardId.Value <= 0)
            {
                return;
            }
            if (!string.IsNullOrEmpty(evt.CardName))
            {
                return;
            }
            try
            {
                string name = resolve(evt.CardId.Value);
                if (!string.IsNullOrEmpty(name))
                {
                    evt.CardName = name;
                }
            }
            catch
            {
                // Fail soft: keep id, leave name unset.
            }
        }

        public static string ResolveFromCatalog(int cardId, ILlmCardCatalog catalog)
        {
            if (catalog == null || cardId <= 0)
            {
                return null;
            }
            try
            {
                LlmCardMetadata meta = catalog.GetCard(cardId);
                if (meta == null || string.IsNullOrEmpty(meta.Name))
                {
                    return null;
                }
                return meta.Name;
            }
            catch
            {
                return null;
            }
        }
    }
}

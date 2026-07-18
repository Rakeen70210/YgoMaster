using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace YgoMaster
{
    enum LlmPublicDuelEventKind
    {
        Unknown,
        MovePhase,
        NormalSummon,
        SpecialSummon,
        PendulumSummon,
        FlipSummon,
        SetMonster,
        SetSpellTrap,
        ActivateEffect,
        AttackDeclare,
        PositionChange,
        PublicTargetSelect,
        GenericAcceptedCommand,
        // Slice 3 public outcomes (public_state_delta evidence)
        TurnChanged,
        PhaseChanged,
        LifePointsChanged,
        CardMovedPublic,
        HandShuffled,
        DeckShuffled,
    }

    enum LlmPublicHistoryEvidence
    {
        AcceptedCommand,
        PublicStateDelta,
    }

    class LlmPublicDuelEvent
    {
        public ulong EventId;
        public ulong RunEffectSeq;
        public int Turn;
        public int Phase;
        public int ActorPlayer;
        /// <summary>Affected seat for outcomes (e.g. LP owner). -1 when not applicable.</summary>
        public int TargetPlayer;
        /// <summary>Card owner coordinate for card outcomes. -1 when not applicable.</summary>
        public int CardPlayer;
        public LlmPublicDuelEventKind Kind;
        public int? CardId;
        public string CardName;
        public string SourceZone;
        public string DestinationZone;
        public string TargetZone;
        public LlmPublicHistoryEvidence Evidence;
        /// <summary>Optional correlation to a prior accepted intent event id. Always null until live-proven.</summary>
        public ulong? CausedByEventId;
        /// <summary>Dedupe key for outcome/raw-derived events (PvP-authored).</summary>
        public string SourceSignature;
        /// <summary>Client duel generation when the event was accepted into history (0 if unset).</summary>
        public int DuelGeneration;
    }

    class LlmAcceptedCommandCapture
    {
        public ulong RunEffectSeq { get; set; }
        public ulong EngineRunEffectSeq { get; set; }
        public int ActorPlayer { get; set; }
        public int CommandPlayer { get; set; }
        public int Position { get; set; }
        public int Index { get; set; }
        public DuelCommandType Command { get; set; }
        public int CardId { get; set; }
        public string CardName { get; set; }
        public int CardUniqueId { get; set; }
        public int CardFace { get; set; }
        public bool SourceFromHand { get; set; }
        public int HandIndex { get; set; }
        public string Metadata { get; set; }
        public int Turn { get; set; }
        public int Phase { get; set; }
    }

    class LlmPublicDuelEventCreationResult
    {
        public bool Accepted;
        public LlmPublicDuelEvent Event;

        public static LlmPublicDuelEventCreationResult NoEvent()
        {
            return new LlmPublicDuelEventCreationResult()
            {
                Accepted = false,
                Event = null,
            };
        }

        public static LlmPublicDuelEventCreationResult FromEvent(LlmPublicDuelEvent publicEvent)
        {
            return new LlmPublicDuelEventCreationResult()
            {
                Accepted = publicEvent != null,
                Event = publicEvent,
            };
        }
    }

    /// <summary>
    /// Semantically immutable public duel history snapshot.
    /// Construction and property access deep-clone events and nested collections so
    /// callers cannot mutate subsequent reads from this state (net48-compatible).
    /// </summary>
    class LlmDuelHistoryState
    {
        readonly List<LlmPublicDuelEvent> events;
        readonly List<Dictionary<string, object>> priorTurnSummaries;
        readonly List<Dictionary<string, object>> revealedCardContext;
        readonly List<string> gapWarnings;

        public int HistoryVersion { get; private set; }
        public ulong LastEventId { get; private set; }
        public ulong FirstDetailedEventId { get; private set; }
        public bool HistoryCompacted { get; private set; }
        /// <summary>"ok" or "over_budget_protected_detail" when mandatory detail cannot fit.</summary>
        public string BudgetStatus { get; private set; }
        public string BudgetReason { get; private set; }
        /// <summary>Frozen duel generation for this immutable projection/snapshot.</summary>
        public int DuelGeneration { get; private set; }

        public IList<LlmPublicDuelEvent> Events
        {
            get
            {
                List<LlmPublicDuelEvent> copy = new List<LlmPublicDuelEvent>();
                foreach (LlmPublicDuelEvent evt in events)
                {
                    copy.Add(CloneEvent(evt));
                }
                return new ReadOnlyCollection<LlmPublicDuelEvent>(copy);
            }
        }

        public IList<Dictionary<string, object>> PriorTurnSummaries
        {
            get
            {
                return new ReadOnlyCollection<Dictionary<string, object>>(DeepCloneDictList(priorTurnSummaries));
            }
        }

        public IList<Dictionary<string, object>> RevealedCardContext
        {
            get
            {
                return new ReadOnlyCollection<Dictionary<string, object>>(DeepCloneDictList(revealedCardContext));
            }
        }

        public IList<string> GapWarnings
        {
            get { return new ReadOnlyCollection<string>(new List<string>(gapWarnings)); }
        }

        public LlmDuelHistoryState()
            : this(
                1,
                0,
                1,
                false,
                new List<LlmPublicDuelEvent>(),
                new List<Dictionary<string, object>>(),
                new List<Dictionary<string, object>>(),
                new List<string>(),
                "ok",
                null,
                0)
        {
        }

        public LlmDuelHistoryState(
            int historyVersion,
            ulong lastEventId,
            ulong firstDetailedEventId,
            bool historyCompacted,
            IEnumerable<LlmPublicDuelEvent> events,
            IEnumerable<Dictionary<string, object>> priorTurnSummaries,
            IEnumerable<Dictionary<string, object>> revealedCardContext,
            IEnumerable<string> gapWarnings)
            : this(
                historyVersion,
                lastEventId,
                firstDetailedEventId,
                historyCompacted,
                events,
                priorTurnSummaries,
                revealedCardContext,
                gapWarnings,
                "ok",
                null,
                0)
        {
        }

        public LlmDuelHistoryState(
            int historyVersion,
            ulong lastEventId,
            ulong firstDetailedEventId,
            bool historyCompacted,
            IEnumerable<LlmPublicDuelEvent> events,
            IEnumerable<Dictionary<string, object>> priorTurnSummaries,
            IEnumerable<Dictionary<string, object>> revealedCardContext,
            IEnumerable<string> gapWarnings,
            string budgetStatus,
            string budgetReason)
            : this(
                historyVersion,
                lastEventId,
                firstDetailedEventId,
                historyCompacted,
                events,
                priorTurnSummaries,
                revealedCardContext,
                gapWarnings,
                budgetStatus,
                budgetReason,
                0)
        {
        }

        public LlmDuelHistoryState(
            int historyVersion,
            ulong lastEventId,
            ulong firstDetailedEventId,
            bool historyCompacted,
            IEnumerable<LlmPublicDuelEvent> events,
            IEnumerable<Dictionary<string, object>> priorTurnSummaries,
            IEnumerable<Dictionary<string, object>> revealedCardContext,
            IEnumerable<string> gapWarnings,
            string budgetStatus,
            string budgetReason,
            int duelGeneration)
        {
            HistoryVersion = historyVersion;
            LastEventId = lastEventId;
            FirstDetailedEventId = firstDetailedEventId;
            HistoryCompacted = historyCompacted;
            BudgetStatus = string.IsNullOrEmpty(budgetStatus) ? "ok" : budgetStatus;
            BudgetReason = budgetReason;
            DuelGeneration = duelGeneration;
            this.events = new List<LlmPublicDuelEvent>();
            if (events != null)
            {
                foreach (LlmPublicDuelEvent evt in events)
                {
                    this.events.Add(CloneEvent(evt));
                }
            }
            this.priorTurnSummaries = DeepCloneDictList(priorTurnSummaries);
            this.revealedCardContext = DeepCloneDictList(revealedCardContext);
            this.gapWarnings = new List<string>();
            if (gapWarnings != null)
            {
                foreach (string warning in gapWarnings)
                {
                    this.gapWarnings.Add(warning);
                }
            }
        }

        public static List<Dictionary<string, object>> DeepCloneDictList(
            IEnumerable<Dictionary<string, object>> source)
        {
            List<Dictionary<string, object>> copy = new List<Dictionary<string, object>>();
            if (source == null)
            {
                return copy;
            }
            foreach (Dictionary<string, object> item in source)
            {
                copy.Add(DeepCloneDict(item));
            }
            return copy;
        }

        public static Dictionary<string, object> DeepCloneDict(Dictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }
            Dictionary<string, object> copy = new Dictionary<string, object>();
            List<string> keys = new List<string>(source.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                copy[key] = DeepCloneValue(source[key]);
            }
            return copy;
        }

        public static object DeepCloneValue(object value)
        {
            if (value == null)
            {
                return null;
            }
            Dictionary<string, object> asDict = value as Dictionary<string, object>;
            if (asDict != null)
            {
                return DeepCloneDict(asDict);
            }
            IList asList = value as IList;
            if (asList != null && !(value is string))
            {
                List<object> listCopy = new List<object>();
                foreach (object item in asList)
                {
                    listCopy.Add(DeepCloneValue(item));
                }
                return listCopy;
            }
            return value;
        }

        public static LlmPublicDuelEvent CloneEvent(LlmPublicDuelEvent evt)
        {
            if (evt == null)
            {
                return null;
            }
            return new LlmPublicDuelEvent()
            {
                EventId = evt.EventId,
                RunEffectSeq = evt.RunEffectSeq,
                Turn = evt.Turn,
                Phase = evt.Phase,
                ActorPlayer = evt.ActorPlayer,
                TargetPlayer = evt.TargetPlayer,
                CardPlayer = evt.CardPlayer,
                Kind = evt.Kind,
                CardId = evt.CardId,
                CardName = evt.CardName,
                SourceZone = evt.SourceZone,
                DestinationZone = evt.DestinationZone,
                TargetZone = evt.TargetZone,
                Evidence = evt.Evidence,
                CausedByEventId = evt.CausedByEventId,
                SourceSignature = evt.SourceSignature,
                DuelGeneration = evt.DuelGeneration,
            };
        }
    }

    class LlmDuelHistoryTrackerOptions
    {
        public int MaxSerializedBytes = 32 * 1024;
        public int MaxDetailedEvents = 128;
    }

    class LlmDuelHistoryAppendResult
    {
        public bool Accepted;
        public bool NewlyAppended;
        public string Error;

        public static LlmDuelHistoryAppendResult Rejected(string error)
        {
            return new LlmDuelHistoryAppendResult()
            {
                Accepted = false,
                NewlyAppended = false,
                Error = error,
            };
        }

        public static LlmDuelHistoryAppendResult Appended()
        {
            return new LlmDuelHistoryAppendResult()
            {
                Accepted = true,
                NewlyAppended = true,
                Error = null,
            };
        }

        public static LlmDuelHistoryAppendResult IgnoredDelivery(string reason)
        {
            return new LlmDuelHistoryAppendResult()
            {
                Accepted = true,
                NewlyAppended = false,
                Error = reason,
            };
        }
    }
}

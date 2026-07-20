using System;
using System.Collections.Generic;

namespace YgoMaster
{
    enum LlmSummonInteractionDisposition
    {
        Ignored,
        Started,
        Retry,
        TemporaryCpu,
        Preserved,
        Completed,
        Reset,
        Rejected,
    }

    class LlmSummonCompletionEvidence
    {
        public bool ExtraDeckCardRemoved { get; set; }
        public bool SummonedCardAppearsOnField { get; set; }
        public bool MaterialsResolved { get; set; }
        public bool ReachedNextDecisionBoundary { get; set; }

        public bool IsAuthoritative
        {
            get
            {
                return ExtraDeckCardRemoved &&
                    SummonedCardAppearsOnField &&
                    MaterialsResolved &&
                    ReachedNextDecisionBoundary;
            }
        }
    }

    class LlmSummonResourceFacts
    {
        public int ExtraDeckCount { get; set; }
        public int SummonedFieldCount { get; set; }
        public bool IsExtraDeck { get; set; }
        public bool RequiresMaterialConsumption { get; set; }
        public Dictionary<int, int> FieldCardCounts { get; set; }
        public Dictionary<int, int> MaterialSourceCardCounts { get; set; }

        public LlmSummonResourceFacts()
        {
            FieldCardCounts = new Dictionary<int, int>();
            MaterialSourceCardCounts = new Dictionary<int, int>();
        }

        public static LlmSummonResourceFacts FromSnapshot(
            DecisionSnapshot snapshot,
            int cardId,
            string sourceFamily)
        {
            if (snapshot == null || snapshot.SelfResources == null)
            {
                return null;
            }

            string normalizedFamily = Normalize(sourceFamily);
            bool isExtraDeck = false;
            int extraDeckCount = 0;
            if (snapshot.SelfResources.ExtraDeck != null)
            {
                foreach (LlmSelfResourceExtraDeckEntry entry in snapshot.SelfResources.ExtraDeck)
                {
                    if (entry == null || entry.CardId != cardId)
                    {
                        continue;
                    }

                    isExtraDeck = true;
                    extraDeckCount += entry.DuplicateCount > 0 ? entry.DuplicateCount : 1;
                    if (string.IsNullOrEmpty(normalizedFamily) || normalizedFamily == "unknown")
                    {
                        normalizedFamily = Normalize(entry.SummonFamily);
                    }
                }
            }

            if (normalizedFamily == "xyz" || normalizedFamily == "xyz_pend" ||
                normalizedFamily == "synchro" || normalizedFamily == "link" ||
                normalizedFamily == "fusion")
            {
                isExtraDeck = true;
            }

            Dictionary<int, int> fieldCounts = CountCards(snapshot.SelfResources.Field);
            Dictionary<int, int> materialSourceCounts = CountCards(snapshot.SelfResources.Hand);
            AddCounts(materialSourceCounts, fieldCounts);
            int summonedFieldCount;
            if (!fieldCounts.TryGetValue(cardId, out summonedFieldCount))
            {
                summonedFieldCount = 0;
            }

            return new LlmSummonResourceFacts()
            {
                ExtraDeckCount = extraDeckCount,
                SummonedFieldCount = summonedFieldCount,
                IsExtraDeck = isExtraDeck,
                RequiresMaterialConsumption = IsMaterialSummonFamily(normalizedFamily),
                FieldCardCounts = fieldCounts,
                MaterialSourceCardCounts = materialSourceCounts,
            };
        }

        public LlmSummonCompletionEvidence CompareTo(
            LlmSummonResourceFacts after,
            bool reachedNextDecisionBoundary)
        {
            if (after == null)
            {
                return null;
            }

            bool extraDeckRemoved = !IsExtraDeck ||
                after.ExtraDeckCount < ExtraDeckCount;
            bool summonedCardAppears =
                after.SummonedFieldCount > SummonedFieldCount;
            bool materialsResolved = !RequiresMaterialConsumption ||
                HasMaterialCountDecrease(after);

            return new LlmSummonCompletionEvidence()
            {
                ExtraDeckCardRemoved = extraDeckRemoved,
                SummonedCardAppearsOnField = summonedCardAppears,
                MaterialsResolved = materialsResolved,
                ReachedNextDecisionBoundary = reachedNextDecisionBoundary,
            };
        }

        bool HasMaterialCountDecrease(LlmSummonResourceFacts after)
        {
            if (HasCountDecrease(FieldCardCounts, after.FieldCardCounts))
            {
                return true;
            }
            return HasCountDecrease(MaterialSourceCardCounts, after.MaterialSourceCardCounts);
        }

        static bool HasCountDecrease(
            Dictionary<int, int> beforeCounts,
            Dictionary<int, int> afterCounts)
        {
            if (beforeCounts == null || afterCounts == null)
            {
                return false;
            }
            foreach (KeyValuePair<int, int> before in beforeCounts)
            {
                int afterCount;
                if (!afterCounts.TryGetValue(before.Key, out afterCount))
                {
                    afterCount = 0;
                }
                if (before.Value > afterCount)
                {
                    return true;
                }
            }
            return false;
        }

        static Dictionary<int, int> CountCards(IList<LlmSelfResourceCard> cards)
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            if (cards == null)
            {
                return counts;
            }
            foreach (LlmSelfResourceCard card in cards)
            {
                if (card == null || card.CardId <= 0)
                {
                    continue;
                }
                int count;
                counts.TryGetValue(card.CardId, out count);
                counts[card.CardId] = count + 1;
            }
            return counts;
        }

        static void AddCounts(Dictionary<int, int> destination, Dictionary<int, int> source)
        {
            if (destination == null || source == null)
            {
                return;
            }
            foreach (KeyValuePair<int, int> pair in source)
            {
                int count;
                destination.TryGetValue(pair.Key, out count);
                destination[pair.Key] = count + pair.Value;
            }
        }

        static bool IsMaterialSummonFamily(string sourceFamily)
        {
            return sourceFamily == "xyz" || sourceFamily == "xyz_pend" ||
                sourceFamily == "synchro" || sourceFamily == "link" ||
                sourceFamily == "fusion";
        }

        static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }
    }

    class LlmSummonInteractionObservation
    {
        public int DuelGeneration { get; set; }
        public ulong RunEffectSeq { get; set; }
        public int ActingPlayer { get; set; }
        public int CardUniqueId { get; set; }
        public int CardId { get; set; }
        public string SourceFamily { get; set; }
        public string ExpectedFollowupFamily { get; set; }
        public bool IsPlacementPrompt { get; set; }
        public bool IsSummonTransition { get; set; }
        public bool IsCancellation { get; set; }
        public bool IsBoundary { get; set; }
        public LlmSummonCompletionEvidence CompletionEvidence { get; set; }
        public LlmSummonResourceFacts ResourceFacts { get; set; }
    }

    class LlmSummonInteractionDecision
    {
        public LlmSummonInteractionDisposition Disposition { get; private set; }
        public string Reason { get; private set; }
        public int Attempt { get; private set; }
        public string StableSignature { get; private set; }
        public string InteractionSignature { get; private set; }
        public ulong OriginatingRunEffectSeq { get; private set; }

        public bool ShouldUseTemporaryCpu
        {
            get { return Disposition == LlmSummonInteractionDisposition.TemporaryCpu; }
        }

        internal static LlmSummonInteractionDecision Create(
            LlmSummonInteractionDisposition disposition,
            string reason,
            int attempt,
            LlmSummonInteractionKey key)
        {
            return new LlmSummonInteractionDecision()
            {
                Disposition = disposition,
                Reason = reason,
                Attempt = attempt,
                StableSignature = key == null ? null : key.StableSignature,
                InteractionSignature = key == null ? null : key.InteractionSignature,
                OriginatingRunEffectSeq = key == null ? 0 : key.OriginatingRunEffectSeq,
            };
        }
    }

    class LlmSummonInteractionKey
    {
        public int DuelGeneration { get; private set; }
        public int ActingPlayer { get; private set; }
        public int CardUniqueId { get; private set; }
        public int CardId { get; private set; }
        public string SourceFamily { get; private set; }
        public string ExpectedFollowupFamily { get; private set; }
        public ulong OriginatingRunEffectSeq { get; private set; }
        public string StableSignature { get; private set; }
        public string InteractionSignature { get; private set; }

        internal static LlmSummonInteractionKey FromObservation(
            LlmSummonInteractionObservation observation)
        {
            string sourceFamily = Normalize(observation.SourceFamily);
            string followupFamily = Normalize(observation.ExpectedFollowupFamily);
            string stable = observation.DuelGeneration + ":" +
                observation.ActingPlayer + ":" +
                observation.CardUniqueId + ":" +
                observation.CardId + ":" +
                sourceFamily + ":" +
                followupFamily;

            return new LlmSummonInteractionKey()
            {
                DuelGeneration = observation.DuelGeneration,
                ActingPlayer = observation.ActingPlayer,
                CardUniqueId = observation.CardUniqueId,
                CardId = observation.CardId,
                SourceFamily = sourceFamily,
                ExpectedFollowupFamily = followupFamily,
                OriginatingRunEffectSeq = observation.RunEffectSeq,
                StableSignature = stable,
                InteractionSignature = stable + ":origin:" + observation.RunEffectSeq,
            };
        }

        internal bool MatchesStableIdentity(LlmSummonInteractionKey other)
        {
            return other != null &&
                DuelGeneration == other.DuelGeneration &&
                ActingPlayer == other.ActingPlayer &&
                CardUniqueId == other.CardUniqueId &&
                CardId == other.CardId &&
                string.Equals(SourceFamily, other.SourceFamily, StringComparison.Ordinal) &&
                string.Equals(ExpectedFollowupFamily, other.ExpectedFollowupFamily, StringComparison.Ordinal);
        }

        static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }
    }

    /// <summary>
    /// Tracks one controlled-seat summon interaction across engine-generated
    /// material, animation, and placement windows. It never authorizes a future
    /// action; it only decides whether the current mechanical placement may be
    /// retried or should be delegated to the server-side CPU lease.
    /// </summary>
    class LlmSummonInteractionTracker
    {
        LlmSummonInteractionKey activeKey;
        LlmSummonResourceFacts initialFacts;
        readonly int maxPlacementAttempts;
        int placementAttemptCount;

        public LlmSummonInteractionTracker(int maxPlacementAttempts)
        {
            this.maxPlacementAttempts = maxPlacementAttempts < 1 ? 1 : maxPlacementAttempts;
        }

        public bool IsActive { get { return activeKey != null; } }
        public int PlacementAttemptCount { get { return placementAttemptCount; } }
        public string ActiveStableSignature
        {
            get { return activeKey == null ? null : activeKey.StableSignature; }
        }
        public ulong OriginatingRunEffectSeq
        {
            get { return activeKey == null ? 0 : activeKey.OriginatingRunEffectSeq; }
        }
        public int ActiveCardId
        {
            get { return activeKey == null ? 0 : activeKey.CardId; }
        }
        public int ActiveCardUniqueId
        {
            get { return activeKey == null ? 0 : activeKey.CardUniqueId; }
        }
        public int ActiveDuelGeneration
        {
            get { return activeKey == null ? 0 : activeKey.DuelGeneration; }
        }
        public int ActiveActingPlayer
        {
            get { return activeKey == null ? -1 : activeKey.ActingPlayer; }
        }
        public string ActiveSourceFamily
        {
            get { return activeKey == null ? null : activeKey.SourceFamily; }
        }
        public string ActiveExpectedFollowupFamily
        {
            get { return activeKey == null ? null : activeKey.ExpectedFollowupFamily; }
        }

        public LlmSummonInteractionDecision Observe(
            LlmSummonInteractionObservation observation)
        {
            if (observation == null)
            {
                return Decision(LlmSummonInteractionDisposition.Ignored, "missing_observation", null);
            }

            if (observation.ActingPlayer < 0 || observation.ActingPlayer > 1)
            {
                return Decision(LlmSummonInteractionDisposition.Rejected, "invalid_acting_player", activeKey);
            }

            LlmSummonInteractionKey observedKey =
                LlmSummonInteractionKey.FromObservation(observation);

            if (activeKey != null &&
                (activeKey.DuelGeneration != observation.DuelGeneration ||
                    activeKey.ActingPlayer != observation.ActingPlayer))
            {
                return Decision(LlmSummonInteractionDisposition.Rejected,
                    "summon_interaction_generation_or_seat_mismatch", activeKey);
            }

            if (activeKey != null && HasCardIdentity(observation) &&
                !activeKey.MatchesStableIdentity(observedKey))
            {
                return Decision(LlmSummonInteractionDisposition.Rejected,
                    "summon_interaction_identity_mismatch", activeKey);
            }

            if (activeKey != null && observation.CompletionEvidence != null &&
                observation.CompletionEvidence.IsAuthoritative)
            {
                if (!activeKey.MatchesStableIdentity(observedKey))
                {
                    return Decision(LlmSummonInteractionDisposition.Rejected,
                        "summon_interaction_completion_identity_mismatch", activeKey);
                }
                LlmSummonInteractionDecision completed = Decision(
                    LlmSummonInteractionDisposition.Completed,
                    "summon_state_confirmed_complete",
                    activeKey);
                Reset();
                return completed;
            }

            if (activeKey != null && observation.IsCancellation)
            {
                LlmSummonInteractionDecision reset = Decision(
                    LlmSummonInteractionDisposition.Reset,
                    "summon_interaction_cancelled",
                    activeKey);
                Reset();
                return reset;
            }

            if (!observation.IsPlacementPrompt)
            {
                if (activeKey == null)
                {
                    return Decision(LlmSummonInteractionDisposition.Ignored,
                        "no_active_summon_interaction", null);
                }

                if (observation.IsBoundary && !observation.IsSummonTransition)
                {
                    LlmSummonInteractionDecision reset = Decision(
                        LlmSummonInteractionDisposition.Reset,
                        "summon_interaction_boundary",
                        activeKey);
                    Reset();
                    return reset;
                }

                return Decision(LlmSummonInteractionDisposition.Preserved,
                    observation.CompletionEvidence == null ?
                        "summon_transition_preserved" :
                        "summon_completion_not_yet_proven",
                    activeKey);
            }

            if (activeKey == null)
            {
                activeKey = observedKey;
                initialFacts = observation.ResourceFacts;
                placementAttemptCount = 1;
                return Decision(LlmSummonInteractionDisposition.Started,
                    "summon_interaction_started", activeKey);
            }

            if (!activeKey.MatchesStableIdentity(observedKey))
            {
                return Decision(LlmSummonInteractionDisposition.Rejected,
                    "summon_interaction_identity_mismatch", activeKey);
            }

            if (placementAttemptCount >= maxPlacementAttempts)
            {
                return Decision(LlmSummonInteractionDisposition.TemporaryCpu,
                    "summon_placement_retry_exhausted", activeKey);
            }

            placementAttemptCount++;
            return Decision(LlmSummonInteractionDisposition.Retry,
                "summon_placement_retry", activeKey);
        }

        public LlmSummonInteractionDecision Begin(
            LlmSummonInteractionObservation observation)
        {
            if (observation == null)
            {
                return Decision(LlmSummonInteractionDisposition.Ignored,
                    "missing_observation", null);
            }

            if (observation.ActingPlayer < 0 || observation.ActingPlayer > 1)
            {
                return Decision(LlmSummonInteractionDisposition.Rejected,
                    "invalid_acting_player", activeKey);
            }

            LlmSummonInteractionKey observedKey =
                LlmSummonInteractionKey.FromObservation(observation);
            if (activeKey != null)
            {
                return Decision(LlmSummonInteractionDisposition.Rejected,
                    "summon_interaction_already_active", activeKey);
            }

            activeKey = observedKey;
            initialFacts = observation.ResourceFacts;
            // The broker-selected summon is the first bounded attempt. The
            // engine's Decide placement is the one permitted retry.
            placementAttemptCount = 1;
            return Decision(LlmSummonInteractionDisposition.Started,
                "summon_interaction_started", activeKey);
        }

        public LlmSummonCompletionEvidence CompareCurrentResources(
            LlmSummonResourceFacts current,
            bool reachedNextDecisionBoundary)
        {
            if (activeKey == null || initialFacts == null)
            {
                return null;
            }
            return initialFacts.CompareTo(current, reachedNextDecisionBoundary);
        }

        public void Reset()
        {
            activeKey = null;
            initialFacts = null;
            placementAttemptCount = 0;
        }

        LlmSummonInteractionDecision Decision(
            LlmSummonInteractionDisposition disposition,
            string reason,
            LlmSummonInteractionKey key)
        {
            return LlmSummonInteractionDecision.Create(
                disposition,
                reason,
                placementAttemptCount,
                key);
        }

        static bool HasCardIdentity(LlmSummonInteractionObservation observation)
        {
            return observation.CardUniqueId > 0 || observation.CardId > 0 ||
                !string.IsNullOrEmpty(observation.SourceFamily) ||
                !string.IsNullOrEmpty(observation.ExpectedFollowupFamily);
        }
    }
}

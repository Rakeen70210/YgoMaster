using System;
using System.Collections.Generic;

namespace YgoMaster
{
    sealed class LlmEffectApplicabilityAnnotation
    {
        public bool IsGrounded { get; private set; }
        public bool? EffectExpectedToApply { get; private set; }
        public string Reason { get; private set; }
        public string PrimaryEffectCapability { get; private set; }
        public int SourceCardId { get; private set; }
        public int SourceOriginalAtk { get; private set; }
        public int TargetCardId { get; private set; }
        public int? SourceOriginalAtkAtMost { get; private set; }
        public string Provenance { get; private set; }
        public bool HasGroundedSecondaryBenefit { get; private set; }

        public static LlmEffectApplicabilityAnnotation Create(
            bool grounded,
            bool? expectedToApply,
            string reason,
            string primaryEffectCapability,
            int sourceCardId,
            int sourceOriginalAtk,
            int targetCardId,
            int? sourceOriginalAtkAtMost,
            string provenance,
            bool hasGroundedSecondaryBenefit)
        {
            return new LlmEffectApplicabilityAnnotation()
            {
                IsGrounded = grounded,
                EffectExpectedToApply = expectedToApply,
                Reason = reason,
                PrimaryEffectCapability = primaryEffectCapability,
                SourceCardId = sourceCardId,
                SourceOriginalAtk = sourceOriginalAtk,
                TargetCardId = targetCardId,
                SourceOriginalAtkAtMost = sourceOriginalAtkAtMost,
                Provenance = provenance,
                HasGroundedSecondaryBenefit = hasGroundedSecondaryBenefit,
            };
        }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>()
            {
                { "is_grounded", IsGrounded },
                { "effect_expected_to_apply", EffectExpectedToApply },
                { "reason", Reason },
                { "primary_effect_capability", PrimaryEffectCapability },
                { "source_card_id", SourceCardId },
                { "source_original_atk", SourceOriginalAtk },
                { "target_card_id", TargetCardId },
                { "source_original_atk_at_most", SourceOriginalAtkAtMost },
                { "provenance", Provenance },
                { "has_grounded_secondary_benefit", HasGroundedSecondaryBenefit },
            };
        }
    }

    static class LlmEffectApplicabilityAnalyzer
    {
        public const string ErrorEffectApplicabilityContradiction =
            "effect_applicability_contradiction";
        public const string BlockedByActivatedMonsterEffectImmunity =
            "blocked_by_activated_monster_effect_immunity";

        public static LlmEffectApplicabilityAnnotation Analyze(
            DecisionSnapshot snapshot,
            LegalAction action)
        {
            return Analyze(snapshot, action, LlmCardEffectCapabilityCatalog.Default);
        }

        public static LlmEffectApplicabilityAnnotation Analyze(
            DecisionSnapshot snapshot,
            LegalAction action,
            ILlmCardEffectCapabilitySource source)
        {
            if (snapshot == null || action == null || source == null
                || action.Kind != LegalActionKind.Command
                || action.Command != DuelCommandType.Action
                || action.CardId <= 0)
            {
                return null;
            }

            LlmCardEffectCapabilityFacts sourceFacts;
            if (!source.TryGet(action.CardId, out sourceFacts)
                || sourceFacts == null
                || !sourceFacts.TargetsOpponentMonster)
            {
                return null;
            }

            int sourceAtk = action.Card != null ? action.Card.Atk : 0;
            bool hasSecondary = sourceFacts.SecondaryBenefits != null
                && sourceFacts.SecondaryBenefits.Length > 0;
            PublicKnownCard target = FindSinglePublicOpponentMonster(snapshot);
            if (target == null)
            {
                return LlmEffectApplicabilityAnnotation.Create(
                    false, null, "target_unknown_or_ambiguous",
                    sourceFacts.PrimaryEffectCapability,
                    action.CardId, sourceAtk, 0, null,
                    "unknown_card_semantics", hasSecondary);
            }

            LlmCardEffectCapabilityFacts targetFacts;
            if (!source.TryGet(target.CardId, out targetFacts)
                || targetFacts == null
                || !sourceFacts.ApplicabilityGrounded
                || !targetFacts.ApplicabilityGrounded)
            {
                return LlmEffectApplicabilityAnnotation.Create(
                    false, null, "unknown_card_semantics",
                    sourceFacts.PrimaryEffectCapability,
                    action.CardId, sourceAtk, target.CardId, null,
                    "unknown_card_semantics", hasSecondary);
            }

            int? threshold =
                targetFacts.UnaffectedByActivatedMonsterEffectsFromSourceOriginalAtkAtMost;
            bool sourceIsMonster = IsMonster(action.Card);
            if (sourceIsMonster && threshold.HasValue && sourceAtk <= threshold.Value)
            {
                return LlmEffectApplicabilityAnnotation.Create(
                    true, false, BlockedByActivatedMonsterEffectImmunity,
                    sourceFacts.PrimaryEffectCapability,
                    action.CardId, sourceAtk, target.CardId, threshold,
                    "structured_capability_catalog", hasSecondary);
            }

            return LlmEffectApplicabilityAnnotation.Create(
                true, true, "applicability_predicates_pass",
                sourceFacts.PrimaryEffectCapability,
                action.CardId, sourceAtk, target.CardId, threshold,
                "structured_capability_catalog", hasSecondary);
        }

        public static bool IsHardBlocked(LegalAction action)
        {
            return action != null
                && action.EffectApplicability != null
                && action.EffectApplicability.IsGrounded
                && action.EffectApplicability.EffectExpectedToApply == false
                && !action.EffectApplicability.HasGroundedSecondaryBenefit;
        }

        static PublicKnownCard FindSinglePublicOpponentMonster(DecisionSnapshot snapshot)
        {
            if (snapshot.PublicState == null || snapshot.PublicState.Players == null)
            {
                return null;
            }
            PublicKnownCard selected = null;
            foreach (PublicPlayerState player in snapshot.PublicState.Players)
            {
                if (player == null || player.Player == snapshot.ControlledPlayer)
                {
                    continue;
                }
                foreach (PublicKnownCard card in player.KnownCards)
                {
                    if (card == null || card.CardId <= 0 || card.Position < 0
                        || card.Position > 4 || !IsPublicFaceUp(card.Face))
                    {
                        continue;
                    }
                    if (selected != null)
                    {
                        return null;
                    }
                    selected = card;
                }
            }
            return selected;
        }

        static bool IsPublicFaceUp(int face)
        {
            return face == LlmDllRuntimeFace.FaceUpPublic
                || face == LlmPublicHistoryRedactionPolicy.PublicFaceUpValue;
        }

        static bool IsMonster(LlmCardMetadata card)
        {
            if (card == null)
            {
                return false;
            }
            string family = card.SummonFamily ?? string.Empty;
            if (family == "spell" || family == "trap")
            {
                return false;
            }
            string kind = card.Kind ?? string.Empty;
            return kind.IndexOf("Magic", StringComparison.OrdinalIgnoreCase) < 0
                && kind.IndexOf("Spell", StringComparison.OrdinalIgnoreCase) < 0
                && kind.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
}

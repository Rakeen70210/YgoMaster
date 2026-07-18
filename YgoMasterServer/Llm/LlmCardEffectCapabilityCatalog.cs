using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Grounded, reusable effect-capability / resolution facts for a card id.
    /// Rules that consume these facts are card-agnostic; this catalog is only a
    /// structured data source (not a named-card branch inside scoring logic).
    /// </summary>
    class LlmCardEffectCapabilityFacts
    {
        public int CardId { get; set; }
        /// <summary>Response capabilities: destroy, negate_activation, negate_effect, …</summary>
        public string[] ResponseCapabilities { get; set; }
        /// <summary>Secondary benefits enumerated separately from primary disruption.</summary>
        public string[] SecondaryBenefits { get; set; }
        /// <summary>
        /// When set, this card as a chain target is a one-shot Spell/Trap whose activation
        /// does not require remaining face-up to finish resolution.
        /// </summary>
        public bool? IsOneShotSpellOrTrap { get; set; }
        /// <summary>
        /// When set, this card's ongoing resolution/effect requires remaining face-up
        /// (Continuous / Field / Equip class).
        /// </summary>
        public bool? RequiresRemainFaceUp { get; set; }
        public bool CapabilitiesGrounded { get; set; }
        public bool ResolutionDependencyGrounded { get; set; }

        public LlmCardEffectCapabilityFacts()
        {
            ResponseCapabilities = new string[0];
            SecondaryBenefits = new string[0];
        }
    }

    /// <summary>
    /// Optional pluggable source for capability/resolution facts.
    /// </summary>
    interface ILlmCardEffectCapabilitySource
    {
        bool TryGet(int cardId, out LlmCardEffectCapabilityFacts facts);
    }

    /// <summary>
    /// Default in-process capability/resolution fact table (YGOMASTER-LLM-005 Slice 2B).
    /// Entries are generic structured data; destroy-vs-negate rules never special-case names.
    /// Missing ids fail open as unknown_card_semantics at the analyzer boundary.
    /// </summary>
    static class LlmCardEffectCapabilityCatalog
    {
        static readonly Dictionary<int, LlmCardEffectCapabilityFacts> ByCardId =
            new Dictionary<int, LlmCardEffectCapabilityFacts>();
        static readonly object Gate = new object();
        static bool _seeded;

        /// <summary>Default singleton source used by production fact derivation.</summary>
        public static readonly ILlmCardEffectCapabilitySource Default =
            new DefaultCapabilitySource();

        static void EnsureSeeded()
        {
            if (_seeded)
            {
                return;
            }
            lock (Gate)
            {
                if (_seeded)
                {
                    return;
                }
                // Keys are Master Duel *internal* CardIds (authoritative for live logs /
                // LlmCardMetadata.CardId). Conversion source: YgoMaster/Data/YdkIds.txt
                // (YGOPro passcode → internal). Do NOT register passcodes as aliases unless
                // the production action path emits them (it does not).
                //
                // Destroy-only Normal Trap with optional Set-from-hand secondary clause.
                // YdkIds: 60082869 4988 (Dust Tornado)
                Register(new LlmCardEffectCapabilityFacts()
                {
                    CardId = 4988,
                    ResponseCapabilities = new[] { "destroy" },
                    SecondaryBenefits = new[] { "optional_set_spell_trap_from_hand" },
                    CapabilitiesGrounded = true,
                    ResolutionDependencyGrounded = false,
                });
                // Counter Trap with activation negation.
                // YdkIds: 41420027 4861 (Solemn Judgment)
                Register(new LlmCardEffectCapabilityFacts()
                {
                    CardId = 4861,
                    ResponseCapabilities = new[] { "negate_activation", "destroy" },
                    SecondaryBenefits = new string[0],
                    CapabilitiesGrounded = true,
                    ResolutionDependencyGrounded = false,
                });
                // Already-activated one-shot Normal Spell (target resolution facts).
                // YdkIds: 35726888 12800 (Foolish Burial Goods); live capture used 12800.
                Register(new LlmCardEffectCapabilityFacts()
                {
                    CardId = 12800,
                    ResponseCapabilities = new string[0],
                    SecondaryBenefits = new string[0],
                    IsOneShotSpellOrTrap = true,
                    RequiresRemainFaceUp = false,
                    CapabilitiesGrounded = false,
                    ResolutionDependencyGrounded = true,
                });
                // Continuous Spell remain-face-up control (target resolution).
                // YdkIds: 44656491 4938 (Messenger of Peace)
                Register(new LlmCardEffectCapabilityFacts()
                {
                    CardId = 4938,
                    ResponseCapabilities = new string[0],
                    SecondaryBenefits = new string[0],
                    IsOneShotSpellOrTrap = false,
                    RequiresRemainFaceUp = true,
                    CapabilitiesGrounded = false,
                    ResolutionDependencyGrounded = true,
                });
                _seeded = true;
            }
        }

        public static void Register(LlmCardEffectCapabilityFacts facts)
        {
            if (facts == null || facts.CardId <= 0)
            {
                return;
            }
            lock (Gate)
            {
                ByCardId[facts.CardId] = facts;
            }
        }

        public static bool TryGet(int cardId, out LlmCardEffectCapabilityFacts facts)
        {
            EnsureSeeded();
            lock (Gate)
            {
                return ByCardId.TryGetValue(cardId, out facts);
            }
        }

        /// <summary>
        /// Frame/kind based resolution dependency when card-id table has no entry.
        /// Continuous / Field / Equip → remain face-up; Normal Spell/Trap → one-shot.
        /// Returns false when structured frame/kind is insufficient.
        /// </summary>
        public static bool TryDeriveResolutionFromFrame(
            string kind,
            string frame,
            out bool isOneShot,
            out bool requiresRemainFaceUp,
            out bool grounded)
        {
            isOneShot = false;
            requiresRemainFaceUp = false;
            grounded = false;
            if (string.IsNullOrEmpty(frame) && string.IsNullOrEmpty(kind))
            {
                return false;
            }
            string f = (frame ?? string.Empty).Trim();
            string k = (kind ?? string.Empty).Trim();
            if (EqualsIgnore(f, "Continuous")
                || EqualsIgnore(f, "Field")
                || EqualsIgnore(f, "Equip")
                || EqualsIgnore(k, "Continuous Spell")
                || EqualsIgnore(k, "Continuous Trap")
                || EqualsIgnore(k, "Field Spell")
                || EqualsIgnore(k, "Equip Spell"))
            {
                isOneShot = false;
                requiresRemainFaceUp = true;
                grounded = true;
                return true;
            }
            bool isSpellOrTrap =
                EqualsIgnore(k, "Spell")
                || EqualsIgnore(k, "Trap")
                || EqualsIgnore(k, "spell")
                || EqualsIgnore(k, "trap")
                || (k.IndexOf("Spell", StringComparison.OrdinalIgnoreCase) >= 0)
                || (k.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0);
            if (EqualsIgnore(f, "Normal") && isSpellOrTrap)
            {
                isOneShot = true;
                requiresRemainFaceUp = false;
                grounded = true;
                return true;
            }
            return false;
        }

        static bool EqualsIgnore(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        sealed class DefaultCapabilitySource : ILlmCardEffectCapabilitySource
        {
            public bool TryGet(int cardId, out LlmCardEffectCapabilityFacts facts)
            {
                return LlmCardEffectCapabilityCatalog.TryGet(cardId, out facts);
            }
        }
    }
}

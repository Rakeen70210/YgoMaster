using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Absolute game-public history visibility. Never depends on controlled-player seat.
    /// </summary>
    static class LlmPublicHistoryRedactionPolicy
    {
        // Observed live values used by existing public-state fixtures: 8 = known face-up.
        public const int PublicFaceUpValue = 8;
        public const int FacedownFaceValue = 4;

        public const int PosMonsterMax = 6;
        public const int PosSpellTrapMin = 7;
        public const int PosSpellTrapMax = 12;
        public const int PosHand = 13;
        // Live-validated engine positions (P2 decision log + DuelClientUtils.positionDeck=15):
        // Extra Deck = 14, Main Deck = 15. Both remain absolute-public-hidden identities.
        public const int PosExtra = 14;
        public const int PosDeck = 15;
        public const int PosGrave = 16;
        public const int PosBanishedMin = 17;

        public static Dictionary<string, object> SerializeEventForBrokerProjection(LlmPublicDuelEvent evt)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            if (evt == null)
            {
                return data;
            }

            data["event_id"] = (long)evt.EventId;
            data["run_effect_seq"] = (long)evt.RunEffectSeq;
            data["turn"] = evt.Turn;
            data["phase"] = PhaseName(evt.Phase);
            data["actor_player"] = evt.ActorPlayer;
            if (evt.TargetPlayer >= 0)
            {
                data["target_player"] = evt.TargetPlayer;
            }
            if (evt.CardPlayer >= 0)
            {
                data["card_player"] = evt.CardPlayer;
            }
            data["kind"] = KindName(evt.Kind);
            data["evidence"] = EvidenceName(evt.Evidence);

            // Sink redaction: set events never serialize identity even if the event object
            // was incorrectly populated upstream.
            if (!IsAbsolutePublicSetKind(evt.Kind) &&
                evt.CardId.HasValue &&
                evt.CardId.Value > 0)
            {
                data["card_id"] = evt.CardId.Value;
            }
            if (!IsAbsolutePublicSetKind(evt.Kind) && !string.IsNullOrEmpty(evt.CardName))
            {
                data["card_name"] = evt.CardName;
            }
            if (!string.IsNullOrEmpty(evt.SourceZone))
            {
                data["source_zone"] = evt.SourceZone;
            }
            if (!string.IsNullOrEmpty(evt.DestinationZone))
            {
                data["destination_zone"] = evt.DestinationZone;
            }
            if (!string.IsNullOrEmpty(evt.TargetZone))
            {
                data["target_zone"] = evt.TargetZone;
            }
            // Only serialize caused_by when an explicit deterministic correlation set it.
            if (evt.CausedByEventId.HasValue)
            {
                data["caused_by_event_id"] = (long)evt.CausedByEventId.Value;
            }
            if (!string.IsNullOrEmpty(evt.SourceSignature))
            {
                data["source_signature"] = evt.SourceSignature;
            }
            return data;
        }

        public static bool IsAbsolutePublicSetKind(LlmPublicDuelEventKind kind)
        {
            return kind == LlmPublicDuelEventKind.SetMonster ||
                kind == LlmPublicDuelEventKind.SetSpellTrap;
        }

        public static bool IsAnnouncedPublicActionCommand(DuelCommandType command)
        {
            // These actions publicly announce the card when accepted by the engine,
            // regardless of pre-command face (hand facedown / unknown).
            switch (command)
            {
                case DuelCommandType.Summon:
                case DuelCommandType.SummonSp:
                case DuelCommandType.Pendulum:
                case DuelCommandType.Reverse:
                case DuelCommandType.Action:
                    return true;
                default:
                    return false;
            }
        }

        public static bool ShouldExposeCardIdentity(LlmAcceptedCommandCapture capture)
        {
            if (capture == null)
            {
                return false;
            }
            if (IsAbsolutePublicSetCommand(capture.Command))
            {
                return false;
            }
            if (IsHiddenListSelection(capture))
            {
                return false;
            }
            if (capture.Command == DuelCommandType.Decide)
            {
                // Target selection stays fail-closed on facedown/hidden/unknown face.
                if (IsFacedownOrUnknownFace(capture.CardFace) ||
                    IsHiddenZonePosition(capture.Position))
                {
                    return false;
                }
            }
            else if (!IsAnnouncedPublicActionCommand(capture.Command) &&
                IsFacedownOrUnknownFace(capture.CardFace))
            {
                // Non-announcing actions (attack/position) still require public face.
                return false;
            }
            return capture.CardId > 0 || !string.IsNullOrEmpty(capture.CardName);
        }

        public static bool ShouldExposeSourceHandIndex(LlmAcceptedCommandCapture capture)
        {
            // Absolute public history never exposes hand/deck source indexes.
            return false;
        }

        public static bool IsAbsolutePublicSetCommand(DuelCommandType command)
        {
            return command == DuelCommandType.Set || command == DuelCommandType.SetMonst;
        }

        public static bool IsPublicFaceUp(int cardFace)
        {
            return cardFace == PublicFaceUpValue;
        }

        public static bool IsFacedownOrUnknownFace(int cardFace)
        {
            return cardFace != PublicFaceUpValue;
        }

        public static bool IsHiddenListSelection(LlmAcceptedCommandCapture capture)
        {
            if (capture == null)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(capture.Metadata) &&
                capture.Metadata.IndexOf("hidden_list", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return capture.Command == DuelCommandType.Decide && capture.Position == PosHand;
        }

        public static bool IsHiddenZonePosition(int position)
        {
            return position == PosHand ||
                position == PosDeck ||
                position == PosExtra ||
                position >= PosBanishedMin;
        }

        public static bool IsBanishedPosition(int position)
        {
            return position >= PosBanishedMin;
        }

        public static string MapPublicZone(int position)
        {
            if (position < 0)
            {
                return null;
            }
            // Slice 1 provisional mapping aligned with Slice 0 fixtures:
            // 0-4 main monster, 5-12 treated as spell/trap (includes EMZ until live
            // face/locate validation refines EMZ as monster_zone).
            if (position <= 4)
            {
                return "monster_zone";
            }
            if (position <= PosSpellTrapMax)
            {
                return "spell_trap_zone";
            }
            if (position == PosHand)
            {
                return "hand";
            }
            if (position == PosDeck)
            {
                return "deck";
            }
            if (position == PosExtra)
            {
                return "extra_deck";
            }
            if (position == PosGrave)
            {
                return "graveyard";
            }
            if (IsBanishedPosition(position))
            {
                return "banished";
            }
            return "field";
        }

        public static string KindName(LlmPublicDuelEventKind kind)
        {
            switch (kind)
            {
                case LlmPublicDuelEventKind.MovePhase: return "move_phase";
                case LlmPublicDuelEventKind.NormalSummon: return "normal_summon";
                case LlmPublicDuelEventKind.SpecialSummon: return "special_summon";
                case LlmPublicDuelEventKind.PendulumSummon: return "pendulum_summon";
                case LlmPublicDuelEventKind.FlipSummon: return "flip_summon";
                case LlmPublicDuelEventKind.SetMonster: return "set_monster";
                case LlmPublicDuelEventKind.SetSpellTrap: return "set_spell_trap";
                case LlmPublicDuelEventKind.ActivateEffect: return "activate_effect";
                case LlmPublicDuelEventKind.AttackDeclare: return "attack_declare";
                case LlmPublicDuelEventKind.PositionChange: return "position_change";
                case LlmPublicDuelEventKind.PublicTargetSelect: return "select_target";
                case LlmPublicDuelEventKind.GenericAcceptedCommand: return "generic_accepted_command";
                case LlmPublicDuelEventKind.TurnChanged: return "turn_changed";
                case LlmPublicDuelEventKind.PhaseChanged: return "phase_changed";
                case LlmPublicDuelEventKind.LifePointsChanged: return "life_points_changed";
                case LlmPublicDuelEventKind.CardMovedPublic: return "card_moved";
                case LlmPublicDuelEventKind.HandShuffled: return "hand_shuffled";
                case LlmPublicDuelEventKind.DeckShuffled: return "deck_shuffled";
                default: return "unknown";
            }
        }

        public static string EvidenceName(LlmPublicHistoryEvidence evidence)
        {
            return evidence == LlmPublicHistoryEvidence.PublicStateDelta
                ? "public_state_delta"
                : "accepted_command";
        }

        public static string PhaseName(int phase)
        {
            if (System.Enum.IsDefined(typeof(DuelPhase), phase))
            {
                return ((DuelPhase)phase).ToString();
            }
            return phase.ToString();
        }
    }
}

namespace YgoMaster
{
    /// <summary>
    /// PvP-worker accepted-command → absolute-public duel history event factory.
    /// </summary>
    static class LlmPublicActionEventFactory
    {
        public static LlmPublicDuelEventCreationResult TryCreateFromAcceptedCommand(
            LlmAcceptedCommandCapture capture,
            ulong nextEventId)
        {
            if (capture == null)
            {
                return LlmPublicDuelEventCreationResult.NoEvent();
            }

            if (capture.RunEffectSeq != capture.EngineRunEffectSeq)
            {
                return LlmPublicDuelEventCreationResult.NoEvent();
            }

            LlmPublicDuelEventKind kind;
            if (!TryMapCommandKind(capture, out kind))
            {
                // Unsupported / unknown: omit or identity-free generic.
                if (IsSupportedSliceCommand(capture.Command))
                {
                    return LlmPublicDuelEventCreationResult.NoEvent();
                }
                LlmPublicDuelEvent generic = BaseEvent(capture, nextEventId, LlmPublicDuelEventKind.GenericAcceptedCommand);
                ClearIdentity(generic);
                return LlmPublicDuelEventCreationResult.FromEvent(generic);
            }

            LlmPublicDuelEvent publicEvent = BaseEvent(capture, nextEventId, kind);
            ApplyZonesAndIdentity(capture, publicEvent, kind);
            return LlmPublicDuelEventCreationResult.FromEvent(publicEvent);
        }

        public static LlmPublicDuelEventCreationResult TryCreateFromMovePhase(
            ulong runEffectSeq,
            ulong engineRunEffectSeq,
            int actorPlayer,
            int phase,
            int turn,
            ulong nextEventId)
        {
            if (runEffectSeq != engineRunEffectSeq)
            {
                return LlmPublicDuelEventCreationResult.NoEvent();
            }

            LlmPublicDuelEvent publicEvent = new LlmPublicDuelEvent()
            {
                EventId = nextEventId,
                RunEffectSeq = runEffectSeq,
                Turn = turn,
                Phase = phase,
                ActorPlayer = actorPlayer,
                TargetPlayer = -1,
                CardPlayer = -1,
                Kind = LlmPublicDuelEventKind.MovePhase,
                DestinationZone = null,
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
            };
            return LlmPublicDuelEventCreationResult.FromEvent(publicEvent);
        }

        static LlmPublicDuelEvent BaseEvent(
            LlmAcceptedCommandCapture capture,
            ulong nextEventId,
            LlmPublicDuelEventKind kind)
        {
            return new LlmPublicDuelEvent()
            {
                EventId = nextEventId,
                RunEffectSeq = capture.RunEffectSeq,
                Turn = capture.Turn,
                Phase = capture.Phase,
                ActorPlayer = capture.ActorPlayer,
                TargetPlayer = -1,
                CardPlayer = -1,
                Kind = kind,
                Evidence = LlmPublicHistoryEvidence.AcceptedCommand,
            };
        }

        static bool IsSupportedSliceCommand(DuelCommandType command)
        {
            switch (command)
            {
                case DuelCommandType.Summon:
                case DuelCommandType.SummonSp:
                case DuelCommandType.Pendulum:
                case DuelCommandType.Reverse:
                case DuelCommandType.Set:
                case DuelCommandType.SetMonst:
                case DuelCommandType.Action:
                case DuelCommandType.Attack:
                case DuelCommandType.TurnAtk:
                case DuelCommandType.TurnDef:
                case DuelCommandType.Decide:
                    return true;
                default:
                    return false;
            }
        }

        static bool TryMapCommandKind(LlmAcceptedCommandCapture capture, out LlmPublicDuelEventKind kind)
        {
            kind = LlmPublicDuelEventKind.Unknown;
            switch (capture.Command)
            {
                case DuelCommandType.Summon:
                    kind = LlmPublicDuelEventKind.NormalSummon;
                    return true;
                case DuelCommandType.SummonSp:
                    kind = LlmPublicDuelEventKind.SpecialSummon;
                    return true;
                case DuelCommandType.Pendulum:
                    kind = LlmPublicDuelEventKind.PendulumSummon;
                    return true;
                case DuelCommandType.Reverse:
                    kind = LlmPublicDuelEventKind.FlipSummon;
                    return true;
                case DuelCommandType.SetMonst:
                    kind = LlmPublicDuelEventKind.SetMonster;
                    return true;
                case DuelCommandType.Set:
                    kind = LlmPublicDuelEventKind.SetSpellTrap;
                    return true;
                case DuelCommandType.Action:
                    kind = LlmPublicDuelEventKind.ActivateEffect;
                    return true;
                case DuelCommandType.Attack:
                    kind = LlmPublicDuelEventKind.AttackDeclare;
                    return true;
                case DuelCommandType.TurnAtk:
                case DuelCommandType.TurnDef:
                    kind = LlmPublicDuelEventKind.PositionChange;
                    return true;
                case DuelCommandType.Decide:
                    kind = LlmPublicDuelEventKind.PublicTargetSelect;
                    return true;
                default:
                    return false;
            }
        }

        static void ApplyZonesAndIdentity(
            LlmAcceptedCommandCapture capture,
            LlmPublicDuelEvent publicEvent,
            LlmPublicDuelEventKind kind)
        {
            switch (kind)
            {
                case LlmPublicDuelEventKind.NormalSummon:
                case LlmPublicDuelEventKind.SpecialSummon:
                case LlmPublicDuelEventKind.PendulumSummon:
                case LlmPublicDuelEventKind.FlipSummon:
                    if (capture.SourceFromHand || capture.Position == LlmPublicHistoryRedactionPolicy.PosHand)
                    {
                        publicEvent.SourceZone = "hand";
                    }
                    else
                    {
                        publicEvent.SourceZone = LlmPublicHistoryRedactionPolicy.MapPublicZone(capture.Position);
                    }
                    publicEvent.DestinationZone = "monster_zone";
                    ApplyIdentityIfPublic(capture, publicEvent);
                    break;

                case LlmPublicDuelEventKind.SetMonster:
                    publicEvent.DestinationZone = "monster_zone";
                    ClearIdentity(publicEvent);
                    break;

                case LlmPublicDuelEventKind.SetSpellTrap:
                    publicEvent.DestinationZone = "spell_trap_zone";
                    ClearIdentity(publicEvent);
                    break;

                case LlmPublicDuelEventKind.ActivateEffect:
                    publicEvent.SourceZone = LlmPublicHistoryRedactionPolicy.MapPublicZone(capture.Position);
                    ApplyIdentityIfPublic(capture, publicEvent);
                    break;

                case LlmPublicDuelEventKind.AttackDeclare:
                    publicEvent.SourceZone = LlmPublicHistoryRedactionPolicy.MapPublicZone(capture.Position);
                    ApplyIdentityIfPublic(capture, publicEvent);
                    break;

                case LlmPublicDuelEventKind.PositionChange:
                    publicEvent.SourceZone = LlmPublicHistoryRedactionPolicy.MapPublicZone(capture.Position);
                    publicEvent.DestinationZone = publicEvent.SourceZone;
                    ApplyIdentityIfPublic(capture, publicEvent);
                    break;

                case LlmPublicDuelEventKind.PublicTargetSelect:
                    publicEvent.TargetZone = LlmPublicHistoryRedactionPolicy.MapPublicZone(capture.Position);
                    if (LlmPublicHistoryRedactionPolicy.IsHiddenListSelection(capture) ||
                        LlmPublicHistoryRedactionPolicy.IsFacedownOrUnknownFace(capture.CardFace) ||
                        LlmPublicHistoryRedactionPolicy.IsHiddenZonePosition(capture.Position))
                    {
                        ClearIdentity(publicEvent);
                    }
                    else
                    {
                        ApplyIdentityIfPublic(capture, publicEvent);
                    }
                    break;

                default:
                    ClearIdentity(publicEvent);
                    break;
            }
        }

        static void ApplyIdentityIfPublic(LlmAcceptedCommandCapture capture, LlmPublicDuelEvent publicEvent)
        {
            if (!LlmPublicHistoryRedactionPolicy.ShouldExposeCardIdentity(capture))
            {
                ClearIdentity(publicEvent);
                return;
            }

            if (capture.CardId > 0)
            {
                publicEvent.CardId = capture.CardId;
            }
            if (!string.IsNullOrEmpty(capture.CardName))
            {
                publicEvent.CardName = capture.CardName;
            }
        }

        static void ClearIdentity(LlmPublicDuelEvent publicEvent)
        {
            publicEvent.CardId = null;
            publicEvent.CardName = null;
        }
    }
}

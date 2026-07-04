using System;

namespace YgoMaster
{
    static class LegalActionExtractor
    {
        public const int DefaultPosNum = 18;
        const int PlayerCount = 2;
        const int PosField = 12;
        const int PosHand = 13;
        const int PosGrave = 16;

        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer)
        {
            return Extract(query, runEffectSeq, viewType, actingPlayer, DefaultPosNum, null);
        }

        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer,
            ILlmCardCatalog cardCatalog)
        {
            return Extract(query, runEffectSeq, viewType, actingPlayer, DefaultPosNum, cardCatalog);
        }

        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer,
            int posNum)
        {
            return Extract(query, runEffectSeq, viewType, actingPlayer, posNum, null);
        }

        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer,
            int posNum,
            ILlmCardCatalog cardCatalog)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = runEffectSeq,
                ViewType = viewType,
                ActingPlayer = actingPlayer,
                ControlledPlayer = actingPlayer,
                Turn = query.GetTurnNum(),
                TurnPlayer = query.GetTurnPlayer(),
                CurrentPhase = query.GetCurrentPhase(),
                CurrentStep = query.GetCurrentStep(),
            };

            AddPublicState(snapshot, query, posNum, cardCatalog);

            switch (viewType)
            {
                case DuelViewType.WaitInput:
                    if (AddSummonPlacementActions(snapshot, query, actingPlayer, cardCatalog))
                    {
                        break;
                    }
                    AddPhaseActions(snapshot, query.GetMovablePhase());
                    AddCommandActions(snapshot, query, actingPlayer, posNum, cardCatalog);
                    break;
                case DuelViewType.RunDialog:
                    AddDialogActions(snapshot, query);
                    break;
                case DuelViewType.RunList:
                    AddListActions(snapshot, query);
                    break;
            }
            return snapshot;
        }

        static bool AddSummonPlacementActions(
            DecisionSnapshot snapshot,
            ILegalActionQuery query,
            int actingPlayer,
            ILlmCardCatalog cardCatalog)
        {
            int cardUniqueId = query.GetSummoningMonsterUniqueId();
            int positionMask = query.GetSummonPositionMask();
            if (cardUniqueId <= 0 || positionMask == 0)
            {
                return false;
            }

            int cardId = query.GetCardIdByUniqueId(cardUniqueId);
            LlmCardMetadata card = GetCard(cardCatalog, cardId);
            for (int position = 0; position <= PosField; position++)
            {
                int positionBit = 1 << position;
                if ((positionMask & positionBit) == 0)
                {
                    continue;
                }

                snapshot.LegalActions.Add(new LegalAction()
                {
                    ActionId = snapshot.LegalActions.Count,
                    Kind = LegalActionKind.Command,
                    Player = actingPlayer,
                    Position = position,
                    Index = 0,
                    Command = DuelCommandType.Decide,
                    CardUniqueId = cardUniqueId,
                    CardId = cardId,
                    Card = card,
                });
            }

            return snapshot.LegalActions.Count > 0;
        }

        static void AddPublicState(
            DecisionSnapshot snapshot,
            ILegalActionQuery query,
            int posNum,
            ILlmCardCatalog cardCatalog)
        {
            for (int player = 0; player < PlayerCount; player++)
            {
                PublicPlayerState playerState = new PublicPlayerState()
                {
                    Player = player,
                    LifePoints = query.GetLifePoints(player),
                };

                for (int position = 0; position <= posNum; position++)
                {
                    int cardNum = query.GetCardNum(player, position);
                    playerState.Positions.Add(new PublicPositionState()
                    {
                        Position = position,
                        Count = cardNum,
                    });
                    AddKnownCards(playerState, query, player, position, cardNum, snapshot.ControlledPlayer, cardCatalog);
                }

                snapshot.PublicState.Players.Add(playerState);
            }
        }

        static void AddKnownCards(
            PublicPlayerState playerState,
            ILegalActionQuery query,
            int player,
            int position,
            int cardNum,
            int controlledPlayer,
            ILlmCardCatalog cardCatalog)
        {
            for (int index = 0; index <= cardNum; index++)
            {
                int cardUniqueId = query.GetCardUniqueId(player, position, index);
                if (cardUniqueId <= 0 ||
                    !CanExposeCardIdentity(query, player, position, index, controlledPlayer))
                {
                    continue;
                }

                int cardId = query.GetCardIdByUniqueId(cardUniqueId);
                if (cardId <= 0)
                {
                    continue;
                }

                playerState.KnownCards.Add(new PublicKnownCard()
                {
                    Player = player,
                    Position = position,
                    Index = index,
                    CardUniqueId = cardUniqueId,
                    CardId = cardId,
                    Face = query.GetCardFace(player, position, index),
                    Card = GetCard(cardCatalog, cardId),
                });
            }
        }

        static bool CanExposeCardIdentity(
            ILegalActionQuery query,
            int player,
            int position,
            int index,
            int controlledPlayer)
        {
            if (position == PosGrave)
            {
                return true;
            }
            if (position == PosHand)
            {
                return player == controlledPlayer || query.GetHandCardOpen(player, index) != 0;
            }
            return false;
        }

        static void AddPhaseActions(DecisionSnapshot snapshot, uint phaseMask)
        {
            foreach (DuelPhase phase in Enum.GetValues(typeof(DuelPhase)))
            {
                int phaseValue = (int)phase;
                if (phaseValue < 0 || phaseValue >= 32)
                {
                    continue;
                }
                if (phase == DuelPhase.Null)
                {
                    continue;
                }
                if ((phaseMask & (1u << phaseValue)) == 0)
                {
                    continue;
                }

                snapshot.LegalActions.Add(new LegalAction()
                {
                    ActionId = snapshot.LegalActions.Count,
                    Kind = LegalActionKind.MovePhase,
                    Phase = phase,
                });
            }
        }

        static void AddCommandActions(
            DecisionSnapshot snapshot,
            ILegalActionQuery query,
            int actingPlayer,
            int posNum,
            ILlmCardCatalog cardCatalog)
        {
            for (int position = 0; position <= posNum; position++)
            {
                int cardNum = query.GetCardNum(actingPlayer, position);
                for (int index = 0; index <= cardNum; index++)
                {
                    uint commandMask = query.GetCommandMask(actingPlayer, position, index);
                    if (commandMask == 0)
                    {
                        continue;
                    }

                    int cardUniqueId = query.GetCardUniqueId(actingPlayer, position, index);
                    int cardId = cardUniqueId <= 0 ? 0 : query.GetCardIdByUniqueId(cardUniqueId);
                    AddCommandActions(
                        snapshot,
                        actingPlayer,
                        position,
                        index,
                        cardUniqueId,
                        cardId,
                        GetCard(cardCatalog, cardId),
                        commandMask);
                }
            }
        }

        static void AddCommandActions(
            DecisionSnapshot snapshot,
            int player,
            int position,
            int index,
            int cardUniqueId,
            int cardId,
            LlmCardMetadata card,
            uint commandMask)
        {
            for (int commandId = 0; commandId < (int)DuelCommandType.COUNT; commandId++)
            {
                if ((commandMask & (1u << commandId)) == 0)
                {
                    continue;
                }
                DuelCommandType command = (DuelCommandType)commandId;
                if (!ShouldExposeCommand(command, commandMask))
                {
                    continue;
                }

                snapshot.LegalActions.Add(new LegalAction()
                {
                    ActionId = snapshot.LegalActions.Count,
                    Kind = LegalActionKind.Command,
                    Player = player,
                    Position = position,
                    Index = index,
                    Command = command,
                    CardUniqueId = cardUniqueId,
                    CardId = cardId,
                    Card = card,
                });
            }
        }

        static bool ShouldExposeCommand(DuelCommandType command, uint commandMask)
        {
            if (command == DuelCommandType.Look ||
                command == DuelCommandType.Surrender ||
                command == DuelCommandType.Draw)
            {
                return false;
            }
            if (command == DuelCommandType.Decide && CountBrokerVisibleCommands(commandMask) <= 1)
            {
                return false;
            }
            return true;
        }

        static int CountBrokerVisibleCommands(uint commandMask)
        {
            int count = 0;
            for (int commandId = 0; commandId < (int)DuelCommandType.COUNT; commandId++)
            {
                if ((commandMask & (1u << commandId)) == 0)
                {
                    continue;
                }
                DuelCommandType command = (DuelCommandType)commandId;
                if (command == DuelCommandType.Look ||
                    command == DuelCommandType.Surrender ||
                    command == DuelCommandType.Draw)
                {
                    continue;
                }
                count++;
            }
            return count;
        }

        static LlmCardMetadata GetCard(ILlmCardCatalog cardCatalog, int cardId)
        {
            if (cardCatalog == null || cardId <= 0)
            {
                return null;
            }
            try
            {
                return cardCatalog.GetCard(cardId);
            }
            catch
            {
                return null;
            }
        }

        static void AddDialogActions(DecisionSnapshot snapshot, ILegalActionQuery query)
        {
            int itemNum = query.GetDialogSelectItemNum();
            for (int index = 0; index < itemNum; index++)
            {
                if (query.GetDialogSelectItemEnable(index) == 0)
                {
                    continue;
                }

                snapshot.LegalActions.Add(new LegalAction()
                {
                    ActionId = snapshot.LegalActions.Count,
                    Kind = LegalActionKind.DialogResult,
                    DialogResult = index,
                    DialogTextId = query.GetDialogSelectItemTextId(index),
                });
            }
        }

        static void AddListActions(DecisionSnapshot snapshot, ILegalActionQuery query)
        {
            int itemMax = query.GetListItemMax();
            int selectMin = query.GetListSelectMin();
            int selectMax = query.GetListSelectMax();
            int isMultiMode = query.GetListIsMultiMode();
            if (isMultiMode != 0 || selectMin != 1 || selectMax != 1)
            {
                return;
            }

            for (int index = 0; index < itemMax; index++)
            {
                snapshot.LegalActions.Add(new LegalAction()
                {
                    ActionId = snapshot.LegalActions.Count,
                    Kind = LegalActionKind.ListIndex,
                    Index = index,
                    ListItemAttribute = query.GetListItemAttribute(index),
                    ListItemFrom = query.GetListItemFrom(index),
                    ListItemId = query.GetListItemId(index),
                    ListItemMsg = query.GetListItemMsg(index),
                    ListItemTargetUniqueId = query.GetListItemTargetUniqueId(index),
                    ListItemUniqueId = query.GetListItemUniqueId(index),
                    ListSelectMin = selectMin,
                    ListSelectMax = selectMax,
                    ListIsMultiMode = isMultiMode,
                });
            }
        }
    }
}

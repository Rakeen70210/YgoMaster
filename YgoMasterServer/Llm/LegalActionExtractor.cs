using System;
using System.Collections.Generic;

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
            return Extract(
                query,
                runEffectSeq,
                viewType,
                actingPlayer,
                DefaultPosNum,
                null,
                null,
                LlmPublicVisibilityMode.RuntimeDllField);
        }

        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer,
            ILlmCardCatalog cardCatalog)
        {
            return Extract(
                query,
                runEffectSeq,
                viewType,
                actingPlayer,
                DefaultPosNum,
                cardCatalog,
                null,
                LlmPublicVisibilityMode.RuntimeDllField);
        }

        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer,
            AttackTargetContext attackTargetContext)
        {
            return Extract(
                query,
                runEffectSeq,
                viewType,
                actingPlayer,
                DefaultPosNum,
                null,
                attackTargetContext,
                LlmPublicVisibilityMode.RuntimeDllField);
        }

        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer,
            ILlmCardCatalog cardCatalog,
            AttackTargetContext attackTargetContext)
        {
            return Extract(
                query,
                runEffectSeq,
                viewType,
                actingPlayer,
                DefaultPosNum,
                cardCatalog,
                attackTargetContext,
                LlmPublicVisibilityMode.RuntimeDllField);
        }

        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer,
            int posNum)
        {
            return Extract(
                query,
                runEffectSeq,
                viewType,
                actingPlayer,
                posNum,
                null,
                null,
                LlmPublicVisibilityMode.RuntimeDllField);
        }

        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer,
            int posNum,
            ILlmCardCatalog cardCatalog)
        {
            return Extract(
                query,
                runEffectSeq,
                viewType,
                actingPlayer,
                posNum,
                cardCatalog,
                null,
                LlmPublicVisibilityMode.RuntimeDllField);
        }

        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer,
            int posNum,
            ILlmCardCatalog cardCatalog,
            AttackTargetContext attackTargetContext)
        {
            return Extract(
                query,
                runEffectSeq,
                viewType,
                actingPlayer,
                posNum,
                cardCatalog,
                attackTargetContext,
                LlmPublicVisibilityMode.RuntimeDllField);
        }

        /// <summary>
        /// Fixture/runtime face-domain overload. Production paths use RuntimeDllField via
        /// the overloads above; offline fixtures may pass FixtureValidated (8/4).
        /// </summary>
        public static DecisionSnapshot Extract(
            ILegalActionQuery query,
            ulong runEffectSeq,
            DuelViewType viewType,
            int actingPlayer,
            int posNum,
            ILlmCardCatalog cardCatalog,
            AttackTargetContext attackTargetContext,
            LlmPublicVisibilityMode selfResourceFaceDomain)
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
                    if (snapshot.LegalActions.Count == 0)
                    {
                        bool addedAttackTargets = AddAttackTargetActions(
                            snapshot, query, attackTargetContext, posNum);
                        if (!addedAttackTargets)
                        {
                            AddEffectTargetActions(
                                snapshot, query, actingPlayer, posNum, cardCatalog);
                        }
                    }
                    break;
                case DuelViewType.RunDialog:
                    AddDialogActions(snapshot, query);
                    break;
                case DuelViewType.RunList:
                    AddListActions(snapshot, query);
                    break;
            }
            ClassifySnapshot(snapshot);

            // Central attach point for all initial/retry/recovery extraction paths.
            snapshot.SelfResources = LlmSelfResourceProjection.Project(
                query,
                snapshot.ControlledPlayer,
                cardCatalog,
                selfResourceFaceDomain);
            return snapshot;
        }

        public static void ClassifySnapshot(DecisionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            int strategicActions = 0;
            int mechanicalActions = 0;
            foreach (LegalAction action in snapshot.LegalActions)
            {
                ApplyActionSemantics(action);
                if (action.IsMechanical)
                {
                    mechanicalActions++;
                }
                else
                {
                    strategicActions++;
                }
            }

            snapshot.StrategicActionCount = strategicActions;
            snapshot.MechanicalActionCount = mechanicalActions;
            snapshot.IsStrategicWindow = strategicActions > 0;
            if (snapshot.LegalActions.Count == 0)
            {
                snapshot.StrategicWindowReason = "no_actions";
            }
            else if (snapshot.IsStrategicWindow)
            {
                snapshot.StrategicWindowReason = "strategic_choices";
            }
            else
            {
                snapshot.StrategicWindowReason = "mechanical_only";
            }
        }

        public static bool TryExtractAutomaticAction(
            ILegalActionQuery query,
            DuelViewType viewType,
            int actingPlayer,
            out LegalAction action)
        {
            return TryExtractAutomaticAction(
                query, viewType, actingPlayer, DefaultPosNum, false, null, out action);
        }

        public static bool TryExtractAutomaticAction(
            ILegalActionQuery query,
            DuelViewType viewType,
            int actingPlayer,
            AttackTargetContext attackTargetContext,
            out LegalAction action)
        {
            return TryExtractAutomaticAction(
                query, viewType, actingPlayer, DefaultPosNum, false, attackTargetContext, out action);
        }

        public static bool TryExtractAutomaticAction(
            ILegalActionQuery query,
            DuelViewType viewType,
            int actingPlayer,
            bool allowBattlePhaseMove,
            out LegalAction action)
        {
            return TryExtractAutomaticAction(
                query, viewType, actingPlayer, DefaultPosNum, allowBattlePhaseMove, null, out action);
        }

        public static bool TryExtractAutomaticAction(
            ILegalActionQuery query,
            DuelViewType viewType,
            int actingPlayer,
            bool allowBattlePhaseMove,
            AttackTargetContext attackTargetContext,
            out LegalAction action)
        {
            return TryExtractAutomaticAction(
                query,
                viewType,
                actingPlayer,
                DefaultPosNum,
                allowBattlePhaseMove,
                attackTargetContext,
                out action);
        }

        public static bool TryExtractAutomaticAction(
            ILegalActionQuery query,
            DuelViewType viewType,
            int actingPlayer,
            int posNum,
            out LegalAction action)
        {
            return TryExtractAutomaticAction(
                query, viewType, actingPlayer, posNum, false, null, out action);
        }

        public static bool TryExtractAutomaticAction(
            ILegalActionQuery query,
            DuelViewType viewType,
            int actingPlayer,
            int posNum,
            bool allowBattlePhaseMove,
            out LegalAction action)
        {
            return TryExtractAutomaticAction(
                query, viewType, actingPlayer, posNum, allowBattlePhaseMove, null, out action);
        }

        public static bool TryExtractAutomaticAction(
            ILegalActionQuery query,
            DuelViewType viewType,
            int actingPlayer,
            int posNum,
            bool allowBattlePhaseMove,
            AttackTargetContext attackTargetContext,
            out LegalAction action)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            action = null;
            if (viewType == DuelViewType.RunDialog)
            {
                return false;
            }
            if (viewType != DuelViewType.WaitInput)
            {
                return false;
            }

            bool found = TryExtractAutomaticCommand(
                    query,
                    actingPlayer,
                    posNum,
                    DuelCommandType.Draw,
                    out action) ||
                TryExtractAutomaticCommand(
                    query,
                    actingPlayer,
                    posNum,
                    DuelCommandType.Decide,
                    out action) ||
                TryExtractAutomaticEffectTarget(query, actingPlayer, posNum, out action) ||
                TryExtractAutomaticAttackTarget(
                    query,
                    attackTargetContext,
                    posNum,
                    out action) ||
                (allowBattlePhaseMove && TryExtractAutomaticBattlePhaseAction(query, out action));
            if (found)
            {
                ApplyActionSemantics(action);
            }
            return found;
        }

        public static bool IsDialogWithoutChoice(ILegalActionQuery query)
        {
            return IsDialogWithoutChoice(query, -1);
        }

        public static bool IsDialogWithoutChoice(ILegalActionQuery query, int dialogType)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }
            if (query.GetDialogCanYesNoSkip() != 0 ||
                IsBinaryChoiceDialog(dialogType) ||
                dialogType == (int)DuelDialogType.SelStand)
            {
                return false;
            }

            int itemNum = query.GetDialogSelectItemNum();
            for (int index = 0; index < itemNum; index++)
            {
                if (query.GetDialogSelectItemEnable(index) != 0)
                {
                    return false;
                }
            }
            return true;
        }

        public static void ApplyViewContext(
            DecisionSnapshot snapshot,
            int viewParam1,
            int viewParam2,
            int viewParam3)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.ViewParam1 = viewParam1;
            snapshot.ViewParam2 = viewParam2;
            snapshot.ViewParam3 = viewParam3;
            ApplyDialogTypeFallback(snapshot, viewParam1);
        }

        public static void ApplyViewContext(
            DecisionSnapshot snapshot,
            ILegalActionQuery query,
            int viewParam1,
            int viewParam2,
            int viewParam3)
        {
            ApplyViewContext(
                snapshot, query, viewParam1, viewParam2, viewParam3, null);
        }

        public static void ApplyViewContext(
            DecisionSnapshot snapshot,
            ILegalActionQuery query,
            int viewParam1,
            int viewParam2,
            int viewParam3,
            ILlmCardCatalog cardCatalog)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            ApplyViewContext(snapshot, viewParam1, viewParam2, viewParam3);
            ApplyWaitInputMenuFallback(
                snapshot, query, viewParam1, viewParam3, cardCatalog);
        }

        static void ApplyDialogTypeFallback(DecisionSnapshot snapshot, int dialogType)
        {
            if (snapshot == null || snapshot.ViewType != DuelViewType.RunDialog ||
                snapshot.LegalActions.Count != 0 || !IsBinaryChoiceDialog(dialogType))
            {
                return;
            }

            AddYesNoDialogAction(snapshot, 1);
            AddYesNoDialogAction(snapshot, 0);
            ClassifySnapshot(snapshot);
        }

        static bool IsBinaryChoiceDialog(int dialogType)
        {
            return dialogType == (int)DuelDialogType.Confirm ||
                dialogType == (int)DuelDialogType.YesNo;
        }

        static void ApplyWaitInputMenuFallback(
            DecisionSnapshot snapshot,
            ILegalActionQuery query,
            int menuType,
            int menuParamType,
            ILlmCardCatalog cardCatalog)
        {
            if (snapshot == null || snapshot.ViewType != DuelViewType.WaitInput)
            {
                return;
            }

            if (menuType == (int)DuelMenuActType.LockOn)
            {
                snapshot.LegalActions.Clear();
                foreach (LegalAction target in CollectEffectTargetActions(
                    query, DefaultPosNum, cardCatalog, -1))
                {
                    target.ActionId = snapshot.LegalActions.Count;
                    snapshot.LegalActions.Add(target);
                }
                ClassifySnapshot(snapshot);
                return;
            }

            if (menuType != (int)DuelMenuActType.CheckChain ||
                !CanCancelMenuSelection(menuParamType))
            {
                return;
            }

            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action.Kind == LegalActionKind.Cancel)
                {
                    return;
                }
            }

            // Live hang (WaitInput CheckChain + TrueCancel/OnlyCancel/… with zero
            // extractable activations): native RunDefault does not advance. Always
            // expose CancelCommand2(false) decline. When the window is otherwise
            // empty, mark the sole decline mechanical so the planner auto-commits
            // without broker and without accepting optional activations.
            bool emptyResponseWindow = snapshot.LegalActions.Count == 0;
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = snapshot.LegalActions.Count,
                Kind = LegalActionKind.Cancel,
                CancelDecide = false,
            });
            ClassifySnapshot(snapshot);
            if (emptyResponseWindow &&
                snapshot.LegalActions.Count == 1 &&
                snapshot.LegalActions[0].Kind == LegalActionKind.Cancel)
            {
                LegalAction decline = snapshot.LegalActions[0];
                decline.IsMechanical = true;
                decline.ActionLabel = "Decline empty response window";
                decline.ActionGroup = "response";
                decline.StrategicRole = "empty_response_window";
                decline.TargetScope = "empty_check_chain_decline";
                decline.ConsequenceHint = "advances_empty_optional_response";
                decline.RequiresTarget = false;
                snapshot.StrategicActionCount = 0;
                snapshot.MechanicalActionCount = 1;
                snapshot.IsStrategicWindow = false;
                snapshot.StrategicWindowReason = "mechanical_only";
            }
        }

        static bool CanCancelMenuSelection(int menuParamType)
        {
            return menuParamType == (int)DuelMenuParamType.Cancel ||
                menuParamType == (int)DuelMenuParamType.TrueCancel ||
                menuParamType == (int)DuelMenuParamType.OnlyCancel ||
                menuParamType == (int)DuelMenuParamType.DecideCancel;
        }

        static bool TryExtractAutomaticCommand(
            ILegalActionQuery query,
            int player,
            int posNum,
            DuelCommandType command,
            out LegalAction action)
        {
            action = null;
            int commandId = (int)command;
            uint commandBit = 1u << commandId;

            for (int position = 0; position <= posNum; position++)
            {
                int cardNum = query.GetCardNum(player, position);
                for (int index = 0; index <= cardNum; index++)
                {
                    uint commandMask = query.GetCommandMask(player, position, index);
                    if ((commandMask & commandBit) == 0)
                    {
                        continue;
                    }

                    int cardUniqueId = query.GetCardUniqueId(player, position, index);
                    action = new LegalAction()
                    {
                        ActionId = 0,
                        Kind = LegalActionKind.Command,
                        Player = player,
                        Position = position,
                        Index = index,
                        Command = command,
                        CardUniqueId = cardUniqueId,
                        CardId = cardUniqueId <= 0 ? 0 : query.GetCardIdByUniqueId(cardUniqueId),
                    };
                    return true;
                }
            }

            return false;
        }

        static bool TryExtractAutomaticEffectTarget(
            ILegalActionQuery query,
            int actingPlayer,
            int posNum,
            out LegalAction action)
        {
            action = null;
            List<LegalAction> targets = CollectEffectTargetActions(
                query, posNum, null, actingPlayer == 0 ? 1 : 0);
            if (targets.Count != 1)
            {
                return false;
            }

            action = targets[0];
            return true;
        }

        static bool TryExtractAutomaticBattlePhaseAction(
            ILegalActionQuery query,
            out LegalAction action)
        {
            action = null;
            if (query.GetCurrentPhase() != (int)DuelPhase.Battle)
            {
                return false;
            }

            action = new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Main2,
            };
            return true;
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

        static bool AddAttackTargetActions(
            DecisionSnapshot snapshot,
            ILegalActionQuery query,
            AttackTargetContext attackTargetContext,
            int posNum)
        {
            LegalAction[] actions = CollectAttackTargetActions(query, attackTargetContext, posNum);
            if (actions.Length <= 1)
            {
                return false;
            }

            for (int i = 0; i < actions.Length; i++)
            {
                actions[i].ActionId = snapshot.LegalActions.Count;
                snapshot.LegalActions.Add(actions[i]);
            }
            return true;
        }

        static void AddEffectTargetActions(
            DecisionSnapshot snapshot,
            ILegalActionQuery query,
            int actingPlayer,
            int posNum,
            ILlmCardCatalog cardCatalog)
        {
            List<LegalAction> targets = CollectEffectTargetActions(
                query, posNum, cardCatalog, actingPlayer == 0 ? 1 : 0);
            foreach (LegalAction target in targets)
            {
                target.ActionId = snapshot.LegalActions.Count;
                snapshot.LegalActions.Add(target);
            }
        }

        static List<LegalAction> CollectEffectTargetActions(
            ILegalActionQuery query,
            int posNum,
            ILlmCardCatalog cardCatalog,
            int targetPlayerFilter)
        {
            List<LegalAction> targets = new List<LegalAction>();
            uint decideBit = 1u << (int)DuelCommandType.Decide;
            for (int targetPlayer = 0; targetPlayer < PlayerCount; targetPlayer++)
            {
                if (targetPlayerFilter >= 0 && targetPlayer != targetPlayerFilter)
                {
                    continue;
                }
                for (int position = 0; position <= posNum; position++)
                {
                    int cardNum = query.GetCardNum(targetPlayer, position);
                    for (int index = 0; index <= cardNum; index++)
                    {
                        uint commandMask = query.GetCommandMask(targetPlayer, position, index);
                        if ((commandMask & decideBit) == 0)
                        {
                            continue;
                        }

                        int cardUniqueId = query.GetCardUniqueId(targetPlayer, position, index);
                        int cardId = cardUniqueId <= 0 ? 0 : query.GetCardIdByUniqueId(cardUniqueId);
                        targets.Add(new LegalAction()
                        {
                            ActionId = targets.Count,
                            Kind = LegalActionKind.Command,
                            Player = targetPlayer,
                            Position = position,
                            Index = index,
                            Command = DuelCommandType.Decide,
                            CardUniqueId = cardUniqueId,
                            CardId = cardId,
                            Card = GetCard(cardCatalog, cardId),
                            IsEffectTargetSelection = true,
                        });
                    }
                }
            }
            return targets;
        }

        static bool TryExtractAutomaticAttackTarget(
            ILegalActionQuery query,
            AttackTargetContext attackTargetContext,
            int posNum,
            out LegalAction action)
        {
            action = null;
            LegalAction[] actions = CollectAttackTargetActions(query, attackTargetContext, posNum);
            if (actions.Length != 1)
            {
                return false;
            }

            action = actions[0];
            action.ActionId = 0;
            return true;
        }

        static LegalAction[] CollectAttackTargetActions(
            ILegalActionQuery query,
            AttackTargetContext attackTargetContext,
            int posNum)
        {
            if (attackTargetContext == null ||
                query.GetCurrentPhase() != (int)DuelPhase.Battle)
            {
                return new LegalAction[0];
            }

            int attackingPlayer = attackTargetContext.AttackingPlayer;
            int attackerLocate = attackTargetContext.AttackerPosition;
            int targetPlayer = attackingPlayer == 0 ? 1 : 0;
            System.Collections.Generic.List<LegalAction> actions =
                new System.Collections.Generic.List<LegalAction>();
            int maxLocate = Math.Min(posNum, PosField);
            if (attackerLocate < 0 || attackerLocate > maxLocate)
            {
                return new LegalAction[0];
            }

            uint targetMask = unchecked((uint)query.GetAttackTargetMask(attackingPlayer, attackerLocate));
            if (targetMask == 0)
            {
                return new LegalAction[0];
            }

            for (int targetLocate = 0; targetLocate <= maxLocate; targetLocate++)
            {
                if ((targetMask & (1u << targetLocate)) == 0)
                {
                    continue;
                }

                int cardNum = query.GetCardNum(targetPlayer, targetLocate);
                int cardUniqueId = query.GetCardUniqueId(targetPlayer, targetLocate, 0);
                if (cardNum <= 0 && cardUniqueId <= 0)
                {
                    continue;
                }

                actions.Add(new LegalAction()
                {
                    ActionId = actions.Count,
                    Kind = LegalActionKind.Command,
                    Player = targetPlayer,
                    Position = targetLocate,
                    Index = 0,
                    Command = DuelCommandType.Decide,
                });
            }
            return actions.ToArray();
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

                int face = query.GetCardFace(player, position, index);
                // Open or own hand is identity-known to the public_state consumer for that
                // controlled seat; normalize a default engine face of 0 to face-up so public
                // serializers can distinguish revealed hand from face-down hidden plantings.
                if (position == PosHand
                    && (player == controlledPlayer || query.GetHandCardOpen(player, index) != 0)
                    && face != 1
                    && face != 8)
                {
                    face = 1;
                }

                playerState.KnownCards.Add(new PublicKnownCard()
                {
                    Player = player,
                    Position = position,
                    Index = index,
                    CardUniqueId = cardUniqueId,
                    CardId = cardId,
                    Face = face,
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

        static void ApplyActionSemantics(LegalAction action)
        {
            if (action == null)
            {
                return;
            }

            action.IsMechanical = false;
            action.RequiresTarget = false;
            action.TargetScope = null;
            action.ConsequenceHint = null;

            if (action.Kind == LegalActionKind.Cancel)
            {
                if (action.CancelDecide)
                {
                    action.ActionLabel = "Pass empty dialog";
                    action.ActionGroup = "pass";
                    action.IsMechanical = true;
                    action.StrategicRole = "no_op_pass";
                    action.TargetScope = "empty_dialog_pass";
                    action.ConsequenceHint = "advances_prompt_without_selection";
                }
                else
                {
                    action.ActionLabel = "Decline response";
                    action.ActionGroup = "response";
                    action.IsMechanical = false;
                    action.StrategicRole = "response_timing";
                    action.TargetScope = "check_chain_decline";
                    action.ConsequenceHint = "holds_optional_response_for_later";
                }
                return;
            }

            if (action.Kind == LegalActionKind.MovePhase)
            {
                action.ActionLabel = PhaseLabel(action.Phase);
                action.ActionGroup = "phase";
                action.StrategicRole = "phase_timing";
                action.ConsequenceHint = PhaseConsequenceHint(action.Phase);
                return;
            }

            if (action.Kind == LegalActionKind.Command)
            {
                ApplyCommandSemantics(action);
                return;
            }

            if (action.Kind == LegalActionKind.DialogResult)
            {
                if (action.DialogIsYesNoPrompt)
                {
                    bool activate = action.DialogResult != 0;
                    action.ActionLabel = activate ?
                        "Activate optional effect" :
                        "Decline optional effect";
                    action.ActionGroup = "optional_effect";
                    action.IsMechanical = false;
                    action.StrategicRole = "effect_activation";
                    action.RequiresTarget = false;
                    action.TargetScope = "yes_no_effect_prompt";
                    action.ConsequenceHint = activate ?
                        "may_open_followup_selection" :
                        "skips_optional_effect";
                    return;
                }
                action.ActionLabel = "Dialog option " + action.DialogResult;
                action.ActionGroup = "dialog";
                action.IsMechanical = true;
                action.StrategicRole = "engine_selection";
                action.RequiresTarget = false;
                action.TargetScope = "dialog_option";
                return;
            }

            if (action.Kind == LegalActionKind.ListIndex)
            {
                action.ActionLabel = "List option " + action.Index;
                action.ActionGroup = "list";
                action.IsMechanical = true;
                action.StrategicRole = "engine_selection";
                action.RequiresTarget = true;
                action.TargetScope = "list_item";
            }
        }

        static void ApplyCommandSemantics(LegalAction action)
        {
            string cardName = action.Card == null ? null : action.Card.Name;
            string suffix = string.IsNullOrEmpty(cardName) ? "" : " " + cardName;
            switch (action.Command)
            {
                case DuelCommandType.Summon:
                    action.ActionLabel = "Summon" + suffix;
                    action.ActionGroup = "summon";
                    action.StrategicRole = "board_development";
                    action.ConsequenceHint = "normal_summon_consumes_turn_summon";
                    break;
                case DuelCommandType.SummonSp:
                    action.ActionLabel = "Special Summon" + suffix;
                    action.ActionGroup = "summon";
                    action.StrategicRole = "board_development";
                    action.ConsequenceHint = "special_summon_develops_board";
                    break;
                case DuelCommandType.Pendulum:
                    action.ActionLabel = "Pendulum Summon" + suffix;
                    action.ActionGroup = "summon";
                    action.StrategicRole = "board_development";
                    action.ConsequenceHint = "summons_available_pendulum_monsters";
                    break;
                case DuelCommandType.SetMonst:
                    action.ActionLabel = "Set Monster" + suffix;
                    action.ActionGroup = "set";
                    action.StrategicRole = "board_development";
                    action.ConsequenceHint = "sets_monster_defensively";
                    break;
                case DuelCommandType.Set:
                    action.ActionLabel = "Set Spell/Trap" + suffix;
                    action.ActionGroup = "set";
                    action.StrategicRole = "resource_preservation";
                    action.ConsequenceHint = "sets_card_for_later_use";
                    break;
                case DuelCommandType.Action:
                    action.ActionLabel = "Activate" + suffix;
                    action.ActionGroup = "activate";
                    action.StrategicRole = "effect_activation";
                    action.RequiresTarget = true;
                    action.TargetScope = "effect_defined";
                    action.ConsequenceHint = "may_open_followup_selection";
                    break;
                case DuelCommandType.Attack:
                    action.ActionLabel = "Attack" + (string.IsNullOrEmpty(cardName) ? "" : ": " + cardName);
                    action.ActionGroup = "battle";
                    action.StrategicRole = "battle_damage";
                    action.RequiresTarget = true;
                    action.TargetScope = "opponent_field_or_direct";
                    action.ConsequenceHint = "declares_attack_may_open_target_selection";
                    break;
                case DuelCommandType.Reverse:
                    action.ActionLabel = "Flip Summon" + suffix;
                    action.ActionGroup = "summon";
                    action.StrategicRole = "board_development";
                    action.ConsequenceHint = "changes_set_monster_face_up";
                    break;
                case DuelCommandType.TurnAtk:
                    action.ActionLabel = "Change to Attack" + suffix;
                    action.ActionGroup = "battle_position";
                    action.StrategicRole = "battle_position";
                    action.ConsequenceHint = "changes_monster_to_attack_position";
                    break;
                case DuelCommandType.TurnDef:
                    action.ActionLabel = "Change to Defense" + suffix;
                    action.ActionGroup = "battle_position";
                    action.StrategicRole = "battle_position";
                    action.ConsequenceHint = "changes_monster_to_defense_position";
                    break;
                case DuelCommandType.Decide:
                    action.ActionLabel = "Decide" + suffix;
                    action.ActionGroup = action.IsEffectTargetSelection ? "target" : "mechanical";
                    action.IsMechanical = !action.IsEffectTargetSelection;
                    action.StrategicRole = action.IsEffectTargetSelection ?
                        "effect_target_selection" :
                        (action.CardUniqueId > 0 ? "placement" : "target_selection");
                    action.RequiresTarget = true;
                    action.TargetScope = action.IsEffectTargetSelection ?
                        "effect_target" :
                        (action.CardUniqueId > 0 ? "summon_placement" : "battle_target");
                    action.ConsequenceHint = "engine_followup_selection";
                    break;
                default:
                    action.ActionLabel = action.Command.ToString() + suffix;
                    action.ActionGroup = "command";
                    action.StrategicRole = "unknown";
                    break;
            }
        }

        static string PhaseLabel(DuelPhase phase)
        {
            switch (phase)
            {
                case DuelPhase.Draw:
                    return "Draw Phase";
                case DuelPhase.Standby:
                    return "Standby Phase";
                case DuelPhase.Main1:
                    return "Main Phase 1";
                case DuelPhase.Battle:
                    return "Battle Phase";
                case DuelPhase.Main2:
                    return "Main Phase 2";
                case DuelPhase.End:
                    return "End Phase";
                default:
                    return phase.ToString();
            }
        }

        static string PhaseConsequenceHint(DuelPhase phase)
        {
            switch (phase)
            {
                case DuelPhase.Battle:
                    return "opens_attack_declarations";
                case DuelPhase.Main2:
                    return "leaves_battle_phase_for_second_main";
                case DuelPhase.End:
                    return "passes_turn_after_open_actions";
                default:
                    return null;
            }
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
            int initialActionCount = snapshot.LegalActions.Count;
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

            if (snapshot.LegalActions.Count != initialActionCount ||
                query.GetDialogCanYesNoSkip() == 0)
            {
                return;
            }

            AddYesNoDialogAction(snapshot, 1);
            AddYesNoDialogAction(snapshot, 0);
        }

        static void AddYesNoDialogAction(DecisionSnapshot snapshot, int result)
        {
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = snapshot.LegalActions.Count,
                Kind = LegalActionKind.DialogResult,
                DialogResult = result,
                DialogIsYesNoPrompt = true,
            });
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

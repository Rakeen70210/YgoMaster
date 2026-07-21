using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-005 Slice 2H: duel capabilities, first-turn combat horizon,
    /// and overlay/body role projection.
    /// </summary>
    static class Llm005Slice2HTests
    {
        public static void RunAll()
        {
            FirstTurnCapabilitiesRejectImmediateBattleValue();
            LaterTurnAllowsBattleWhenLegal();
            SeparatesTurnWideBattleFromModalChooseNow();
            RequestJsonIncludesDuelCapabilities();
            OverlayMaterialIsNotIndependentBody();
            IndependentBodyCountExcludesMaterials();
        }

        static void FirstTurnCapabilitiesRejectImmediateBattleValue()
        {
            // Captured opening: P2 goes first, internal turn=0, Master Duel Turn 1.
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 10,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                Turn = 0,
                TurnPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                CardId = 1,
                Card = new LlmCardMetadata() { CardId = 1, Name = "Exploder Dragon" },
            });
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
            });
            // No Enter Battle Phase, no Attack.

            LlmDuelCapabilities caps = LlmDuelCapabilitiesProjector.Project(snapshot);
            AssertEqual(0, caps.TurnIndexZeroBased, "turn_index_zero_based");
            AssertEqual(1, caps.TurnNumberOneBased, "turn_number_one_based");
            AssertEqual(true, caps.IsDuelFirstTurn, "is_duel_first_turn");
            AssertEqual(true, caps.IsControlledPlayersFirstTurn, "is_controlled_players_first_turn");
            AssertEqual(false, caps.BattlePhaseAllowedThisTurn, "battle_phase_allowed_this_turn");
            AssertEqual(false, caps.CanChooseBattlePhaseNow, "can_choose_battle_phase_now");
            AssertEqual(false, caps.CanDeclareAttackNow, "can_declare_attack_now");
            AssertEqual(
                "first_turn_player_cannot_battle",
                caps.BattleUnavailableReason,
                "battle_unavailable_reason");
            AssertEqual("next_controlled_turn", caps.DamageHorizon, "damage_horizon");
        }

        static void LaterTurnAllowsBattleWhenLegal()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 50,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                Turn = 2,
                TurnPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
            });
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 1,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Attack,
                CardId = 20,
            });

            LlmDuelCapabilities caps = LlmDuelCapabilitiesProjector.Project(snapshot);
            AssertEqual(2, caps.TurnIndexZeroBased, "turn index");
            AssertEqual(3, caps.TurnNumberOneBased, "display turn");
            AssertEqual(true, caps.BattlePhaseAllowedThisTurn, "battle allowed later turn");
            AssertEqual(true, caps.CanChooseBattlePhaseNow, "can choose battle now");
            AssertEqual(true, caps.CanDeclareAttackNow, "can attack now");
            AssertEqual("this_turn", caps.DamageHorizon, "damage this turn");
        }

        static void SeparatesTurnWideBattleFromModalChooseNow()
        {
            // Later turn, controlled player's turn, but current window is a dialog/target
            // with no Enter Battle Phase action — turn-wide still allowed.
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 60,
                ViewType = DuelViewType.RunDialog,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                Turn = 2,
                TurnPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.DialogResult,
                DialogResult = 1,
                ActionLabel = "Activate optional effect",
            });

            LlmDuelCapabilities caps = LlmDuelCapabilitiesProjector.Project(snapshot);
            AssertEqual(true, caps.BattlePhaseAllowedThisTurn, "turn-wide battle allowed");
            AssertEqual(false, caps.CanChooseBattlePhaseNow, "modal choose-now false");
            AssertEqual(false, caps.CanDeclareAttackNow, "no attack in modal");
        }

        static void RequestJsonIncludesDuelCapabilities()
        {
            DecisionSnapshot snapshot = new DecisionSnapshot()
            {
                RunEffectSeq = 11,
                ViewType = DuelViewType.WaitInput,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                Turn = 0,
                TurnPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            snapshot.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
            });
            snapshot.DuelCapabilities = LlmDuelCapabilitiesProjector.Project(snapshot);
            string json = LlmBrokerProtocol.SerializeDecisionRequest(snapshot);
            AssertTrue(json.IndexOf("duel_capabilities", StringComparison.Ordinal) >= 0,
                "request has duel_capabilities");
            AssertTrue(json.IndexOf("turn_index_zero_based", StringComparison.Ordinal) >= 0,
                "has turn_index_zero_based");
            AssertTrue(json.IndexOf("turn_number_one_based", StringComparison.Ordinal) >= 0,
                "has turn_number_one_based");
            AssertTrue(json.IndexOf("battle_phase_allowed_this_turn", StringComparison.Ordinal) >= 0,
                "has battle_phase_allowed_this_turn");
            // Preserve ambiguous internal turn for compatibility.
            AssertTrue(json.IndexOf("\"turn\":0", StringComparison.Ordinal) >= 0,
                "preserves internal turn=0");
        }

        static void OverlayMaterialIsNotIndependentBody()
        {
            OverlayQuery query = new OverlayQuery();
            query.CardNums[Key(1, 2)] = 1; // body + material index
            query.CardUniqueIds[Key(1, 2, 0)] = 100;
            query.CardUniqueIds[Key(1, 2, 1)] = 101;
            query.CardIdsByUniqueId[100] = 5057; // Number 20
            query.CardIdsByUniqueId[101] = 4061; // Hyena material
            query.OverlayByLocate[Key(1, 2)] = 1;
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[5057] = new LlmCardMetadata()
            {
                CardId = 5057,
                Name = "Number 20: Giga-Brilliant",
                Frame = "Xyz",
                SummonFamily = "xyz",
                UsesRank = true,
                Level = 3,
            };
            catalog.Cards[4061] = new LlmCardMetadata()
            {
                CardId = 4061,
                Name = "Gene-Warped Warwolf",
            };

            LlmSelfResources resources = LlmSelfResourceProjection.Project(
                query, 1, catalog, LlmPublicVisibilityMode.RuntimeDllField);

            int bodies = 0;
            int materials = 0;
            foreach (LlmSelfResourceCard card in resources.Field)
            {
                if (card.CountsAsIndependentBody)
                {
                    bodies++;
                }
                if (card.BoardRole == "xyz_material")
                {
                    materials++;
                }
            }
            AssertEqual(1, bodies, "one independent Xyz body");
            AssertEqual(1, materials, "one material role");
            AssertEqual(true, resources.Field[0].OverlayCount.HasValue &&
                resources.Field[0].OverlayCount.Value == 1, "overlay_count on body");
        }

        static void IndependentBodyCountExcludesMaterials()
        {
            OverlayQuery query = new OverlayQuery();
            query.CardNums[Key(1, 1)] = 0;
            query.CardUniqueIds[Key(1, 1, 0)] = 200;
            query.CardIdsByUniqueId[200] = 1000;
            query.OverlayByLocate[Key(1, 1)] = 0;
            FakeCardCatalog catalog = new FakeCardCatalog();
            catalog.Cards[1000] = new LlmCardMetadata()
            {
                CardId = 1000,
                Name = "Normal Monster",
            };

            LlmSelfResources resources = LlmSelfResourceProjection.Project(
                query, 1, catalog, LlmPublicVisibilityMode.RuntimeDllField);
            Dictionary<string, object> serialized = LlmSelfResourceProjection.Serialize(resources, null);
            string json = MiniJSON.Json.Serialize(serialized);
            AssertTrue(json.IndexOf("board_role", StringComparison.Ordinal) >= 0, "board_role serialized");
            AssertTrue(json.IndexOf("counts_as_independent_body", StringComparison.Ordinal) >= 0,
                "independent body flag serialized");
        }

        static string Key(params int[] args)
        {
            string[] parts = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                parts[i] = args[i].ToString();
            }
            return string.Join(",", parts);
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value)
            {
                throw new Exception(message + ": expected true");
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + ": expected " + expected + ", got " + actual);
            }
        }

        sealed class OverlayQuery : ILegalActionQuery, ILlmOverlayQuery
        {
            public readonly Dictionary<string, int> CardNums = new Dictionary<string, int>();
            public readonly Dictionary<string, int> CardUniqueIds = new Dictionary<string, int>();
            public readonly Dictionary<int, int> CardIdsByUniqueId = new Dictionary<int, int>();
            public readonly Dictionary<string, int> OverlayByLocate = new Dictionary<string, int>();

            public int GetThisCardOverlayNum(int player, int locate)
            {
                int value;
                return OverlayByLocate.TryGetValue(player + "," + locate, out value) ? value : 0;
            }

            public int GetCardNum(int player, int position)
            {
                int value;
                return CardNums.TryGetValue(player + "," + position, out value) ? value : 0;
            }

            public uint GetCommandMask(int player, int position, int index) { return 0; }
            public int GetCardFace(int player, int position, int index) { return 1; }
            public int GetCardIdByUniqueId(int uniqueId)
            {
                int value;
                return CardIdsByUniqueId.TryGetValue(uniqueId, out value) ? value : 0;
            }
            public int GetCardUniqueId(int player, int position, int index)
            {
                int value;
                return CardUniqueIds.TryGetValue(player + "," + position + "," + index, out value)
                    ? value : 0;
            }
            public int GetHandCardOpen(int player, int index) { return 0; }
            public int GetLifePoints(int player) { return 8000; }
            public uint GetMovablePhase() { return 0; }
            public int GetCurrentPhase() { return (int)DuelPhase.Main1; }
            public int GetCurrentStep() { return 0; }
            public int GetTurnNum() { return 0; }
            public int GetTurnPlayer() { return 1; }
            public int GetAttackTargetMask(int player, int locate) { return 0; }
            public int GetDialogCanYesNoSkip() { return 0; }
            public int GetDialogSelectItemEnable(int index) { return 0; }
            public int GetDialogSelectItemNum() { return 0; }
            public int GetDialogSelectItemTextId(int index) { return 0; }
            public int GetListItemAttribute(int index) { return 0; }
            public int GetListItemFrom(int index) { return 0; }
            public int GetListItemId(int index) { return 0; }
            public int GetListItemMax() { return 0; }
            public int GetListItemMsg(int index) { return 0; }
            public int GetListItemTargetUniqueId(int index) { return 0; }
            public int GetListItemUniqueId(int index) { return 0; }
            public int GetListSelectMax() { return 0; }
            public int GetListSelectMin() { return 0; }
            public int GetListIsMultiMode() { return 0; }
            public int GetSummoningMonsterUniqueId() { return 0; }
            public int GetSummonPositionMask() { return 0; }
        }

        sealed class FakeCardCatalog : ILlmCardCatalog
        {
            public readonly Dictionary<int, LlmCardMetadata> Cards =
                new Dictionary<int, LlmCardMetadata>();

            public LlmCardMetadata GetCard(int cardId)
            {
                LlmCardMetadata card;
                Cards.TryGetValue(cardId, out card);
                return card;
            }
        }
    }
}

using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Grounded turn/phase capability projection (YGOMASTER-LLM-005 Slice 2H).
    /// Derived from authoritative engine/rules state, not provider inference.
    /// </summary>
    class LlmDuelCapabilities
    {
        public int TurnIndexZeroBased { get; set; }
        public int TurnNumberOneBased { get; set; }
        public int TurnPlayer { get; set; }
        public int ControlledPlayer { get; set; }
        public bool IsControlledPlayersTurn { get; set; }
        public bool IsDuelFirstTurn { get; set; }
        public bool IsControlledPlayersFirstTurn { get; set; }
        public int CurrentPhase { get; set; }
        public int CurrentStep { get; set; }
        public string CurrentPhaseName { get; set; }
        /// <summary>
        /// Turn-wide: whether Battle is allowed for the controlled player this turn
        /// under master-rule first-turn restrictions.
        /// </summary>
        public bool BattlePhaseAllowedThisTurn { get; set; }
        /// <summary>
        /// Modal: whether Enter Battle Phase is a current legal action.
        /// </summary>
        public bool CanChooseBattlePhaseNow { get; set; }
        /// <summary>
        /// Modal: whether any Attack command is currently legal.
        /// </summary>
        public bool CanDeclareAttackNow { get; set; }
        public string BattleUnavailableReason { get; set; }
        public string AttackUnavailableReason { get; set; }
        /// <summary>this_turn | next_controlled_turn | unavailable</summary>
        public string DamageHorizon { get; set; }
        public List<string> LegalNextPhaseTransitions { get; set; }
        public List<string> ProjectionWarnings { get; set; }

        public LlmDuelCapabilities()
        {
            CurrentPhaseName = string.Empty;
            BattleUnavailableReason = string.Empty;
            AttackUnavailableReason = string.Empty;
            DamageHorizon = "unknown";
            LegalNextPhaseTransitions = new List<string>();
            ProjectionWarnings = new List<string>();
        }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>()
            {
                { "turn_index_zero_based", TurnIndexZeroBased },
                { "turn_number_one_based", TurnNumberOneBased },
                { "turn_player", TurnPlayer },
                { "controlled_player", ControlledPlayer },
                { "is_controlled_players_turn", IsControlledPlayersTurn },
                { "is_duel_first_turn", IsDuelFirstTurn },
                { "is_controlled_players_first_turn", IsControlledPlayersFirstTurn },
                { "current_phase", CurrentPhase },
                { "current_phase_name", CurrentPhaseName },
                { "current_step", CurrentStep },
                { "battle_phase_allowed_this_turn", BattlePhaseAllowedThisTurn },
                { "can_choose_battle_phase_now", CanChooseBattlePhaseNow },
                { "can_declare_attack_now", CanDeclareAttackNow },
                { "battle_unavailable_reason",
                    string.IsNullOrEmpty(BattleUnavailableReason) ? null : BattleUnavailableReason },
                { "attack_unavailable_reason",
                    string.IsNullOrEmpty(AttackUnavailableReason) ? null : AttackUnavailableReason },
                { "damage_horizon", DamageHorizon },
                { "legal_next_phase_transitions", LegalNextPhaseTransitions },
                { "projection_warnings", ProjectionWarnings },
            };
        }
    }

    static class LlmDuelCapabilitiesProjector
    {
        public static LlmDuelCapabilities Project(DecisionSnapshot snapshot)
        {
            LlmDuelCapabilities caps = new LlmDuelCapabilities();
            if (snapshot == null)
            {
                caps.ProjectionWarnings.Add("missing_snapshot");
                caps.DamageHorizon = "unknown";
                return caps;
            }

            caps.TurnIndexZeroBased = snapshot.Turn;
            caps.TurnNumberOneBased = snapshot.Turn + 1;
            caps.TurnPlayer = snapshot.TurnPlayer;
            caps.ControlledPlayer = snapshot.ControlledPlayer;
            caps.IsControlledPlayersTurn = snapshot.TurnPlayer == snapshot.ControlledPlayer;
            caps.IsDuelFirstTurn = snapshot.Turn == 0;
            caps.IsControlledPlayersFirstTurn =
                snapshot.Turn == 0 && snapshot.TurnPlayer == snapshot.ControlledPlayer;
            caps.CurrentPhase = snapshot.CurrentPhase;
            caps.CurrentStep = snapshot.CurrentStep;
            caps.CurrentPhaseName = PhaseName(snapshot.CurrentPhase);

            // Master Duel: the player who takes the first turn of the duel cannot
            // enter Battle that turn. That is turn index 0 for whoever is turn player.
            bool firstTurnNoBattle = snapshot.Turn == 0;
            // Turn-wide permission for the controlled player on this turn clock.
            // On turn 0, Battle is forbidden for the turn player (first player).
            // On later turns, Battle is allowed when it is the controlled player's turn.
            if (firstTurnNoBattle)
            {
                caps.BattlePhaseAllowedThisTurn = false;
            }
            else if (caps.IsControlledPlayersTurn)
            {
                caps.BattlePhaseAllowedThisTurn = true;
            }
            else
            {
                // Not their turn; battle becomes available on their next turn (not first).
                caps.BattlePhaseAllowedThisTurn = true;
            }

            bool hasBattleMove = false;
            bool hasAttack = false;
            if (snapshot.LegalActions != null)
            {
                foreach (LegalAction action in snapshot.LegalActions)
                {
                    if (action == null)
                    {
                        continue;
                    }
                    if (action.Kind == LegalActionKind.MovePhase)
                    {
                        string phaseName = PhaseName((int)action.Phase);
                        if (!caps.LegalNextPhaseTransitions.Contains(phaseName))
                        {
                            caps.LegalNextPhaseTransitions.Add(phaseName);
                        }
                        if (action.Phase == DuelPhase.Battle)
                        {
                            hasBattleMove = true;
                        }
                    }
                    if (action.Kind == LegalActionKind.Command &&
                        action.Command == DuelCommandType.Attack)
                    {
                        hasAttack = true;
                    }
                }
            }

            caps.CanChooseBattlePhaseNow = hasBattleMove;
            caps.CanDeclareAttackNow = hasAttack;

            if (!caps.BattlePhaseAllowedThisTurn && caps.IsControlledPlayersTurn && firstTurnNoBattle)
            {
                caps.BattleUnavailableReason = "first_turn_player_cannot_battle";
                caps.AttackUnavailableReason = "first_turn_player_cannot_battle";
                caps.DamageHorizon = "next_controlled_turn";
            }
            else if (!caps.CanChooseBattlePhaseNow && caps.CanDeclareAttackNow)
            {
                // Already in battle / attack available without phase move.
                caps.BattleUnavailableReason = string.Empty;
                caps.DamageHorizon = "this_turn";
            }
            else if (!caps.CanChooseBattlePhaseNow && !hasAttack)
            {
                if (caps.BattlePhaseAllowedThisTurn)
                {
                    // Modal absence only — battle may still be allowed after current prompt.
                    caps.BattleUnavailableReason = "engine_action_not_legal";
                    caps.DamageHorizon = caps.IsControlledPlayersTurn
                        ? "this_turn"
                        : "next_controlled_turn";
                }
                else
                {
                    caps.DamageHorizon = "next_controlled_turn";
                }
            }
            else if (caps.CanChooseBattlePhaseNow || hasAttack)
            {
                caps.DamageHorizon = "this_turn";
            }
            else
            {
                caps.DamageHorizon = "unknown";
            }

            if (!caps.CanDeclareAttackNow && string.IsNullOrEmpty(caps.AttackUnavailableReason))
            {
                if (!caps.BattlePhaseAllowedThisTurn && caps.IsControlledPlayersTurn)
                {
                    caps.AttackUnavailableReason = "first_turn_player_cannot_battle";
                }
                else if (!caps.IsControlledPlayersTurn)
                {
                    caps.AttackUnavailableReason = "not_controlled_players_turn";
                }
                else
                {
                    caps.AttackUnavailableReason = "engine_action_not_legal";
                }
            }

            return caps;
        }

        static string PhaseName(int phase)
        {
            if (System.Enum.IsDefined(typeof(DuelPhase), phase))
            {
                return ((DuelPhase)phase).ToString();
            }
            return "Phase" + phase;
        }
    }
}

namespace YgoMaster
{
    /// <summary>
    /// Deterministic recovery after non-stale broker quality/provider failures.
    /// Progress is hard: select a still-legal action to commit before last-resort CpuThinking.
    /// Optional response windows prefer decline/pass over list-order activations
    /// (YGOMASTER-LLM-003 Milestone 3D).
    /// </summary>
    static class LlmBrokerRecovery
    {
        public const string PolicyPreferredAction = "preferred_action";
        public const string PolicyOptionalDecline = "optional_decline";
        public const string PolicyForcedMechanical = "forced_mechanical";
        public const string PolicyGroundedPositive = "grounded_positive";
        public const string PolicyQualityPasser = "quality_passer";
        public const string PolicyEndPhase = "end_phase";
        public const string PolicyFirstNonMechanical = "first_non_mechanical";

        public static bool IsStaleError(string error)
        {
            return error == "stale_run_effect_seq";
        }

        public static bool TrySelectAction(
            DecisionSnapshot snapshot,
            string error,
            LegalAction preferredAction,
            out LegalAction selectedAction,
            out string policyBranch)
        {
            selectedAction = null;
            policyBranch = null;

            if (IsStaleError(error))
            {
                return false;
            }
            if (snapshot == null || snapshot.LegalActions == null || snapshot.LegalActions.Count == 0)
            {
                return false;
            }

            // 1) Exact still-legal provider intent (even when quality rejected it).
            if (preferredAction != null
                && error != LlmEffectApplicabilityAnalyzer.ErrorEffectApplicabilityContradiction)
            {
                foreach (LegalAction candidate in snapshot.LegalActions)
                {
                    if (LlmBrokerProtocol.IsSameAction(candidate, preferredAction))
                    {
                        selectedAction = candidate;
                        policyBranch = PolicyPreferredAction;
                        return true;
                    }
                }
            }

            // A target continuation is part of the strategic attack choice. Without
            // an exact still-legal provider target, recovery must report divergence
            // and hand off explicitly instead of selecting the first engine target.
            if (IsAttackTargetWindow(snapshot))
            {
                return false;
            }

            // 2) Legal decline/pass/no-response for optional timing/chain/activation/mode.
            // Never activate/pay cost/choose target merely because list order puts it first.
            LegalAction optionalDecline;
            if (TryFindOptionalDeclineOrPass(snapshot, out optionalDecline))
            {
                selectedAction = optionalDecline;
                policyBranch = PolicyOptionalDecline;
                return true;
            }

            // 3) Proven forced/mechanical action when that is the only safe progress.
            LegalAction forcedMechanical;
            if (TryFindSoleForcedMechanical(snapshot, out forcedMechanical))
            {
                selectedAction = forcedMechanical;
                policyBranch = PolicyForcedMechanical;
                return true;
            }

            // 4) Grounded deterministic action with a positive heuristic score.
            LegalAction grounded;
            if (TrySelectGroundedPositive(snapshot, out grounded))
            {
                selectedAction = grounded;
                policyBranch = PolicyGroundedPositive;
                return true;
            }

            // 5) Legacy quality passer / end-phase / first non-mechanical fallbacks
            // for windows without optional decline and without positive grounded score.
            foreach (LegalAction candidate in snapshot.LegalActions)
            {
                LlmBrokerValidationResult quality = LlmBrokerProtocol.ValidateActionQuality(
                    snapshot, candidate);
                if (quality.IsValid)
                {
                    selectedAction = candidate;
                    policyBranch = PolicyQualityPasser;
                    return true;
                }
            }

            LegalAction endPhase = null;
            LegalAction firstNonMechanical = null;
            foreach (LegalAction candidate in snapshot.LegalActions)
            {
                if (candidate.IsMechanical)
                {
                    continue;
                }
                if (LlmEffectApplicabilityAnalyzer.IsHardBlocked(candidate))
                {
                    continue;
                }
                if (firstNonMechanical == null)
                {
                    firstNonMechanical = candidate;
                }
                if (endPhase == null &&
                    candidate.Kind == LegalActionKind.MovePhase &&
                    candidate.Phase == DuelPhase.End)
                {
                    endPhase = candidate;
                }
            }

            if (endPhase != null)
            {
                selectedAction = endPhase;
                policyBranch = PolicyEndPhase;
                return true;
            }
            if (firstNonMechanical != null)
            {
                selectedAction = firstNonMechanical;
                policyBranch = PolicyFirstNonMechanical;
                return true;
            }

            return false;
        }

        public static bool IsOptionalDeclineOrPass(LegalAction action)
        {
            if (action == null)
            {
                return false;
            }
            if (action.Kind == LegalActionKind.Cancel)
            {
                // CancelDecide empty-dialog pass and strategic response decline.
                return true;
            }
            if (action.Kind == LegalActionKind.DialogResult)
            {
                if (!string.IsNullOrEmpty(action.ActionLabel) &&
                    action.ActionLabel.IndexOf("Decline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                if (action.DialogIsYesNoPrompt && action.DialogResult == 0)
                {
                    return true;
                }
            }
            if (!string.IsNullOrEmpty(action.TargetScope))
            {
                string scope = action.TargetScope;
                if (scope == "check_timing_decline" ||
                    scope == "check_chain_decline" ||
                    scope == "empty_check_timing_decline" ||
                    scope == "empty_check_chain_decline" ||
                    scope == "empty_dialog_pass")
                {
                    return true;
                }
            }
            if (action.StrategicRole == "response_timing" ||
                action.StrategicRole == "empty_response_window" ||
                action.StrategicRole == "no_op_pass")
            {
                return true;
            }
            return false;
        }

        static bool TryFindOptionalDeclineOrPass(
            DecisionSnapshot snapshot,
            out LegalAction selected)
        {
            selected = null;
            foreach (LegalAction candidate in snapshot.LegalActions)
            {
                if (!IsOptionalDeclineOrPass(candidate))
                {
                    continue;
                }
                // Prefer strategic (non-mechanical) decline when both exist.
                if (selected == null || (selected.IsMechanical && !candidate.IsMechanical))
                {
                    selected = candidate;
                }
            }
            return selected != null;
        }

        static bool TryFindSoleForcedMechanical(
            DecisionSnapshot snapshot,
            out LegalAction selected)
        {
            selected = null;
            LegalAction only = null;
            int count = 0;
            foreach (LegalAction candidate in snapshot.LegalActions)
            {
                count++;
                if (candidate.IsMechanical)
                {
                    only = candidate;
                }
            }
            if (count == 1 && only != null)
            {
                selected = only;
                return true;
            }
            return false;
        }

        static bool TrySelectGroundedPositive(
            DecisionSnapshot snapshot,
            out LegalAction selected)
        {
            selected = null;
            // Reuse protocol heuristic; require a strictly positive score so we do not
            // treat unknown-applicability activations as "safe" by default when a
            // decline was already considered and found absent.
            LegalAction best;
            if (!LlmBrokerProtocol.TrySelectHeuristicBestAction(snapshot, out best))
            {
                return false;
            }
            selected = best;
            return true;
        }

        static bool IsAttackTargetWindow(DecisionSnapshot snapshot)
        {
            if (snapshot == null || snapshot.LegalActions == null ||
                snapshot.LegalActions.Count < 2)
            {
                return false;
            }
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action == null || !action.IsAttackTargetSelection)
                {
                    return false;
                }
            }
            return true;
        }
    }
}

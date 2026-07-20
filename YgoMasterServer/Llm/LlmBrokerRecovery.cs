namespace YgoMaster
{
    /// <summary>
    /// Deterministic recovery after non-stale broker quality/provider failures.
    /// Progress is hard: select a still-legal action to commit before last-resort CpuThinking.
    /// Quality guards remain on the happy path; recovery may intentionally reuse a
    /// quality-rejected provider choice (e.g. early_end_phase End Phase).
    /// </summary>
    static class LlmBrokerRecovery
    {
        public const string PolicyPreferredAction = "preferred_action";
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

            // 1) Prefer provider intent even when quality rejected it (seq 68 early_end_phase).
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

            // 2) First action that still passes quality validation.
            foreach (LegalAction candidate in snapshot.LegalActions)
            {
                LlmBrokerValidationResult quality = LlmBrokerProtocol.ValidateActionQuality(snapshot, candidate);
                if (quality.IsValid)
                {
                    selectedAction = candidate;
                    policyBranch = PolicyQualityPasser;
                    return true;
                }
            }

            // 3) Non-mechanical legal action; prefer End Phase when present.
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

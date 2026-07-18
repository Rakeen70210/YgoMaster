namespace YgoMaster
{
    /// <summary>
    /// Pure emission policy for private self_resources_audit lines (YGOMASTER-LLM-005).
    /// P2 with LlmBrokerControlPlayer=1 must never emit player 0 private resources.
    /// </summary>
    static class LlmSelfResourcesAuditPolicy
    {
        /// <summary>
        /// Returns true only when audit is enabled, the configured control seat is valid
        /// (0 or 1), and the snapshot's controlled and acting players both match that seat.
        /// </summary>
        public static bool ShouldEmitAudit(
            bool auditEnabled,
            int configuredControlPlayer,
            DecisionSnapshot snapshot)
        {
            if (!auditEnabled)
            {
                return false;
            }
            if (configuredControlPlayer != 0 && configuredControlPlayer != 1)
            {
                return false;
            }
            if (snapshot == null)
            {
                return false;
            }
            if (snapshot.ControlledPlayer != configuredControlPlayer)
            {
                return false;
            }
            if (snapshot.ActingPlayer != configuredControlPlayer)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Defense-in-depth: whether the serializer may attach SelfResources payload.
        /// Same gate as emission; keeps mismatched-seat calls from leaking private data.
        /// </summary>
        public static bool ShouldSerializeSelfResourcesPayload(
            bool auditEnabled,
            int configuredControlPlayer,
            DecisionSnapshot snapshot)
        {
            return ShouldEmitAudit(auditEnabled, configuredControlPlayer, snapshot)
                && snapshot != null
                && snapshot.SelfResources != null;
        }
    }
}

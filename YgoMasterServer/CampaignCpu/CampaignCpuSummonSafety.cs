namespace YgoMaster
{
    /// <summary>
    /// V1 risk cut for normal Summon/Set commands. Level 5+ monsters can open tribute
    /// confirmation/material continuations that were created under dual-Human capture and
    /// cannot be retargeted by a later NativeLease flip. Unknown levels fail to native.
    /// </summary>
    static class CampaignCpuSummonSafety
    {
        public static bool IsNormalSummonOrSet(CampaignCpuLegalAction action)
        {
            return action != null
                && action.Kind == LegalActionKind.Command
                && (action.Command == DuelCommandType.Summon
                    || action.Command == DuelCommandType.SetMonst);
        }

        public static bool ShouldDeferToNative(
            CampaignCpuLegalAction action,
            bool levelKnown,
            int level)
        {
            if (!IsNormalSummonOrSet(action))
            {
                return false;
            }
            return !levelKnown || level < 1 || level > 4;
        }
    }
}

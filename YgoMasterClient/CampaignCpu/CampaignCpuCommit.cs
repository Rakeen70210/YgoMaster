using System;
using YgoMaster;

namespace YgoMasterClient
{
    /// <summary>
    /// Client adapter over pure <see cref="CampaignCpuNativeCommit"/>.
    /// Live path binds DuelDll wrappers; pure TryApply is shared with the harness.
    /// </summary>
    static class CampaignCpuCommit
    {
        public static CampaignCpuCommitOutcome TryApply(
            CampaignCpuLegalAction action,
            Action<int> movePhase,
            Action<int, int, int, int> doCommand,
            Action<uint> dlgSetResult,
            Action<int> listSetIndex,
            Action<bool> cancelCommand2)
        {
            return CampaignCpuNativeCommit.TryApply(
                action,
                movePhase,
                doCommand,
                dlgSetResult,
                listSetIndex,
                cancelCommand2,
                defaultLocation: null);
        }

        public static CampaignCpuCommitOutcome TryApply(
            CampaignCpuLegalAction action,
            Action<int> movePhase,
            Action<int, int, int, int> doCommand,
            Action<uint> dlgSetResult,
            Action<int> listSetIndex,
            Action<bool> cancelCommand2,
            Action defaultLocation)
        {
            return CampaignCpuNativeCommit.TryApply(
                action,
                movePhase,
                doCommand,
                dlgSetResult,
                listSetIndex,
                cancelCommand2,
                defaultLocation);
        }

        /// <summary>
        /// Live path: invoke DuelDll public wrappers (incl. DefaultLocation).
        /// </summary>
        public static CampaignCpuCommitOutcome TryApplyLive(CampaignCpuLegalAction action)
        {
            return TryApply(
                action,
                phase => DuelDll.CampaignCpu_MovePhase(phase),
                (player, position, index, commandId) =>
                    DuelDll.CampaignCpu_DoCommand(player, position, index, commandId),
                result => DuelDll.CampaignCpu_DlgSetResult(result),
                index => DuelDll.CampaignCpu_ListSetIndex(index),
                decide => DuelDll.CampaignCpu_CancelCommand2(decide),
                () => DuelDll.CampaignCpu_DefaultLocation());
        }
    }
}

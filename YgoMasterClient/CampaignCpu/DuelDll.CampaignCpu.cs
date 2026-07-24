using System;
using YgoMaster;

namespace YgoMasterClient
{
    /// <summary>
    /// Public wrappers for CampaignCpu live query/commit. Same partial as DuelDll so
    /// private proxy methods are accessible without widening them globally.
    /// </summary>
    unsafe static partial class DuelDll
    {
        public static int CampaignCpu_GetCardNum(int player, int position)
        {
            return DLL_DuelGetCardNum(player, position);
        }

        public static uint CampaignCpu_GetCommandMask(int player, int position, int index)
        {
            return DLL_DuelComGetCommandMask(player, position, index);
        }

        public static int CampaignCpu_GetCardFace(int player, int position, int index)
        {
            return DLL_DuelGetCardFace(player, position, index);
        }

        public static int CampaignCpu_GetCardIdByUniqueId(int uniqueId)
        {
            return (int)DLL_DuelGetCardIDByUniqueID2(uniqueId);
        }

        public static int CampaignCpu_GetCardUniqueId(int player, int position, int index)
        {
            return DLL_DuelGetCardUniqueID(player, position, index);
        }

        public static int CampaignCpu_GetHandCardOpen(int player, int index)
        {
            return DLL_DuelGetHandCardOpen(player, index) ? 1 : 0;
        }

        public static int CampaignCpu_GetLifePoints(int player)
        {
            return DLL_DuelGetLP(player);
        }

        public static uint CampaignCpu_GetMovablePhase()
        {
            return DLL_DuelComGetMovablePhase();
        }

        public static int CampaignCpu_GetCurrentPhase()
        {
            return (int)DLL_DuelGetCurrentPhase();
        }

        public static int CampaignCpu_GetCurrentStep()
        {
            return (int)DLL_DuelGetCurrentStep();
        }

        public static int CampaignCpu_GetTurnNum()
        {
            return (int)DLL_DuelGetTurnNum();
        }

        public static int CampaignCpu_GetTurnPlayer()
        {
            return DLL_DuelWhichTurnNow();
        }

        public static int CampaignCpu_GetAttackTargetMask(int player, int locate)
        {
            return DLL_DuelGetAttackTargetMask(player, locate);
        }

        public static int CampaignCpu_GetDialogCanYesNoSkip()
        {
            return DLL_DuelDlgCanYesNoSkip();
        }

        public static int CampaignCpu_GetDialogSelectItemEnable(int index)
        {
            return DLL_DuelDlgGetSelectItemEnable(index);
        }

        public static int CampaignCpu_GetDialogSelectItemNum()
        {
            return DLL_DuelDlgGetSelectItemNum();
        }

        public static int CampaignCpu_GetDialogSelectItemTextId(int index)
        {
            return DLL_DuelDlgGetSelectItemStr(index);
        }

        public static int CampaignCpu_GetListItemAttribute(int index)
        {
            return DLL_DuelListGetItemAttribute(index);
        }

        public static int CampaignCpu_GetListItemFrom(int index)
        {
            return DLL_DuelListGetItemFrom(index);
        }

        public static int CampaignCpu_GetListItemId(int index)
        {
            return DLL_DuelListGetItemID(index);
        }

        public static int CampaignCpu_GetListItemMax()
        {
            return DLL_DuelListGetItemMax();
        }

        public static int CampaignCpu_GetListItemMsg(int index)
        {
            return DLL_DuelListGetItemMsg(index);
        }

        public static int CampaignCpu_GetListItemTargetUniqueId(int index)
        {
            return DLL_DuelListGetItemTargetUniqueID(index);
        }

        public static int CampaignCpu_GetListItemUniqueId(int index)
        {
            return DLL_DuelListGetItemUniqueID(index);
        }

        public static int CampaignCpu_GetListSelectMax()
        {
            return DLL_DuelListGetSelectMax();
        }

        public static int CampaignCpu_GetListSelectMin()
        {
            return DLL_DuelListGetSelectMin();
        }

        public static int CampaignCpu_GetListIsMultiMode()
        {
            return DLL_DuelListIsMultiMode();
        }

        public static int CampaignCpu_GetSummoningMonsterUniqueId()
        {
            return DLL_DlgProcGetSummoningMonsterUniqueID();
        }

        public static int CampaignCpu_GetSummonPositionMask()
        {
            return DLL_DuelDlgGetPosMaskOfThisSummon();
        }

        /// <summary>
        /// Engine default WaitInput/Location placement. Used when the summon pos mask is
        /// empty at RunEffect intercept (live residual) so CampaignCpu can finish placement
        /// without painting human UI or leasing TemporaryCpu for the rest of Main.
        /// </summary>
        public static void CampaignCpu_DefaultLocation()
        {
            if (DLL_DuelComDefaultLocation == null)
            {
                throw new InvalidOperationException("DLL_DuelComDefaultLocation not loaded");
            }
            DLL_DuelComDefaultLocation();
        }

        public static void CampaignCpu_MovePhase(int phase)
        {
            DLL_DuelComMovePhase(phase);
        }

        public static void CampaignCpu_DoCommand(int player, int position, int index, int commandId)
        {
            DLL_DuelComDoCommand(player, position, index, commandId);
        }

        public static void CampaignCpu_DlgSetResult(uint result)
        {
            DLL_DuelDlgSetResult(result);
        }

        public static void CampaignCpu_ListSetIndex(int index)
        {
            DLL_DuelListSetIndex(index);
        }

        public static void CampaignCpu_CancelCommand2(bool decide)
        {
            DLL_DuelComCancelCommand2(decide);
        }

        public static void CampaignCpu_SetPlayerType(int player, int type)
        {
            // Call original only — dual-driver lock uses the hook path separately.
            hookDLL_DuelSetPlayerType.Original(player, type);
        }

        public static void CampaignCpu_SetCpuParam(int player, uint param)
        {
            hookDLL_DuelSetCpuParam.Original(player, param);
        }

        /// <summary>
        /// Player-type readback for PR2b evidence. Returns DLL_DuelIsHuman (non-zero = Human).
        /// No direct GetPlayerType export; this is the available native confirmation surface.
        /// </summary>
        public static int CampaignCpu_IsHuman(int player)
        {
            return DLL_DuelIsHuman(player);
        }

        /// <summary>
        /// Field-zone trap query. <paramref name="locate"/> is the engine zone locate
        /// (field positions 0–12 for single-card zones).
        /// </summary>
        public static bool CampaignCpu_IsThisTrap(int player, int locate)
        {
            return DLL_DuelIsThisTrap(player, locate);
        }

        public static int CampaignCpu_IsThisTrapMonster(int player, int locate)
        {
            return DLL_DuelIsThisTrapMonster(player, locate);
        }
    }
}

using System;

namespace YgoMaster
{
    class PvpEngineStateLegalActionQuery : ILegalActionQuery, ILlmOverlayQuery
    {
        readonly PvpEngineState state;

        public PvpEngineStateLegalActionQuery(PvpEngineState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }
            this.state = state;
        }

        public int GetThisCardOverlayNum(int player, int locate)
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetThisCardOverlayNum, player, locate);
        }

        public int GetCardNum(int player, int position)
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetCardNum, player, position);
        }

        public uint GetCommandMask(int player, int position, int index)
        {
            return (uint)state.GetValue(PvpOperationType.DLL_DuelComGetCommandMask, player, position, index);
        }

        public int GetCardFace(int player, int position, int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetCardFace, player, position, index);
        }

        public int GetCardIdByUniqueId(int uniqueId)
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetCardIDByUniqueID2, uniqueId);
        }

        public int GetCardUniqueId(int player, int position, int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetCardUniqueID, player, position, index);
        }

        public int GetHandCardOpen(int player, int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetHandCardOpen, player, index);
        }

        public int GetLifePoints(int player)
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetLP, player);
        }

        public uint GetMovablePhase()
        {
            return (uint)state.GetValue(PvpOperationType.DLL_DuelComGetMovablePhase);
        }

        public int GetCurrentPhase()
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetCurrentPhase);
        }

        public int GetCurrentStep()
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetCurrentStep);
        }

        public int GetTurnNum()
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetTurnNum);
        }

        public int GetTurnPlayer()
        {
            return state.GetValue(PvpOperationType.DLL_DuelWhichTurnNow);
        }

        public int GetAttackTargetMask(int player, int locate)
        {
            return state.GetValue(PvpOperationType.DLL_DuelGetAttackTargetMask, player, locate);
        }

        public int GetDialogCanYesNoSkip()
        {
            return state.GetValue(PvpOperationType.DLL_DuelDlgCanYesNoSkip);
        }

        public int GetDialogSelectItemEnable(int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelDlgGetSelectItemEnable, index);
        }

        public int GetDialogSelectItemNum()
        {
            return state.GetValue(PvpOperationType.DLL_DuelDlgGetSelectItemNum);
        }

        public int GetDialogSelectItemTextId(int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelDlgGetSelectItemStr, index);
        }

        public int GetListItemAttribute(int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelListGetItemAttribute, index);
        }

        public int GetListItemFrom(int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelListGetItemFrom, index);
        }

        public int GetListItemId(int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelListGetItemID, index);
        }

        public int GetListItemMax()
        {
            return state.GetValue(PvpOperationType.DLL_DuelListGetItemMax);
        }

        public int GetListItemMsg(int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelListGetItemMsg, index);
        }

        public int GetListItemTargetUniqueId(int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelListGetItemTargetUniqueID, index);
        }

        public int GetListItemUniqueId(int index)
        {
            return state.GetValue(PvpOperationType.DLL_DuelListGetItemUniqueID, index);
        }

        public int GetListSelectMax()
        {
            return state.GetValue(PvpOperationType.DLL_DuelListGetSelectMax);
        }

        public int GetListSelectMin()
        {
            return state.GetValue(PvpOperationType.DLL_DuelListGetSelectMin);
        }

        public int GetListIsMultiMode()
        {
            return state.GetValue(PvpOperationType.DLL_DuelListIsMultiMode);
        }

        public int GetSummoningMonsterUniqueId()
        {
            return state.GetValue(PvpOperationType.DLL_DlgProcGetSummoningMonsterUniqueID);
        }

        public int GetSummonPositionMask()
        {
            return state.GetValue(PvpOperationType.DLL_DuelDlgGetPosMaskOfThisSummon);
        }
    }
}

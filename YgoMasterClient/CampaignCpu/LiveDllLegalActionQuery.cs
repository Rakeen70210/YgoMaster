using YgoMaster;

namespace YgoMasterClient
{
    /// <summary>
    /// Client-only ILegalActionQuery over public DuelDll.CampaignCpu_* wrappers.
    /// Harness must never reference this type; use fakes instead.
    /// </summary>
    sealed class LiveDllLegalActionQuery : ILegalActionQuery
    {
        public int GetCardNum(int player, int position)
        {
            return DuelDll.CampaignCpu_GetCardNum(player, position);
        }

        public uint GetCommandMask(int player, int position, int index)
        {
            return DuelDll.CampaignCpu_GetCommandMask(player, position, index);
        }

        public int GetCardFace(int player, int position, int index)
        {
            return DuelDll.CampaignCpu_GetCardFace(player, position, index);
        }

        public int GetCardIdByUniqueId(int uniqueId)
        {
            return DuelDll.CampaignCpu_GetCardIdByUniqueId(uniqueId);
        }

        public int GetCardUniqueId(int player, int position, int index)
        {
            return DuelDll.CampaignCpu_GetCardUniqueId(player, position, index);
        }

        public int GetHandCardOpen(int player, int index)
        {
            return DuelDll.CampaignCpu_GetHandCardOpen(player, index);
        }

        public int GetLifePoints(int player)
        {
            return DuelDll.CampaignCpu_GetLifePoints(player);
        }

        public uint GetMovablePhase()
        {
            return DuelDll.CampaignCpu_GetMovablePhase();
        }

        public int GetCurrentPhase()
        {
            return DuelDll.CampaignCpu_GetCurrentPhase();
        }

        public int GetCurrentStep()
        {
            return DuelDll.CampaignCpu_GetCurrentStep();
        }

        public int GetTurnNum()
        {
            return DuelDll.CampaignCpu_GetTurnNum();
        }

        public int GetTurnPlayer()
        {
            return DuelDll.CampaignCpu_GetTurnPlayer();
        }

        public int GetAttackTargetMask(int player, int locate)
        {
            return DuelDll.CampaignCpu_GetAttackTargetMask(player, locate);
        }

        public int GetDialogCanYesNoSkip()
        {
            return DuelDll.CampaignCpu_GetDialogCanYesNoSkip();
        }

        public int GetDialogSelectItemEnable(int index)
        {
            return DuelDll.CampaignCpu_GetDialogSelectItemEnable(index);
        }

        public int GetDialogSelectItemNum()
        {
            return DuelDll.CampaignCpu_GetDialogSelectItemNum();
        }

        public int GetDialogSelectItemTextId(int index)
        {
            return DuelDll.CampaignCpu_GetDialogSelectItemTextId(index);
        }

        public int GetListItemAttribute(int index)
        {
            return DuelDll.CampaignCpu_GetListItemAttribute(index);
        }

        public int GetListItemFrom(int index)
        {
            return DuelDll.CampaignCpu_GetListItemFrom(index);
        }

        public int GetListItemId(int index)
        {
            return DuelDll.CampaignCpu_GetListItemId(index);
        }

        public int GetListItemMax()
        {
            return DuelDll.CampaignCpu_GetListItemMax();
        }

        public int GetListItemMsg(int index)
        {
            return DuelDll.CampaignCpu_GetListItemMsg(index);
        }

        public int GetListItemTargetUniqueId(int index)
        {
            return DuelDll.CampaignCpu_GetListItemTargetUniqueId(index);
        }

        public int GetListItemUniqueId(int index)
        {
            return DuelDll.CampaignCpu_GetListItemUniqueId(index);
        }

        public int GetListSelectMax()
        {
            return DuelDll.CampaignCpu_GetListSelectMax();
        }

        public int GetListSelectMin()
        {
            return DuelDll.CampaignCpu_GetListSelectMin();
        }

        public int GetListIsMultiMode()
        {
            return DuelDll.CampaignCpu_GetListIsMultiMode();
        }

        public int GetSummoningMonsterUniqueId()
        {
            return DuelDll.CampaignCpu_GetSummoningMonsterUniqueId();
        }

        public int GetSummonPositionMask()
        {
            return DuelDll.CampaignCpu_GetSummonPositionMask();
        }
    }
}

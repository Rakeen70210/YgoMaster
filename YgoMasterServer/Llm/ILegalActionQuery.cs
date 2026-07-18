namespace YgoMaster
{
    interface ILegalActionQuery
    {
        int GetCardNum(int player, int position);
        uint GetCommandMask(int player, int position, int index);
        int GetCardFace(int player, int position, int index);
        int GetCardIdByUniqueId(int uniqueId);
        int GetCardUniqueId(int player, int position, int index);
        int GetHandCardOpen(int player, int index);
        int GetLifePoints(int player);
        uint GetMovablePhase();
        int GetCurrentPhase();
        int GetCurrentStep();
        int GetTurnNum();
        int GetTurnPlayer();
        int GetAttackTargetMask(int player, int locate);
        int GetDialogCanYesNoSkip();
        int GetDialogSelectItemEnable(int index);
        int GetDialogSelectItemNum();
        int GetDialogSelectItemTextId(int index);
        int GetListItemAttribute(int index);
        int GetListItemFrom(int index);
        int GetListItemId(int index);
        int GetListItemMax();
        int GetListItemMsg(int index);
        int GetListItemTargetUniqueId(int index);
        int GetListItemUniqueId(int index);
        int GetListSelectMax();
        int GetListSelectMin();
        int GetListIsMultiMode();
        int GetSummoningMonsterUniqueId();
        int GetSummonPositionMask();
    }
}

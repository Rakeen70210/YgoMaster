using System.Collections.Generic;

namespace YgoMaster
{
    enum LegalActionKind
    {
        MovePhase,
        Command,
        DialogResult,
        ListIndex
    }

    class LegalAction
    {
        public int ActionId { get; set; }
        public LegalActionKind Kind { get; set; }
        public int Player { get; set; }
        public int Position { get; set; }
        public int Index { get; set; }
        public DuelCommandType Command { get; set; }
        public DuelPhase Phase { get; set; }
        public int CardUniqueId { get; set; }
        public int CardId { get; set; }
        public LlmCardMetadata Card { get; set; }
        public int DialogResult { get; set; }
        public int DialogTextId { get; set; }
        public int ListItemAttribute { get; set; }
        public int ListItemFrom { get; set; }
        public int ListItemId { get; set; }
        public int ListItemMsg { get; set; }
        public int ListItemTargetUniqueId { get; set; }
        public int ListItemUniqueId { get; set; }
        public int ListSelectMax { get; set; }
        public int ListSelectMin { get; set; }
        public int ListIsMultiMode { get; set; }
    }

    class DecisionSnapshot
    {
        public ulong RunEffectSeq { get; set; }
        public DuelViewType ViewType { get; set; }
        public int ActingPlayer { get; set; }
        public int ControlledPlayer { get; set; }
        public int Turn { get; set; }
        public int TurnPlayer { get; set; }
        public int CurrentPhase { get; set; }
        public int CurrentStep { get; set; }
        public PublicDuelState PublicState { get; private set; }
        public List<LegalAction> LegalActions { get; private set; }

        public DecisionSnapshot()
        {
            PublicState = new PublicDuelState();
            LegalActions = new List<LegalAction>();
        }
    }

    class PublicDuelState
    {
        public List<PublicPlayerState> Players { get; private set; }

        public PublicDuelState()
        {
            Players = new List<PublicPlayerState>();
        }
    }

    class PublicPlayerState
    {
        public int Player { get; set; }
        public int LifePoints { get; set; }
        public List<PublicPositionState> Positions { get; private set; }
        public List<PublicKnownCard> KnownCards { get; private set; }

        public PublicPlayerState()
        {
            Positions = new List<PublicPositionState>();
            KnownCards = new List<PublicKnownCard>();
        }
    }

    class PublicPositionState
    {
        public int Position { get; set; }
        public int Count { get; set; }
    }

    class PublicKnownCard
    {
        public int Player { get; set; }
        public int Position { get; set; }
        public int Index { get; set; }
        public int CardUniqueId { get; set; }
        public int CardId { get; set; }
        public int Face { get; set; }
        public LlmCardMetadata Card { get; set; }
    }

    class LlmCardMetadata
    {
        public int CardId { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
        public string Kind { get; set; }
        public string Attribute { get; set; }
        public int Level { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Scale { get; set; }
    }

    interface ILlmCardCatalog
    {
        LlmCardMetadata GetCard(int cardId);
    }
}

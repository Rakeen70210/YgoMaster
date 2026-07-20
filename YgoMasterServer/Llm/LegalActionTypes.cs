using System.Collections.Generic;

namespace YgoMaster
{
    enum LegalActionKind
    {
        MovePhase,
        Command,
        DialogResult,
        ListIndex,
        Cancel
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
        public bool DialogIsYesNoPrompt { get; set; }
        public bool CancelDecide { get; set; }
        public bool IsEffectTargetSelection { get; set; }
        public bool IsAttackTargetSelection { get; set; }
        public int ListItemAttribute { get; set; }
        public int ListItemFrom { get; set; }
        public int ListItemId { get; set; }
        public int ListItemMsg { get; set; }
        public int ListItemTargetUniqueId { get; set; }
        public int ListItemUniqueId { get; set; }
        public int ListSelectMax { get; set; }
        public int ListSelectMin { get; set; }
        public int ListIsMultiMode { get; set; }
        public string ActionLabel { get; set; }
        public string ActionGroup { get; set; }
        public bool IsMechanical { get; set; }
        public string StrategicRole { get; set; }
        public bool RequiresTarget { get; set; }
        public string TargetScope { get; set; }
        public string TargetToken { get; set; }
        public string ConsequenceHint { get; set; }
        public LlmEffectApplicabilityAnnotation EffectApplicability { get; set; }
    }

    class AttackTargetContext
    {
        public int AttackingPlayer { get; set; }
        public int AttackerPosition { get; set; }
        public ulong OriginRunEffectSeq { get; set; }
        public int OriginDuelGeneration { get; set; }
        public int AttackerCardId { get; set; }
        public int AttackerUniqueId { get; set; }
        public string OriginActionLabel { get; set; }
        public string OriginReason { get; set; }
        public string OriginPlan { get; set; }
    }

    class DecisionSnapshot
    {
        public ulong RunEffectSeq { get; set; }
        public DuelViewType ViewType { get; set; }
        public int ViewParam1 { get; set; }
        public int ViewParam2 { get; set; }
        public int ViewParam3 { get; set; }
        public int ActingPlayer { get; set; }
        public int ControlledPlayer { get; set; }
        public int Turn { get; set; }
        public int TurnPlayer { get; set; }
        public int CurrentPhase { get; set; }
        public int CurrentStep { get; set; }
        public PublicDuelState PublicState { get; private set; }
        public LlmTurnMemoryState TurnMemory { get; set; }
        /// <summary>
        /// Immutable authoritative public duel history snapshot (full, uncompacted).
        /// Attached alongside turn_memory; not yet serialized into broker request JSON.
        /// </summary>
        public LlmDuelHistoryState DuelHistory { get; set; }
        /// <summary>
        /// Immutable controlled-player private self-resource projection (YGOMASTER-LLM-005).
        /// Attached at extraction; not serialized into default decision-window /decide JSON.
        /// </summary>
        public LlmSelfResources SelfResources { get; set; }
        public AttackTargetContext AttackTargetContext { get; set; }
        public List<LegalAction> LegalActions { get; private set; }
        public bool IsStrategicWindow { get; set; }
        public string StrategicWindowReason { get; set; }
        public int StrategicActionCount { get; set; }
        public int MechanicalActionCount { get; set; }

        public DecisionSnapshot()
        {
            PublicState = new PublicDuelState();
            TurnMemory = new LlmTurnMemoryState();
            DuelHistory = new LlmDuelHistoryState();
            LegalActions = new List<LegalAction>();
            StrategicWindowReason = "no_actions";
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

        /// <summary>Grounded frame from GameCardInfo.Frame (e.g. Normal, Xyz, Link).</summary>
        public string Frame { get; set; }
        /// <summary>
        /// Coarse summon family for material rules: main_deck_monster, xyz, synchro, link,
        /// fusion, ritual, spell, trap, token, unknown.
        /// </summary>
        public string SummonFamily { get; set; }
        public bool IsExtraDeck { get; set; }
        public bool IsTuner { get; set; }
        /// <summary>True when Level field is rank semantics (Xyz / XyzPend).</summary>
        public bool UsesRank { get; set; }
        /// <summary>
        /// Link rating when a grounded source exists. Null when unavailable — never inferred
        /// from DEF or display text.
        /// </summary>
        public int? LinkRating { get; set; }
    }

    interface ILlmCardCatalog
    {
        LlmCardMetadata GetCard(int cardId);
    }

    class LlmTurnMemoryState
    {
        public List<LlmRecentActionMemory> RecentActions { get; private set; }
        public List<LlmUsedCardMemory> CardsUsedThisTurn { get; private set; }
        public bool NormalSummonUsed { get; set; }
        public string PhasePlan { get; set; }
        /// <summary>Slice 6B: intended followup intent (null when absent).</summary>
        public LlmIntendedFollowupMemory IntendedFollowup { get; set; }

        public LlmTurnMemoryState()
        {
            RecentActions = new List<LlmRecentActionMemory>();
            CardsUsedThisTurn = new List<LlmUsedCardMemory>();
        }
    }

    class LlmRecentActionMemory
    {
        public int Turn { get; set; }
        public int Phase { get; set; }
        public string ActionType { get; set; }
        public string ActionLabel { get; set; }
        public int CardId { get; set; }
        public string CardName { get; set; }
        public string Reason { get; set; }
        public string Plan { get; set; }
    }

    class LlmUsedCardMemory
    {
        public int CardId { get; set; }
        public string Name { get; set; }
    }
}

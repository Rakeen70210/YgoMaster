using System;

namespace YgoMaster
{
    enum LlmActionCommitKind
    {
        MovePhase,
        Command,
        DialogResult,
        ListIndex,
        Cancel
    }

    class LlmActionCommitPlan
    {
        const int PosSelect = 18;

        public LlmActionCommitKind Kind { get; private set; }
        public int PhaseId { get; private set; }
        public int Player { get; private set; }
        public int Position { get; private set; }
        public int Index { get; private set; }
        public int CommandId { get; private set; }
        public uint DialogResult { get; private set; }
        public bool CancelDecide { get; private set; }

        public static LlmActionCommitPlan FromLegalAction(LegalAction action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            if (action.Kind == LegalActionKind.MovePhase)
            {
                return new LlmActionCommitPlan()
                {
                    Kind = LlmActionCommitKind.MovePhase,
                    PhaseId = (int)action.Phase,
                };
            }
            if (action.Kind == LegalActionKind.DialogResult)
            {
                return new LlmActionCommitPlan()
                {
                    Kind = LlmActionCommitKind.DialogResult,
                    DialogResult = (uint)action.DialogResult,
                };
            }
            if (action.Kind == LegalActionKind.ListIndex)
            {
                return new LlmActionCommitPlan()
                {
                    Kind = LlmActionCommitKind.ListIndex,
                    Index = action.Index,
                };
            }
            if (action.Kind == LegalActionKind.Cancel)
            {
                return new LlmActionCommitPlan()
                {
                    Kind = LlmActionCommitKind.Cancel,
                    CancelDecide = action.CancelDecide,
                };
            }

            bool isSummonPlacement =
                action.Command == DuelCommandType.Decide &&
                action.TargetScope == "summon_placement";
            return new LlmActionCommitPlan()
            {
                Kind = LlmActionCommitKind.Command,
                Player = action.Player,
                Position = isSummonPlacement ? PosSelect : action.Position,
                Index = isSummonPlacement ? action.Position : action.Index,
                CommandId = (int)action.Command,
            };
        }
    }
}

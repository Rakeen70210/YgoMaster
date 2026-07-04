using System;

namespace YgoMaster
{
    enum LlmActionCommitKind
    {
        MovePhase,
        Command,
        DialogResult,
        ListIndex
    }

    class LlmActionCommitPlan
    {
        public LlmActionCommitKind Kind { get; private set; }
        public int PhaseId { get; private set; }
        public int Player { get; private set; }
        public int Position { get; private set; }
        public int Index { get; private set; }
        public int CommandId { get; private set; }
        public uint DialogResult { get; private set; }

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

            return new LlmActionCommitPlan()
            {
                Kind = LlmActionCommitKind.Command,
                Player = action.Player,
                Position = action.Position,
                Index = action.Index,
                CommandId = (int)action.Command,
            };
        }
    }
}

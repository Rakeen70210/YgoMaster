using System;

namespace YgoMaster
{
    /// <summary>
    /// Pure native plan + tri-state dispatcher for CampaignCpu commits.
    /// Linked into client and both harnesses; no DuelDll dependency.
    /// </summary>
    sealed class CampaignCpuNativePlan
    {
        public LegalActionKind Kind;
        public int PhaseId;
        public int Player;
        public int Position;
        public int Index;
        public int CommandId;
        public uint DialogResult;
        public bool CancelDecide;
    }

    static class CampaignCpuNativeCommit
    {
        public const int PosSelect = 18;

        public static bool TryBuildPlan(CampaignCpuLegalAction action, out CampaignCpuNativePlan plan)
        {
            plan = null;
            if (action == null)
            {
                return false;
            }
            try
            {
                plan = BuildPlan(action);
            }
            catch
            {
                plan = null;
                return false;
            }
            return plan != null;
        }

        public static CampaignCpuCommitOutcome TryApply(
            CampaignCpuLegalAction action,
            Action<int> movePhase,
            Action<int, int, int, int> doCommand,
            Action<uint> dlgSetResult,
            Action<int> listSetIndex,
            Action<bool> cancelCommand2)
        {
            if (action == null
                || movePhase == null
                || doCommand == null
                || dlgSetResult == null
                || listSetIndex == null
                || cancelCommand2 == null)
            {
                return CampaignCpuCommitOutcome.NotStarted;
            }

            CampaignCpuNativePlan plan;
            if (!TryBuildPlan(action, out plan) || plan == null)
            {
                return CampaignCpuCommitOutcome.NotStarted;
            }

            try
            {
                switch (plan.Kind)
                {
                    case LegalActionKind.MovePhase:
                        movePhase(plan.PhaseId);
                        break;
                    case LegalActionKind.Command:
                        doCommand(plan.Player, plan.Position, plan.Index, plan.CommandId);
                        break;
                    case LegalActionKind.DialogResult:
                        dlgSetResult(plan.DialogResult);
                        break;
                    case LegalActionKind.ListIndex:
                        listSetIndex(plan.Index);
                        break;
                    case LegalActionKind.Cancel:
                        cancelCommand2(plan.CancelDecide);
                        break;
                    default:
                        return CampaignCpuCommitOutcome.NotStarted;
                }
                return CampaignCpuCommitOutcome.Applied;
            }
            catch
            {
                return CampaignCpuCommitOutcome.Indeterminate;
            }
        }

        static CampaignCpuNativePlan BuildPlan(CampaignCpuLegalAction action)
        {
            if (action.Kind == LegalActionKind.MovePhase)
            {
                return new CampaignCpuNativePlan
                {
                    Kind = LegalActionKind.MovePhase,
                    PhaseId = (int)action.Phase,
                };
            }
            if (action.Kind == LegalActionKind.DialogResult)
            {
                return new CampaignCpuNativePlan
                {
                    Kind = LegalActionKind.DialogResult,
                    DialogResult = (uint)action.DialogResult,
                };
            }
            if (action.Kind == LegalActionKind.ListIndex)
            {
                return new CampaignCpuNativePlan
                {
                    Kind = LegalActionKind.ListIndex,
                    Index = action.Index,
                };
            }
            if (action.Kind == LegalActionKind.Cancel)
            {
                return new CampaignCpuNativePlan
                {
                    Kind = LegalActionKind.Cancel,
                    CancelDecide = action.CancelDecide,
                };
            }
            if (action.Kind != LegalActionKind.Command)
            {
                return null;
            }

            bool isSummonPlacement =
                action.Command == DuelCommandType.Decide &&
                string.Equals(action.TargetScope, "summon_placement", StringComparison.Ordinal);
            return new CampaignCpuNativePlan
            {
                Kind = LegalActionKind.Command,
                Player = action.Player,
                Position = isSummonPlacement ? PosSelect : action.Position,
                Index = isSummonPlacement ? action.Position : action.Index,
                CommandId = (int)action.Command,
            };
        }
    }
}

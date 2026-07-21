using System;
using System.Collections.Generic;
using YgoMaster;

namespace YgoMasterClient
{
    /// <summary>
    /// Builds CampaignCpuObservation: Extract → ApplyViewContext → project.
    /// LegalActionExtractor / DecisionSnapshot are boundary-only types.
    /// </summary>
    static class CampaignCpuObservationBuilder
    {
        public static CampaignCpuObservation Build(
            ILegalActionQuery query,
            ulong campaignCpuViewSeq,
            DuelViewType viewType,
            int viewParam1,
            int viewParam2,
            int viewParam3,
            int actingPlayer,
            int ownedSeat,
            int chapterId,
            out LegalAction automaticAction,
            out bool multiSelect)
        {
            automaticAction = null;
            multiSelect = false;
            if (query == null)
            {
                return new CampaignCpuObservation
                {
                    ChapterId = chapterId,
                    OwnedSeat = ownedSeat,
                    ActingPlayer = actingPlayer,
                    ViewType = viewType,
                    ViewParam1 = viewParam1,
                    ViewParam2 = viewParam2,
                    ViewParam3 = viewParam3,
                    CampaignCpuViewSeq = campaignCpuViewSeq,
                    WindowClass = CampaignCpuWindowClassifier.ClassifyWindow(viewType, viewParam1),
                    IsMainPhaseWaitInput = CampaignCpuWindowClassifier.IsMainPhaseWaitInput(
                        viewType, viewParam1),
                };
            }

            try
            {
                multiSelect = viewType == DuelViewType.RunList && query.GetListIsMultiMode() != 0;
            }
            catch
            {
                multiSelect = false;
            }

            DecisionSnapshot snap = LegalActionExtractor.Extract(
                query,
                campaignCpuViewSeq,
                viewType,
                actingPlayer);
            // ControlledPlayer on snapshot is set to actingPlayer by Extract; for
            // CampaignCpu the controlled opponent seat is OwnedSeat (== acting when owned).
            snap.ControlledPlayer = ownedSeat;
            LegalActionExtractor.ApplyViewContext(
                snap, query, viewParam1, viewParam2, viewParam3);

            // Mechanical helpers (auto_or_native)
            if (LegalActionExtractor.TryExtractAutomaticAction(
                query, viewType, actingPlayer, out automaticAction))
            {
                // keep automaticAction
            }
            else if (viewType == DuelViewType.RunDialog
                && LegalActionExtractor.TryExtractForcedDialogAcknowledgement(
                    query, viewParam1, defaultResult: 1, out automaticAction))
            {
                // keep
            }
            else
            {
                automaticAction = null;
            }

            CampaignCpuObservation obs = CampaignCpuObservationProjector.Project(
                snap, chapterId, ownedSeat, actingPlayer, multiSelect);

            // Prefer explicit hand ids for owned seat via unique-id walk.
            try
            {
                FillSelfHandCardIds(query, ownedSeat, obs);
            }
            catch
            {
                // leave projector hand ids; scorer fail-closed on predicates if needed
            }

            try
            {
                obs.SelfLp = query.GetLifePoints(ownedSeat);
                int opp = ownedSeat == 0 ? 1 : 0;
                obs.OppLp = query.GetLifePoints(opp);
            }
            catch
            {
            }

            return obs;
        }

        static void FillSelfHandCardIds(ILegalActionQuery query, int ownedSeat, CampaignCpuObservation obs)
        {
            obs.SelfHandCardIds = new List<int>();
            int n = query.GetCardNum(ownedSeat, CampaignCpuDefaults.PosHand);
            if (n < 0)
            {
                n = 0;
            }
            for (int i = 0; i < n; i++)
            {
                int uid = query.GetCardUniqueId(ownedSeat, CampaignCpuDefaults.PosHand, i);
                if (uid <= 0)
                {
                    continue;
                }
                int id = query.GetCardIdByUniqueId(uid);
                if (id > 0)
                {
                    obs.SelfHandCardIds.Add(id);
                }
            }
        }
    }
}

using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Thin DTO consumed by the pure scorer. No Llm* graph.
    /// </summary>
    sealed class CampaignCpuObservation
    {
        public int ChapterId;
        public ulong CampaignCpuViewSeq;
        public DuelViewType ViewType;
        public int ViewParam1;
        public int ViewParam2;
        public int ViewParam3;
        public int ActingPlayer;
        public int OwnedSeat;
        /// <summary>Local human seat (MyID). Required on production decision rows for S10 ownership gates.</summary>
        public int MyId;
        /// <summary>Monotonic duel generation from controller (segments multi-duel audits).</summary>
        public int DuelGeneration;
        public int Turn;
        public int TurnPlayer;
        public int Phase;
        public int SelfLp;
        public int OppLp;
        public int SelfMonsterCount;
        public int OppMonsterCount;
        public List<int> SelfHandCardIds;
        public List<int> SelfFieldFaceUpCardIds;
        public List<int> OppFieldFaceUpCardIds;
        public List<CampaignCpuLegalAction> LegalActions;
        public bool IsMainPhaseWaitInput;
        public bool IsMultiSelectList;
        public string WindowClass;
        /// <summary>
        /// True when hand/field card-id queries threw or produced an unusable partial list.
        /// Scorer must treat card-id when-predicates as failed (never match).
        /// </summary>
        public bool PredicateQueryFailed;
        public List<CampaignCpuMonsterTacticalState> TacticalMonsters;

        public CampaignCpuObservation()
        {
            SelfHandCardIds = new List<int>();
            SelfFieldFaceUpCardIds = new List<int>();
            OppFieldFaceUpCardIds = new List<int>();
            LegalActions = new List<CampaignCpuLegalAction>();
            WindowClass = "Unsupported";
            PredicateQueryFailed = false;
            TacticalMonsters = new List<CampaignCpuMonsterTacticalState>();
        }

        public static CampaignCpuLegalAction FromLegalAction(LegalAction action)
        {
            if (action == null)
            {
                return null;
            }
            return new CampaignCpuLegalAction
            {
                ActionId = action.ActionId,
                Kind = action.Kind,
                Command = action.Command,
                Phase = action.Phase,
                CardId = action.CardId,
                Position = action.Position,
                Index = action.Index,
                DialogResult = action.DialogResult,
                CancelDecide = action.CancelDecide,
                Label = action.ActionLabel ?? string.Empty,
                IsMechanical = action.IsMechanical,
                TargetScope = action.TargetScope ?? string.Empty,
                Player = action.Player,
            };
        }

        public static string FingerprintLegalActions(IList<CampaignCpuLegalAction> actions)
        {
            if (actions == null || actions.Count == 0)
            {
                return "empty";
            }
            var parts = new List<string>(actions.Count);
            for (int i = 0; i < actions.Count; i++)
            {
                CampaignCpuLegalAction a = actions[i];
                if (a == null)
                {
                    continue;
                }
                parts.Add(a.CanonicalIdentity);
            }
            parts.Sort(System.StringComparer.Ordinal);
            return string.Join(";", parts.ToArray());
        }
    }

    static class CampaignCpuObservationContext
    {
        public static void Stamp(
            CampaignCpuObservation observation,
            int myId,
            int duelGeneration)
        {
            if (observation == null)
            {
                return;
            }
            observation.MyId = myId;
            observation.DuelGeneration = duelGeneration;
        }
    }

    /// <summary>
    /// Compact tactical snapshot used for deterministic position safety.
    /// Unknown values remain unknown; callers must not synthesize zeros as proof.
    /// </summary>
    sealed class CampaignCpuMonsterTacticalState
    {
        public int Player;
        public int Position;
        public int Index;
        public int UniqueId;
        public int CardId;
        public bool FaceKnown;
        public bool FaceUp;
        public bool TurnKnown;
        public int TurnRaw;
        public bool IsAttack;
        public bool IsDefense;
        public bool HasAtk;
        public int Atk;
        public bool HasDef;
        public int Def;

    }

    /// <summary>
    /// Projects a DecisionSnapshot (boundary type from LegalActionExtractor) into
    /// CampaignCpuObservation. Pure: no DuelDll calls.
    /// </summary>
    static class CampaignCpuObservationProjector
    {
        public static CampaignCpuObservation Project(
            DecisionSnapshot snapshot,
            int chapterId,
            int ownedSeat,
            int actingPlayer,
            bool isMultiSelectList)
        {
            var obs = new CampaignCpuObservation
            {
                ChapterId = chapterId,
                OwnedSeat = ownedSeat,
                ActingPlayer = actingPlayer,
                IsMultiSelectList = isMultiSelectList,
            };

            if (snapshot == null)
            {
                obs.WindowClass = "Unsupported";
                return obs;
            }

            obs.CampaignCpuViewSeq = snapshot.RunEffectSeq;
            obs.ViewType = snapshot.ViewType;
            obs.ViewParam1 = snapshot.ViewParam1;
            obs.ViewParam2 = snapshot.ViewParam2;
            obs.ViewParam3 = snapshot.ViewParam3;
            obs.Turn = snapshot.Turn;
            obs.TurnPlayer = snapshot.TurnPlayer;
            obs.Phase = snapshot.CurrentPhase;
            obs.IsMainPhaseWaitInput = CampaignCpuWindowClassifier.IsMainPhaseWaitInput(
                snapshot.ViewType, snapshot.ViewParam1);
            obs.WindowClass = CampaignCpuWindowClassifier.ClassifyWindow(
                snapshot.ViewType, snapshot.ViewParam1);

            if (snapshot.LegalActions != null)
            {
                for (int i = 0; i < snapshot.LegalActions.Count; i++)
                {
                    CampaignCpuLegalAction row =
                        CampaignCpuObservation.FromLegalAction(snapshot.LegalActions[i]);
                    if (row != null)
                    {
                        obs.LegalActions.Add(row);
                    }
                }
            }

            if (snapshot.PublicState != null && snapshot.PublicState.Players != null)
            {
                for (int i = 0; i < snapshot.PublicState.Players.Count; i++)
                {
                    PublicPlayerState player = snapshot.PublicState.Players[i];
                    if (player == null)
                    {
                        continue;
                    }
                    bool isSelf = player.Player == ownedSeat;
                    if (isSelf)
                    {
                        obs.SelfLp = player.LifePoints;
                    }
                    else
                    {
                        obs.OppLp = player.LifePoints;
                    }

                    if (player.KnownCards != null)
                    {
                        for (int c = 0; c < player.KnownCards.Count; c++)
                        {
                            PublicKnownCard card = player.KnownCards[c];
                            if (card == null || card.CardId <= 0)
                            {
                                continue;
                            }
                            // Face-up field-ish: not hand (13), deck, etc.
                            bool isHand = card.Position == CampaignCpuDefaults.PosHand;
                            if (isHand)
                            {
                                if (isSelf)
                                {
                                    obs.SelfHandCardIds.Add(card.CardId);
                                }
                                continue;
                            }
                            // Monster zones 0-6, spell 7-12 typical MD layout.
                            bool isField = card.Position >= 0 && card.Position <= 12;
                            if (!isField)
                            {
                                continue;
                            }
                            if (isSelf)
                            {
                                obs.SelfFieldFaceUpCardIds.Add(card.CardId);
                                if (card.Position <= 6)
                                {
                                    obs.SelfMonsterCount++;
                                }
                            }
                            else
                            {
                                obs.OppFieldFaceUpCardIds.Add(card.CardId);
                                if (card.Position <= 6)
                                {
                                    obs.OppMonsterCount++;
                                }
                            }
                        }
                    }
                }
            }

            return obs;
        }
    }
}

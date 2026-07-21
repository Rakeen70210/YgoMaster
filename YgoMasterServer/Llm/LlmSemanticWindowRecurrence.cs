using System;
using System.Collections.Generic;
using System.Text;

namespace YgoMaster
{
    enum LlmSemanticWindowDisposition
    {
        UseBroker = 0,
        ReuseDecline = 1,
        TemporaryCpu = 2,
    }

    /// <summary>
    /// Pure, deterministic semantic fingerprint for optional-response decision windows.
    /// Intentionally excludes run_effect_seq and request-local action_id
    /// (YGOMASTER-LLM-003 Milestone 3C).
    /// </summary>
    static class LlmDecisionWindowSemanticFingerprint
    {
        public static string Build(DecisionSnapshot snapshot, int duelGeneration)
        {
            if (snapshot == null)
            {
                return "null";
            }

            StringBuilder sb = new StringBuilder(256);
            sb.Append("g=").Append(duelGeneration);
            sb.Append("|seat=").Append(snapshot.ActingPlayer);
            sb.Append("|view=").Append((int)snapshot.ViewType);
            sb.Append("|p1=").Append(snapshot.ViewParam1);
            sb.Append("|p2=").Append(snapshot.ViewParam2);
            sb.Append("|p3=").Append(snapshot.ViewParam3);
            sb.Append("|turn=").Append(snapshot.Turn);
            sb.Append("|tp=").Append(snapshot.TurnPlayer);
            sb.Append("|phase=").Append(snapshot.CurrentPhase);
            sb.Append("|step=").Append(snapshot.CurrentStep);
            sb.Append("|hist=").Append(GetHistoryHighWater(snapshot));
            sb.Append("|pub=").Append(BuildPublicStateDigest(snapshot));
            sb.Append("|acts=").Append(BuildLegalActionSemanticDigest(snapshot));
            return sb.ToString();
        }

        static long GetHistoryHighWater(DecisionSnapshot snapshot)
        {
            if (snapshot == null || snapshot.DuelHistory == null)
            {
                return 0;
            }
            // Last accepted public event id is the authoritative high-water mark.
            return (long)snapshot.DuelHistory.LastEventId;
        }

        static string BuildPublicStateDigest(DecisionSnapshot snapshot)
        {
            if (snapshot.PublicState == null || snapshot.PublicState.Players == null)
            {
                return "-";
            }
            StringBuilder sb = new StringBuilder();
            List<PublicPlayerState> players = new List<PublicPlayerState>(snapshot.PublicState.Players);
            players.Sort((a, b) => a.Player.CompareTo(b.Player));
            foreach (PublicPlayerState player in players)
            {
                if (player == null)
                {
                    continue;
                }
                sb.Append('p').Append(player.Player).Append(":lp=").Append(player.LifePoints);
                if (player.Positions != null)
                {
                    List<PublicPositionState> positions =
                        new List<PublicPositionState>(player.Positions);
                    positions.Sort((a, b) => a.Position.CompareTo(b.Position));
                    foreach (PublicPositionState pos in positions)
                    {
                        if (pos == null)
                        {
                            continue;
                        }
                        sb.Append(",z").Append(pos.Position).Append('=').Append(pos.Count);
                    }
                }
                if (player.KnownCards != null)
                {
                    List<string> known = new List<string>();
                    foreach (PublicKnownCard card in player.KnownCards)
                    {
                        if (card == null)
                        {
                            continue;
                        }
                        known.Add(
                            card.Position + ":" + card.Index + ":" + card.CardId + ":" + card.Face);
                    }
                    known.Sort(StringComparer.Ordinal);
                    foreach (string entry in known)
                    {
                        sb.Append(",k=").Append(entry);
                    }
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        static string BuildLegalActionSemanticDigest(DecisionSnapshot snapshot)
        {
            if (snapshot.LegalActions == null || snapshot.LegalActions.Count == 0)
            {
                return "-";
            }
            List<string> parts = new List<string>(snapshot.LegalActions.Count);
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action == null)
                {
                    continue;
                }
                // Exclude ActionId (request-local). Include stable semantic identity.
                StringBuilder part = new StringBuilder();
                part.Append(action.Kind);
                part.Append(':').Append(action.Command);
                part.Append(':').Append(action.Phase);
                part.Append(':').Append(action.Player);
                part.Append(':').Append(action.Position);
                part.Append(':').Append(action.Index);
                part.Append(':').Append(action.CardId);
                part.Append(':').Append(action.CardUniqueId);
                part.Append(':').Append(action.DialogResult);
                part.Append(':').Append(action.CancelDecide ? 1 : 0);
                part.Append(':').Append(action.IsMechanical ? 1 : 0);
                part.Append(':').Append(action.StrategicRole ?? "");
                part.Append(':').Append(action.TargetScope ?? "");
                part.Append(':').Append(action.ActionLabel ?? "");
                parts.Add(part.ToString());
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts.ToArray());
        }
    }

    /// <summary>
    /// Tracks cross-sequence optional-response recurrence after successful decline/pass.
    /// First occurrence: broker. Second identical no-progress: reuse decline.
    /// Third: temporary CPU with reason semantic_optional_response_loop.
    /// </summary>
    class LlmSemanticOptionalResponseTracker
    {
        public const string RecoveryReasonSemanticLoop = "semantic_optional_response_loop";

        string activeFingerprint = string.Empty;
        int successfulDeclineCommits;
        int duelGeneration = -1;
        LegalAction cachedDeclineTemplate;
        ulong originRunEffectSeq;
        string lastDispositionReason = string.Empty;

        public string ActiveFingerprint { get { return activeFingerprint; } }
        public int SuccessfulDeclineCommits { get { return successfulDeclineCommits; } }
        public ulong OriginRunEffectSeq { get { return originRunEffectSeq; } }
        public string LastRecoveryReason { get { return lastDispositionReason; } }

        public void Reset()
        {
            activeFingerprint = string.Empty;
            successfulDeclineCommits = 0;
            duelGeneration = -1;
            cachedDeclineTemplate = null;
            originRunEffectSeq = 0;
            lastDispositionReason = string.Empty;
        }

        public LlmSemanticWindowDisposition ObserveStrategicWindow(
            DecisionSnapshot snapshot,
            int currentDuelGeneration)
        {
            lastDispositionReason = string.Empty;
            if (snapshot == null)
            {
                return LlmSemanticWindowDisposition.UseBroker;
            }

            if (duelGeneration != currentDuelGeneration)
            {
                Reset();
                duelGeneration = currentDuelGeneration;
            }

            string fingerprint = LlmDecisionWindowSemanticFingerprint.Build(
                snapshot, currentDuelGeneration);
            if (string.IsNullOrEmpty(activeFingerprint) ||
                !string.Equals(activeFingerprint, fingerprint, StringComparison.Ordinal))
            {
                // Authoritative progress / new semantic window.
                activeFingerprint = fingerprint;
                successfulDeclineCommits = 0;
                cachedDeclineTemplate = null;
                originRunEffectSeq = snapshot.RunEffectSeq;
                return LlmSemanticWindowDisposition.UseBroker;
            }

            if (successfulDeclineCommits <= 0)
            {
                return LlmSemanticWindowDisposition.UseBroker;
            }
            if (successfulDeclineCommits == 1)
            {
                LegalAction currentDecline;
                if (TryMatchCachedDecline(snapshot, out currentDecline))
                {
                    lastDispositionReason = "reuse_prior_decline";
                    return LlmSemanticWindowDisposition.ReuseDecline;
                }
                // Decline no longer legal: treat as progress / fresh window.
                Reset();
                duelGeneration = currentDuelGeneration;
                activeFingerprint = fingerprint;
                originRunEffectSeq = snapshot.RunEffectSeq;
                return LlmSemanticWindowDisposition.UseBroker;
            }

            // successfulDeclineCommits >= 2 → third semantic occurrence.
            lastDispositionReason = RecoveryReasonSemanticLoop;
            return LlmSemanticWindowDisposition.TemporaryCpu;
        }

        public void RecordSuccessfulDeclineOrPass(
            DecisionSnapshot snapshot,
            LegalAction action,
            int currentDuelGeneration)
        {
            if (snapshot == null || action == null)
            {
                return;
            }
            if (!LlmBrokerRecovery.IsOptionalDeclineOrPass(action))
            {
                // Activations / targets / costs must never seed reuse.
                return;
            }
            if (action.Kind != LegalActionKind.Cancel &&
                action.Kind != LegalActionKind.DialogResult)
            {
                // Only decline/pass-shaped actions are reusable.
                if (!IsDeclineShaped(action))
                {
                    return;
                }
            }
            // Never cache activations even if mislabeled.
            if (action.Kind == LegalActionKind.Command)
            {
                return;
            }

            if (duelGeneration != currentDuelGeneration)
            {
                Reset();
                duelGeneration = currentDuelGeneration;
            }

            string fingerprint = LlmDecisionWindowSemanticFingerprint.Build(
                snapshot, currentDuelGeneration);
            if (!string.Equals(activeFingerprint, fingerprint, StringComparison.Ordinal))
            {
                activeFingerprint = fingerprint;
                successfulDeclineCommits = 0;
                originRunEffectSeq = snapshot.RunEffectSeq;
            }
            if (successfulDeclineCommits == 0)
            {
                originRunEffectSeq = snapshot.RunEffectSeq;
            }
            successfulDeclineCommits++;
            cachedDeclineTemplate = CloneActionTemplate(action);
        }

        public bool TryGetReusableDecline(
            DecisionSnapshot snapshot,
            out LegalAction currentDecline)
        {
            return TryMatchCachedDecline(snapshot, out currentDecline);
        }

        public bool ShouldPreserveHistoryAfterCommit(LegalAction action)
        {
            return action != null && LlmBrokerRecovery.IsOptionalDeclineOrPass(action);
        }

        static bool IsDeclineShaped(LegalAction action)
        {
            if (action == null)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(action.ActionLabel) &&
                action.ActionLabel.IndexOf("Decline", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return LlmBrokerRecovery.IsOptionalDeclineOrPass(action);
        }

        bool TryMatchCachedDecline(DecisionSnapshot snapshot, out LegalAction currentDecline)
        {
            currentDecline = null;
            if (snapshot == null || snapshot.LegalActions == null || cachedDeclineTemplate == null)
            {
                return false;
            }
            foreach (LegalAction candidate in snapshot.LegalActions)
            {
                if (candidate == null || !LlmBrokerRecovery.IsOptionalDeclineOrPass(candidate))
                {
                    continue;
                }
                if (LlmBrokerProtocol.IsSameAction(candidate, cachedDeclineTemplate) ||
                    IsSemanticallySameDecline(candidate, cachedDeclineTemplate))
                {
                    currentDecline = candidate;
                    return true;
                }
            }
            // Fall back: any legal optional decline when template was a cancel decline.
            if (cachedDeclineTemplate.Kind == LegalActionKind.Cancel)
            {
                foreach (LegalAction candidate in snapshot.LegalActions)
                {
                    if (candidate != null &&
                        candidate.Kind == LegalActionKind.Cancel &&
                        candidate.CancelDecide == cachedDeclineTemplate.CancelDecide)
                    {
                        currentDecline = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        static bool IsSemanticallySameDecline(LegalAction current, LegalAction cached)
        {
            if (current == null || cached == null)
            {
                return false;
            }
            if (current.Kind != cached.Kind)
            {
                return false;
            }
            if (current.Kind == LegalActionKind.Cancel)
            {
                return current.CancelDecide == cached.CancelDecide;
            }
            if (current.Kind == LegalActionKind.DialogResult)
            {
                return current.DialogResult == cached.DialogResult &&
                    current.DialogIsYesNoPrompt == cached.DialogIsYesNoPrompt;
            }
            return false;
        }

        static LegalAction CloneActionTemplate(LegalAction action)
        {
            return new LegalAction()
            {
                // ActionId intentionally omitted / zero — not part of semantic identity.
                Kind = action.Kind,
                Player = action.Player,
                Position = action.Position,
                Index = action.Index,
                Command = action.Command,
                Phase = action.Phase,
                CardUniqueId = action.CardUniqueId,
                CardId = action.CardId,
                DialogResult = action.DialogResult,
                DialogTextId = action.DialogTextId,
                DialogIsYesNoPrompt = action.DialogIsYesNoPrompt,
                CancelDecide = action.CancelDecide,
                IsMechanical = action.IsMechanical,
                StrategicRole = action.StrategicRole,
                TargetScope = action.TargetScope,
                ActionLabel = action.ActionLabel,
                ActionGroup = action.ActionGroup,
            };
        }
    }
}

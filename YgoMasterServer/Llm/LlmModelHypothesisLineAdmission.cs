using System;
using System.Collections.Generic;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Fail-closed admission of provider-authored model_hypothesis lines (Slice 6B).
    /// No score authority over grounded deterministic lines; no schema/provider changes.
    /// </summary>
    static class LlmModelHypothesisLineAdmission
    {
        public const string ReasonMissingLegalRoot = "missing_legal_root";
        public const string ReasonInvalidProvenance = "invalid_provenance";
        public const string ReasonMissingUncertainty = "missing_uncertainty";
        public const string ReasonOpponentHidden = "opponent_hidden_identity";

        public static bool TryAdmit(
            DecisionSnapshot snapshot,
            LlmSearchGraph graph,
            LlmSearchLine candidate,
            out LlmSearchLine admitted,
            out string reason)
        {
            admitted = null;
            reason = null;
            if (candidate == null)
            {
                reason = ReasonMissingLegalRoot;
                return false;
            }
            if (!string.Equals(candidate.Provenance, "model_hypothesis", StringComparison.Ordinal))
            {
                reason = ReasonInvalidProvenance;
                return false;
            }
            if (candidate.Uncertainty == null || candidate.Uncertainty.Count == 0)
            {
                reason = ReasonMissingUncertainty;
                return false;
            }
            if (snapshot == null || snapshot.LegalActions == null
                || !HasLegalRoot(snapshot, candidate.RootActionId))
            {
                reason = ReasonMissingLegalRoot;
                return false;
            }
            if (ContainsOpponentHidden(snapshot, graph, candidate))
            {
                reason = ReasonOpponentHidden;
                return false;
            }

            // Future steps must be non-commit / non-current.
            if (candidate.Steps != null)
            {
                for (int i = 1; i < candidate.Steps.Count; i++)
                {
                    LlmSearchStep step = candidate.Steps[i];
                    if (step == null)
                    {
                        continue;
                    }
                    if (step.CurrentLegal || step.CommitEligible)
                    {
                        reason = ReasonInvalidProvenance;
                        return false;
                    }
                }
            }

            int groundedScore = MaxGroundedScore(graph, candidate.RootActionId);
            LlmSearchLine copy = CloneLine(candidate);
            copy.Provenance = "model_hypothesis";
            copy.CommitEligible = false;
            // Cap Score to grounded deterministic authority; strip all candidate ScoreFeatures
            // so unknown keys cannot retain huge values.
            if (copy.Score > groundedScore)
            {
                copy.Score = groundedScore;
            }
            copy.ScoreFeatures = new Dictionary<string, int>();
            if (copy.Steps != null)
            {
                for (int i = 1; i < copy.Steps.Count; i++)
                {
                    if (copy.Steps[i] == null)
                    {
                        continue;
                    }
                    copy.Steps[i].CurrentLegal = false;
                    copy.Steps[i].CommitEligible = false;
                    copy.Steps[i].Provenance = "model_hypothesis";
                }
            }
            // Uncertainty already required non-empty above; do not invent a dead-branch fill.
            admitted = copy;
            reason = null;
            return true;
        }

        static bool HasLegalRoot(DecisionSnapshot snapshot, int rootActionId)
        {
            foreach (LegalAction a in snapshot.LegalActions)
            {
                if (a != null && a.ActionId == rootActionId)
                {
                    return true;
                }
            }
            return false;
        }

        static int MaxGroundedScore(LlmSearchGraph graph, int rootActionId)
        {
            int max = 0;
            if (graph == null || graph.Lines == null)
            {
                return max;
            }
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line == null || line.RootActionId != rootActionId)
                {
                    continue;
                }
                if (string.Equals(line.Provenance, "model_hypothesis", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.Score > max)
                {
                    max = line.Score;
                }
            }
            return max;
        }

        static bool ContainsOpponentHidden(
            DecisionSnapshot snapshot,
            LlmSearchGraph graph,
            LlmSearchLine candidate)
        {
            HashSet<int> allowedIds = CollectAllowedCardIds(snapshot, graph);
            HashSet<string> allowedNames = CollectAllowedNames(snapshot, graph);
            string blob = SerializeLineBlob(candidate);
            // Opponent known cards that are not controlled are forbidden if appear in hypothesis.
            if (snapshot != null && snapshot.PublicState != null && snapshot.PublicState.Players != null)
            {
                int controlled = snapshot.ControlledPlayer;
                foreach (PublicPlayerState p in snapshot.PublicState.Players)
                {
                    if (p == null || p.Player == controlled || p.KnownCards == null)
                    {
                        continue;
                    }
                    foreach (PublicKnownCard k in p.KnownCards)
                    {
                        if (k == null)
                        {
                            continue;
                        }
                        if (k.CardId > 0 && !allowedIds.Contains(k.CardId)
                            && blob.IndexOf(k.CardId.ToString(), StringComparison.Ordinal) >= 0)
                        {
                            return true;
                        }
                        string n = k.Card != null ? k.Card.Name : null;
                        if (!string.IsNullOrEmpty(n) && !allowedNames.Contains(n)
                            && blob.IndexOf(n, StringComparison.Ordinal) >= 0)
                        {
                            return true;
                        }
                    }
                }
            }
            // Heuristic: SENTINEL / HIDDEN_RIVAL / OPP_PRIVATE tokens
            if (blob.IndexOf("SENTINEL_OPP", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("HIDDEN_RIVAL", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("OPP_PRIVATE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return false;
        }

        static HashSet<int> CollectAllowedCardIds(DecisionSnapshot snapshot, LlmSearchGraph graph)
        {
            HashSet<int> ids = new HashSet<int>();
            if (snapshot != null && snapshot.LegalActions != null)
            {
                foreach (LegalAction a in snapshot.LegalActions)
                {
                    if (a == null)
                    {
                        continue;
                    }
                    if (a.CardId > 0)
                    {
                        ids.Add(a.CardId);
                    }
                    if (a.Card != null && a.Card.CardId > 0)
                    {
                        ids.Add(a.Card.CardId);
                    }
                }
            }
            LlmSelfResources self = graph != null ? graph.SourceSelfResources : null;
            if (self != null)
            {
                AddSelfIds(ids, self.Hand);
                AddSelfIds(ids, self.Field);
                AddSelfIds(ids, self.Graveyard);
                AddSelfIds(ids, self.Banished);
                if (self.ExtraDeck != null)
                {
                    foreach (LlmSelfResourceExtraDeckEntry e in self.ExtraDeck)
                    {
                        if (e != null && e.CardId > 0)
                        {
                            ids.Add(e.CardId);
                        }
                    }
                }
            }
            return ids;
        }

        static void AddSelfIds(HashSet<int> ids, IList<LlmSelfResourceCard> cards)
        {
            if (cards == null)
            {
                return;
            }
            foreach (LlmSelfResourceCard c in cards)
            {
                if (c != null && c.CardId > 0)
                {
                    ids.Add(c.CardId);
                }
            }
        }

        static HashSet<string> CollectAllowedNames(DecisionSnapshot snapshot, LlmSearchGraph graph)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            if (snapshot != null && snapshot.LegalActions != null)
            {
                foreach (LegalAction a in snapshot.LegalActions)
                {
                    if (a != null && a.Card != null && !string.IsNullOrEmpty(a.Card.Name))
                    {
                        names.Add(a.Card.Name);
                    }
                }
            }
            return names;
        }

        static string SerializeLineBlob(LlmSearchLine line)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(line.LineId).Append('|').Append(line.Provenance);
            if (line.Steps != null)
            {
                foreach (LlmSearchStep s in line.Steps)
                {
                    if (s != null)
                    {
                        sb.Append('|').Append(s.Label);
                    }
                }
            }
            if (line.Unlocks != null)
            {
                foreach (string u in line.Unlocks)
                {
                    sb.Append('|').Append(u);
                }
            }
            return sb.ToString();
        }

        static LlmSearchLine CloneLine(LlmSearchLine src)
        {
            LlmSearchLine copy = new LlmSearchLine()
            {
                LineId = src.LineId,
                RootActionId = src.RootActionId,
                Provenance = src.Provenance,
                CommitEligible = src.CommitEligible,
                StrategicDepth = src.StrategicDepth,
                Score = src.Score,
                Fingerprint = src.Fingerprint,
                IsRootShell = src.IsRootShell,
                Boundary = src.Boundary,
                TemplateId = src.TemplateId,
                SummonRequirement = src.SummonRequirement,
            };
            if (src.Steps != null)
            {
                foreach (LlmSearchStep s in src.Steps)
                {
                    if (s == null)
                    {
                        continue;
                    }
                    copy.Steps.Add(new LlmSearchStep()
                    {
                        Label = s.Label,
                        CurrentLegal = s.CurrentLegal,
                        CommitEligible = s.CommitEligible,
                        Provenance = s.Provenance,
                        Fingerprint = s.Fingerprint,
                    });
                }
            }
            if (src.Uncertainty != null)
            {
                foreach (string u in src.Uncertainty)
                {
                    copy.Uncertainty.Add(u);
                }
            }
            if (src.ScoreFeatures != null)
            {
                foreach (KeyValuePair<string, int> kv in src.ScoreFeatures)
                {
                    copy.ScoreFeatures[kv.Key] = kv.Value;
                }
            }
            return copy;
        }
    }
}

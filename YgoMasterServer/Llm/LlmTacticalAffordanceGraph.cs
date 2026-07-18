using System;
using System.Collections.Generic;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Layer A root-complete tactical affordance graph builder (YGOMASTER-LLM-005 Slice 2A).
    /// Emits mandatory depth-zero root shells and records filtered-root exclusions.
    /// Continuations (Rank-4, battle, Main2) are expanded by LlmBoundedLineSearch.
    /// </summary>
    static class LlmTacticalAffordanceGraph
    {
        internal const string ProvenanceEngineCurrent = "engine_current";
        internal const string ProvenanceRulesInferred = "rules_inferred";
        internal const string BoundaryOpponent = "opponent_response_or_fresh_engine_window";
        internal const string BoundaryUnsupportedMulti = "unsupported_multi_select";
        internal const string BoundaryUnknownDialog = "unknown_card_semantics";
        internal const string BoundaryRandom = "random_outcome";
        internal const string BoundaryDepthBudget = "depth_budget";
        internal const string BoundaryNodeBudget = "node_budget";
        internal const string BoundaryTimeBudget = "time_budget";
        internal const string BoundaryTransposition = "transposition";

        public static LlmSearchGraph Build(
            DecisionSnapshot snapshot,
            LlmSelfResources selfResources,
            LlmSearchLimits limits)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }
            if (limits == null)
            {
                limits = LlmSearchLimits.CreateDefault();
            }
            else
            {
                // Clamp expansion dimensions only. MaxSerializedBytes is validated strictly by
                // Project/TryBuild (fail closed below MinimumSupported) rather than clamped here.
                ILlmSearchBudgetClock clockPreserve = limits.Clock;
                limits = limits.Clone();
                limits.Clock = clockPreserve;
                limits.MaxStrategicDepth = Math.Max(
                    LlmSearchLimits.MinMaxStrategicDepth,
                    Math.Min(limits.MaxStrategicDepth, LlmSearchLimits.HardMaxStrategicDepth));
                limits.MaxNodes = Math.Max(
                    LlmSearchLimits.MinMaxNodes,
                    Math.Min(limits.MaxNodes, LlmSearchLimits.HardMaxNodes));
                if (limits.BeamWidth <= 0)
                {
                    limits.BeamWidth = LlmSearchLimits.DefaultBeamWidth;
                }
                limits.BeamWidth = Math.Max(
                    LlmSearchLimits.MinBeamWidth,
                    Math.Min(limits.BeamWidth, LlmSearchLimits.HardMaxBeamWidth));
                if (limits.MaxWallMs <= 0)
                {
                    limits.MaxWallMs = LlmSearchLimits.DefaultMaxWallMs;
                }
                limits.MaxWallMs = Math.Max(
                    LlmSearchLimits.MinMaxWallMs,
                    Math.Min(limits.MaxWallMs, LlmSearchLimits.HardMaxWallMs));
            }

            ILlmSearchBudgetClock clock = limits.Clock ?? new LlmSearchSystemBudgetClock();

            LlmSearchGraph graph = new LlmSearchGraph()
            {
                SearchId = "seq:" + snapshot.RunEffectSeq.ToString() + ":generation:0",
                RootRunEffectSeq = (long)snapshot.RunEffectSeq,
                Limits = limits.Clone(),
                Status = "budget_complete",
                SourceSnapshot = snapshot,
                SourceSelfResources = selfResources,
            };
            graph.Limits.Clock = limits.Clock;

            List<LegalAction> strategicRoots = new List<LegalAction>();
            if (snapshot.LegalActions != null)
            {
                foreach (LegalAction action in snapshot.LegalActions)
                {
                    if (action == null)
                    {
                        continue;
                    }
                    if (action.IsMechanical)
                    {
                        // Plan: count every filtered mechanical with stable reason.
                        string mechanicalReason = MechanicalExclusionReason(action);
                        graph.Coverage.RootExclusions.Add(
                            "action:" + action.ActionId + ":" + mechanicalReason);
                        graph.Coverage.ExclusionReasons.Add(mechanicalReason);
                        continue;
                    }
                    // Non-strategic filtered kinds still get explicit exclusion reasons.
                    if (IsFilteredNonStrategic(action, out string exclusion))
                    {
                        graph.Coverage.RootExclusions.Add(
                            "action:" + action.ActionId + ":" + exclusion);
                        graph.Coverage.ExclusionReasons.Add(exclusion);
                        continue;
                    }
                    strategicRoots.Add(action);
                }
            }

            graph.Coverage.LegalRootActions = strategicRoots.Count;

            List<LlmSearchLine> lines = new List<LlmSearchLine>();
            int boundaryNodes = 0;
            foreach (LegalAction root in strategicRoots)
            {
                LlmSearchLine rootLine = CreateEngineCurrentRootLine(root, selfResources);
                lines.Add(rootLine);
                if (IsBoundaryOnlyRoot(root))
                {
                    boundaryNodes++;
                }
            }

            graph.Lines = lines;
            graph.Coverage.RootShellCount = lines.Count;
            graph.Coverage.RepresentedRootActions = strategicRoots.Count;
            graph.Coverage.ExpandedNodes = 0;
            graph.Coverage.ContinuationExpansionCount = 0;
            graph.Coverage.BoundaryNodes = boundaryNodes;
            graph.Coverage.ElapsedMs = clock.ElapsedMilliseconds;
            // Slice 2B: attach immediate-outcome annotations and apply value-zero score features
            // before continuations expand. Does not select or suppress roots.
            LlmImmediateOutcomeAnalyzer.AnnotateSearchGraph(graph);
            // Continuations expanded by LlmBoundedLineSearch.Search (called by BuildAndSearch
            // and production audit). Do not Freeze here so Search can mutate.
            return graph;
        }

        internal static LlmSearchLine CreateEngineCurrentRootLine(
            LegalAction root,
            LlmSelfResources selfResources)
        {
            string label = !string.IsNullOrEmpty(root.ActionLabel)
                ? root.ActionLabel
                : DescribeAction(root);

            LlmSearchLine line = new LlmSearchLine()
            {
                LineId = "line:root:" + root.ActionId,
                RootActionId = root.ActionId,
                Provenance = ProvenanceEngineCurrent,
                CommitEligible = true,
                StrategicDepth = 0,
                IsRootShell = true,
                Score = ScoreRootShell(root),
                Boundary = BoundaryOpponent,
                Fingerprint = FingerprintRoot(root),
            };
            line.Steps.Add(new LlmSearchStep()
            {
                Label = label,
                CurrentLegal = true,
                CommitEligible = true,
                Provenance = ProvenanceEngineCurrent,
                Fingerprint = line.Fingerprint,
            });
            line.Uncertainty.Add("receding_horizon_requires_fresh_engine_window");

            if (IsUnsupportedMultiSelect(root))
            {
                line.Boundary = BoundaryUnsupportedMulti;
                line.Uncertainty.Add("unsupported multi-select is a boundary, not a guessed continuation");
            }
            else if (IsUnknownOrMandatoryPrompt(root))
            {
                line.Boundary = BoundaryUnknownDialog;
                line.Uncertainty.Add("unknown or mandatory selection surface; stop without guessed continuation");
            }
            return line;
        }

        internal static bool IsMaterialSetupAction(LegalAction root)
        {
            if (root == null || root.Kind != LegalActionKind.Command)
            {
                return false;
            }
            // SetMonst face-down cannot create Xyz material — excluded by design review.
            return root.Command == DuelCommandType.Reverse
                || root.Command == DuelCommandType.Summon;
        }

        /// <summary>
        /// Root-specific grounded Level 4 check. Never inherits another card's material facts.
        /// </summary>
        internal static bool RootHasGroundedLevel4Material(LegalAction root)
        {
            if (!IsMaterialSetupAction(root))
            {
                return false;
            }
            if (root.Card == null)
            {
                return false;
            }
            if (root.Card.IsExtraDeck)
            {
                return false;
            }
            if (root.Card.Level != 4)
            {
                return false;
            }
            return true;
        }

        internal static bool IsUnsupportedMultiSelect(LegalAction root)
        {
            if (root == null)
            {
                return false;
            }
            if (root.ListIsMultiMode != 0)
            {
                return true;
            }
            if (root.Kind == LegalActionKind.ListIndex
                && root.ListSelectMax > 1
                && root.ListSelectMax > root.ListSelectMin)
            {
                string label = root.ActionLabel ?? string.Empty;
                if (label.IndexOf("Multi", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool IsUnknownOrMandatoryPrompt(LegalAction root)
        {
            if (root == null)
            {
                return false;
            }
            if (root.Kind == LegalActionKind.DialogResult && root.DialogTextId == 0
                && string.IsNullOrEmpty(root.ActionLabel))
            {
                return true;
            }
            string role = root.StrategicRole ?? string.Empty;
            if (role.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return false;
        }

        internal static bool IsBattlePhaseRoot(LegalAction root)
        {
            return root != null
                && root.Kind == LegalActionKind.MovePhase
                && root.Phase == DuelPhase.Battle;
        }

        internal static bool IsEndPhaseRoot(LegalAction root)
        {
            return root != null
                && root.Kind == LegalActionKind.MovePhase
                && root.Phase == DuelPhase.End;
        }

        internal static bool IsAttackRoot(LegalAction root)
        {
            return root != null
                && root.Kind == LegalActionKind.Command
                && root.Command == DuelCommandType.Attack;
        }

        static bool IsFilteredNonStrategic(LegalAction action, out string reason)
        {
            reason = null;
            if (action.Kind == LegalActionKind.Command)
            {
                if (action.Command == DuelCommandType.Look)
                {
                    reason = "filtered_look";
                    return true;
                }
                if (action.Command == DuelCommandType.Surrender)
                {
                    reason = "filtered_surrender";
                    return true;
                }
                if (action.Command == DuelCommandType.Draw)
                {
                    reason = "filtered_forced_draw";
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Stable exclusion reason for mechanical actions:
        /// filtered_mechanical:&lt;kind&gt;/&lt;scope&gt;
        /// </summary>
        internal static string MechanicalExclusionReason(LegalAction action)
        {
            if (action == null)
            {
                return "filtered_mechanical:null/null";
            }
            string kind = action.Kind.ToString();
            string scope;
            switch (action.Kind)
            {
                case LegalActionKind.Command:
                    scope = action.Command.ToString();
                    break;
                case LegalActionKind.MovePhase:
                    scope = action.Phase.ToString();
                    break;
                case LegalActionKind.DialogResult:
                    scope = action.DialogIsYesNoPrompt ? "yes_no" : "dialog";
                    break;
                case LegalActionKind.ListIndex:
                    scope = action.IsEffectTargetSelection ? "effect_target" : "list";
                    break;
                case LegalActionKind.Cancel:
                    scope = action.CancelDecide ? "cancel_decide" : "cancel";
                    break;
                default:
                    scope = "unknown";
                    break;
            }
            return "filtered_mechanical:" + kind + "/" + scope;
        }

        static bool IsBoundaryOnlyRoot(LegalAction root)
        {
            return IsUnsupportedMultiSelect(root) || IsUnknownOrMandatoryPrompt(root);
        }

        static int ScoreRootShell(LegalAction root)
        {
            // Deterministic low scores for ordering; continuations score higher when they unlock.
            if (IsBattlePhaseRoot(root) || IsAttackRoot(root))
            {
                return 10;
            }
            if (IsMaterialSetupAction(root))
            {
                return 5;
            }
            return 1;
        }

        internal static string FingerprintRoot(LegalAction root)
        {
            if (root == null)
            {
                return "root:null";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("root:");
            sb.Append(root.ActionId);
            sb.Append(':');
            sb.Append(root.Kind);
            sb.Append(':');
            sb.Append(root.Command);
            sb.Append(':');
            sb.Append(root.Phase);
            sb.Append(':');
            sb.Append(root.CardId);
            sb.Append(':');
            sb.Append(root.Position);
            return sb.ToString();
        }

        /// <summary>
        /// Allowed-information state fingerprint for transposition (plan §Transposition).
        /// Never includes root ActionId or opponent-hidden identities/deck order.
        /// Includes root semantic effect/card identity so distinct resulting states do not collapse.
        /// </summary>
        internal static string FingerprintAllowedInformationState(
            DecisionSnapshot snapshot,
            LlmSelfResources self,
            LegalAction appliedRoot,
            string continuationKind)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("ais|");
            sb.Append(continuationKind ?? "none");
            sb.Append('|');
            AppendRootSemanticKey(sb, appliedRoot);
            sb.Append('|');
            // turn / phase / step / acting player
            int turn = snapshot != null ? snapshot.Turn : (self != null ? self.Turn : 0);
            int phase = snapshot != null
                ? snapshot.CurrentPhase
                : (self != null ? self.CurrentPhase : 0);
            int step = snapshot != null
                ? snapshot.CurrentStep
                : (self != null ? self.CurrentStep : 0);
            int actor = snapshot != null ? snapshot.ActingPlayer : 0;
            sb.Append("t:").Append(turn);
            sb.Append("/ph:").Append(phase);
            sb.Append("/st:").Append(step);
            sb.Append("/act:").Append(actor);
            AppendPublicLifePoints(sb, snapshot);
            AppendControlledSelfResources(sb, self, appliedRoot);
            AppendPublicBoard(sb, snapshot);
            AppendTurnMemoryFlags(sb, snapshot);
            AppendHistoryCursor(sb, snapshot);
            return sb.ToString();
        }

        internal static string FingerprintContinuation(
            LegalAction root,
            string continuationKind,
            LlmSelfResources self,
            DecisionSnapshot snapshot)
        {
            return FingerprintAllowedInformationState(snapshot, self, root, continuationKind);
        }

        /// <summary>
        /// Root semantic key only (kind/command/phase/card identity/zones). Never ActionId.
        /// </summary>
        internal static string RootSemanticKey(LegalAction root)
        {
            StringBuilder sb = new StringBuilder();
            AppendRootSemanticKey(sb, root);
            return sb.ToString();
        }

        static void AppendRootSemanticKey(StringBuilder sb, LegalAction root)
        {
            if (root == null)
            {
                sb.Append("root:null");
                return;
            }
            int cardId = root.CardId;
            if (cardId <= 0 && root.Card != null)
            {
                cardId = root.Card.CardId;
            }
            int level = root.Card != null ? root.Card.Level : 0;
            sb.Append("root:");
            sb.Append(root.Kind);
            sb.Append('/');
            sb.Append(root.Command);
            sb.Append('/');
            sb.Append(root.Phase);
            sb.Append("/cid:").Append(cardId);
            sb.Append("/lv:").Append(level);
            sb.Append("/pos:").Append(root.Position);
            sb.Append('/').Append(root.Index);
            sb.Append("/dlg:").Append(root.DialogResult);
            sb.Append('/').Append(root.DialogTextId);
        }

        static void AppendPublicLifePoints(StringBuilder sb, DecisionSnapshot snapshot)
        {
            sb.Append("/lp:");
            int lp0 = 0;
            int lp1 = 0;
            if (snapshot != null && snapshot.PublicState != null && snapshot.PublicState.Players != null)
            {
                foreach (PublicPlayerState player in snapshot.PublicState.Players)
                {
                    if (player == null)
                    {
                        continue;
                    }
                    if (player.Player == 0)
                    {
                        lp0 = player.LifePoints;
                    }
                    else if (player.Player == 1)
                    {
                        lp1 = player.LifePoints;
                    }
                }
            }
            sb.Append(lp0).Append(',').Append(lp1);
        }

        static void AppendControlledSelfResources(
            StringBuilder sb,
            LlmSelfResources self,
            LegalAction appliedRoot)
        {
            sb.Append("/self{");
            if (self == null)
            {
                sb.Append('}');
                return;
            }
            sb.Append("h:");
            AppendSelfCardList(sb, self.Hand, appliedRoot, faceUpBanishedOnly: false, applyRootFace: false);
            sb.Append(";f:");
            AppendSelfCardList(sb, self.Field, appliedRoot, faceUpBanishedOnly: false, applyRootFace: true);
            sb.Append(";g:");
            AppendSelfCardList(sb, self.Graveyard, appliedRoot, faceUpBanishedOnly: false, applyRootFace: false);
            sb.Append(";b:");
            AppendSelfCardList(sb, self.Banished, appliedRoot, faceUpBanishedOnly: true, applyRootFace: false);
            sb.Append(";e:");
            AppendSelfExtraList(sb, self.ExtraDeck);
            sb.Append('}');
        }

        static void AppendSelfCardList(
            StringBuilder sb,
            IList<LlmSelfResourceCard> cards,
            LegalAction appliedRoot,
            bool faceUpBanishedOnly,
            bool applyRootFace)
        {
            if (cards == null || cards.Count == 0)
            {
                sb.Append('-');
                return;
            }
            List<string> keys = new List<string>();
            foreach (LlmSelfResourceCard card in cards)
            {
                if (card == null)
                {
                    continue;
                }
                bool faceUp = card.IsFaceUp;
                if (faceUpBanishedOnly && !faceUp)
                {
                    continue;
                }
                if (applyRootFace && appliedRoot != null
                    && appliedRoot.Kind == LegalActionKind.Command
                    && (appliedRoot.Command == DuelCommandType.Reverse
                        || appliedRoot.Command == DuelCommandType.Summon))
                {
                    int rootCid = appliedRoot.CardId > 0
                        ? appliedRoot.CardId
                        : (appliedRoot.Card != null ? appliedRoot.Card.CardId : 0);
                    if (rootCid > 0 && card.CardId == rootCid)
                    {
                        faceUp = true;
                    }
                }
                keys.Add(
                    card.CardId + ":" + card.Zone + ":" + card.Index + ":"
                    + (faceUp ? 1 : 0) + ":"
                    + (card.Level.HasValue ? card.Level.Value : -1) + ":"
                    + (card.Rank.HasValue ? card.Rank.Value : -1) + ":"
                    + (card.Frame ?? string.Empty) + ":"
                    + (card.SummonFamily ?? string.Empty));
            }
            keys.Sort(StringComparer.Ordinal);
            if (keys.Count == 0)
            {
                sb.Append('-');
                return;
            }
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(keys[i]);
            }
        }

        static void AppendSelfExtraList(StringBuilder sb, IList<LlmSelfResourceExtraDeckEntry> extra)
        {
            if (extra == null || extra.Count == 0)
            {
                sb.Append('-');
                return;
            }
            List<string> keys = new List<string>();
            foreach (LlmSelfResourceExtraDeckEntry entry in extra)
            {
                if (entry == null)
                {
                    continue;
                }
                keys.Add(
                    entry.CardId + ":"
                    + (entry.Rank.HasValue ? entry.Rank.Value : -1) + ":"
                    + (entry.Level.HasValue ? entry.Level.Value : -1) + ":"
                    + (entry.Frame ?? string.Empty) + ":"
                    + (entry.SummonFamily ?? string.Empty) + ":"
                    + (entry.DuplicateCount > 0 ? entry.DuplicateCount : 1));
            }
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(keys[i]);
            }
        }

        static void AppendPublicBoard(StringBuilder sb, DecisionSnapshot snapshot)
        {
            sb.Append("/pub{");
            if (snapshot == null || snapshot.PublicState == null || snapshot.PublicState.Players == null)
            {
                sb.Append('}');
                return;
            }
            List<string> posKeys = new List<string>();
            List<string> knownKeys = new List<string>();
            foreach (PublicPlayerState player in snapshot.PublicState.Players)
            {
                if (player == null)
                {
                    continue;
                }
                if (player.Positions != null)
                {
                    foreach (PublicPositionState pos in player.Positions)
                    {
                        if (pos == null)
                        {
                            continue;
                        }
                        posKeys.Add(player.Player + ":" + pos.Position + ":" + pos.Count);
                    }
                }
                if (player.KnownCards != null)
                {
                    foreach (PublicKnownCard card in player.KnownCards)
                    {
                        if (card == null)
                        {
                            continue;
                        }
                        knownKeys.Add(
                            card.Player + ":" + card.Position + ":" + card.Index + ":"
                            + card.CardId + ":" + card.Face);
                    }
                }
            }
            posKeys.Sort(StringComparer.Ordinal);
            knownKeys.Sort(StringComparer.Ordinal);
            sb.Append("pos:");
            AppendJoined(sb, posKeys);
            sb.Append(";id:");
            AppendJoined(sb, knownKeys);
            sb.Append('}');
        }

        static void AppendTurnMemoryFlags(StringBuilder sb, DecisionSnapshot snapshot)
        {
            sb.Append("/tm:");
            if (snapshot == null || snapshot.TurnMemory == null)
            {
                sb.Append('-');
                return;
            }
            LlmTurnMemoryState tm = snapshot.TurnMemory;
            sb.Append(tm.NormalSummonUsed ? 1 : 0);
            sb.Append(':');
            List<string> used = new List<string>();
            if (tm.CardsUsedThisTurn != null)
            {
                foreach (LlmUsedCardMemory card in tm.CardsUsedThisTurn)
                {
                    if (card != null)
                    {
                        used.Add(card.CardId.ToString());
                    }
                }
            }
            used.Sort(StringComparer.Ordinal);
            AppendJoined(sb, used);
        }

        static void AppendHistoryCursor(StringBuilder sb, DecisionSnapshot snapshot)
        {
            sb.Append("/hist:");
            if (snapshot == null || snapshot.DuelHistory == null)
            {
                sb.Append("0:0:0");
                return;
            }
            LlmDuelHistoryState hist = snapshot.DuelHistory;
            int eventCount = 0;
            try
            {
                if (hist.Events != null)
                {
                    eventCount = hist.Events.Count;
                }
            }
            catch
            {
                eventCount = 0;
            }
            sb.Append(hist.HistoryVersion);
            sb.Append(':');
            sb.Append(hist.LastEventId);
            sb.Append(':');
            sb.Append(eventCount);
        }

        static void AppendJoined(StringBuilder sb, List<string> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                sb.Append('-');
                return;
            }
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(keys[i]);
            }
        }

        /// <summary>
        /// Root-specific Rank 4 continuation after this setup action (no global candidacy inheritance).
        /// </summary>
        internal static bool RootMeetsRank4MaterialAfterRoot(LlmSelfResources self, LegalAction root)
        {
            if (!RootHasGroundedLevel4Material(root) || CountRank4Extra(self) <= 0)
            {
                return false;
            }
            int fu = CountFaceUpLevel4(self);
            if (root.Command == DuelCommandType.Reverse || root.Command == DuelCommandType.Summon)
            {
                return fu + 1 >= 2;
            }
            return false;
        }

        internal static int CountFaceUpLevel4(LlmSelfResources self)
        {
            int n = 0;
            if (self == null || self.Field == null)
            {
                return 0;
            }
            foreach (LlmSelfResourceCard card in self.Field)
            {
                if (card != null && !card.IsExtraDeck && card.IsFaceUp
                    && card.Level.HasValue && card.Level.Value == 4)
                {
                    n++;
                }
            }
            return n;
        }

        internal static int CountFaceDownLevel4(LlmSelfResources self)
        {
            int n = 0;
            if (self == null || self.Field == null)
            {
                return 0;
            }
            foreach (LlmSelfResourceCard card in self.Field)
            {
                if (card != null && !card.IsExtraDeck && !card.IsFaceUp
                    && card.Level.HasValue && card.Level.Value == 4)
                {
                    n++;
                }
            }
            return n;
        }

        internal static int CountRank4Extra(LlmSelfResources self)
        {
            int n = 0;
            if (self == null || self.ExtraDeck == null)
            {
                return 0;
            }
            foreach (LlmSelfResourceExtraDeckEntry entry in self.ExtraDeck)
            {
                if (entry != null && IsRank4ExtraCandidate(entry))
                {
                    n += entry.DuplicateCount > 0 ? entry.DuplicateCount : 1;
                }
            }
            return n;
        }

        internal static bool IsRank4ExtraCandidate(LlmSelfResourceExtraDeckEntry entry)
        {
            if (entry == null)
            {
                return false;
            }
            int rankOrLevel = entry.Rank.HasValue
                ? entry.Rank.Value
                : (entry.Level.HasValue ? entry.Level.Value : 0);
            if (rankOrLevel != 4)
            {
                return false;
            }
            if (string.Equals(entry.SummonFamily, "xyz", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Frame, "Xyz", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Frame, "XyzPend", StringComparison.OrdinalIgnoreCase)
                || entry.UsesRank)
            {
                return true;
            }
            if (string.Equals(entry.Kind, "Xyz", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Kind, "Monster", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(entry.SummonFamily))
            {
                return true;
            }
            return false;
        }

        internal static string AnalyzeRank4NoCandidateReason(LlmSelfResources self)
        {
            if (self == null)
            {
                return "missing_metadata";
            }
            bool anyLevel = false;
            int fu = 0;
            int fd = 0;
            if (self.Field != null)
            {
                foreach (LlmSelfResourceCard card in self.Field)
                {
                    if (card == null || card.IsExtraDeck)
                    {
                        continue;
                    }
                    if (card.Level.HasValue && card.Level.Value > 0)
                    {
                        anyLevel = true;
                    }
                    if (card.Level.HasValue && card.Level.Value == 4)
                    {
                        if (card.IsFaceUp)
                        {
                            fu++;
                        }
                        else
                        {
                            fd++;
                        }
                    }
                }
            }
            int r4 = CountRank4Extra(self);
            if (self.Field != null && self.Field.Count > 0 && !anyLevel)
            {
                return "missing_metadata";
            }
            if (r4 <= 0)
            {
                return "empty_extra_deck";
            }
            int afterFlip = fu + (fd > 0 ? 1 : 0);
            if (Math.Max(fu, afterFlip) < 2)
            {
                return "insufficient_materials";
            }
            return null;
        }

        internal static bool HasRank4Candidacy(LlmSelfResources self)
        {
            return string.IsNullOrEmpty(AnalyzeRank4NoCandidateReason(self))
                && CountRank4Extra(self) > 0
                && (CountFaceUpLevel4(self) + CountFaceDownLevel4(self)) >= 1;
        }

        internal static string DescribeAction(LegalAction action)
        {
            if (action == null)
            {
                return "action";
            }
            if (action.Kind == LegalActionKind.MovePhase)
            {
                return "Enter " + action.Phase.ToString() + " Phase";
            }
            if (action.Kind == LegalActionKind.Command)
            {
                return action.Command.ToString();
            }
            return action.Kind.ToString();
        }

        internal static string FirstRank4ExtraName(LlmSelfResources self)
        {
            if (self == null || self.ExtraDeck == null)
            {
                return null;
            }
            foreach (LlmSelfResourceExtraDeckEntry entry in self.ExtraDeck)
            {
                if (entry != null && IsRank4ExtraCandidate(entry))
                {
                    return entry.Name;
                }
            }
            return null;
        }

        // -----------------------------------------------------------------
        // Synchro candidacy (YGOMASTER-LLM-005 Slice 2C)
        // -----------------------------------------------------------------

        /// <summary>
        /// Grounded face-up materials after applying a Normal/Flip setup root.
        /// Reverse: only root Position+Index becomes face-up (fail closed if ungrounded).
        /// Summon: root is always appended as a distinct source even if same CardId is on field.
        /// </summary>
        internal static List<LlmSynchroMaterial> BuildPostRootFaceUpMaterials(
            LlmSelfResources self,
            LegalAction root)
        {
            List<LlmSynchroMaterial> materials = new List<LlmSynchroMaterial>();
            int rootCid = RootCardId(root);
            bool isReverse = root != null
                && root.Kind == LegalActionKind.Command
                && root.Command == DuelCommandType.Reverse;
            bool isSummon = root != null
                && root.Kind == LegalActionKind.Command
                && root.Command == DuelCommandType.Summon;

            bool reverseTargetGrounded = false;
            if (self != null && self.Field != null)
            {
                foreach (LlmSelfResourceCard card in self.Field)
                {
                    if (card == null || card.IsExtraDeck)
                    {
                        continue;
                    }
                    bool faceUp = card.IsFaceUp;
                    if (isReverse && rootCid > 0
                        && card.CardId == rootCid
                        && card.Zone == root.Position
                        && card.Index == root.Index)
                    {
                        // Exact Position+Index target only.
                        faceUp = true;
                        reverseTargetGrounded = true;
                        materials.Add(LlmSynchroMaterial.FromFieldCard(card, faceUpOverride: true));
                        continue;
                    }
                    if (isReverse
                        && rootCid > 0
                        && card.CardId == rootCid
                        && !card.IsFaceUp)
                    {
                        // Other face-down copies of the same CardId stay face-down — never promote.
                        continue;
                    }
                    if (!faceUp)
                    {
                        continue;
                    }
                    materials.Add(LlmSynchroMaterial.FromFieldCard(card, faceUpOverride: null));
                }
            }

            // Fail closed: Reverse without a grounded Position+Index target adds no reverse material.
            if (isReverse && !reverseTargetGrounded)
            {
                // materials already exclude ungrounded reverse promotions
            }

            if (isSummon && root != null && root.Card != null && rootCid > 0)
            {
                // Always add the summoned root as a distinct material source. An existing
                // face-up field copy with the same CardId is a different physical copy.
                materials.Add(LlmSynchroMaterial.FromRootCard(root));
            }

            materials.Sort(CompareSynchroMaterialBySource);
            return materials;
        }

        internal static bool IsSynchroExtraCandidate(LlmSelfResourceExtraDeckEntry entry)
        {
            if (entry == null)
            {
                return false;
            }
            if (!entry.Level.HasValue || entry.Level.Value <= 0)
            {
                return false;
            }
            if (entry.UsesRank)
            {
                return false;
            }
            if (string.Equals(entry.SummonFamily, "synchro", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Frame, "Synchro", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Kind, "Synchro", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        internal static int CountSynchroExtra(LlmSelfResources self)
        {
            int n = 0;
            if (self == null || self.ExtraDeck == null)
            {
                return 0;
            }
            foreach (LlmSelfResourceExtraDeckEntry entry in self.ExtraDeck)
            {
                if (entry != null && IsSynchroExtraCandidate(entry))
                {
                    n += entry.DuplicateCount > 0 ? entry.DuplicateCount : 1;
                }
            }
            return n;
        }

        /// <summary>
        /// Whether Synchro family analysis is relevant for this root/state (do not let Rank4
        /// reasons silence Synchro evidence when tuners or Synchro ED are present).
        /// </summary>
        internal static bool IsSynchroFamilyRelevant(LlmSelfResources self, LegalAction root)
        {
            if (CountSynchroExtra(self) > 0)
            {
                return true;
            }
            if (root != null && root.Card != null && root.Card.IsTuner)
            {
                return true;
            }
            if (self != null && self.Field != null)
            {
                foreach (LlmSelfResourceCard card in self.Field)
                {
                    if (card != null && card.IsTuner.HasValue && card.IsTuner.Value)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Root-scoped Synchro no-candidate reason. Always includes "synchro_" prefix.
        /// Null when a match exists (or root is not a material setup).
        /// </summary>
        internal static string AnalyzeSynchroNoCandidateReason(
            LlmSelfResources self,
            LegalAction root)
        {
            if (!IsMaterialSetupAction(root))
            {
                return null;
            }
            if (root.Card == null)
            {
                return "synchro_missing_metadata";
            }
            if (root.Card.Level <= 0)
            {
                return "synchro_unknown_level";
            }

            List<LlmSynchroMaterial> materials = BuildPostRootFaceUpMaterials(self, root);
            bool missingLevel = false;
            bool missingTunerFlag = false;
            List<LlmSynchroMaterial> tuners = new List<LlmSynchroMaterial>();
            List<LlmSynchroMaterial> nonTuners = new List<LlmSynchroMaterial>();
            foreach (LlmSynchroMaterial m in materials)
            {
                if (m == null)
                {
                    continue;
                }
                if (!m.Level.HasValue || m.Level.Value <= 0)
                {
                    missingLevel = true;
                    continue;
                }
                if (!m.IsTuner.HasValue)
                {
                    missingTunerFlag = true;
                    continue;
                }
                if (m.IsTuner.Value)
                {
                    tuners.Add(m);
                }
                else
                {
                    nonTuners.Add(m);
                }
            }
            if (missingLevel || missingTunerFlag)
            {
                return missingLevel ? "synchro_unknown_level" : "synchro_missing_metadata";
            }
            if (CountSynchroExtra(self) <= 0)
            {
                return "synchro_missing_extra_deck";
            }
            if (tuners.Count == 0)
            {
                return "synchro_no_tuner";
            }
            if (nonTuners.Count == 0)
            {
                return "synchro_no_non_tuner";
            }

            // Exact level-sum search.
            List<LlmSynchroCandidateSpec> specs = EnumerateSynchroCandidates(self, root);
            if (specs == null || specs.Count == 0)
            {
                return "synchro_insufficient_level_sum";
            }
            return null;
        }

        /// <summary>
        /// Enumerate distinct (material subset × matching Extra Deck Synchro) candidates.
        /// Exactly one tuner + one or more non-tuners; positive levels sum to ED level.
        /// Ordered deterministically by material card ids then Extra Deck card id.
        /// </summary>
        internal static List<LlmSynchroCandidateSpec> EnumerateSynchroCandidates(
            LlmSelfResources self,
            LegalAction root)
        {
            List<LlmSynchroCandidateSpec> results = new List<LlmSynchroCandidateSpec>();
            if (!IsMaterialSetupAction(root) || root == null)
            {
                return results;
            }
            // Never invent continuations for engine-current Extra Deck summons.
            if (root.Command == DuelCommandType.SummonSp)
            {
                return results;
            }

            List<LlmSynchroMaterial> materials = BuildPostRootFaceUpMaterials(self, root);
            List<LlmSynchroMaterial> tuners = new List<LlmSynchroMaterial>();
            List<LlmSynchroMaterial> nonTuners = new List<LlmSynchroMaterial>();
            foreach (LlmSynchroMaterial m in materials)
            {
                if (m == null || !m.Level.HasValue || m.Level.Value <= 0 || !m.IsTuner.HasValue)
                {
                    continue;
                }
                if (m.IsTuner.Value)
                {
                    tuners.Add(m);
                }
                else
                {
                    nonTuners.Add(m);
                }
            }
            tuners.Sort(CompareSynchroMaterialBySource);
            nonTuners.Sort(CompareSynchroMaterialBySource);
            if (tuners.Count == 0 || nonTuners.Count == 0)
            {
                return results;
            }

            List<LlmSelfResourceExtraDeckEntry> synchros = new List<LlmSelfResourceExtraDeckEntry>();
            if (self != null && self.ExtraDeck != null)
            {
                foreach (LlmSelfResourceExtraDeckEntry e in self.ExtraDeck)
                {
                    if (IsSynchroExtraCandidate(e))
                    {
                        synchros.Add(e);
                    }
                }
            }
            synchros.Sort(delegate(LlmSelfResourceExtraDeckEntry a, LlmSelfResourceExtraDeckEntry b)
            {
                int ca = a != null ? a.CardId : 0;
                int cb = b != null ? b.CardId : 0;
                return ca.CompareTo(cb);
            });
            if (synchros.Count == 0)
            {
                return results;
            }

            int n = nonTuners.Count;
            int subsetCount = (1 << n) - 1;
            foreach (LlmSynchroMaterial tuner in tuners)
            {
                for (int mask = 1; mask <= subsetCount; mask++)
                {
                    List<LlmSynchroMaterial> subset = new List<LlmSynchroMaterial>();
                    int sum = tuner.Level.Value;
                    for (int i = 0; i < n; i++)
                    {
                        if ((mask & (1 << i)) == 0)
                        {
                            continue;
                        }
                        LlmSynchroMaterial nt = nonTuners[i];
                        subset.Add(nt);
                        sum += nt.Level.Value;
                    }
                    if (subset.Count == 0)
                    {
                        continue;
                    }
                    subset.Sort(CompareSynchroMaterialBySource);
                    foreach (LlmSelfResourceExtraDeckEntry extra in synchros)
                    {
                        if (extra.Level.Value != sum)
                        {
                            continue;
                        }
                        List<LlmSynchroMaterial> full = new List<LlmSynchroMaterial>();
                        full.Add(tuner);
                        full.AddRange(subset);
                        full.Sort(CompareSynchroMaterialBySource);
                        results.Add(new LlmSynchroCandidateSpec()
                        {
                            Materials = full,
                            Extra = extra,
                            LevelSum = sum,
                        });
                    }
                }
            }

            results.Sort(CompareSynchroCandidateSpec);
            return results;
        }

        static int RootCardId(LegalAction root)
        {
            if (root == null)
            {
                return 0;
            }
            if (root.CardId > 0)
            {
                return root.CardId;
            }
            return root.Card != null ? root.Card.CardId : 0;
        }

        static int CompareSynchroMaterialBySource(LlmSynchroMaterial a, LlmSynchroMaterial b)
        {
            // Stable: CardId, then source kind (field before root), zone, index, action id.
            int ca = a != null ? a.CardId : 0;
            int cb = b != null ? b.CardId : 0;
            int c = ca.CompareTo(cb);
            if (c != 0)
            {
                return c;
            }
            int sa = a != null && a.IsRootSource ? 1 : 0;
            int sb = b != null && b.IsRootSource ? 1 : 0;
            c = sa.CompareTo(sb);
            if (c != 0)
            {
                return c;
            }
            int za = a != null ? a.Zone : 0;
            int zb = b != null ? b.Zone : 0;
            c = za.CompareTo(zb);
            if (c != 0)
            {
                return c;
            }
            int ia = a != null ? a.Index : 0;
            int ib = b != null ? b.Index : 0;
            c = ia.CompareTo(ib);
            if (c != 0)
            {
                return c;
            }
            int aa = a != null ? a.RootActionId : 0;
            int ab = b != null ? b.RootActionId : 0;
            return aa.CompareTo(ab);
        }

        static int CompareSynchroCandidateSpec(LlmSynchroCandidateSpec a, LlmSynchroCandidateSpec b)
        {
            if (a == null && b == null)
            {
                return 0;
            }
            if (a == null)
            {
                return 1;
            }
            if (b == null)
            {
                return -1;
            }
            string ka = a.MaterialKey();
            string kb = b.MaterialKey();
            int c = string.CompareOrdinal(ka, kb);
            if (c != 0)
            {
                return c;
            }
            int ea = a.Extra != null ? a.Extra.CardId : 0;
            int eb = b.Extra != null ? b.Extra.CardId : 0;
            return ea.CompareTo(eb);
        }
    }

    /// <summary>
    /// Lightweight material identity for Synchro candidacy enumeration.
    /// Source identity (zone+index or root action) distinguishes same-CardId copies.
    /// </summary>
    sealed class LlmSynchroMaterial
    {
        public int CardId;
        public string Name;
        public int Zone;
        public int Index;
        public int? Level;
        public bool? IsTuner;
        /// <summary>True when this material is the Normal Summon root copy (not a field copy).</summary>
        public bool IsRootSource;
        public int RootActionId;

        public static LlmSynchroMaterial FromFieldCard(
            LlmSelfResourceCard card,
            bool? faceUpOverride)
        {
            return new LlmSynchroMaterial()
            {
                CardId = card.CardId,
                Name = card.Name,
                Zone = card.Zone,
                Index = card.Index,
                Level = card.Level,
                IsTuner = card.IsTuner,
                IsRootSource = false,
                RootActionId = 0,
            };
        }

        public static LlmSynchroMaterial FromRootCard(LegalAction root)
        {
            LlmCardMetadata meta = root != null ? root.Card : null;
            int cid = root != null
                ? (root.CardId > 0 ? root.CardId : (meta != null ? meta.CardId : 0))
                : 0;
            return new LlmSynchroMaterial()
            {
                CardId = cid,
                Name = meta != null ? meta.Name : null,
                Zone = root != null ? root.Position : -1,
                Index = root != null ? root.Index : -1,
                Level = meta != null && meta.Level > 0 ? (int?)meta.Level : null,
                IsTuner = meta != null ? (bool?)meta.IsTuner : null,
                IsRootSource = true,
                RootActionId = root != null ? root.ActionId : 0,
            };
        }

        /// <summary>Stable source key for fingerprints / line ids (no opponent-hidden data).</summary>
        public string SourceKey()
        {
            if (IsRootSource)
            {
                return "cid:" + CardId + ":src:root:a" + RootActionId;
            }
            return "cid:" + CardId + ":src:z" + Zone + ":idx" + Index;
        }

        public string IdentityUnlock()
        {
            string name = !string.IsNullOrEmpty(Name) ? Name : ("card_" + CardId);
            if (IsRootSource)
            {
                return "synchro material: " + name + " (" + CardId + "; src:root; a:"
                    + RootActionId + ")";
            }
            return "synchro material: " + name + " (" + CardId + "; z:" + Zone
                + "; idx:" + Index + ")";
        }
    }

    /// <summary>One inferred Synchro candidate (material set + Extra Deck entry).</summary>
    sealed class LlmSynchroCandidateSpec
    {
        public List<LlmSynchroMaterial> Materials;
        public LlmSelfResourceExtraDeckEntry Extra;
        public int LevelSum;

        public string MaterialKey()
        {
            if (Materials == null || Materials.Count == 0)
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Materials.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('+');
                }
                sb.Append(Materials[i] != null ? Materials[i].SourceKey() : "null");
            }
            return sb.ToString();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Structured grounded facts for one current legal root's immediate chain outcome
    /// (YGOMASTER-LLM-005 Slice 2B). Fixtures / extractors populate this; text alone is not
    /// authority for hard rejection.
    /// </summary>
    class LlmImmediateOutcomeInput
    {
        public int RootActionId { get; set; }
        public int TargetCardId { get; set; }
        public string TargetName { get; set; }

        public bool TargetAlreadyActivated { get; set; }
        public bool TargetIsAlreadyActivated
        {
            get { return TargetAlreadyActivated; }
            set { TargetAlreadyActivated = value; }
        }
        public bool TargetActivatedOnChain
        {
            get { return TargetAlreadyActivated; }
            set { TargetAlreadyActivated = value; }
        }

        public bool TargetIsOneShotSpellOrTrap { get; set; }
        public bool IsOneShotSpellOrTrap
        {
            get { return TargetIsOneShotSpellOrTrap; }
            set { TargetIsOneShotSpellOrTrap = value; }
        }

        public bool TargetResolutionRequiresRemainFaceUp { get; set; }
        public bool ResolutionRequiresRemainFaceUp
        {
            get { return TargetResolutionRequiresRemainFaceUp; }
            set { TargetResolutionRequiresRemainFaceUp = value; }
        }
        public bool TargetMustRemainFaceUp
        {
            get { return TargetResolutionRequiresRemainFaceUp; }
            set { TargetResolutionRequiresRemainFaceUp = value; }
        }

        public string TargetCardKind { get; set; }
        public string TargetKind
        {
            get { return TargetCardKind; }
            set { TargetCardKind = value; }
        }
        public string TargetFrame { get; set; }

        public bool CapabilitiesGrounded { get; set; }
        public bool ResponseCapabilitiesGrounded
        {
            get { return CapabilitiesGrounded; }
            set { CapabilitiesGrounded = value; }
        }
        public bool ResolutionDependencyGrounded { get; set; }
        public bool SemanticsGrounded
        {
            get { return CapabilitiesGrounded && ResolutionDependencyGrounded; }
            set
            {
                CapabilitiesGrounded = value;
                ResolutionDependencyGrounded = value;
            }
        }

        public List<string> ResponseCapabilities { get; set; }
        public List<string> Capabilities
        {
            get { return ResponseCapabilities; }
            set { ResponseCapabilities = value; }
        }
        public List<string> EffectCapabilities
        {
            get { return ResponseCapabilities; }
            set { ResponseCapabilities = value; }
        }

        public List<string> SecondaryBenefits { get; set; }
        public List<string> SecondaryEffects
        {
            get { return SecondaryBenefits; }
            set { SecondaryBenefits = value; }
        }
        public List<string> SecondaryValueItems
        {
            get { return SecondaryBenefits; }
            set { SecondaryBenefits = value; }
        }

        public string Provenance { get; set; }
        public string CapabilityProvenance { get; set; }

        public LlmImmediateOutcomeInput()
        {
            ResponseCapabilities = new List<string>();
            SecondaryBenefits = new List<string>();
            Provenance = "rules_inferred";
        }
    }

    /// <summary>
    /// Immutable deterministic annotation for a current legal root's predicted immediate outcome.
    /// </summary>
    class LlmImmediateOutcomeAnnotation
    {
        public int RootActionId { get; private set; }
        public int TargetCardId { get; private set; }
        public string TargetName { get; private set; }

        public bool TargetAlreadyActivated { get; private set; }
        public bool TargetIsAlreadyActivated { get { return TargetAlreadyActivated; } }
        public bool TargetActivatedOnChain { get { return TargetAlreadyActivated; } }

        public bool TargetIsOneShotSpellOrTrap { get; private set; }
        public bool TargetResolutionRequiresRemainFaceUp { get; private set; }
        public bool ResolutionRequiresRemainFaceUp
        {
            get { return TargetResolutionRequiresRemainFaceUp; }
        }
        public bool TargetMustRemainFaceUp
        {
            get { return TargetResolutionRequiresRemainFaceUp; }
        }

        public string TargetCardKind { get; private set; }
        public string TargetFrame { get; private set; }

        public IList<string> ResponseCapabilities { get; private set; }
        public IList<string> Capabilities { get { return ResponseCapabilities; } }
        public IList<string> EffectCapabilities { get { return ResponseCapabilities; } }

        public bool PrimaryEffectStopped { get; private set; }
        public bool PrimaryEffectPrevented { get { return PrimaryEffectStopped; } }
        public bool TargetEffectExpectedToResolve { get; private set; }
        public bool PrimaryEffectExpectedToResolve
        {
            get { return TargetEffectExpectedToResolve; }
        }

        /// <summary>0 = no primary disruption (e.g. destroy-only vs activated one-shot).</summary>
        public int PrimaryDisruptionValue { get; private set; }
        public int PrimaryDisruptionScore { get { return PrimaryDisruptionValue; } }
        public int NegationValue { get { return PrimaryDisruptionValue; } }

        public IList<string> SecondaryBenefits { get; private set; }
        public IList<string> SecondaryEffects { get { return SecondaryBenefits; } }
        public IList<string> SecondaryValueItems { get { return SecondaryBenefits; } }

        public string Provenance { get; private set; }
        public string OutcomeProvenance { get { return Provenance; } }
        public double Confidence { get; private set; }
        public double OutcomeConfidence { get { return Confidence; } }

        public string Boundary { get; private set; }
        public string UncertaintyBoundary { get { return Boundary; } }
        public string FailOpenBoundary { get { return Boundary; } }
        public string SemanticsBoundary { get { return Boundary; } }

        public IList<string> Uncertainty { get; private set; }
        public bool IsUnknownCardSemantics { get; private set; }
        public bool UnknownSemantics { get { return IsUnknownCardSemantics; } }

        /// <summary>
        /// True only when the reusable already-activated one-shot destroy-does-not-negate
        /// rule was applied. Remain-face-up and unknown paths leave this false.
        /// </summary>
        public bool AppliedOneShotDestroyDoesNotNegateRule { get; private set; }
        public bool UsedOneShotSpellTrapRule
        {
            get { return AppliedOneShotDestroyDoesNotNegateRule; }
        }
        public bool ClassifiedAsOneShotDestroyResolve
        {
            get { return AppliedOneShotDestroyDoesNotNegateRule; }
        }

        /// <summary>Deterministic scoring feature: penalty weight for known value-zero primary.</summary>
        public int ValueZeroPrimaryPenalty { get; private set; }
        public int SecondaryBenefitCount { get; private set; }
        public string PredictedPrimaryResult { get; private set; }

        internal static LlmImmediateOutcomeAnnotation Create(
            int rootActionId,
            int targetCardId,
            string targetName,
            bool targetAlreadyActivated,
            bool targetIsOneShotSpellOrTrap,
            bool targetResolutionRequiresRemainFaceUp,
            string targetCardKind,
            string targetFrame,
            IList<string> responseCapabilities,
            bool primaryEffectStopped,
            bool targetEffectExpectedToResolve,
            int primaryDisruptionValue,
            IList<string> secondaryBenefits,
            string provenance,
            double confidence,
            string boundary,
            IList<string> uncertainty,
            bool isUnknownCardSemantics,
            bool appliedOneShotDestroyDoesNotNegateRule,
            int valueZeroPrimaryPenalty,
            string predictedPrimaryResult)
        {
            List<string> caps = new List<string>();
            if (responseCapabilities != null)
            {
                foreach (string c in responseCapabilities)
                {
                    if (!string.IsNullOrEmpty(c))
                    {
                        caps.Add(c);
                    }
                }
            }
            List<string> secondary = new List<string>();
            if (secondaryBenefits != null)
            {
                foreach (string s in secondaryBenefits)
                {
                    if (!string.IsNullOrEmpty(s))
                    {
                        secondary.Add(s);
                    }
                }
            }
            List<string> unc = new List<string>();
            if (uncertainty != null)
            {
                foreach (string u in uncertainty)
                {
                    if (!string.IsNullOrEmpty(u))
                    {
                        unc.Add(u);
                    }
                }
            }

            return new LlmImmediateOutcomeAnnotation()
            {
                RootActionId = rootActionId,
                TargetCardId = targetCardId,
                TargetName = targetName,
                TargetAlreadyActivated = targetAlreadyActivated,
                TargetIsOneShotSpellOrTrap = targetIsOneShotSpellOrTrap,
                TargetResolutionRequiresRemainFaceUp = targetResolutionRequiresRemainFaceUp,
                TargetCardKind = targetCardKind,
                TargetFrame = targetFrame,
                ResponseCapabilities = new ReadOnlyCollection<string>(caps),
                PrimaryEffectStopped = primaryEffectStopped,
                TargetEffectExpectedToResolve = targetEffectExpectedToResolve,
                PrimaryDisruptionValue = primaryDisruptionValue,
                SecondaryBenefits = new ReadOnlyCollection<string>(secondary),
                Provenance = provenance ?? "rules_inferred",
                Confidence = confidence,
                Boundary = boundary,
                Uncertainty = new ReadOnlyCollection<string>(unc),
                IsUnknownCardSemantics = isUnknownCardSemantics,
                AppliedOneShotDestroyDoesNotNegateRule = appliedOneShotDestroyDoesNotNegateRule,
                ValueZeroPrimaryPenalty = valueZeroPrimaryPenalty,
                SecondaryBenefitCount = secondary.Count,
                PredictedPrimaryResult = predictedPrimaryResult,
            };
        }
    }

    /// <summary>
    /// Result of checking a provider response against a grounded outcome annotation.
    /// </summary>
    class LlmImmediateOutcomeValidationResult
    {
        public bool IsValid { get; private set; }
        public bool Valid { get { return IsValid; } }
        public bool Success { get { return IsValid; } }
        public bool Ok { get { return IsValid; } }
        public string Error { get; private set; }
        public string Reason { get { return Error; } }
        public string FailureReason { get { return Error; } }
        public string Code { get { return Error; } }

        public static LlmImmediateOutcomeValidationResult ValidResult()
        {
            return new LlmImmediateOutcomeValidationResult()
            {
                IsValid = true,
                Error = null,
            };
        }

        public static LlmImmediateOutcomeValidationResult Invalid(string error)
        {
            return new LlmImmediateOutcomeValidationResult()
            {
                IsValid = false,
                Error = error ?? "outcome_contradiction",
            };
        }
    }

    /// <summary>
    /// Audit emission policy for immediate-outcome lines. Default-off until provider integration.
    /// </summary>
    static class LlmImmediateOutcomeAuditPolicy
    {
        public static bool ShouldEmitAudit(
            bool auditEnabled,
            int configuredControlPlayer,
            DecisionSnapshot snapshot)
        {
            if (!auditEnabled)
            {
                return false;
            }
            if (configuredControlPlayer != 0 && configuredControlPlayer != 1)
            {
                return false;
            }
            if (snapshot == null)
            {
                return false;
            }
            if (snapshot.ControlledPlayer != configuredControlPlayer)
            {
                return false;
            }
            if (snapshot.ActingPlayer != configuredControlPlayer)
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Pure deterministic analyzer for current-chain immediate outcomes
    /// (YGOMASTER-LLM-005 Slice 2B). No named-card blacklists; grounded structured facts only.
    /// Audit-only / default-off relative to live action selection.
    /// </summary>
    static class LlmImmediateOutcomeAnalyzer
    {
        public const string CapabilityDestroy = "destroy";
        public const string CapabilityNegateActivation = "negate_activation";
        public const string CapabilityNegateEffect = "negate_effect";
        public const string BoundaryUnknownCardSemantics = "unknown_card_semantics";
        public const string ProvenanceRulesInferred = "rules_inferred";
        public const string ErrorOutcomeContradiction = "outcome_contradiction";

        public const int DisruptionNegation = 1;
        public const int DisruptionRemainFaceUpRemoval = 1;
        public const int DisruptionNone = 0;
        public const int ValueZeroPrimaryPenaltyWeight = 10;

        /// <summary>
        /// Production entry: derive grounded/unknown input from the current snapshot and root,
        /// then annotate. Does not require a test-only manually populated input.
        /// </summary>
        public static LlmImmediateOutcomeAnnotation AnalyzeRoot(
            DecisionSnapshot snapshot,
            LegalAction root)
        {
            return AnalyzeRoot(snapshot, root, LlmCardEffectCapabilityCatalog.Default);
        }

        public static LlmImmediateOutcomeAnnotation AnalyzeRoot(
            DecisionSnapshot snapshot,
            LegalAction root,
            ILlmCardEffectCapabilitySource capabilitySource)
        {
            LlmImmediateOutcomeInput input = DeriveInput(snapshot, root, capabilitySource);
            return Analyze(snapshot, root, input);
        }

        /// <summary>
        /// Attach immediate-outcome annotations to every strategic root shell and apply
        /// ValueZeroPrimaryPenalty to deterministic line scores (does not suppress roots).
        /// </summary>
        public static void AnnotateSearchGraph(LlmSearchGraph graph)
        {
            AnnotateSearchGraph(graph, LlmCardEffectCapabilityCatalog.Default);
        }

        public static void AnnotateSearchGraph(
            LlmSearchGraph graph,
            ILlmCardEffectCapabilitySource capabilitySource)
        {
            if (graph == null || graph.Lines == null)
            {
                return;
            }
            DecisionSnapshot snapshot = graph.SourceSnapshot;
            if (snapshot == null || snapshot.LegalActions == null)
            {
                return;
            }
            Dictionary<int, LegalAction> roots = new Dictionary<int, LegalAction>();
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action != null && !action.IsMechanical)
                {
                    roots[action.ActionId] = action;
                }
            }
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line == null || !line.IsRootShell)
                {
                    continue;
                }
                LegalAction root;
                if (!roots.TryGetValue(line.RootActionId, out root) || root == null)
                {
                    continue;
                }
                LlmImmediateOutcomeAnnotation ann = AnalyzeRoot(snapshot, root, capabilitySource);
                line.ImmediateOutcome = ann;
                if (line.ScoreFeatures == null)
                {
                    line.ScoreFeatures = new Dictionary<string, int>();
                }
                if (ann != null && ann.ValueZeroPrimaryPenalty > 0)
                {
                    line.ScoreFeatures["value_zero_primary_penalty"] = ann.ValueZeroPrimaryPenalty;
                    line.Score = Math.Max(0, line.Score - ann.ValueZeroPrimaryPenalty);
                }
                if (ann != null)
                {
                    line.ScoreFeatures["primary_disruption_value"] = ann.PrimaryDisruptionValue;
                }
            }
        }

        /// <summary>
        /// Derive a conservative LlmImmediateOutcomeInput from current engine/snapshot facts.
        /// </summary>
        public static LlmImmediateOutcomeInput DeriveInput(
            DecisionSnapshot snapshot,
            LegalAction root)
        {
            return DeriveInput(snapshot, root, LlmCardEffectCapabilityCatalog.Default);
        }

        public static LlmImmediateOutcomeInput DeriveInput(
            DecisionSnapshot snapshot,
            LegalAction root,
            ILlmCardEffectCapabilitySource capabilitySource)
        {
            LlmImmediateOutcomeInput input = new LlmImmediateOutcomeInput();
            if (root != null)
            {
                input.RootActionId = root.ActionId;
            }
            if (capabilitySource == null)
            {
                capabilitySource = LlmCardEffectCapabilityCatalog.Default;
            }

            // Response capabilities from catalog for the root's card.
            LlmCardEffectCapabilityFacts responseFacts = null;
            bool capsGrounded = false;
            if (root != null && root.CardId > 0
                && capabilitySource.TryGet(root.CardId, out responseFacts)
                && responseFacts != null
                && responseFacts.CapabilitiesGrounded)
            {
                capsGrounded = true;
                if (responseFacts.ResponseCapabilities != null)
                {
                    input.ResponseCapabilities = new List<string>(responseFacts.ResponseCapabilities);
                }
                if (responseFacts.SecondaryBenefits != null)
                {
                    input.SecondaryBenefits = new List<string>(responseFacts.SecondaryBenefits);
                }
            }

            // Resolve chain target from effect-target actions or face-up public ST.
            LegalAction targetAction = FindChainTargetAction(snapshot, root);
            PublicKnownCard targetKnown = null;
            if (targetAction == null)
            {
                targetKnown = FindActivatedFaceUpSpellTrapTarget(snapshot, root);
            }

            int targetCardId = 0;
            string targetName = null;
            string targetKind = null;
            string targetFrame = null;
            LlmCardMetadata targetMeta = null;
            if (targetAction != null)
            {
                targetCardId = targetAction.CardId;
                targetMeta = targetAction.Card;
            }
            else if (targetKnown != null)
            {
                targetCardId = targetKnown.CardId;
                targetMeta = targetKnown.Card;
            }
            if (targetMeta != null)
            {
                targetName = targetMeta.Name;
                targetKind = targetMeta.Kind;
                targetFrame = targetMeta.Frame;
            }
            input.TargetCardId = targetCardId;
            input.TargetName = targetName;
            input.TargetCardKind = targetKind;
            input.TargetFrame = targetFrame;

            bool chainWindow = IsChainResponseWindow(snapshot);
            // Already activated: chain-response window with a face-up Normal/known target,
            // or an explicit effect-target selection of a public card.
            input.TargetAlreadyActivated = chainWindow
                && targetCardId > 0
                && (targetAction != null
                    || (targetKnown != null && IsPublicFaceUp(targetKnown.Face)));

            // Resolution dependency: catalog target facts, else frame/kind derivation.
            bool resGrounded = false;
            bool isOneShot = false;
            bool remainFaceUp = false;
            LlmCardEffectCapabilityFacts targetFacts = null;
            if (targetCardId > 0
                && capabilitySource.TryGet(targetCardId, out targetFacts)
                && targetFacts != null
                && targetFacts.ResolutionDependencyGrounded)
            {
                resGrounded = true;
                isOneShot = targetFacts.IsOneShotSpellOrTrap.GetValueOrDefault();
                remainFaceUp = targetFacts.RequiresRemainFaceUp.GetValueOrDefault();
            }
            else if (LlmCardEffectCapabilityCatalog.TryDeriveResolutionFromFrame(
                targetKind, targetFrame, out isOneShot, out remainFaceUp, out resGrounded))
            {
                // derived
            }

            input.TargetIsOneShotSpellOrTrap = isOneShot;
            input.TargetResolutionRequiresRemainFaceUp = remainFaceUp;
            input.CapabilitiesGrounded = capsGrounded;
            input.ResolutionDependencyGrounded = resGrounded;
            input.Provenance = ProvenanceRulesInferred;
            input.CapabilityProvenance = capsGrounded
                ? ProvenanceRulesInferred
                : BoundaryUnknownCardSemantics;

            // Non-chain / non-response roots: leave ungrounded so unknown fail-open applies
            // rather than inventing a destroy-vs-negate package.
            if (!chainWindow || root == null || !IsResponseRoot(root))
            {
                if (!capsGrounded)
                {
                    input.CapabilitiesGrounded = false;
                }
                // Decline / phase roots have no target package.
                if (root != null
                    && (root.Kind == LegalActionKind.Cancel
                        || root.Kind == LegalActionKind.MovePhase
                        || root.IsMechanical))
                {
                    input.CapabilitiesGrounded = false;
                    input.ResolutionDependencyGrounded = false;
                    input.TargetAlreadyActivated = false;
                }
            }

            return input;
        }

        static bool IsChainResponseWindow(DecisionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }
            if (snapshot.ViewParam1 == (int)DuelMenuActType.CheckChain)
            {
                return true;
            }
            string reason = snapshot.StrategicWindowReason ?? string.Empty;
            if (reason.IndexOf("chain", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return false;
        }

        static bool IsResponseRoot(LegalAction root)
        {
            if (root == null)
            {
                return false;
            }
            if (root.Kind == LegalActionKind.Cancel)
            {
                return false;
            }
            if (root.Kind == LegalActionKind.Command
                && root.Command == DuelCommandType.Action)
            {
                return true;
            }
            string role = root.StrategicRole ?? string.Empty;
            return role.IndexOf("chain", StringComparison.OrdinalIgnoreCase) >= 0
                || role.IndexOf("response", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static LegalAction FindChainTargetAction(DecisionSnapshot snapshot, LegalAction root)
        {
            if (snapshot == null || snapshot.LegalActions == null)
            {
                return null;
            }
            foreach (LegalAction action in snapshot.LegalActions)
            {
                if (action == null || action.IsMechanical)
                {
                    continue;
                }
                if (action.IsEffectTargetSelection && action.CardId > 0)
                {
                    return action;
                }
            }
            return null;
        }

        /// <summary>
        /// When no explicit effect-target action exists, ground a public face-up S/T only if
        /// exactly one eligible candidate is visible. Multiple candidates fail open (null)
        /// rather than preferring Normal-frame or any other heuristic.
        /// </summary>
        static PublicKnownCard FindActivatedFaceUpSpellTrapTarget(
            DecisionSnapshot snapshot,
            LegalAction root)
        {
            if (snapshot == null || snapshot.PublicState == null)
            {
                return null;
            }
            int controlled = snapshot.ControlledPlayer;
            List<PublicKnownCard> eligible = new List<PublicKnownCard>();
            foreach (PublicPlayerState player in snapshot.PublicState.Players)
            {
                if (player == null || player.Player == controlled)
                {
                    continue;
                }
                if (player.KnownCards == null)
                {
                    continue;
                }
                foreach (PublicKnownCard card in player.KnownCards)
                {
                    if (card == null || card.CardId <= 0)
                    {
                        continue;
                    }
                    // Spell/Trap zones / field: only face-up public identities.
                    if (card.Position < 0 || card.Position > 12)
                    {
                        continue;
                    }
                    if (!IsPublicFaceUp(card.Face))
                    {
                        continue;
                    }
                    LlmCardMetadata meta = card.Card;
                    string kind = meta != null ? meta.Kind : null;
                    bool looksSpellTrap =
                        (!string.IsNullOrEmpty(kind)
                            && (kind.IndexOf("Spell", StringComparison.OrdinalIgnoreCase) >= 0
                                || kind.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0))
                        || card.Position >= 5;
                    if (!looksSpellTrap)
                    {
                        continue;
                    }
                    eligible.Add(card);
                }
            }
            // Explicit single public candidate only. Ambiguity → null → unknown_card_semantics.
            if (eligible.Count == 1)
            {
                return eligible[0];
            }
            return null;
        }

        static bool IsPublicFaceUp(int face)
        {
            return face == 1 || face == 8;
        }

        public static LlmImmediateOutcomeAnnotation Analyze(
            DecisionSnapshot snapshot,
            LegalAction root,
            LlmImmediateOutcomeInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            int rootActionId = root != null ? root.ActionId : input.RootActionId;
            List<string> caps = NormalizeCapabilities(input.ResponseCapabilities);
            List<string> secondary = NormalizeStringList(input.SecondaryBenefits);

            // Fail-open when capabilities or resolution dependency are not grounded.
            if (!input.CapabilitiesGrounded || !input.ResolutionDependencyGrounded)
            {
                List<string> unknownUncertainty = new List<string>()
                {
                    BoundaryUnknownCardSemantics,
                    "insufficient structured capability or resolution-dependency data",
                };
                return LlmImmediateOutcomeAnnotation.Create(
                    rootActionId,
                    input.TargetCardId,
                    RedactTargetName(input.TargetName),
                    input.TargetAlreadyActivated,
                    input.TargetIsOneShotSpellOrTrap,
                    input.TargetResolutionRequiresRemainFaceUp,
                    input.TargetCardKind,
                    input.TargetFrame,
                    caps,
                    /* primaryEffectStopped */ false,
                    /* targetEffectExpectedToResolve */ false,
                    DisruptionNone,
                    secondary,
                    BoundaryUnknownCardSemantics,
                    /* confidence */ 0.25,
                    BoundaryUnknownCardSemantics,
                    unknownUncertainty,
                    /* isUnknown */ true,
                    /* appliedOneShot */ false,
                    /* valueZeroPenalty */ 0,
                    "unknown");
            }

            bool hasNegate = HasCapability(caps, CapabilityNegateActivation)
                || HasCapability(caps, CapabilityNegateEffect);
            bool hasDestroy = HasCapability(caps, CapabilityDestroy);
            bool alreadyActivated = input.TargetAlreadyActivated;
            bool remainFaceUp = input.TargetResolutionRequiresRemainFaceUp;
            bool oneShot = input.TargetIsOneShotSpellOrTrap && !remainFaceUp;

            // Grounded negation of an already-activated effect.
            if (alreadyActivated && hasNegate)
            {
                return LlmImmediateOutcomeAnnotation.Create(
                    rootActionId,
                    input.TargetCardId,
                    RedactTargetName(input.TargetName),
                    alreadyActivated,
                    input.TargetIsOneShotSpellOrTrap,
                    remainFaceUp,
                    input.TargetCardKind,
                    input.TargetFrame,
                    caps,
                    /* primaryEffectStopped */ true,
                    /* targetEffectExpectedToResolve */ false,
                    DisruptionNegation,
                    secondary,
                    string.IsNullOrEmpty(input.Provenance)
                        ? ProvenanceRulesInferred
                        : input.Provenance,
                    /* confidence */ 0.95,
                    null,
                    new List<string>(),
                    false,
                    false,
                    /* valueZeroPenalty */ 0,
                    "stopped_by_negation");
            }

            // Remain-face-up Continuous / Field / Equip: do NOT apply one-shot rule.
            if (remainFaceUp && alreadyActivated && hasDestroy)
            {
                List<string> remainUncertainty = new List<string>()
                {
                    "remain-face-up dependency: ongoing Continuous/Field/Equip effect",
                    "destroy removes face-up source; not classified as one-shot resolve-through",
                };
                return LlmImmediateOutcomeAnnotation.Create(
                    rootActionId,
                    input.TargetCardId,
                    RedactTargetName(input.TargetName),
                    alreadyActivated,
                    /* oneShot */ false,
                    /* remainFaceUp */ true,
                    input.TargetCardKind,
                    input.TargetFrame,
                    caps,
                    // Destroy ends the ongoing face-up-dependent effect path.
                    /* primaryEffectStopped */ true,
                    /* targetEffectExpectedToResolve */ false,
                    DisruptionRemainFaceUpRemoval,
                    secondary,
                    string.IsNullOrEmpty(input.Provenance)
                        ? ProvenanceRulesInferred
                        : input.Provenance,
                    /* confidence */ 0.85,
                    null,
                    remainUncertainty,
                    false,
                    /* appliedOneShot */ false,
                    /* valueZeroPenalty */ 0,
                    "ongoing_effect_removed_by_destroy");
            }

            if (remainFaceUp)
            {
                List<string> remainOpen = new List<string>()
                {
                    "remain-face-up resolution dependency grounded",
                    "one-shot Normal Spell/Trap destroy rule not applied",
                };
                return LlmImmediateOutcomeAnnotation.Create(
                    rootActionId,
                    input.TargetCardId,
                    RedactTargetName(input.TargetName),
                    alreadyActivated,
                    false,
                    true,
                    input.TargetCardKind,
                    input.TargetFrame,
                    caps,
                    false,
                    false,
                    DisruptionNone,
                    secondary,
                    string.IsNullOrEmpty(input.Provenance)
                        ? ProvenanceRulesInferred
                        : input.Provenance,
                    0.7,
                    null,
                    remainOpen,
                    false,
                    false,
                    0,
                    "remain_face_up_unresolved_path");
            }

            // Already-activated one-shot Spell/Trap + destroy-only → effect still resolves.
            if (alreadyActivated && oneShot && hasDestroy && !hasNegate)
            {
                return LlmImmediateOutcomeAnnotation.Create(
                    rootActionId,
                    input.TargetCardId,
                    RedactTargetName(input.TargetName),
                    alreadyActivated,
                    true,
                    false,
                    input.TargetCardKind,
                    input.TargetFrame,
                    caps,
                    /* primaryEffectStopped */ false,
                    /* targetEffectExpectedToResolve */ true,
                    DisruptionNone,
                    secondary,
                    string.IsNullOrEmpty(input.Provenance)
                        ? ProvenanceRulesInferred
                        : input.Provenance,
                    /* confidence */ 0.95,
                    null,
                    new List<string>()
                    {
                        "destroy_does_not_negate_already_activated_one_shot",
                    },
                    false,
                    /* appliedOneShot */ true,
                    ValueZeroPrimaryPenaltyWeight,
                    "one_shot_resolves_after_destroy");
            }

            // Default grounded: no strong claim about interruption.
            List<string> defaultUncertainty = new List<string>()
            {
                "no reusable destroy-versus-negate or remain-face-up rule matched",
            };
            return LlmImmediateOutcomeAnnotation.Create(
                rootActionId,
                input.TargetCardId,
                RedactTargetName(input.TargetName),
                alreadyActivated,
                input.TargetIsOneShotSpellOrTrap,
                remainFaceUp,
                input.TargetCardKind,
                input.TargetFrame,
                caps,
                false,
                alreadyActivated && oneShot,
                DisruptionNone,
                secondary,
                string.IsNullOrEmpty(input.Provenance)
                    ? ProvenanceRulesInferred
                    : input.Provenance,
                0.6,
                null,
                defaultUncertainty,
                false,
                false,
                0,
                "no_strong_primary_claim");
        }

        public static LlmImmediateOutcomeAnnotation Annotate(
            DecisionSnapshot snapshot,
            LegalAction root,
            LlmImmediateOutcomeInput input)
        {
            return Analyze(snapshot, root, input);
        }

        public static LlmImmediateOutcomeAnnotation Evaluate(
            DecisionSnapshot snapshot,
            LegalAction root,
            LlmImmediateOutcomeInput input)
        {
            return Analyze(snapshot, root, input);
        }

        /// <summary>
        /// Reject provider claims that contradict a grounded deterministic outcome fact.
        /// Unknown-semantics annotations fail open (no hard outcome_contradiction).
        /// Second parameter is typed (not bare object) so reflection binders do not
        /// accidentally pass the annotation twice.
        /// </summary>
        public static LlmImmediateOutcomeValidationResult ValidateProviderResponse(
            LlmImmediateOutcomeAnnotation annotation,
            LlmBrokerDecisionResponse providerResponse)
        {
            return ValidateProviderResponseCore(annotation, providerResponse);
        }

        public static LlmImmediateOutcomeValidationResult ValidateProviderResponse(
            LlmImmediateOutcomeAnnotation annotation,
            Dictionary<string, object> providerResponse)
        {
            return ValidateProviderResponseCore(annotation, providerResponse);
        }

        public static LlmImmediateOutcomeValidationResult ValidateResponseConsistency(
            LlmImmediateOutcomeAnnotation annotation,
            LlmBrokerDecisionResponse providerResponse)
        {
            return ValidateProviderResponseCore(annotation, providerResponse);
        }

        public static LlmImmediateOutcomeValidationResult ValidateConsistency(
            LlmImmediateOutcomeAnnotation annotation,
            LlmBrokerDecisionResponse providerResponse)
        {
            return ValidateProviderResponseCore(annotation, providerResponse);
        }

        static LlmImmediateOutcomeValidationResult ValidateProviderResponseCore(
            LlmImmediateOutcomeAnnotation annotation,
            object providerResponse)
        {
            if (annotation == null)
            {
                return LlmImmediateOutcomeValidationResult.Invalid("missing_annotation");
            }
            if (providerResponse == null)
            {
                return LlmImmediateOutcomeValidationResult.Invalid("missing_response");
            }

            // Text-only / unknown semantics cannot hard-reject.
            if (annotation.IsUnknownCardSemantics
                || string.Equals(
                    annotation.Boundary,
                    BoundaryUnknownCardSemantics,
                    StringComparison.OrdinalIgnoreCase))
            {
                return LlmImmediateOutcomeValidationResult.ValidResult();
            }

            bool? claimedStopped = ReadOptionalBool(
                providerResponse,
                "PrimaryEffectStopped",
                "primary_effect_stopped",
                "PrimaryDisruptionClaimed",
                "primary_disruption_claimed");
            string reason = ReadOptionalString(providerResponse, "Reason", "reason") ?? string.Empty;
            string valueBasis = ReadOptionalString(
                providerResponse, "ValueBasis", "value_basis") ?? string.Empty;
            string predicted = ReadOptionalString(
                providerResponse, "PredictedResolution", "predicted_resolution") ?? string.Empty;

            bool reasonClaimsStop = ReasonClaimsPrimaryStopped(reason);
            bool basisClaimsNegation =
                valueBasis.IndexOf("negat", StringComparison.OrdinalIgnoreCase) >= 0
                || valueBasis.IndexOf("interrupt", StringComparison.OrdinalIgnoreCase) >= 0
                || valueBasis.IndexOf("prevent", StringComparison.OrdinalIgnoreCase) >= 0;
            bool predictedClaimsStop =
                predicted.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0
                || predicted.IndexOf("negat", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(predicted, "stopped", StringComparison.OrdinalIgnoreCase);

            bool providerClaimsStopped =
                (claimedStopped.HasValue && claimedStopped.Value)
                || reasonClaimsStop
                || predictedClaimsStop
                || (basisClaimsNegation && !annotation.PrimaryEffectStopped);

            // Grounded: destroy-only / value-zero primary must not be credited as negation.
            if (!annotation.PrimaryEffectStopped && providerClaimsStopped)
            {
                return LlmImmediateOutcomeValidationResult.Invalid(ErrorOutcomeContradiction);
            }

            if (annotation.PrimaryDisruptionValue == 0
                && !annotation.PrimaryEffectStopped
                && basisClaimsNegation)
            {
                return LlmImmediateOutcomeValidationResult.Invalid(ErrorOutcomeContradiction);
            }

            // Consistent claims (including low-value + secondary basis) are accepted.
            return LlmImmediateOutcomeValidationResult.ValidResult();
        }

        /// <summary>
        /// Audit-only dictionary projection. Never includes opponent-hidden identities;
        /// only structured outcome fields from the annotation.
        /// </summary>
        public static Dictionary<string, object> ProjectAudit(
            DecisionSnapshot snapshot,
            LlmImmediateOutcomeAnnotation annotation)
        {
            return BuildAuditDictionary(snapshot, annotation);
        }

        public static Dictionary<string, object> ProjectAudit(
            LlmImmediateOutcomeAnnotation annotation)
        {
            return BuildAuditDictionary(null, annotation);
        }

        public static Dictionary<string, object> Project(
            DecisionSnapshot snapshot,
            LlmImmediateOutcomeAnnotation annotation)
        {
            return ProjectAudit(snapshot, annotation);
        }

        public static Dictionary<string, object> Project(
            LlmImmediateOutcomeAnnotation annotation)
        {
            return ProjectAudit(annotation);
        }

        public static Dictionary<string, object> ToAuditDictionary(
            LlmImmediateOutcomeAnnotation annotation)
        {
            return ProjectAudit(annotation);
        }

        public static string SerializeAudit(
            DecisionSnapshot snapshot,
            LlmImmediateOutcomeAnnotation annotation)
        {
            return MiniJSON.Json.Serialize(ProjectAudit(snapshot, annotation));
        }

        public static string Serialize(LlmImmediateOutcomeAnnotation annotation)
        {
            return MiniJSON.Json.Serialize(ProjectAudit(annotation));
        }

        public static string SerializeAudit(
            DecisionSnapshot snapshot,
            LlmImmediateOutcomeAnnotation annotation,
            bool auditEnabled,
            int configuredControlPlayer)
        {
            if (!LlmImmediateOutcomeAuditPolicy.ShouldEmitAudit(
                auditEnabled, configuredControlPlayer, snapshot))
            {
                return MiniJSON.Json.Serialize(new Dictionary<string, object>()
                {
                    { "kind", "llm_immediate_outcome_audit" },
                    { "audit_enabled", auditEnabled },
                    { "audit_emitted", false },
                    { "configured_control_player", configuredControlPlayer },
                });
            }
            Dictionary<string, object> data = ProjectAudit(snapshot, annotation);
            data["audit_enabled"] = true;
            data["audit_emitted"] = true;
            data["configured_control_player"] = configuredControlPlayer;
            return MiniJSON.Json.Serialize(data);
        }

        static Dictionary<string, object> BuildAuditDictionary(
            DecisionSnapshot snapshot,
            LlmImmediateOutcomeAnnotation annotation)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                { "kind", "llm_immediate_outcome_audit" },
                { "schema_version", LlmBrokerProtocol.SchemaVersion },
            };
            if (snapshot != null)
            {
                data["run_effect_seq"] = (long)snapshot.RunEffectSeq;
                data["controlled_player"] = snapshot.ControlledPlayer;
                data["acting_player"] = snapshot.ActingPlayer;
            }
            if (annotation == null)
            {
                data["annotation"] = null;
                return data;
            }

            // Explicit snake_case audit bag — never dumps PublicState or opponent hidden facts.
            Dictionary<string, object> ann = new Dictionary<string, object>()
            {
                { "root_action_id", annotation.RootActionId },
                { "target_card_id", annotation.TargetCardId },
                { "target_name", annotation.TargetName },
                { "target_already_activated", annotation.TargetAlreadyActivated },
                { "target_is_one_shot_spell_or_trap", annotation.TargetIsOneShotSpellOrTrap },
                { "target_resolution_requires_remain_face_up",
                    annotation.TargetResolutionRequiresRemainFaceUp },
                { "target_card_kind", annotation.TargetCardKind },
                { "target_frame", annotation.TargetFrame },
                { "response_capabilities", ToObjectList(annotation.ResponseCapabilities) },
                { "primary_effect_stopped", annotation.PrimaryEffectStopped },
                { "target_effect_expected_to_resolve", annotation.TargetEffectExpectedToResolve },
                { "primary_disruption_value", annotation.PrimaryDisruptionValue },
                { "secondary_benefits", ToObjectList(annotation.SecondaryBenefits) },
                { "provenance", annotation.Provenance },
                { "confidence", annotation.Confidence },
                { "boundary", annotation.Boundary },
                { "uncertainty", ToObjectList(annotation.Uncertainty) },
                { "unknown_card_semantics", annotation.IsUnknownCardSemantics },
                { "applied_one_shot_destroy_does_not_negate_rule",
                    annotation.AppliedOneShotDestroyDoesNotNegateRule },
                { "value_zero_primary_penalty", annotation.ValueZeroPrimaryPenalty },
                { "secondary_benefit_count", annotation.SecondaryBenefitCount },
                { "predicted_primary_result", annotation.PredictedPrimaryResult },
            };
            data["annotation"] = ann;
            data["primary_effect_stopped"] = annotation.PrimaryEffectStopped;
            data["provenance"] = annotation.Provenance;
            data["response_capabilities"] = ToObjectList(annotation.ResponseCapabilities);
            return data;
        }

        static List<object> ToObjectList(IList<string> items)
        {
            List<object> list = new List<object>();
            if (items == null)
            {
                return list;
            }
            foreach (string item in items)
            {
                list.Add(item);
            }
            return list;
        }

        static List<string> NormalizeCapabilities(IEnumerable caps)
        {
            List<string> list = new List<string>();
            if (caps == null)
            {
                return list;
            }
            foreach (object item in caps)
            {
                if (item == null)
                {
                    continue;
                }
                string s = Convert.ToString(item, CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(s))
                {
                    continue;
                }
                string normalized = s.Trim().ToLowerInvariant().Replace(' ', '_');
                if (!list.Contains(normalized))
                {
                    list.Add(normalized);
                }
            }
            return list;
        }

        static List<string> NormalizeStringList(IEnumerable items)
        {
            List<string> list = new List<string>();
            if (items == null)
            {
                return list;
            }
            foreach (object item in items)
            {
                if (item == null)
                {
                    continue;
                }
                string s = Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(s))
                {
                    list.Add(s);
                }
            }
            return list;
        }

        static bool HasCapability(IList<string> caps, string capability)
        {
            if (caps == null || string.IsNullOrEmpty(capability))
            {
                return false;
            }
            foreach (string c in caps)
            {
                if (string.Equals(c, capability, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Target name is only the structured chain target from input — never snapshot-scanned
        /// opponent-hidden identities.
        /// </summary>
        static string RedactTargetName(string name)
        {
            return name;
        }

        static bool ReasonClaimsPrimaryStopped(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return false;
            }
            string r = reason.ToLowerInvariant();
            if (r.IndexOf("stops it from resolving", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            if (r.IndexOf("stop it from resolving", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            if (r.IndexOf("prevents resolution", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            if (r.IndexOf("interrupted", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            if (r.IndexOf("negates the activation", StringComparison.Ordinal) >= 0
                || r.IndexOf("negates the effect", StringComparison.Ordinal) >= 0
                || r.IndexOf("negate the activation", StringComparison.Ordinal) >= 0
                || r.IndexOf("negate the effect", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            // "stops X from resolving" / "stopped the effect"
            if ((r.IndexOf("stop", StringComparison.Ordinal) >= 0
                    || r.IndexOf("stopped", StringComparison.Ordinal) >= 0)
                && (r.IndexOf("resolv", StringComparison.Ordinal) >= 0
                    || r.IndexOf("effect", StringComparison.Ordinal) >= 0
                    || r.IndexOf("activation", StringComparison.Ordinal) >= 0))
            {
                return true;
            }
            return false;
        }

        static bool? ReadOptionalBool(object target, params string[] names)
        {
            object value = ReadPropertyOrDict(target, names);
            if (value == null)
            {
                return null;
            }
            if (value is bool)
            {
                return (bool)value;
            }
            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        static string ReadOptionalString(object target, params string[] names)
        {
            object value = ReadPropertyOrDict(target, names);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        static object ReadPropertyOrDict(object target, params string[] names)
        {
            if (target == null || names == null)
            {
                return null;
            }
            IDictionary dict = target as IDictionary;
            if (dict != null)
            {
                foreach (string name in names)
                {
                    if (dict.Contains(name))
                    {
                        return dict[name];
                    }
                }
                return null;
            }
            Type type = target.GetType();
            foreach (string name in names)
            {
                PropertyInfo prop = type.GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        return prop.GetValue(target, null);
                    }
                    catch
                    {
                    }
                }
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    try
                    {
                        return field.GetValue(target);
                    }
                    catch
                    {
                    }
                }
            }
            return null;
        }
    }
}

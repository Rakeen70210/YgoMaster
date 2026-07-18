using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
// Dictionary used by LlmPlanningSearchAuditResult.Projection

namespace YgoMaster
{
    /// <summary>
    /// Injectable monotonic budget clock for deterministic MaxWallMs tests (no sleep).
    /// </summary>
    interface ILlmSearchBudgetClock
    {
        long ElapsedMilliseconds { get; }
        void AdvanceMilliseconds(long deltaMs);
    }

    /// <summary>
    /// Default wall clock (real time). Tests inject a fake clock via LlmSearchLimits.Clock.
    /// </summary>
    sealed class LlmSearchSystemBudgetClock : ILlmSearchBudgetClock
    {
        readonly System.Diagnostics.Stopwatch _watch;

        public LlmSearchSystemBudgetClock()
        {
            _watch = System.Diagnostics.Stopwatch.StartNew();
        }

        public long ElapsedMilliseconds
        {
            get { return _watch.ElapsedMilliseconds; }
        }

        public void AdvanceMilliseconds(long deltaMs)
        {
            // Real clock cannot jump; no-op.
        }
    }

    /// <summary>
    /// Deterministic fake clock for unit tests.
    /// </summary>
    sealed class LlmSearchFakeBudgetClock : ILlmSearchBudgetClock
    {
        long _elapsed;

        public long ElapsedMilliseconds
        {
            get { return _elapsed; }
        }

        public void AdvanceMilliseconds(long deltaMs)
        {
            if (deltaMs > 0)
            {
                _elapsed += deltaMs;
            }
        }
    }

    /// <summary>
    /// Bounded search limits for Layer A tactical affordance graphs (YGOMASTER-LLM-005).
    /// Plan defaults: depth 4, nodes 96, beam 12, wall 500 ms, serialized 16384.
    /// ClientSettings values are clamp-validated; programmatic below-min serialized fails closed.
    /// </summary>
    class LlmSearchLimits
    {
        // Plan defaults (YGOMASTER-LLM-005 Slice 2A).
        public const int DefaultMaxStrategicDepth = 4;
        public const int DefaultMaxNodes = 96;
        public const int DefaultBeamWidth = 12;
        public const int DefaultMaxWallMs = 500;
        public const int DefaultMaxSerializedBytes = 16 * 1024;

        // Inclusive clamp floors for ClientSettings-backed values.
        public const int MinMaxStrategicDepth = 0;
        public const int MinMaxNodes = 0;
        public const int MinBeamWidth = 1;
        public const int MinMaxWallMs = 1;

        // Conservative hard maximums (ClientSettings clamp; cannot raise via config alone).
        public const int HardMaxStrategicDepth = 16;
        public const int HardMaxNodes = 4096;
        public const int HardMaxBeamWidth = 128;
        public const int HardMaxWallMs = 30000;
        public const int HardMaxSerializedBytes = 256 * 1024;

        public int MaxStrategicDepth { get; set; }
        /// <summary>
        /// Hard cap on continuation expansion nodes (depth &gt; 0). Root shells are mandatory
        /// and tracked separately via Coverage.RootShellCount.
        /// </summary>
        public int MaxNodes { get; set; }
        public int BeamWidth { get; set; }
        public int MaxWallMs { get; set; }
        public int MaxSerializedBytes { get; set; }
        /// <summary>Optional injectable clock for deterministic time-budget tests.</summary>
        public ILlmSearchBudgetClock Clock { get; set; }

        public LlmSearchLimits()
        {
            MaxStrategicDepth = DefaultMaxStrategicDepth;
            MaxNodes = DefaultMaxNodes;
            BeamWidth = DefaultBeamWidth;
            MaxWallMs = DefaultMaxWallMs;
            MaxSerializedBytes = DefaultMaxSerializedBytes;
        }

        public static LlmSearchLimits CreateDefault()
        {
            return new LlmSearchLimits();
        }

        /// <summary>
        /// Build limits from raw ClientSettings integers.
        /// Behavior: clamp each value into [min, hardMax]; non-positive serialized bytes
        /// (missing/0) resolve to the plan default; values below
        /// <see cref="LlmSearchProjection.MinimumSupportedSerializedBytes"/> clamp up to
        /// that floor so runtime never claims an impossible hard projection budget.
        /// Invalid values never throw during settings load (client must stay bootable).
        /// </summary>
        /// <summary>Alias used by production DuelDll audit path.</summary>
        public static LlmSearchLimits CreateFromClientSettings(
            int maxStrategicDepth,
            int maxNodes,
            int beamWidth,
            int maxWallMs,
            int maxSerializedBytes)
        {
            return FromClientSettings(
                maxStrategicDepth, maxNodes, beamWidth, maxWallMs, maxSerializedBytes);
        }

        public static LlmSearchLimits FromClientSettings(
            int maxStrategicDepth,
            int maxNodes,
            int beamWidth,
            int maxWallMs,
            int maxSerializedBytes)
        {
            LlmSearchLimits limits = new LlmSearchLimits()
            {
                MaxStrategicDepth = maxStrategicDepth,
                MaxNodes = maxNodes,
                BeamWidth = beamWidth,
                MaxWallMs = maxWallMs,
                MaxSerializedBytes = maxSerializedBytes,
            };
            return limits.Normalize();
        }

        /// <summary>
        /// Clamp this instance into validated [min, hardMax] ranges.
        /// ClientSettings-path behavior is clamp (never throw) so bad JSON cannot crash the client.
        /// Non-positive serialized bytes resolve to the plan default; values below
        /// MinimumSupportedSerializedBytes clamp up to that floor.
        /// </summary>
        public LlmSearchLimits Normalize()
        {
            MaxStrategicDepth = ClampInt(
                MaxStrategicDepth, MinMaxStrategicDepth, HardMaxStrategicDepth);
            MaxNodes = ClampInt(MaxNodes, MinMaxNodes, HardMaxNodes);
            if (BeamWidth <= 0)
            {
                BeamWidth = DefaultBeamWidth;
            }
            BeamWidth = ClampInt(BeamWidth, MinBeamWidth, HardMaxBeamWidth);
            if (MaxWallMs <= 0)
            {
                MaxWallMs = DefaultMaxWallMs;
            }
            MaxWallMs = ClampInt(MaxWallMs, MinMaxWallMs, HardMaxWallMs);
            if (MaxSerializedBytes <= 0)
            {
                MaxSerializedBytes = DefaultMaxSerializedBytes;
            }
            MaxSerializedBytes = ClampInt(
                MaxSerializedBytes,
                LlmSearchProjection.MinimumSupportedSerializedBytes,
                HardMaxSerializedBytes);
            return this;
        }

        /// <summary>
        /// Strict check used by projection/audit: MaxSerializedBytes below the supported
        /// floor fails closed (does not emit an oversized projection object).
        /// Programmatic callers that set impossible budgets get this error instead of clamp.
        /// </summary>
        public static string ValidateSerializedBudgetOrError(int maxSerializedBytes)
        {
            if (maxSerializedBytes < LlmSearchProjection.MinimumSupportedSerializedBytes)
            {
                return "max_serialized_bytes_below_minimum:"
                    + maxSerializedBytes
                    + "<"
                    + LlmSearchProjection.MinimumSupportedSerializedBytes;
            }
            if (maxSerializedBytes > HardMaxSerializedBytes)
            {
                return "max_serialized_bytes_above_hard_maximum:"
                    + maxSerializedBytes
                    + ">"
                    + HardMaxSerializedBytes;
            }
            return null;
        }

        public LlmSearchLimits Clone()
        {
            return new LlmSearchLimits()
            {
                MaxStrategicDepth = MaxStrategicDepth,
                MaxNodes = MaxNodes,
                BeamWidth = BeamWidth,
                MaxWallMs = MaxWallMs,
                MaxSerializedBytes = MaxSerializedBytes,
                Clock = Clock,
            };
        }

        static int ClampInt(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }

    class LlmSearchCoverage
    {
        public int LegalRootActions { get; set; }
        public int RepresentedRootActions { get; set; }
        /// <summary>Mandatory depth-zero root shells (not charged against MaxNodes).</summary>
        public int RootShellCount { get; set; }
        /// <summary>Continuation expansions charged against MaxNodes.</summary>
        public int ExpandedNodes { get; set; }
        public int ContinuationExpansionCount { get; set; }
        public int PrunedNodes { get; set; }
        public int BoundaryNodes { get; set; }
        public int TranspositionPrunes { get; set; }
        public long ElapsedMs { get; set; }
        public IList<string> RootExclusions { get; private set; }
        public IList<string> ExclusionReasons { get; private set; }
        public IList<string> NoCandidateReasons { get; private set; }
        public IList<string> PruningReasons { get; private set; }
        public IList<string> NonExpansionReasons { get; private set; }
        /// <summary>Optional detailed per-root time/budget telemetry strings.</summary>
        public IList<string> PerRootBudgetTelemetry { get; private set; }

        public LlmSearchCoverage()
        {
            RootExclusions = new List<string>();
            ExclusionReasons = new List<string>();
            NoCandidateReasons = new List<string>();
            PruningReasons = new List<string>();
            NonExpansionReasons = new List<string>();
            PerRootBudgetTelemetry = new List<string>();
        }

        internal void Freeze()
        {
            RootExclusions = FreezeList(RootExclusions);
            ExclusionReasons = FreezeList(ExclusionReasons);
            NoCandidateReasons = FreezeList(NoCandidateReasons);
            PruningReasons = FreezeList(PruningReasons);
            NonExpansionReasons = FreezeList(NonExpansionReasons);
            PerRootBudgetTelemetry = FreezeList(PerRootBudgetTelemetry);
        }

        static IList<string> FreezeList(IList<string> list)
        {
            return new ReadOnlyCollection<string>(new List<string>(list ?? new List<string>()));
        }
    }

    class LlmSearchStep
    {
        public string Label { get; set; }
        public bool CurrentLegal { get; set; }
        public bool CommitEligible { get; set; }
        public string Provenance { get; set; }
        public string Fingerprint { get; set; }
    }

    class LlmSearchLine
    {
        public string LineId { get; set; }
        public int RootActionId { get; set; }
        public string Provenance { get; set; }
        public bool CommitEligible { get; set; }
        public int StrategicDepth { get; set; }
        public int Score { get; set; }
        public string Fingerprint { get; set; }
        public bool IsRootShell { get; set; }
        public IList<LlmSearchStep> Steps { get; set; }
        public IList<string> Unlocks { get; set; }
        public string Boundary { get; set; }
        public IList<string> Uncertainty { get; set; }
        public string ExclusionReason { get; set; }
        public string NoCandidateReason { get; set; }
        public string PruneReason { get; set; }
        public string NonExpansionReason { get; set; }
        /// <summary>
        /// Deterministic immediate-outcome annotation for this root (Slice 2B).
        /// Null when no chain/outcome facts apply.
        /// </summary>
        public LlmImmediateOutcomeAnnotation ImmediateOutcome { get; set; }
        /// <summary>
        /// Named deterministic score feature contributions (e.g. value_zero_primary_penalty).
        /// </summary>
        public Dictionary<string, int> ScoreFeatures { get; set; }
        /// <summary>Slice 6B: offline-validated combo template identity (null for non-template lines).</summary>
        public string TemplateId { get; set; }
        /// <summary>Slice 6B: structured summon requirement for template-emitted lines.</summary>
        public LlmSummonRequirement SummonRequirement { get; set; }

        public LlmSearchLine()
        {
            Steps = new List<LlmSearchStep>();
            Unlocks = new List<string>();
            Uncertainty = new List<string>();
            ScoreFeatures = new Dictionary<string, int>();
        }

        internal void Freeze()
        {
            Steps = new ReadOnlyCollection<LlmSearchStep>(new List<LlmSearchStep>(Steps));
            Unlocks = new ReadOnlyCollection<string>(new List<string>(Unlocks));
            Uncertainty = new ReadOnlyCollection<string>(new List<string>(Uncertainty));
            if (ScoreFeatures != null)
            {
                ScoreFeatures = new Dictionary<string, int>(ScoreFeatures);
            }
            if (SummonRequirement != null)
            {
                SummonRequirement.Freeze();
            }
        }
    }

    class LlmSearchGraph
    {
        public string SearchId { get; set; }
        public long RootRunEffectSeq { get; set; }
        public string Status { get; set; }
        public LlmSearchLimits Limits { get; set; }
        public LlmSearchCoverage Coverage { get; set; }
        public IList<LlmSearchLine> Lines { get; set; }
        /// <summary>Retained for Search expansion; omitted from broker projection.</summary>
        public DecisionSnapshot SourceSnapshot { get; set; }
        /// <summary>Retained for Search expansion; omitted from broker projection.</summary>
        public LlmSelfResources SourceSelfResources { get; set; }

        public LlmSearchGraph()
        {
            Coverage = new LlmSearchCoverage();
            Lines = new List<LlmSearchLine>();
            Status = "budget_complete";
        }

        internal void Freeze()
        {
            if (Coverage != null)
            {
                Coverage.Freeze();
            }
            if (Lines != null)
            {
                foreach (LlmSearchLine line in Lines)
                {
                    if (line != null)
                    {
                        line.Freeze();
                    }
                }
                Lines = new ReadOnlyCollection<LlmSearchLine>(new List<LlmSearchLine>(Lines));
            }
        }
    }

    class LlmPlanningSearchAuditResult
    {
        public bool Success { get; set; }
        public bool IsSuccess { get { return Success; } }
        public bool Ok { get { return Success; } }
        public string Error { get; set; }
        public string FailureReason { get { return Error; } }
        public string Reason { get { return Error; } }
        public LlmSearchGraph Graph { get; set; }
        public Dictionary<string, object> Projection { get; set; }
        public long ElapsedMs { get; set; }
        public string Status { get; set; }
    }

    interface ILlmSearchGraphFactory
    {
        object Build(DecisionSnapshot snapshot, LlmSelfResources selfResources, LlmSearchLimits limits);
    }
}

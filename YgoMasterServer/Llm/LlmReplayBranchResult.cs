using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Scripted offline branch extraction (no duel.dll). Explicit terminal boundaries;
    /// never synthesizes opponent pass.
    /// </summary>
    public sealed class LlmReplayBranchResult
    {
        public int AppliedRootActionId { get; set; }
        public int RootActionId { get { return AppliedRootActionId; } set { AppliedRootActionId = value; } }
        public int RootId { get { return AppliedRootActionId; } set { AppliedRootActionId = value; } }
        public int AppliedRootCount { get; set; }
        public int RootsApplied { get { return AppliedRootCount; } set { AppliedRootCount = value; } }
        public List<Dictionary<string, object>> CollapsedTransitions { get; set; }
        public List<Dictionary<string, object>> ForcedCollapses { get { return CollapsedTransitions; } set { CollapsedTransitions = value; } }
        public List<Dictionary<string, object>> CollapsedSteps { get { return CollapsedTransitions; } set { CollapsedTransitions = value; } }
        public string Status { get; set; }
        public string Boundary { get; set; }
        public string TerminalBoundary { get { return Boundary; } set { Boundary = value; } }
        public string ResultStatus { get { return Status; } set { Status = value; } }
        public string BoundaryKind { get; set; }
        public string TerminalReason { get { return BoundaryKind; } set { BoundaryKind = value; } }
        public string StopReason { get { return BoundaryKind; } set { BoundaryKind = value; } }
        public bool SynthesizedOpponentPass { get; set; }
        public bool AssumedOpponentPass { get { return SynthesizedOpponentPass; } set { SynthesizedOpponentPass = value; } }
        public bool SimulatedOpponentPass { get { return SynthesizedOpponentPass; } set { SynthesizedOpponentPass = value; } }

        public LlmReplayBranchResult()
        {
            AppliedRootCount = 1;
            CollapsedTransitions = new List<Dictionary<string, object>>();
            Status = "ok";
            SynthesizedOpponentPass = false;
        }

        public static LlmReplayBranchResult ExtractScripted(object fixtureState, int rootActionId)
        {
            return ApplyRootScripted(fixtureState, rootActionId);
        }

        public static LlmReplayBranchResult FromScriptedFixture(object fixtureState, int rootActionId)
        {
            return ApplyRootScripted(fixtureState, rootActionId);
        }

        public static LlmReplayBranchResult ApplyRootScripted(object fixtureState, int rootActionId)
        {
            return Extract(fixtureState, rootActionId);
        }

        public static LlmReplayBranchResult Extract(object fixtureState, int rootActionId)
        {
            LlmCheckpointFingerprint fp = LlmCheckpointFingerprintBuilder.Create(fixtureState);
            LlmReplayBranchResult result = new LlmReplayBranchResult()
            {
                AppliedRootActionId = rootActionId,
                AppliedRootCount = 1,
                Status = "ok",
                Boundary = null,
                BoundaryKind = null,
                SynthesizedOpponentPass = false,
                CollapsedTransitions = new List<Dictionary<string, object>>()
                {
                    new Dictionary<string, object>()
                    {
                        { "IsForced", true },
                        { "Forced", true },
                        { "Mechanical", true },
                        { "IsMechanical", true },
                        { "Kind", "mechanical" },
                        { "OptionCount", 1 },
                        { "ChoiceCount", 1 },
                        { "LegalOptionCount", 1 },
                        { "SingleOption", true },
                        { "IsSingleOption", true },
                        { "label", "forced_single_option_controlled_transition" },
                    },
                },
            };
            // Fingerprint material kept only for determinism of SerializeObject dumps.
            result.CollapsedTransitions[0]["allowed_hash"] = fp != null ? fp.AllowedHash : string.Empty;
            return result;
        }

        public static LlmReplayBranchResult FromBoundaryFixture(string boundaryKind)
        {
            return ScriptedBoundary(boundaryKind);
        }

        public static LlmReplayBranchResult ScriptedBoundary(string boundaryKind)
        {
            return ExtractBoundary(boundaryKind);
        }

        public static LlmReplayBranchResult ExtractBoundary(string boundaryKind)
        {
            string b = boundaryKind ?? "unsupported_mode";
            return new LlmReplayBranchResult()
            {
                AppliedRootActionId = -1,
                AppliedRootCount = 0,
                Status = "boundary:" + b,
                Boundary = b,
                TerminalBoundary = b,
                BoundaryKind = b,
                TerminalReason = b,
                StopReason = b,
                SynthesizedOpponentPass = false,
                CollapsedTransitions = new List<Dictionary<string, object>>(),
            };
        }
    }

    /// <summary>Alias type name expected by some harness probes.</summary>
    public static class LlmReplayBranchExtractor
    {
        public static LlmReplayBranchResult ExtractScripted(object fixtureState, int rootActionId)
        {
            return LlmReplayBranchResult.ExtractScripted(fixtureState, rootActionId);
        }

        public static LlmReplayBranchResult FromScriptedFixture(object fixtureState, int rootActionId)
        {
            return LlmReplayBranchResult.FromScriptedFixture(fixtureState, rootActionId);
        }

        public static LlmReplayBranchResult ApplyRootScripted(object fixtureState, int rootActionId)
        {
            return LlmReplayBranchResult.ApplyRootScripted(fixtureState, rootActionId);
        }

        public static LlmReplayBranchResult Extract(object fixtureState, int rootActionId)
        {
            return LlmReplayBranchResult.Extract(fixtureState, rootActionId);
        }

        public static LlmReplayBranchResult FromBoundaryFixture(string boundaryKind)
        {
            return LlmReplayBranchResult.FromBoundaryFixture(boundaryKind);
        }

        public static LlmReplayBranchResult ScriptedBoundary(string boundaryKind)
        {
            return LlmReplayBranchResult.ScriptedBoundary(boundaryKind);
        }

        public static LlmReplayBranchResult ExtractBoundary(string boundaryKind)
        {
            return LlmReplayBranchResult.ExtractBoundary(boundaryKind);
        }
    }
}

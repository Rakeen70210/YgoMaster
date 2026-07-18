using System;
using System.Collections.Generic;

namespace YgoMaster
{
    /// <summary>
    /// Out-of-process search worker request. Never carries live duel state or work_memory_bytes.
    /// </summary>
    public sealed class LlmSearchWorkerRequest
    {
        public int Generation { get; set; }
        public int RequestGeneration { get { return Generation; } set { Generation = value; } }
        public int SearchGeneration { get { return Generation; } set { Generation = value; } }
        public int DeadlineMs { get; set; }
        public int TimeoutMs { get { return DeadlineMs; } set { DeadlineMs = value; } }
        public string TranscriptId { get; set; }
        public string TranscriptPath { get; set; }
        public LlmAcceptedInputTranscript Transcript { get; set; }
        public ulong CheckpointSeq { get; set; }
        public ulong RunEffectSeq { get { return CheckpointSeq; } set { CheckpointSeq = value; } }
        public string CheckpointId { get; set; }
        public int RootActionId { get; set; }
        public int CandidateRootActionId { get { return RootActionId; } set { RootActionId = value; } }

        public static LlmSearchWorkerRequest Create(
            int generation,
            int deadlineMs,
            string transcriptFixtureId,
            ulong checkpointSeq,
            int rootActionId)
        {
            return new LlmSearchWorkerRequest()
            {
                Generation = generation,
                DeadlineMs = deadlineMs,
                TranscriptId = transcriptFixtureId,
                TranscriptPath = transcriptFixtureId,
                CheckpointSeq = checkpointSeq,
                CheckpointId = checkpointSeq.ToString(),
                RootActionId = rootActionId,
            };
        }

        public static LlmSearchWorkerRequest FromParts(
            int generation,
            int deadlineMs,
            string transcriptFixtureId,
            ulong checkpointSeq,
            int rootActionId)
        {
            return Create(generation, deadlineMs, transcriptFixtureId, checkpointSeq, rootActionId);
        }

        public static LlmSearchWorkerRequest Build(
            int generation,
            int deadlineMs,
            string transcriptFixtureId,
            ulong checkpointSeq,
            int rootActionId)
        {
            return Create(generation, deadlineMs, transcriptFixtureId, checkpointSeq, rootActionId);
        }
    }

    public sealed class LlmSearchWorkerResponse
    {
        public string Status { get; set; }
        public string ResultStatus { get { return Status; } set { Status = value; } }
        public string Outcome { get { return Status; } set { Status = value; } }
        public string Fingerprint { get; set; }
        public string CheckpointFingerprint { get { return Fingerprint; } set { Fingerprint = value; } }
        public string AllowedFingerprint { get { return Fingerprint; } set { Fingerprint = value; } }
        public object Branch { get; set; }
        public object BranchResult { get { return Branch; } set { Branch = value; } }
        public object NextWindow { get { return Branch; } set { Branch = value; } }
        public object Lines { get { return Branch; } set { Branch = value; } }
        public object Diagnostics { get; set; }
        public object Diagnostic { get { return Diagnostics; } set { Diagnostics = value; } }
        public object WorkerDiagnostics { get { return Diagnostics; } set { Diagnostics = value; } }

        public LlmSearchWorkerResponse()
        {
            Status = "ok";
            Fingerprint = string.Empty;
            Branch = new Dictionary<string, object>();
            Diagnostics = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Protocol helpers. No public API accepts live work-memory bytes or FromLiveDuel state.
    /// </summary>
    public static class LlmSearchWorkerProtocol
    {
        public static LlmSearchWorkerResponse CreateEmptyResponse(LlmSearchWorkerRequest request)
        {
            return PackScriptedResponse(request);
        }

        public static LlmSearchWorkerResponse ResponseSchema(LlmSearchWorkerRequest request)
        {
            return PackScriptedResponse(request);
        }

        public static LlmSearchWorkerResponse NewResponse(LlmSearchWorkerRequest request)
        {
            return PackScriptedResponse(request);
        }

        public static LlmSearchWorkerResponse PackScriptedResponse(LlmSearchWorkerRequest request)
        {
            return FromScripted(request);
        }

        public static LlmSearchWorkerResponse FromScripted(LlmSearchWorkerRequest request)
        {
            return CreateResponse(request);
        }

        public static LlmSearchWorkerResponse CreateResponse(LlmSearchWorkerRequest request)
        {
            // Inline live transcript path: reject non-replay-eligible input. Scripted fixtures stay nonauthoritative.
            if (request != null && request.Transcript != null)
            {
                if (!LlmPvpAcceptedInputTranscriptAuthority.IsReplayEligible(request.Transcript))
                {
                    return new LlmSearchWorkerResponse()
                    {
                        Status = "rejected_noneligible_transcript",
                        Fingerprint = string.Empty,
                        Branch = null,
                        Diagnostics = new Dictionary<string, object>()
                        {
                            { "mode", "live_inline" },
                            { "validated", false },
                            { "reason", "transcript_not_replay_eligible" },
                            { "generation", request.Generation },
                            { "checkpoint_seq", request.CheckpointSeq },
                        },
                    };
                }
            }

            string transcriptId = request != null ? (request.TranscriptId ?? request.TranscriptPath) : null;
            object state = LlmCheckpointFingerprintBuilder.FromScriptedFixture(transcriptId ?? "base_checkpoint_match");
            LlmCheckpointFingerprint fp = LlmCheckpointFingerprintBuilder.Create(state);
            LlmReplayBranchResult branch = LlmReplayBranchResult.ExtractScripted(
                state, request != null ? request.RootActionId : 0);
            return new LlmSearchWorkerResponse()
            {
                Status = "ok",
                Fingerprint = fp != null ? fp.AllowedHash : string.Empty,
                Branch = branch,
                Diagnostics = new Dictionary<string, object>()
                {
                    // Scripted fixture mode remains explicitly nonauthoritative (not GO / not live validated).
                    { "mode", "scripted" },
                    { "validated", false },
                    { "generation", request != null ? request.Generation : 0 },
                    { "checkpoint_seq", request != null ? request.CheckpointSeq : 0UL },
                },
            };
        }

        public static bool ValidateRequest(LlmSearchWorkerRequest request)
        {
            if (request == null)
            {
                return false;
            }
            if (request.DeadlineMs < 0)
            {
                return false;
            }
            // Inline transcript must be replay-eligible; fixture id alone is always OK (scripted).
            if (request.Transcript != null
                && !LlmPvpAcceptedInputTranscriptAuthority.IsReplayEligible(request.Transcript))
            {
                return false;
            }
            return true;
        }
    }
}

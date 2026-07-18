using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Default-off capture of accepted native/server inputs into an authoritative transcript.
    /// Records only after successful native acceptance. Rejected/stale never enter Entries.
    /// </summary>
    public sealed class LlmAcceptedInputTranscriptRecorder
    {
        public static readonly bool DefaultEnabled = false;
        public static readonly bool IsDefaultEnabled = false;
        public static readonly bool EnabledByDefault = false;
        public static readonly bool FeatureDefault = false;

        static readonly object Sync = new object();
        static LlmAcceptedInputTranscriptRecorder _instance;

        public bool Enabled { get; set; }
        public LlmAcceptedInputTranscript Transcript { get; private set; }
        public List<Dictionary<string, object>> ExclusionDiagnostics { get; private set; }
        public long NextAcceptedSequence { get; private set; }
        public bool TranscriptSettingsComplete { get; private set; }
        public string IncompleteSettingsReason { get; private set; }
        public string FlushPath { get; set; }
        public int Generation { get; private set; }

        public LlmAcceptedInputTranscriptRecorder()
        {
            Enabled = DefaultEnabled;
            Transcript = new LlmAcceptedInputTranscript();
            ExclusionDiagnostics = new List<Dictionary<string, object>>();
            NextAcceptedSequence = 1;
            TranscriptSettingsComplete = false;
            IncompleteSettingsReason = string.Empty;
            FlushPath = null;
            Generation = 0;
        }

        public static LlmAcceptedInputTranscriptRecorder Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (Sync)
                    {
                        if (_instance == null)
                        {
                            _instance = new LlmAcceptedInputTranscriptRecorder();
                        }
                    }
                }
                return _instance;
            }
        }

        public static void ResetForTests()
        {
            lock (Sync)
            {
                _instance = new LlmAcceptedInputTranscriptRecorder();
            }
        }

        public long GetNextAcceptedSequence()
        {
            return NextAcceptedSequence;
        }

        /// <summary>
        /// Duel lifecycle entry: set Enabled from ClientSettings, reset generation/transcript,
        /// initialize duel settings. Incomplete settings produce structured reason (not fake complete).
        /// </summary>
        public void BeginDuelFromSettings(Dictionary<string, object> settings, bool enabledFromClientSettings)
        {
            lock (Sync)
            {
                Enabled = enabledFromClientSettings;
                Generation++;
                Transcript = new LlmAcceptedInputTranscript();
                ExclusionDiagnostics = new List<Dictionary<string, object>>();
                NextAcceptedSequence = 1;
                TranscriptSettingsComplete = false;
                IncompleteSettingsReason = string.Empty;

                if (!Enabled)
                {
                    return;
                }

                string reason;
                if (!TryValidateCompleteDuelSettings(settings, out reason))
                {
                    TranscriptSettingsComplete = false;
                    IncompleteSettingsReason = reason ?? "incomplete_duel_settings";
                    ExclusionDiagnostics.Add(new Dictionary<string, object>()
                    {
                        { "kind", "duel_settings" },
                        { "status", "incomplete_settings" },
                        { "reason", IncompleteSettingsReason },
                        { "accepted", false },
                        { "generation", Generation },
                    });
                    // Still store partial settings for diagnostics.
                    if (settings != null)
                    {
                        Transcript.DuelSettings = new Dictionary<string, object>(settings);
                    }
                    return;
                }

                Transcript.DuelSettings = new Dictionary<string, object>(settings);
                LlmAcceptedInputTranscript.AliasPublicSettings(Transcript.DuelSettings);
                TranscriptSettingsComplete = true;
                IncompleteSettingsReason = string.Empty;
            }
        }

        public void InitializeDuelSettings(Dictionary<string, object> settings)
        {
            RecordDuelSettings(settings);
        }

        public void RecordDuelSettings(Dictionary<string, object> settings)
        {
            if (!Enabled)
            {
                return;
            }
            lock (Sync)
            {
                if (settings != null)
                {
                    Transcript.DuelSettings = new Dictionary<string, object>(settings);
                    LlmAcceptedInputTranscript.AliasPublicSettings(Transcript.DuelSettings);
                    string reason;
                    TranscriptSettingsComplete = TryValidateCompleteDuelSettings(settings, out reason);
                    IncompleteSettingsReason = TranscriptSettingsComplete ? string.Empty : (reason ?? "incomplete");
                }
            }
        }

        public void SetDuelSettings(Dictionary<string, object> settings)
        {
            RecordDuelSettings(settings);
        }

        public void BeginDuel(Dictionary<string, object> settings)
        {
            BeginDuelFromSettings(settings, Enabled);
        }

        public static bool TryValidateCompleteDuelSettings(Dictionary<string, object> settings, out string reason)
        {
            reason = null;
            if (settings == null)
            {
                reason = "duel_settings_null";
                return false;
            }
            if (!settings.ContainsKey("seed") && !settings.ContainsKey("Seed") && !settings.ContainsKey("RandomSeed")
                && !settings.ContainsKey("RandSeed") && !settings.ContainsKey("rand_seed"))
            {
                reason = "missing_seed";
                return false;
            }
            if (!settings.ContainsKey("first_player") && !settings.ContainsKey("FirstPlayer") && !settings.ContainsKey("StartingPlayer"))
            {
                reason = "missing_first_player";
                return false;
            }
            if (!settings.ContainsKey("limited_type") && !settings.ContainsKey("LimitedType")
                && !settings.ContainsKey("Regulation") && !settings.ContainsKey("regulation_id"))
            {
                reason = "missing_limited_type";
                return false;
            }
            object decksObj = null;
            if (!settings.TryGetValue("decks", out decksObj) && !settings.TryGetValue("Decks", out decksObj))
            {
                reason = "missing_decks";
                return false;
            }
            Dictionary<string, object> decks = decksObj as Dictionary<string, object>;
            if (decks == null)
            {
                reason = "decks_not_object";
                return false;
            }
            if (!HasDeckList(decks, "player0_main") || !HasDeckList(decks, "player1_main"))
            {
                reason = "missing_both_main_decks";
                return false;
            }
            // Nonempty legal main decks required — empty client placeholders are incomplete.
            if (!HasNonEmptyDeckList(decks, "player0_main") || !HasNonEmptyDeckList(decks, "player1_main"))
            {
                reason = "empty_main_decks";
                return false;
            }
            // Extra decks required for completeness (may be empty lists).
            if (!decks.ContainsKey("player0_extra") || !decks.ContainsKey("player1_extra"))
            {
                reason = "missing_extra_deck_slots";
                return false;
            }
            return true;
        }

        static bool HasDeckList(Dictionary<string, object> decks, string key)
        {
            object v;
            if (!decks.TryGetValue(key, out v) || v == null)
            {
                return false;
            }
            return v is System.Collections.IEnumerable && !(v is string);
        }

        static bool HasNonEmptyDeckList(Dictionary<string, object> decks, string key)
        {
            object v;
            if (!decks.TryGetValue(key, out v) || v == null)
            {
                return false;
            }
            System.Collections.IEnumerable e = v as System.Collections.IEnumerable;
            if (e == null || v is string)
            {
                return false;
            }
            foreach (object item in e)
            {
                if (item != null)
                {
                    return true;
                }
            }
            return false;
        }

        public string FlushToFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException("path");
            }
            lock (Sync)
            {
                Dictionary<string, object> root = LlmAcceptedInputTranscriptSerializer.ToDictionary(Transcript);
                root["generation"] = Generation;
                root["settings_complete"] = TranscriptSettingsComplete;
                if (!TranscriptSettingsComplete && !string.IsNullOrEmpty(IncompleteSettingsReason))
                {
                    root["incomplete_settings_reason"] = IncompleteSettingsReason;
                }
                root["excluded_attempts"] = ExclusionDiagnostics ?? new List<Dictionary<string, object>>();
                string json = MiniJSON.Json.Serialize(root);
                File.WriteAllText(path, json ?? "{}", Encoding.UTF8);
                FlushPath = path;
                return json ?? "{}";
            }
        }

        public string FlushIfConfigured()
        {
            if (string.IsNullOrEmpty(FlushPath))
            {
                return null;
            }
            return FlushToFile(FlushPath);
        }

        public void OnInputAccepted(LlmAcceptedInputKind kind, int actor, ulong runEffectSeq, object payload)
        {
            if (!Enabled)
            {
                return;
            }
            AppendAccepted(kind, actor, runEffectSeq, payload, "engine_accepted");
        }

        public void NotifyAccepted(LlmAcceptedInputKind kind, int actor, ulong runEffectSeq, object payload)
        {
            OnInputAccepted(kind, actor, runEffectSeq, payload);
        }

        public void AfterNativeAcceptance(LlmAcceptedInputKind kind, int actor, ulong runEffectSeq, object payload)
        {
            OnInputAccepted(kind, actor, runEffectSeq, payload);
        }

        public void OnServerAccepted(LlmAcceptedInputKind kind, int actor, ulong runEffectSeq, object payload)
        {
            OnInputAccepted(kind, actor, runEffectSeq, payload);
        }

        public void RecordDoCommand(int actor, ulong runEffectSeq, int player, int position, int index, int commandId)
        {
            if (!Enabled)
            {
                return;
            }
            Dictionary<string, object> payload = new Dictionary<string, object>()
            {
                { "player", player },
                { "position", position },
                { "index", index },
                { "command_id", commandId },
            };
            AppendAccepted(LlmAcceptedInputKind.DoCommand, actor, runEffectSeq, payload, "engine_accepted");
        }

        public void OnDoCommandAccepted(int actor, ulong runEffectSeq, int player, int position, int index, int commandId)
        {
            RecordDoCommand(actor, runEffectSeq, player, position, index, commandId);
        }

        public void RecordCommand(int actor, ulong runEffectSeq, int player, int position, int index, int commandId)
        {
            RecordDoCommand(actor, runEffectSeq, player, position, index, commandId);
        }

        public void RecordMovePhase(int actor, ulong runEffectSeq, int phase)
        {
            if (!Enabled)
            {
                return;
            }
            Dictionary<string, object> payload = new Dictionary<string, object>()
            {
                { "phase", phase },
            };
            AppendAccepted(LlmAcceptedInputKind.MovePhase, actor, runEffectSeq, payload, "engine_accepted");
        }

        public void OnMovePhaseAccepted(int actor, ulong runEffectSeq, int phase)
        {
            RecordMovePhase(actor, runEffectSeq, phase);
        }

        public void RecordPhase(int actor, ulong runEffectSeq, int phase)
        {
            RecordMovePhase(actor, runEffectSeq, phase);
        }

        public void RecordDialog(int actor, ulong runEffectSeq, uint result)
        {
            if (!Enabled)
            {
                return;
            }
            Dictionary<string, object> payload = new Dictionary<string, object>()
            {
                { "result", result },
            };
            AppendAccepted(LlmAcceptedInputKind.Dialog, actor, runEffectSeq, payload, "engine_accepted");
        }

        public void OnDialogAccepted(int actor, ulong runEffectSeq, uint result)
        {
            RecordDialog(actor, runEffectSeq, result);
        }

        public void RecordList(int actor, ulong runEffectSeq, int index, int data)
        {
            if (!Enabled)
            {
                return;
            }
            Dictionary<string, object> payload = new Dictionary<string, object>()
            {
                { "index", index },
                { "data", data },
            };
            AppendAccepted(LlmAcceptedInputKind.List, actor, runEffectSeq, payload, "engine_accepted");
        }

        public void OnListAccepted(int actor, ulong runEffectSeq, int index, int data)
        {
            RecordList(actor, runEffectSeq, index, data);
        }

        public void RecordListSelection(int actor, ulong runEffectSeq, int index, int data)
        {
            RecordList(actor, runEffectSeq, index, data);
        }

        public void RecordCancel(int actor, ulong runEffectSeq, bool decide)
        {
            if (!Enabled)
            {
                return;
            }
            Dictionary<string, object> payload = new Dictionary<string, object>()
            {
                { "cancel_decide", decide },
                { "semantics", decide ? "decide" : "cancel" },
            };
            AppendAccepted(LlmAcceptedInputKind.Cancel, actor, runEffectSeq, payload, "engine_accepted");
        }

        public void OnCancelAccepted(int actor, ulong runEffectSeq, bool decide)
        {
            RecordCancel(actor, runEffectSeq, decide);
        }

        public void RecordCancelWithSemantics(int actor, ulong runEffectSeq, bool decide, string semantics)
        {
            if (!Enabled)
            {
                return;
            }
            Dictionary<string, object> payload = new Dictionary<string, object>()
            {
                { "cancel_decide", decide },
                { "semantics", semantics ?? string.Empty },
            };
            AppendAccepted(LlmAcceptedInputKind.Cancel, actor, runEffectSeq, payload, "engine_accepted");
        }

        public void RecordAutomatic(int actor, ulong runEffectSeq, string reason, object detail)
        {
            if (!Enabled)
            {
                return;
            }
            Dictionary<string, object> payload = new Dictionary<string, object>()
            {
                { "kind", "automatic" },
                { "reason", reason ?? string.Empty },
                { "detail", detail },
                { "auto_commit", true },
            };
            AppendAccepted(LlmAcceptedInputKind.Automatic, actor, runEffectSeq, payload, "engine_accepted_automatic");
        }

        public void OnAutomaticAccepted(int actor, ulong runEffectSeq, string reason, object detail)
        {
            RecordAutomatic(actor, runEffectSeq, reason, detail);
        }

        public void RecordAutomaticCommit(int actor, ulong runEffectSeq, string reason, object detail)
        {
            RecordAutomatic(actor, runEffectSeq, reason, detail);
        }

        public void NoteRejectedAttempt(string kind, ulong runEffectSeq, string status)
        {
            if (!Enabled)
            {
                return;
            }
            lock (Sync)
            {
                ExclusionDiagnostics.Add(new Dictionary<string, object>()
                {
                    { "kind", kind ?? string.Empty },
                    { "run_effect_seq", runEffectSeq },
                    { "status", status ?? "rejected" },
                    { "accepted", false },
                    { "generation", Generation },
                });
            }
        }

        public void RecordRejectedAttempt(string kind, ulong runEffectSeq, string status)
        {
            NoteRejectedAttempt(kind, runEffectSeq, status);
        }

        public void RecordStaleAttempt(string kind, ulong runEffectSeq, string status)
        {
            NoteRejectedAttempt(kind, runEffectSeq, status ?? "rejected_stale_seq");
        }

        public void RecordExclusion(string kind, ulong runEffectSeq, string status)
        {
            NoteRejectedAttempt(kind, runEffectSeq, status);
        }

        public List<Dictionary<string, object>> GetExclusions()
        {
            lock (Sync)
            {
                return new List<Dictionary<string, object>>(ExclusionDiagnostics);
            }
        }

        public List<Dictionary<string, object>> DrainExclusions()
        {
            lock (Sync)
            {
                List<Dictionary<string, object>> copy = new List<Dictionary<string, object>>(ExclusionDiagnostics);
                ExclusionDiagnostics.Clear();
                return copy;
            }
        }

        void AppendAccepted(
            LlmAcceptedInputKind kind,
            int actor,
            ulong runEffectSeq,
            object payload,
            string provenance)
        {
            lock (Sync)
            {
                LlmAcceptedInputEntry entry = new LlmAcceptedInputEntry()
                {
                    AcceptedSequence = NextAcceptedSequence++,
                    Kind = kind,
                    Actor = actor,
                    RunEffectSeq = runEffectSeq,
                    Payload = payload ?? new Dictionary<string, object>(),
                    AcceptanceProvenance = provenance ?? "engine_accepted",
                    Accepted = true,
                };
                Transcript.Entries.Add(entry);
            }
        }
    }
}

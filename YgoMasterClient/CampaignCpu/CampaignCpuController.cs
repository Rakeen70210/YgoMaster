using System;
using System.Collections.Generic;
using System.IO;
using YgoMaster;

namespace YgoMasterClient
{
    /// <summary>
    /// Solo CampaignCpu orchestration. Hook surfaces: OnDuelBegin/End, RunEffect (non-PvP),
    /// SetPlayerType coerce, SysAct progress tick. Never extends the PvP SysAct switch.
    /// </summary>
    static class CampaignCpuController
    {
        static readonly SoloTemporaryCpuStateMachine StateMachine = new SoloTemporaryCpuStateMachine();
        static bool GateActiveForDuel;
        static bool AlwaysNativeMode;
        static int ChapterId;
        static int OwnedSeat;
        static int MyId;
        static int DuelGeneration;
        static ulong ViewSeq;
        static CampaignCpuRulePack ActivePack;
        static CampaignCpuRuleIndex CachedIndex;
        static string CachedRulesDir;
        static int DecisionCount;
        /// <summary>
        /// When true, PR4a always-native mode: owned windows → NativeLease, no rule commits.
        /// Cleared when LogOnly is false and pack allows scripting (PR4b).
        /// </summary>
        static bool ForceAlwaysNativeLease;

        public static bool IsGateActiveForDuel
        {
            get { return GateActiveForDuel; }
        }

        public static void OnDuelBegin(GameMode gameMode)
        {
            ResetDuelLocal();
            if (!ClientSettings.CampaignCpuEnabled)
            {
                return;
            }
            if (gameMode != GameMode.SoloSingle)
            {
                return;
            }
            if (DuelDll.IsPvpDuel || DuelDll.IsPvpSpectator)
            {
                return;
            }

            MyId = DuelDll.MyID;
            OwnedSeat = CampaignCpuControlPolicy.ResolveOwnedSeat(MyId);
            ChapterId = 0;
            try
            {
                ChapterId = YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Duel.chapter");
            }
            catch
            {
                ChapterId = 0;
            }
            if (ChapterId <= 0)
            {
                CampaignCpuAuditLog.Write("chapter_missing", new Dictionary<string, object>
                {
                    { "my_id", MyId },
                });
                return;
            }

            string rulesDir = ResolveRulesDir();
            try
            {
                CachedIndex = CampaignCpuRulePackLoader.LoadIndex(rulesDir);
                CachedRulesDir = rulesDir;
            }
            catch (Exception ex)
            {
                CampaignCpuAuditLog.Write("pack_index_load_failed", new Dictionary<string, object>
                {
                    { "error", ex.Message },
                    { "rules_dir", rulesDir },
                });
                return;
            }

            CampaignCpuChapterIndexEntry entry;
            if (!CachedIndex.Chapters.TryGetValue(ChapterId, out entry) || entry == null)
            {
                if (ClientSettings.CampaignCpuStrictChapterAllowlist)
                {
                    return;
                }
                return;
            }
            if (!entry.Enabled && ClientSettings.CampaignCpuStrictChapterAllowlist)
            {
                return;
            }

            try
            {
                ActivePack = CampaignCpuRulePackLoader.LoadPack(
                    rulesDir, entry, requireDeckHash: entry.Enabled);
            }
            catch (Exception ex)
            {
                CampaignCpuAuditLog.Write("pack_load_failed", new Dictionary<string, object>
                {
                    { "chapter_id", ChapterId },
                    { "error", ex.Message },
                });
                return;
            }

            // Deck fingerprint: prefer ClientWork deck if present; fail closed when enabled.
            if (entry.Enabled)
            {
                List<int> main, extra, side;
                if (!TryReadOpponentDeckFromClientWork(out main, out extra, out side))
                {
                    // Fall back to SoloDuels file on disk under Data/
                    if (!TryReadOpponentDeckFromSoloDuels(ChapterId, out main, out extra, out side))
                    {
                        CampaignCpuAuditLog.Write("deck_fingerprint_unavailable", new Dictionary<string, object>
                        {
                            { "chapter_id", ChapterId },
                        });
                        ActivePack = null;
                        return;
                    }
                }
                string actual = CampaignCpuDeckFingerprint.Compute(main, extra, side);
                if (!string.Equals(actual, ActivePack.DeckHash, StringComparison.OrdinalIgnoreCase))
                {
                    CampaignCpuAuditLog.Write("deck_fingerprint_mismatch", new Dictionary<string, object>
                    {
                        { "chapter_id", ChapterId },
                        { "expected", ActivePack.DeckHash },
                        { "actual", actual },
                    });
                    ActivePack = null;
                    return;
                }
            }

            GateActiveForDuel = true;
            DuelGeneration++;
            StateMachine.ActivateHumanOwned();
            // PR4a safety: when LogOnly, never script-commit; always NativeLease for owned windows.
            // When not LogOnly and scripting not disabled, PR4b rule commits are allowed.
            ForceAlwaysNativeLease = ClientSettings.CampaignCpuLogOnly;
            AlwaysNativeMode = ClientSettings.CampaignCpuLogOnly;
            DecisionCount = 0;

            CampaignCpuAuditLog.Write("pack_loaded", new Dictionary<string, object>
            {
                { "chapter_id", ChapterId },
                { "pack", ActivePack.Name },
                { "deck_hash", ActivePack.DeckHash },
                { "owned_seat", OwnedSeat },
                { "my_id", MyId },
                { "log_only", ClientSettings.CampaignCpuLogOnly },
            });
            CampaignCpuAuditLog.Write("seat_owned", new Dictionary<string, object>
            {
                { "owned_seat", OwnedSeat },
                { "my_id", MyId },
                { "duel_generation", DuelGeneration },
            });

            // Coerce opponent seat to Human for scripted ownership (unless we immediately lease).
            try
            {
                DuelDll.CampaignCpu_SetPlayerType(OwnedSeat, (int)DuelPlayerType.Human);
            }
            catch
            {
            }
        }

        public static void OnDuelEnd()
        {
            if (GateActiveForDuel)
            {
                CampaignCpuAuditLog.Write("duel_end_summary", new Dictionary<string, object>
                {
                    { "chapter_id", ChapterId },
                    { "decisions", DecisionCount },
                    { "state", StateMachine.State.ToString() },
                    { "scripting_disabled", StateMachine.ScriptingDisabledForDuel },
                    { "scripting_disabled_reason", StateMachine.ScriptingDisabledReason },
                });
            }
            ResetDuelLocal();
        }

        static void ResetDuelLocal()
        {
            GateActiveForDuel = false;
            AlwaysNativeMode = false;
            ForceAlwaysNativeLease = false;
            ChapterId = 0;
            OwnedSeat = 1;
            MyId = 0;
            ViewSeq = 0;
            ActivePack = null;
            DecisionCount = 0;
            StateMachine.Reset();
        }

        /// <summary>
        /// Sole RunEffect router for CampaignCpu. Returns null when CampaignCpu is inactive
        /// so the caller forwards originalRunEffect once.
        /// </summary>
        public delegate int OriginalRunEffectDelegate(int id, int param1, int param2, int param3);

        public static int? OnRunEffect(
            int id,
            int param1,
            int param2,
            int param3,
            OriginalRunEffectDelegate originalRunEffect)
        {
            if (!GateActiveForDuel || originalRunEffect == null)
            {
                return null;
            }
            if (DuelDll.IsPvpDuel || DuelDll.IsPvpSpectator)
            {
                return null;
            }

            ViewSeq++;
            DuelViewType viewType = (DuelViewType)id;
            int turnPlayer = -1;
            int phase = -1;
            try
            {
                turnPlayer = DuelDll.CampaignCpu_GetTurnPlayer();
                phase = DuelDll.CampaignCpu_GetCurrentPhase();
            }
            catch
            {
            }

            int doCommandUser = -1;
            int runDialogUser = -1;
            CampaignCpuEngineWorkSeats.TryReadSeats(out doCommandUser, out runDialogUser);

            int actingPlayer;
            bool actingResolved = CampaignCpuActingPlayerResolver.TryResolve(
                viewType,
                doCommandUser,
                runDialogUser,
                param1,
                MyId,
                turnPlayer,
                out actingPlayer);

            string legalFp = string.Empty;
            var progress = CampaignCpuProgressToken.Create(
                DuelGeneration,
                viewType,
                param1,
                param2,
                param3,
                actingResolved ? actingPlayer : -1,
                0,
                phase,
                legalFp);

            // CommitQuarantine
            if (StateMachine.State == SoloTemporaryCpuState.CommitQuarantine)
            {
                if (!CampaignCpuProgressToken.IsFreshSemanticProgress(
                    progress, StateMachine.QuarantineWatch != null
                        ? StateMachine.QuarantineWatch.CommittedToken
                        : null))
                {
                    return CampaignCpuDefaults.ScriptedHandledReturnCode;
                }
                StateMachine.DisableScripting("commit_quarantine");
                return BeginFallback(
                    id, param1, param2, param3, progress, "fresh_view_after_quarantine", originalRunEffect);
            }

            // AwaitingProgress
            if (StateMachine.State == SoloTemporaryCpuState.AwaitingProgress)
            {
                if (!CampaignCpuProgressToken.IsFreshSemanticProgress(
                    progress,
                    StateMachine.ProgressWatch != null
                        ? StateMachine.ProgressWatch.CommittedToken
                        : null))
                {
                    return CampaignCpuDefaults.ScriptedHandledReturnCode;
                }
                StateMachine.CompleteAwaitingProgress();
            }

            // NativeLease restore handshake
            if (StateMachine.State == SoloTemporaryCpuState.NativeLease)
            {
                // Track CpuThinking-like continuation heuristically via non-decision wait frames
                if (viewType == DuelViewType.WaitFrame || viewType == DuelViewType.CursorSet)
                {
                    StateMachine.MarkCpuThinkingObserved();
                }

                string deny;
                bool requireCpuThinking = false; // PR4a spike may enable
                if (StateMachine.CanRestoreFromNativeLease(
                    DuelGeneration,
                    ViewSeq,
                    progress,
                    actingResolved ? (int?)actingPlayer : null,
                    OwnedSeat,
                    MyId,
                    requireCpuThinking,
                    viewType,
                    param1,
                    out deny))
                {
                    try
                    {
                        DuelDll.CampaignCpu_SetPlayerType(OwnedSeat, (int)DuelPlayerType.Human);
                    }
                    catch
                    {
                    }
                    var lease = StateMachine.ActiveLease;
                    StateMachine.RestoreHumanOwned();
                    CampaignCpuAuditLog.Write("temporary_cpu_restore", new Dictionary<string, object>
                    {
                        { "entry_view_seq", lease != null ? lease.EntryViewSeq : 0UL },
                        { "exit_view_seq", ViewSeq },
                        { "acting", actingResolved ? actingPlayer : -1 },
                        { "deny_was", deny },
                    });
                }
                else
                {
                    return originalRunEffect(id, param1, param2, param3);
                }
            }

            if (StateMachine.State == SoloTemporaryCpuState.NativeLease)
            {
                return originalRunEffect(id, param1, param2, param3);
            }

            if (!actingResolved)
            {
                CampaignCpuAuditLog.Write("acting_player_unknown", new Dictionary<string, object>
                {
                    { "view", viewType.ToString() },
                    { "param1", param1 },
                    { "do_command_user", doCommandUser },
                    { "run_dialog_user", runDialogUser },
                    { "turn_player", turnPlayer },
                });
                return originalRunEffect(id, param1, param2, param3);
            }

            if (actingPlayer == MyId
                || !CampaignCpuControlPolicy.IsOwnedOpponentSeat(actingPlayer, OwnedSeat, MyId))
            {
                return originalRunEffect(id, param1, param2, param3);
            }

            // Owned opponent decision window
            if (StateMachine.ScriptingDisabledForDuel
                || ForceAlwaysNativeLease
                || ActivePack == null
                || !CampaignCpuWindowClassifier.IsScriptedWindow(viewType, param1, ActivePack))
            {
                string reason = ForceAlwaysNativeLease
                    ? "always_native_lease"
                    : (StateMachine.ScriptingDisabledForDuel
                        ? "scripting_disabled"
                        : "v1_non_main_phase");
                return BeginFallback(
                    id, param1, param2, param3, progress, reason, originalRunEffect);
            }

            // Scripted Main Phase path (PR4b)
            try
            {
                var query = new LiveDllLegalActionQuery();
                LegalAction automatic;
                bool multiSelect;
                CampaignCpuObservation obs = CampaignCpuObservationBuilder.Build(
                    query,
                    ViewSeq,
                    viewType,
                    param1,
                    param2,
                    param3,
                    actingPlayer,
                    OwnedSeat,
                    ChapterId,
                    out automatic,
                    out multiSelect);

                progress = CampaignCpuProgressToken.Create(
                    DuelGeneration,
                    viewType,
                    param1,
                    param2,
                    param3,
                    actingPlayer,
                    obs.Turn,
                    obs.Phase,
                    CampaignCpuObservation.FingerprintLegalActions(obs.LegalActions));

                CampaignCpuDecision decision;
                if (automatic != null)
                {
                    decision = CampaignCpuDecision.Commit(
                        CampaignCpuRoute.MechanicalAuto,
                        CampaignCpuObservation.FromLegalAction(automatic),
                        "mechanical_auto",
                        "mechanical",
                        0,
                        true);
                }
                else
                {
                    decision = CampaignCpuScorer.Decide(obs, ActivePack);
                }

                CampaignCpuAuditLog.WriteRaw(
                    CampaignCpuAuditSerializer.SerializeDecision(ViewSeq, decision, obs));

                if (decision.Route == CampaignCpuRoute.RuleCommit
                    || decision.Route == CampaignCpuRoute.MechanicalAuto)
                {
                    if (ClientSettings.CampaignCpuLogOnly)
                    {
                        return BeginFallback(
                            id, param1, param2, param3, progress, "log_only_shadow", originalRunEffect);
                    }

                    CampaignCpuCommitOutcome outcome = CampaignCpuCommit.TryApplyLive(decision.Action);
                    if (outcome == CampaignCpuCommitOutcome.Applied)
                    {
                        DecisionCount++;
                        StateMachine.ArmAwaitingProgress(
                            ViewSeq,
                            progress,
                            decision.Action != null ? decision.Action.CanonicalIdentity : string.Empty,
                            DateTime.UtcNow);
                        CampaignCpuAuditLog.Write("commit_applied", new Dictionary<string, object>
                        {
                            { "view_seq", ViewSeq },
                            { "rule_id", decision.RuleId },
                            { "action", decision.Action != null ? decision.Action.CanonicalIdentity : null },
                        });
                        return CampaignCpuDefaults.ScriptedHandledReturnCode;
                    }
                    if (outcome == CampaignCpuCommitOutcome.NotStarted)
                    {
                        return BeginFallback(
                            id, param1, param2, param3, progress, "commit_not_started", originalRunEffect);
                    }
                    StateMachine.EnterCommitQuarantine(
                        ViewSeq,
                        progress,
                        decision.Action != null ? decision.Action.CanonicalIdentity : string.Empty,
                        DateTime.UtcNow,
                        "commit_indeterminate");
                    CampaignCpuAuditLog.Write("commit_indeterminate", new Dictionary<string, object>
                    {
                        { "view_seq", ViewSeq },
                        { "action", decision.Action != null ? decision.Action.CanonicalIdentity : null },
                    });
                    return CampaignCpuDefaults.ScriptedHandledReturnCode;
                }

                return BeginFallback(
                    id, param1, param2, param3, progress, decision.Reason ?? "no_match", originalRunEffect);
            }
            catch (Exception ex)
            {
                CampaignCpuAuditLog.Write("precommit_error", new Dictionary<string, object>
                {
                    { "error", ex.Message },
                    { "view_seq", ViewSeq },
                });
                return BeginFallback(
                    id, param1, param2, param3, progress, "precommit_error", originalRunEffect);
            }
        }

        static int BeginFallback(
            int id,
            int p1,
            int p2,
            int p3,
            CampaignCpuProgressToken progress,
            string reason,
            OriginalRunEffectDelegate originalRunEffect)
        {
            StateMachine.BeginNativeLease(
                reason,
                DuelGeneration,
                ViewSeq,
                progress,
                OwnedSeat,
                DateTime.UtcNow);
            try
            {
                DuelDll.CampaignCpu_SetPlayerType(OwnedSeat, (int)DuelPlayerType.CPU);
                // Ensure cpu param remains applied (typical solo 100).
                try
                {
                    DuelDll.CampaignCpu_SetCpuParam(OwnedSeat, 100u);
                }
                catch
                {
                }
            }
            catch
            {
            }
            CampaignCpuAuditLog.Write("temporary_cpu_begin", new Dictionary<string, object>
            {
                { "reason", reason },
                { "view_seq", ViewSeq },
                { "owned_seat", OwnedSeat },
            });
            return originalRunEffect(id, p1, p2, p3);
        }

        public static void OnSoloSysActTick()
        {
            if (!GateActiveForDuel)
            {
                return;
            }
            if (StateMachine.State != SoloTemporaryCpuState.AwaitingProgress)
            {
                return;
            }
            int timeout = ClientSettings.CampaignCpuProgressTimeoutMs > 0
                ? ClientSettings.CampaignCpuProgressTimeoutMs
                : CampaignCpuDefaults.DefaultProgressTimeoutMs;
            if (!StateMachine.IsProgressTimedOut(DateTime.UtcNow, timeout))
            {
                return;
            }
            var watch = StateMachine.ProgressWatch;
            StateMachine.EnterCommitQuarantine(
                watch != null ? watch.CommittedViewSeq : ViewSeq,
                watch != null ? watch.CommittedToken : null,
                watch != null ? watch.ActionIdentity : string.Empty,
                DateTime.UtcNow,
                "post_commit_stall");
            CampaignCpuAuditLog.Write("post_commit_stall", new Dictionary<string, object>
            {
                { "view_seq", ViewSeq },
                { "timeout_ms", timeout },
            });
        }

        /// <summary>
        /// Dual-driver lock for SetPlayerType: force Human for OwnedSeat unless NativeLease.
        /// Never rewrite MyID.
        /// </summary>
        public static int CoercePlayerType(int player, int type)
        {
            if (!GateActiveForDuel)
            {
                return type;
            }
            if (player == MyId)
            {
                return type;
            }
            if (player != OwnedSeat)
            {
                return type;
            }
            if (StateMachine.State == SoloTemporaryCpuState.NativeLease)
            {
                return (int)DuelPlayerType.CPU;
            }
            return (int)DuelPlayerType.Human;
        }

        static string ResolveRulesDir()
        {
            string rel = ClientSettings.CampaignCpuRulesDir;
            if (string.IsNullOrEmpty(rel))
            {
                rel = "CampaignCpuRules";
            }
            // Prefer Data/CampaignCpuRules next to game data
            string dataRoot = Program.DataDir;
            if (!string.IsNullOrEmpty(dataRoot))
            {
                string candidate = Path.Combine(dataRoot, rel);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            return Path.Combine(Program.ClientDataDir, rel);
        }

        static bool TryReadOpponentDeckFromClientWork(
            out List<int> main,
            out List<int> extra,
            out List<int> side)
        {
            main = null;
            extra = null;
            side = null;
            try
            {
                // Optional path; often absent on client — fail to SoloDuels file.
                return false;
            }
            catch
            {
                return false;
            }
        }

        static bool TryReadOpponentDeckFromSoloDuels(
            int chapterId,
            out List<int> main,
            out List<int> extra,
            out List<int> side)
        {
            main = new List<int>();
            extra = new List<int>();
            side = new List<int>();
            try
            {
                string path = Path.Combine(
                    Program.DataDir, "SoloDuels", chapterId + ".json");
                if (!File.Exists(path))
                {
                    return false;
                }
                Dictionary<string, object> root =
                    MiniJSON.Json.DeserializeStripped(File.ReadAllText(path))
                    as Dictionary<string, object>;
                if (root == null)
                {
                    return false;
                }
                Dictionary<string, object> duel = Utils.GetDictionary(root, "Duel");
                if (duel == null)
                {
                    return false;
                }
                List<object> decks = Utils.GetValue(duel, "Deck", (List<object>)null);
                if (decks == null || decks.Count < 2)
                {
                    return false;
                }
                Dictionary<string, object> opp = decks[1] as Dictionary<string, object>;
                if (opp == null)
                {
                    return false;
                }
                main = ReadCardIds(Utils.GetDictionary(opp, "Main"));
                extra = ReadCardIds(Utils.GetDictionary(opp, "Extra"));
                side = ReadCardIds(Utils.GetDictionary(opp, "Side"));
                return true;
            }
            catch
            {
                return false;
            }
        }

        static List<int> ReadCardIds(Dictionary<string, object> zone)
        {
            var result = new List<int>();
            if (zone == null)
            {
                return result;
            }
            List<object> ids = Utils.GetValue(zone, "CardIds", (List<object>)null);
            if (ids == null)
            {
                return result;
            }
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == null)
                {
                    continue;
                }
                int id;
                if (int.TryParse(ids[i].ToString(), out id))
                {
                    result.Add(id);
                }
            }
            return result;
        }
    }
}

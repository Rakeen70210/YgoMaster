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
        /// <summary>
        /// Seat coerce + seat_owned audit must wait until duel.dll work memory exists.
        /// Calling SetPlayerType/IsHuman from OnDuelBegin AVs (null+0xc in duel.dll) —
        /// live 2026-07-21 after pack_loaded, before seat_owned.
        /// </summary>
        static bool PendingSeatOwnershipAssert;

        public static bool IsGateActiveForDuel
        {
            get { return GateActiveForDuel; }
        }

        public static void OnDuelBegin(GameMode gameMode)
        {
            ResetDuelLocal();
            // Drop any stale pointer from a previous duel before engine re-inits.
            CampaignCpuEngineWorkSeats.EngineWorkBase = IntPtr.Zero;
            if (!ClientSettings.CampaignCpuEnabled)
            {
                return;
            }
            if (!CampaignCpuControlPolicy.IsEligibleSoloCampaignMode(
                (int)gameMode,
                DuelDll.IsPvpDuel,
                DuelDll.IsPvpSpectator))
            {
                // Probe-only: make silent non-solo skips visible (e.g. Room / Replay).
                if (ClientSettings.CampaignCpuProbeLogging)
                {
                    CampaignCpuAuditLog.Write("gate_skipped", new Dictionary<string, object>
                    {
                        { "reason", "ineligible_game_mode" },
                        { "game_mode", (int)gameMode },
                        { "game_mode_name", gameMode.ToString() },
                        { "is_pvp", DuelDll.IsPvpDuel },
                        { "is_spectator", DuelDll.IsPvpSpectator },
                    });
                }
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
                    { "game_mode", (int)gameMode },
                    { "game_mode_name", gameMode.ToString() },
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
                    { "chapter_id", ChapterId },
                });
                return;
            }

            CampaignCpuChapterIndexEntry entry;
            if (!CachedIndex.Chapters.TryGetValue(ChapterId, out entry) || entry == null)
            {
                CampaignCpuAuditLog.Write("chapter_not_allowlisted", new Dictionary<string, object>
                {
                    { "chapter_id", ChapterId },
                    { "strict", ClientSettings.CampaignCpuStrictChapterAllowlist },
                    { "reason", "missing_from_index" },
                });
                return;
            }
            if (!entry.Enabled && ClientSettings.CampaignCpuStrictChapterAllowlist)
            {
                CampaignCpuAuditLog.Write("chapter_not_allowlisted", new Dictionary<string, object>
                {
                    { "chapter_id", ChapterId },
                    { "strict", true },
                    { "reason", "index_entry_disabled" },
                });
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
            // PR4a safety: always NativeLease until AllowScriptedCommits is on.
            // LogOnly must NOT force always-native — it is PR3 shadow capture
            // (extract → score → audit → native, no commit) once AllowScriptedCommits is true.
            ForceAlwaysNativeLease = !ClientSettings.CampaignCpuAllowScriptedCommits;
            AlwaysNativeMode = ForceAlwaysNativeLease;
            DecisionCount = 0;

            string mode;
            if (ForceAlwaysNativeLease)
            {
                mode = "pr4a_always_native";
            }
            else if (ClientSettings.CampaignCpuLogOnly)
            {
                mode = "pr3_capture_shadow";
            }
            else
            {
                mode = "pr4b_scripted";
            }

            CampaignCpuAuditLog.Write("pack_loaded", new Dictionary<string, object>
            {
                { "chapter_id", ChapterId },
                { "pack", ActivePack.Name },
                { "deck_hash", ActivePack.DeckHash },
                { "owned_seat", OwnedSeat },
                { "my_id", MyId },
                { "log_only", ClientSettings.CampaignCpuLogOnly },
                { "allow_scripted_commits", ClientSettings.CampaignCpuAllowScriptedCommits },
                { "always_native_lease", ForceAlwaysNativeLease },
                { "mode", mode },
                { "seat_assert", "deferred_until_engine_work" },
                { "capture_full_legals", true },
            });

            // Defer SetPlayerType / IsHuman / seat_owned until DLL_SetWorkMemory has run.
            // OnDuelBegin is too early: duel.dll player APIs AV on null engine state.
            PendingSeatOwnershipAssert = true;
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
            PendingSeatOwnershipAssert = false;
            ChapterId = 0;
            OwnedSeat = 1;
            MyId = 0;
            ViewSeq = 0;
            ActivePack = null;
            DecisionCount = 0;
            StateMachine.Reset();
        }

        /// <summary>
        /// True once local duel.dll has published work memory for this duel.
        /// Native player-type APIs are unsafe before this.
        /// </summary>
        static bool IsEngineWorkReady()
        {
            return CampaignCpuEngineWorkSeats.EngineWorkBase != IntPtr.Zero;
        }

        /// <summary>
        /// Coerce OwnedSeat to Human and emit seat_owned once the engine is ready.
        /// Safe to call from RunEffect / SysAct; no-ops until work memory exists.
        /// </summary>
        static void TryCompleteSeatOwnershipAssert(string reason)
        {
            if (!GateActiveForDuel || !PendingSeatOwnershipAssert)
            {
                return;
            }
            if (!IsEngineWorkReady())
            {
                return;
            }

            try
            {
                DuelDll.CampaignCpu_SetPlayerType(OwnedSeat, (int)DuelPlayerType.Human);
            }
            catch
            {
            }

            int ownedIsHuman = TryReadIsHuman(OwnedSeat);
            int myIsHuman = TryReadIsHuman(MyId);
            CampaignCpuAuditLog.Write("seat_owned", new Dictionary<string, object>
            {
                { "owned_seat", OwnedSeat },
                { "my_id", MyId },
                { "duel_generation", DuelGeneration },
                { "owned_is_human_readback", ownedIsHuman },
                { "my_is_human_readback", myIsHuman },
                { "player_type_readback", "DLL_DuelIsHuman" },
                { "player_type_readback_note",
                    "No DLL_DuelGetPlayerType export; IsHuman is the available native confirmation." },
                { "assert_reason", reason },
                { "engine_work_base_nonzero", true },
            });
            PendingSeatOwnershipAssert = false;
        }

        /// <summary>
        /// Apply SetPlayerType only when engine work memory is present (native-safe).
        /// </summary>
        static void TrySetPlayerTypeSafe(int player, int type)
        {
            if (!IsEngineWorkReady())
            {
                return;
            }
            try
            {
                DuelDll.CampaignCpu_SetPlayerType(player, type);
            }
            catch
            {
            }
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

            TryCompleteSeatOwnershipAssert("run_effect");

            ViewSeq++;
            DuelViewType viewType = (DuelViewType)id;

            // Belt-and-suspenders: re-assert Human ownership at DuelStart (PR2b).
            if (viewType == DuelViewType.DuelStart
                && StateMachine.State != SoloTemporaryCpuState.NativeLease
                && IsEngineWorkReady())
            {
                TrySetPlayerTypeSafe(OwnedSeat, (int)DuelPlayerType.Human);
                CampaignCpuAuditLog.Write("seat_reassert_human", new Dictionary<string, object>
                {
                    { "owned_seat", OwnedSeat },
                    { "view", "DuelStart" },
                    { "owned_is_human_readback", TryReadIsHuman(OwnedSeat) },
                    { "my_is_human_readback", TryReadIsHuman(MyId) },
                });
            }

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
            bool seatsReadable = CampaignCpuEngineWorkSeats.TryReadSeats(
                out doCommandUser, out runDialogUser);

            int actingPlayer;
            bool actingResolved = CampaignCpuActingPlayerResolver.TryResolve(
                viewType,
                doCommandUser,
                runDialogUser,
                param1,
                MyId,
                turnPlayer,
                out actingPlayer);

            // PR2b probe: decision-family windows with seat sources + IsHuman readback.
            if (ClientSettings.CampaignCpuProbeLogging
                && IsProbeInterestingView(viewType, param1))
            {
                WriteActingPlayerProbe(
                    viewType,
                    param1,
                    doCommandUser,
                    runDialogUser,
                    turnPlayer,
                    seatsReadable,
                    actingResolved,
                    actingPlayer);
            }

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
                    TrySetPlayerTypeSafe(OwnedSeat, (int)DuelPlayerType.Human);
                    var lease = StateMachine.ActiveLease;
                    StateMachine.RestoreHumanOwned();
                    CampaignCpuAuditLog.Write("temporary_cpu_restore", new Dictionary<string, object>
                    {
                        { "entry_view_seq", lease != null ? lease.EntryViewSeq : 0UL },
                        { "entry_reason", lease != null ? lease.Reason : null },
                        { "exit_view_seq", ViewSeq },
                        { "acting", actingResolved ? actingPlayer : -1 },
                        { "window_class", CampaignCpuWindowClassifier.ClassifyWindow(viewType, param1) },
                        { "owned_is_human_readback", TryReadIsHuman(OwnedSeat) },
                        { "my_is_human_readback", TryReadIsHuman(MyId) },
                        { "observed_cpu_thinking", lease != null && lease.ObservedCpuThinking },
                        { "deny_was", deny },
                    });
                }
                else
                {
                    if (ClientSettings.CampaignCpuProbeLogging
                        && IsProbeInterestingView(viewType, param1))
                    {
                        CampaignCpuAuditLog.Write("temporary_cpu_hold", new Dictionary<string, object>
                        {
                            { "view_seq", ViewSeq },
                            { "deny", deny },
                            { "window_class",
                                CampaignCpuWindowClassifier.ClassifyWindow(viewType, param1) },
                            { "acting", actingResolved ? actingPlayer : -1 },
                        });
                    }
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

            if (actingPlayer == MyId)
            {
                // Human seat: never CampaignCpu commit (PR2b pass criterion).
                if (ClientSettings.CampaignCpuProbeLogging
                    && IsProbeInterestingView(viewType, param1))
                {
                    CampaignCpuAuditLog.Write("pass_through_myid", new Dictionary<string, object>
                    {
                        { "view_seq", ViewSeq },
                        { "my_id", MyId },
                        { "window_class",
                            CampaignCpuWindowClassifier.ClassifyWindow(viewType, param1) },
                    });
                }
                return originalRunEffect(id, param1, param2, param3);
            }

            if (!CampaignCpuControlPolicy.IsOwnedOpponentSeat(actingPlayer, OwnedSeat, MyId))
            {
                return originalRunEffect(id, param1, param2, param3);
            }

            // Owned opponent decision window
            if (StateMachine.ScriptingDisabledForDuel
                || ForceAlwaysNativeLease
                || ActivePack == null
                || !CampaignCpuWindowClassifier.IsScriptedWindow(viewType, param1, ActivePack))
            {
                string reason;
                if (ForceAlwaysNativeLease)
                {
                    reason = "always_native_lease";
                }
                else if (StateMachine.ScriptingDisabledForDuel)
                {
                    reason = "scripting_disabled";
                }
                else if (CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(viewType, param1))
                {
                    // Dual-Human residual: re-lease CPU for trap/timing so MD does not
                    // paint opponent activate UI on the local human screen after restore.
                    reason = "owned_response_hold";
                }
                else
                {
                    reason = "v1_non_main_phase";
                }
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

                // Always audit the scored decision + full legal menu (PR3 capture / PR4b).
                CampaignCpuAuditLog.WriteRaw(
                    CampaignCpuAuditSerializer.SerializeDecision(
                        ViewSeq,
                        decision,
                        obs,
                        shadowOnly: ClientSettings.CampaignCpuLogOnly));

                if (decision.Route == CampaignCpuRoute.RuleCommit
                    || decision.Route == CampaignCpuRoute.MechanicalAuto)
                {
                    if (ClientSettings.CampaignCpuLogOnly)
                    {
                        // Shadow: record would-be commit, then native plays this window.
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

                // FallbackNative (or other non-commit routes): already audited above.
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
            TrySetPlayerTypeSafe(OwnedSeat, (int)DuelPlayerType.CPU);
            // Ensure cpu param remains applied (typical solo 100).
            if (IsEngineWorkReady())
            {
                try
                {
                    DuelDll.CampaignCpu_SetCpuParam(OwnedSeat, 100u);
                }
                catch
                {
                }
            }
            // Exact-once: this is the sole originalRunEffect call on the BeginFallback path.
            int ret = originalRunEffect(id, p1, p2, p3);
            string windowClass = CampaignCpuWindowClassifier.ClassifyWindow((DuelViewType)id, p1);
            CampaignCpuAuditLog.Write("temporary_cpu_begin", new Dictionary<string, object>
            {
                { "reason", reason },
                { "view_seq", ViewSeq },
                { "owned_seat", OwnedSeat },
                { "window_class", windowClass },
                { "owned_is_human_readback", TryReadIsHuman(OwnedSeat) },
                { "my_is_human_readback", TryReadIsHuman(MyId) },
                { "exact_once_forward", true },
            });

            // Under AllowScriptedCommits, one-shot Human restore only after pure DrawPhase.
            // Long Draw leases eat rival Main extract; response/battle leases must hold CPU
            // until handshake restore (owned Main or MyID boundary) so dual-Human does not
            // paint opponent trap/timing UI on the local human screen.
            bool oneShotRestore = CampaignCpuWindowClassifier.ShouldOneShotRestoreAfterNativeForward(
                ClientSettings.CampaignCpuAllowScriptedCommits,
                (DuelViewType)id,
                p1);
            if (oneShotRestore
                && StateMachine.State == SoloTemporaryCpuState.NativeLease)
            {
                TrySetPlayerTypeSafe(OwnedSeat, (int)DuelPlayerType.Human);
                StateMachine.RestoreHumanOwned();
                CampaignCpuAuditLog.Write("temporary_cpu_oneshot_restore", new Dictionary<string, object>
                {
                    { "reason", reason },
                    { "view_seq", ViewSeq },
                    { "window_class", windowClass },
                    { "oneshot_policy", "draw_phase_only" },
                    { "owned_is_human_readback", TryReadIsHuman(OwnedSeat) },
                    { "my_is_human_readback", TryReadIsHuman(MyId) },
                    { "note", "AllowScriptedCommits: restore Human after DrawPhase so Main menus can be extracted" },
                });
            }
            return ret;
        }

        public static void OnSoloSysActTick()
        {
            if (!GateActiveForDuel)
            {
                return;
            }
            TryCompleteSeatOwnershipAssert("sysact");
            // PR4a always-native: AwaitingProgress only arms after scripted commits (PR4b).
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
                // Never rewrite human seat type.
                return type;
            }
            if (player != OwnedSeat)
            {
                return type;
            }
            int coerced = StateMachine.State == SoloTemporaryCpuState.NativeLease
                ? (int)DuelPlayerType.CPU
                : (int)DuelPlayerType.Human;
            if (coerced != type && ClientSettings.CampaignCpuProbeLogging)
            {
                CampaignCpuAuditLog.Write("set_player_type_coerce", new Dictionary<string, object>
                {
                    { "player", player },
                    { "requested_type", type },
                    { "coerced_type", coerced },
                    { "state", StateMachine.State.ToString() },
                    { "owned_seat", OwnedSeat },
                    { "my_id", MyId },
                });
            }
            return coerced;
        }

        static bool IsProbeInterestingView(DuelViewType viewType, int param1)
        {
            if (viewType == DuelViewType.RunDialog || viewType == DuelViewType.RunList)
            {
                return true;
            }
            if (viewType == DuelViewType.WaitInput)
            {
                return true;
            }
            return false;
        }

        static void WriteActingPlayerProbe(
            DuelViewType viewType,
            int param1,
            int doCommandUser,
            int runDialogUser,
            int turnPlayer,
            bool seatsReadable,
            bool actingResolved,
            int actingPlayer)
        {
            string family = CampaignCpuWindowClassifier.ClassifyWindow(viewType, param1);
            string expectedSource = "unknown";
            if (viewType == DuelViewType.WaitInput)
            {
                if (param1 >= (int)DuelMenuActType.CheckTiming
                    && param1 <= (int)DuelMenuActType.LockOn)
                {
                    expectedSource = "do_command_user";
                }
                else
                {
                    expectedSource = "turn_player";
                }
            }
            else if (viewType == DuelViewType.RunDialog)
            {
                expectedSource = param1 == 1 ? "none_info" : "run_dialog_user";
            }
            else if (viewType == DuelViewType.RunList)
            {
                expectedSource = "param1_or_turn";
            }

            bool isMyId = actingResolved && actingPlayer == MyId;
            bool isOwned = actingResolved
                && CampaignCpuControlPolicy.IsOwnedOpponentSeat(actingPlayer, OwnedSeat, MyId);

            CampaignCpuAuditLog.Write("acting_player_probe", new Dictionary<string, object>
            {
                { "view_seq", ViewSeq },
                { "window_class", family },
                { "view", viewType.ToString() },
                { "param1", param1 },
                { "do_command_user", doCommandUser },
                { "run_dialog_user", runDialogUser },
                { "turn_player", turnPlayer },
                { "seats_readable", seatsReadable },
                { "engine_work_base_nonzero",
                    CampaignCpuEngineWorkSeats.EngineWorkBase != IntPtr.Zero },
                { "do_command_offset",
                    CampaignCpuEngineWorkSeats.EffectiveDoCommandUserOffset },
                { "run_dialog_offset",
                    CampaignCpuEngineWorkSeats.EffectiveRunDialogUserOffset },
                { "acting_resolved", actingResolved },
                { "acting_player", actingResolved ? actingPlayer : -1 },
                { "expected_source", expectedSource },
                { "is_my_id", isMyId },
                { "is_owned_seat", isOwned },
                { "owned_seat", OwnedSeat },
                { "my_id", MyId },
                { "owned_is_human_readback", TryReadIsHuman(OwnedSeat) },
                { "my_is_human_readback", TryReadIsHuman(MyId) },
                { "sm_state", StateMachine.State.ToString() },
                { "always_native", ForceAlwaysNativeLease },
            });
        }

        static int TryReadIsHuman(int player)
        {
            if (!IsEngineWorkReady())
            {
                return -1;
            }
            try
            {
                return DuelDll.CampaignCpu_IsHuman(player);
            }
            catch
            {
                return -1;
            }
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

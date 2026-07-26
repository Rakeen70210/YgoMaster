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
        /// <summary>True only after OwnedSeat and MyID both read back Human (positive IsHuman=1).</summary>
        static bool SeatOwnershipConfirmed;
        static int SeatAssertFailedAttempts;
        /// <summary>Last owned-seat field snapshot for probe play-delta logging.</summary>
        static List<CampaignCpuZoneCard> LastOwnedFieldSnapshot;
        static string LastOwnedFieldFingerprint;

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
            if (!CampaignCpuControlPolicy.TryResolveOwnedSeat(MyId, out OwnedSeat))
            {
                CampaignCpuAuditLog.Write("gate_skipped", new Dictionary<string, object>
                {
                    { "reason", "invalid_my_id" },
                    { "my_id", MyId },
                    { "game_mode", (int)gameMode },
                    { "game_mode_name", gameMode.ToString() },
                });
                return;
            }
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

            if (ActivePack == null || ActivePack.ChapterId != ChapterId)
            {
                CampaignCpuAuditLog.Write("pack_chapter_mismatch", new Dictionary<string, object>
                {
                    { "chapter_id", ChapterId },
                    { "pack_chapter_id", ActivePack != null ? ActivePack.ChapterId : 0 },
                });
                ActivePack = null;
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
                { "duel_generation", DuelGeneration },
                { "log_only", ClientSettings.CampaignCpuLogOnly },
                { "allow_scripted_commits", ClientSettings.CampaignCpuAllowScriptedCommits },
                { "always_native_lease", ForceAlwaysNativeLease },
                { "mode", mode },
                { "seat_assert", "deferred_until_engine_work" },
                { "max_decisions_per_duel", ActivePack.Policy != null
                    ? ActivePack.Policy.MaxDecisionsPerDuel : 0 },
                { "capture_full_legals", true },
            });

            // Defer SetPlayerType / IsHuman / seat_owned until DLL_SetWorkMemory has run.
            // OnDuelBegin is too early: duel.dll player APIs AV on null engine state.
            PendingSeatOwnershipAssert = true;
            SeatOwnershipConfirmed = false;
            SeatAssertFailedAttempts = 0;
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
                    { "my_id", MyId },
                    { "owned_seat", OwnedSeat },
                    { "duel_generation", DuelGeneration },
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
            SeatOwnershipConfirmed = false;
            SeatAssertFailedAttempts = 0;
            ChapterId = 0;
            OwnedSeat = 1;
            MyId = 0;
            ViewSeq = 0;
            ActivePack = null;
            DecisionCount = 0;
            LastOwnedFieldSnapshot = null;
            LastOwnedFieldFingerprint = null;
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
        /// Coerce OwnedSeat to Human and confirm seat_owned once the engine is ready.
        /// Does not clear pending until both OwnedSeat and MyID positively read back Human.
        /// After MaxSeatAssertAttempts failures, disables scripting for the rest of the duel.
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

            bool setThrew = false;
            try
            {
                DuelDll.CampaignCpu_SetPlayerType(OwnedSeat, (int)DuelPlayerType.Human);
            }
            catch (Exception ex)
            {
                setThrew = true;
                CampaignCpuAuditLog.Write("seat_assert_set_failed", new Dictionary<string, object>
                {
                    { "owned_seat", OwnedSeat },
                    { "my_id", MyId },
                    { "duel_generation", DuelGeneration },
                    { "error", ex.Message },
                    { "assert_reason", reason },
                });
            }

            int ownedIsHuman = TryReadIsHuman(OwnedSeat);
            int myIsHuman = TryReadIsHuman(MyId);
            bool confirmed = !setThrew
                && CampaignCpuControlPolicy.IsSeatOwnershipConfirmed(ownedIsHuman, myIsHuman);

            CampaignCpuAuditLog.Write("seat_owned", new Dictionary<string, object>
            {
                { "owned_seat", OwnedSeat },
                { "my_id", MyId },
                { "duel_generation", DuelGeneration },
                { "owned_is_human_readback", ownedIsHuman },
                { "my_is_human_readback", myIsHuman },
                { "confirmed", confirmed },
                { "set_threw", setThrew },
                { "player_type_readback", "DLL_DuelIsHuman" },
                { "player_type_readback_note",
                    "No DLL_DuelGetPlayerType export; IsHuman is the available native confirmation." },
                { "assert_reason", reason },
                { "engine_work_base_nonzero", true },
                { "failed_attempts", SeatAssertFailedAttempts },
            });

            if (confirmed)
            {
                SeatOwnershipConfirmed = true;
                PendingSeatOwnershipAssert = false;
                SeatAssertFailedAttempts = 0;
                return;
            }

            // Keep pending so we retry on the next RunEffect/SysAct. Never permit
            // scripted handling until confirmed (see SeatOwnershipConfirmed gate).
            SeatAssertFailedAttempts++;
            if (CampaignCpuControlPolicy.ShouldDisableScriptingAfterSeatAssertFailures(
                SeatAssertFailedAttempts))
            {
                PendingSeatOwnershipAssert = false;
                SeatOwnershipConfirmed = false;
                StateMachine.DisableScripting("seat_assert_failed");
                CampaignCpuAuditLog.Write("seat_assert_failed", new Dictionary<string, object>
                {
                    { "owned_seat", OwnedSeat },
                    { "my_id", MyId },
                    { "duel_generation", DuelGeneration },
                    { "owned_is_human_readback", ownedIsHuman },
                    { "my_is_human_readback", myIsHuman },
                    { "failed_attempts", SeatAssertFailedAttempts },
                    { "scripting_disabled", true },
                });
            }
        }

        /// <summary>
        /// Apply SetPlayerType only when engine work memory is present (native-safe).
        /// Prefer <see cref="TryTransitionPlayerType"/> when the state machine depends on success.
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
        /// Set player type and confirm via IsHuman readback. Does not advance the state machine.
        /// Human desired → Confirmed only when IsHuman==1; CPU desired → Confirmed only when IsHuman==0.
        /// </summary>
        static CampaignCpuPlayerTypeTransitionResult TryTransitionPlayerType(
            int player,
            int desiredType,
            out int isHumanReadback)
        {
            isHumanReadback = -1;
            if (!IsEngineWorkReady())
            {
                return CampaignCpuPlayerTypeTransitionResult.Unknown;
            }
            try
            {
                DuelDll.CampaignCpu_SetPlayerType(player, desiredType);
            }
            catch
            {
                return CampaignCpuPlayerTypeTransitionResult.Unknown;
            }
            isHumanReadback = TryReadIsHuman(player);
            return CampaignCpuControlPolicy.EvaluatePlayerTypeTransition(
                desiredType, isHumanReadback);
        }

        /// <summary>
        /// Build the production progress-check token. Always populates base fields from the same
        /// sources as arming. When the callback is a Main WaitInput and extract is safe, re-extracts
        /// a known legal fingerprint so same-view menu changes can prove progress.
        /// </summary>
        static CampaignCpuProgressToken BuildProgressCheckToken(
            DuelViewType viewType,
            int param1,
            int param2,
            int param3,
            int actingSeat,
            bool actingResolved,
            int turn,
            int phase)
        {
            int seat = actingResolved ? actingSeat : -1;
            bool known = false;
            string fingerprint = null;
            if (actingResolved
                && CampaignCpuProgressCheck.IsSafeLegalFingerprintExtractView(viewType, param1)
                && IsEngineWorkReady())
            {
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
                        actingSeat,
                        OwnedSeat,
                        ChapterId,
                        out automatic,
                        out multiSelect);
                    fingerprint = CampaignCpuObservation.FingerprintLegalActions(obs.LegalActions);
                    known = true;
                }
                catch
                {
                    known = false;
                    fingerprint = null;
                }
            }
            return CampaignCpuProgressCheck.BuildCheckToken(
                DuelGeneration,
                viewType,
                param1,
                param2,
                param3,
                seat,
                turn,
                phase,
                known,
                fingerprint);
        }

        static void AuditPlayerTypeTransitionFailure(
            string path,
            int desiredType,
            CampaignCpuPlayerTypeTransitionResult result,
            int isHumanReadback,
            string windowClass)
        {
            CampaignCpuAuditLog.Write("player_type_transition_failed", new Dictionary<string, object>
            {
                { "path", path },
                { "desired_type", desiredType },
                { "desired_type_name", desiredType == (int)DuelPlayerType.Human ? "Human" : "CPU" },
                { "result", result.ToString() },
                { "owned_seat", OwnedSeat },
                { "owned_is_human_readback", isHumanReadback },
                { "my_is_human_readback", TryReadIsHuman(MyId) },
                { "view_seq", ViewSeq },
                { "window_class", windowClass },
                { "state", StateMachine.State.ToString() },
            });
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
            int turn = -1;
            try
            {
                turnPlayer = DuelDll.CampaignCpu_GetTurnPlayer();
                phase = DuelDll.CampaignCpu_GetCurrentPhase();
                // Same turn source for check-path and arm-path tokens (PR4b M1 parity).
                turn = DuelDll.CampaignCpu_GetTurnNum();
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

            // M3: owned-Main boundary candidates while NativeLease is active (no policy change).
            // Capture acting seat BEFORE any TemporaryCpu restore flip below.
            if (StateMachine.State == SoloTemporaryCpuState.NativeLease
                && viewType == DuelViewType.CpuThinking)
            {
                // Record real CpuThinking before the boundary probe line for this view.
                StateMachine.MarkCpuThinkingObserved();
            }
            if (ClientSettings.CampaignCpuProbeLogging
                && StateMachine.State == SoloTemporaryCpuState.NativeLease
                && CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(viewType, param1))
            {
                WriteOwnedMainBoundaryProbe(
                    viewType,
                    param1,
                    param2,
                    param3,
                    doCommandUser,
                    runDialogUser,
                    turnPlayer,
                    turn,
                    phase,
                    actingResolved,
                    actingPlayer);
            }

            // Probe: log owned-seat field cards (incl. face-down ST + is_trap) when plays move.
            if (ClientSettings.CampaignCpuProbeLogging
                && CampaignCpuFieldDiff.IsFieldProbeView(viewType, param1))
            {
                TryWriteOwnedFieldProbe(
                    viewType,
                    param1,
                    turn,
                    turnPlayer,
                    phase);
            }

            // Check path: same base fields as arming; re-extract legal fingerprint when safe
            // (Main WaitInput) so same-view menu changes can prove progress (M1).
            var progress = BuildProgressCheckToken(
                viewType,
                param1,
                param2,
                param3,
                actingPlayer,
                actingResolved,
                turn,
                phase);

            // CommitQuarantine
            if (StateMachine.State == SoloTemporaryCpuState.CommitQuarantine)
            {
                CampaignCpuProgressCheckResult qCheck = CampaignCpuProgressCheck.EvaluateCommitQuarantine(
                    progress,
                    StateMachine.QuarantineWatch != null
                        ? StateMachine.QuarantineWatch.CommittedToken
                        : null);
                CampaignCpuEffectKind qEffect = CampaignCpuRunEffectRouter.MapProgressGate(
                    SoloTemporaryCpuState.CommitQuarantine, qCheck);
                if (qEffect == CampaignCpuEffectKind.SuppressSameView)
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
                // Live M7 (2026-07-24): after Activate → Location Decide that does not
                // advance, the engine re-issues the same WaitInput_Location. Suppressing
                // that as same-view freezes Main until post_commit_stall. Allow one
                // re-entry so DefaultLocation can replace a no-op Decide; if the armed
                // commit was already default_location, keep suppress/timeout (no spin).
                bool ownedTurnLocation = CampaignCpuWindowClassifier.IsOwnedTurnLocation(
                    viewType, param1, turnPlayer, OwnedSeat, MyId);
                string armedIdentity = StateMachine.ProgressWatch != null
                    ? StateMachine.ProgressWatch.ActionIdentity
                    : null;
                bool armedWasDefaultLocation = !string.IsNullOrEmpty(armedIdentity)
                    && armedIdentity.IndexOf(
                        CampaignCpuLocation.DefaultLocationScope,
                        StringComparison.Ordinal) >= 0;
                if (ownedTurnLocation && !armedWasDefaultLocation)
                {
                    StateMachine.CompleteAwaitingProgress();
                    CampaignCpuAuditLog.Write("awaiting_progress_location_reentry", new Dictionary<string, object>
                    {
                        { "view_seq", ViewSeq },
                        { "turn_player", turnPlayer },
                        { "owned_seat", OwnedSeat },
                        { "my_id", MyId },
                        { "param2", param2 },
                        { "armed_identity", armedIdentity },
                    });
                }
                else
                {
                    CampaignCpuProgressCheckResult pCheck = CampaignCpuProgressCheck.EvaluateAwaitingProgress(
                        progress,
                        StateMachine.ProgressWatch != null
                            ? StateMachine.ProgressWatch.CommittedToken
                            : null);
                    CampaignCpuEffectKind pEffect = CampaignCpuRunEffectRouter.MapProgressGate(
                        SoloTemporaryCpuState.AwaitingProgress, pCheck);
                    if (pEffect == CampaignCpuEffectKind.SuppressSameView)
                    {
                        return CampaignCpuDefaults.ScriptedHandledReturnCode;
                    }
                    StateMachine.CompleteAwaitingProgress();
                }
            }

            // NativeLease restore handshake
            if (StateMachine.State == SoloTemporaryCpuState.NativeLease)
            {
                // Prefer real CpuThinking; keep WaitFrame/CursorSet heuristic as secondary evidence.
                if (viewType == DuelViewType.CpuThinking
                    || viewType == DuelViewType.WaitFrame
                    || viewType == DuelViewType.CursorSet)
                {
                    StateMachine.MarkCpuThinkingObserved();
                }

                string deny;
                bool requireCpuThinking = false; // PR4a spike may enable

                // A5: restore Human on owned-turn PhaseChange → Main1/Main2 *before*
                // forwarding. Live M3: param2 is the new phase; GetCurrentPhase is still old;
                // OwnedSeat remains CPU until this flip. Do not score this boundary.
                if (StateMachine.CanRestoreOwnedMainCaptureBoundary(
                    DuelGeneration,
                    ViewSeq,
                    progress,
                    viewType,
                    param1,
                    param2,
                    turnPlayer,
                    OwnedSeat,
                    out deny))
                {
                    int humanRb;
                    CampaignCpuPlayerTypeTransitionResult tr = TryTransitionPlayerType(
                        OwnedSeat, (int)DuelPlayerType.Human, out humanRb);
                    string windowClassA5 = CampaignCpuWindowClassifier.ClassifyWindow(viewType, param1);
                    CampaignCpuEffectKind a5Effect = CampaignCpuRunEffectRouter.MapTransitionResult(
                        tr, restoreHumanPath: true);
                    if (a5Effect == CampaignCpuEffectKind.TransitionFailedForwardOriginal)
                    {
                        // Remain NativeLease; do not claim HumanOwned without positive readback.
                        AuditPlayerTypeTransitionFailure(
                            "owned_main_capture_boundary",
                            (int)DuelPlayerType.Human,
                            tr,
                            humanRb,
                            windowClassA5);
                        return originalRunEffect(id, param1, param2, param3);
                    }
                    var lease = StateMachine.ActiveLease;
                    StateMachine.RestoreHumanOwned();
                    CampaignCpuAuditLog.Write("temporary_cpu_restore", new Dictionary<string, object>
                    {
                        { "reason", "owned_main_capture_boundary" },
                        { "entry_view_seq", lease != null ? lease.EntryViewSeq : 0UL },
                        { "entry_reason", lease != null ? lease.Reason : null },
                        { "exit_view_seq", ViewSeq },
                        { "window_class", windowClassA5 },
                        { "turn", turn },
                        { "turn_player", turnPlayer },
                        { "phase_current", phase },
                        { "phase_new", param2 },
                        { "phase_change_seat", param1 },
                        { "acting", actingResolved ? actingPlayer : -1 },
                        { "my_id", MyId },
                        { "owned_seat", OwnedSeat },
                        { "duel_generation", DuelGeneration },
                        { "owned_is_human_readback", humanRb },
                        { "my_is_human_readback", TryReadIsHuman(MyId) },
                        { "transition_confirmed", true },
                        { "observed_cpu_thinking", lease != null && lease.ObservedCpuThinking },
                        { "deny_was", deny },
                    });
                    // Fall through as HumanOwned. PhaseChange is not a scripted window and
                    // acting is unresolved → single original forward below (no score/commit).
                }
                else if (StateMachine.CanRestoreFromNativeLease(
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
                    int humanRb;
                    CampaignCpuPlayerTypeTransitionResult tr = TryTransitionPlayerType(
                        OwnedSeat, (int)DuelPlayerType.Human, out humanRb);
                    string windowClassHs = CampaignCpuWindowClassifier.ClassifyWindow(viewType, param1);
                    CampaignCpuEffectKind hsEffect = CampaignCpuRunEffectRouter.MapTransitionResult(
                        tr, restoreHumanPath: true);
                    if (hsEffect == CampaignCpuEffectKind.TransitionFailedForwardOriginal)
                    {
                        AuditPlayerTypeTransitionFailure(
                            "handshake_boundary",
                            (int)DuelPlayerType.Human,
                            tr,
                            humanRb,
                            windowClassHs);
                        return originalRunEffect(id, param1, param2, param3);
                    }
                    var lease = StateMachine.ActiveLease;
                    StateMachine.RestoreHumanOwned();
                    CampaignCpuAuditLog.Write("temporary_cpu_restore", new Dictionary<string, object>
                    {
                        { "reason", "handshake_boundary" },
                        { "entry_view_seq", lease != null ? lease.EntryViewSeq : 0UL },
                        { "entry_reason", lease != null ? lease.Reason : null },
                        { "exit_view_seq", ViewSeq },
                        { "acting", actingResolved ? actingPlayer : -1 },
                        { "window_class", windowClassHs },
                        { "my_id", MyId },
                        { "owned_seat", OwnedSeat },
                        { "duel_generation", DuelGeneration },
                        { "owned_is_human_readback", humanRb },
                        { "my_is_human_readback", TryReadIsHuman(MyId) },
                        { "transition_confirmed", true },
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

            // Commit-on multi-step: owned-turn SelStand after scripted summon. Dual-Human
            // attributes RunDialog/SelStand to MyId; A4 TemporaryCpu cannot answer and
            // originalRunEffect paints ATK/DEF on the human. Auto DlgSetResult before A4.
            // LogOnly keeps A4/native (no intercept). Fail closed → A4 BeginFallback.
            if (CampaignCpuSelStand.ShouldIntercept(
                ClientSettings.CampaignCpuLogOnly,
                ClientSettings.CampaignCpuAllowScriptedCommits,
                SeatOwnershipConfirmed,
                StateMachine.ScriptingDisabledForDuel,
                StateMachine.State,
                viewType,
                param1,
                turnPlayer,
                OwnedSeat,
                MyId))
            {
                return HandleOwnedTurnSelStand(
                    id, param1, param2, param3, progress, actingPlayer,
                    turn, turnPlayer, phase, originalRunEffect);
            }

            // Commit-on multi-step: owned-turn WaitInput/Location after scripted
            // Activate/Set/Summon. Dual-Human attributes zone select to MyId; A4 cannot
            // answer and originalRunEffect paints "Select position for …" on the human
            // (live Future Fusion 2026-07-23). Auto Decide@PosSelect before A4/pass_through.
            // LogOnly keeps native (no intercept). Fail closed → BeginFallback.
            if (CampaignCpuLocation.ShouldIntercept(
                ClientSettings.CampaignCpuLogOnly,
                ClientSettings.CampaignCpuAllowScriptedCommits,
                SeatOwnershipConfirmed,
                StateMachine.ScriptingDisabledForDuel,
                StateMachine.State,
                viewType,
                param1,
                turnPlayer,
                OwnedSeat,
                MyId))
            {
                return HandleOwnedTurnLocation(
                    id, param1, param2, param3, progress, actingPlayer,
                    turn, turnPlayer, phase, originalRunEffect);
            }

            // A4 dual-Human residual (live 2026-07-22): under dual-Human, MD attributes
            // opponent trap/chain RunDialog + CheckChain to MyId (run_dialog_user=0).
            // Re-lease OwnedSeat→CPU for every response-class window while HumanOwned,
            // before pass_through_myid — independent of acting resolution.
            // Response leases never one-shot restore (A5 does not weaken this).
            // Location is not response-class — mechanical path above answers it.
            if (CampaignCpuWindowClassifier.ShouldReLeaseDualHumanResponseWindow(
                StateMachine.State, viewType, param1))
            {
                // Production-used A4 seam (MapDualHumanResponseHold → BeginNativeLeaseAndForward).
                CampaignCpuEffectKind a4Effect = CampaignCpuRunEffectRouter.MapDualHumanResponseHold();
                if (a4Effect != CampaignCpuEffectKind.BeginNativeLeaseAndForward)
                {
                    return originalRunEffect(id, param1, param2, param3);
                }
                string holdReason = CampaignCpuWindowClassifier.DualHumanResponseHoldReason(
                    actingResolved,
                    actingPlayer,
                    OwnedSeat,
                    MyId);
                return BeginFallback(
                    id, param1, param2, param3, progress, holdReason, originalRunEffect);
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
                // A5 fail-closed: MyId sample on owned-turn Main is stale dual-Human
                // residual — never pass-through as human Main; never score/commit. Re-lease.
                if (CampaignCpuWindowClassifier.IsStaleMyIdOwnedMainWaitInput(
                    viewType, param1, actingResolved, actingPlayer, turnPlayer, OwnedSeat, MyId))
                {
                    return BeginFallback(
                        id, param1, param2, param3, progress,
                        "stale_myid_owned_main", originalRunEffect);
                }

                // Human seat: never CampaignCpu commit (PR2b pass criterion).
                // Response-class windows already re-leased above (A4).
                CampaignCpuEffectKind passEffect = CampaignCpuRunEffectRouter.MapPassThrough();
                if (ClientSettings.CampaignCpuProbeLogging
                    && IsProbeInterestingView(viewType, param1))
                {
                    CampaignCpuAuditLog.Write("pass_through_myid", new Dictionary<string, object>
                    {
                        { "view_seq", ViewSeq },
                        { "my_id", MyId },
                        { "duel_generation", DuelGeneration },
                        { "window_class",
                            CampaignCpuWindowClassifier.ClassifyWindow(viewType, param1) },
                        { "effect", passEffect.ToString() },
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
                || !SeatOwnershipConfirmed
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
                else if (!SeatOwnershipConfirmed)
                {
                    reason = PendingSeatOwnershipAssert
                        ? "seat_assert_pending"
                        : "seat_assert_unconfirmed";
                }
                else
                {
                    // Response-class already handled in A4 HumanOwned gate above.
                    reason = "v1_non_main_phase";
                }
                // Production-used forced-native seam (always-native / disabled / non-scripted).
                CampaignCpuEffectKind forced = CampaignCpuRunEffectRouter.MapOwnedWindowForcedNative();
                if (forced != CampaignCpuEffectKind.BeginNativeLeaseAndForward)
                {
                    return originalRunEffect(id, param1, param2, param3);
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
                // Production decision rows must carry ownership + generation for S10/analyzer.
                obs.MyId = MyId;
                obs.DuelGeneration = DuelGeneration;

                progress = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                    DuelGeneration,
                    viewType,
                    param1,
                    param2,
                    param3,
                    actingPlayer,
                    obs.Turn,
                    obs.Phase,
                    CampaignCpuObservation.FingerprintLegalActions(obs.LegalActions));

                if (obs.PredicateQueryFailed)
                {
                    CampaignCpuAuditLog.Write("predicate_eval_failed", new Dictionary<string, object>
                    {
                        { "view_seq", ViewSeq },
                        { "duel_generation", DuelGeneration },
                        { "chapter_id", ChapterId },
                        { "owned_seat", OwnedSeat },
                        { "acting_player", actingPlayer },
                        { "window_class", obs.WindowClass },
                    });
                }

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
                    decision = CampaignCpuScorer.Decide(
                        obs,
                        ActivePack,
                        predicateEvalFailed: obs.PredicateQueryFailed,
                        appliedDecisionCount: DecisionCount);
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

                    if (CampaignCpuScorer.IsDecisionCapReached(ActivePack.Policy, DecisionCount))
                    {
                        return BeginFallback(
                            id, param1, param2, param3, progress,
                            "max_decisions_per_duel", originalRunEffect);
                    }

                    CampaignCpuCommitOutcome outcome = CampaignCpuCommit.TryApplyLive(decision.Action);
                    CampaignCpuEffectKind commitEffect = CampaignCpuRunEffectRouter.MapCommitOutcome(outcome);
                    if (commitEffect == CampaignCpuEffectKind.CommitApplied)
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
                            { "decision_count", DecisionCount },
                            { "my_id", MyId },
                            { "owned_seat", OwnedSeat },
                            { "duel_generation", DuelGeneration },
                            { "turn", obs.Turn },
                            { "turn_player", obs.TurnPlayer },
                            { "phase", obs.Phase },
                        });
                        return CampaignCpuDefaults.ScriptedHandledReturnCode;
                    }
                    if (commitEffect == CampaignCpuEffectKind.BeginNativeLeaseAndForward)
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
                        { "my_id", MyId },
                        { "owned_seat", OwnedSeat },
                        { "duel_generation", DuelGeneration },
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

        /// <summary>
        /// Mechanical SelStand (ATK/DEF) for owned-turn dual-Human residual under commit-on.
        /// Never originalRunEffect on success (that paints the position UI on MyId).
        /// Does not count toward pack max_decisions_per_duel.
        /// </summary>
        static int HandleOwnedTurnSelStand(
            int id,
            int param1,
            int param2,
            int param3,
            CampaignCpuProgressToken progress,
            int actingPlayer,
            int turn,
            int turnPlayer,
            int phase,
            OriginalRunEffectDelegate originalRunEffect)
        {
            int mask = 0;
            try
            {
                mask = DuelDll.CampaignCpu_GetSummonPositionMask();
            }
            catch
            {
                mask = 0;
            }

            CampaignCpuLegalAction action;
            if (!CampaignCpuSelStand.TryBuildMechanicalAction(mask, out action) || action == null)
            {
                CampaignCpuAuditLog.Write("sel_stand_mask_empty", new Dictionary<string, object>
                {
                    { "view_seq", ViewSeq },
                    { "mask", mask },
                    { "owned_seat", OwnedSeat },
                    { "my_id", MyId },
                    { "acting_player", actingPlayer },
                    { "duel_generation", DuelGeneration },
                    { "turn", turn },
                    { "turn_player", turnPlayer },
                    { "phase", phase },
                });
                return BeginFallback(
                    id, param1, param2, param3, progress, "sel_stand_mask_empty", originalRunEffect);
            }

            int armTurn = turn >= 0 ? turn : (progress != null ? progress.Turn : 0);
            int armPhase = phase >= 0 ? phase : (progress != null ? progress.Phase : 0);

            // Refresh progress fingerprint so AwaitingProgress treats this commit distinctly.
            string fp = CampaignCpuObservation.FingerprintLegalActions(
                new List<CampaignCpuLegalAction> { action });
            CampaignCpuProgressToken armProgress = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                DuelGeneration,
                (DuelViewType)id,
                param1,
                param2,
                param3,
                actingPlayer,
                armTurn,
                armPhase,
                fp);

            var decision = CampaignCpuDecision.Commit(
                CampaignCpuRoute.MechanicalAuto,
                action,
                CampaignCpuSelStand.Reason,
                CampaignCpuSelStand.RuleId,
                0,
                true);

            // Mechanical observation: real turn/phase from live reads (not synthetic zeros).
            var obs = new CampaignCpuObservation
            {
                ChapterId = ChapterId,
                CampaignCpuViewSeq = ViewSeq,
                ViewType = (DuelViewType)id,
                ViewParam1 = param1,
                ViewParam2 = param2,
                ViewParam3 = param3,
                ActingPlayer = actingPlayer,
                OwnedSeat = OwnedSeat,
                MyId = MyId,
                DuelGeneration = DuelGeneration,
                Turn = armTurn,
                TurnPlayer = turnPlayer >= 0 ? turnPlayer : OwnedSeat,
                Phase = armPhase,
                WindowClass = CampaignCpuWindowClassifier.ClassifyWindow((DuelViewType)id, param1),
                LegalActions = new List<CampaignCpuLegalAction> { action },
            };
            CampaignCpuAuditLog.WriteRaw(
                CampaignCpuAuditSerializer.SerializeDecision(
                    ViewSeq, decision, obs, shadowOnly: false));

            CampaignCpuCommitOutcome outcome = CampaignCpuCommit.TryApplyLive(action);
            CampaignCpuEffectKind commitEffect = CampaignCpuRunEffectRouter.MapCommitOutcome(outcome);
            if (commitEffect == CampaignCpuEffectKind.CommitApplied)
            {
                // Do not increment DecisionCount — not a pack-scored Main decision.
                StateMachine.ArmAwaitingProgress(
                    ViewSeq,
                    armProgress,
                    action.CanonicalIdentity,
                    DateTime.UtcNow);
                CampaignCpuAuditLog.Write("commit_applied", new Dictionary<string, object>
                {
                    { "view_seq", ViewSeq },
                    { "rule_id", CampaignCpuSelStand.RuleId },
                    { "action", action.CanonicalIdentity },
                    { "dialog_result", action.DialogResult },
                    { "summon_position_mask", mask },
                    { "decision_count", DecisionCount },
                    { "mechanical", true },
                    { "my_id", MyId },
                    { "owned_seat", OwnedSeat },
                    { "duel_generation", DuelGeneration },
                    { "turn", armTurn },
                    { "turn_player", turnPlayer },
                    { "phase", armPhase },
                });
                return CampaignCpuDefaults.ScriptedHandledReturnCode;
            }
            if (commitEffect == CampaignCpuEffectKind.BeginNativeLeaseAndForward)
            {
                return BeginFallback(
                    id, param1, param2, param3, progress, "sel_stand_commit_not_started", originalRunEffect);
            }
            StateMachine.EnterCommitQuarantine(
                ViewSeq,
                armProgress,
                action.CanonicalIdentity,
                DateTime.UtcNow,
                "sel_stand_commit_indeterminate");
            CampaignCpuAuditLog.Write("commit_indeterminate", new Dictionary<string, object>
            {
                { "view_seq", ViewSeq },
                { "action", action.CanonicalIdentity },
                { "rule_id", CampaignCpuSelStand.RuleId },
                { "dialog_result", action.DialogResult },
                { "my_id", MyId },
                { "owned_seat", OwnedSeat },
                { "duel_generation", DuelGeneration },
            });
            return CampaignCpuDefaults.ScriptedHandledReturnCode;
        }

        /// <summary>
        /// Mechanical WaitInput/Location (zone place) for owned-turn dual-Human residual
        /// under commit-on. Never originalRunEffect on success (that paints zone UI on MyId).
        /// Always DLL_DuelComDefaultLocation (live: Decide@PosSelect can no-op and re-prompt
        /// Location → AwaitingProgress suppress → Main stall). Does not count toward pack
        /// max_decisions_per_duel.
        /// </summary>
        static int HandleOwnedTurnLocation(
            int id,
            int param1,
            int param2,
            int param3,
            CampaignCpuProgressToken progress,
            int actingPlayer,
            int turn,
            int turnPlayer,
            int phase,
            OriginalRunEffectDelegate originalRunEffect)
        {
            int mask = 0;
            int dlgUniqueId = 0;
            int cardUniqueId = 0;
            int cardId = 0;
            try
            {
                mask = DuelDll.CampaignCpu_GetSummonPositionMask();
                dlgUniqueId = DuelDll.CampaignCpu_GetSummoningMonsterUniqueId();
            }
            catch
            {
                mask = 0;
                dlgUniqueId = 0;
            }

            // param2 is WaitInput Location unique-id (same as LLM view_param2).
            cardUniqueId = CampaignCpuLocation.ResolveCardUniqueId(dlgUniqueId, param2);
            if (cardUniqueId > 0)
            {
                try
                {
                    cardId = DuelDll.CampaignCpu_GetCardIdByUniqueId(cardUniqueId);
                }
                catch
                {
                    cardId = 0;
                }
            }

            CampaignCpuLegalAction action;
            string ruleId;
            string reason;
            if (!CampaignCpuLocation.TryResolveMechanicalPlacement(
                    mask, OwnedSeat, cardId, out action, out ruleId, out reason)
                || action == null)
            {
                CampaignCpuAuditLog.Write("location_mask_empty", new Dictionary<string, object>
                {
                    { "view_seq", ViewSeq },
                    { "mask", mask },
                    { "dlg_unique_id", dlgUniqueId },
                    { "view_param2", param2 },
                    { "card_unique_id", cardUniqueId },
                    { "card_id", cardId },
                    { "owned_seat", OwnedSeat },
                    { "my_id", MyId },
                    { "acting_player", actingPlayer },
                });
                return BeginFallback(
                    id, param1, param2, param3, progress, "location_mask_empty", originalRunEffect);
            }

            bool usedDefault = CampaignCpuLocation.IsDefaultLocationAction(action);
            if (usedDefault)
            {
                // Probe residual: engine mask empty at intercept; DefaultLocation path.
                CampaignCpuAuditLog.Write("location_default_location", new Dictionary<string, object>
                {
                    { "view_seq", ViewSeq },
                    { "mask", mask },
                    { "dlg_unique_id", dlgUniqueId },
                    { "view_param2", param2 },
                    { "card_unique_id", cardUniqueId },
                    { "card_id", cardId },
                    { "owned_seat", OwnedSeat },
                });
            }

            int armTurn = turn >= 0 ? turn : (progress != null ? progress.Turn : 0);
            int armPhase = phase >= 0 ? phase : (progress != null ? progress.Phase : 0);

            string fp = CampaignCpuObservation.FingerprintLegalActions(
                new List<CampaignCpuLegalAction> { action });
            CampaignCpuProgressToken armProgress = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                DuelGeneration,
                (DuelViewType)id,
                param1,
                param2,
                param3,
                actingPlayer,
                armTurn,
                armPhase,
                fp);

            var decision = CampaignCpuDecision.Commit(
                CampaignCpuRoute.MechanicalAuto,
                action,
                reason,
                ruleId,
                0,
                true);

            var obs = new CampaignCpuObservation
            {
                ChapterId = ChapterId,
                CampaignCpuViewSeq = ViewSeq,
                ViewType = (DuelViewType)id,
                ViewParam1 = param1,
                ViewParam2 = param2,
                ViewParam3 = param3,
                ActingPlayer = actingPlayer,
                OwnedSeat = OwnedSeat,
                MyId = MyId,
                DuelGeneration = DuelGeneration,
                Turn = armTurn,
                TurnPlayer = turnPlayer >= 0 ? turnPlayer : OwnedSeat,
                Phase = armPhase,
                WindowClass = CampaignCpuWindowClassifier.ClassifyWindow((DuelViewType)id, param1),
                LegalActions = new List<CampaignCpuLegalAction> { action },
            };
            CampaignCpuAuditLog.WriteRaw(
                CampaignCpuAuditSerializer.SerializeDecision(
                    ViewSeq, decision, obs, shadowOnly: false));

            CampaignCpuCommitOutcome outcome = CampaignCpuCommit.TryApplyLive(action);
            CampaignCpuEffectKind commitEffect = CampaignCpuRunEffectRouter.MapCommitOutcome(outcome);
            if (commitEffect == CampaignCpuEffectKind.CommitApplied)
            {
                StateMachine.ArmAwaitingProgress(
                    ViewSeq,
                    armProgress,
                    action.CanonicalIdentity,
                    DateTime.UtcNow);
                CampaignCpuAuditLog.Write("commit_applied", new Dictionary<string, object>
                {
                    { "view_seq", ViewSeq },
                    { "rule_id", ruleId },
                    { "action", action.CanonicalIdentity },
                    { "zone", usedDefault ? null : (object)action.Position },
                    { "player", action.Player },
                    { "placement_mask", mask },
                    { "card_unique_id", cardUniqueId },
                    { "card_id", cardId },
                    { "default_location", usedDefault },
                    { "decision_count", DecisionCount },
                    { "mechanical", true },
                    { "my_id", MyId },
                    { "owned_seat", OwnedSeat },
                    { "duel_generation", DuelGeneration },
                    { "turn", armTurn },
                    { "turn_player", turnPlayer },
                    { "phase", armPhase },
                });
                return CampaignCpuDefaults.ScriptedHandledReturnCode;
            }
            if (commitEffect == CampaignCpuEffectKind.BeginNativeLeaseAndForward)
            {
                return BeginFallback(
                    id, param1, param2, param3, progress,
                    usedDefault ? "location_default_not_started" : "location_commit_not_started",
                    originalRunEffect);
            }
            StateMachine.EnterCommitQuarantine(
                ViewSeq,
                armProgress,
                action.CanonicalIdentity,
                DateTime.UtcNow,
                usedDefault ? "location_default_indeterminate" : "location_commit_indeterminate");
            CampaignCpuAuditLog.Write("commit_indeterminate", new Dictionary<string, object>
            {
                { "view_seq", ViewSeq },
                { "action", action.CanonicalIdentity },
                { "rule_id", ruleId },
                { "zone", usedDefault ? null : (object)action.Position },
                { "default_location", usedDefault },
                { "my_id", MyId },
                { "owned_seat", OwnedSeat },
                { "duel_generation", DuelGeneration },
            });
            return CampaignCpuDefaults.ScriptedHandledReturnCode;
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
            string windowClass = CampaignCpuWindowClassifier.ClassifyWindow((DuelViewType)id, p1);

            // Confirm CPU ownership before claiming NativeLease. A failed/unknown flip must
            // never proceed as if native CPU owns the window (M5).
            int cpuRb;
            CampaignCpuPlayerTypeTransitionResult cpuTr = TryTransitionPlayerType(
                OwnedSeat, (int)DuelPlayerType.CPU, out cpuRb);
            // Production-used seam: same MapTransitionResult harness tests exercise.
            CampaignCpuEffectKind cpuEffect = CampaignCpuRunEffectRouter.MapTransitionResult(
                cpuTr, restoreHumanPath: false);
            if (cpuEffect == CampaignCpuEffectKind.TransitionFailedForwardOriginal)
            {
                AuditPlayerTypeTransitionFailure(
                    "begin_fallback:" + reason,
                    (int)DuelPlayerType.CPU,
                    cpuTr,
                    cpuRb,
                    windowClass);
                // Fail closed: do not enter NativeLease; forward once under current HumanOwned.
                StateMachine.DisableScripting("player_type_transition_failed_cpu");
                return originalRunEffect(id, p1, p2, p3);
            }

            StateMachine.BeginNativeLease(
                reason,
                DuelGeneration,
                ViewSeq,
                progress,
                OwnedSeat,
                DateTime.UtcNow);
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
            CampaignCpuAuditLog.Write("temporary_cpu_begin", new Dictionary<string, object>
            {
                { "reason", reason },
                { "view_seq", ViewSeq },
                { "owned_seat", OwnedSeat },
                { "my_id", MyId },
                { "duel_generation", DuelGeneration },
                { "window_class", windowClass },
                { "owned_is_human_readback", cpuRb },
                { "my_is_human_readback", TryReadIsHuman(MyId) },
                { "transition_confirmed", true },
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
                int humanRb;
                CampaignCpuPlayerTypeTransitionResult humanTr = TryTransitionPlayerType(
                    OwnedSeat, (int)DuelPlayerType.Human, out humanRb);
                if (!CampaignCpuControlPolicy.IsTransitionConfirmed(humanTr))
                {
                    // Stay NativeLease rather than claiming HumanOwned without proof.
                    AuditPlayerTypeTransitionFailure(
                        "oneshot_restore",
                        (int)DuelPlayerType.Human,
                        humanTr,
                        humanRb,
                        windowClass);
                    return ret;
                }
                StateMachine.RestoreHumanOwned();
                CampaignCpuAuditLog.Write("temporary_cpu_oneshot_restore", new Dictionary<string, object>
                {
                    { "reason", reason },
                    { "view_seq", ViewSeq },
                    { "window_class", windowClass },
                    { "oneshot_policy", "draw_phase_only" },
                    { "owned_is_human_readback", humanRb },
                    { "my_is_human_readback", TryReadIsHuman(MyId) },
                    { "transition_confirmed", true },
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
            // M3 candidates also appear in general acting probes when decision-family is sparse.
            if (CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(viewType, param1))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// M3 LogOnly instrumentation: candidate pre-Main handoff views under NativeLease.
        /// Does not flip player types or alter restore policy.
        /// </summary>
        static void WriteOwnedMainBoundaryProbe(
            DuelViewType viewType,
            int param1,
            int param2,
            int param3,
            int doCommandUser,
            int runDialogUser,
            int turnPlayer,
            int turn,
            int phase,
            bool actingResolved,
            int actingPlayer)
        {
            var lease = StateMachine.ActiveLease;
            int phaseMain1 = (int)DuelPhase.Main1;
            int phaseMain2 = (int)DuelPhase.Main2;
            CampaignCpuAuditLog.Write("owned_main_boundary_probe", new Dictionary<string, object>
            {
                { "duel_generation", DuelGeneration },
                { "view_seq", ViewSeq },
                { "view", viewType.ToString() },
                { "param1", param1 },
                { "param2", param2 },
                { "param3", param3 },
                { "probe_kind", CampaignCpuWindowClassifier.BoundaryProbeKind(viewType, param1) },
                { "window_class", CampaignCpuWindowClassifier.ClassifyWindow(viewType, param1) },
                { "turn", turn },
                { "turn_player", turnPlayer },
                { "phase", phase },
                { "turn_player_is_owned", turnPlayer == OwnedSeat },
                { "phase_is_main", phase == phaseMain1 || phase == phaseMain2 },
                { "do_command_user", doCommandUser },
                { "run_dialog_user", runDialogUser },
                { "acting_resolved", actingResolved },
                // Acting seat sampled before any TemporaryCpu restore flip on this callback.
                { "acting_player_before_flip", actingResolved ? actingPlayer : -1 },
                { "owned_seat", OwnedSeat },
                { "my_id", MyId },
                { "lease_reason", lease != null ? lease.Reason : null },
                { "lease_entry_view_seq", lease != null ? lease.EntryViewSeq : 0UL },
                { "lease_entry_reason", lease != null ? lease.Reason : null },
                { "sm_state", StateMachine.State.ToString() },
                { "owned_is_human_readback", TryReadIsHuman(OwnedSeat) },
                { "my_is_human_readback", TryReadIsHuman(MyId) },
                { "observed_cpu_thinking", lease != null && lease.ObservedCpuThinking },
            });
        }

        /// <summary>
        /// Probe-only: snapshot OwnedSeat field zones 0–12, emit delta when cards change.
        /// Solo engine exposes face-down card_ids locally so we can label set traps vs spells.
        /// </summary>
        static void TryWriteOwnedFieldProbe(
            DuelViewType viewType,
            int param1,
            int turn,
            int turnPlayer,
            int phase)
        {
            if (!IsEngineWorkReady())
            {
                return;
            }
            List<CampaignCpuZoneCard> current;
            try
            {
                current = SnapshotOwnedFieldZones(OwnedSeat);
            }
            catch
            {
                return;
            }
            string fp = CampaignCpuFieldDiff.Fingerprint(current);
            if (string.Equals(fp, LastOwnedFieldFingerprint, StringComparison.Ordinal))
            {
                return;
            }

            var added = new List<CampaignCpuZoneCard>();
            var removed = new List<CampaignCpuZoneCard>();
            var changed = new List<CampaignCpuZoneCard>();
            CampaignCpuFieldDiff.Diff(LastOwnedFieldSnapshot, current, added, removed, changed);

            var setFacedownSt = new List<object>();
            var setTraps = new List<object>();
            for (int i = 0; i < added.Count; i++)
            {
                CampaignCpuZoneCard c = added[i];
                if (c == null || !c.IsSpellTrapZone || !c.IsFaceDown)
                {
                    continue;
                }
                Dictionary<string, object> d = c.ToAuditDict();
                d["play_hint"] = CampaignCpuFieldDiff.DescribePlayHint(c);
                setFacedownSt.Add(d);
                if (c.IsTrap)
                {
                    setTraps.Add(d);
                }
            }

            CampaignCpuAuditLog.Write("owned_field_delta", new Dictionary<string, object>
            {
                { "duel_generation", DuelGeneration },
                { "view_seq", ViewSeq },
                { "view", viewType.ToString() },
                { "param1", param1 },
                { "window_class", CampaignCpuWindowClassifier.ClassifyWindow(viewType, param1) },
                { "turn", turn },
                { "turn_player", turnPlayer },
                { "phase", phase },
                { "owned_seat", OwnedSeat },
                { "note", "card_id is engine-local (solo); face-down ids are known to the client engine" },
                { "added", CampaignCpuFieldDiff.ToAuditList(added) },
                { "removed", CampaignCpuFieldDiff.ToAuditList(removed) },
                { "changed", CampaignCpuFieldDiff.ToAuditList(changed) },
                { "set_facedown_spell_trap", setFacedownSt },
                { "set_facedown_traps", setTraps },
                { "field", CampaignCpuFieldDiff.ToAuditList(current) },
                { "field_fingerprint", fp },
            });

            // One line per newly set face-down ST for easy grepping.
            for (int i = 0; i < added.Count; i++)
            {
                CampaignCpuZoneCard c = added[i];
                if (c == null || !c.IsSpellTrapZone || !c.IsFaceDown)
                {
                    continue;
                }
                CampaignCpuAuditLog.Write("owned_card_set", new Dictionary<string, object>
                {
                    { "duel_generation", DuelGeneration },
                    { "view_seq", ViewSeq },
                    { "turn", turn },
                    { "turn_player", turnPlayer },
                    { "phase", phase },
                    { "owned_seat", OwnedSeat },
                    { "position", c.Position },
                    { "card_id", c.CardId },
                    { "unique_id", c.UniqueId },
                    { "face", c.Face },
                    { "is_trap", c.IsTrap },
                    { "is_trap_monster", c.IsTrapMonster },
                    { "play_hint", CampaignCpuFieldDiff.DescribePlayHint(c) },
                    { "view", viewType.ToString() },
                });
            }

            LastOwnedFieldSnapshot = current;
            LastOwnedFieldFingerprint = fp;
        }

        static List<CampaignCpuZoneCard> SnapshotOwnedFieldZones(int player)
        {
            var cards = new List<CampaignCpuZoneCard>();
            // Monster 0–6 + Spell/Trap 7–12 (single-card zones; index usually 0).
            for (int pos = 0; pos <= CampaignCpuZoneCard.PosSpellTrapMax; pos++)
            {
                int n = 0;
                try
                {
                    n = DuelDll.CampaignCpu_GetCardNum(player, pos);
                }
                catch
                {
                    continue;
                }
                if (n <= 0)
                {
                    continue;
                }
                for (int idx = 0; idx < n; idx++)
                {
                    int uid = 0;
                    int cardId = 0;
                    int face = CampaignCpuZoneCard.FaceDownOrNonPublic;
                    bool isTrap = false;
                    bool isTrapMonster = false;
                    try
                    {
                        uid = DuelDll.CampaignCpu_GetCardUniqueId(player, pos, idx);
                        if (uid > 0)
                        {
                            cardId = DuelDll.CampaignCpu_GetCardIdByUniqueId(uid);
                        }
                        face = DuelDll.CampaignCpu_GetCardFace(player, pos, idx);
                        // locate == position for single-card field zones.
                        isTrap = DuelDll.CampaignCpu_IsThisTrap(player, pos);
                        isTrapMonster = DuelDll.CampaignCpu_IsThisTrapMonster(player, pos) != 0;
                    }
                    catch
                    {
                        // Keep partial card row; still useful if id/face succeeded.
                    }
                    if (uid <= 0 && cardId <= 0)
                    {
                        continue;
                    }
                    cards.Add(new CampaignCpuZoneCard
                    {
                        Position = pos,
                        Index = idx,
                        UniqueId = uid,
                        CardId = cardId,
                        Face = face,
                        IsTrap = isTrap,
                        IsTrapMonster = isTrapMonster,
                    });
                }
            }
            return cards;
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using YgoMaster.Net.Message;
using YgoMaster.Net;
using YgoMaster;
using System.Runtime.InteropServices;
using IL2CPP;

// NOTE: Be careful with threading here
// - If you want to run something on the duel thread use ActionsToRunInNextSysAct
// - If you want to run something in the game thread use TradeUtils.AddAction()

namespace YgoMasterClient
{
    unsafe static partial class DuelDll
    {
        public static List<byte> ReplayData = new List<byte>();

        public static List<Action> ActionsToRunInNextSysAct = new List<Action>();

        static DateTime LastSysActLogTime;
        static object LogLocker = new object();
        static LlmBrokerRequestGate LlmBrokerGate = new LlmBrokerRequestGate();
        static LlmTurnMemoryTracker LlmTurnMemory = new LlmTurnMemoryTracker(6);
        static LlmDuelHistoryTracker LlmDuelHistory = new LlmDuelHistoryTracker(null);
        static LlmAutomaticActionLoopGuard LlmAutomaticActionGuard =
            new LlmAutomaticActionLoopGuard();
        static LlmTemporaryCpuSelectionCoordinator LlmTemporaryCpuSelection =
            new LlmTemporaryCpuSelectionCoordinator();
        static int LlmBrokerDuelGeneration;
        static int LlmDuelHistoryGeneration;
        static bool AllowLlmAutomaticMain2FollowUp;
        static AttackTargetContext PendingLlmAttackTargetContext;

        public static IntPtr CardPropMem;

        public static DuelResultType SpecialResultType;
        public static DuelFinishType SpecialFinishType;
        public static int DuelEndResult;
        public static int DuelEndFinish;
        public static int DuelEndFinishCardID;
        public static bool HasNetworkError;
        public static bool HasDuelStart;
        public static bool HasDuelEnd;
        public static bool HasSysActFinished;
        public static DateTime BeginDuelTime;
        public static ulong RunEffectSeq;
        public static bool IsPvpDuel;
        public static bool IsPvpSpectator;
        public static PvpSpectatorRapidState PvpSpectatorRapidState;
        public static int SpectatorCount;
        public static int MyID;
        public static bool SendLiveRecordData;
        public static bool IsFieldGuideNear;
        public static bool IsFieldGuideNearReal;
        public static DateTime LastFieldGuideUpdate;
        public static bool IsInsideDuelTimerPrepareToDuel;
        public static bool IsTimerEnabled;
        public static DateTime LastCheckTimeOver;
        public static int AddTimeAtStartOfTurn;
        public static int AddTimeAtEndOfTurn;
        public static int RivalID
        {
            get { return MyID == 0 ? 1 : 0; }
        }

        static PvpEngineState pvpEngineState = new PvpEngineState();
        static Queue<PvpEngineState> pvpEngineStates = new Queue<PvpEngineState>();
        static Queue<PvpEngineState> freePvpEngineStates = new Queue<PvpEngineState>();

        static IntPtr engineInstance;
        static IntPtr engineInstanceReplayStream;
        static IL2Field fieldEngineGameMode;
        static IL2Field fieldEngineInstance;
        static IL2Field fieldEngineReplayStream;
        static IL2Method methodReplayStreamAdd;
        static IL2Method methodReplayStreamFinish;
        static IntPtr activePlayerFieldEffectInstance;
        static IL2Method methodSwitchGuide;

        delegate void Del_SetGuideEnable(IntPtr thisPtr, bool near, bool enable, bool turnchange);
        static Hook<Del_SetGuideEnable> hookSetGuideEnable;

        // Engine
        delegate int Del_RunEffect(int id, int param1, int param2, int param3);
        static Del_RunEffect myRunEffect = RunEffect;
        static Del_RunEffect originalRunEffect;

        delegate int Del_IsBusyEffect(int id);
        static Del_IsBusyEffect myIsBusyEffect = IsBusyEffect;
        static Del_IsBusyEffect originalIsBusyEffect;

        delegate void Del_DLL_SetEffectDelegate(IntPtr runEffect, IntPtr isBusyEffect);
        static Hook<Del_DLL_SetEffectDelegate> hookDLL_SetEffectDelegate;

        delegate void Del_DLL_DuelComMovePhase(int phase);
        static Hook<Del_DLL_DuelComMovePhase> hookDLL_DuelComMovePhase;

        delegate void Del_DLL_DuelComDoCommand(int player, int position, int index, int commandId);
        static Hook<Del_DLL_DuelComDoCommand> hookDLL_DuelComDoCommand;

        delegate int Del_DLL_DuelComCancelCommand();
        static Hook<Del_DLL_DuelComCancelCommand> hookDLL_DuelComCancelCommand;

        delegate int Del_DLL_DuelComCancelCommand2(bool decide);
        static Hook<Del_DLL_DuelComCancelCommand2> hookDLL_DuelComCancelCommand2;

        delegate void Del_DLL_DuelDlgSetResult(uint result);
        static Hook<Del_DLL_DuelDlgSetResult> hookDLL_DuelDlgSetResult;

        delegate void Del_DLL_DuelListSetCardExData(int index, int data);
        static Hook<Del_DLL_DuelListSetCardExData> hookDLL_DuelListSetCardExData;

        delegate void Del_DLL_DuelListSetIndex(int index);
        static Hook<Del_DLL_DuelListSetIndex> hookDLL_DuelListSetIndex;

        delegate void Del_DLL_DuelListInitString();
        static Hook<Del_DLL_DuelListInitString> hookDLL_DuelListInitString;

        public delegate void Del_DLL_DuelComCheatCard(int player, int position, int index, int cardId, int face, int turn);
        public static Del_DLL_DuelComCheatCard DLL_DuelComCheatCard;

        public delegate void Del_DLL_DuelComDoDebugCommand(int player, int position, int index, int commandId);
        public static Del_DLL_DuelComDoDebugCommand DLL_DuelComDoDebugCommand;

        public delegate void Del_DLL_DuelComDebugCommand();
        public static Del_DLL_DuelComDebugCommand DLL_DuelComDebugCommand;

        delegate int Del_DLL_DuelSysAct();
        static Hook<Del_DLL_DuelSysAct> hookDLL_DuelSysAct;

        delegate void Del_AddRecord(IntPtr ptr, int size);
        delegate void Del_DLL_SetAddRecordDelegate(Del_AddRecord addRecord);
        static Del_DLL_SetAddRecordDelegate DLL_SetAddRecordDelegate;

        static DuelDll()
        {
            IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");

            IL2Class engineClassInfo = assembly.GetClass("Engine", "YgomGame.Duel");
            fieldEngineInstance = engineClassInfo.GetField("s_instance");
            fieldEngineReplayStream = engineClassInfo.GetField("replayStream");
            fieldEngineGameMode = engineClassInfo.GetField("gameMode");

            IL2Class replayStreamClassInfo = assembly.GetClass("ReplayStream", "YgomGame.Duel");
            methodReplayStreamAdd = replayStreamClassInfo.GetMethod("Add");
            methodReplayStreamFinish = replayStreamClassInfo.BaseType.GetMethod("Finish");

            IL2Class fieldEffectClassInfo = assembly.GetClass("ActivePlayerFieldEffect", "YgomGame.Duel");
            hookSetGuideEnable = new Hook<Del_SetGuideEnable>(SetGuideEnable, fieldEffectClassInfo.GetMethod("SetGuideEnable"));
            methodSwitchGuide = fieldEffectClassInfo.GetMethod("SwitchGuide");

            IntPtr lib = PInvoke.LoadLibrary(Path.Combine("masterduel_Data", "Plugins", "x86_64", "duel.dll"));
            if (lib == IntPtr.Zero)
            {
                throw new Exception("Failed to load duel.dll");
            }

            InitProxyFunctions(lib);

            hookDLL_SetEffectDelegate = new Hook<Del_DLL_SetEffectDelegate>(DLL_SetEffectDelegate, PInvoke.GetProcAddress(lib, "DLL_SetEffectDelegate"));
            hookDLL_DuelSysAct = new Hook<Del_DLL_DuelSysAct>(DLL_DuelSysAct, PInvoke.GetProcAddress(lib, "DLL_DuelSysAct"));

            hookDLL_DuelComMovePhase = new Hook<Del_DLL_DuelComMovePhase>(DLL_DuelComMovePhase, PInvoke.GetProcAddress(lib, "DLL_DuelComMovePhase"));
            hookDLL_DuelComDoCommand = new Hook<Del_DLL_DuelComDoCommand>(DLL_DuelComDoCommand, PInvoke.GetProcAddress(lib, "DLL_DuelComDoCommand"));
            hookDLL_DuelComCancelCommand = new Hook<Del_DLL_DuelComCancelCommand>(DLL_DuelComCancelCommand, PInvoke.GetProcAddress(lib, "DLL_DuelComCancelCommand"));
            hookDLL_DuelComCancelCommand2 = new Hook<Del_DLL_DuelComCancelCommand2>(DLL_DuelComCancelCommand2, PInvoke.GetProcAddress(lib, "DLL_DuelComCancelCommand2"));
            hookDLL_DuelDlgSetResult = new Hook<Del_DLL_DuelDlgSetResult>(DLL_DuelDlgSetResult, PInvoke.GetProcAddress(lib, "DLL_DuelDlgSetResult"));
            hookDLL_DuelListSetCardExData = new Hook<Del_DLL_DuelListSetCardExData>(DLL_DuelListSetCardExData, PInvoke.GetProcAddress(lib, "DLL_DuelListSetCardExData"));
            hookDLL_DuelListSetIndex = new Hook<Del_DLL_DuelListSetIndex>(DLL_DuelListSetIndex, PInvoke.GetProcAddress(lib, "DLL_DuelListSetIndex"));
            hookDLL_DuelListInitString = new Hook<Del_DLL_DuelListInitString>(DLL_DuelListInitString, PInvoke.GetProcAddress(lib, "DLL_DuelListInitString"));

            DLL_DuelComCheatCard = Utils.GetFunc<Del_DLL_DuelComCheatCard>(PInvoke.GetProcAddress(lib, "DLL_DuelComCheatCard"));
            DLL_DuelComDoDebugCommand = Utils.GetFunc<Del_DLL_DuelComDoDebugCommand>(PInvoke.GetProcAddress(lib, "DLL_DuelComDoDebugCommand"));
            DLL_DuelComDebugCommand = Utils.GetFunc<Del_DLL_DuelComDebugCommand>(PInvoke.GetProcAddress(lib, "DLL_DuelComDebugCommand"));

            DLL_SetAddRecordDelegate = Utils.GetFunc<Del_DLL_SetAddRecordDelegate>(PInvoke.GetProcAddress(lib, "DLL_SetAddRecordDelegate"));
        }

        static void Log(string str)
        {
            if (ClientSettings.PvpLogToConsole)
            {
                Console.WriteLine(str);
            }
            LogToFile(str);
        }

        static void LogToFile(string str, bool append = true)
        {
            if (!ClientSettings.PvpLogToFile)
            {
                return;
            }
            lock (LogLocker)
            {
                try
                {
                    string fileName = Path.Combine(Program.ClientDataDir, "DuelLog.txt");
                    string fullLog = "[" + DateTime.Now.TimeOfDay + "] " + str + (append ? "\n" : string.Empty);
                    if (append)
                    {
                        File.AppendAllText(fileName, fullLog);
                    }
                    else
                    {
                        File.WriteAllText(fileName, fullLog);
                    }
                }
                catch
                {
                }
            }
        }

        static void ResetLlmDecisionLog()
        {
            LogLlmJsonLine(string.Empty, false);
        }

        static void LogLlmDecisionWindow()
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                DecisionSnapshot snapshot;
                lock (pvpEngineState)
                {
                    int actingPlayer;
                    if (!TryGetLlmBrokerActingPlayer(out actingPlayer))
                    {
                        return;
                    }
                    snapshot = LegalActionExtractor.Extract(
                        new PvpEngineStateLegalActionQuery(pvpEngineState),
                        pvpEngineState.RunEffectSeq,
                        pvpEngineState.ViewType,
                        actingPlayer,
                        YdkLlmCardCatalog.Instance,
                        GetLlmAttackTargetContext());
                    AttachLlmViewContext(snapshot);
                    AttachLlmTurnMemory(snapshot);
                }
                // Default decision_window stays free of private self_resources.
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeDecisionWindow(snapshot));
                // Private audit only for the configured control seat (never opponent private state).
                if (LlmSelfResourcesAuditPolicy.ShouldEmitAudit(
                    ClientSettings.LlmSelfResourcesAuditEnabled,
                    ClientSettings.LlmBrokerControlPlayer,
                    snapshot))
                {
                    LogLlmJsonLine(LlmDecisionLogSerializer.SerializeSelfResourcesAudit(
                        snapshot,
                        true,
                        ClientSettings.LlmBrokerControlPlayer));
                }
                // Layer A planning search audit (default-off; seat-gated; never mutates /decide).
                TryLogLlmPlanningSearchAudit(snapshot);
            }
            catch
            {
            }
        }

        static void TryLogLlmPlanningSearchAudit(DecisionSnapshot snapshot)
        {
            if (!LlmSelfResourcesAuditPolicy.ShouldEmitAudit(
                ClientSettings.LlmPlanningSearchAuditEnabled,
                ClientSettings.LlmBrokerControlPlayer,
                snapshot))
            {
                return;
            }
            try
            {
                LlmSearchLimits limits = LlmSearchLimits.CreateFromClientSettings(
                    ClientSettings.LlmSearchMaxStrategicDepth,
                    ClientSettings.LlmSearchMaxNodes,
                    ClientSettings.LlmSearchBeamWidth,
                    ClientSettings.LlmSearchMaxWallMs,
                    ClientSettings.LlmSearchMaxSerializedBytes);
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeSearchStarted(snapshot, limits));
                LlmPlanningSearchAuditResult result = LlmPlanningSearchAudit.TryBuild(
                    snapshot,
                    snapshot.SelfResources,
                    limits);
                if (result != null && result.Success)
                {
                    LogLlmJsonLine(LlmDecisionLogSerializer.SerializeSearchCompleted(result));
                }
                else
                {
                    LogLlmJsonLine(LlmDecisionLogSerializer.SerializeSearchCompleted(result));
                    LogLlmJsonLine(LlmDecisionLogSerializer.SerializeSearchFallback(
                        snapshot,
                        result != null ? result.Error : "search_failed"));
                }
            }
            catch
            {
                try
                {
                    LogLlmJsonLine(LlmDecisionLogSerializer.SerializeSearchFallback(
                        snapshot,
                        "search_exception"));
                }
                catch
                {
                }
            }
        }

        static void LogLlmCommittedDialogResult(uint result)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeCommittedDialogResult(RunEffectSeq, result));
            }
            catch
            {
            }
        }

        static void LogLlmCommittedListIndex(int index)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeCommittedListIndex(RunEffectSeq, index));
            }
            catch
            {
            }
        }

        static void LogLlmCommittedPhase(int phase)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeCommittedPhase(RunEffectSeq, phase));
            }
            catch
            {
            }
        }

        static void LogLlmCommittedCommand(int player, int position, int index, int commandId)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeCommittedCommand(
                    RunEffectSeq, player, position, index, commandId));
            }
            catch
            {
            }
        }

        // YGOMASTER-LLM-005 Slice 5: default-off accepted-input capture (recorder only; no worker consume).
        static void TryRecordAcceptedDoCommand(int player, int position, int index, int commandId)
        {
            try
            {
                LlmAcceptedInputTranscriptRecorder.Instance.RecordDoCommand(
                    player, RunEffectSeq, player, position, index, commandId);
            }
            catch
            {
            }
        }

        static void TryRecordAcceptedMovePhase(int phase)
        {
            try
            {
                LlmAcceptedInputTranscriptRecorder.Instance.RecordMovePhase(
                    MyID, RunEffectSeq, phase);
            }
            catch
            {
            }
        }

        static void TryRecordAcceptedDialog(uint result)
        {
            try
            {
                LlmAcceptedInputTranscriptRecorder.Instance.RecordDialog(
                    MyID, RunEffectSeq, result);
            }
            catch
            {
            }
        }

        static void TryRecordAcceptedList(int index, int data)
        {
            try
            {
                LlmAcceptedInputTranscriptRecorder.Instance.RecordList(
                    MyID, RunEffectSeq, index, data);
            }
            catch
            {
            }
        }

        static void TryRecordAcceptedCancel(bool decide)
        {
            try
            {
                LlmAcceptedInputTranscriptRecorder.Instance.RecordCancel(
                    MyID, RunEffectSeq, decide);
            }
            catch
            {
            }
        }

        static void TryRecordAcceptedAutomatic(string reason, LegalAction action)
        {
            // Slice 5: do not record a separate Automatic live row on the client.
            // Automatic origin is metadata on the same Pvp-accepted input (server authority).
            // Client-local capture remains diagnostic-only and is never replay-eligible.
        }

        static void TryBeginAcceptedInputTranscriptForDuel()
        {
            try
            {
                Dictionary<string, object> settings = BuildAuthoritativeDuelSettingsSnapshot();
                // Diagnostic-only client snapshot — never authoritative / replay-eligible.
                if (settings != null && !settings.ContainsKey("source"))
                {
                    settings["source"] = "client_local_placeholder";
                }
                LlmAcceptedInputTranscriptRecorder.Instance.FlushPath =
                    ClientSettings.LlmAcceptedInputTranscriptFlushPath;
                LlmAcceptedInputTranscriptRecorder.Instance.BeginDuelFromSettings(
                    settings,
                    ClientSettings.LlmAcceptedInputTranscriptEnabled);
            }
            catch
            {
            }
        }

        static void TryFlushAcceptedInputTranscriptAtDuelEnd()
        {
            try
            {
                LlmAcceptedInputTranscriptRecorder.Instance.FlushIfConfigured();
            }
            catch
            {
            }
        }

        static Dictionary<string, object> BuildAuthoritativeDuelSettingsSnapshot()
        {
            // Best-effort from ClientWork; incomplete fields yield IncompleteSettingsReason.
            Dictionary<string, object> decks = new Dictionary<string, object>()
            {
                { "player0_main", new object[0] },
                { "player1_main", new object[0] },
                { "player0_extra", new object[0] },
                { "player1_extra", new object[0] },
            };
            Dictionary<string, object> settings = new Dictionary<string, object>()
            {
                { "seed", 0 },
                { "first_player", MyID },
                { "limited_type", 0 },
                { "decks", decks },
            };
            try
            {
                // Prefer known Duel.* JSON paths when present.
                int first = YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Duel.firstPlayer");
                settings["first_player"] = first;
            }
            catch
            {
            }
            try
            {
                int seed = YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Duel.Seed");
                if (seed != 0)
                {
                    settings["seed"] = unchecked((uint)seed);
                }
            }
            catch
            {
            }
            try
            {
                int limited = YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Duel.regulation_id");
                settings["limited_type"] = limited;
            }
            catch
            {
            }
            return settings;
        }

        static void LogLlmBrokerCommitted(
            ulong requestRunEffectSeq,
            LlmBrokerDecisionResponse response,
            LegalAction action)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeBrokerCommittedAction(
                    requestRunEffectSeq,
                    RunEffectSeq,
                    response,
                    action));
            }
            catch
            {
            }
        }

        static void LogLlmBrokerAutomaticAction(ulong runEffectSeq, string reason, LegalAction action)
        {
            // Automatic capture is recorded only after CommitLlmAction succeeds (see below).
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeBrokerAutomaticAction(
                    runEffectSeq,
                    reason,
                    action));
            }
            catch
            {
            }
        }

        static void LogLlmBrokerSkippedWindow(ulong runEffectSeq, DecisionSnapshot snapshot, string reason)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeBrokerSkippedWindow(
                    runEffectSeq,
                    snapshot,
                    reason));
            }
            catch
            {
            }
        }

        static void LogLlmBrokerUnsupportedWindow(ulong runEffectSeq, LlmDecisionWindowPlan plan)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeBrokerUnsupportedWindow(
                    runEffectSeq,
                    plan));
            }
            catch
            {
            }
        }

        static void LogLlmBrokerWindowRouted(ulong runEffectSeq, LlmDecisionWindowPlan plan)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeWindowRouted(
                    runEffectSeq,
                    plan));
            }
            catch
            {
            }
        }

        static void LogLlmBrokerRequestStarted(DecisionSnapshot snapshot)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeBrokerRequestStarted(snapshot));
            }
            catch
            {
            }
        }

        static void LogLlmBrokerResponse(ulong requestRunEffectSeq, LlmBrokerDecisionResult result)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeBrokerResponse(
                    requestRunEffectSeq,
                    RunEffectSeq,
                    result));
            }
            catch
            {
            }
        }

        static void LogLlmBrokerCommitSkipped(ulong requestRunEffectSeq, string reason)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeBrokerCommitSkipped(
                    requestRunEffectSeq,
                    RunEffectSeq,
                    reason));
            }
            catch
            {
            }
        }

        static void LogLlmBrokerRejectedAction(ulong requestRunEffectSeq, string error)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeBrokerRejectedAction(
                    requestRunEffectSeq,
                    RunEffectSeq,
                    error));
            }
            catch
            {
            }
        }

        static void LogLlmJsonLine(string json, bool append = true)
        {
            if (!ClientSettings.LlmDecisionLogEnabled)
            {
                return;
            }

            lock (LogLocker)
            {
                try
                {
                    string fileName = Path.Combine(Program.ClientDataDir, "LlmDecisionLog.jsonl");
                    if (append)
                    {
                        File.AppendAllText(fileName, json + "\n");
                    }
                    else
                    {
                        File.WriteAllText(fileName, json);
                    }
                }
                catch
                {
                }
            }
        }

        static bool TryStartLlmBrokerDecision()
        {
            if (!ClientSettings.LlmBrokerEnabled || !IsPvpDuel ||
                HasDuelEnd || HasNetworkError || SpecialFinishType != DuelFinishType.None)
            {
                return false;
            }

            DecisionSnapshot snapshot;
            LegalAction automaticAction = null;
            bool allowAutomaticMain2FollowUp;
            LlmDecisionWindowPlan decisionPlan;
            LlmBrokerRequestGateDecision? gateDecision = null;
            lock (pvpEngineState)
            {
                int actingPlayer;
                if (!TryGetLlmBrokerActingPlayer(out actingPlayer))
                {
                    return false;
                }
                if (!IsLlmBrokerControlPlayer(actingPlayer))
                {
                    return false;
                }

                snapshot = LegalActionExtractor.Extract(
                    new PvpEngineStateLegalActionQuery(pvpEngineState),
                    pvpEngineState.RunEffectSeq,
                    pvpEngineState.ViewType,
                    actingPlayer,
                    YdkLlmCardCatalog.Instance,
                    GetLlmAttackTargetContext());
                AttachLlmViewContext(snapshot);
                AttachLlmTurnMemory(snapshot);
                bool isInfoDialog = pvpEngineState.ViewType == DuelViewType.RunDialog &&
                    pvpEngineState.Param1 == 1;
                bool requiresCpuFallback =
                    LlmDecisionWindowPlanner.RequiresCpuFallback(snapshot);
                if (snapshot.LegalActions.Count == 0 &&
                    !requiresCpuFallback)
                {
                    allowAutomaticMain2FollowUp = AllowLlmAutomaticMain2FollowUp;
                    LegalActionExtractor.TryExtractAutomaticAction(
                        new PvpEngineStateLegalActionQuery(pvpEngineState),
                        pvpEngineState.ViewType,
                        actingPlayer,
                        allowAutomaticMain2FollowUp,
                        GetLlmAttackTargetContext(),
                        out automaticAction);
                }
                // Evaluate mutates gate state on StartRequest. Never evaluate for
                // info-only dialogs (they always Default and must not pin in-flight).
                // When a request is already in flight, consult the gate so same-seq
                // Suppress wins over Automatic; different-seq Fallback is applied only
                // on the strategic path inside the planner.
                bool shouldEvaluateGate = !isInfoDialog &&
                    !requiresCpuFallback &&
                    automaticAction == null &&
                    snapshot.LegalActions.Count > 0 &&
                    snapshot.IsStrategicWindow;
                if (shouldEvaluateGate ||
                    (!isInfoDialog &&
                        !requiresCpuFallback &&
                        LlmBrokerGate.IsRequestInFlight()))
                {
                    gateDecision = LlmBrokerGate.Evaluate(snapshot.RunEffectSeq);
                }
                decisionPlan = LlmDecisionWindowPlanner.Plan(
                    snapshot,
                    true,
                    actingPlayer,
                    MyID,
                    IsLlmBrokerControlPlayer(actingPlayer),
                    gateDecision,
                    isInfoDialog,
                    true,
                    automaticAction);
            }

            LogLlmBrokerWindowRouted(snapshot.RunEffectSeq, decisionPlan);

            LegalAction automaticActionToCommit =
                LlmDecisionWindowPlanner.ResolveAutomaticActionForCommit(
                    decisionPlan,
                    automaticAction);
            if (decisionPlan.Route == LlmDecisionWindowRoute.Automatic &&
                automaticActionToCommit != null)
            {
                if (!LlmAutomaticActionGuard.TryAcquire(snapshot, automaticActionToCommit))
                {
                    Log("LLM broker automatic action blocked: repeated summon placement prompt");
                    LogLlmBrokerSkippedWindow(
                        snapshot.RunEffectSeq,
                        snapshot,
                        "repeated_summon_placement");
                    return false;
                }
                try
                {
                    Log("LLM broker automatic action: " + DescribeLlmAction(automaticActionToCommit));
                    LogLlmBrokerAutomaticAction(
                        snapshot.RunEffectSeq,
                        decisionPlan.Reason ?? "automatic_action",
                        automaticActionToCommit);
                    CommitLlmAutomaticAction(
                        automaticActionToCommit,
                        decisionPlan.Reason ?? "automatic_action");
                    if (automaticActionToCommit.Kind == LegalActionKind.MovePhase &&
                        automaticActionToCommit.Phase == DuelPhase.Main2)
                    {
                        AllowLlmAutomaticMain2FollowUp = false;
                    }
                    return true;
                }
                catch (Exception e)
                {
                    Log("LLM broker automatic action failed: " + e.Message);
                    return false;
                }
            }

            LlmAutomaticActionGuard.TryAcquire(snapshot, null);

            if (decisionPlan.Route == LlmDecisionWindowRoute.Automatic)
            {
                Log("LLM broker automatic route had no action to commit");
                return false;
            }

            if (decisionPlan.Route == LlmDecisionWindowRoute.Broker)
            {
                return TryQueueLlmBrokerDecision(snapshot, gateDecision);
            }

            if (decisionPlan.Route == LlmDecisionWindowRoute.TemporaryCpu)
            {
                LogLlmBrokerSkippedWindow(snapshot.RunEffectSeq, snapshot, decisionPlan.Reason);
                if (!LlmTemporaryCpuSelection.TryBegin(snapshot.RunEffectSeq, snapshot.ActingPlayer))
                {
                    return true;
                }

                Log("LLM broker temporary CPU selection: player:" + snapshot.ActingPlayer +
                    " seq:" + snapshot.RunEffectSeq);
                Program.NetClient.Send(new DuelComSetTemporaryCpuMessage()
                {
                    RunEffectSeq = snapshot.RunEffectSeq,
                    Player = snapshot.ActingPlayer,
                });
                return true;
            }

            if (decisionPlan.Route == LlmDecisionWindowRoute.CpuFallback)
            {
                if (decisionPlan.IsUnsupportedWindow)
                {
                    LogLlmBrokerUnsupportedWindow(snapshot.RunEffectSeq, decisionPlan);
                }
                // Mechanical skips and unsupported gaps both get skipped_window for
                // continuity with pre-route telemetry; unsupported also gets its own event.
                if (decisionPlan.IsUnsupportedWindow ||
                    decisionPlan.Reason == "mechanical_only" ||
                    (snapshot != null && !snapshot.IsStrategicWindow))
                {
                    LogLlmBrokerSkippedWindow(snapshot.RunEffectSeq, snapshot, decisionPlan.Reason);
                }
                return false;
            }

            return decisionPlan.Route == LlmDecisionWindowRoute.Suppressed;
        }

        static bool TryQueueLlmBrokerDecision(
            DecisionSnapshot snapshot,
            LlmBrokerRequestGateDecision? gateDecision = null)
        {
            if (snapshot == null || snapshot.LegalActions.Count == 0)
            {
                Log("LLM broker skipped: no legal actions");
                return false;
            }
            if (!snapshot.IsStrategicWindow)
            {
                string reason = string.IsNullOrEmpty(snapshot.StrategicWindowReason) ?
                    "non_strategic_window" :
                    snapshot.StrategicWindowReason;
                Log("LLM broker skipped: " + reason);
                LogLlmBrokerSkippedWindow(snapshot.RunEffectSeq, snapshot, reason);
                return false;
            }

            AttachLlmTurnMemory(snapshot);
            LlmBrokerRequestGateDecision effectiveGateDecision = gateDecision.HasValue ?
                gateDecision.Value :
                LlmBrokerGate.Evaluate(snapshot.RunEffectSeq);
            if (effectiveGateDecision != LlmBrokerRequestGateDecision.StartRequest)
            {
                return effectiveGateDecision == LlmBrokerRequestGateDecision.SuppressForPendingRequest ||
                    effectiveGateDecision == LlmBrokerRequestGateDecision.SuppressForCompletedRequest;
            }

            int duelGeneration = GetLlmBrokerDuelGeneration();
            LogLlmBrokerRequestStarted(snapshot);
            Thread thread = new Thread(() => RunLlmBrokerDecision(snapshot, duelGeneration));
            thread.IsBackground = true;
            thread.Start();
            return true;
        }

        static void RunLlmBrokerDecision(DecisionSnapshot snapshot, int duelGeneration)
        {
            int timeoutMs = ClientSettings.LlmBrokerTimeoutMs > 0 ? ClientSettings.LlmBrokerTimeoutMs : 2000;
            LlmBrokerDecisionResult result;
            try
            {
                result = LlmBrokerClient.RequestDecision(
                    snapshot,
                    new HttpLlmBrokerTransport(ClientSettings.LlmBrokerUrl),
                    timeoutMs);
            }
            catch
            {
                result = LlmBrokerDecisionResult.Failure("broker_thread_error", null, null);
            }

            lock (ActionsToRunInNextSysAct)
            {
                ActionsToRunInNextSysAct.Add(() => TryCommitLlmBrokerDecision(
                    snapshot.RunEffectSeq, duelGeneration, result));
            }
        }

        static void TryCommitLlmBrokerDecision(
            ulong requestSeq,
            int requestDuelGeneration,
            LlmBrokerDecisionResult result)
        {
            bool shouldFinishGate = true;
            DecisionSnapshot retrySnapshot = null;
            try
            {
                if (requestDuelGeneration != GetLlmBrokerDuelGeneration())
                {
                    shouldFinishGate = false;
                    Log("LLM broker commit skipped: duel changed");
                    return;
                }
                if (!ClientSettings.LlmBrokerEnabled || !IsPvpDuel ||
                    HasDuelEnd || HasNetworkError || SpecialFinishType != DuelFinishType.None)
                {
                    LogLlmBrokerCommitSkipped(requestSeq, "duel not active");
                    return;
                }
                LogLlmBrokerResponse(requestSeq, result);
                if (result == null || !result.IsSuccess)
                {
                    LogLlmBrokerFailure(result);
                    string failureError = result == null ? "missing_result" : result.Error;
                    if (TryRecoverLlmBrokerDecision(
                        requestSeq,
                        result,
                        failureError,
                        null,
                        null))
                    {
                        return;
                    }
                    LlmBrokerGate.MarkFailed(requestSeq);
                    FallbackLlmBrokerDecisionIfNeeded();
                    return;
                }

                DecisionSnapshot currentSnapshot = null;
                string commitSkipReason = null;
                lock (pvpEngineState)
                {
                    int actingPlayer;
                    if (!TryGetLlmBrokerActingPlayer(out actingPlayer))
                    {
                        commitSkipReason = "view changed";
                    }
                    else if (!IsLlmBrokerControlPlayer(actingPlayer))
                    {
                        commitSkipReason = "control player changed";
                    }
                    else
                    {
                        currentSnapshot = LegalActionExtractor.Extract(
                            new PvpEngineStateLegalActionQuery(pvpEngineState),
                            pvpEngineState.RunEffectSeq,
                            pvpEngineState.ViewType,
                            actingPlayer,
                            YdkLlmCardCatalog.Instance,
                            GetLlmAttackTargetContext());
                        AttachLlmViewContext(currentSnapshot);
                        AttachLlmTurnMemory(currentSnapshot);
                    }
                }
                if (commitSkipReason != null)
                {
                    Log("LLM broker commit skipped: " + commitSkipReason);
                    LogLlmBrokerCommitSkipped(requestSeq, commitSkipReason);
                    if (!TryRecoverLlmBrokerDecision(
                        requestSeq,
                        result,
                        commitSkipReason,
                        null,
                        result != null ? result.Action : null))
                    {
                        LlmBrokerGate.MarkFailed(requestSeq);
                        FallbackLlmBrokerDecisionIfNeeded();
                    }
                    return;
                }

                LlmBrokerValidationResult validation = LlmBrokerProtocol.ValidateResponse(
                    currentSnapshot,
                    result.Response,
                    result.Action,
                    result.LatencyMs);
                if (!validation.IsValid)
                {
                    Log("LLM broker rejected action: " + validation.Error);
                    LogLlmBrokerRejectedAction(requestSeq, validation.Error);
                    if (validation.Error == "stale_run_effect_seq" &&
                        currentSnapshot != null &&
                        currentSnapshot.RunEffectSeq != requestSeq &&
                        currentSnapshot.LegalActions.Count > 0)
                    {
                        // Stale: retry the new seq; never recover-commit the old seq.
                        LlmBrokerGate.MarkFailed(requestSeq);
                        retrySnapshot = currentSnapshot;
                        return;
                    }

                    if (TryRecoverLlmBrokerDecision(
                        requestSeq,
                        result,
                        validation.Error,
                        currentSnapshot,
                        validation.Action != null ? validation.Action : result.Action))
                    {
                        return;
                    }

                    LlmBrokerGate.MarkFailed(requestSeq);
                    FallbackLlmBrokerDecisionIfNeeded();
                    return;
                }

                try
                {
                    CommitLlmAction(validation.Action);
                    AllowLlmAutomaticMain2FollowUp =
                        validation.Action.Kind == LegalActionKind.MovePhase &&
                        validation.Action.Phase == DuelPhase.Main2;
                    LogLlmBrokerCommitted(requestSeq, result.Response, validation.Action);
                    LlmTurnMemory.RecordCommittedAction(
                        currentSnapshot,
                        result.Response,
                        validation.Action);
                    LlmBrokerGate.MarkCompleted(requestSeq);
                }
                catch (Exception e)
                {
                    Log("LLM broker commit failed: " + e.Message);
                    LogLlmBrokerCommitSkipped(requestSeq, "commit failed: " + e.Message);
                    if (!TryRecoverLlmBrokerDecision(
                        requestSeq,
                        result,
                        "commit failed: " + e.Message,
                        currentSnapshot,
                        result != null ? result.Action : null))
                    {
                        LlmBrokerGate.MarkFailed(requestSeq);
                        FallbackLlmBrokerDecisionIfNeeded();
                    }
                    return;
                }
            }
            finally
            {
                if (shouldFinishGate)
                {
                    LlmBrokerGate.Finish(requestSeq);
                }
                if (retrySnapshot != null &&
                    requestDuelGeneration == GetLlmBrokerDuelGeneration() &&
                    ClientSettings.LlmBrokerEnabled &&
                    IsPvpDuel &&
                    !HasDuelEnd &&
                    !HasNetworkError &&
                    SpecialFinishType == DuelFinishType.None)
                {
                    TryQueueLlmBrokerDecision(retrySnapshot);
                }
            }
        }

        static int GetLlmBrokerDuelGeneration()
        {
            return Interlocked.CompareExchange(ref LlmBrokerDuelGeneration, 0, 0);
        }

        static void AttachLlmViewContext(DecisionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            LegalActionExtractor.ApplyViewContext(
                snapshot,
                new PvpEngineStateLegalActionQuery(pvpEngineState),
                pvpEngineState.Param1,
                pvpEngineState.Param2,
                pvpEngineState.Param3,
                YdkLlmCardCatalog.Instance);
        }

        static void AttachLlmTurnMemory(DecisionSnapshot snapshot)
        {
            if (snapshot != null)
            {
                snapshot.TurnMemory = LlmTurnMemory.CreateSnapshot(snapshot);
                // Attach immutable full history alongside turn_memory on every extraction
                // path (decision window, broker queue, retry, recovery). Same request keeps
                // its attached snapshot; a newly extracted retry gets then-current history.
                LlmDecisionSnapshotHistory.Attach(snapshot, LlmDuelHistory);
            }
        }

        static AttackTargetContext GetLlmAttackTargetContext()
        {
            if (PendingLlmAttackTargetContext == null)
            {
                return null;
            }

            return new AttackTargetContext()
            {
                AttackingPlayer = PendingLlmAttackTargetContext.AttackingPlayer,
                AttackerPosition = PendingLlmAttackTargetContext.AttackerPosition,
            };
        }

        static void UpdateLlmAttackTargetContextAfterCommit(LegalAction action)
        {
            if (action != null &&
                action.Kind == LegalActionKind.Command &&
                action.Command == DuelCommandType.Attack)
            {
                PendingLlmAttackTargetContext = new AttackTargetContext()
                {
                    AttackingPlayer = action.Player,
                    AttackerPosition = action.Position,
                };
                return;
            }

            PendingLlmAttackTargetContext = null;
        }

        static void ClearLlmAttackTargetContext()
        {
            PendingLlmAttackTargetContext = null;
        }

        static void AdvanceLlmBrokerDuelGeneration()
        {
            Interlocked.Increment(ref LlmBrokerDuelGeneration);
        }

        static bool IsLlmBrokerControlPlayer(int player)
        {
            return LlmBrokerControlPolicy.ShouldControlPlayer(
                ClientSettings.LlmBrokerEnabled,
                IsPvpDuel,
                ClientSettings.LlmBrokerUrl,
                ClientSettings.LlmBrokerControlPlayer,
                MyID,
                player);
        }

        static bool TryGetLlmBrokerActingPlayer(out int player)
        {
            int turnPlayer = pvpEngineState.GetValue(PvpOperationType.DLL_DuelWhichTurnNow);
            return LlmBrokerPlayerResolver.TryResolve(
                pvpEngineState.ViewType,
                pvpEngineState.DoCommandUser,
                pvpEngineState.RunDialogUser,
                pvpEngineState.Param1,
                MyID,
                turnPlayer,
                out player);
        }

        static bool HasLocalDefaultWaitInputInteraction(int actingPlayer)
        {
            try
            {
                lock (pvpEngineState)
                {
                    if (pvpEngineState.ViewType != DuelViewType.WaitInput)
                    {
                        return true;
                    }

                    PvpEngineStateLegalActionQuery query = new PvpEngineStateLegalActionQuery(pvpEngineState);
                    DecisionSnapshot snapshot = LegalActionExtractor.Extract(
                        query,
                        pvpEngineState.RunEffectSeq,
                        pvpEngineState.ViewType,
                        actingPlayer,
                        YdkLlmCardCatalog.Instance,
                        GetLlmAttackTargetContext());
                    AttachLlmViewContext(snapshot);
                    if (LlmDecisionWindowPlanner.RequiresCpuFallback(snapshot))
                    {
                        return true;
                    }
                    if (snapshot.LegalActions.Count > 0)
                    {
                        return true;
                    }

                    LegalAction automaticAction;
                    return LegalActionExtractor.TryExtractAutomaticAction(
                        query,
                        pvpEngineState.ViewType,
                        actingPlayer,
                        GetLlmAttackTargetContext(),
                        out automaticAction);
                }
            }
            catch (Exception e)
            {
                Log("LLM broker local WaitInput probe failed: " + e.Message);
                return false;
            }
        }

        static void LogLlmBrokerFailure(LlmBrokerDecisionResult result)
        {
            if (result == null)
            {
                Log("LLM broker failed: missing_result");
                return;
            }

            string message = "LLM broker failed: " + result.Error;
            if (!string.IsNullOrEmpty(result.ErrorDetail))
            {
                message += " (" + result.ErrorDetail + ")";
            }
            Log(message);
        }

        static bool TryRecoverLlmBrokerDecision(
            ulong requestSeq,
            LlmBrokerDecisionResult result,
            string error,
            DecisionSnapshot existingSnapshot,
            LegalAction rejectedAction)
        {
            if (HasDuelEnd || HasNetworkError || SpecialFinishType != DuelFinishType.None)
            {
                return false;
            }
            if (LlmBrokerRecovery.IsStaleError(error))
            {
                return false;
            }

            DecisionSnapshot snapshot = existingSnapshot;
            try
            {
                if (snapshot == null)
                {
                    lock (pvpEngineState)
                    {
                        int actingPlayer;
                        if (!TryGetLlmBrokerActingPlayer(out actingPlayer))
                        {
                            return false;
                        }
                        if (!IsLlmBrokerControlPlayer(actingPlayer))
                        {
                            return false;
                        }

                        snapshot = LegalActionExtractor.Extract(
                            new PvpEngineStateLegalActionQuery(pvpEngineState),
                            pvpEngineState.RunEffectSeq,
                            pvpEngineState.ViewType,
                            actingPlayer,
                            YdkLlmCardCatalog.Instance,
                            GetLlmAttackTargetContext());
                        AttachLlmViewContext(snapshot);
                        AttachLlmTurnMemory(snapshot);
                    }
                }
                if (snapshot.RunEffectSeq != requestSeq)
                {
                    return false;
                }

                LegalAction preferredAction = rejectedAction;
                if (preferredAction == null && result != null && result.Action != null)
                {
                    preferredAction = result.Action;
                }

                LegalAction recoveryAction;
                string policyBranch;
                if (!LlmBrokerRecovery.TrySelectAction(
                    snapshot,
                    error,
                    preferredAction,
                    out recoveryAction,
                    out policyBranch))
                {
                    return false;
                }

                CommitLlmAction(recoveryAction);
                AllowLlmAutomaticMain2FollowUp =
                    recoveryAction.Kind == LegalActionKind.MovePhase &&
                    recoveryAction.Phase == DuelPhase.Main2;
                LlmBrokerDecisionResponse recoveryResponse = result != null ? result.Response : null;
                Log("LLM broker recovery: " + (string.IsNullOrEmpty(error) ? "unknown" : error) +
                    " -> " + policyBranch + " " + DescribeLlmAction(recoveryAction));
                LogLlmBrokerRecovered(requestSeq, error, policyBranch, recoveryAction, recoveryResponse);
                // Keep normal committed events so analyzer commit coverage still counts recovery.
                LogLlmBrokerCommitted(requestSeq, recoveryResponse, recoveryAction);
                LlmTurnMemory.RecordCommittedAction(snapshot, recoveryResponse, recoveryAction);
                LlmBrokerGate.MarkCompleted(requestSeq);
                return true;
            }
            catch (Exception e)
            {
                Log("LLM broker recovery failed: " + e.Message);
                return false;
            }
        }

        static void LogLlmBrokerRecovered(
            ulong requestRunEffectSeq,
            string error,
            string policyBranch,
            LegalAction action,
            LlmBrokerDecisionResponse response)
        {
            if (!ClientSettings.LlmDecisionLogEnabled || !IsPvpDuel)
            {
                return;
            }

            try
            {
                LogLlmJsonLine(LlmDecisionLogSerializer.SerializeBrokerRecoveredAction(
                    requestRunEffectSeq,
                    RunEffectSeq,
                    error,
                    policyBranch,
                    action,
                    response));
            }
            catch
            {
            }
        }

        static void FallbackLlmBrokerDecisionIfNeeded()
        {
            if (HasDuelEnd || HasNetworkError || SpecialFinishType != DuelFinishType.None)
            {
                return;
            }

            bool hasActingPlayer = false;
            int actingPlayer = -1;
            bool brokerControlsActingPlayer = false;
            DuelViewType currentViewType = DuelViewType.Null;
            int currentParam1 = 0;
            int currentParam2 = 0;
            int currentParam3 = 0;
            lock (pvpEngineState)
            {
                hasActingPlayer = TryGetLlmBrokerActingPlayer(out actingPlayer);
                if (hasActingPlayer)
                {
                    brokerControlsActingPlayer = IsLlmBrokerControlPlayer(actingPlayer);
                    currentViewType = pvpEngineState.ViewType;
                    currentParam1 = pvpEngineState.Param1;
                    currentParam2 = pvpEngineState.Param2;
                    currentParam3 = pvpEngineState.Param3;
                }
            }

            bool isInfoDialog =
                currentViewType == DuelViewType.RunDialog &&
                currentParam1 == 1;
            LlmBrokerViewHandling fallbackHandling = LlmBrokerControlPolicy.DecideFallbackHandling(
                hasActingPlayer,
                actingPlayer,
                MyID,
                brokerControlsActingPlayer,
                isInfoDialog);
            if (fallbackHandling == LlmBrokerViewHandling.RunCpuThinking)
            {
                Log("LLM broker fallback: CpuThinking");
                ClearLlmAttackTargetContext();
                RunEffect((int)DuelViewType.CpuThinking, 0, 0, 0);
            }
            else if (fallbackHandling == LlmBrokerViewHandling.RunDefault && hasActingPlayer)
            {
                Log("LLM broker fallback: current view");
                RunEffect((int)currentViewType, currentParam1, currentParam2, currentParam3);
            }
        }

        static void CommitLlmAction(LegalAction action)
        {
            LlmActionCommitPlan plan = LlmActionCommitPlan.FromLegalAction(action);
            bool isAutomatic = action != null
                && action.IsMechanical
                && !string.IsNullOrEmpty(action.StrategicRole)
                && action.StrategicRole.IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0;
            // Prefer explicit automatic flag when present.
            if (action != null && action.IsMechanical)
            {
                // Broker automatic commits are recorded after successful native path below
                // when LogLlmBrokerAutomaticAction was paired — use ActionLabel marker.
            }
            try
            {
                switch (plan.Kind)
                {
                    case LlmActionCommitKind.MovePhase:
                        DLL_DuelComMovePhase(plan.PhaseId);
                        break;
                    case LlmActionCommitKind.Command:
                        DLL_DuelComDoCommand(plan.Player, plan.Position, plan.Index, plan.CommandId);
                        break;
                    case LlmActionCommitKind.DialogResult:
                        DLL_DuelDlgSetResult(plan.DialogResult);
                        break;
                    case LlmActionCommitKind.ListIndex:
                        DLL_DuelListSetIndex(plan.Index);
                        break;
                    case LlmActionCommitKind.Cancel:
                        DLL_DuelComCancelCommand2(plan.CancelDecide);
                        break;
                }
                UpdateLlmAttackTargetContextAfterCommit(action);
            }
            catch
            {
                TryNoteRejectedCommit("commit_exception", action);
                throw;
            }
        }

        /// <summary>
        /// Ambient commitment origin stamped onto DuelComMessage for the same accepted input.
        /// Set only around automatic commits; cleared in finally so ordinary messages stay empty.
        /// </summary>
        [ThreadStatic]
        static string llmCommitmentOriginForNextCom;

        static void CommitLlmAutomaticAction(LegalAction action, string reason)
        {
            llmCommitmentOriginForNextCom = "automatic_client_commit";
            try
            {
                CommitLlmAction(action);
                // No separate Automatic transcript row — origin travels on the real DuelCom message.
                TryRecordAcceptedAutomatic(reason, action);
            }
            catch
            {
                TryNoteRejectedCommit("automatic_commit_failed", action);
                throw;
            }
            finally
            {
                llmCommitmentOriginForNextCom = null;
            }
        }

        static string CurrentLlmCommitmentOriginOrEmpty()
        {
            return llmCommitmentOriginForNextCom ?? string.Empty;
        }

        static void TryNoteRejectedCommit(string status, LegalAction action)
        {
            try
            {
                LlmAcceptedInputTranscriptRecorder.Instance.NoteRejectedAttempt(
                    action != null ? action.Kind.ToString() : "unknown",
                    RunEffectSeq,
                    status ?? "rejected");
            }
            catch
            {
            }
        }

        static string DescribeLlmAction(LegalAction action)
        {
            if (action == null)
            {
                return "null";
            }
            switch (action.Kind)
            {
                case LegalActionKind.MovePhase:
                    return "MovePhase " + action.Phase;
                case LegalActionKind.Command:
                    return "Command " + action.Command;
                case LegalActionKind.DialogResult:
                    return "DialogResult " + action.DialogResult;
                case LegalActionKind.ListIndex:
                    return "ListIndex " + action.Index;
                case LegalActionKind.Cancel:
                    return "Cancel decide:" + action.CancelDecide;
            }
            return action.Kind.ToString();
        }

        public static void OnDuelRoomBattleReady()
        {
            lock (ActionsToRunInNextSysAct)
            {
                ActionsToRunInNextSysAct.Clear();
            }
            LlmBrokerGate.Reset();
            LlmTurnMemory.Reset();
            LlmAutomaticActionGuard.Reset();
            LlmTemporaryCpuSelection.Reset();
            AllowLlmAutomaticMain2FollowUp = false;
            ClearLlmAttackTargetContext();
            AdvanceLlmBrokerDuelGeneration();
            ResetLlmDuelHistoryForNewDuel();
        }

        public static void OnDuelBegin(GameMode gameMode)
        {
            LogToFile(string.Empty, false);
            ReplayData.Clear();
            SpecialResultType = DuelResultType.None;
            SpecialFinishType = DuelFinishType.None;
            DuelEndResult = 0;
            DuelEndFinish = 0;
            DuelEndFinishCardID = 0;
            HasNetworkError = false;
            HasDuelStart = false;
            HasDuelEnd = false;
            HasSysActFinished = false;
            BeginDuelTime = DateTime.UtcNow;
            RunEffectSeq = 0;
            IsPvpDuel = Program.NetClient != null && gameMode == GameMode.Room;
            IsPvpSpectator = Program.NetClient != null && gameMode == GameMode.Audience;
            if (IsPvpDuel)
            {
                ResetLlmDecisionLog();
            }
            LlmBrokerGate.Reset();
            LlmTurnMemory.Reset();
            LlmAutomaticActionGuard.Reset();
            LlmTemporaryCpuSelection.Reset();
            AllowLlmAutomaticMain2FollowUp = false;
            ClearLlmAttackTargetContext();
            AdvanceLlmBrokerDuelGeneration();
            ResetLlmDuelHistoryForNewDuel();
            SpectatorCount = 0;
            MyID = YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Duel.MyID");
            SendLiveRecordData = YgomSystem.Utility.ClientWork.GetByJsonPath<bool>("Duel.SendLiveRecordData");
            IsInsideDuelTimerPrepareToDuel = false;
            TryBeginAcceptedInputTranscriptForDuel();

            IsTimerEnabled = IsPvpDuel && YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Duel.TotalTimeMax") > 0;
            LastCheckTimeOver = DateTime.MinValue;
            if (IsPvpDuel)
            {
                AddTimeAtStartOfTurn = YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Duel.AddTimeAtStartOfTurn");
                AddTimeAtEndOfTurn = YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Duel.AddTimeAtEndOfTurn");
            }
            else
            {
                AddTimeAtStartOfTurn = 0;
                AddTimeAtEndOfTurn = 0;
            }

            engineInstance = IntPtr.Zero;
            engineInstanceReplayStream = IntPtr.Zero;
            activePlayerFieldEffectInstance = IntPtr.Zero;

            PvpSpectatorRapidState = PvpSpectatorRapidState.None;
            if (IsPvpSpectator)
            {
                // NOTE: Although we use "rapid" it doesn't actually do what it does in-game by default which is why we do this speed-up
                if (YgomSystem.Utility.ClientWork.GetByJsonPath<bool>("Duel.rapid") &&
                    ClientSettings.DuelClientSpectatorRapidTimeMultiplayer > 0)
                {
                    PvpSpectatorRapidState = PvpSpectatorRapidState.WaitingForSysAct;
                }
                Program.NetClient.Send(new DuelSpectatorEnterMessage());
            }

            DuelTapSync.ClearState();
        }

        public static void OnDuelEnd()
        {
            PvpSpectatorRapidState = PvpSpectatorRapidState.None;
            engineInstance = IntPtr.Zero;
            engineInstanceReplayStream = IntPtr.Zero;
            activePlayerFieldEffectInstance = IntPtr.Zero;
            DuelTapSync.ClearState();
            DuelEmoteHelper.OnEndDuel();
            TryFlushAcceptedInputTranscriptAtDuelEnd();
            // History must not survive duel end (plan reset invariant).
            ResetLlmDuelHistoryForNewDuel();
        }

        static void ClearEngineState()
        {
            lock (pvpEngineState)
            {
                pvpEngineState.Clear();
                foreach (PvpEngineState state in pvpEngineStates)
                {
                    freePvpEngineStates.Enqueue(state);
                }
                foreach (PvpEngineState state in freePvpEngineStates)
                {
                    state.Clear();
                }
            }
        }

        public static void OnInitEngineStep()
        {
            // NOTE: DuelClient.InitEngineStep is also where ReplayStream is set up. Might be useful
            if (IsPvpDuel)
            {
                Log("OnInitEngineStep");
            }
            DLL_SetAddRecordDelegate(AddRecord);
        }

        static void DLL_SetEffectDelegate(IntPtr runEffect, IntPtr isBusyEffect)
        {
            originalRunEffect = Utils.GetFunc<Del_RunEffect>(runEffect);
            originalIsBusyEffect = Utils.GetFunc<Del_IsBusyEffect>(isBusyEffect);
            hookDLL_SetEffectDelegate.Original(Marshal.GetFunctionPointerForDelegate(myRunEffect), Marshal.GetFunctionPointerForDelegate(myIsBusyEffect));
        }

        static Del_AddRecord AddRecord = (IntPtr ptr, int size) =>
        {
            for (int i = 0; i < size; i++)
            {
                ReplayData.Add(*(byte*)(ptr + i));
            }
            if (IsPvpDuel && SendLiveRecordData)
            {
                byte[] buffer = new byte[size];
                Marshal.Copy(ptr, buffer, 0, buffer.Length);
                Program.NetClient.Send(new DuelSpectatorDataMessage()
                {
                    Buffer = buffer
                });
            }
        };

        static void SetGuideEnable(IntPtr thisPtr, bool near, bool enable, bool turnchange)
        {
            if (IsPvpDuel)
            {
                //Log("SetGuideEnable:" + thisPtr + " near:" + near + " enable:" + enable + " turnchange:" + turnchange);
            }
            activePlayerFieldEffectInstance = thisPtr;
            LastFieldGuideUpdate = DateTime.UtcNow;
            bool nearEnable = (near && enable) || (!near && !enable);
            IsFieldGuideNearReal = nearEnable;
            if (IsPvpDuel && SendLiveRecordData && near && IsFieldGuideNear != nearEnable)
            {
                IsFieldGuideNear = nearEnable;
                Program.NetClient.Send(new DuelSpectatorFieldGuideMessage()
                {
                    Near = nearEnable
                });
            }
            hookSetGuideEnable.Original(thisPtr, near, enable, turnchange);
        }

        static IntPtr GetReplayStream()
        {
            if (engineInstance == IntPtr.Zero)
            {
                engineInstance = fieldEngineInstance.GetValue().ptr;
            }
            if (engineInstance != IntPtr.Zero && engineInstanceReplayStream == IntPtr.Zero)
            {
                engineInstanceReplayStream = fieldEngineReplayStream.GetValue(engineInstance).ptr;
            }
            return engineInstanceReplayStream;
        }

        static void UpdateFieldGuide()
        {
            LastFieldGuideUpdate = DateTime.UtcNow;
            if (activePlayerFieldEffectInstance != IntPtr.Zero)
            {
                int team = IsFieldGuideNear ? 0 : 1;
                bool forceswitch = true;
                methodSwitchGuide.Invoke(activePlayerFieldEffectInstance, new IntPtr[] { new IntPtr(&team), new IntPtr(&forceswitch) });
            }
        }

        static void EndSpectatorReplayStream()
        {
            IntPtr replayStream = GetReplayStream();
            if (replayStream != IntPtr.Zero)
            {
                methodReplayStreamFinish.Invoke(engineInstanceReplayStream);
            }
        }

        static void UpdateSpectatorCount(int num)
        {
            SpectatorCount = num;
            Action action = () =>
            {
                YgomGame.Duel.DuelHUD.OnChangeWatcherNum(SpectatorCount);
            };
            TradeUtils.AddAction(action);
        }

        static int InjectDuelEnd()
        {
            DuelTapSync.ClearState();
            DuelEmoteHelper.OnEndDuel();
            HasDuelEnd = true;
            if (IsPvpSpectator)
            {
                EndSpectatorReplayStream();
            }
            if (SpecialFinishType != DuelFinishType.None)
            {
                return originalRunEffect((int)DuelViewType.DuelEnd, (int)SpecialResultType, (int)SpecialFinishType, 0);
            }
            else if (HasNetworkError)
            {
                return originalRunEffect((int)DuelViewType.DuelEnd, (int)DuelResultType.Draw, (int)DuelFinishType.FinishError, 0);
            }
            return 0;
        }

        static int RunEffect(int id, int param1, int param2, int param3)
        {
            if (IsPvpDuel || IsPvpSpectator)
            {
                DuelEmoteHelper.OnRunEffect((DuelViewType)id, param1, param2, param3);
            }
            if (IsPvpSpectator)
            {
                if (HasNetworkError)
                {
                    if (HasDuelStart)
                    {
                        EndSpectatorReplayStream();
                        return InjectDuelEnd();
                    }
                    else
                    {
                        if ((DuelViewType)id == DuelViewType.DuelStart)
                        {
                            HasDuelStart = true;
                        }
                        originalRunEffect(id, param1, param2, param3);
                        EndSpectatorReplayStream();
                        return InjectDuelEnd();
                    }
                }

                switch ((DuelViewType)id)
                {
                    case DuelViewType.DuelStart:
                        UpdateSpectatorCount(SpectatorCount);
                        HasDuelStart = true;
                        break;
                    case DuelViewType.DuelEnd:
                        DuelTapSync.ClearState();
                        DuelEmoteHelper.OnEndDuel();
                        HasDuelEnd = true;
                        EndSpectatorReplayStream();
                        break;
                }
            }
            return originalRunEffect(id, param1, param2, param3);
        }

        static int IsBusyEffect(int id)
        {
            if (HasNetworkError || SpecialFinishType != DuelFinishType.None)
            {
                return originalIsBusyEffect(id);
            }
            return originalIsBusyEffect(id);
        }

        public static int DLL_DuelSysAct()
        {
            if (IsPvpDuel || IsPvpSpectator)
            {
                if (LastSysActLogTime < DateTime.UtcNow - TimeSpan.FromSeconds(3))
                {
                    LastSysActLogTime = DateTime.UtcNow;
                    LogToFile("DLL_DuelSysAct");
                }

                if (IsTimerEnabled && SpecialFinishType == DuelFinishType.None && LastCheckTimeOver < DateTime.UtcNow - TimeSpan.FromSeconds(1))
                {
                    LastCheckTimeOver = DateTime.UtcNow;
                    if (YgomGame.Duel.DuelTimer3D.IsPlayerTimeOver)
                    {
                        SpecialResultType = DuelResultType.Lose;
                        SpecialFinishType = DuelFinishType.TimeOut;
                        InjectDuelEnd();
                    }
                }

                if (IsPvpSpectator)
                {
                    if (PvpSpectatorRapidState == PvpSpectatorRapidState.WaitingForSysAct)
                    {
                        PvpSpectatorRapidState = PvpSpectatorRapidState.Active;
                        PInvoke.SetTimeMultiplier(ClientSettings.DuelClientSpectatorRapidTimeMultiplayer);
                    }
                    if (PvpSpectatorRapidState == PvpSpectatorRapidState.Active && YgomGame.Duel.DuelClient.ReplayRealtime)
                    {
                        PvpSpectatorRapidState = PvpSpectatorRapidState.Finished;
                        PInvoke.SetTimeMultiplier(ClientSettings.DuelClientTimeMultiplier != 0 ?
                            ClientSettings.DuelClientTimeMultiplier : ClientSettings.TimeMultiplier);
                    }

                    // Hacky fix for field guide being on the wrong side when spectating a duel
                    if (IsFieldGuideNearReal != IsFieldGuideNear && LastFieldGuideUpdate < DateTime.UtcNow - TimeSpan.FromSeconds(1))
                    {
                        UpdateFieldGuide();
                    }
                }

                if (IsPvpDuel)
                {
                    lock (pvpEngineStates)
                    {
                        if (HasSysActFinished && pvpEngineStates.Count == 0)
                        {
                            return 1;
                        }
                    }

                    PvpEngineState stateUpdate = null;
                    lock (pvpEngineState)
                    {
                        if (pvpEngineState.IsBusyEffect.Count > 0)
                        {
                            HashSet<DuelViewType> changedStates = null;
                            foreach (KeyValuePair<DuelViewType, int> busyState in pvpEngineState.IsBusyEffect)
                            {
                                if (originalIsBusyEffect((int)busyState.Key) == 0)
                                {
                                    if (changedStates == null)
                                    {
                                        changedStates = new HashSet<DuelViewType>();
                                    }
                                    //Log("Send DuelIsBusyEffectMessage " + busyState.Key + " " + pvpEngineState.RunEffectSeq);
                                    changedStates.Add(busyState.Key);
                                    Program.NetClient.Send(new DuelIsBusyEffectMessage()
                                    {
                                        RunEffectSeq = pvpEngineState.RunEffectSeq,
                                        ViewType = busyState.Key
                                    });
                                }
                            }
                            if (changedStates != null)
                            {
                                foreach (DuelViewType viewType in changedStates)
                                {
                                    pvpEngineState.IsBusyEffect.Remove(viewType);
                                }
                            }
                        }
                        else if (pvpEngineStates.Count > 0)
                        {
                            stateUpdate = pvpEngineStates.Dequeue();
                            if (stateUpdate != null)
                            {
                                pvpEngineState.Update(stateUpdate);
                                RunEffectSeq = pvpEngineState.RunEffectSeq;
                            }
                        }
                    }

                    if (stateUpdate != null)
                    {
                        if (LlmTemporaryCpuSelection.ShouldRestore(
                            pvpEngineState.ViewType,
                            pvpEngineState.Param1))
                        {
                            Log("LLM broker temporary CPU selection completed: view:" +
                                pvpEngineState.ViewType + " seq:" + pvpEngineState.RunEffectSeq);
                            LlmTemporaryCpuSelection.MarkRestored();
                        }
                        switch (pvpEngineState.ViewType)
                        {
                            case DuelViewType.DuelStart:
                                UpdateSpectatorCount(SpectatorCount);
                                HasDuelStart = true;
                                goto default;
                            case DuelViewType.DuelEnd:
                                DuelTapSync.ClearState();
                                DuelEmoteHelper.OnEndDuel();
                                HasDuelEnd = true;
                                if (MyID != 0)
                                {
                                    switch ((DuelResultType)pvpEngineState.Param1)
                                    {
                                        case DuelResultType.Win:
                                            pvpEngineState.Param1 = (int)DuelResultType.Lose;
                                            break;
                                        case DuelResultType.Lose:
                                            pvpEngineState.Param1 = (int)DuelResultType.Win;
                                            break;
                                    }
                                }
                                // We do this because the DLL_XXXX functions don't seem to be working correctly
                                DuelEndResult = pvpEngineState.Param1;
                                DuelEndFinish = pvpEngineState.Param2;
                                DuelEndFinishCardID = pvpEngineState.Param3;
                                goto default;
                            case DuelViewType.TurnChange:
                                if (pvpEngineState.Param1 == MyID && AddTimeAtStartOfTurn > 0)
                                {
                                    TradeUtils.AddAction(() =>
                                    {
                                        YgomGame.Duel.DuelTimer3D.AddTurnTime(AddTimeAtStartOfTurn, AddTimeAtStartOfTurn + AddTimeAtEndOfTurn);
                                    });
                                }
                                else if (pvpEngineState.Param1 == RivalID && AddTimeAtEndOfTurn > 0)
                                {
                                    TradeUtils.AddAction(() =>
                                    {
                                        YgomGame.Duel.DuelTimer3D.AddTurnTime(AddTimeAtEndOfTurn, AddTimeAtStartOfTurn + AddTimeAtEndOfTurn);
                                    });
                                }
                                goto default;
                            case DuelViewType.WaitInput:
                                LogLlmDecisionWindow();
                                if (LlmTemporaryCpuSelection.ShouldSuppressDecisionView(
                                    pvpEngineState.ViewType,
                                    pvpEngineState.Param1))
                                {
                                    Log("LLM broker temporary CPU owns WaitInput seq:" +
                                        pvpEngineState.RunEffectSeq);
                                    break;
                                }
                                int waitInputPlayer;
                                bool hasWaitInputPlayer = TryGetLlmBrokerActingPlayer(out waitInputPlayer);
                                bool llmBrokerControlsCurrentPlayer = hasWaitInputPlayer &&
                                    IsLlmBrokerControlPlayer(waitInputPlayer);
                                bool llmBrokerRequestStartedOrPending = TryStartLlmBrokerDecision();
                                bool hasLocalDefaultInteraction =
                                    !hasWaitInputPlayer ||
                                    waitInputPlayer != MyID ||
                                    HasLocalDefaultWaitInputInteraction(waitInputPlayer);
                                LlmBrokerViewHandling waitInputHandling = LlmBrokerControlPolicy.DecideViewHandling(
                                    hasWaitInputPlayer,
                                    waitInputPlayer,
                                    MyID,
                                    llmBrokerControlsCurrentPlayer,
                                    llmBrokerRequestStartedOrPending,
                                    false,
                                    hasLocalDefaultInteraction);
                                if (waitInputHandling == LlmBrokerViewHandling.RunDefault)
                                {
                                    // Native default: original RunEffect for this WaitInput.
                                    // Used for uncontrolled local seats and controlled empty
                                    // WaitInput (no legal/automatic action) — never CpuThinking.
                                    Log("LLM broker WaitInput handling: NativeDefault player:" +
                                        waitInputPlayer +
                                        " my_id:" + MyID +
                                        " broker_controls:" + llmBrokerControlsCurrentPlayer +
                                        " request_started_or_pending:" + llmBrokerRequestStartedOrPending +
                                        " has_local_interaction:" + hasLocalDefaultInteraction +
                                        " param1:" + pvpEngineState.Param1 +
                                        " param2:" + pvpEngineState.Param2 +
                                        " param3:" + pvpEngineState.Param3 +
                                        " reason:" +
                                        (!hasLocalDefaultInteraction && llmBrokerControlsCurrentPlayer ?
                                            "empty_controlled_waitinput" :
                                            "run_default"));
                                    goto default;
                                }
                                if (waitInputHandling == LlmBrokerViewHandling.RunCpuThinking)
                                {
                                    Log("LLM broker WaitInput handling: CpuThinking player:" +
                                        waitInputPlayer +
                                        " my_id:" + MyID +
                                        " broker_controls:" + llmBrokerControlsCurrentPlayer +
                                        " request_started_or_pending:" + llmBrokerRequestStartedOrPending +
                                        " has_local_interaction:" + hasLocalDefaultInteraction);
                                    ClearLlmAttackTargetContext();
                                    RunEffect((int)DuelViewType.CpuThinking, 0, 0, 0);
                                }
                                else if (waitInputHandling == LlmBrokerViewHandling.SuppressForBroker)
                                {
                                    Log("LLM broker WaitInput handling: SuppressForBroker player:" +
                                        waitInputPlayer +
                                        " request_started_or_pending:" + llmBrokerRequestStartedOrPending);
                                }
                                break;
                            case DuelViewType.RunDialog:
                                LogLlmDecisionWindow();
                                if (LlmTemporaryCpuSelection.ShouldSuppressDecisionView(
                                    pvpEngineState.ViewType,
                                    pvpEngineState.Param1))
                                {
                                    Log("LLM broker temporary CPU owns RunDialog seq:" +
                                        pvpEngineState.RunEffectSeq);
                                    break;
                                }
                                if (LegalActionExtractor.IsDialogWithoutChoice(
                                    new PvpEngineStateLegalActionQuery(pvpEngineState),
                                    pvpEngineState.Param1))
                                {
                                    Log("LLM broker empty RunDialog: native default" +
                                        " param1:" + pvpEngineState.Param1 +
                                        " param2:" + pvpEngineState.Param2 +
                                        " param3:" + pvpEngineState.Param3);
                                    RunEffect(
                                        (int)pvpEngineState.ViewType,
                                        pvpEngineState.Param1,
                                        pvpEngineState.Param2,
                                        pvpEngineState.Param3);
                                    break;
                                }
                                // 1 = YgomGame.Duel.Engine.DialogType.Info
                                int dialogPlayer;
                                bool hasDialogPlayer = TryGetLlmBrokerActingPlayer(out dialogPlayer);
                                bool llmBrokerControlsDialogPlayer = hasDialogPlayer &&
                                    IsLlmBrokerControlPlayer(dialogPlayer);
                                bool llmBrokerDialogRequestStartedOrPending = TryStartLlmBrokerDecision();
                                LlmBrokerViewHandling dialogHandling = LlmBrokerControlPolicy.DecideViewHandling(
                                    hasDialogPlayer,
                                    dialogPlayer,
                                    MyID,
                                    llmBrokerControlsDialogPlayer,
                                    llmBrokerDialogRequestStartedOrPending,
                                    pvpEngineState.Param1 == 1);
                                if (dialogHandling == LlmBrokerViewHandling.RunDefault)
                                {
                                    goto default;
                                }
                                if (dialogHandling == LlmBrokerViewHandling.RunCpuThinking)
                                {
                                    RunEffect((int)DuelViewType.CpuThinking, 0, 0, 0);
                                }
                                break;
                            case DuelViewType.RunList:
                                LogLlmDecisionWindow();
                                if (LlmTemporaryCpuSelection.ShouldSuppressDecisionView(
                                    pvpEngineState.ViewType,
                                    pvpEngineState.Param1))
                                {
                                    Log("LLM broker temporary CPU owns RunList seq:" +
                                        pvpEngineState.RunEffectSeq);
                                    break;
                                }
                                int listPlayer;
                                bool hasListPlayer = TryGetLlmBrokerActingPlayer(out listPlayer);
                                bool llmBrokerControlsListPlayer = hasListPlayer &&
                                    IsLlmBrokerControlPlayer(listPlayer);
                                bool llmBrokerListRequestStartedOrPending = TryStartLlmBrokerDecision();
                                LlmBrokerViewHandling listHandling = LlmBrokerControlPolicy.DecideViewHandling(
                                    hasListPlayer,
                                    listPlayer,
                                    MyID,
                                    llmBrokerControlsListPlayer,
                                    llmBrokerListRequestStartedOrPending,
                                    false);
                                if (listHandling == LlmBrokerViewHandling.RunDefault)
                                {
                                    goto default;
                                }
                                if (listHandling == LlmBrokerViewHandling.RunCpuThinking)
                                {
                                    RunEffect((int)DuelViewType.CpuThinking, 0, 0, 0);
                                }
                                break;
                            default:
                                RunEffect((int)pvpEngineState.ViewType, pvpEngineState.Param1, pvpEngineState.Param2, pvpEngineState.Param3);
                                break;
                        }
                        stateUpdate.Clear();
                        lock (pvpEngineState)
                        {
                            freePvpEngineStates.Enqueue(stateUpdate);
                        }
                    }
                }

                if (ActionsToRunInNextSysAct.Count > 0)
                {
                    List<Action> actionsToRun;
                    lock (ActionsToRunInNextSysAct)
                    {
                        actionsToRun = new List<Action>(ActionsToRunInNextSysAct);
                        ActionsToRunInNextSysAct.Clear();
                    }
                    foreach (Action action in actionsToRun)
                    {
                        action();
                    }
                }

                if ((HasNetworkError || SpecialFinishType != DuelFinishType.None) && HasDuelStart)
                {
                    return 1;
                }

                if (IsPvpDuel)
                {
                    return 0;
                }
            }
            return hookDLL_DuelSysAct.Original();
        }

        static void DLL_DuelComMovePhase(int phase)
        {
            if (IsPvpDuel)
            {
                Log("DLL_DuelComMovePhase phase:" + phase + " seq:" + RunEffectSeq);
                LogLlmCommittedPhase(phase);
                Program.NetClient.Send(new DuelComMovePhaseMessage()
                {
                    RunEffectSeq = RunEffectSeq,
                    Phase = phase,
                    CommitmentOrigin = CurrentLlmCommitmentOriginOrEmpty()
                });
            }
            // Native first, then record (void original).
            hookDLL_DuelComMovePhase.Original(phase);
            if (IsPvpDuel)
            {
                TryRecordAcceptedMovePhase(phase);
            }
        }

        public static void DLL_DuelComDoCommand(int player, int position, int index, int commandId)
        {
            if (IsPvpDuel)
            {
                Log("DLL_DuelComDoCommand player:" + player + " pos:" + position + " indx:" + index + " cmd:" + commandId + " seq:" + RunEffectSeq);
                LogLlmCommittedCommand(player, position, index, commandId);
                Program.NetClient.Send(new DuelComDoCommandMessage()
                {
                    RunEffectSeq = RunEffectSeq,
                    Player = player,
                    Position = position,
                    Index = index,
                    CommandId = commandId,
                    CommitmentOrigin = CurrentLlmCommitmentOriginOrEmpty()
                });
            }
            hookDLL_DuelComDoCommand.Original(player, position, index, commandId);
            if (IsPvpDuel)
            {
                TryRecordAcceptedDoCommand(player, position, index, commandId);
            }
        }

        static int DLL_DuelComCancelCommand()
        {
            if (IsPvpDuel)
            {
                Log("DLL_DuelComCancelCommand seq:" + RunEffectSeq);
                Program.NetClient.Send(new DuelComCancelCommandMessage()
                {
                    RunEffectSeq = RunEffectSeq,
                    CommitmentOrigin = CurrentLlmCommitmentOriginOrEmpty()
                });
            }
            int ret = hookDLL_DuelComCancelCommand.Original();
            if (IsPvpDuel)
            {
                TryRecordAcceptedCancel(false);
            }
            return ret;
        }

        static int DLL_DuelComCancelCommand2(bool decide)
        {
            if (IsPvpDuel)
            {
                Log("DLL_DuelComCancelCommand2 decide:" + decide + " seq:" + RunEffectSeq);
                Program.NetClient.Send(new DuelComCancelCommand2Message()
                {
                    RunEffectSeq = RunEffectSeq,
                    Decide = decide,
                    CommitmentOrigin = CurrentLlmCommitmentOriginOrEmpty()
                });
            }
            int ret = hookDLL_DuelComCancelCommand2.Original(decide);
            if (IsPvpDuel)
            {
                TryRecordAcceptedCancel(decide);
            }
            return ret;
        }

        static void DLL_DuelDlgSetResult(uint result)
        {
            if (IsPvpDuel)
            {
                Log("DLL_DuelDlgSetResult result:" + result + " seq:" + RunEffectSeq);
                LogLlmCommittedDialogResult(result);
                Program.NetClient.Send(new DuelDlgSetResultMessage()
                {
                    RunEffectSeq = RunEffectSeq,
                    Result = result,
                    CommitmentOrigin = CurrentLlmCommitmentOriginOrEmpty()
                });
            }
            hookDLL_DuelDlgSetResult.Original(result);
            if (IsPvpDuel)
            {
                TryRecordAcceptedDialog(result);
            }
        }

        static void DLL_DuelListSetCardExData(int index, int data)
        {
            if (IsPvpDuel)
            {
                Log("DLL_DuelListSetCardExData index:" + index + " data:" + data + " seq:" + RunEffectSeq);
                Program.NetClient.Send(new DuelListSetCardExDataMessage()
                {
                    RunEffectSeq = RunEffectSeq,
                    Index = index,
                    Data = data,
                    CommitmentOrigin = CurrentLlmCommitmentOriginOrEmpty()
                });
            }
            hookDLL_DuelListSetCardExData.Original(index, data);
            if (IsPvpDuel)
            {
                TryRecordAcceptedList(index, data);
            }
        }

        static void DLL_DuelListSetIndex(int index)
        {
            if (IsPvpDuel)
            {
                Log("DLL_DuelListSetIndex index:" + index + "seq:" + RunEffectSeq);
                LogLlmCommittedListIndex(index);
                Program.NetClient.Send(new DuelListSetIndexMessage()
                {
                    RunEffectSeq = RunEffectSeq,
                    Index = index,
                    CommitmentOrigin = CurrentLlmCommitmentOriginOrEmpty()
                });
            }
            hookDLL_DuelListSetIndex.Original(index);
            if (IsPvpDuel)
            {
                TryRecordAcceptedList(index, 0);
            }
        }

        static void DLL_DuelListInitString()
        {
            if (IsPvpDuel)
            {
                Log("DLL_DuelListInitString seq:" + RunEffectSeq);
                Program.NetClient.Send(new DuelListInitStringMessage()
                {
                    RunEffectSeq = RunEffectSeq,
                    CommitmentOrigin = CurrentLlmCommitmentOriginOrEmpty()
                });
            }
            hookDLL_DuelListInitString.Original();
        }

        public static void HandleNetMessage(NetClient client, NetMessage message)
        {
            switch (message.Type)
            {
                case NetMessageType.ConnectionResponse: OnConnectionResponse((ConnectionResponseMessage)message); break;
                case NetMessageType.Ping: OnPing((PingMessage)message); break;
                case NetMessageType.DuelError: OnDuelError((DuelErrorMessage)message); break;
                case NetMessageType.OpponentDuelEnded: OnOpponentDuelEnded((OpponentDuelEndedMessage)message); break;
                case NetMessageType.DuelSpectatorData: OnDuelSpectatorData((DuelSpectatorDataMessage)message); break;
                case NetMessageType.DuelSpectatorFieldGuide: OnDuelSpectatorFieldGuide((DuelSpectatorFieldGuideMessage)message); break;
                case NetMessageType.DuelSpectatorCount: OnDuelSpectatorCount((DuelSpectatorCountMessage)message); break;
                case NetMessageType.DuelTapSync: DuelTapSync.OnDuelTapSync((DuelTapSyncMessage)message); break;
                case NetMessageType.DuelEmote: DuelEmoteHelper.OnDuelEmote((DuelEmoteMessage)message); break;
                case NetMessageType.DuelEngineState: OnDuelEngineState((DuelEngineStateMessage)message); break;
                case NetMessageType.DuelIsBusyEffect: OnDuelIsBusyEffect((DuelIsBusyEffectMessage)message); break;
                case NetMessageType.DuelSysActFinished: OnDuelSysActFinished((DuelSysActFinishedMessage)message); break;
                case NetMessageType.DuelPublicActionEvent: OnDuelPublicActionEvent((DuelPublicActionEventMessage)message); break;
                case NetMessageType.DuelRawViewEvidence: OnDuelRawViewEvidence((DuelRawViewEvidenceMessage)message); break;
                case NetMessageType.DuelFaceProbeEvidence: OnDuelFaceProbeEvidence((DuelFaceProbeEvidenceMessage)message); break;
            }
        }

        static void ResetLlmDuelHistoryForNewDuel()
        {
            LlmDuelHistoryGeneration = LlmDuelHistoryLifecycle.ResetForBoundary(
                LlmDuelHistory,
                LlmDuelHistoryGeneration);
        }

        static void EnsureLlmPublicHistoryNameResolver()
        {
            if (LlmPublicDuelEventClientIngest.DefaultNameResolver != null)
            {
                return;
            }
            LlmPublicDuelEventClientIngest.DefaultNameResolver = cardId =>
                LlmPublicHistoryCardNameEnrichment.ResolveFromCatalog(
                    cardId,
                    YdkLlmCardCatalog.Instance);
        }

        static void OnDuelPublicActionEvent(DuelPublicActionEventMessage message)
        {
            if (message == null)
            {
                return;
            }

            // Authoritative history only: never append from local prediction / Original hooks.
            EnsureLlmPublicHistoryNameResolver();
            LlmPublicDuelEvent publicEvent = message.ToEvent();
            string error;
            string auditJson;
            bool shouldAudit = LlmPublicDuelEventClientIngest.TryIngestForAudit(
                LlmDuelHistory,
                publicEvent,
                out error,
                out auditJson);
            if (shouldAudit && ClientSettings.LlmDecisionLogEnabled && IsPvpDuel)
            {
                try
                {
                    LogLlmJsonLine(auditJson);
                }
                catch
                {
                }
            }
        }

        static void OnDuelRawViewEvidence(DuelRawViewEvidenceMessage message)
        {
            if (message == null)
            {
                return;
            }

            // Audit-only: never enter broker history / request projection.
            LlmRawDuelViewEvidence evidence = message.ToEvidence();
            if (!LlmDuelHistory.TryAcceptRawViewEvidence(evidence))
            {
                return;
            }
            if (ClientSettings.LlmDecisionLogEnabled && IsPvpDuel)
            {
                try
                {
                    LogLlmJsonLine(MiniJSON.Json.Serialize(evidence.ToAuditDictionary()));
                }
                catch
                {
                }
            }
        }

        static void OnDuelFaceProbeEvidence(DuelFaceProbeEvidenceMessage message)
        {
            if (message == null)
            {
                return;
            }

            // Audit-only identity-free face probes for live face validation.
            // Never enter duel_history / request projection; dedupe by probe_id.
            LlmFaceProbeEvidence probe = message.ToProbe();
            string auditJson;
            if (!LlmPublicDuelEventClientIngest.TryIngestFaceProbeForAudit(
                LlmDuelHistory, probe, out auditJson))
            {
                return;
            }
            if (ClientSettings.LlmDecisionLogEnabled && IsPvpDuel)
            {
                try
                {
                    LogLlmJsonLine(auditJson);
                }
                catch
                {
                }
            }
        }

        static void OnConnectionResponse(ConnectionResponseMessage message)
        {
            if (!message.Success)
            {
                Log("Session server failed to validate token '" + ClientSettings.MultiplayerToken + "'");
            }
        }

        static void OnNetworkError()
        {
            try
            {
                // try/catch as we aren't in the main thread
                if (HasNetworkError || SpecialFinishType != DuelFinishType.None || HasDuelEnd/* ||
                    (YgomGame.Duel.DuelClient.Step != DuelClientStep.ExecDuel && !IsPvpSpectator)*/)
                {
                    return;
                }
            }
            catch
            {
            }

            lock (ActionsToRunInNextSysAct)
            {
                ActionsToRunInNextSysAct.Add(() =>
                {
                    if (HasNetworkError || SpecialFinishType != DuelFinishType.None || HasDuelEnd/* ||
                        (YgomGame.Duel.DuelClient.Step != DuelClientStep.ExecDuel && !IsPvpSpectator)*/)
                    {
                        return;
                    }
                    HasNetworkError = true;
                    Log("OnNetworkError");
                    // History must not survive disconnect/network recovery into a new duel generation.
                    ResetLlmDuelHistoryForNewDuel();
                    if (/*IsPvpSpectator && */!HasDuelStart)
                    {
                        // NOTE: This is really hacky and looks weird but the client can get stuck without this
                        originalRunEffect((int)DuelViewType.DuelStart, 0, 0, 0);
                        HasDuelStart = true;
                    }
                    if (HasDuelStart)
                    {
                        InjectDuelEnd();
                    }
                    if (IsPvpDuel)
                    {
                        Program.NetClient.Send(new DuelErrorMessage());
                    }
                });
            }
        }

        static void OnPing(PingMessage message)
        {
            // TODO: Send some info stating if we're in a duel
            Program.NetClient.Send(new PongMessage()
            {
                ServerToClientLatency = Utils.GetEpochTime() - Utils.GetEpochTime(message.RequestTime),
                ResponseTime = DateTime.UtcNow,
            });

            if (message.DuelingState != DuelRoomTableState.Dueling && IsPvpDuel &&
                !HasNetworkError && SpecialFinishType == DuelFinishType.None && !HasDuelEnd &&
                BeginDuelTime < DateTime.UtcNow - TimeSpan.FromSeconds(5) &&
                YgomGame.Duel.DuelClient.Instance != IntPtr.Zero)
            {
                OnNetworkError();
            }
        }

        static void OnDuelError(DuelErrorMessage message)
        {
            if (IsPvpDuel || IsPvpSpectator)
            {
                OnNetworkError();
            }
        }

        static void OnOpponentDuelEnded(OpponentDuelEndedMessage message)
        {
            if (!IsPvpDuel)
            {
                return;
            }
            Action action = () =>
            {
                if (message.Result == DuelResultType.Lose && SpecialFinishType == DuelFinishType.None)
                {
                    switch (message.Finish)
                    {
                        case DuelFinishType.TimeOut:
                        case DuelFinishType.Surrender:
                            SpecialResultType = DuelResultType.Win;
                            SpecialFinishType = message.Finish;
                            InjectDuelEnd();
                            break;
                    }
                }
                HasDuelEnd = true;
                ResetLlmDuelHistoryForNewDuel();
            };
            lock (ActionsToRunInNextSysAct)
            {
                ActionsToRunInNextSysAct.Add(action);
            }
        }

        static void OnDuelSpectatorData(DuelSpectatorDataMessage message)
        {
            Action action = () =>
            {
                if (message.Buffer != null && message.Buffer.Length > 0)
                {
                    if (IsPvpDuel)
                    {
                        if (message.IsFirstData)
                        {
                            ReplayData.Clear();
                        }
                        ReplayData.AddRange(message.Buffer);
                    }
                    else if (IsPvpSpectator)
                    {
                        IntPtr replayStream = GetReplayStream();
                        if (replayStream != IntPtr.Zero)
                        {
                            IL2Array<byte> buffer = new IL2Array<byte>(message.Buffer.Length, IL2SystemClass.Byte);
                            buffer.CopyFrom(message.Buffer);
                            methodReplayStreamAdd.Invoke(replayStream, new IntPtr[] { buffer.ptr });
                        }
                    }
                }
            };
            lock (ActionsToRunInNextSysAct)
            {
                ActionsToRunInNextSysAct.Add(action);
            }
        }

        static void OnDuelSpectatorFieldGuide(DuelSpectatorFieldGuideMessage message)
        {
            TradeUtils.AddAction(() =>
            {
                IsFieldGuideNear = message.Near;
                UpdateFieldGuide();
            });
        }

        static void OnDuelSpectatorCount(DuelSpectatorCountMessage message)
        {
            UpdateSpectatorCount(message.Count);
        }

        static void OnDuelEngineState(DuelEngineStateMessage message)
        {
            Log("OnDuelEngineState " + message.RunEffectSeq + " " + message.ViewType);
            if (message.RunEffectSeq == 1)
            {
                ClearEngineState();
            }
            PvpEngineState state;
            lock (pvpEngineState)
            {
                if (freePvpEngineStates.Count > 0)
                {
                    state = freePvpEngineStates.Dequeue();
                    state.Clear();
                }
                else
                {
                    state = new PvpEngineState();
                }
            }
            state.RunEffectSeq = message.RunEffectSeq;
            state.ViewType = message.ViewType;
            state.Param1 = message.Param1;
            state.Param2 = message.Param2;
            state.Param3 = message.Param3;
            state.DoCommandUser = message.DoCommandUser;
            state.RunDialogUser = message.RunDialogUser;
            state.Read(message.CompressedBuffer);
            lock (pvpEngineState)
            {
                pvpEngineStates.Enqueue(state);
            }
        }

        static void OnDuelIsBusyEffect(DuelIsBusyEffectMessage message)
        {
            lock (pvpEngineState)
            {
                PvpEngineState state;
                if (pvpEngineState.RunEffectSeq == message.RunEffectSeq)
                {
                    state = pvpEngineState;
                }
                else
                {
                    state = pvpEngineStates.FirstOrDefault(x => x.RunEffectSeq == message.RunEffectSeq);
                }
                if (state != null)
                {
                    Log("OnDuelIsBusyEffect " + message.RunEffectSeq + " " + message.ViewType);
                    state.IsBusyEffect[message.ViewType] = 0;
                }
                else
                {
                    Utils.LogWarning("Failed to find state for IsBusyEffect seq " + message.RunEffectSeq + " " + message.ViewType);
                }
            }
        }

        static void OnDuelSysActFinished(DuelSysActFinishedMessage message)
        {
            HasSysActFinished = true;
        }
    }

    enum PvpSpectatorRapidState
    {
        None,
        WaitingForSysAct,
        Active,
        Finished
    }
}

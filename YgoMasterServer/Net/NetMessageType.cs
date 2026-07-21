using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YgoMaster.Net
{
    enum NetMessageType
    {
        PvpServerConnectionRequest,
        ConnectionRequest,
        ConnectionResponse,
        Ping,
        Pong,

        // Misc duel messages
        OpponentDuelEnded,
        DuelError,
        DuelTapSync,
        DuelEmote,
        DuelEngineState,
        DuelSysActFinished,
        DuelIsBusyEffect,

        // Duel spectator messages
        DuelSpectatorEnter,
        DuelSpectatorCount,
        DuelSpectatorData,
        DuelSpectatorFieldGuide,

        // Duel com messages
        DuelComMovePhase,
        DuelComDoCommand,
        DuelComCancelCommand,
        DuelComCancelCommand2,
        DuelDlgSetResult,
        DuelListSetCardExData,
        DuelListSetIndex,
        DuelListInitString,
        DuelComSetTemporaryCpu,
        DuelComAcquireStrategicPromptLease,
        DuelComReleaseStrategicPromptLease,
        // PvP worker → duelists: grant/deny strategic prompt lease
        DuelStrategicPromptLeaseResult,
        // PvP worker → duelists: durable lease lifecycle telemetry (JSONL line)
        DuelStrategicPromptLeaseEvent,

        // Authoritative public accepted-action events (PvP worker → session → duelists)
        DuelPublicActionEvent,
        // Raw DuelView audit evidence (PvP worker → session → duelists; audit-only)
        DuelRawViewEvidence,
        // Identity-free face probes (PvP worker → session → duelists; audit-only live face mapping)
        DuelFaceProbeEvidence,

        // Trade messages
        TradeEnterRoom,
        TradeLeaveRoom,
        TradeMoveCard,
        TradeStateChange,

        // Friend messages
        FriendDuelInvite,

    }
}

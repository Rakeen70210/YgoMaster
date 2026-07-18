using System.Collections.Generic;
using YgoMaster.Net;

namespace YgoMaster
{
    enum LlmPublicActionEventRoutingDecision
    {
        Dropped,
        FanOutToDuelists,
    }

    class LlmPublicActionEventRoutingResult
    {
        public LlmPublicActionEventRoutingDecision Decision;
        public readonly List<NetClient> Recipients = new List<NetClient>();
        public bool LoopedBackToPvpWorker;
    }

    /// <summary>
    /// Session-server routing for DuelPublicActionEvent: accept only from table.PvpClient,
    /// fan out only to Player1/Player2, never bounce back to the PvP worker.
    /// </summary>
    static class LlmPublicActionEventRouting
    {
        public static LlmPublicActionEventRoutingResult RouteIncoming(
            DuelRoomTable table,
            NetClient sender,
            object message)
        {
            LlmPublicActionEventRoutingResult result = new LlmPublicActionEventRoutingResult()
            {
                Decision = LlmPublicActionEventRoutingDecision.Dropped,
                LoopedBackToPvpWorker = false,
            };

            if (table == null || sender == null)
            {
                return result;
            }

            if (table.PvpClient == null || !object.ReferenceEquals(table.PvpClient, sender))
            {
                return result;
            }

            if (table.State != DuelRoomTableState.Dueling)
            {
                return result;
            }

            Player p1 = table.Player1;
            Player p2 = table.Player2;
            if (p1 != null && p1.NetClient != null)
            {
                result.Recipients.Add(p1.NetClient);
            }
            if (p2 != null && p2.NetClient != null)
            {
                result.Recipients.Add(p2.NetClient);
            }

            // Explicit anti-loop: never include PvpClient even if a player shared the socket.
            result.Recipients.RemoveAll(client => object.ReferenceEquals(client, table.PvpClient));
            result.LoopedBackToPvpWorker = false;
            result.Decision = LlmPublicActionEventRoutingDecision.FanOutToDuelists;
            return result;
        }

        public static int ResolveActorSeatFromTable(DuelRoomTable table, Player sender)
        {
            if (table == null || sender == null)
            {
                return -1;
            }
            if (object.ReferenceEquals(table.Player1, sender))
            {
                return 0;
            }
            if (object.ReferenceEquals(table.Player2, sender))
            {
                return 1;
            }
            return -1;
        }
    }
}

namespace YgoMaster
{
    enum LlmBrokerViewHandling
    {
        RunDefault,
        SuppressForBroker,
        RunCpuThinking,
    }

    static class LlmBrokerControlPolicy
    {
        public static bool ShouldControlPlayer(
            bool brokerEnabled,
            bool isPvpDuel,
            string brokerUrl,
            int controlPlayer,
            int myId,
            int player)
        {
            return brokerEnabled &&
                isPvpDuel &&
                !string.IsNullOrEmpty(brokerUrl) &&
                IsPlayerIndex(controlPlayer) &&
                IsPlayerIndex(myId) &&
                player == myId &&
                player == controlPlayer;
        }

        public static LlmBrokerViewHandling DecideViewHandling(
            bool hasActingPlayer,
            int actingPlayer,
            int myId,
            bool brokerControlsActingPlayer,
            bool brokerRequestStartedOrPending,
            bool isInfoDialog)
        {
            if (isInfoDialog || !hasActingPlayer)
            {
                return LlmBrokerViewHandling.RunDefault;
            }

            if (brokerControlsActingPlayer)
            {
                return brokerRequestStartedOrPending ?
                    LlmBrokerViewHandling.SuppressForBroker :
                    LlmBrokerViewHandling.RunCpuThinking;
            }

            return actingPlayer == myId ?
                LlmBrokerViewHandling.RunDefault :
                LlmBrokerViewHandling.RunCpuThinking;
        }

        static bool IsPlayerIndex(int player)
        {
            return player >= 0 && player <= 1;
        }
    }
}

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
            return DecideViewHandling(
                hasActingPlayer,
                actingPlayer,
                myId,
                brokerControlsActingPlayer,
                brokerRequestStartedOrPending,
                isInfoDialog,
                true);
        }

        public static LlmBrokerViewHandling DecideViewHandling(
            bool hasActingPlayer,
            int actingPlayer,
            int myId,
            bool brokerControlsActingPlayer,
            bool brokerRequestStartedOrPending,
            bool isInfoDialog,
            bool hasLocalDefaultInteraction)
        {
            if (isInfoDialog || !hasActingPlayer)
            {
                return LlmBrokerViewHandling.RunDefault;
            }

            if (brokerControlsActingPlayer)
            {
                // In-flight broker request always wins — never native-default under a pending prompt.
                if (brokerRequestStartedOrPending)
                {
                    return LlmBrokerViewHandling.SuppressForBroker;
                }

                // Live PvP hang (empty WaitInput): when the controlled seat has no legal or
                // automatic action, CpuThinking never emits a network duel command and the
                // server stalls. Same grounding as empty RunDialog — native original RunEffect
                // (RunDefault) advances presentation/mechanical state without inventing cancel.
                // hasLocalDefaultInteraction=true means extractable actions exist; then
                // CpuThinking remains the non-pending fallback when a request did not start.
                if (!hasLocalDefaultInteraction)
                {
                    return LlmBrokerViewHandling.RunDefault;
                }

                return LlmBrokerViewHandling.RunCpuThinking;
            }

            return actingPlayer == myId ?
                LlmBrokerViewHandling.RunDefault :
                LlmBrokerViewHandling.RunCpuThinking;
        }

        public static LlmBrokerViewHandling DecideFallbackHandling(
            bool hasActingPlayer,
            int actingPlayer,
            int myId,
            bool brokerControlsActingPlayer,
            bool isInfoDialog)
        {
            return DecideViewHandling(
                hasActingPlayer,
                actingPlayer,
                myId,
                brokerControlsActingPlayer,
                false,
                isInfoDialog,
                true);
        }

        static bool IsPlayerIndex(int player)
        {
            return player >= 0 && player <= 1;
        }
    }
}

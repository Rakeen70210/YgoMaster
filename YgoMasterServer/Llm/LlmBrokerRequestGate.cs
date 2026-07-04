namespace YgoMaster
{
    enum LlmBrokerRequestGateDecision
    {
        StartRequest,
        SuppressForPendingRequest,
        SuppressForCompletedRequest,
        FallbackToDefault
    }

    class LlmBrokerRequestGate
    {
        readonly object locker = new object();
        bool requestInFlight;
        ulong requestSeq;
        bool hasFailedSeq;
        ulong failedSeq;
        bool hasCompletedSeq;
        ulong completedSeq;
        bool completedSeqSuppressConsumed;

        public LlmBrokerRequestGateDecision Evaluate(ulong runEffectSeq)
        {
            lock (locker)
            {
                if (hasFailedSeq && failedSeq == runEffectSeq)
                {
                    return LlmBrokerRequestGateDecision.FallbackToDefault;
                }
                if (hasCompletedSeq && completedSeq == runEffectSeq)
                {
                    if (completedSeqSuppressConsumed)
                    {
                        return LlmBrokerRequestGateDecision.FallbackToDefault;
                    }
                    completedSeqSuppressConsumed = true;
                    return LlmBrokerRequestGateDecision.SuppressForCompletedRequest;
                }
                if (requestInFlight)
                {
                    return requestSeq == runEffectSeq ?
                        LlmBrokerRequestGateDecision.SuppressForPendingRequest :
                        LlmBrokerRequestGateDecision.FallbackToDefault;
                }

                requestSeq = runEffectSeq;
                requestInFlight = true;
                return LlmBrokerRequestGateDecision.StartRequest;
            }
        }

        public void MarkFailed(ulong runEffectSeq)
        {
            lock (locker)
            {
                hasFailedSeq = true;
                failedSeq = runEffectSeq;
            }
        }

        public void MarkCompleted(ulong runEffectSeq)
        {
            lock (locker)
            {
                hasCompletedSeq = true;
                completedSeq = runEffectSeq;
                completedSeqSuppressConsumed = false;
            }
        }

        public void Finish(ulong runEffectSeq)
        {
            lock (locker)
            {
                if (requestInFlight && requestSeq == runEffectSeq)
                {
                    requestInFlight = false;
                }
            }
        }

        public void Reset()
        {
            lock (locker)
            {
                requestInFlight = false;
                requestSeq = 0;
                hasFailedSeq = false;
                failedSeq = 0;
                hasCompletedSeq = false;
                completedSeq = 0;
                completedSeqSuppressConsumed = false;
            }
        }
    }
}

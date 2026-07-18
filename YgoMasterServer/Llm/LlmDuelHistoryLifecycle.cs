namespace YgoMaster
{
    /// <summary>
    /// Duel-generation reset helper for client lifecycle boundaries (begin/end/disconnect).
    /// Does not touch broker gate or turn_memory.
    /// </summary>
    static class LlmDuelHistoryLifecycle
    {
        public static int ResetForBoundary(LlmDuelHistoryTracker tracker, int currentGeneration)
        {
            int nextGeneration = currentGeneration + 1;
            if (tracker != null)
            {
                tracker.ResetForNewDuel(nextGeneration);
            }
            return nextGeneration;
        }
    }
}

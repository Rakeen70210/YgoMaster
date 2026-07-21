using System;

namespace YgoMaster
{
    /// <summary>
    /// Pure helpers for the RunEffect exact-once originalRunEffect contract.
    /// Outer controller must never double-forward after BeginFallback.
    /// </summary>
    static class CampaignCpuRunEffectContract
    {
        public enum OuterAction
        {
            /// <summary>Call original exactly once in the outer function.</summary>
            ForwardOriginalOnce,
            /// <summary>Do not call original; scripted path already handled the view.</summary>
            ScriptedHandledNoOriginal,
            /// <summary>
            /// Enter NativeLease then call original exactly once inside BeginFallback;
            /// outer must return that result without calling original again.
            /// </summary>
            BeginFallbackThenReturn
        }

        /// <summary>
        /// Simulates the dual-driver invariant: total original invocations per OnRunEffect entry
        /// must be 0 or 1.
        /// </summary>
        public static int CountOriginalInvocations(
            OuterAction action,
            Func<int> originalOnce)
        {
            if (originalOnce == null)
            {
                throw new ArgumentNullException("originalOnce");
            }
            int calls = 0;
            Func<int> counting = () =>
            {
                calls++;
                return originalOnce();
            };

            switch (action)
            {
                case OuterAction.ForwardOriginalOnce:
                    counting();
                    break;
                case OuterAction.ScriptedHandledNoOriginal:
                    break;
                case OuterAction.BeginFallbackThenReturn:
                    // BeginFallback is the sole forwarder on this path.
                    counting();
                    break;
                default:
                    throw new ArgumentOutOfRangeException("action");
            }
            return calls;
        }

        /// <summary>
        /// PR4a always-native: owned opponent decision windows always use BeginFallback
        /// (never RuleCommit), regardless of scripted_views.
        /// </summary>
        public static OuterAction DecideAlwaysNativeOwnedWindow()
        {
            return OuterAction.BeginFallbackThenReturn;
        }

        /// <summary>
        /// Pass-through for MyID / unknown / non-owned: outer forwards once.
        /// </summary>
        public static OuterAction DecidePassThrough()
        {
            return OuterAction.ForwardOriginalOnce;
        }
    }
}

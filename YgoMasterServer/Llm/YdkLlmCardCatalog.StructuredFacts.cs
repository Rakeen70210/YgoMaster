namespace YgoMaster
{
    /// <summary>
    /// Pure GameCardInfo-frame/kind mapping for LLM structured card facts.
    /// Link-compiled into the harness without YdkHelper so Slice 1A can assert
    /// the real YDK mapping path.
    /// </summary>
    static partial class YdkLlmCardCatalog
    {
        public static string ResolveSummonFamily(CardFrame frame)
        {
            switch (frame)
            {
                case CardFrame.Xyz:
                case CardFrame.XyzPend:
                    return "xyz";
                case CardFrame.Sync:
                case CardFrame.SyncPend:
                case CardFrame.Dsync:
                    return "synchro";
                case CardFrame.Link:
                    return "link";
                case CardFrame.Fusion:
                case CardFrame.FusionPend:
                    return "fusion";
                case CardFrame.Ritual:
                case CardFrame.RitualPend:
                    return "ritual";
                case CardFrame.Magic:
                    return "spell";
                case CardFrame.Trap:
                    return "trap";
                case CardFrame.Token:
                    return "token";
                case CardFrame.Normal:
                case CardFrame.Effect:
                case CardFrame.Pend:
                case CardFrame.PendFx:
                    return "main_deck_monster";
                default:
                    return "unknown";
            }
        }

        public static bool IsTunerKind(CardKind kind)
        {
            switch (kind)
            {
                case CardKind.Tuner:
                case CardKind.TunerFx:
                case CardKind.SyncTuner:
                case CardKind.Dtuner:
                case CardKind.SpTuner:
                case CardKind.SpDtuner:
                case CardKind.FlipTuner:
                case CardKind.UnionTuner:
                case CardKind.PendTuner:
                case CardKind.PendNTuner:
                case CardKind.FusionTuner:
                case CardKind.FusionTunerFX:
                case CardKind.RirualTunerFX:
                case CardKind.TokenTuner:
                    return true;
                default:
                    return false;
            }
        }
    }
}

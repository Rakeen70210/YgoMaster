namespace YgoMaster
{
    /// <summary>
    /// Visibility policy modes for absolute-public history (Slice 3).
    /// RuntimeDllField uses live-validated DLL_DuelGetCardFace values (0/1).
    /// FixtureValidated uses the separate legal-action/public-state fixture face domain (8/4).
    /// </summary>
    enum LlmPublicVisibilityMode
    {
        /// <summary>Deprecated alias: same as RuntimeDllField (grave + face-up field via DLL face).</summary>
        RuntimeSafe,
        /// <summary>
        /// Live-validated runtime capture: grave always; field 0..12 only when DLL raw_face==1.
        /// Banished remains closed (no live face-up/facedown banished fixture in Slice 3).
        /// </summary>
        RuntimeDllField,
        /// <summary>Opt-in offline fixture policy: face-up field/banished with fixture face=8.</summary>
        FixtureValidated,
    }

    /// <summary>
    /// DLL_DuelGetCardFace runtime domain validated from live P2 duel face probes (2026-07-15):
    /// raw_face 0 = facedown/non-public identity; raw_face 1 = face-up/public identity.
    /// Distinct from fixture/legal-action face domain (8/4) — never compare across domains.
    /// </summary>
    static class LlmDllRuntimeFace
    {
        public const int FacedownOrNonPublic = 0;
        public const int FaceUpPublic = 1;

        public static bool IsPublicFaceUp(int dllRawFace)
        {
            return dllRawFace == FaceUpPublic;
        }

        public static bool IsFacedownOrNonPublic(int dllRawFace)
        {
            return dllRawFace == FacedownOrNonPublic;
        }
    }

    /// <summary>
    /// Absolute game-public face/zone visibility (Slice 3).
    /// Fail closed on unknown face/location. Does not depend on controlled player.
    /// </summary>
    static class LlmAbsolutePublicVisibility
    {
        public static LlmPublicVisibilityMode RuntimeMode
        {
            get { return LlmPublicVisibilityMode.RuntimeDllField; }
        }

        public static bool IsTargetRawEvidenceFamily(DuelViewType viewType)
        {
            switch (viewType)
            {
                case DuelViewType.TurnChange:
                case DuelViewType.PhaseChange:
                case DuelViewType.RunSummon:
                case DuelViewType.RunSpSummon:
                case DuelViewType.CutinSummon:
                case DuelViewType.CutinReverse:
                case DuelViewType.CutinFlip:
                case DuelViewType.ChainSet:
                case DuelViewType.ChainRun:
                case DuelViewType.ChainStep:
                case DuelViewType.ChainEnd:
                case DuelViewType.CutinActivate:
                case DuelViewType.CutinChain:
                case DuelViewType.BattleAttack:
                case DuelViewType.BattleRun:
                case DuelViewType.BattleEnd:
                case DuelViewType.LifeDamage:
                case DuelViewType.LifeSet:
                case DuelViewType.CardMove:
                case DuelViewType.CardSet:
                case DuelViewType.CardFlipTurn:
                case DuelViewType.CardBreak:
                case DuelViewType.CardExclude:
                case DuelViewType.CardVanish:
                case DuelViewType.CutinBreak:
                case DuelViewType.CutinDamage:
                case DuelViewType.HandShow:
                case DuelViewType.HandOpen:
                case DuelViewType.HandShuffle:
                case DuelViewType.DeckShuffle:
                case DuelViewType.MonstShuffle:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsShuffleView(DuelViewType viewType)
        {
            return viewType == DuelViewType.HandShuffle ||
                viewType == DuelViewType.DeckShuffle ||
                viewType == DuelViewType.MonstShuffle;
        }

        /// <summary>
        /// Runtime projection gate using DLL face domain (0/1). Prefer this for live capture.
        /// </summary>
        public static bool CanExposeDllRuntimeCardIdentity(int position, int dllRawFace, bool handOpen = false)
        {
            return CanExposeCardIdentity(position, dllRawFace, handOpen, LlmPublicVisibilityMode.RuntimeDllField);
        }

        /// <summary>Runtime projection gate (DLL face domain by default).</summary>
        public static bool CanExposeCardIdentity(int position, int face, bool handOpen = false)
        {
            return CanExposeCardIdentity(position, face, handOpen, RuntimeMode);
        }

        public static bool CanExposeCardIdentity(
            int position,
            int face,
            bool handOpen,
            LlmPublicVisibilityMode mode)
        {
            if (position < 0)
            {
                return false;
            }
            if (position == LlmPublicHistoryRedactionPolicy.PosGrave)
            {
                return true;
            }
            if (position == LlmPublicHistoryRedactionPolicy.PosHand)
            {
                return handOpen;
            }
            if (position == LlmPublicHistoryRedactionPolicy.PosDeck ||
                position == LlmPublicHistoryRedactionPolicy.PosExtra)
            {
                return false;
            }

            // Normalize legacy RuntimeSafe to RuntimeDllField.
            if (mode == LlmPublicVisibilityMode.RuntimeSafe)
            {
                mode = LlmPublicVisibilityMode.RuntimeDllField;
            }

            if (mode == LlmPublicVisibilityMode.RuntimeDllField)
            {
                // Field 0..12 only when DLL raw_face == 1. Banished closed.
                if (position <= LlmPublicHistoryRedactionPolicy.PosSpellTrapMax)
                {
                    return LlmDllRuntimeFace.IsPublicFaceUp(face);
                }
                return false;
            }

            // FixtureValidated: separate domain face=8.
            // Banished face-up may be modeled offline only (never runtime DLL path).
            if (!LlmPublicHistoryRedactionPolicy.IsPublicFaceUp(face))
            {
                return false;
            }
            if (position <= LlmPublicHistoryRedactionPolicy.PosSpellTrapMax)
            {
                return true;
            }
            if (position == LlmPublicHistoryRedactionPolicy.PosBanishedMin)
            {
                return true;
            }
            return false;
        }

        public static bool IsFaceUpMonsterFixture(int position, int face)
        {
            return position >= 0 && position <= 4 &&
                face == LlmPublicHistoryRedactionPolicy.PublicFaceUpValue;
        }

        public static bool IsFacedownMonsterFixture(int position, int face)
        {
            return position >= 0 && position <= 4 &&
                face == LlmPublicHistoryRedactionPolicy.FacedownFaceValue;
        }

        public static bool IsFaceUpBanishedFixture(int position, int face)
        {
            return position == LlmPublicHistoryRedactionPolicy.PosBanishedMin &&
                face == LlmPublicHistoryRedactionPolicy.PublicFaceUpValue;
        }

        public static bool IsFacedownBanishedFixture(int position, int face)
        {
            return position == LlmPublicHistoryRedactionPolicy.PosBanishedMin &&
                face == LlmPublicHistoryRedactionPolicy.FacedownFaceValue;
        }

        /// <summary>Field positions eligible for identity-free face probes (0-12).</summary>
        public static readonly int[] FaceProbeFieldPositions =
            new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        /// <summary>
        /// Runtime identity positions: field 0..12 + grave. No hand/deck/extra/banished.
        /// </summary>
        public static readonly int[] RuntimeIdentityPositions = BuildRuntimeIdentityPositions();

        /// <summary>Legacy name: now field+grave (not grave-only).</summary>
        public static readonly int[] RuntimeSafeIdentityPositions = RuntimeIdentityPositions;

        static int[] BuildRuntimeIdentityPositions()
        {
            int[] positions = new int[FaceProbeFieldPositions.Length + 1];
            for (int i = 0; i < FaceProbeFieldPositions.Length; i++)
            {
                positions[i] = FaceProbeFieldPositions[i];
            }
            positions[positions.Length - 1] = LlmPublicHistoryRedactionPolicy.PosGrave;
            return positions;
        }
    }
}

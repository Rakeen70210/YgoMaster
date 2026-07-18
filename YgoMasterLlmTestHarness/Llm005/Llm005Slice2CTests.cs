using System;
using System.Collections.Generic;
using System.Linq;

namespace YgoMaster
{
    /// <summary>
    /// YGOMASTER-LLM-005 Slice 2C: optional Layer A Synchro candidacy fixtures (TDD RED).
    ///
    /// Hardened contract (independent review):
    /// - Exact material/ED identities (name or internal/synthetic id) — never generic words alone.
    /// - Tuner-as-setup-root and multi-non-tuner subset candidacy.
    /// - Engine-current SummonSp Synchro is not duplicated as a future inferred edge.
    /// - Deterministic nonzero setup/unlock score evidence on emitted candidates.
    /// - Negative reasons must be scoped to the tested root.
    /// Link remains unsupported while LinkRating is null.
    ///
    /// Card IDs: Master Duel internal ids (YdkIds.txt) or clearly synthetic fixture ids.
    /// </summary>
    static class Llm005Slice2CTests
    {
        const int ControlledPlayer = 1;
        const int OpponentPlayer = 0;
        const int RuntimeFaceUp = 1;
        const int RuntimeFaceDown = 0;

        // Internal ids from YgoMaster/Data/YdkIds.txt (passcode → internal).
        // Girochin Kuwagata 84620194 → 5136 (live P2 log card_id 5136); Cave Dragon 66672569 → 4103;
        // Junk Synchron 63977008 → 7687; Stardust Dragon 44508094 → 7734;
        // Number 39: Utopia 84013237 → 9575. Note: 77084837 → 5374 is a different card.
        const int GirochinInternalCardId = 5136;
        const int CaveDragonInternalCardId = 4103;
        const int JunkSynchronInternalCardId = 7687;
        const int StardustDragonInternalCardId = 7734;
        const int UtopiaInternalCardId = 9575;

        // Clearly synthetic fixture identities (not Ydk passcodes).
        const int SynthNonTunerLevel5Id = 99105201;
        const int SynthNonTunerLevel2Id = 99105202;
        const int SynthTunerLevel3Id = 99105203;
        const int SynthTunerLevel4Id = 99105204;
        const int SynthLevel3NonTunerId = 99105205;
        const int SynthUnknownLevelId = 99105206;
        const int SynthNonTunerLevel3Id = 99105207;
        const int SynthNonTunerLevel4Id = 99105208;
        const int SynthDupNonTunerLevel2Id = 99105209; // two copies share this CardId
        const int SynthDupTunerLevel3Id = 99105210; // two face-down copies for flip targeting
        const int SentinelOppHandId = 99105290;
        const string SentinelOppHandName = "SENTINEL_OPP_HAND_SLICE2C";
        const int SentinelOppSetId = 99105291;
        const string SentinelOppSetName = "SENTINEL_OPP_SET_SLICE2C";
        const int SentinelOppExtraId = 99105292;
        const string SentinelOppExtraName = "SENTINEL_OPP_EXTRA_SLICE2C";

        const string JunkSynchronName = "Junk Synchron";
        const string StardustName = "Stardust Dragon";
        const string NonTunerL5Name = "SYNTH_L5_NONTUNER";
        const string NonTunerL3Name = "SYNTH_L3_NONTUNER_A";
        const string NonTunerL4Name = "SYNTH_L4_NONTUNER_B";
        const string UtopiaName = "Number 39: Utopia";

        public static void RunAll()
        {
            GroundedTunerPlusNonTunerEmitsFutureOnlySynchroCandidate();
            SummonOrFlipTunerWithFaceUpNonTunerEmitsSynchroCandidate();
            OneTunerPlusTwoNonTunersExactLevelSumEmitsSynchroWithOrderedMaterials();
            EngineCurrentSummonSpSynchroNotDuplicatedAsFutureInferred();
            SynchroCandidatePreservesProvenanceBoundaryScoreAndProjection();
            NegativeControlsNoSynchroEdgeWithRootScopedReason();
            OpponentHiddenCannotAffectOrLeakSynchroGraph();
            LinkAbsentWhenLinkRatingNullNeverInferFromDefLevelText();
            ExistingXyzRootCompletenessUndisturbed();
            // Independent-review regressions (must fail against pre-fix production).
            NormalSummonSecondCopyOfSameCardIdIsDistinctMaterial();
            ReversePromotesOnlyExactPositionAndIndexTarget();
            DuplicateCardIdMaterialsRemainDistinctCandidateLines();
            ResourceEfficientFewerMaterialsScoresHigherForSameExtra();
        }

        // ---------------------------------------------------------------------
        // 1. Grounded Synchro: face-up tuner + summon non-tuner
        // ---------------------------------------------------------------------
        static void GroundedTunerPlusNonTunerEmitsFutureOnlySynchroCandidate()
        {
            // Face-up Junk Synchron (tuner L3) + legal Normal Summon of L5 non-tuner.
            // After summon: L3+L5=8 matches controlled Extra Deck Stardust (level 8 Synchro).
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.LegalActions.Add(MakeSummonRoot(
                SynthNonTunerLevel5Id, NonTunerL5Name, level: 5, isTuner: false));
            snap.LegalActions.Add(MakeEndRoot(1));
            snap.LegalActions.Add(MakeBattleRoot(2));
            snap.StrategicActionCount = 3;

            LlmSelfResources self = MakeSynchroReadySelf(
                faceUpTunerLevel: 3,
                faceUpTunerId: JunkSynchronInternalCardId,
                faceUpTunerName: JunkSynchronName,
                includeLevel5NonTunerInHand: true,
                includeLevel8SynchroExtra: true,
                includeRank4XyzExtra: false);

            LlmSearchGraph graph = Expand(snap, self);
            AssertRootCoverage(graph, 3, "synchro positive root completeness");

            LlmSearchLine synchro = FindSynchroContinuation(graph, rootActionId: 0);
            AssertNotNull(synchro,
                "Slice 2C: grounded tuner + non-tuner level-sum matching Extra Deck Synchro "
                + "must emit a Synchro candidate line (missing production Synchro candidacy)");
            AssertFutureOnlySynchroLine(synchro, "summon non-tuner setup");
            AssertExactMaterialAndExtraIdentities(
                synchro,
                JunkSynchronName, JunkSynchronInternalCardId,
                NonTunerL5Name, SynthNonTunerLevel5Id,
                StardustName, StardustDragonInternalCardId);
            AssertNonzeroSetupUnlockScore(synchro, "summon non-tuner setup");
        }

        // ---------------------------------------------------------------------
        // 1b. Positive: legal root summons/flips a grounded tuner; face-up non-tuner supplies rest
        // ---------------------------------------------------------------------
        static void SummonOrFlipTunerWithFaceUpNonTunerEmitsSynchroCandidate()
        {
            // Face-up L5 non-tuner + Flip Summon face-down Junk Synchron (tuner L3).
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Reverse,
                Player = ControlledPlayer,
                Position = 1,
                Index = 0,
                CardId = JunkSynchronInternalCardId,
                Card = Meta(
                    JunkSynchronInternalCardId, JunkSynchronName, "Effect", "main_deck_monster",
                    level: 3, isTuner: true),
                ActionLabel = "Flip Summon " + JunkSynchronName,
                IsMechanical = false,
                StrategicRole = "board_development",
            });
            snap.LegalActions.Add(MakeEndRoot(1));
            snap.StrategicActionCount = 2;

            LlmSelfResources self = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(
                        SynthNonTunerLevel5Id, NonTunerL5Name, 0, true,
                        level: 5, isTuner: false),
                    FieldCard(
                        JunkSynchronInternalCardId, JunkSynchronName, 1, false,
                        level: 3, isTuner: true),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, level: 8),
                });

            LlmSearchGraph graph = Expand(snap, self);
            LlmSearchLine synchro = FindSynchroContinuation(graph, rootActionId: 0);
            AssertNotNull(synchro,
                "Slice 2C: flip/summon tuner with face-up non-tuner must emit Synchro candidate");
            AssertFutureOnlySynchroLine(synchro, "flip tuner setup");
            AssertExactMaterialAndExtraIdentities(
                synchro,
                JunkSynchronName, JunkSynchronInternalCardId,
                NonTunerL5Name, SynthNonTunerLevel5Id,
                StardustName, StardustDragonInternalCardId);
            AssertNonzeroSetupUnlockScore(synchro, "flip tuner setup");
        }

        // ---------------------------------------------------------------------
        // 1c. One tuner + two non-tuners whose levels sum exactly (subset candidacy)
        // ---------------------------------------------------------------------
        static void OneTunerPlusTwoNonTunersExactLevelSumEmitsSynchroWithOrderedMaterials()
        {
            // Face-up tuner L3 + face-up non-tuners L2 and L3; summon is not required —
            // use Normal Summon of a dummy? Better: root Summons one non-tuner L3 while
            // field already has face-up tuner L3 + face-up non-tuner L2 → after: 3+2+3=8.
            // Materials for Stardust L8: tuner 3 + non-tuners 2+3.
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.LegalActions.Add(MakeSummonRoot(
                SynthNonTunerLevel3Id, NonTunerL3Name, level: 3, isTuner: false));
            snap.LegalActions.Add(MakeEndRoot(1));
            snap.StrategicActionCount = 2;

            LlmSelfResources self = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(
                        SynthNonTunerLevel3Id, NonTunerL3Name, 13, true,
                        level: 3, isTuner: false),
                },
                new List<LlmSelfResourceCard>()
                {
                    // Deterministic zone order: zone 0 tuner, zone 1 L2 non-tuner.
                    FieldCard(
                        JunkSynchronInternalCardId, JunkSynchronName, 0, true,
                        level: 3, isTuner: true),
                    FieldCard(
                        SynthNonTunerLevel2Id, "SYNTH_L2_NONTUNER", 1, true,
                        level: 2, isTuner: false),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, level: 8),
                });

            LlmSearchGraph graph = Expand(snap, self);
            LlmSearchLine synchro = FindSynchroContinuation(graph, rootActionId: 0);
            AssertNotNull(synchro,
                "Slice 2C: 1 tuner + 2 non-tuners exact level-sum must emit Synchro candidate "
                + "(not an exactly-two-material shortcut)");
            AssertFutureOnlySynchroLine(synchro, "multi non-tuner subset");

            // Exact identities for all three materials + ED.
            string blob = ProjectedSynchroBlob(synchro);
            AssertExactIdentityPresent(blob, JunkSynchronName, JunkSynchronInternalCardId,
                "tuner material");
            AssertExactIdentityPresent(blob, "SYNTH_L2_NONTUNER", SynthNonTunerLevel2Id,
                "non-tuner L2 material");
            AssertExactIdentityPresent(blob, NonTunerL3Name, SynthNonTunerLevel3Id,
                "non-tuner L3 material");
            AssertExactIdentityPresent(blob, StardustName, StardustDragonInternalCardId,
                "Extra Deck Synchro");

            // Deterministic material ordering: identities appear in stable ascending card_id order
            // or documented zone order in projection (assert relative order by card id ascending).
            int idxTuner = FirstIdentityIndex(blob, JunkSynchronName, JunkSynchronInternalCardId);
            int idxL2 = FirstIdentityIndex(blob, "SYNTH_L2_NONTUNER", SynthNonTunerLevel2Id);
            int idxL3 = FirstIdentityIndex(blob, NonTunerL3Name, SynthNonTunerLevel3Id);
            AssertTrue(idxTuner >= 0 && idxL2 >= 0 && idxL3 >= 0,
                "all three material identities must appear for ordering check");
            // Expected deterministic order by card_id ascending: 7687, 99105202, 99105207
            AssertTrue(
                idxTuner < idxL2 && idxL2 < idxL3,
                "deterministic material identity order by ascending card_id "
                + "(tuner 7687, L2 99105202, L3 99105207); got indices "
                + idxTuner + "," + idxL2 + "," + idxL3);

            AssertNonzeroSetupUnlockScore(synchro, "multi non-tuner subset");

            // Second expand is byte-identical projection.
            LlmSearchGraph graph2 = Expand(snap, self);
            AssertEqual(
                MiniJSON.Json.Serialize(LlmSearchProjection.Project(graph)),
                MiniJSON.Json.Serialize(LlmSearchProjection.Project(graph2)),
                "deterministic multi-non-tuner Synchro projection");
        }

        // ---------------------------------------------------------------------
        // 1d. Engine-current SummonSp Synchro root is not duplicated as future-only
        // ---------------------------------------------------------------------
        static void EngineCurrentSummonSpSynchroNotDuplicatedAsFutureInferred()
        {
            DecisionSnapshot snap = MakeBaseSnapshot();
            // Current legal Xyz-style SummonSp root for Stardust (already engine-current).
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.SummonSp,
                Player = ControlledPlayer,
                Position = 14,
                Index = 0,
                CardId = StardustDragonInternalCardId,
                Card = new LlmCardMetadata()
                {
                    CardId = StardustDragonInternalCardId,
                    Name = StardustName,
                    Kind = "Synchro",
                    Frame = "Synchro",
                    SummonFamily = "synchro",
                    Level = 8,
                    IsExtraDeck = true,
                    IsTuner = false,
                    LinkRating = null,
                },
                ActionLabel = "Synchro Summon " + StardustName,
                IsMechanical = false,
                StrategicRole = "extra_deck_summon",
            });
            snap.LegalActions.Add(MakeEndRoot(1));
            snap.StrategicActionCount = 2;

            // Board already has materials; engine already offers SummonSp — must not also emit
            // a future-only rules_inferred Synchro continuation for the same root.
            LlmSelfResources self = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(
                        JunkSynchronInternalCardId, JunkSynchronName, 0, true,
                        level: 3, isTuner: true),
                    FieldCard(
                        SynthNonTunerLevel5Id, NonTunerL5Name, 1, true,
                        level: 5, isTuner: false),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, level: 8),
                });

            LlmSearchGraph graph = Expand(snap, self);
            LlmSearchLine currentRoot = graph.Lines.FirstOrDefault(l =>
                l != null && l.IsRootShell && l.RootActionId == 0);
            AssertNotNull(currentRoot, "SummonSp Synchro must remain a represented current root");
            AssertTrue(
                currentRoot.Provenance == "engine_current" || currentRoot.CommitEligible,
                "current SummonSp root is engine_current / commit-eligible");

            LlmSearchLine futureDup = FindSynchroContinuation(graph, rootActionId: 0);
            AssertTrue(
                futureDup == null,
                "engine-current SummonSp Synchro must not be duplicated as a future "
                + "rules_inferred Synchro continuation on the same root");
        }

        // ---------------------------------------------------------------------
        // 2. Provenance, exact identities, boundary, score, deterministic projection
        // ---------------------------------------------------------------------
        static void SynchroCandidatePreservesProvenanceBoundaryScoreAndProjection()
        {
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.LegalActions.Add(MakeSummonRoot(
                SynthNonTunerLevel5Id, NonTunerL5Name, level: 5, isTuner: false));
            snap.LegalActions.Add(MakeEndRoot(1));
            LlmSelfResources self = MakeSynchroReadySelf(
                faceUpTunerLevel: 3,
                faceUpTunerId: JunkSynchronInternalCardId,
                faceUpTunerName: JunkSynchronName,
                includeLevel5NonTunerInHand: true,
                includeLevel8SynchroExtra: true,
                includeRank4XyzExtra: false);

            LlmSearchGraph a = Expand(snap, self);
            LlmSearchGraph b = Expand(snap, self);
            LlmSearchLine synA = FindSynchroContinuation(a, 0);
            AssertNotNull(synA, "synchro line for provenance fixture");

            AssertEqual("rules_inferred", synA.Provenance, "provenance rules_inferred");
            AssertEqual(false, synA.CommitEligible, "commit_eligible false");
            AssertTrue(
                !string.IsNullOrEmpty(synA.Boundary)
                && (synA.Boundary.IndexOf("opponent", StringComparison.OrdinalIgnoreCase) >= 0
                    || synA.Boundary.IndexOf("fresh", StringComparison.OrdinalIgnoreCase) >= 0
                    || synA.Boundary.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0),
                "explicit opponent-response/unsupported boundary, got: " + synA.Boundary);

            AssertExactMaterialAndExtraIdentities(
                synA,
                JunkSynchronName, JunkSynchronInternalCardId,
                NonTunerL5Name, SynthNonTunerLevel5Id,
                StardustName, StardustDragonInternalCardId);

            AssertTrue(
                synA.Uncertainty != null
                && synA.Uncertainty.Any(u =>
                    u != null
                    && (u.IndexOf("engine", StringComparison.OrdinalIgnoreCase) >= 0
                        || u.IndexOf("confirm", StringComparison.OrdinalIgnoreCase) >= 0
                        || u.IndexOf("unchecked", StringComparison.OrdinalIgnoreCase) >= 0)),
                "uncertainty for future engine confirmation required");

            AssertNonzeroSetupUnlockScore(synA, "provenance fixture");

            string projA = MiniJSON.Json.Serialize(LlmSearchProjection.Project(a));
            string projB = MiniJSON.Json.Serialize(LlmSearchProjection.Project(b));
            AssertEqual(projA, projB, "deterministic projection for identical Synchro fixtures");
            AssertExactIdentityPresent(projA, StardustName, StardustDragonInternalCardId,
                "provider projection Extra Deck Synchro");
            AssertExactIdentityPresent(projA, JunkSynchronName, JunkSynchronInternalCardId,
                "provider projection tuner");
            AssertExactIdentityPresent(projA, NonTunerL5Name, SynthNonTunerLevel5Id,
                "provider projection non-tuner");
        }

        // ---------------------------------------------------------------------
        // 3. Negative controls — no Synchro edge + root-scoped reason
        // ---------------------------------------------------------------------
        static void NegativeControlsNoSynchroEdgeWithRootScopedReason()
        {
            // 3a. Insufficient total level (tuner L3 + non-tuner L2 = 5, ED wants 8).
            AssertNoSynchroWithRootScopedReason(
                MakeSummonRoot(SynthNonTunerLevel2Id, "SYNTH_L2", level: 2, isTuner: false),
                MakeFieldSelf(
                    tunerId: JunkSynchronInternalCardId,
                    tunerName: JunkSynchronName,
                    tunerLevel: 3,
                    extraSynchroLevel: 8),
                "insufficient",
                "insufficient total level");

            // 3b. No tuner on field (two non-tuners).
            AssertNoSynchroWithRootScopedReason(
                MakeSummonRoot(SynthNonTunerLevel5Id, NonTunerL5Name, level: 5, isTuner: false),
                MakeFieldSelf(
                    tunerId: SynthLevel3NonTunerId,
                    tunerName: "SYNTH_L3_NONTUNER",
                    tunerLevel: 3,
                    tunerIsTuner: false,
                    extraSynchroLevel: 8),
                "no_tuner",
                "no tuner");

            // 3c. Two tuners without a non-tuner.
            DecisionSnapshot twoTuners = MakeBaseSnapshot();
            twoTuners.LegalActions.Add(
                MakeSummonRoot(SynthTunerLevel4Id, "SYNTH_L4_TUNER", level: 4, isTuner: true));
            twoTuners.LegalActions.Add(MakeEndRoot(1));
            LlmSelfResources twoTunerSelf = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(
                        JunkSynchronInternalCardId, JunkSynchronName, 0, true,
                        level: 3, isTuner: true),
                    FieldCard(
                        SynthTunerLevel3Id, "SYNTH_L3_TUNER_B", 1, true,
                        level: 3, isTuner: true),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, level: 8),
                });
            LlmSearchGraph gTwo = Expand(twoTuners, twoTunerSelf);
            AssertTrue(FindSynchroContinuation(gTwo, 0) == null,
                "two tuners without non-tuner: no Synchro edge");
            AssertTrue(
                HasRootScopedSynchroNoCandidateReason(gTwo, 0),
                "two tuners without non-tuner: root-scoped explicit no-candidate reason required");

            // 3d. Unknown / nonpositive level on setup root material.
            AssertNoSynchroWithRootScopedReason(
                MakeSummonRoot(SynthUnknownLevelId, "SYNTH_UNKNOWN_LEVEL", level: 0, isTuner: false),
                MakeFieldSelf(
                    tunerId: JunkSynchronInternalCardId,
                    tunerName: JunkSynchronName,
                    tunerLevel: 3,
                    extraSynchroLevel: 8),
                "level",
                "unknown/nonpositive level");

            // 3e. Missing matching Synchro Extra Deck candidate.
            AssertNoSynchroWithRootScopedReason(
                MakeSummonRoot(SynthNonTunerLevel5Id, NonTunerL5Name, level: 5, isTuner: false),
                MakeFieldSelf(
                    tunerId: JunkSynchronInternalCardId,
                    tunerName: JunkSynchronName,
                    tunerLevel: 3,
                    extraSynchroLevel: 0),
                "extra",
                "missing matching Synchro Extra Deck");
        }

        // ---------------------------------------------------------------------
        // 4. Hidden-information safety
        // ---------------------------------------------------------------------
        static void OpponentHiddenCannotAffectOrLeakSynchroGraph()
        {
            DecisionSnapshot snapA = MakeBaseSnapshot();
            snapA.LegalActions.Add(
                MakeSummonRoot(SynthNonTunerLevel5Id, NonTunerL5Name, level: 5, isTuner: false));
            snapA.LegalActions.Add(MakeEndRoot(1));
            DecisionSnapshot snapB = MakeBaseSnapshot();
            snapB.LegalActions.Add(
                MakeSummonRoot(SynthNonTunerLevel5Id, NonTunerL5Name, level: 5, isTuner: false));
            snapB.LegalActions.Add(MakeEndRoot(1));

            PlantOpponentHidden(snapA, SentinelOppHandId, SentinelOppHandName,
                SentinelOppSetId, SentinelOppSetName, SentinelOppExtraId, SentinelOppExtraName);
            PlantOpponentHidden(snapB, 99105293, "ALT_OPP_HAND_2C",
                99105294, "ALT_OPP_SET_2C", 99105295, "ALT_OPP_EXTRA_2C");

            LlmSelfResources self = MakeSynchroReadySelf(
                faceUpTunerLevel: 3,
                faceUpTunerId: JunkSynchronInternalCardId,
                faceUpTunerName: JunkSynchronName,
                includeLevel5NonTunerInHand: true,
                includeLevel8SynchroExtra: true,
                includeRank4XyzExtra: false);

            LlmSearchGraph gA = Expand(snapA, self);
            LlmSearchGraph gB = Expand(snapB, self);
            string projA = MiniJSON.Json.Serialize(LlmSearchProjection.Project(gA));
            string projB = MiniJSON.Json.Serialize(LlmSearchProjection.Project(gB));
            AssertEqual(projA, projB,
                "Layer A Synchro projection must be invariant across opponent-hidden pairs");

            AssertNoLeak(projA,
                SentinelOppHandName, SentinelOppHandId,
                SentinelOppSetName, SentinelOppSetId,
                SentinelOppExtraName, SentinelOppExtraId,
                "ALT_OPP_HAND_2C", "ALT_OPP_SET_2C", "ALT_OPP_EXTRA_2C");
            AssertNoLeak(SerializeGraph(gA),
                SentinelOppHandName, SentinelOppSetName, SentinelOppExtraName);
        }

        // ---------------------------------------------------------------------
        // 5. Link remains absent; never infer LinkRating from DEF/level/text
        // ---------------------------------------------------------------------
        static void LinkAbsentWhenLinkRatingNullNeverInferFromDefLevelText()
        {
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.LegalActions.Add(
                MakeSummonRoot(SynthNonTunerLevel5Id, NonTunerL5Name, level: 5, isTuner: false));
            snap.LegalActions.Add(MakeEndRoot(1));

            LlmSelfResources self = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(
                        JunkSynchronInternalCardId, JunkSynchronName, 0, true,
                        level: 3, isTuner: true, def: 2, text: "Tuner. Not a Link."),
                    FieldCard(
                        SynthNonTunerLevel2Id, "SYNTH_BODY", 1, true,
                        level: 2, isTuner: false, def: 2, text: "Link-looking DEF only"),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    LlmSelfResourceExtraDeckEntry.Create(
                        99105300, "SYNTH_LINK_NULL_RATING", "Link", "link",
                        level: null, rank: null, usesRank: false,
                        linkRating: null,
                        isTuner: false, kind: "Link", atk: 1000, def: 2,
                        text: "2 monsters. Link-2 (text must not create rating).",
                        duplicateCount: 1),
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, level: 8),
                });

            AssertTrue(
                self.ExtraDeck.Any(e => e.CardId == 99105300 && !e.LinkRating.HasValue),
                "fixture Extra Deck LinkRating is null");

            LlmSearchGraph graph = Expand(snap, self);
            AssertTrue(
                !graph.Lines.Any(l =>
                    l != null
                    && LineMentions(l, "Link")
                    && !LineMentions(l, "Synchro")),
                "no Link candidacy while LinkRating is null "
                + "(DEF/level/text must never be treated as link rating)");
            string proj = MiniJSON.Json.Serialize(LlmSearchProjection.Project(graph));
            AssertTrue(
                proj.IndexOf("Potential Link", StringComparison.OrdinalIgnoreCase) < 0
                && proj.IndexOf("link_summon", StringComparison.OrdinalIgnoreCase) < 0
                && proj.IndexOf("Link Summon", StringComparison.OrdinalIgnoreCase) < 0,
                "projection must not invent Link candidates from DEF/level/text");
        }

        // ---------------------------------------------------------------------
        // Review regression A: normal-summon second copy of same CardId
        // ---------------------------------------------------------------------
        static void NormalSummonSecondCopyOfSameCardIdIsDistinctMaterial()
        {
            // Face-up tuner L3 + face-up copy of L2 non-tuner already on field.
            // Root Normal Summons a second copy of the same L2 CardId from hand.
            // Both copies are required: 3+2+2=7 for a Level 7 Synchro.
            const int level7SynchroId = 99105310;
            const string level7SynchroName = "SYNTH_SYNCHRO_L7";
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = ControlledPlayer,
                Position = 13,
                Index = 0,
                CardId = SynthDupNonTunerLevel2Id,
                Card = Meta(
                    SynthDupNonTunerLevel2Id, "SYNTH_DUP_L2", "Effect", "main_deck_monster",
                    level: 2, isTuner: false),
                ActionLabel = "Summon SYNTH_DUP_L2",
            });
            snap.LegalActions.Add(MakeEndRoot(1));

            LlmSelfResources self = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(SynthDupNonTunerLevel2Id, "SYNTH_DUP_L2", 13, true,
                        level: 2, isTuner: false, index: 0),
                },
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(JunkSynchronInternalCardId, JunkSynchronName, 0, true,
                        level: 3, isTuner: true, index: 0),
                    FieldCard(SynthDupNonTunerLevel2Id, "SYNTH_DUP_L2", 1, true,
                        level: 2, isTuner: false, index: 0),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(level7SynchroId, level7SynchroName, level: 7),
                });

            // Production API: post-root materials must include both field copy and root summon.
            List<LlmSynchroMaterial> mats =
                LlmTacticalAffordanceGraph.BuildPostRootFaceUpMaterials(self, snap.LegalActions[0]);
            int dupCount = mats.Count(m => m != null && m.CardId == SynthDupNonTunerLevel2Id);
            AssertEqual(2, dupCount,
                "normal-summon second copy of same CardId must remain a distinct material "
                + "(field copy + root copy); got count=" + dupCount);

            LlmSearchGraph graph = Expand(snap, self);
            LlmSearchLine synchro = FindSynchroContinuation(graph, 0);
            AssertNotNull(synchro,
                "Synchro candidacy requiring two same-CardId non-tuners + tuner must emit");
            string blob = ProjectedSynchroBlob(synchro);
            // Unlock text must distinguish sources (zone/index or root marker), not CardId alone.
            AssertTrue(
                CountOccurrences(blob, "SYNTH_DUP_L2") >= 2
                || CountOccurrences(blob, SynthDupNonTunerLevel2Id.ToString()) >= 2,
                "projection must surface both same-CardId material copies");
            AssertTrue(
                blob.IndexOf("root", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("z:", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("zone", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("idx", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("index", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("src:", StringComparison.OrdinalIgnoreCase) >= 0,
                "material unlocks must carry source identity (zone/index or root marker)");
        }

        // ---------------------------------------------------------------------
        // Review regression B: Reverse promotes only Position+Index target
        // ---------------------------------------------------------------------
        static void ReversePromotesOnlyExactPositionAndIndexTarget()
        {
            // Two face-down tuners with same CardId at zone 1 index 0 and zone 1 index 1.
            // Root Reverse targets index 1 only. Only that copy becomes face-up.
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Reverse,
                Player = ControlledPlayer,
                Position = 1,
                Index = 1,
                CardId = SynthDupTunerLevel3Id,
                Card = Meta(
                    SynthDupTunerLevel3Id, "SYNTH_DUP_TUNER", "Effect", "main_deck_monster",
                    level: 3, isTuner: true),
                ActionLabel = "Flip Summon SYNTH_DUP_TUNER",
            });
            snap.LegalActions.Add(MakeEndRoot(1));

            LlmSelfResources self = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(SynthNonTunerLevel5Id, NonTunerL5Name, 0, true,
                        level: 5, isTuner: false, index: 0),
                    FieldCard(SynthDupTunerLevel3Id, "SYNTH_DUP_TUNER", 1, false,
                        level: 3, isTuner: true, index: 0),
                    FieldCard(SynthDupTunerLevel3Id, "SYNTH_DUP_TUNER", 1, false,
                        level: 3, isTuner: true, index: 1),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, level: 8),
                });

            List<LlmSynchroMaterial> mats =
                LlmTacticalAffordanceGraph.BuildPostRootFaceUpMaterials(self, snap.LegalActions[0]);
            int faceUpDupTuners = mats.Count(m =>
                m != null && m.CardId == SynthDupTunerLevel3Id);
            AssertEqual(1, faceUpDupTuners,
                "Reverse must promote only the exact Position+Index target; got face-up dup tuners="
                + faceUpDupTuners);
            // Source identity must encode index 1 (not the other face-down copy at index 0).
            string matBlob = string.Join(" | ", mats.Where(m => m != null).Select(m => m.IdentityUnlock()));
            AssertTrue(
                matBlob.IndexOf("idx:1", StringComparison.OrdinalIgnoreCase) >= 0
                || matBlob.IndexOf("index:1", StringComparison.OrdinalIgnoreCase) >= 0
                || matBlob.IndexOf("i:1", StringComparison.OrdinalIgnoreCase) >= 0,
                "promoted material unlock must identify root.Index=1; blob=" + matBlob);

            // Fail-closed: wrong index target that cannot be grounded → no arbitrary promotion.
            LegalAction ungrounded = new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Reverse,
                Player = ControlledPlayer,
                Position = 1,
                Index = 9, // no field card at index 9
                CardId = SynthDupTunerLevel3Id,
                Card = Meta(
                    SynthDupTunerLevel3Id, "SYNTH_DUP_TUNER", "Effect", "main_deck_monster",
                    level: 3, isTuner: true),
            };
            List<LlmSynchroMaterial> failClosed =
                LlmTacticalAffordanceGraph.BuildPostRootFaceUpMaterials(self, ungrounded);
            AssertEqual(0, failClosed.Count(m => m != null && m.CardId == SynthDupTunerLevel3Id),
                "ungrounded Reverse target must not promote arbitrary same-CardId face-downs");
        }

        // ---------------------------------------------------------------------
        // Review regression C: duplicate CardId materials remain distinct lines
        // ---------------------------------------------------------------------
        static void DuplicateCardIdMaterialsRemainDistinctCandidateLines()
        {
            // Tuner L3 + two face-up L2 non-tuners with same CardId at distinct zones.
            // Subsets {L2@z1} and {L2@z2} alone do not reach level 8; need both for 3+2+2=7
            // or with a summoned L5... Simpler: face-up tuner L4 + two face-up L2 same id
            // + ED L6 and L8: wait.
            // Use: face-up tuner L3, two face-up L2 same CardId, ED L5 (tuner+one L2) and L7 (tuner+both).
            // Distinct lines: (tuner + z1)→L5, (tuner + z2)→L5, (tuner + z1 + z2)→L7.
            // At minimum two L5 lines must not collapse via CardId-only fingerprints.
            const int level5SynchroId = 99105311;
            const int level7SynchroId = 99105312;
            DecisionSnapshot snap = MakeBaseSnapshot();
            // Setup root: Summon a dummy L1 that does not participate — or Reverse nothing.
            // Use Summon of L1 non-tuner that is NOT needed for L5 lines... Actually use
            // Reverse of face-down L0? Better: Normal Summon tuner when two L2 already face-up.
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = ControlledPlayer,
                Position = 13,
                Index = 0,
                CardId = JunkSynchronInternalCardId,
                Card = Meta(
                    JunkSynchronInternalCardId, JunkSynchronName, "Effect", "main_deck_monster",
                    level: 3, isTuner: true),
                ActionLabel = "Summon " + JunkSynchronName,
            });
            snap.LegalActions.Add(MakeEndRoot(1));

            LlmSelfResources self = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(JunkSynchronInternalCardId, JunkSynchronName, 13, true,
                        level: 3, isTuner: true, index: 0),
                },
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(SynthDupNonTunerLevel2Id, "SYNTH_DUP_L2", 0, true,
                        level: 2, isTuner: false, index: 0),
                    FieldCard(SynthDupNonTunerLevel2Id, "SYNTH_DUP_L2", 1, true,
                        level: 2, isTuner: false, index: 0),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(level5SynchroId, "SYNTH_SYNCHRO_L5", level: 5),
                    ExtraSynchro(level7SynchroId, "SYNTH_SYNCHRO_L7", level: 7),
                });

            LlmSearchGraph graph = Expand(snap, self);
            List<LlmSearchLine> synLines = graph.Lines
                .Where(l => l != null && !l.IsRootShell && l.RootActionId == 0
                    && (LineMentions(l, "Synchro") || (l.LineId != null
                        && l.LineId.IndexOf("synchro", StringComparison.OrdinalIgnoreCase) >= 0)))
                .ToList();
            AssertTrue(synLines.Count >= 2,
                "duplicate same-CardId materials at distinct zones must produce multiple "
                + "distinct Synchro candidate lines; got " + synLines.Count);

            HashSet<string> fps = new HashSet<string>();
            HashSet<string> lineIds = new HashSet<string>();
            foreach (LlmSearchLine line in synLines)
            {
                AssertTrue(!string.IsNullOrEmpty(line.Fingerprint), "fingerprint required");
                AssertTrue(!string.IsNullOrEmpty(line.LineId), "line id required");
                AssertTrue(fps.Add(line.Fingerprint),
                    "duplicate fingerprint collapsed distinct material sets: " + line.Fingerprint);
                AssertTrue(lineIds.Add(line.LineId),
                    "duplicate line id collapsed distinct material sets: " + line.LineId);
                // Projected unlocks must encode source identity beyond bare CardId.
                string blob = ProjectedSynchroBlob(line);
                AssertTrue(
                    blob.IndexOf("z:", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("zone", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("idx", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("index", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("src:", StringComparison.OrdinalIgnoreCase) >= 0
                    || blob.IndexOf("root", StringComparison.OrdinalIgnoreCase) >= 0,
                    "projected materials must include source identity (zone/index/root)");
            }
        }

        // ---------------------------------------------------------------------
        // Review regression D: fewer materials score higher for same Extra Deck target
        // ---------------------------------------------------------------------
        static void ResourceEfficientFewerMaterialsScoresHigherForSameExtra()
        {
            // Face-up tuner L3 + face-up L2 + face-up L3 non-tuner; root Summons nothing needed —
            // root Normal Summons a useless? Actually all three already face-up after root Summon
            // of L0? Use Summon of L3 non-tuner while field has tuner L3 + L2 → materials can
            // form L8 with 2 materials (tuner+L5? no) or 3 (3+2+3).
            // Field: face-up tuner L3, face-up L5 non-tuner. Root Summons L0? 
            // Simpler: field face-up tuner L3 + face-up L2 + face-up L3 non-tuner already.
            // Root is Reverse of a non-material face-down? That adds a 4th material.
            // Use Normal Summon of L3 non-tuner (root) with face-up tuner L3 + face-up L2:
            // Candidates for Stardust L8:
            //   A: tuner(3)+root L3+field L2 = 8 (3 materials)
            // Also need a 2-material path to same ED: face-up L5 already + root? 
            // Field: tuner L3, non-tuner L5 face-up. Root Summons L0 no.
            // Field: tuner L3, L2, L3. Root is End? Not setup.
            // Field: face-up L5 non-tuner. Root Summons tuner L3 → 2 materials for L8.
            // Field: face-up L2 + face-up L3 non-tuners. Root Summons tuner L3 → 3 materials for L8.
            // Single root: Summon tuner L3 onto field with face-up L2 and L3 non-tuners AND
            // also face-up L5? Then both 2-mat (tuner+L5) and 3-mat (tuner+L2+L3) reach 8.
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = ControlledPlayer,
                Position = 13,
                Index = 0,
                CardId = JunkSynchronInternalCardId,
                Card = Meta(
                    JunkSynchronInternalCardId, JunkSynchronName, "Effect", "main_deck_monster",
                    level: 3, isTuner: true),
                ActionLabel = "Summon " + JunkSynchronName,
            });
            snap.LegalActions.Add(MakeEndRoot(1));

            LlmSelfResources self = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(JunkSynchronInternalCardId, JunkSynchronName, 13, true,
                        level: 3, isTuner: true, index: 0),
                },
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(SynthNonTunerLevel5Id, NonTunerL5Name, 0, true,
                        level: 5, isTuner: false, index: 0),
                    FieldCard(SynthNonTunerLevel2Id, "SYNTH_L2_NONTUNER", 1, true,
                        level: 2, isTuner: false, index: 0),
                    FieldCard(SynthNonTunerLevel3Id, NonTunerL3Name, 2, true,
                        level: 3, isTuner: false, index: 0),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    ExtraSynchro(StardustDragonInternalCardId, StardustName, level: 8),
                });

            LlmSearchGraph graph = Expand(snap, self);
            List<LlmSearchLine> stardustLines = graph.Lines
                .Where(l => l != null && !l.IsRootShell && l.RootActionId == 0
                    && ProjectedSynchroBlob(l).IndexOf(
                        StardustDragonInternalCardId.ToString(), StringComparison.Ordinal) >= 0)
                .ToList();
            AssertTrue(stardustLines.Count >= 2,
                "need both 2-material and 3-material paths to same Extra Deck Synchro");

            LlmSearchLine twoMat = null;
            LlmSearchLine threeMat = null;
            foreach (LlmSearchLine line in stardustLines)
            {
                int matCount = CountMaterialUnlocks(line);
                if (matCount == 2)
                {
                    twoMat = line;
                }
                else if (matCount == 3)
                {
                    threeMat = line;
                }
            }
            AssertNotNull(twoMat, "2-material Stardust line required");
            AssertNotNull(threeMat, "3-material Stardust line required");
            AssertTrue(twoMat.Score > threeMat.Score,
                "resource-efficient 2-material line must score strictly higher than 3-material "
                + "for the same Extra Deck target; got 2mat=" + twoMat.Score
                + " 3mat=" + threeMat.Score);

            AssertTrue(
                twoMat.ScoreFeatures != null
                && twoMat.ScoreFeatures.Any(p =>
                    p.Key != null
                    && p.Key.IndexOf("resource", StringComparison.OrdinalIgnoreCase) >= 0
                    && p.Value < 0),
                "deterministic negative resource-cost score feature required on Synchro lines");
            AssertTrue(
                threeMat.ScoreFeatures != null
                && threeMat.ScoreFeatures.Any(p =>
                    p.Key != null
                    && p.Key.IndexOf("resource", StringComparison.OrdinalIgnoreCase) >= 0
                    && p.Value < 0),
                "3-material line must also expose negative resource-cost feature");
            int cost2 = twoMat.ScoreFeatures
                .Where(p => p.Key != null
                    && p.Key.IndexOf("resource", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.Value).First();
            int cost3 = threeMat.ScoreFeatures
                .Where(p => p.Key != null
                    && p.Key.IndexOf("resource", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.Value).First();
            AssertTrue(cost3 < cost2,
                "more materials must incur a more-negative resource cost feature "
                + "(cost2=" + cost2 + " cost3=" + cost3 + ")");
            AssertTrue(twoMat.Score > 0 && threeMat.Score > 0,
                "setup/unlock value remains positive overall despite resource penalty");
        }

        // ---------------------------------------------------------------------
        // 6. Existing Xyz behavior / root completeness undisturbed
        // ---------------------------------------------------------------------
        static void ExistingXyzRootCompletenessUndisturbed()
        {
            DecisionSnapshot snap = MakeBaseSnapshot();
            snap.LegalActions.Add(new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Reverse,
                CardId = CaveDragonInternalCardId,
                Card = Meta(CaveDragonInternalCardId, "The Dragon Dwelling in the Cave", "Effect",
                    "main_deck_monster", level: 4, isTuner: false),
                ActionLabel = "Flip Summon The Dragon Dwelling in the Cave",
            });
            snap.LegalActions.Add(MakeBattleRoot(1));
            snap.LegalActions.Add(MakeEndRoot(2));
            snap.StrategicActionCount = 3;

            LlmSelfResources self = LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(GirochinInternalCardId, "Girochin Kuwagata", 0, true,
                        level: 4, isTuner: false),
                    FieldCard(CaveDragonInternalCardId, "The Dragon Dwelling in the Cave", 1, false,
                        level: 4, isTuner: false),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceExtraDeckEntry>()
                {
                    LlmSelfResourceExtraDeckEntry.Create(
                        UtopiaInternalCardId, UtopiaName, "Xyz", "xyz",
                        level: null, rank: 4, usesRank: true, linkRating: null,
                        isTuner: false, kind: "Xyz", atk: 2500, def: 2000, text: null,
                        duplicateCount: 1),
                });

            LlmSearchGraph graph = Expand(snap, self);
            AssertRootCoverage(graph, 3, "xyz root completeness under Slice 2C");
            AssertTrue(
                graph.Lines.Any(l =>
                    l != null
                    && l.RootActionId == 0
                    && !l.IsRootShell
                    && l.Provenance == "rules_inferred"
                    && !l.CommitEligible
                    && LineMentions(l, "Rank 4")),
                "existing Rank 4 Xyz candidacy must remain");
            AssertTrue(
                graph.Lines.Count(l => l != null && l.IsRootShell) == 3,
                "three root shells retained");
        }

        // =====================================================================
        // Assertion helpers (exact identity / score / root-scoped reasons)
        // =====================================================================

        static void AssertFutureOnlySynchroLine(LlmSearchLine synchro, string context)
        {
            AssertEqual(false, synchro.CommitEligible,
                context + ": Synchro candidate line must not be commit-eligible as a whole");
            AssertEqual("rules_inferred", synchro.Provenance, context + ": provenance");
            AssertTrue(!synchro.IsRootShell, context + ": not a root shell");
            AssertTrue(
                synchro.Steps != null
                && synchro.Steps.Any(s =>
                    s != null
                    && !s.CurrentLegal
                    && !s.CommitEligible
                    && s.Label != null
                    && s.Label.IndexOf("Synchro", StringComparison.OrdinalIgnoreCase) >= 0),
                context + ": future-only Synchro step CurrentLegal=false CommitEligible=false");
        }

        /// <summary>
        /// Exact identity only: name OR card id string. Generic words ("tuner", "Synchro",
        /// "level-sum") alone do NOT satisfy these asserts.
        /// </summary>
        static void AssertExactMaterialAndExtraIdentities(
            LlmSearchLine line,
            string tunerName,
            int tunerId,
            string nonTunerName,
            int nonTunerId,
            string extraName,
            int extraId)
        {
            string blob = ProjectedSynchroBlob(line);
            AssertExactIdentityPresent(blob, tunerName, tunerId, "tuner material");
            AssertExactIdentityPresent(blob, nonTunerName, nonTunerId, "non-tuner material");
            AssertExactIdentityPresent(blob, extraName, extraId, "Extra Deck Synchro");
        }

        static void AssertExactIdentityPresent(
            string blob, string name, int cardId, string role)
        {
            bool byName = !string.IsNullOrEmpty(name)
                && blob.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
            bool byId = blob.IndexOf(cardId.ToString(), StringComparison.Ordinal) >= 0;
            AssertTrue(byName || byId,
                role + ": exact identity required (name='" + name + "' or card_id=" + cardId
                + "); generic words alone are insufficient. blob=" + Truncate(blob, 400));
        }

        static void AssertNonzeroSetupUnlockScore(LlmSearchLine line, string context)
        {
            // Accept either ScoreFeatures setup/unlock keys or a strictly positive line Score
            // above a bare root-shell baseline, with future steps still non-current.
            bool featureHit = false;
            if (line.ScoreFeatures != null)
            {
                foreach (KeyValuePair<string, int> pair in line.ScoreFeatures)
                {
                    if (pair.Value <= 0 || pair.Key == null)
                    {
                        continue;
                    }
                    string k = pair.Key;
                    if (k.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0
                        || k.IndexOf("unlock", StringComparison.OrdinalIgnoreCase) >= 0
                        || k.IndexOf("synchro", StringComparison.OrdinalIgnoreCase) >= 0
                        || k.IndexOf("candidacy", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        featureHit = true;
                        break;
                    }
                }
            }
            bool scoreHit = line.Score > 0;
            AssertTrue(featureHit || scoreHit,
                context + ": deterministic nonzero setup/unlock score feature or positive "
                + "line Score required (got Score=" + line.Score + ")");
            // Future steps remain non-current / non-commit-eligible.
            AssertTrue(
                line.Steps != null
                && line.Steps.Any(s =>
                    s != null
                    && s.Label != null
                    && s.Label.IndexOf("Synchro", StringComparison.OrdinalIgnoreCase) >= 0
                    && !s.CurrentLegal
                    && !s.CommitEligible),
                context + ": score evidence must not make Synchro step current/commit-eligible");
        }

        static void AssertNoSynchroWithRootScopedReason(
            LegalAction setupRoot,
            LlmSelfResources self,
            string reasonHint,
            string context)
        {
            DecisionSnapshot snap = MakeBaseSnapshot();
            setupRoot.ActionId = 0;
            snap.LegalActions.Add(setupRoot);
            // Second strategic root that must NOT supply the reason for root 0.
            snap.LegalActions.Add(MakeEndRoot(1));
            LlmSearchGraph graph = Expand(snap, self);
            AssertTrue(FindSynchroContinuation(graph, 0) == null,
                context + ": no Synchro edge expected");
            AssertTrue(
                HasRootScopedSynchroNoCandidateReason(graph, 0)
                || HasRootScopedReasonHint(graph, 0, reasonHint),
                context + ": root-scoped explicit unsupported/no-candidate reason required "
                + "(hint='" + reasonHint + "'); reasons from other roots/generic graph text "
                + "do not satisfy");
        }

        /// <summary>
        /// Reason must be attached to the tested root shell (or a root-tagged coverage
        /// exclusion for that root id). Global Coverage lists alone are insufficient.
        /// </summary>
        static bool HasRootScopedSynchroNoCandidateReason(LlmSearchGraph graph, int rootActionId)
        {
            if (graph == null || graph.Lines == null)
            {
                return false;
            }
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line == null || line.RootActionId != rootActionId || !line.IsRootShell)
                {
                    continue;
                }
                if (LooksLikeSynchroNoCandidate(line.NoCandidateReason)
                    || LooksLikeSynchroNoCandidate(line.ExclusionReason)
                    || LooksLikeSynchroNoCandidate(line.NonExpansionReason)
                    || LooksLikeSynchroNoCandidate(line.Boundary))
                {
                    return true;
                }
                if (line.Uncertainty != null
                    && line.Uncertainty.Any(LooksLikeSynchroNoCandidate))
                {
                    return true;
                }
            }
            if (graph.Coverage != null && graph.Coverage.RootExclusions != null)
            {
                string tag = "root:" + rootActionId + ":";
                string tag2 = "action:" + rootActionId + ":";
                foreach (string r in graph.Coverage.RootExclusions)
                {
                    if (r == null)
                    {
                        continue;
                    }
                    if ((r.IndexOf(tag, StringComparison.Ordinal) >= 0
                            || r.IndexOf(tag2, StringComparison.Ordinal) >= 0)
                        && LooksLikeSynchroNoCandidate(r))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static bool HasRootScopedReasonHint(
            LlmSearchGraph graph, int rootActionId, string hint)
        {
            if (graph == null || string.IsNullOrEmpty(hint))
            {
                return false;
            }
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line == null || line.RootActionId != rootActionId || !line.IsRootShell)
                {
                    continue;
                }
                // Must be synchro-related AND contain the hint — not a Rank4/other reason.
                if ((LooksLikeSynchroNoCandidate(line.NoCandidateReason)
                        && ContainsHint(line.NoCandidateReason, hint))
                    || (LooksLikeSynchroNoCandidate(line.ExclusionReason)
                        && ContainsHint(line.ExclusionReason, hint))
                    || (LooksLikeSynchroNoCandidate(line.Boundary)
                        && ContainsHint(line.Boundary, hint))
                    || (LooksLikeSynchroNoCandidate(line.NonExpansionReason)
                        && ContainsHint(line.NonExpansionReason, hint)))
                {
                    return true;
                }
                if (line.Uncertainty != null)
                {
                    foreach (string u in line.Uncertainty)
                    {
                        if (LooksLikeSynchroNoCandidate(u) && ContainsHint(u, hint))
                        {
                            return true;
                        }
                    }
                }
            }
            if (graph.Coverage != null && graph.Coverage.RootExclusions != null)
            {
                string tag = "root:" + rootActionId + ":";
                string tag2 = "action:" + rootActionId + ":";
                foreach (string r in graph.Coverage.RootExclusions)
                {
                    if (r != null
                        && (r.IndexOf(tag, StringComparison.Ordinal) >= 0
                            || r.IndexOf(tag2, StringComparison.Ordinal) >= 0)
                        && LooksLikeSynchroNoCandidate(r)
                        && ContainsHint(r, hint))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static bool LooksLikeSynchroNoCandidate(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            return s.IndexOf("synchro", StringComparison.OrdinalIgnoreCase) >= 0
                && (s.IndexOf("no_candidate", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("no_tuner", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("tuner", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("extra", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("level", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static bool ContainsHint(string s, string hint)
        {
            return !string.IsNullOrEmpty(s)
                && s.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string ProjectedSynchroBlob(LlmSearchLine line)
        {
            // Project a single-line graph so ScoreFeatures / Unlocks / Steps serialize.
            Dictionary<string, object> proj = LlmSearchProjection.Project(new LlmSearchGraph()
            {
                SearchId = "slice2c-line",
                Status = "budget_complete",
                Lines = new List<LlmSearchLine>() { line },
                Coverage = new LlmSearchCoverage()
                {
                    LegalRootActions = 1,
                    RepresentedRootActions = 1,
                    RootShellCount = line.IsRootShell ? 1 : 0,
                },
            });
            // Also append raw unlocks/uncertainty/scorefeatures for identity search.
            string json = MiniJSON.Json.Serialize(proj);
            if (line.Unlocks != null)
            {
                foreach (string u in line.Unlocks)
                {
                    json += "\n" + u;
                }
            }
            if (line.Uncertainty != null)
            {
                foreach (string u in line.Uncertainty)
                {
                    json += "\n" + u;
                }
            }
            if (line.ScoreFeatures != null)
            {
                foreach (KeyValuePair<string, int> p in line.ScoreFeatures)
                {
                    json += "\n" + p.Key + "=" + p.Value;
                }
            }
            if (line.Steps != null)
            {
                foreach (LlmSearchStep s in line.Steps)
                {
                    if (s != null && s.Label != null)
                    {
                        json += "\n" + s.Label;
                    }
                }
            }
            if (line.LineId != null)
            {
                json += "\n" + line.LineId;
            }
            return json;
        }

        static int FirstIdentityIndex(string blob, string name, int cardId)
        {
            int byName = string.IsNullOrEmpty(name)
                ? -1
                : blob.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            int byId = blob.IndexOf(cardId.ToString(), StringComparison.Ordinal);
            if (byName < 0)
            {
                return byId;
            }
            if (byId < 0)
            {
                return byName;
            }
            return Math.Min(byName, byId);
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
            {
                return s;
            }
            return s.Substring(0, max) + "...";
        }

        // =====================================================================
        // Graph helpers
        // =====================================================================

        static LlmSearchLine FindSynchroContinuation(LlmSearchGraph graph, int rootActionId)
        {
            if (graph == null || graph.Lines == null)
            {
                return null;
            }
            foreach (LlmSearchLine line in graph.Lines)
            {
                if (line == null || line.IsRootShell || line.RootActionId != rootActionId)
                {
                    continue;
                }
                if (LineMentions(line, "Synchro") || LineMentions(line, "synchro"))
                {
                    return line;
                }
                if (line.LineId != null
                    && line.LineId.IndexOf("synchro", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return line;
                }
            }
            return null;
        }

        static bool LineMentions(LlmSearchLine line, string fragment)
        {
            if (line == null || string.IsNullOrEmpty(fragment))
            {
                return false;
            }
            if (line.LineId != null
                && line.LineId.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (line.Steps != null)
            {
                foreach (LlmSearchStep s in line.Steps)
                {
                    if (s != null && s.Label != null
                        && s.Label.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            if (line.Unlocks != null)
            {
                foreach (string u in line.Unlocks)
                {
                    if (u != null
                        && u.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static void AssertRootCoverage(LlmSearchGraph graph, int strategicRoots, string context)
        {
            AssertNotNull(graph, context + ": graph");
            AssertNotNull(graph.Coverage, context + ": coverage");
            AssertEqual(strategicRoots, graph.Coverage.RepresentedRootActions,
                context + ": represented_root_actions");
            AssertEqual(strategicRoots, graph.Lines.Count(l => l != null && l.IsRootShell),
                context + ": root shells");
        }

        static LlmSearchGraph Expand(DecisionSnapshot snap, LlmSelfResources self)
        {
            LlmSearchLimits limits = LlmSearchLimits.CreateDefault();
            LlmSearchGraph graph = LlmTacticalAffordanceGraph.Build(snap, self, limits);
            return LlmBoundedLineSearch.Search(graph, limits);
        }

        static DecisionSnapshot MakeBaseSnapshot()
        {
            return new DecisionSnapshot()
            {
                RunEffectSeq = 820,
                ViewType = DuelViewType.WaitInput,
                ControlledPlayer = ControlledPlayer,
                ActingPlayer = ControlledPlayer,
                Turn = 3,
                TurnPlayer = ControlledPlayer,
                CurrentPhase = (int)DuelPhase.Main1,
                IsStrategicWindow = true,
                StrategicWindowReason = "strategic_actions",
            };
        }

        static LegalAction MakeEndRoot(int actionId)
        {
            return new LegalAction()
            {
                ActionId = actionId,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.End,
                ActionLabel = "End Phase",
                IsMechanical = false,
                StrategicRole = "phase_timing",
            };
        }

        static LegalAction MakeBattleRoot(int actionId)
        {
            return new LegalAction()
            {
                ActionId = actionId,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
                ActionLabel = "Enter Battle Phase",
                IsMechanical = false,
                StrategicRole = "phase_timing",
            };
        }

        static LegalAction MakeSummonRoot(int cardId, string name, int level, bool isTuner)
        {
            return new LegalAction()
            {
                ActionId = 0,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                Player = ControlledPlayer,
                CardId = cardId,
                Card = Meta(cardId, name, "Effect", "main_deck_monster", level, isTuner),
                ActionLabel = "Summon " + name,
                IsMechanical = false,
                StrategicRole = "board_development",
            };
        }

        static LlmSelfResources MakeSynchroReadySelf(
            int faceUpTunerLevel,
            int faceUpTunerId,
            string faceUpTunerName,
            bool includeLevel5NonTunerInHand,
            bool includeLevel8SynchroExtra,
            bool includeRank4XyzExtra)
        {
            List<LlmSelfResourceCard> hand = new List<LlmSelfResourceCard>();
            if (includeLevel5NonTunerInHand)
            {
                hand.Add(FieldCard(
                    SynthNonTunerLevel5Id, NonTunerL5Name, 13, true,
                    level: 5, isTuner: false));
            }
            List<LlmSelfResourceExtraDeckEntry> extra = new List<LlmSelfResourceExtraDeckEntry>();
            if (includeLevel8SynchroExtra)
            {
                extra.Add(ExtraSynchro(StardustDragonInternalCardId, StardustName, level: 8));
            }
            if (includeRank4XyzExtra)
            {
                extra.Add(LlmSelfResourceExtraDeckEntry.Create(
                    UtopiaInternalCardId, UtopiaName, "Xyz", "xyz",
                    level: null, rank: 4, usesRank: true, linkRating: null,
                    isTuner: false, kind: "Xyz", atk: 2500, def: 2000, text: null,
                    duplicateCount: 1));
            }
            return LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                hand,
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(
                        faceUpTunerId, faceUpTunerName, 0, true,
                        level: faceUpTunerLevel, isTuner: true),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                extra);
        }

        static LlmSelfResources MakeFieldSelf(
            int tunerId,
            string tunerName,
            int tunerLevel,
            int extraSynchroLevel,
            bool tunerIsTuner = true)
        {
            List<LlmSelfResourceExtraDeckEntry> extra = new List<LlmSelfResourceExtraDeckEntry>();
            if (extraSynchroLevel > 0)
            {
                extra.Add(ExtraSynchro(
                    StardustDragonInternalCardId, StardustName, level: extraSynchroLevel));
            }
            return LlmSelfResources.Create(
                ControlledPlayer,
                LlmPublicVisibilityMode.RuntimeDllField,
                3,
                (int)DuelPhase.Main1,
                0,
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>()
                {
                    FieldCard(tunerId, tunerName, 0, true, level: tunerLevel, isTuner: tunerIsTuner),
                },
                new List<LlmSelfResourceCard>(),
                new List<LlmSelfResourceCard>(),
                extra);
        }

        static LlmSelfResourceCard FieldCard(
            int cardId,
            string name,
            int zone,
            bool faceUp,
            int level,
            bool isTuner,
            int def = 0,
            string text = null,
            int index = 0)
        {
            return LlmSelfResourceCard.Create(
                cardId,
                name,
                zone,
                index,
                faceUp ? RuntimeFaceUp : RuntimeFaceDown,
                faceUp,
                level > 0 ? (int?)level : null,
                null,
                false,
                isTuner,
                null,
                "Effect",
                "main_deck_monster",
                false,
                "Effect",
                1500,
                def,
                text,
                0);
        }

        static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
            {
                return 0;
            }
            int count = 0;
            int idx = 0;
            while (idx < haystack.Length)
            {
                int found = haystack.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    break;
                }
                count++;
                idx = found + needle.Length;
            }
            return count;
        }

        static int CountMaterialUnlocks(LlmSearchLine line)
        {
            if (line == null || line.Unlocks == null)
            {
                return 0;
            }
            int n = 0;
            foreach (string u in line.Unlocks)
            {
                if (u != null
                    && u.IndexOf("synchro material:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    n++;
                }
            }
            return n;
        }

        static LlmSelfResourceExtraDeckEntry ExtraSynchro(int cardId, string name, int level)
        {
            return LlmSelfResourceExtraDeckEntry.Create(
                cardId,
                name,
                "Synchro",
                "synchro",
                level,
                null,
                false,
                null,
                false,
                "Synchro",
                2500,
                2000,
                null,
                1);
        }

        static LlmCardMetadata Meta(
            int cardId,
            string name,
            string kind,
            string summonFamily,
            int level,
            bool isTuner)
        {
            return new LlmCardMetadata()
            {
                CardId = cardId,
                Name = name,
                Kind = kind,
                Frame = "Effect",
                SummonFamily = summonFamily,
                Level = level,
                IsTuner = isTuner,
                LinkRating = null,
            };
        }

        static void PlantOpponentHidden(
            DecisionSnapshot snap,
            int handId,
            string handName,
            int setId,
            string setName,
            int extraId,
            string extraName)
        {
            PublicPlayerState opp = null;
            foreach (PublicPlayerState p in snap.PublicState.Players)
            {
                if (p.Player == OpponentPlayer)
                {
                    opp = p;
                    break;
                }
            }
            if (opp == null)
            {
                opp = new PublicPlayerState() { Player = OpponentPlayer, LifePoints = 8000 };
                snap.PublicState.Players.Add(opp);
            }
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = 13,
                Index = 0,
                CardId = handId,
                Face = RuntimeFaceDown,
                Card = new LlmCardMetadata() { CardId = handId, Name = handName },
            });
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = 5,
                Index = 0,
                CardId = setId,
                Face = RuntimeFaceDown,
                Card = new LlmCardMetadata() { CardId = setId, Name = setName },
            });
            opp.KnownCards.Add(new PublicKnownCard()
            {
                Player = OpponentPlayer,
                Position = 14,
                Index = 0,
                CardId = extraId,
                Face = RuntimeFaceUp,
                Card = new LlmCardMetadata() { CardId = extraId, Name = extraName },
            });
        }

        static string SerializeGraph(LlmSearchGraph graph)
        {
            return MiniJSON.Json.Serialize(LlmSearchProjection.Project(graph));
        }

        static void AssertNoLeak(string json, params object[] forbidden)
        {
            foreach (object item in forbidden)
            {
                if (item == null)
                {
                    continue;
                }
                string token = item.ToString();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }
                AssertFalse(
                    json != null && json.Contains(token),
                    "Slice 2C graph/projection leaks " + token);
            }
        }

        static void AssertTrue(bool v, string m)
        {
            if (!v)
            {
                throw new Exception(m);
            }
        }

        static void AssertFalse(bool v, string m)
        {
            if (v)
            {
                throw new Exception(m);
            }
        }

        static void AssertNotNull(object v, string m)
        {
            if (v == null)
            {
                throw new Exception(m + ": expected non-null");
            }
        }

        static void AssertEqual<T>(T expected, T actual, string m)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(m + ": expected " + expected + ", got " + actual);
            }
        }
    }
}

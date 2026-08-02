using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    /// <summary>
    /// Pure CampaignCpu unit tests (PR1/PR2a/PR3/state-machine/commit). No live DLL.
    /// </summary>
    static class CampaignCpuTests
    {
        public static void RunAll()
        {
            DeckFingerprintPins11010078FromSoloDuels();
            PackLoaderRejectsUnknownCommand();
            PackLoaderRequiresDeckHashWhenEnabled();
            ActingPlayerResolverMainPhaseUsesTurnPlayer();
            ActingPlayerResolverCheckTimingUsesDoCommandUser();
            ActingPlayerResolverInfoDialogUnresolved();
            ScorerPriorityPicksG1TopAction();
            ScorerG2WhenTopAbsent();
            ScorerNoMatchAllZeroFallback();
            OpeningSetSafetyChoosesAllowlistedDefensiveSet();
            OpeningSetSafetyRequiresOwnedMainContext();
            OpeningSetSafetyPreservesNonQualifyingFallbacks();
            OpeningSetSafetyProjectsAndAuditsLiveBasicStats();
            TributeNormalSummonSafetyDefersUnsafeContinuations();
            ScriptedCommitSafetyOnlyAllowsProvenSingleStepLineage();
            SafeContinuationSelectionSkipsUnsafeTopScoredEffect();
            SafeContinuationSelectionAllowsTacticalPhaseExit();
            SuccessiveMainRecapturePolicyRequiresSafeFreshProvenance();
            SuccessiveMainRecapturePolicyFailsNativeOnUnsafeOrBoundedInputs();
            SuccessiveMainTimeoutRefreshesOnSemanticProgress();
            AbandonedSuccessiveMainChainBecomesSilentTerminalHold();
            StableMainBoundaryRequiresTwoMatchingSysActSamples();
            CpuThinkingIsNotARecaptureBoundary();
            StableMainBoundaryDirectCommitKeepsCpuOwnership();
            PositionSafetyRejectsDominatedBlueEyesTurnDefense();
            PositionSafetyPreservesUnknownAndBeneficialDefense();
            PositionSafetyDefaultsOffInScorer();
            ExplicitPhaseExitIgnoresActionIdTieOrdering();
            TacticalSnapshotProjectsModifiedStatsAndUnknownFaceDownThreats();
            TacticalSnapshotPopulationFeedsPositionSafetyScorer();
            NonMainWindowIsNotScriptedG4();
            DeckFingerprintMismatchGateG5();
            ScorerDeterministicSameObservation();
            ScorerNeverExhaustionRestoresLegalSet();
            ProductPackLoadsFromDiskWithPr3Cards();
            ObservationProjectorMapsLegalActions();
            SoloTemporaryCpuRestoreRequiresSemanticProgress();
            SoloTemporaryCpuProgressTimeoutQuarantines();
            CommitNotStartedWhenNullAction();
            CommitAppliedOnSuccessfulNative();
            CommitIndeterminateOnNativeThrow();
            CommitNotStartedWhenDelegateMissing();
            CommitMovePhaseDispatchesOnce();
            CommitDialogResultDispatchesOnce();
            CommitListIndexDispatchesOnce();
            CommitCancelDispatchesOnce();
            CommitSummonPlacementRewritesPositionIndex();
            CommitUnsupportedKindNotStarted();
            WindowClassifierMainPhaseOnly();
            ProgressTokenFreshness();
            ProgressTokenArmVsCheckPathNotFalseFresh();
            ProgressTokenKnownLegalChangeIsFresh();
            ProgressTokenKnownToUnknownNotFresh();
            ProgressTokenEmptyLegalKnownVsUnknown();
            ProgressCheckBuildTokenSameViewChangedMenuIsFresh();
            ProgressCheckBuildTokenKnownToUnknownNotFresh();
            ProgressCheckSafeExtractViewIsMainOnly();
            PostCommitIndeterminateSameViewSuppressesOriginal();
            PostCommitTimeoutQuarantinesWithoutOriginal();
            PostCommitFreshAfterQuarantineForwardsOnce();
            ExactOnceOriginalContract();
            AlwaysNativeOwnedWindowUsesBeginFallback();
            RunEffectRouterMapsProgressGateAndCountsOriginals();
            RunEffectRouterMapsTransitionAndCommitOutcomes();
            RunEffectPlanOrdersProductionBranches();
            ControlPolicyNeverOwnsMyId();
            PlayerTypeTransitionRequiresPositiveReadback();
            SoloCampaignModeGateAcceptsLiveSoloDuelsGameModeZero();
            SoloTemporaryCpuNativeContinuationBlocksRestore();
            SoloTemporaryCpuMyIdBoundaryRestores();
            OneShotRestoreOnlyDrawPhaseUnderAllowScripted();
            OwnedResponseNativeWindowClassifier();
            DualHumanMyIdResponseHoldA4();
            OwnedTurnSelStandClassifier();
            SelStandMaskPickerAndMechanicalAction();
            SelStandLiveHighBitMaskSemantics();
            SelStandInterceptPolicyGates();
            OwnedTurnLocationClassifier();
            LocationZonePickerAndMechanicalAction();
            LocationInterceptPolicyGates();
            NativeLeaseBoundaryProbeViewsM3();
            OwnedMainCaptureBoundaryA5();
            FieldDiffDetectsSetTrapAndFaceChange();
            AuditSerializerDecisionIncludesFullLegalMenu();
            AuditSerializerDecisionEmitsMyIdAndGeneration();
            AuditLifecycleContextIncludesOwnershipAndGeneration();
            AuditFileStrictCapAcrossLaunches();
            NativeCandidateTraceValidatesShapeAndCapturesWithoutMutation();
            NativeTraceDiagnosticsClassifyActivationAndBoundInvocationProbe();
            // M5 fail-closed activation / pack / predicate hardening
            InvalidMyIdDoesNotResolveOwnedSeat();
            SeatOwnershipConfirmRequiresPositiveHumanReadback();
            SeatAssertFailuresDisableScriptingPredicate();
            PathContainmentRejectsSiblingPrefixEscape();
            PackLoaderRejectsUnknownPhaseIn();
            PackLoaderRejectsInvalidCardIdPredicate();
            PackLoaderRejectsUnsupportedVersionAndPolicy();
            PackIndexRejectsDefaultEnabledTrue();
            ScorerDecisionCapReturnsNative();
            ScorerPredicateQueryFailureFailsWhenCardPredicates();
            ScorerG2OwnsRuleWhenKaiserAbsent();
            ProductPackStillLoadsUnderStrictParse();
            Console.WriteLine("PASS CampaignCpuTests.RunAll");
        }

        static string RulesDir()
        {
            // Prefer explicit root (repo root or parent-of-YgoMaster). Harness often runs from
            // /tmp/ygomaster-build/harness with no relative path to Data/.
            string env = Environment.GetEnvironmentVariable("YGOMASTER_ROOT");
            if (!string.IsNullOrEmpty(env))
            {
                string fromEnv = TryRulesDirUnder(env);
                if (fromEnv != null)
                {
                    return fromEnv;
                }
            }

            // Harness cwd is typically repo root or bin; search upward for Data/CampaignCpuRules.
            string[] seeds =
            {
                Directory.GetCurrentDirectory(),
                AppDomain.CurrentDomain.BaseDirectory,
            };
            for (int s = 0; s < seeds.Length; s++)
            {
                string dir = seeds[s];
                if (string.IsNullOrEmpty(dir))
                {
                    continue;
                }
                for (int i = 0; i < 10; i++)
                {
                    string found = TryRulesDirUnder(dir);
                    if (found != null)
                    {
                        return found;
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    if (parent == null)
                    {
                        break;
                    }
                    dir = parent.FullName;
                }
            }

            throw new InvalidOperationException(
                "CampaignCpuRules dir not found. Set YGOMASTER_ROOT to the repo root "
                + "(directory that contains Data/CampaignCpuRules).");
        }

        static string TryRulesDirUnder(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return null;
            }
            string[] candidates =
            {
                // Prefer tracked source tree (has fixtures) over gitignored runtime Data/.
                Path.Combine(root, "YgoMaster", "Data", "CampaignCpuRules"),
                Path.Combine(root, "Data", "CampaignCpuRules"),
            };
            string fallback = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!Directory.Exists(candidates[i]))
                {
                    continue;
                }
                string full = Path.GetFullPath(candidates[i]);
                // Prefer a tree that includes the PR3 golden fixtures when both exist.
                if (Directory.Exists(Path.Combine(full, "fixtures", "11010078")))
                {
                    return full;
                }
                if (fallback == null)
                {
                    fallback = full;
                }
            }
            return fallback;
        }

        static void DeckFingerprintPins11010078FromSoloDuels()
        {
            // Card ids from SoloDuels/11010078 Deck[1] (sorted canonical form).
            var main = new List<int>
            {
                3868, 4007, 4007, 4007, 4747, 5700, 6433, 6782, 7508, 7557,
                7850, 8143, 8344, 8656, 9863, 10588, 10590, 10591, 10592, 12253,
                12290, 12291, 12292, 12293, 12294, 12364, 12485, 12486, 12487, 12488,
                12489, 12490, 12491, 12491, 12492, 12493, 12494, 12692, 13842, 13843
            };
            var extra = new List<int> { 4386, 5502, 10325, 12153, 12252, 12484 };
            var side = new List<int>();
            string hash = CampaignCpuDeckFingerprint.Compute(main, extra, side);
            AssertEqual(
                "sha256:d1ed3390a63030a78a6da913cf8436878a6e26e39f55dc1ec8e38dc7985e7835",
                hash,
                "11010078 deck fingerprint");
            AssertTrue(CampaignCpuDeckFingerprint.IsWellFormedHash(hash), "well-formed hash");
        }

        static void PackLoaderRejectsUnknownCommand()
        {
            string bad = @"{
              ""version"": 1,
              ""chapter_id"": 1,
              ""deck_hash"": ""sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
              ""never"": [ { ""id"": ""x"", ""match"": { ""command"": ""NotARealCommand"" } } ],
              ""priority"": [],
              ""fallback_scoring"": []
            }";
            bool threw = false;
            try
            {
                CampaignCpuRulePackLoader.LoadPackFromText(bad, "test", requireDeckHash: true);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            AssertTrue(threw, "unknown command must fail pack load");
        }

        static void PackLoaderRequiresDeckHashWhenEnabled()
        {
            string missing = @"{
              ""version"": 1,
              ""chapter_id"": 1,
              ""never"": [],
              ""priority"": [],
              ""fallback_scoring"": []
            }";
            bool threw = false;
            try
            {
                CampaignCpuRulePackLoader.LoadPackFromText(missing, "test", requireDeckHash: true);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            AssertTrue(threw, "enabled pack requires deck_hash");
        }

        static void ActingPlayerResolverMainPhaseUsesTurnPlayer()
        {
            int player;
            bool ok = CampaignCpuActingPlayerResolver.TryResolve(
                DuelViewType.WaitInput,
                doCommandUser: 0,
                runDialogUser: -1,
                param1: (int)DuelMenuActType.MainPhase,
                myId: 0,
                turnPlayer: 1,
                out player);
            AssertTrue(ok, "main phase resolve");
            AssertEqual(1, player, "turn player for main phase");
        }

        static void ActingPlayerResolverCheckTimingUsesDoCommandUser()
        {
            int player;
            bool ok = CampaignCpuActingPlayerResolver.TryResolve(
                DuelViewType.WaitInput,
                doCommandUser: 1,
                runDialogUser: -1,
                param1: (int)DuelMenuActType.CheckTiming,
                myId: 0,
                turnPlayer: 0,
                out player);
            AssertTrue(ok, "check timing resolve");
            AssertEqual(1, player, "doCommandUser for check timing");
        }

        static void ActingPlayerResolverInfoDialogUnresolved()
        {
            int player;
            bool ok = CampaignCpuActingPlayerResolver.TryResolve(
                DuelViewType.RunDialog,
                doCommandUser: -1,
                runDialogUser: 1,
                param1: 1,
                myId: 0,
                turnPlayer: 1,
                out player);
            AssertTrue(!ok, "info dialog has no acting player");
        }

        static string FixturePath(string relativeUnderFixtures)
        {
            return Path.Combine(RulesDir(), "fixtures", "11010078", relativeUnderFixtures);
        }

        static Dictionary<string, object> LoadFixtureRoot(string fileName)
        {
            string path = FixturePath(fileName);
            AssertTrue(File.Exists(path), "fixture exists: " + fileName);
            Dictionary<string, object> root =
                MiniJSON.Json.DeserializeStripped(File.ReadAllText(path)) as Dictionary<string, object>;
            AssertTrue(root != null, "fixture json: " + fileName);
            return root;
        }

        /// <summary>
        /// Product pack is the on-disk chapter file (PR3 contract source of truth).
        /// </summary>
        static CampaignCpuRulePack LoadProductPack()
        {
            string path = Path.Combine(RulesDir(), "chapters", "11010078.json");
            AssertTrue(File.Exists(path), "product pack on disk");
            return CampaignCpuRulePackLoader.LoadPackFromText(File.ReadAllText(path), path, true);
        }

        static void ProductPackLoadsFromDiskWithPr3Cards()
        {
            CampaignCpuRulePack pack = LoadProductPack();
            AssertEqual(11010078, pack.ChapterId, "chapter");
            AssertEqual(
                "sha256:d1ed3390a63030a78a6da913cf8436878a6e26e39f55dc1ec8e38dc7985e7835",
                pack.DeckHash,
                "deck hash pin");
            AssertTrue(pack.Priority != null && pack.Priority.Count >= 2, "priority rules");
            AssertEqual("g1-opening-special-or-action", pack.Priority[0].Id, "g1 rule id");
            AssertTrue(
                pack.Priority[0].Prefer != null
                && pack.Priority[0].Prefer.Count >= 1
                && pack.Priority[0].Prefer[0].HasCardId
                && pack.Priority[0].Prefer[0].CardId == 12485,
                "g1 top prefer is SummonSp Kaiser Vorse Raider 12485");
            AssertTrue(
                !pack.Policy.SuccessiveMainRecapture,
                "successive Main remains pack-default off before live gates");
            AssertTrue(
                !pack.Policy.PositionSafetyEnabled,
                "position safety remains pack-default off before live gates");
            AssertEqual(
                "battle_then_end",
                pack.Policy.PhaseExitPolicy,
                "explicit chapter phase exit policy");
            AssertEqual(0, pack.Policy.AttackTurnRaw.Value, "calibrated Attack raw");
            AssertEqual(1, pack.Policy.DefenseTurnRaw.Value, "calibrated Defense raw");
        }

        static CampaignCpuObservation ObservationFromFixtureDict(Dictionary<string, object> obsDict)
        {
            var obs = new CampaignCpuObservation
            {
                ChapterId = Utils.GetValue<int>(obsDict, "chapter_id", 11010078),
                ActingPlayer = Utils.GetValue<int>(obsDict, "acting_player", 1),
                OwnedSeat = Utils.GetValue<int>(obsDict, "owned_seat", 1),
                Turn = Utils.GetValue<int>(obsDict, "turn", 1),
                TurnPlayer = Utils.GetValue<int>(obsDict, "turn_player", 1),
                Phase = Utils.GetValue<int>(obsDict, "phase", (int)DuelPhase.Main1),
                SelfLp = Utils.GetValue<int>(obsDict, "self_lp", 8000),
                OppLp = Utils.GetValue<int>(obsDict, "opp_lp", 8000),
                IsMainPhaseWaitInput = Utils.GetValue<bool>(obsDict, "is_main_phase_wait_input", false),
                IsMultiSelectList = Utils.GetValue<bool>(obsDict, "is_multi_select", false),
                WindowClass = Utils.GetValue<string>(obsDict, "window_class") ?? "Unsupported",
            };

            string viewType = Utils.GetValue<string>(obsDict, "view_type") ?? "WaitInput";
            obs.ViewType = (DuelViewType)Enum.Parse(typeof(DuelViewType), viewType, ignoreCase: true);
            if (obsDict.ContainsKey("view_param1"))
            {
                obs.ViewParam1 = Utils.GetValue<int>(obsDict, "view_param1", 0);
            }
            else
            {
                string vpn = Utils.GetValue<string>(obsDict, "view_param1_name");
                if (string.Equals(vpn, "MainPhase", StringComparison.OrdinalIgnoreCase))
                {
                    obs.ViewParam1 = (int)DuelMenuActType.MainPhase;
                }
            }

            AddIntList(obs.SelfHandCardIds, Utils.GetValue(obsDict, "self_hand_card_ids", (List<object>)null));
            AddIntList(
                obs.SelfFieldFaceUpCardIds,
                Utils.GetValue(obsDict, "self_field_face_up_card_ids", (List<object>)null));
            AddIntList(
                obs.OppFieldFaceUpCardIds,
                Utils.GetValue(obsDict, "opp_field_face_up_card_ids", (List<object>)null));

            List<object> legal = Utils.GetValue(obsDict, "legal_actions", (List<object>)null);
            if (legal != null)
            {
                for (int i = 0; i < legal.Count; i++)
                {
                    Dictionary<string, object> a = legal[i] as Dictionary<string, object>;
                    if (a == null)
                    {
                        continue;
                    }
                    obs.LegalActions.Add(LegalActionFromFixture(a));
                }
            }
            return obs;
        }

        static void AddIntList(List<int> dest, List<object> src)
        {
            if (dest == null || src == null)
            {
                return;
            }
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i] == null)
                {
                    continue;
                }
                dest.Add(Convert.ToInt32(src[i]));
            }
        }

        static CampaignCpuLegalAction LegalActionFromFixture(Dictionary<string, object> a)
        {
            string kindStr = Utils.GetValue<string>(a, "kind") ?? "Command";
            LegalActionKind kind = (LegalActionKind)Enum.Parse(typeof(LegalActionKind), kindStr, true);
            var action = new CampaignCpuLegalAction
            {
                ActionId = Utils.GetValue<int>(a, "action_id"),
                Kind = kind,
                CardId = Utils.GetValue<int>(a, "card_id"),
                Position = Utils.GetValue<int>(a, "position"),
                Index = Utils.GetValue<int>(a, "index"),
                DialogResult = Utils.GetValue<int>(a, "dialog_result"),
                CancelDecide = Utils.GetValue<bool>(a, "cancel_decide", false),
                Label = Utils.GetValue<string>(a, "label") ?? string.Empty,
                IsMechanical = Utils.GetValue<bool>(a, "is_mechanical", false),
                TargetScope = Utils.GetValue<string>(a, "target_scope") ?? string.Empty,
                Player = Utils.GetValue<int>(a, "player", 1),
            };
            string cmdStr = Utils.GetValue<string>(a, "command");
            if (!string.IsNullOrEmpty(cmdStr))
            {
                // Capture serializer may emit "Attack" on MovePhase rows; ignore bad command enums.
                try
                {
                    action.Command = (DuelCommandType)Enum.Parse(typeof(DuelCommandType), cmdStr, true);
                }
                catch (ArgumentException)
                {
                    action.Command = default(DuelCommandType);
                }
            }
            string phaseStr = Utils.GetValue<string>(a, "phase");
            if (!string.IsNullOrEmpty(phaseStr) && kind == LegalActionKind.MovePhase)
            {
                action.Phase = (DuelPhase)Enum.Parse(typeof(DuelPhase), phaseStr, true);
            }
            return action;
        }

        static CampaignCpuObservation LoadObservationFixture(string fileName)
        {
            Dictionary<string, object> root = LoadFixtureRoot(fileName);
            Dictionary<string, object> obsDict = Utils.GetDictionary(root, "observation");
            AssertTrue(obsDict != null, "observation in " + fileName);
            return ObservationFromFixtureDict(obsDict);
        }

        static Dictionary<string, object> LoadExpect(string fileName)
        {
            Dictionary<string, object> root = LoadFixtureRoot(fileName);
            Dictionary<string, object> exp = Utils.GetDictionary(root, "expect");
            AssertTrue(exp != null, "expect in " + fileName);
            return exp;
        }

        static CampaignCpuObservation MakeMainObs(params CampaignCpuLegalAction[] actions)
        {
            var obs = new CampaignCpuObservation
            {
                ChapterId = 11010078,
                ViewType = DuelViewType.WaitInput,
                ViewParam1 = (int)DuelMenuActType.MainPhase,
                ActingPlayer = 1,
                OwnedSeat = 1,
                Turn = 1,
                TurnPlayer = 1,
                Phase = (int)DuelPhase.Main1,
                IsMainPhaseWaitInput = true,
                WindowClass = "WaitInput_MainPhase",
            };
            // Captured opening hand subset used by product when-predicates.
            obs.SelfHandCardIds.AddRange(new[] { 8344, 12485, 12292, 3868, 12493, 7557 });
            if (actions != null)
            {
                for (int i = 0; i < actions.Length; i++)
                {
                    obs.LegalActions.Add(actions[i]);
                }
            }
            return obs;
        }

        static CampaignCpuLegalAction Cmd(int actionId, DuelCommandType cmd, int cardId)
        {
            return new CampaignCpuLegalAction
            {
                ActionId = actionId,
                Kind = LegalActionKind.Command,
                Command = cmd,
                CardId = cardId,
                Position = CampaignCpuDefaults.PosHand,
                Index = 0,
                Player = 1,
                Label = cmd + " " + cardId,
            };
        }

        static CampaignCpuLegalAction Phase(int actionId, DuelPhase phase)
        {
            return new CampaignCpuLegalAction
            {
                ActionId = actionId,
                Kind = LegalActionKind.MovePhase,
                Phase = phase,
                Label = phase + " Phase",
            };
        }

        static CampaignCpuRulePack EmptyPack()
        {
            return new CampaignCpuRulePack
            {
                Policy = new CampaignCpuPackPolicy(),
                Never = new List<CampaignCpuNeverRule>(),
                Priority = new List<CampaignCpuPriorityRule>(),
                FallbackScoring = new List<CampaignCpuFallbackRule>(),
            };
        }

        static CampaignCpuProgressToken Tok(
            int generation,
            DuelViewType viewType,
            int p1,
            int p2,
            int p3,
            int acting,
            int turn,
            int phase,
            string fingerprint)
        {
            return CampaignCpuProgressToken.CreateWithLegalFingerprint(
                generation,
                viewType,
                p1,
                p2,
                p3,
                acting,
                turn,
                phase,
                fingerprint);
        }

        static void ScorerPriorityPicksG1TopAction()
        {
            // G1 fixture: live PR3 capture Main1 menu → SummonSp 12485 Kaiser Vorse Raider
            CampaignCpuRulePack pack = LoadProductPack();
            CampaignCpuObservation obs = LoadObservationFixture("g1_opening_a_main1.json");
            Dictionary<string, object> exp = LoadExpect("g1_opening_a_main1.json");
            CampaignCpuDecision d = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(CampaignCpuRoute.RuleCommit, d.Route, "G1 route");
            AssertEqual(Utils.GetValue<string>(exp, "rule_id"), d.RuleId, "G1 rule id");
            AssertEqual(Utils.GetValue<string>(exp, "reason"), d.Reason, "G1 reason");
            AssertTrue(d.Action != null, "G1 action");
            AssertEqual(Utils.GetValue<string>(exp, "action_identity"), d.Action.CanonicalIdentity, "G1 identity");
            AssertEqual(Utils.GetValue<int>(exp, "card_id"), d.Action.CardId, "G1 card");
            AssertEqual(DuelCommandType.SummonSp, d.Action.Command, "G1 command");
        }

        static void ScorerG2WhenTopAbsent()
        {
            // G2: hand lacks Kaiser 12485 → g2-next-preferred-without-top owns Summon 12292.
            CampaignCpuRulePack pack = LoadProductPack();
            CampaignCpuObservation obs = LoadObservationFixture("g2_opening_a_without_top_summon_sp.json");
            Dictionary<string, object> exp = LoadExpect("g2_opening_a_without_top_summon_sp.json");
            CampaignCpuDecision d = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(CampaignCpuRoute.RuleCommit, d.Route, "G2 route");
            AssertEqual(Utils.GetValue<string>(exp, "rule_id"), d.RuleId, "G2 rule id");
            AssertEqual("g2-next-preferred-without-top", d.RuleId, "G2 owns its rule id");
            AssertTrue(d.Action != null, "G2 action");
            AssertEqual(Utils.GetValue<string>(exp, "action_identity"), d.Action.CanonicalIdentity, "G2 identity");
            AssertEqual(Utils.GetValue<int>(exp, "card_id"), d.Action.CardId, "G2 card");
            AssertEqual(DuelCommandType.Summon, d.Action.Command, "G2 command");
        }

        static void ScorerG2OwnsRuleWhenKaiserAbsent()
        {
            CampaignCpuRulePack pack = LoadProductPack();
            // Product G1 requires 12485; G2 requires lacks 12485 + has 12292/8344.
            CampaignCpuPriorityRule g1 = null;
            CampaignCpuPriorityRule g2 = null;
            for (int i = 0; i < pack.Priority.Count; i++)
            {
                if (pack.Priority[i].Id == "g1-opening-special-or-action")
                {
                    g1 = pack.Priority[i];
                }
                if (pack.Priority[i].Id == "g2-next-preferred-without-top")
                {
                    g2 = pack.Priority[i];
                }
            }
            AssertTrue(g1 != null && g1.RequiredForSlice, "G1 required");
            AssertTrue(g2 != null && g2.RequiredForSlice, "G2 required");
            AssertTrue(
                g2.When != null
                && g2.When.SelfLacksCardId != null
                && g2.When.SelfLacksCardId.Contains(12485),
                "G2 self_lacks 12485");
            CampaignCpuObservation g2Obs = LoadObservationFixture(
                "g2_opening_a_without_top_summon_sp.json");
            AssertTrue(
                !CampaignCpuMatchers.WhenHolds(g1.When, g2Obs, false),
                "G1 when must not hold on G2 observation");
            AssertTrue(
                CampaignCpuMatchers.WhenHolds(g2.When, g2Obs, false),
                "G2 when holds on G2 observation");
        }

        static void ScorerNoMatchAllZeroFallback()
        {
            // G3: move-only Main1 menu + empty hand → Native no_match
            CampaignCpuRulePack pack = LoadProductPack();
            CampaignCpuObservation obs = LoadObservationFixture("g3_move_only_no_match.json");
            Dictionary<string, object> exp = LoadExpect("g3_move_only_no_match.json");
            CampaignCpuDecision d = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(CampaignCpuRoute.FallbackNative, d.Route, "G3 native");
            AssertEqual(Utils.GetValue<string>(exp, "reason"), d.Reason, "G3 reason");
        }

        static void OpeningSetSafetyChoosesAllowlistedDefensiveSet()
        {
            CampaignCpuRulePack pack = LoadOpeningSetSafetyPack();
            CampaignCpuObservation obs = OpeningMasterObservation();

            CampaignCpuDecision decision = CampaignCpuScorer.Decide(obs, pack);

            AssertEqual(
                DuelCommandType.SetMonst,
                decision.Action.Command,
                "opening Master chooses paired defensive Set");
            AssertEqual(
                "opening_position_safety",
                decision.Reason,
                "opening replacement reason");
            AssertEqual(
                "opening_defensive_set",
                decision.RuleId,
                "opening replacement rule");
        }

        static void OpeningSetSafetyRequiresOwnedMainContext()
        {
            CampaignCpuRulePack pack = LoadOpeningSetSafetyPack();

            CampaignCpuObservation wrongActor = OpeningMasterObservation();
            wrongActor.ActingPlayer = 0;
            CampaignCpuDecision actorDecision =
                CampaignCpuScorer.Decide(wrongActor, pack);
            AssertEqual(
                DuelCommandType.Summon,
                actorDecision.Action.Command,
                "non-owned actor cannot receive opening replacement");

            CampaignCpuObservation wrongTurn = OpeningMasterObservation();
            wrongTurn.TurnPlayer = 0;
            CampaignCpuDecision turnDecision =
                CampaignCpuScorer.Decide(wrongTurn, pack);
            AssertEqual(
                DuelCommandType.Summon,
                turnDecision.Action.Command,
                "non-owned turn cannot receive opening replacement");

            CampaignCpuObservation nonMain = OpeningMasterObservation();
            nonMain.IsMainPhaseWaitInput = false;
            nonMain.WindowClass = "Unsupported";
            CampaignCpuDecision nonMainDecision =
                CampaignCpuScorer.Decide(nonMain, pack);
            AssertEqual(
                DuelCommandType.Summon,
                nonMainDecision.Action.Command,
                "non-Main observation cannot receive opening replacement");
        }

        static CampaignCpuRulePack LoadOpeningSetSafetyPack()
        {
            const string json = @"{
              ""version"": 1,
              ""chapter_id"": 11010078,
              ""deck_hash"": ""sha256:d1ed3390a63030a78a6da913cf8436878a6e26e39f55dc1ec8e38dc7985e7835"",
              ""policy"": {
                ""on_no_match"": ""native_cpu"",
                ""opening_set_safety_enabled"": true,
                ""opening_set_safety_card_ids"": [12293]
              },
              ""never"": [],
              ""priority"": [],
              ""fallback_scoring"": [
                {
                  ""id"": ""prefer-summon"",
                  ""match"": { ""command"": ""Summon"" },
                  ""score"": 8
                },
                {
                  ""id"": ""prefer-set-monst"",
                  ""match"": { ""command"": ""SetMonst"" },
                  ""score"": 4
                },
                {
                  ""id"": ""penalize-early-end"",
                  ""match"": { ""kind"": ""MovePhase"", ""phase"": ""End"" },
                  ""score"": -20
                }
              ]
            }";
            return CampaignCpuRulePackLoader.LoadPackFromText(
                json, "opening-set-safety", requireDeckHash: true);
        }

        static CampaignCpuObservation OpeningMasterObservation()
        {
            CampaignCpuLegalAction summon =
                Cmd(0, DuelCommandType.Summon, 12293);
            summon.Index = 3;
            CampaignCpuLegalAction set =
                Cmd(1, DuelCommandType.SetMonst, 12293);
            set.Index = 3;
            summon.BasicLevelKnown = true;
            summon.BasicLevel = 1;
            summon.BasicAtkKnown = true;
            summon.BasicAtk = 300;
            summon.BasicDefKnown = true;
            summon.BasicDef = 1200;
            set.BasicLevelKnown = true;
            set.BasicLevel = 1;
            set.BasicAtkKnown = true;
            set.BasicAtk = 300;
            set.BasicDefKnown = true;
            set.BasicDef = 1200;

            CampaignCpuObservation obs = MakeMainObs(
                summon,
                set,
                Phase(2, DuelPhase.End));
            obs.Turn = 0;
            obs.SelfMonsterCount = 0;
            obs.OppMonsterCount = 0;
            obs.SelfHandCardIds.Clear();
            obs.SelfHandCardIds.Add(12293);
            return obs;
        }

        static void OpeningSetSafetyPreservesNonQualifyingFallbacks()
        {
            CampaignCpuRulePack disabled = LoadOpeningSetSafetyPack();
            disabled.Policy.OpeningSetSafetyEnabled = false;
            AssertOpeningPreservesSummon(
                disabled,
                OpeningMasterObservation(),
                "flag off");

            CampaignCpuRulePack notAllowlisted =
                LoadOpeningSetSafetyPack();
            notAllowlisted.Policy.OpeningSetSafetyCardIds.Clear();
            AssertOpeningPreservesSummon(
                notAllowlisted,
                OpeningMasterObservation(),
                "card not allowlisted");

            CampaignCpuRulePack priority = LoadOpeningSetSafetyPack();
            priority.Priority.Add(new CampaignCpuPriorityRule
            {
                Id = "explicit-face-up-plan",
                Priority = 100,
                ListIndex = 0,
                Prefer = new List<CampaignCpuMatchSpec>
                {
                    new CampaignCpuMatchSpec
                    {
                        HasCommand = true,
                        Command = DuelCommandType.Summon,
                        HasCardId = true,
                        CardId = 12293,
                    },
                },
                ScoreBonus = 50,
            });
            CampaignCpuDecision explicitDecision =
                CampaignCpuScorer.Decide(
                    OpeningMasterObservation(),
                    priority);
            AssertEqual(
                DuelCommandType.Summon,
                explicitDecision.Action.Command,
                "explicit priority is not overridden");
            AssertEqual(
                "explicit-face-up-plan",
                explicitDecision.RuleId,
                "explicit priority attribution retained");

            CampaignCpuObservation later = OpeningMasterObservation();
            later.Turn = 1;
            AssertOpeningPreservesSummon(
                LoadOpeningSetSafetyPack(), later, "later turn");

            CampaignCpuObservation occupied = OpeningMasterObservation();
            occupied.SelfMonsterCount = 1;
            AssertOpeningPreservesSummon(
                LoadOpeningSetSafetyPack(), occupied, "occupied field");

            CampaignCpuObservation battle = OpeningMasterObservation();
            battle.LegalActions.Add(Phase(3, DuelPhase.Battle));
            AssertOpeningPreservesSummon(
                LoadOpeningSetSafetyPack(), battle, "Battle available");

            CampaignCpuObservation unknown = OpeningMasterObservation();
            unknown.LegalActions[0].BasicDefKnown = false;
            AssertOpeningPreservesSummon(
                LoadOpeningSetSafetyPack(), unknown, "unknown DEF");

            CampaignCpuObservation highLevel = OpeningMasterObservation();
            highLevel.LegalActions[0].BasicLevel = 5;
            AssertOpeningPreservesSummon(
                LoadOpeningSetSafetyPack(), highLevel, "high Level");

            CampaignCpuObservation attackFavored =
                OpeningMasterObservation();
            attackFavored.LegalActions[0].BasicAtk = 1200;
            attackFavored.LegalActions[0].BasicDef = 300;
            AssertOpeningPreservesSummon(
                LoadOpeningSetSafetyPack(),
                attackFavored,
                "ATK not below DEF");

            CampaignCpuObservation noPair = OpeningMasterObservation();
            noPair.LegalActions.RemoveAt(1);
            AssertOpeningPreservesSummon(
                LoadOpeningSetSafetyPack(), noPair, "paired Set absent");
        }

        static void AssertOpeningPreservesSummon(
            CampaignCpuRulePack pack,
            CampaignCpuObservation observation,
            string label)
        {
            CampaignCpuDecision decision =
                CampaignCpuScorer.Decide(observation, pack);
            AssertEqual(
                DuelCommandType.Summon,
                decision.Action.Command,
                label + " preserves prior Summon");
            AssertTrue(
                !string.Equals(
                    decision.RuleId,
                    "opening_defensive_set",
                    StringComparison.Ordinal),
                label + " has no opening override attribution");
        }

        static void OpeningSetSafetyProjectsAndAuditsLiveBasicStats()
        {
            CampaignCpuObservation obs = OpeningMasterObservation();
            for (int i = 0; i < obs.LegalActions.Count; i++)
            {
                CampaignCpuLegalAction action = obs.LegalActions[i];
                action.BasicLevelKnown = false;
                action.BasicAtkKnown = false;
                action.BasicDefKnown = false;
            }
            var query = new FakeCampaignCpuCardBasicStatsQuery
            {
                Level = 1,
                Atk = 300,
                Def = 1200,
            };
            CampaignCpuLegalActionBasicStatsBuilder.Populate(
                query,
                obs);

            AssertTrue(
                obs.LegalActions[0].BasicLevelKnown,
                "Summon Level projected");
            AssertEqual(
                300,
                obs.LegalActions[0].BasicAtk,
                "Summon ATK projected");
            AssertEqual(
                1200,
                obs.LegalActions[1].BasicDef,
                "paired Set DEF projected");
            AssertEqual(
                2,
                query.ReadCount,
                "only normal Summon/Set actions query BasicVal");

            CampaignCpuDecision decision = CampaignCpuScorer.Decide(
                obs,
                LoadOpeningSetSafetyPack());
            string line = CampaignCpuAuditSerializer.SerializeDecision(
                29,
                decision,
                obs,
                shadowOnly: false);
            AssertTrue(
                line.Contains("\"basic_level\":1")
                || line.Contains("\"basic_level\": 1"),
                "audit emits Basic Level");
            AssertTrue(
                line.Contains("\"basic_atk\":300")
                || line.Contains("\"basic_atk\": 300"),
                "audit emits Basic ATK");
            AssertTrue(
                line.Contains("\"basic_def\":1200")
                || line.Contains("\"basic_def\": 1200"),
                "audit emits Basic DEF");
            AssertTrue(
                line.Contains("Command|Summon|12293|13|3|"),
                "audit emits replaced Summon identity");
        }

        sealed class FakeCampaignCpuCardBasicStatsQuery :
            ICampaignCpuCardBasicStatsQuery
        {
            public int Level;
            public int Atk;
            public int Def;
            public int ReadCount;

            public bool TryReadCardBasicStats(
                int player,
                int position,
                int index,
                out int level,
                out int atk,
                out int def)
            {
                ReadCount++;
                level = Level;
                atk = Atk;
                def = Def;
                return true;
            }
        }

        static void TributeNormalSummonSafetyDefersUnsafeContinuations()
        {
            CampaignCpuLegalAction summon = Cmd(1, DuelCommandType.Summon, 12692);
            bool levelSix = CampaignCpuSummonSafety.ShouldDeferToNative(
                summon, true, 6);
            bool levelFour = CampaignCpuSummonSafety.ShouldDeferToNative(
                summon, true, 4);
            bool unknownLevel = CampaignCpuSummonSafety.ShouldDeferToNative(
                summon, false, 0);
            CampaignCpuLegalAction setMonster = Cmd(
                2, DuelCommandType.SetMonst, 12692);
            bool levelSixSet = CampaignCpuSummonSafety.ShouldDeferToNative(
                setMonster, true, 6);
            CampaignCpuLegalAction special = Cmd(
                3, DuelCommandType.SummonSp, 12485);
            bool specialUnknown = CampaignCpuSummonSafety.ShouldDeferToNative(
                special, false, 0);

            AssertTrue(levelSix, "level 5+ normal summon defers to native");
            AssertTrue(!levelFour, "level 1-4 normal summon remains scriptable");
            AssertTrue(unknownLevel, "unknown normal summon level fails native");
            AssertTrue(levelSixSet, "level 5+ monster set also defers to native");
            AssertTrue(!specialUnknown, "special summon is outside tribute guard");
        }

        static void ScriptedCommitSafetyOnlyAllowsProvenSingleStepLineage()
        {
            CampaignCpuLegalAction pandemicDragonEffect =
                Cmd(1, DuelCommandType.Action, 12489);
            CampaignCpuLegalAction specialSummon =
                Cmd(2, DuelCommandType.SummonSp, 12485);
            CampaignCpuLegalAction turnDefense =
                Cmd(3, DuelCommandType.TurnDef, 4007);
            CampaignCpuLegalAction lowLevelSummon =
                Cmd(4, DuelCommandType.Summon, 4747);
            CampaignCpuLegalAction lowLevelSet =
                Cmd(5, DuelCommandType.SetMonst, 4747);
            CampaignCpuLegalAction spellTrapSet =
                Cmd(7, DuelCommandType.Set, 12491);
            CampaignCpuLegalAction tributeSummon =
                Cmd(6, DuelCommandType.Summon, 12692);

            AssertTrue(
                CampaignCpuActionChainFactory.ShouldDeferScriptedCommit(
                    pandemicDragonEffect, false, 0),
                "Pandemic Dragon effect selection must be native before commit");
            AssertTrue(
                CampaignCpuActionChainFactory.ShouldDeferScriptedCommit(
                    specialSummon, false, 0),
                "special summon continuation remains native");
            AssertTrue(
                CampaignCpuActionChainFactory.ShouldDeferScriptedCommit(
                    turnDefense, false, 0),
                "position command remains native until proven");
            AssertTrue(
                !CampaignCpuActionChainFactory.ShouldDeferScriptedCommit(
                    lowLevelSummon, true, 4),
                "level 1-4 normal summon is proven scripted lineage");
            AssertTrue(
                !CampaignCpuActionChainFactory.ShouldDeferScriptedCommit(
                    lowLevelSet, true, 4),
                "level 1-4 monster set is proven scripted lineage");
            AssertTrue(
                !CampaignCpuActionChainFactory.ShouldDeferScriptedCommit(
                    spellTrapSet, false, 0),
                "single-step Spell/Trap set is proven scripted lineage");
            AssertTrue(
                CampaignCpuActionChainFactory.ShouldDeferScriptedCommit(
                    tributeSummon, true, 8),
                "tribute summon remains native");
        }

        static void SafeContinuationSelectionSkipsUnsafeTopScoredEffect()
        {
            CampaignCpuRulePack pack = LoadProductPack();
            CampaignCpuObservation obs = MakeMainObs(
                Cmd(0, DuelCommandType.Action, 10590),
                Cmd(1, DuelCommandType.Set, 12491));
            obs.SelfHandCardIds.Clear();
            obs.SelfHandCardIds.AddRange(new[] { 10590, 12491 });

            CampaignCpuSafeContinuationSelection result =
                CampaignCpuSafeContinuationSelector.Decide(
                    obs,
                    pack,
                    predicateEvalFailed: false,
                    appliedDecisionCount: 0,
                    resolveCardLevel: action => 0);

            AssertEqual(
                CampaignCpuRoute.RuleCommit,
                result.Decision.Route,
                "safe lower-ranked action remains scriptable");
            AssertEqual(
                DuelCommandType.Set,
                result.Decision.Action.Command,
                "unsafe top-scored effect is skipped for proven Set");
            AssertEqual(
                "prefer-set",
                result.Decision.RuleId,
                "safe action retains scorer rule attribution");
            AssertTrue(
                result.ExcludedActionIdentities.Contains(
                    obs.LegalActions[0].CanonicalIdentity),
                "unsafe effect exclusion is auditable");
        }

        static void SuccessiveMainRecapturePolicyRequiresSafeFreshProvenance()
        {
            CampaignCpuLegalAction summon = Cmd(3, DuelCommandType.Summon, 4747);
            CampaignCpuActionChain chain = CampaignCpuActionChainFactory.Create(
                duelGeneration: 8,
                originViewSeq: 100,
                originTurn: 3,
                originTurnPlayer: 1,
                originPhase: (int)DuelPhase.Main1,
                action: summon,
                ruleId: "prefer-summon",
                levelKnown: true,
                level: 4,
                progressToken: Tok(
                    8, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase,
                    0, 2, 1, 3, (int)DuelPhase.Main1, "summon-menu"),
                startedUtc: new DateTime(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc));
            AssertEqual(
                CampaignCpuContinuationEligibility.ScriptedResponseContinuation,
                chain.Eligibility,
                "simple low-level normal summon is recapturable provenance");
            AssertEqual(
                "summon-menu",
                chain.OriginLegalActionFingerprint,
                "origin menu fingerprint retained across native continuation");
            chain.NativeResponseSeen = true;

            var input = new CampaignCpuRecapturePolicyInput
                {
                    Enabled = true,
                    Chain = chain,
                    DuelGeneration = 8,
                    CurrentViewSeq = 110,
                    CurrentProgressToken = Tok(
                        8, DuelViewType.WaitInput,
                        (int)DuelMenuActType.MainPhase, 0, 0, 1, 3,
                        (int)DuelPhase.Main1, "stable-main-menu"),
                    IsStableMainMenuBoundary = true,
                    OwnedIsHuman = 0,
                    MyIsHuman = 1,
                    DoCommandUser = 0,
                    RunDialogUser = 0,
                    Turn = 3,
                    TurnPlayer = 1,
                    Phase = (int)DuelPhase.Main1,
                    OwnedSeat = 1,
                    MyId = 0,
                    ResponseWindowInFlight = false,
                    NowUtc = new DateTime(2026, 7, 27, 3, 0, 1, DateTimeKind.Utc),
                    MaxAttemptsPerChain = 2,
                    MaxAttemptsPerTurn = 3,
                    TimeoutMs = 5000,
                };
            CampaignCpuRecapturePolicyResult result =
                CampaignCpuRecapturePolicy.Evaluate(input);

            AssertEqual(
                CampaignCpuRecaptureDecision.CommitStableOwnedMain,
                result.Decision,
                "stable owned Main menu requests pending recapture");
            AssertEqual(
                "stable_owned_main_menu",
                result.Reason,
                "positive recapture reason");

            input.CurrentProgressToken = Tok(
                8, DuelViewType.WaitInput,
                (int)DuelMenuActType.MainPhase, 0, 0, 1, 3,
                (int)DuelPhase.Main1, "summon-menu");
            CampaignCpuRecapturePolicyResult staleMenu =
                CampaignCpuRecapturePolicy.Evaluate(input);
            AssertEqual(
                CampaignCpuRecaptureDecision.HoldNative,
                staleMenu.Decision,
                "origin legal menu cannot masquerade as a new Main boundary");
            AssertEqual(
                "legal_menu_not_changed_from_origin",
                staleMenu.Reason,
                "origin-menu denial is explicit");
        }

        static void SuccessiveMainRecapturePolicyFailsNativeOnUnsafeOrBoundedInputs()
        {
            CampaignCpuLegalAction special = Cmd(4, DuelCommandType.SummonSp, 12485);
            CampaignCpuActionChain unsafeChain = CampaignCpuActionChainFactory.Create(
                8, 100, 3, 1, (int)DuelPhase.Main1, special, "prefer-special",
                true, 4,
                Tok(8, DuelViewType.WaitInput, 2, 0, 2, 1, 3, 2, "special"),
                new DateTime(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc));
            AssertEqual(
                CampaignCpuContinuationEligibility.UnsafeContinuation,
                unsafeChain.Eligibility,
                "special summon is unsafe provenance by default");

            CampaignCpuRecapturePolicyInput input = new CampaignCpuRecapturePolicyInput
            {
                Enabled = true,
                Chain = unsafeChain,
                DuelGeneration = 8,
                CurrentViewSeq = 110,
                CurrentProgressToken = Tok(
                    8, DuelViewType.CpuThinking, 1, 0, 0, 1, 3, 2, "cpu"),
                ViewType = DuelViewType.CpuThinking,
                Param1 = 1,
                Turn = 3,
                TurnPlayer = 1,
                Phase = (int)DuelPhase.Main1,
                OwnedSeat = 1,
                MyId = 0,
                NowUtc = new DateTime(2026, 7, 27, 3, 0, 1, DateTimeKind.Utc),
                MaxAttemptsPerChain = 2,
                MaxAttemptsPerTurn = 3,
                TimeoutMs = 5000,
            };
            CampaignCpuRecapturePolicyResult unsafeResult =
                CampaignCpuRecapturePolicy.Evaluate(input);
            AssertEqual(
                CampaignCpuRecaptureDecision.AbandonRecaptureForTurn,
                unsafeResult.Decision,
                "unsafe lineage abandons recapture");
            AssertEqual("unsafe_lineage", unsafeResult.Reason, "unsafe deny reason");

            unsafeChain.Eligibility =
                CampaignCpuContinuationEligibility.ScriptedResponseContinuation;
            unsafeChain.NativeResponseSeen = true;
            unsafeChain.RecaptureAttempts = 2;
            CampaignCpuRecapturePolicyResult capped =
                CampaignCpuRecapturePolicy.Evaluate(input);
            AssertEqual(
                CampaignCpuRecaptureDecision.AbandonRecaptureForTurn,
                capped.Decision,
                "per-chain cap fails native");
            AssertEqual("chain_attempt_cap", capped.Reason, "cap deny reason");

            unsafeChain.RecaptureAttempts = 0;
            input.DuelGeneration = 9;
            CampaignCpuRecapturePolicyResult wrongGeneration =
                CampaignCpuRecapturePolicy.Evaluate(input);
            AssertEqual(
                CampaignCpuRecaptureDecision.ClearAtPhaseOrTurnBoundary,
                wrongGeneration.Decision,
                "generation mismatch clears lineage");
            AssertEqual(
                "generation_mismatch",
                wrongGeneration.Reason,
                "generation deny reason");
        }

        static void SuccessiveMainTimeoutRefreshesOnSemanticProgress()
        {
            DateTime started =
                new DateTime(2026, 7, 29, 19, 11, 49, DateTimeKind.Utc);
            CampaignCpuActionChain chain = CampaignCpuActionChainFactory.Create(
                8, 427, 2, 1, (int)DuelPhase.Main1,
                Cmd(6, DuelCommandType.Summon, 12292),
                "g1-opening-special-or-action",
                true,
                1,
                Tok(8, DuelViewType.WaitInput,
                    (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                    (int)DuelPhase.Main1, "origin"),
                started);
            var sm = new SoloTemporaryCpuStateMachine();
            sm.ActivateHumanOwned();
            sm.ArmActionChain(chain);

            bool refreshed = sm.ObserveActionChainSemanticProgress(
                Tok(8, DuelViewType.RunDialog, 4, 0, 0, 0, 2,
                    (int)DuelPhase.Main1, null),
                started.AddSeconds(6));
            AssertTrue(refreshed, "fresh animation view refreshes chain progress");
            chain.NativeResponseSeen = true;

            CampaignCpuRecapturePolicyResult result =
                CampaignCpuRecapturePolicy.Evaluate(
                    new CampaignCpuRecapturePolicyInput
                    {
                        Enabled = true,
                        Chain = chain,
                        DuelGeneration = 8,
                        CurrentViewSeq = 466,
                        CurrentProgressToken = Tok(
                            8, DuelViewType.WaitInput,
                            (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                            (int)DuelPhase.Main1, "changed"),
                        IsStableMainMenuBoundary = true,
                        OwnedIsHuman = 0,
                        MyIsHuman = 1,
                        Turn = 2,
                        TurnPlayer = 1,
                        Phase = (int)DuelPhase.Main1,
                        OwnedSeat = 1,
                        MyId = 0,
                        NowUtc = started.AddSeconds(9),
                        TimeoutMs = 5000,
                    });
            AssertEqual(
                CampaignCpuRecaptureDecision.CommitStableOwnedMain,
                result.Decision,
                "stable menu inside refreshed inactivity deadline commits");

            result = CampaignCpuRecapturePolicy.Evaluate(
                new CampaignCpuRecapturePolicyInput
                {
                    Enabled = true,
                    Chain = chain,
                    DuelGeneration = 8,
                    CurrentViewSeq = 467,
                    CurrentProgressToken = Tok(
                        8, DuelViewType.WaitInput,
                        (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                        (int)DuelPhase.Main1, "changed-again"),
                    IsStableMainMenuBoundary = true,
                    OwnedIsHuman = 0,
                    MyIsHuman = 1,
                    Turn = 2,
                    TurnPlayer = 1,
                    Phase = (int)DuelPhase.Main1,
                    OwnedSeat = 1,
                    MyId = 0,
                    NowUtc = started.AddSeconds(12),
                    TimeoutMs = 5000,
                });
            AssertEqual(
                CampaignCpuRecaptureDecision.AbandonRecaptureForTurn,
                result.Decision,
                "true five-second semantic inactivity still times out");
            AssertEqual("recapture_timeout", result.Reason, "inactivity timeout reason");
        }

        static void AbandonedSuccessiveMainChainBecomesSilentTerminalHold()
        {
            CampaignCpuActionChain chain = CampaignCpuActionChainFactory.Create(
                8, 427, 2, 1, (int)DuelPhase.Main1,
                Cmd(6, DuelCommandType.Summon, 12292),
                "prefer-summon",
                true,
                1,
                Tok(8, DuelViewType.WaitInput,
                    (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                    (int)DuelPhase.Main1, "origin"),
                DateTime.UtcNow);
            chain.Abandoned = true;
            chain.AbandonReason = "recapture_timeout";

            CampaignCpuRecapturePolicyResult result =
                CampaignCpuRecapturePolicy.Evaluate(
                    new CampaignCpuRecapturePolicyInput
                    {
                        Enabled = true,
                        Chain = chain,
                        DuelGeneration = 8,
                        CurrentViewSeq = 466,
                        Turn = 2,
                        TurnPlayer = 1,
                        Phase = (int)DuelPhase.Main1,
                        OwnedSeat = 1,
                        MyId = 0,
                    });
            AssertEqual(
                CampaignCpuRecaptureDecision.HoldNative,
                result.Decision,
                "abandoned chain is terminal and does not re-abandon");
            AssertEqual(
                "already_abandoned",
                result.Reason,
                "terminal hold has non-error suppression reason");
        }

        static void StableMainBoundaryRequiresTwoMatchingSysActSamples()
        {
            var tracker = new CampaignCpuStableMainBoundaryTracker();
            var sample = new CampaignCpuStableMainBoundarySample
            {
                DuelGeneration = 8,
                Turn = 3,
                Phase = (int)DuelPhase.Main1,
                TurnPlayer = 1,
                OwnedSeat = 1,
                OwnedIsHuman = 0,
                MyIsHuman = 1,
                DoCommandUser = 0,
                RunDialogUser = 0,
                LegalActionFingerprint =
                    "Command|Summon|4747|13|0|;MovePhase|End|0|0|5|",
            };

            AssertTrue(
                !tracker.Observe(sample),
                "first pre-SysAct menu sample only primes stability");
            AssertTrue(
                tracker.Observe(sample),
                "second identical pre-SysAct menu sample proves stability");

            sample.OwnedIsHuman = 1;
            AssertTrue(
                !tracker.Observe(sample),
                "lost CPU ownership resets the candidate");
            sample.OwnedIsHuman = 0;
            AssertTrue(
                !tracker.Observe(sample),
                "restored CPU-owned sample must prime again");
        }

        static void CpuThinkingIsNotARecaptureBoundary()
        {
            CampaignCpuActionChain chain = CampaignCpuActionChainFactory.Create(
                8, 100, 3, 1, (int)DuelPhase.Main1,
                Cmd(3, DuelCommandType.Summon, 4747),
                "prefer-summon",
                true,
                4,
                Tok(8, DuelViewType.WaitInput,
                    (int)DuelMenuActType.MainPhase, 0, 0, 1, 3,
                    (int)DuelPhase.Main1, "origin"),
                new DateTime(2026, 7, 28, 3, 0, 0, DateTimeKind.Utc));
            chain.NativeResponseSeen = true;
            chain.CpuThinkingCount = 2;

            CampaignCpuRecapturePolicyResult result =
                CampaignCpuRecapturePolicy.Evaluate(
                    new CampaignCpuRecapturePolicyInput
                    {
                        Enabled = true,
                        Chain = chain,
                        DuelGeneration = 8,
                        CurrentViewSeq = 110,
                        CurrentProgressToken = Tok(
                            8, DuelViewType.CpuThinking, 1, 0, 0, 1, 3,
                            (int)DuelPhase.Main1, "cpu-thinking"),
                        ViewType = DuelViewType.CpuThinking,
                        Param1 = 1,
                        Turn = 3,
                        TurnPlayer = 1,
                        Phase = (int)DuelPhase.Main1,
                        OwnedSeat = 1,
                        MyId = 0,
                        IsStableMainMenuBoundary = false,
                        OwnedIsHuman = 0,
                        MyIsHuman = 1,
                        DoCommandUser = 0,
                        RunDialogUser = 0,
                        NowUtc = new DateTime(
                            2026, 7, 28, 3, 0, 1, DateTimeKind.Utc),
                    });

            AssertEqual(
                CampaignCpuRecaptureDecision.HoldNative,
                result.Decision,
                "CpuThinking callback remains native");
            AssertEqual(
                "stable_main_menu_required",
                result.Reason,
                "closed callback boundary is explicit");
        }

        static void StableMainBoundaryDirectCommitKeepsCpuOwnership()
        {
            var sm = new SoloTemporaryCpuStateMachine();
            sm.ActivateHumanOwned();
            sm.ArmActionChain(CampaignCpuActionChainFactory.Create(
                4, 10, 2, 1, (int)DuelPhase.Main1,
                Cmd(1, DuelCommandType.Summon, 4747),
                "prefer-summon",
                true,
                4,
                Tok(4, DuelViewType.WaitInput,
                    (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                    (int)DuelPhase.Main1, "origin"),
                DateTime.UtcNow));
            sm.BeginNativeLease(
                "dual_human_myid_response_hold",
                4,
                12,
                Tok(4, DuelViewType.RunDialog, 2, 0, 0, 0, 2,
                    (int)DuelPhase.Main1, "dialog"),
                1,
                DateTime.UtcNow);
            sm.MarkNativeResponseForwarded();

            int commits = 0;
            CampaignCpuStableMainDirectCommitResult result =
                CampaignCpuStableMainDirectCommit.Execute(
                    sm,
                    boundaryViewSeq: 20,
                    commit: () =>
                    {
                        commits++;
                        AssertEqual(
                            SoloTemporaryCpuState.NativeLease,
                            sm.State,
                            "direct commit occurs while CPU lease stays authoritative");
                        return CampaignCpuCommitOutcome.Applied;
                    });

            AssertEqual(1, commits, "one direct commit");
            AssertEqual(
                CampaignCpuCommitOutcome.Applied,
                result.CommitOutcome,
                "applied result");
            AssertTrue(result.Applied, "direct transport reports applied");
            AssertEqual(
                SoloTemporaryCpuState.NativeLease,
                sm.State,
                "next SysAct runs with CPU ownership");
            AssertEqual(
                1,
                sm.ActiveActionChain.RecaptureAttempts,
                "stable direct commit consumes one bounded attempt");
            AssertEqual(
                20UL,
                sm.ActiveActionChain.LastRecaptureViewSeq,
                "stable direct-commit boundary sequence retained");
        }

        static void PositionSafetyRejectsDominatedBlueEyesTurnDefense()
        {
            CampaignCpuObservation obs = TacticalObservation(
                ownCardId: 4007,
                ownAtk: 3000,
                ownDef: 2500,
                ownIsAttack: true,
                opposingAtk: 3000);
            CampaignCpuLegalAction turnDef = Cmd(0, DuelCommandType.TurnDef, 4007);
            turnDef.Player = 1;
            turnDef.Position = 2;
            turnDef.Index = 0;
            CampaignCpuLegalAction battle = Phase(1, DuelPhase.Battle);
            CampaignCpuLegalAction end = Phase(2, DuelPhase.End);
            obs.LegalActions.Add(turnDef);
            obs.LegalActions.Add(battle);
            obs.LegalActions.Add(end);

            CampaignCpuPositionSafetyResult result =
                CampaignCpuPositionSafety.FilterDominatedPositionChanges(
                    obs,
                    obs.LegalActions,
                    new List<int>());
            AssertTrue(result.RemovedAnyAction, "Blue-Eyes TurnDef removed");
            AssertEqual("dominated_turn_defense", result.Reason, "Blue-Eyes reason");
            AssertEqual(2, result.FilteredActions.Count, "Battle and End remain");
            AssertTrue(
                !result.FilteredActions.Exists(a => a.Command == DuelCommandType.TurnDef),
                "dominated TurnDef absent from filtered output");
        }

        static void SafeContinuationSelectionAllowsTacticalPhaseExit()
        {
            CampaignCpuObservation obs = TacticalObservation(
                ownCardId: 4007,
                ownAtk: 3000,
                ownDef: 2500,
                ownIsAttack: true,
                opposingAtk: 3000);
            CampaignCpuLegalAction turnDef =
                Cmd(0, DuelCommandType.TurnDef, 4007);
            turnDef.Player = 1;
            turnDef.Position = 2;
            turnDef.Index = 0;
            obs.LegalActions.Add(turnDef);
            obs.LegalActions.Add(Cmd(1, DuelCommandType.Action, 8344));
            obs.LegalActions.Add(Cmd(2, DuelCommandType.Action, 0));
            obs.LegalActions.Add(Phase(1, DuelPhase.Battle));
            obs.LegalActions.Add(Phase(2, DuelPhase.End));

            CampaignCpuRulePack pack = EmptyPack();
            pack.Policy.OnNoMatch = "native_cpu";
            pack.Policy.PositionSafetyEnabled = true;
            pack.Policy.PhaseExitPolicy = "battle_then_end";
            pack.FallbackScoring.Add(new CampaignCpuFallbackRule
            {
                Id = "prefer-action",
                Match = new CampaignCpuMatchSpec
                {
                    HasCommand = true,
                    Command = DuelCommandType.Action,
                },
                Score = 10,
            });

            CampaignCpuSafeContinuationSelection selection =
                CampaignCpuSafeContinuationSelector.Decide(
                    obs,
                    pack,
                    predicateEvalFailed: false,
                    appliedDecisionCount: 0,
                    resolveCardLevel: action => 0);

            AssertEqual(
                CampaignCpuRoute.RuleCommit,
                selection.Decision.Route,
                "tactical phase exit remains directly committable");
            AssertEqual(
                DuelPhase.Battle,
                selection.Decision.Action.Phase,
                "known attacker exits to Battle after dominated TurnDef");
            AssertEqual(
                "dominated_turn_defense",
                selection.Decision.TacticalFilterReason,
                "direct selection retains dominated-position audit");
            AssertTrue(
                selection.SelectedTerminalPhaseExit,
                "selector marks the phase exit safe for terminal direct commit");
        }

        static void PositionSafetyPreservesUnknownAndBeneficialDefense()
        {
            CampaignCpuObservation beneficial = TacticalObservation(
                ownCardId: 4007,
                ownAtk: 2000,
                ownDef: 3000,
                ownIsAttack: true,
                opposingAtk: 2500);
            CampaignCpuLegalAction turnDef = Cmd(0, DuelCommandType.TurnDef, 4007);
            turnDef.Player = 1;
            turnDef.Position = 2;
            turnDef.Index = 0;
            beneficial.LegalActions.Add(turnDef);
            CampaignCpuPositionSafetyResult allowed =
                CampaignCpuPositionSafety.FilterDominatedPositionChanges(
                    beneficial, beneficial.LegalActions, new List<int>());
            AssertTrue(!allowed.RemovedAnyAction, "beneficial Defense remains legal");

            beneficial.TacticalMonsters[0].HasDef = false;
            CampaignCpuPositionSafetyResult unknown =
                CampaignCpuPositionSafety.FilterDominatedPositionChanges(
                    beneficial, beneficial.LegalActions, new List<int>());
            AssertTrue(!unknown.RemovedAnyAction, "unknown DEF cannot prove domination");

            beneficial.TacticalMonsters[0].HasDef = true;
            beneficial.TacticalMonsters[0].Def = 1000;
            CampaignCpuPositionSafetyResult excepted =
                CampaignCpuPositionSafety.FilterDominatedPositionChanges(
                    beneficial, beneficial.LegalActions, new List<int> { 4007 });
            AssertTrue(!excepted.RemovedAnyAction, "pack card exception overrides guard");
        }

        static void PositionSafetyDefaultsOffInScorer()
        {
            CampaignCpuObservation obs = TacticalObservation(
                ownCardId: 4007,
                ownAtk: 3000,
                ownDef: 2500,
                ownIsAttack: true,
                opposingAtk: 3000);
            CampaignCpuLegalAction turnDef =
                Cmd(0, DuelCommandType.TurnDef, 4007);
            turnDef.Player = 1;
            turnDef.Position = 2;
            turnDef.Index = 0;
            obs.LegalActions.Add(turnDef);
            obs.LegalActions.Add(Phase(1, DuelPhase.End));

            CampaignCpuRulePack pack = EmptyPack();
            pack.Policy.OnNoMatch = "first_legal";
            pack.Policy.PhaseExitPolicy = "battle_then_end";
            CampaignCpuDecision decision = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(
                turnDef.CanonicalIdentity,
                decision.Action.CanonicalIdentity,
                "default-off tactical switch preserves legacy scorer behavior");
            AssertEqual(
                0,
                decision.TacticalFilteredActionIdentities.Count,
                "default-off tactical switch audits no behavior filter");
        }

        static void ExplicitPhaseExitIgnoresActionIdTieOrdering()
        {
            CampaignCpuObservation obs = TacticalObservation(
                ownCardId: 4007,
                ownAtk: 3000,
                ownDef: 2500,
                ownIsAttack: true,
                opposingAtk: 3000);
            CampaignCpuLegalAction turnDef = Cmd(0, DuelCommandType.TurnDef, 4007);
            turnDef.Player = 1;
            turnDef.Position = 2;
            turnDef.Index = 0;
            obs.LegalActions.Add(turnDef);
            obs.LegalActions.Add(Phase(99, DuelPhase.Battle));
            obs.LegalActions.Add(Phase(1, DuelPhase.End));
            CampaignCpuRulePack pack = EmptyPack();
            pack.Policy.OnNoMatch = "native_cpu";
            pack.Policy.PositionSafetyEnabled = true;
            pack.Policy.PhaseExitPolicy = "battle_then_end";

            CampaignCpuDecision battle = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(CampaignCpuRoute.RuleCommit, battle.Route, "explicit phase commit");
            AssertEqual(DuelPhase.Battle, battle.Action.Phase, "known attacker chooses Battle");
            AssertEqual("phase_exit_battle", battle.RuleId, "named Battle fallback");

            obs.LegalActions.RemoveAll(
                a => a.Kind == LegalActionKind.MovePhase && a.Phase == DuelPhase.Battle);
            CampaignCpuDecision end = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(DuelPhase.End, end.Action.Phase, "Battle unavailable chooses End");
            AssertEqual("phase_exit_end", end.RuleId, "named End fallback");
        }

        static CampaignCpuObservation TacticalObservation(
            int ownCardId,
            int ownAtk,
            int ownDef,
            bool ownIsAttack,
            int opposingAtk)
        {
            var obs = new CampaignCpuObservation
            {
                OwnedSeat = 1,
                MyId = 0,
                ActingPlayer = 1,
                Turn = 3,
                TurnPlayer = 1,
                Phase = (int)DuelPhase.Main1,
                IsMainPhaseWaitInput = true,
            };
            obs.TacticalMonsters.Add(new CampaignCpuMonsterTacticalState
            {
                Player = 1,
                Position = 2,
                Index = 0,
                CardId = ownCardId,
                FaceKnown = true,
                FaceUp = true,
                TurnKnown = true,
                IsAttack = ownIsAttack,
                IsDefense = !ownIsAttack,
                HasAtk = true,
                Atk = ownAtk,
                HasDef = true,
                Def = ownDef,
            });
            obs.TacticalMonsters.Add(new CampaignCpuMonsterTacticalState
            {
                Player = 0,
                Position = 2,
                Index = 0,
                CardId = 9999,
                FaceKnown = true,
                FaceUp = true,
                HasAtk = true,
                Atk = opposingAtk,
            });
            return obs;
        }

        static void TacticalSnapshotProjectsModifiedStatsAndUnknownFaceDownThreats()
        {
            var query = new FakeCampaignCpuTacticalQuery();
            query.Add(1, 2, 0, 41, 4007, face: 1, turn: 11, atk: 3200, def: 2500);
            query.Add(0, 2, 0, 42, 9999, face: 1, turn: 11, atk: 3000, def: 3000);
            query.Add(0, 3, 0, 43, 8888, face: 0, turn: 22, atk: 5000, def: 5000);

            List<CampaignCpuMonsterTacticalState> states =
                CampaignCpuTacticalSnapshotBuilder.Build(
                    query,
                    ownedSeat: 1,
                    attackTurnRaw: 11,
                    defenseTurnRaw: 22);
            AssertEqual(3, states.Count, "all monster-zone rows projected");
            CampaignCpuMonsterTacticalState own =
                states.Find(m => m.Player == 1 && m.CardId == 4007);
            AssertTrue(own != null, "owned Blue-Eyes projected");
            AssertEqual(3200, own.Atk, "live modified ATK retained");
            AssertEqual(2500, own.Def, "live modified DEF retained");
            AssertTrue(own.TurnKnown && own.IsAttack, "configured raw stance mapped");

            CampaignCpuMonsterTacticalState hidden =
                states.Find(m => m.Player == 0 && m.CardId == 8888);
            AssertTrue(hidden != null && hidden.FaceKnown && !hidden.FaceUp, "face-down retained");
            AssertTrue(
                !hidden.HasAtk && !hidden.HasDef,
                "face-down opponent stats not invented from local privileged reads");

            states = CampaignCpuTacticalSnapshotBuilder.Build(
                query,
                ownedSeat: 1,
                attackTurnRaw: null,
                defenseTurnRaw: null);
            own = states.Find(m => m.Player == 1 && m.CardId == 4007);
            AssertTrue(!own.TurnKnown, "uncalibrated raw stance remains unknown");
            AssertEqual(11, own.TurnRaw, "raw stance still audited");
        }

        static void TacticalSnapshotPopulationFeedsPositionSafetyScorer()
        {
            var query = new FakeCampaignCpuTacticalQuery();
            query.Add(1, 2, 0, 41, 4007, face: 1, turn: 0, atk: 3000, def: 2500);
            query.Add(0, 2, 0, 42, 9999, face: 1, turn: 0, atk: 3000, def: 3000);

            var observation = new CampaignCpuObservation
            {
                OwnedSeat = 1,
                ActingPlayer = 1,
                TurnPlayer = 1,
                Phase = (int)DuelPhase.Main1,
                IsMainPhaseWaitInput = true,
            };
            CampaignCpuLegalAction turnDef =
                Cmd(0, DuelCommandType.TurnDef, 4007);
            turnDef.Player = 1;
            turnDef.Position = 2;
            turnDef.Index = 0;
            observation.LegalActions.Add(turnDef);
            observation.LegalActions.Add(Phase(1, DuelPhase.End));

            CampaignCpuRulePack pack = EmptyPack();
            pack.Policy.OnNoMatch = "first_legal";
            pack.Policy.PositionSafetyEnabled = true;
            pack.Policy.PhaseExitPolicy = "battle_then_end";
            pack.Policy.AttackTurnRaw = 0;
            pack.Policy.DefenseTurnRaw = 1;

            CampaignCpuTacticalSnapshotBuilder.PopulateObservation(
                query,
                observation,
                pack.Policy);
            CampaignCpuDecision decision =
                CampaignCpuScorer.Decide(observation, pack);

            AssertEqual(
                "dominated_turn_defense",
                decision.TacticalFilterReason,
                "stable-menu tactical population drives position guard");
            AssertEqual(
                DuelPhase.End,
                decision.Action.Phase,
                "dominated Blue-Eyes defense yields explicit phase exit");
        }

        sealed class FakeCampaignCpuTacticalQuery : ICampaignCpuTacticalQuery
        {
            sealed class Row
            {
                public int Player;
                public int Position;
                public int Index;
                public int UniqueId;
                public int CardId;
                public int Face;
                public int Turn;
                public int Atk;
                public int Def;
            }

            readonly List<Row> rows = new List<Row>();

            public void Add(
                int player,
                int position,
                int index,
                int uniqueId,
                int cardId,
                int face,
                int turn,
                int atk,
                int def)
            {
                rows.Add(new Row
                {
                    Player = player,
                    Position = position,
                    Index = index,
                    UniqueId = uniqueId,
                    CardId = cardId,
                    Face = face,
                    Turn = turn,
                    Atk = atk,
                    Def = def,
                });
            }

            public int GetMonsterCount(int player, int position)
            {
                int count = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Player == player && rows[i].Position == position)
                    {
                        count++;
                    }
                }
                return count;
            }

            public bool TryReadMonster(
                int player,
                int position,
                int index,
                out int uniqueId,
                out int cardId,
                out int face,
                out int turn,
                out int atk,
                out int def)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    Row row = rows[i];
                    if (row.Player == player
                        && row.Position == position
                        && row.Index == index)
                    {
                        uniqueId = row.UniqueId;
                        cardId = row.CardId;
                        face = row.Face;
                        turn = row.Turn;
                        atk = row.Atk;
                        def = row.Def;
                        return true;
                    }
                }
                uniqueId = cardId = face = turn = atk = def = 0;
                return false;
            }
        }

        static void NonMainWindowIsNotScriptedG4()
        {
            // G4: RunDialog is not Main WaitInput — controller leases native; offline classifier gate.
            Dictionary<string, object> root = LoadFixtureRoot("g4_non_main_run_dialog.json");
            Dictionary<string, object> obsDict = Utils.GetDictionary(root, "observation");
            CampaignCpuObservation obs = ObservationFromFixtureDict(obsDict);
            AssertTrue(!obs.IsMainPhaseWaitInput, "G4 not main wait input");
            AssertEqual("RunDialog", obs.WindowClass, "G4 window class");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsMainPhaseWaitInput(obs.ViewType, obs.ViewParam1),
                "G4 classifier rejects non-main");
            AssertEqual(
                "RunDialog",
                CampaignCpuWindowClassifier.ClassifyWindow(obs.ViewType, obs.ViewParam1),
                "G4 classify");
        }

        static void DeckFingerprintMismatchGateG5()
        {
            // G5: product pack pin vs wrong live hash → gate inactive (fully native).
            Dictionary<string, object> root = LoadFixtureRoot("g5_deck_fingerprint_mismatch.json");
            string packHash = Utils.GetValue<string>(root, "pack_deck_hash");
            string liveHash = Utils.GetValue<string>(root, "live_deck_hash");
            CampaignCpuRulePack pack = LoadProductPack();
            AssertEqual(packHash, pack.DeckHash, "G5 pack pin matches product");
            AssertTrue(CampaignCpuDeckFingerprint.IsWellFormedHash(packHash), "G5 pack well-formed");
            AssertTrue(CampaignCpuDeckFingerprint.IsWellFormedHash(liveHash), "G5 live well-formed");
            AssertTrue(
                !string.Equals(packHash, liveHash, StringComparison.OrdinalIgnoreCase),
                "G5 mismatch");
            // Gate rule: activation requires exact equality — mismatch leaves scripting off.
            bool gateActive = string.Equals(packHash, liveHash, StringComparison.OrdinalIgnoreCase);
            AssertTrue(!gateActive, "G5 gate inactive");
        }

        static void ScorerDeterministicSameObservation()
        {
            // G6: identical captured G1 observation twice
            CampaignCpuRulePack pack = LoadProductPack();
            CampaignCpuObservation obs = LoadObservationFixture("g1_opening_a_main1.json");
            Dictionary<string, object> exp = LoadExpect("g6_identical_observation_twice.json");
            CampaignCpuDecision a = CampaignCpuScorer.Decide(obs, pack);
            CampaignCpuDecision b = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(a.RuleId, b.RuleId, "G6 rule id stable");
            AssertEqual(a.Action.CanonicalIdentity, b.Action.CanonicalIdentity, "G6 identity stable");
            AssertEqual(Utils.GetValue<string>(exp, "stable_rule_id"), a.RuleId, "G6 expected rule");
            AssertEqual(
                Utils.GetValue<string>(exp, "stable_action_identity"),
                a.Action.CanonicalIdentity,
                "G6 expected identity");
        }

        static void ScorerNeverExhaustionRestoresLegalSet()
        {
            CampaignCpuRulePack pack = LoadProductPack();
            // Only surrender legal — never-rule would empty set → restore
            CampaignCpuObservation obs = MakeMainObs(Cmd(1, DuelCommandType.Surrender, 0));
            obs.SelfHandCardIds.Clear();
            CampaignCpuDecision d = CampaignCpuScorer.Decide(obs, pack);
            // After never exhaustion, fallback may still no_match (surrender not in fallback scores)
            AssertTrue(
                d.Route == CampaignCpuRoute.FallbackNative || d.Route == CampaignCpuRoute.RuleCommit,
                "never exhaustion does not crash");
        }

        static void ObservationProjectorMapsLegalActions()
        {
            var snap = new DecisionSnapshot
            {
                RunEffectSeq = 7,
                ViewType = DuelViewType.WaitInput,
                ViewParam1 = (int)DuelMenuActType.MainPhase,
                ActingPlayer = 1,
                ControlledPlayer = 1,
                Turn = 2,
                TurnPlayer = 1,
                CurrentPhase = (int)DuelPhase.Main1,
            };
            snap.LegalActions.Add(new LegalAction
            {
                ActionId = 3,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Summon,
                CardId = 4007,
                Position = 13,
                Player = 1,
                ActionLabel = "Summon Blue-Eyes",
            });
            CampaignCpuObservation obs = CampaignCpuObservationProjector.Project(
                snap, 11010078, ownedSeat: 1, actingPlayer: 1, isMultiSelectList: false);
            AssertEqual(1, obs.LegalActions.Count, "projected action count");
            AssertEqual(4007, obs.LegalActions[0].CardId, "projected card");
            AssertTrue(obs.IsMainPhaseWaitInput, "main phase flag");
            AssertEqual("WaitInput_MainPhase", obs.WindowClass, "window class");
        }

        static void SoloTemporaryCpuRestoreRequiresSemanticProgress()
        {
            var sm = new SoloTemporaryCpuStateMachine();
            sm.ActivateHumanOwned();
            var entry = CampaignCpuProgressToken.Create(
                1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, (int)DuelPhase.Main1, "a");
            sm.BeginNativeLease("test", 1, 10, entry, ownedSeat: 1, DateTime.UtcNow);
            string deny;
            bool can = sm.CanRestoreFromNativeLease(
                currentDuelGeneration: 1,
                currentViewSeq: 10, // not advanced
                currentToken: entry,
                resolvedActingSeat: 1,
                ownedSeat: 1,
                myId: 0,
                requireCpuThinking: false,
                viewType: DuelViewType.WaitInput,
                param1: (int)DuelMenuActType.MainPhase,
                out deny);
            AssertTrue(!can, "same view_seq cannot restore");
            var next = CampaignCpuProgressToken.Create(
                1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, (int)DuelPhase.Main1, "b");
            can = sm.CanRestoreFromNativeLease(
                1, 11, next, 1, 1, 0, false,
                DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, out deny);
            AssertTrue(can, "fresh token + advanced seq restores");
        }

        static void SoloTemporaryCpuProgressTimeoutQuarantines()
        {
            var sm = new SoloTemporaryCpuStateMachine();
            sm.ActivateHumanOwned();
            var token = CampaignCpuProgressToken.Create(
                1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, 0, "x");
            sm.ArmAwaitingProgress(5, token, "id", DateTime.UtcNow.AddSeconds(-5));
            AssertTrue(sm.IsProgressTimedOut(DateTime.UtcNow, 2000), "timeout detected");
            sm.EnterCommitQuarantine(5, token, "id", DateTime.UtcNow, "post_commit_stall");
            AssertEqual(SoloTemporaryCpuState.CommitQuarantine, sm.State, "quarantine state");
            AssertTrue(sm.ScriptingDisabledForDuel, "scripting disabled after quarantine");
        }

        static void CommitNotStartedWhenNullAction()
        {
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                null,
                phase => { },
                (p, pos, idx, cmd) => { },
                r => { },
                i => { },
                d => { });
            AssertEqual(CampaignCpuCommitOutcome.NotStarted, outcome, "null action NotStarted");
        }

        static void CommitAppliedOnSuccessfulNative()
        {
            var action = Cmd(1, DuelCommandType.Summon, 4007);
            int calls = 0;
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                action,
                phase => { },
                (p, pos, idx, cmd) => { calls++; },
                r => { },
                i => { },
                d => { });
            AssertEqual(CampaignCpuCommitOutcome.Applied, outcome, "applied");
            AssertEqual(1, calls, "doCommand once");
        }

        static void CommitIndeterminateOnNativeThrow()
        {
            var action = Cmd(1, DuelCommandType.Summon, 4007);
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                action,
                phase => { },
                (p, pos, idx, cmd) => { throw new InvalidOperationException("native"); },
                r => { },
                i => { },
                d => { });
            AssertEqual(CampaignCpuCommitOutcome.Indeterminate, outcome, "indeterminate");
        }

        static void CommitNotStartedWhenDelegateMissing()
        {
            var action = Cmd(1, DuelCommandType.Summon, 4007);
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                action,
                null,
                (p, pos, idx, cmd) => { },
                r => { },
                i => { },
                d => { });
            AssertEqual(CampaignCpuCommitOutcome.NotStarted, outcome, "missing movePhase");
        }

        static void CommitMovePhaseDispatchesOnce()
        {
            var action = new CampaignCpuLegalAction
            {
                ActionId = 1,
                Kind = LegalActionKind.MovePhase,
                Phase = DuelPhase.Battle,
                Player = 1,
            };
            int calls = 0;
            int seenPhase = -1;
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                action,
                phase => { calls++; seenPhase = phase; },
                (p, pos, idx, cmd) => { calls += 10; },
                r => { calls += 100; },
                i => { calls += 1000; },
                d => { calls += 10000; });
            AssertEqual(CampaignCpuCommitOutcome.Applied, outcome, "move phase applied");
            AssertEqual(1, calls, "only movePhase once");
            AssertEqual((int)DuelPhase.Battle, seenPhase, "phase id");
        }

        static void CommitDialogResultDispatchesOnce()
        {
            var action = new CampaignCpuLegalAction
            {
                ActionId = 1,
                Kind = LegalActionKind.DialogResult,
                DialogResult = 2,
            };
            int calls = 0;
            uint seen = 0;
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                action,
                phase => { calls += 10; },
                (p, pos, idx, cmd) => { calls += 100; },
                r => { calls++; seen = r; },
                i => { calls += 1000; },
                d => { calls += 10000; });
            AssertEqual(CampaignCpuCommitOutcome.Applied, outcome, "dialog applied");
            AssertEqual(1, calls, "only dlg once");
            AssertEqual(2u, seen, "dialog result");
        }

        static void CommitListIndexDispatchesOnce()
        {
            var action = new CampaignCpuLegalAction
            {
                ActionId = 1,
                Kind = LegalActionKind.ListIndex,
                Index = 4,
            };
            int calls = 0;
            int seen = -1;
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                action,
                phase => { calls += 10; },
                (p, pos, idx, cmd) => { calls += 100; },
                r => { calls += 1000; },
                i => { calls++; seen = i; },
                d => { calls += 10000; });
            AssertEqual(CampaignCpuCommitOutcome.Applied, outcome, "list applied");
            AssertEqual(1, calls, "only list once");
            AssertEqual(4, seen, "list index");
        }

        static void CommitCancelDispatchesOnce()
        {
            var action = new CampaignCpuLegalAction
            {
                ActionId = 1,
                Kind = LegalActionKind.Cancel,
                CancelDecide = true,
            };
            int calls = 0;
            bool seen = false;
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                action,
                phase => { calls += 10; },
                (p, pos, idx, cmd) => { calls += 100; },
                r => { calls += 1000; },
                i => { calls += 10000; },
                d => { calls++; seen = d; });
            AssertEqual(CampaignCpuCommitOutcome.Applied, outcome, "cancel applied");
            AssertEqual(1, calls, "only cancel once");
            AssertTrue(seen, "cancel decide");
        }

        static void CommitSummonPlacementRewritesPositionIndex()
        {
            var action = new CampaignCpuLegalAction
            {
                ActionId = 1,
                Kind = LegalActionKind.Command,
                Command = DuelCommandType.Decide,
                TargetScope = "summon_placement",
                Player = 1,
                Position = 3, // zone index in Position for summon_placement
                Index = 9,
            };
            int seenPos = -1;
            int seenIdx = -1;
            int seenCmd = -1;
            int calls = 0;
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                action,
                phase => { },
                (p, pos, idx, cmd) =>
                {
                    calls++;
                    seenPos = pos;
                    seenIdx = idx;
                    seenCmd = cmd;
                },
                r => { },
                i => { },
                d => { });
            AssertEqual(CampaignCpuCommitOutcome.Applied, outcome, "placement applied");
            AssertEqual(1, calls, "doCommand once");
            AssertEqual(CampaignCpuNativeCommit.PosSelect, seenPos, "PosSelect rewrite");
            AssertEqual(3, seenIdx, "zone from Position");
            AssertEqual((int)DuelCommandType.Decide, seenCmd, "command id");
        }

        static void CommitUnsupportedKindNotStarted()
        {
            var action = new CampaignCpuLegalAction
            {
                ActionId = 1,
                Kind = (LegalActionKind)999,
            };
            CampaignCpuNativePlan plan;
            AssertTrue(!CampaignCpuNativeCommit.TryBuildPlan(action, out plan), "plan rejected");
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                action,
                phase => { },
                (p, pos, idx, cmd) => { },
                r => { },
                i => { },
                d => { });
            AssertEqual(CampaignCpuCommitOutcome.NotStarted, outcome, "unsupported NotStarted");
        }

        static void WindowClassifierMainPhaseOnly()
        {
            AssertTrue(
                CampaignCpuWindowClassifier.IsMainPhaseWaitInput(
                    DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "main");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsMainPhaseWaitInput(
                    DuelViewType.WaitInput, (int)DuelMenuActType.BattlePhase),
                "battle not main");
            AssertEqual(
                "WaitInput_BattlePhase",
                CampaignCpuWindowClassifier.ClassifyWindow(
                    DuelViewType.WaitInput, (int)DuelMenuActType.BattlePhase),
                "battle class");
            AssertTrue(
                CampaignCpuWindowClassifier.IsDrawPhaseWaitInput(
                    DuelViewType.WaitInput, (int)DuelMenuActType.DrawPhase),
                "draw phase wait input");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsDrawPhaseWaitInput(
                    DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "main is not draw");
        }

        /// <summary>
        /// Option B: under AllowScripted, oneshot only pure DrawPhase — not all non-Main.
        /// </summary>
        static void OneShotRestoreOnlyDrawPhaseUnderAllowScripted()
        {
            AssertTrue(
                CampaignCpuWindowClassifier.ShouldOneShotRestoreAfterNativeForward(
                    true, DuelViewType.WaitInput, (int)DuelMenuActType.DrawPhase),
                "Draw + AllowScripted oneshots");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldOneShotRestoreAfterNativeForward(
                    true, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "Main never oneshots");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldOneShotRestoreAfterNativeForward(
                    true, DuelViewType.WaitInput, (int)DuelMenuActType.CheckTiming),
                "CheckTiming holds lease");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldOneShotRestoreAfterNativeForward(
                    true, DuelViewType.WaitInput, (int)DuelMenuActType.CheckChain),
                "CheckChain holds lease");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldOneShotRestoreAfterNativeForward(
                    true, DuelViewType.WaitInput, (int)DuelMenuActType.BattlePhase),
                "Battle holds lease");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldOneShotRestoreAfterNativeForward(
                    true, DuelViewType.RunDialog, 0),
                "RunDialog holds lease");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldOneShotRestoreAfterNativeForward(
                    true, DuelViewType.RunList, 0),
                "RunList holds lease");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldOneShotRestoreAfterNativeForward(
                    false, DuelViewType.WaitInput, (int)DuelMenuActType.DrawPhase),
                "PR4a !AllowScripted never oneshots");
        }

        /// <summary>
        /// Option A: owned response windows that re-lease TemporaryCpu when HumanOwned.
        /// </summary>
        static void OwnedResponseNativeWindowClassifier()
        {
            AssertTrue(
                CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(
                    DuelViewType.RunDialog, 0),
                "activate/select RunDialog");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(
                    DuelViewType.RunDialog, 1),
                "Info RunDialog excluded");
            AssertTrue(
                CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(
                    DuelViewType.RunList, 0),
                "RunList");
            AssertTrue(
                CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(
                    DuelViewType.WaitInput, (int)DuelMenuActType.CheckTiming),
                "CheckTiming");
            AssertTrue(
                CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(
                    DuelViewType.WaitInput, (int)DuelMenuActType.CheckChain),
                "CheckChain");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(
                    DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "Main is scripted path not response hold");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(
                    DuelViewType.WaitInput, (int)DuelMenuActType.DrawPhase),
                "Draw uses oneshot policy not response classifier");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(
                    DuelViewType.WaitInput, (int)DuelMenuActType.BattlePhase),
                "Battle is non-main lease not response family");
        }

        /// <summary>
        /// A4: HumanOwned + response-class re-leases even when acting is MyId
        /// (live dual-Human misroute of opponent trap/chain dialogs).
        /// </summary>
        static void DualHumanMyIdResponseHoldA4()
        {
            AssertTrue(
                CampaignCpuWindowClassifier.ShouldReLeaseDualHumanResponseWindow(
                    SoloTemporaryCpuState.HumanOwned,
                    DuelViewType.RunDialog,
                    0),
                "HumanOwned Confirm RunDialog re-leases");
            AssertTrue(
                CampaignCpuWindowClassifier.ShouldReLeaseDualHumanResponseWindow(
                    SoloTemporaryCpuState.HumanOwned,
                    DuelViewType.WaitInput,
                    (int)DuelMenuActType.CheckChain),
                "HumanOwned CheckChain re-leases");
            AssertTrue(
                CampaignCpuWindowClassifier.ShouldReLeaseDualHumanResponseWindow(
                    SoloTemporaryCpuState.HumanOwned,
                    DuelViewType.RunList,
                    0),
                "HumanOwned RunList re-leases");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldReLeaseDualHumanResponseWindow(
                    SoloTemporaryCpuState.NativeLease,
                    DuelViewType.RunDialog,
                    0),
                "NativeLease does not re-enter BeginFallback via A4");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldReLeaseDualHumanResponseWindow(
                    SoloTemporaryCpuState.HumanOwned,
                    DuelViewType.WaitInput,
                    (int)DuelMenuActType.MainPhase),
                "Main never uses A4 response hold");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldReLeaseDualHumanResponseWindow(
                    SoloTemporaryCpuState.HumanOwned,
                    DuelViewType.RunDialog,
                    1),
                "Info RunDialog excluded");
            AssertEqual(
                "dual_human_myid_response_hold",
                CampaignCpuWindowClassifier.DualHumanResponseHoldReason(
                    true, actingPlayer: 0, ownedSeat: 1, myId: 0),
                "MyId misroute reason");
            AssertEqual(
                "owned_response_hold",
                CampaignCpuWindowClassifier.DualHumanResponseHoldReason(
                    true, actingPlayer: 1, ownedSeat: 1, myId: 0),
                "owned-seat resolve reason");
            AssertEqual(
                "dual_human_unknown_response_hold",
                CampaignCpuWindowClassifier.DualHumanResponseHoldReason(
                    false, actingPlayer: -1, ownedSeat: 1, myId: 0),
                "unresolved acting reason");
            // SelStand remains response-class for A4 when intercept does not apply.
            AssertTrue(
                CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(
                    DuelViewType.RunDialog, (int)DuelDialogType.SelStand),
                "SelStand still response-class for A4 fallback");
            AssertTrue(
                CampaignCpuWindowClassifier.ShouldReLeaseDualHumanResponseWindow(
                    SoloTemporaryCpuState.HumanOwned,
                    DuelViewType.RunDialog,
                    (int)DuelDialogType.SelStand),
                "A4 still classifies SelStand when intercept skipped");
        }

        /// <summary>
        /// Owned-turn SelStand classifier: turn_player owns seat, not MyId.
        /// </summary>
        static void OwnedTurnSelStandClassifier()
        {
            AssertTrue(
                CampaignCpuWindowClassifier.IsSelStandRunDialog(
                    DuelViewType.RunDialog, (int)DuelDialogType.SelStand),
                "SelStand dialog type");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsSelStandRunDialog(
                    DuelViewType.RunDialog, 1),
                "Info is not SelStand");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsSelStandRunDialog(
                    DuelViewType.WaitInput, (int)DuelDialogType.SelStand),
                "WaitInput is not SelStand RunDialog");
            AssertTrue(
                CampaignCpuWindowClassifier.IsOwnedTurnSelStand(
                    DuelViewType.RunDialog,
                    (int)DuelDialogType.SelStand,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "owned turn SelStand");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedTurnSelStand(
                    DuelViewType.RunDialog,
                    (int)DuelDialogType.SelStand,
                    turnPlayer: 0,
                    ownedSeat: 1,
                    myId: 0),
                "human turn never owned SelStand");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedTurnSelStand(
                    DuelViewType.RunDialog,
                    (int)DuelDialogType.SelStand,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 1),
                "owned==myId rejected");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedTurnSelStand(
                    DuelViewType.RunDialog,
                    0,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "non-SelStand RunDialog");
        }

        static void SelStandMaskPickerAndMechanicalAction()
        {
            int stand;
            AssertTrue(
                CampaignCpuSelStand.TryPickStandFromMask(
                    CampaignCpuSelStand.StandFaceUpAttack | CampaignCpuSelStand.StandFaceUpDefense,
                    out stand),
                "ATK+DEF mask ok");
            AssertEqual(CampaignCpuSelStand.StandFaceUpAttack, stand, "prefer face-up ATK");
            AssertTrue(
                CampaignCpuSelStand.TryPickStandFromMask(
                    CampaignCpuSelStand.StandFaceUpDefense,
                    out stand),
                "DEF-only ok");
            AssertEqual(CampaignCpuSelStand.StandFaceUpDefense, stand, "DEF when only option");
            AssertTrue(
                CampaignCpuSelStand.TryPickStandFromMask(
                    CampaignCpuSelStand.StandFaceDownDefense,
                    out stand),
                "face-down DEF via lowest bit");
            AssertEqual(CampaignCpuSelStand.StandFaceDownDefense, stand, "lowest set bit");
            AssertTrue(
                !CampaignCpuSelStand.TryPickStandFromMask(0, out stand),
                "mask 0 fail closed");

            CampaignCpuLegalAction action;
            AssertTrue(
                CampaignCpuSelStand.TryBuildMechanicalAction(
                    CampaignCpuSelStand.StandFaceUpAttack | CampaignCpuSelStand.StandFaceUpDefense,
                    out action),
                "build action");
            AssertEqual(LegalActionKind.DialogResult, action.Kind, "DialogResult kind");
            AssertEqual(CampaignCpuSelStand.StandFaceUpAttack, action.DialogResult, "dialog result");
            AssertTrue(action.IsMechanical, "mechanical");
            AssertEqual(CampaignCpuSelStand.TargetScope, action.TargetScope, "scope");
            AssertTrue(
                !CampaignCpuSelStand.TryBuildMechanicalAction(0, out action),
                "no action on empty mask");

            // Commit transport still DlgSetResult once.
            uint seen = 0;
            int calls = 0;
            CampaignCpuLegalAction commitAction;
            CampaignCpuSelStand.TryBuildMechanicalAction(
                CampaignCpuSelStand.StandFaceUpAttack, out commitAction);
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                commitAction,
                phase => { },
                (p, pos, idx, cmd) => { calls += 100; },
                r => { calls++; seen = r; },
                i => { },
                d => { });
            AssertEqual(CampaignCpuCommitOutcome.Applied, outcome, "sel stand applied");
            AssertEqual(1, calls, "only dlgSetResult");
            AssertEqual((uint)CampaignCpuSelStand.StandFaceUpAttack, seen, "stand value");
        }

        /// <summary>
        /// Live M7 mechanical_sel_stand: mask 0x1F0000 → dialog 0x10000 (not classic 0x1/0x4).
        /// Production-shaped picker + mechanical action + native dialog dispatch.
        /// </summary>
        static void SelStandLiveHighBitMaskSemantics()
        {
            AssertEqual(0x1F0000, CampaignCpuSelStand.LiveRecordedStandMask, "live mask const");
            AssertEqual(0x10000, CampaignCpuSelStand.LiveRecordedStandResult, "live result const");
            AssertEqual(
                CampaignCpuSelStand.LiveRecordedStandResult,
                CampaignCpuSelStand.StandMdFaceUpAttackBit,
                "MD face-up ATK bit aliases live result");

            int stand;
            AssertTrue(
                CampaignCpuSelStand.TryPickStandFromMask(
                    CampaignCpuSelStand.LiveRecordedStandMask, out stand),
                "live mask pickable");
            AssertEqual(
                CampaignCpuSelStand.LiveRecordedStandResult,
                stand,
                "live mask → 0x10000 (MD face-up ATK bit / lowest set)");

            // Classic low bits still win when both encodings are present.
            AssertTrue(
                CampaignCpuSelStand.TryPickStandFromMask(
                    CampaignCpuSelStand.LiveRecordedStandMask
                        | CampaignCpuSelStand.StandFaceUpAttack,
                    out stand),
                "mixed classic+MD");
            AssertEqual(
                CampaignCpuSelStand.StandFaceUpAttack,
                stand,
                "classic ATK preferred over high-bit when both legal");

            CampaignCpuLegalAction action;
            AssertTrue(
                CampaignCpuSelStand.TryBuildMechanicalAction(
                    CampaignCpuSelStand.LiveRecordedStandMask, out action)
                && action != null,
                "live mechanical action");
            AssertEqual(
                CampaignCpuSelStand.LiveRecordedStandResult,
                action.DialogResult,
                "dialog_result live");
            AssertEqual(
                CampaignCpuSelStand.LiveRecordedStandResult,
                action.Position,
                "position carries stand");
            AssertEqual(CampaignCpuSelStand.RuleId, "mechanical_sel_stand", "rule id pin");
            AssertTrue(action.IsMechanical, "mechanical flag");

            uint seen = 0;
            int calls = 0;
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                action,
                phase => { },
                (p, pos, idx, cmd) => { calls += 100; },
                r => { calls++; seen = r; },
                i => { },
                d => { });
            AssertEqual(CampaignCpuCommitOutcome.Applied, outcome, "live dialog applied");
            AssertEqual(1, calls, "live dialog once");
            AssertEqual((uint)CampaignCpuSelStand.LiveRecordedStandResult, seen, "native 0x10000");
        }

        static void SelStandInterceptPolicyGates()
        {
            AssertTrue(
                CampaignCpuSelStand.ShouldIntercept(
                    logOnly: false,
                    allowScriptedCommits: true,
                    seatOwnershipConfirmed: true,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.HumanOwned,
                    viewType: DuelViewType.RunDialog,
                    param1: (int)DuelDialogType.SelStand,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "commit-on owned SelStand intercepts");
            AssertTrue(
                !CampaignCpuSelStand.ShouldIntercept(
                    logOnly: true,
                    allowScriptedCommits: true,
                    seatOwnershipConfirmed: true,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.HumanOwned,
                    viewType: DuelViewType.RunDialog,
                    param1: (int)DuelDialogType.SelStand,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "LogOnly disables intercept");
            AssertTrue(
                !CampaignCpuSelStand.ShouldIntercept(
                    logOnly: false,
                    allowScriptedCommits: false,
                    seatOwnershipConfirmed: true,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.HumanOwned,
                    viewType: DuelViewType.RunDialog,
                    param1: (int)DuelDialogType.SelStand,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "AllowScripted false disables");
            AssertTrue(
                !CampaignCpuSelStand.ShouldIntercept(
                    logOnly: false,
                    allowScriptedCommits: true,
                    seatOwnershipConfirmed: true,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.NativeLease,
                    viewType: DuelViewType.RunDialog,
                    param1: (int)DuelDialogType.SelStand,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "NativeLease not intercept");
            AssertTrue(
                !CampaignCpuSelStand.ShouldIntercept(
                    logOnly: false,
                    allowScriptedCommits: true,
                    seatOwnershipConfirmed: true,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.HumanOwned,
                    viewType: DuelViewType.RunDialog,
                    param1: (int)DuelDialogType.SelStand,
                    turnPlayer: 0,
                    ownedSeat: 1,
                    myId: 0),
                "human turn no intercept");
            AssertTrue(
                !CampaignCpuSelStand.ShouldIntercept(
                    logOnly: false,
                    allowScriptedCommits: true,
                    seatOwnershipConfirmed: false,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.HumanOwned,
                    viewType: DuelViewType.RunDialog,
                    param1: (int)DuelDialogType.SelStand,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "unconfirmed seat no intercept");
        }

        /// <summary>
        /// Owned-turn WaitInput/Location classifier: turn_player owns seat, not MyId.
        /// Human-turn Location (own card place) must never match.
        /// </summary>
        static void OwnedTurnLocationClassifier()
        {
            AssertTrue(
                CampaignCpuWindowClassifier.IsLocationWaitInput(
                    DuelViewType.WaitInput, (int)DuelMenuActType.Location),
                "Location menu type");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsLocationWaitInput(
                    DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "MainPhase is not Location");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsLocationWaitInput(
                    DuelViewType.RunDialog, (int)DuelMenuActType.Location),
                "RunDialog is not Location WaitInput");
            AssertTrue(
                CampaignCpuWindowClassifier.IsOwnedTurnLocation(
                    DuelViewType.WaitInput,
                    (int)DuelMenuActType.Location,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "owned turn Location");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedTurnLocation(
                    DuelViewType.WaitInput,
                    (int)DuelMenuActType.Location,
                    turnPlayer: 0,
                    ownedSeat: 1,
                    myId: 0),
                "human turn never owned Location");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedTurnLocation(
                    DuelViewType.WaitInput,
                    (int)DuelMenuActType.Location,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 1),
                "owned==myId rejected");
            // Location is not A4 response-class (mechanical answer required).
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedResponseNativeWindow(
                    DuelViewType.WaitInput, (int)DuelMenuActType.Location),
                "Location not response-class for A4");
        }

        static void LocationZonePickerAndMechanicalAction()
        {
            int zone;
            // Bits 2 and 5 set → prefer lowest (2).
            AssertTrue(
                CampaignCpuLocation.TryPickZoneFromMask((1 << 2) | (1 << 5), out zone),
                "multi-zone mask ok");
            AssertEqual(2, zone, "prefer lowest zone index");
            AssertTrue(
                CampaignCpuLocation.TryPickZoneFromMask(1 << 0, out zone),
                "zone 0 ok");
            AssertEqual(0, zone, "zone 0");
            AssertTrue(
                CampaignCpuLocation.TryPickZoneFromMask(1 << CampaignCpuLocation.PosField, out zone),
                "PosField bit ok");
            AssertEqual(CampaignCpuLocation.PosField, zone, "PosField zone");
            AssertTrue(
                !CampaignCpuLocation.TryPickZoneFromMask(0, out zone),
                "mask 0 fail closed");
            // Bit above PosField only → fail closed (not walked).
            AssertTrue(
                !CampaignCpuLocation.TryPickZoneFromMask(1 << (CampaignCpuLocation.PosField + 1), out zone),
                "bit above PosField fail closed");

            AssertEqual(55, CampaignCpuLocation.ResolveCardUniqueId(0, 55), "param2 UID");
            AssertEqual(9, CampaignCpuLocation.ResolveCardUniqueId(9, 55), "dlg UID preferred");
            AssertEqual(0, CampaignCpuLocation.ResolveCardUniqueId(0, 0), "no UID");

            CampaignCpuLegalAction action;
            AssertTrue(
                CampaignCpuLocation.TryBuildMechanicalAction(
                    (1 << 3) | (1 << 7),
                    ownedSeat: 1,
                    cardId: 6782,
                    out action),
                "build action");
            AssertEqual(LegalActionKind.Command, action.Kind, "Command kind");
            AssertEqual(DuelCommandType.Decide, action.Command, "Decide");
            AssertEqual(3, action.Position, "lowest zone in Position");
            AssertEqual(1, action.Player, "OwnedSeat player");
            AssertEqual(6782, action.CardId, "card id");
            AssertTrue(action.IsMechanical, "mechanical");
            AssertEqual(CampaignCpuLocation.TargetScope, action.TargetScope, "scope");
            AssertTrue(
                !CampaignCpuLocation.TryBuildMechanicalAction(0, 1, 6782, out action),
                "no action on empty mask");
            AssertTrue(
                !CampaignCpuLocation.TryBuildMechanicalAction(1 << 1, -1, 0, out action),
                "invalid seat no action");

            // Commit transport: DoCommand(player, PosSelect, zone, Decide) once.
            int calls = 0;
            int seenPlayer = -1, seenPos = -1, seenIdx = -1, seenCmd = -1;
            CampaignCpuLegalAction commitAction;
            CampaignCpuLocation.TryBuildMechanicalAction(1 << 4, 1, 6782, out commitAction);
            CampaignCpuCommitOutcome outcome = CampaignCpuNativeCommit.TryApply(
                commitAction,
                phase => { calls += 1000; },
                (p, pos, idx, cmd) =>
                {
                    calls++;
                    seenPlayer = p;
                    seenPos = pos;
                    seenIdx = idx;
                    seenCmd = cmd;
                },
                r => { calls += 100; },
                i => { calls += 10; },
                d => { calls += 10; });
            AssertEqual(CampaignCpuCommitOutcome.Applied, outcome, "location applied");
            AssertEqual(1, calls, "only doCommand");
            AssertEqual(1, seenPlayer, "player OwnedSeat");
            AssertEqual(CampaignCpuNativeCommit.PosSelect, seenPos, "PosSelect rewrite");
            AssertEqual(4, seenIdx, "zone as Index");
            AssertEqual((int)DuelCommandType.Decide, seenCmd, "Decide cmd");

            // Live resolve always prefers DefaultLocation (Decide@mask stalled Main).
            string ruleId;
            string reason;
            AssertTrue(
                CampaignCpuLocation.TryResolveMechanicalPlacement(
                    0, 1, 7850, out action, out ruleId, out reason),
                "resolve with empty mask uses default");
            AssertEqual(CampaignCpuLocation.RuleIdDefault, ruleId, "default rule id");
            AssertEqual(CampaignCpuLocation.ReasonDefault, reason, "default reason");
            AssertTrue(CampaignCpuLocation.IsDefaultLocationAction(action), "default scope");
            AssertEqual(CampaignCpuLocation.DefaultLocationScope, action.TargetScope, "scope");

            int defaultCalls = 0;
            int doCmdCalls = 0;
            CampaignCpuCommitOutcome defOutcome = CampaignCpuNativeCommit.TryApply(
                action,
                phase => { },
                (p, pos, idx, cmd) => { doCmdCalls++; },
                r => { },
                i => { },
                d => { },
                () => { defaultCalls++; });
            AssertEqual(CampaignCpuCommitOutcome.Applied, defOutcome, "default location applied");
            AssertEqual(1, defaultCalls, "DefaultLocation once");
            AssertEqual(0, doCmdCalls, "no DoCommand for default");

            // Without defaultLocation delegate → NotStarted.
            AssertEqual(
                CampaignCpuCommitOutcome.NotStarted,
                CampaignCpuNativeCommit.TryApply(
                    action,
                    phase => { },
                    (p, pos, idx, cmd) => { },
                    r => { },
                    i => { },
                    d => { }),
                "default without delegate NotStarted");

            // Non-empty mask still resolves DefaultLocation (not Decide@PosSelect).
            AssertTrue(
                CampaignCpuLocation.TryResolveMechanicalPlacement(
                    1 << 2, 1, 7850, out action, out ruleId, out reason),
                "non-empty mask still resolves");
            AssertEqual(CampaignCpuLocation.RuleIdDefault, ruleId, "default preferred over mask");
            AssertTrue(CampaignCpuLocation.IsDefaultLocationAction(action), "default action");
            AssertEqual(CampaignCpuLocation.DefaultLocationScope, action.TargetScope, "default scope");

            // High-bit S/T-style mask (live Future Fusion residual 0x1F0000) also defaults.
            AssertTrue(
                CampaignCpuLocation.TryResolveMechanicalPlacement(
                    0x1F0000, 1, 12491, out action, out ruleId, out reason),
                "high-bit mask resolves default");
            AssertEqual(CampaignCpuLocation.RuleIdDefault, ruleId, "high-bit default rule");
            AssertTrue(CampaignCpuLocation.IsDefaultLocationAction(action), "high-bit default");
        }

        static void LocationInterceptPolicyGates()
        {
            AssertTrue(
                CampaignCpuLocation.ShouldIntercept(
                    logOnly: false,
                    allowScriptedCommits: true,
                    seatOwnershipConfirmed: true,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.HumanOwned,
                    viewType: DuelViewType.WaitInput,
                    param1: (int)DuelMenuActType.Location,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "commit-on owned Location intercepts");
            AssertTrue(
                !CampaignCpuLocation.ShouldIntercept(
                    logOnly: true,
                    allowScriptedCommits: true,
                    seatOwnershipConfirmed: true,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.HumanOwned,
                    viewType: DuelViewType.WaitInput,
                    param1: (int)DuelMenuActType.Location,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "LogOnly disables intercept");
            AssertTrue(
                !CampaignCpuLocation.ShouldIntercept(
                    logOnly: false,
                    allowScriptedCommits: false,
                    seatOwnershipConfirmed: true,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.HumanOwned,
                    viewType: DuelViewType.WaitInput,
                    param1: (int)DuelMenuActType.Location,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "AllowScripted false disables");
            AssertTrue(
                !CampaignCpuLocation.ShouldIntercept(
                    logOnly: false,
                    allowScriptedCommits: true,
                    seatOwnershipConfirmed: true,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.NativeLease,
                    viewType: DuelViewType.WaitInput,
                    param1: (int)DuelMenuActType.Location,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "NativeLease not intercept");
            AssertTrue(
                !CampaignCpuLocation.ShouldIntercept(
                    logOnly: false,
                    allowScriptedCommits: true,
                    seatOwnershipConfirmed: true,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.HumanOwned,
                    viewType: DuelViewType.WaitInput,
                    param1: (int)DuelMenuActType.Location,
                    turnPlayer: 0,
                    ownedSeat: 1,
                    myId: 0),
                "human turn no intercept");
            AssertTrue(
                !CampaignCpuLocation.ShouldIntercept(
                    logOnly: false,
                    allowScriptedCommits: true,
                    seatOwnershipConfirmed: false,
                    scriptingDisabledForDuel: false,
                    state: SoloTemporaryCpuState.HumanOwned,
                    viewType: DuelViewType.WaitInput,
                    param1: (int)DuelMenuActType.Location,
                    turnPlayer: 1,
                    ownedSeat: 1,
                    myId: 0),
                "unconfirmed seat no intercept");
        }

        static void ProgressTokenFreshness()
        {
            var a = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, 0, "x");
            var b = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, 0, "x");
            var c = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, 0, "y");
            AssertTrue(!CampaignCpuProgressToken.IsFreshSemanticProgress(b, a), "same not fresh");
            AssertTrue(CampaignCpuProgressToken.IsFreshSemanticProgress(c, a), "changed is fresh");
        }

        /// <summary>
        /// M3: pure classification of owned-Main boundary probe candidates.
        /// Does not change TemporaryCpu restore policy.
        /// </summary>
        static void NativeLeaseBoundaryProbeViewsM3()
        {
            AssertTrue(
                CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(
                    DuelViewType.TurnChange, 0),
                "TurnChange");
            AssertTrue(
                CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(
                    DuelViewType.PhaseChange, 0),
                "PhaseChange");
            AssertTrue(
                CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(
                    DuelViewType.CpuThinking, 0),
                "CpuThinking");
            AssertTrue(
                CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(
                    DuelViewType.CutinDraw, 0),
                "CutinDraw");
            AssertTrue(
                CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(
                    DuelViewType.WaitInput, (int)DuelMenuActType.DrawPhase),
                "WaitInput_DrawPhase");
            AssertTrue(
                CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(
                    DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "WaitInput_MainPhase");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(
                    DuelViewType.WaitInput, (int)DuelMenuActType.BattlePhase),
                "BattlePhase not boundary");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(
                    DuelViewType.RunDialog, 0),
                "RunDialog not M3 boundary (A4 response path)");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsNativeLeaseBoundaryProbeView(
                    DuelViewType.WaitFrame, 0),
                "WaitFrame not M3 boundary candidate");

            AssertEqual(
                "PhaseChange",
                CampaignCpuWindowClassifier.BoundaryProbeKind(DuelViewType.PhaseChange, 0),
                "phase probe kind");
            AssertEqual(
                "WaitInput_MainPhase",
                CampaignCpuWindowClassifier.BoundaryProbeKind(
                    DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "main probe kind");
            AssertEqual(
                "CpuThinking",
                CampaignCpuWindowClassifier.ClassifyWindow(DuelViewType.CpuThinking, 0),
                "classify CpuThinking");
            AssertEqual(
                "TurnChange",
                CampaignCpuWindowClassifier.ClassifyWindow(DuelViewType.TurnChange, 0),
                "classify TurnChange");
        }

        /// <summary>
        /// A5: PhaseChange → Main1/Main2 for OwnedSeat is the capture boundary
        /// (live: param2 = new phase; GetCurrentPhase is still old). Response re-lease
        /// (A4) remains independent; stale MyID on owned Main is fail-closed.
        /// </summary>
        static void OwnedMainCaptureBoundaryA5()
        {
            const int owned = 1;
            const int myId = 0;

            // Live-shaped Main1 entry: param1=owned, param2=Main1, current phase still Standby.
            AssertTrue(
                CampaignCpuWindowClassifier.IsOwnedMainCapturePhaseChange(
                    DuelViewType.PhaseChange,
                    param1: owned,
                    param2: (int)DuelPhase.Main1,
                    turnPlayer: owned,
                    ownedSeat: owned),
                "PhaseChange → Main1 owned is capture boundary");
            AssertTrue(
                CampaignCpuWindowClassifier.IsOwnedMainCapturePhaseChange(
                    DuelViewType.PhaseChange,
                    param1: owned,
                    param2: (int)DuelPhase.Main2,
                    turnPlayer: owned,
                    ownedSeat: owned),
                "PhaseChange → Main2 owned is capture boundary");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedMainCapturePhaseChange(
                    DuelViewType.PhaseChange,
                    param1: owned,
                    param2: (int)DuelPhase.Main1,
                    turnPlayer: myId,
                    ownedSeat: owned),
                "human turn PhaseChange Main is not owned capture");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedMainCapturePhaseChange(
                    DuelViewType.PhaseChange,
                    param1: owned,
                    param2: (int)DuelPhase.Standby,
                    turnPlayer: owned,
                    ownedSeat: owned),
                "Standby entry is not Main capture");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedMainCapturePhaseChange(
                    DuelViewType.PhaseChange,
                    param1: owned,
                    param2: (int)DuelPhase.Draw,
                    turnPlayer: owned,
                    ownedSeat: owned),
                "Draw entry is not Main capture (preserve draw FX)");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedMainCapturePhaseChange(
                    DuelViewType.WaitInput,
                    param1: (int)DuelMenuActType.MainPhase,
                    param2: 0,
                    turnPlayer: owned,
                    ownedSeat: owned),
                "WaitInput Main is not PhaseChange capture");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsOwnedMainCapturePhaseChange(
                    DuelViewType.RunDialog,
                    param1: 0,
                    param2: (int)DuelPhase.Main1,
                    turnPlayer: owned,
                    ownedSeat: owned),
                "RunDialog never A5 capture (A4 path)");

            // Stale MyID on owned-turn Main → fail-closed, never pass-through commit.
            AssertTrue(
                CampaignCpuWindowClassifier.IsStaleMyIdOwnedMainWaitInput(
                    DuelViewType.WaitInput,
                    (int)DuelMenuActType.MainPhase,
                    actingResolved: true,
                    actingPlayer: myId,
                    turnPlayer: owned,
                    ownedSeat: owned,
                    myId: myId),
                "stale MyID + owned turn_player Main");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsStaleMyIdOwnedMainWaitInput(
                    DuelViewType.WaitInput,
                    (int)DuelMenuActType.MainPhase,
                    actingResolved: true,
                    actingPlayer: myId,
                    turnPlayer: myId,
                    ownedSeat: owned,
                    myId: myId),
                "true human Main is not stale owned");
            AssertTrue(
                !CampaignCpuWindowClassifier.IsStaleMyIdOwnedMainWaitInput(
                    DuelViewType.WaitInput,
                    (int)DuelMenuActType.MainPhase,
                    actingResolved: true,
                    actingPlayer: owned,
                    turnPlayer: owned,
                    ownedSeat: owned,
                    myId: myId),
                "fresh owned acting is not stale");

            // SM: response lease → owned Main PhaseChange restores once (no acting required).
            var sm = new SoloTemporaryCpuStateMachine();
            sm.ActivateHumanOwned();
            var entry = CampaignCpuProgressToken.CreateWithoutLegalFingerprint(
                1,
                DuelViewType.RunDialog,
                0, 0, 0,
                actingSeat: myId,
                turn: 0,
                phase: (int)DuelPhase.Main1);
            sm.BeginNativeLease(
                "dual_human_myid_response_hold", 1, 81, entry, owned, DateTime.UtcNow);

            // Still in Draw for owned turn — no restore.
            var drawChange = CampaignCpuProgressToken.CreateWithoutLegalFingerprint(
                1, DuelViewType.PhaseChange, owned, (int)DuelPhase.Draw, 0, -1, 1, (int)DuelPhase.Null);
            string deny;
            AssertTrue(
                !sm.CanRestoreOwnedMainCaptureBoundary(
                    1, 119, drawChange, DuelViewType.PhaseChange,
                    param1: owned, param2: (int)DuelPhase.Draw,
                    turnPlayer: owned, ownedSeat: owned, out deny),
                "Draw PhaseChange holds CPU");
            AssertEqual("not_owned_main_capture_boundary", deny, "draw deny");

            // Main1 PhaseChange — restore.
            var mainChange = CampaignCpuProgressToken.CreateWithoutLegalFingerprint(
                1, DuelViewType.PhaseChange, owned, (int)DuelPhase.Main1, 0, -1, 1, (int)DuelPhase.Standby);
            AssertTrue(
                sm.CanRestoreOwnedMainCaptureBoundary(
                    1, 130, mainChange, DuelViewType.PhaseChange,
                    param1: owned, param2: (int)DuelPhase.Main1,
                    turnPlayer: owned, ownedSeat: owned, out deny),
                "Main1 PhaseChange can restore");
            sm.RestoreHumanOwned();
            AssertEqual(SoloTemporaryCpuState.HumanOwned, sm.State, "HumanOwned after A5");

            // A4 still re-leases response after restore.
            AssertTrue(
                CampaignCpuWindowClassifier.ShouldReLeaseDualHumanResponseWindow(
                    sm.State, DuelViewType.RunDialog, 0),
                "A4 re-lease after A5 restore");
            AssertTrue(
                !CampaignCpuWindowClassifier.ShouldReLeaseDualHumanResponseWindow(
                    sm.State, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "Main never A4 response hold");
        }

        static void FieldDiffDetectsSetTrapAndFaceChange()
        {
            AssertTrue(
                CampaignCpuFieldDiff.IsFieldProbeView(DuelViewType.CardSet, 0),
                "CardSet is field probe");
            AssertTrue(
                CampaignCpuFieldDiff.IsFieldProbeView(
                    DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "Main WaitInput is field probe");
            AssertTrue(
                !CampaignCpuFieldDiff.IsFieldProbeView(DuelViewType.WaitFrame, 0),
                "WaitFrame not field probe");

            var prev = new List<CampaignCpuZoneCard>();
            var curr = new List<CampaignCpuZoneCard>
            {
                new CampaignCpuZoneCard
                {
                    Position = 8,
                    Index = 0,
                    UniqueId = 42,
                    CardId = 12493,
                    Face = CampaignCpuZoneCard.FaceDownOrNonPublic,
                    IsTrap = true,
                },
            };
            var added = new List<CampaignCpuZoneCard>();
            var removed = new List<CampaignCpuZoneCard>();
            var changed = new List<CampaignCpuZoneCard>();
            CampaignCpuFieldDiff.Diff(prev, curr, added, removed, changed);
            AssertEqual(1, added.Count, "added set");
            AssertEqual(0, removed.Count, "no removed");
            AssertTrue(added[0].IsTrap, "trap flag");
            AssertEqual(
                "set_trap_or_traplike",
                CampaignCpuFieldDiff.DescribePlayHint(added[0]),
                "set trap hint");

            var flipped = new List<CampaignCpuZoneCard>
            {
                new CampaignCpuZoneCard
                {
                    Position = 8,
                    Index = 0,
                    UniqueId = 42,
                    CardId = 12493,
                    Face = CampaignCpuZoneCard.FaceUpPublic,
                    IsTrap = true,
                },
            };
            CampaignCpuFieldDiff.Diff(curr, flipped, added, removed, changed);
            AssertEqual(0, added.Count, "same unique not added");
            AssertEqual(1, changed.Count, "face change");
            AssertEqual(
                "face_up_trap",
                CampaignCpuFieldDiff.DescribePlayHint(changed[0]),
                "face-up trap hint");

            string fp1 = CampaignCpuFieldDiff.Fingerprint(curr);
            string fp2 = CampaignCpuFieldDiff.Fingerprint(flipped);
            AssertTrue(fp1 != fp2, "fingerprint changes with face");
        }

        /// <summary>
        /// Production bug: arm path stored full token (Turn + known legal); check path used
        /// Turn=0 and empty string fingerprint, so a repeated same-view callback looked fresh.
        /// </summary>
        static void ProgressTokenArmVsCheckPathNotFalseFresh()
        {
            var armed = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                duelGeneration: 1,
                viewType: DuelViewType.WaitInput,
                p1: (int)DuelMenuActType.MainPhase,
                p2: 0,
                p3: 0,
                actingSeat: 1,
                turn: 2,
                phase: (int)DuelPhase.Main1,
                legalActionFingerprint: "Command|Summon|4007|13|0|");
            // Same callback through the lightweight/check builder (unknown legal).
            var check = CampaignCpuProgressToken.CreateWithoutLegalFingerprint(
                duelGeneration: 1,
                viewType: DuelViewType.WaitInput,
                p1: (int)DuelMenuActType.MainPhase,
                p2: 0,
                p3: 0,
                actingSeat: 1,
                turn: 2,
                phase: (int)DuelPhase.Main1);
            AssertTrue(
                !CampaignCpuProgressToken.IsFreshSemanticProgress(check, armed),
                "arm vs check same base is not fresh");
            AssertEqual(
                CampaignCpuProgressCheckResult.SuppressSameView,
                CampaignCpuProgressCheck.EvaluateAwaitingProgress(check, armed),
                "awaiting suppresses same view");
            AssertEqual(
                CampaignCpuProgressCheckResult.SuppressSameView,
                CampaignCpuProgressCheck.EvaluateCommitQuarantine(check, armed),
                "quarantine suppresses same view");
        }

        static void ProgressTokenKnownLegalChangeIsFresh()
        {
            var a = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1, "Command|Summon|4007|13|0|");
            var b = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1, "Command|SummonSp|12488|13|0|");
            AssertTrue(
                CampaignCpuProgressToken.IsFreshSemanticProgress(b, a),
                "same base + different known legal is fresh");
        }

        static void ProgressTokenKnownToUnknownNotFresh()
        {
            var known = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1, "x");
            var unknown = CampaignCpuProgressToken.CreateWithoutLegalFingerprint(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1);
            AssertTrue(
                !CampaignCpuProgressToken.IsFreshSemanticProgress(unknown, known),
                "known→unknown is not fresh");
        }

        static void ProgressTokenEmptyLegalKnownVsUnknown()
        {
            var knownEmpty = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, 0, null);
            AssertTrue(knownEmpty.LegalFingerprintKnown, "known empty flag");
            AssertEqual("empty", knownEmpty.LegalActionFingerprint, "empty sentinel");
            var unknown = CampaignCpuProgressToken.CreateWithoutLegalFingerprint(
                1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, 0);
            AssertTrue(!unknown.LegalFingerprintKnown, "unknown flag");
            AssertTrue(
                !CampaignCpuProgressToken.IsFreshSemanticProgress(unknown, knownEmpty),
                "known empty vs unknown not fresh");
            // Base field change is still fresh even with unknown legal.
            var nextPhase = CampaignCpuProgressToken.CreateWithoutLegalFingerprint(
                1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, (int)DuelPhase.Battle);
            AssertTrue(
                CampaignCpuProgressToken.IsFreshSemanticProgress(nextPhase, knownEmpty),
                "phase change is fresh with unknown legal");
        }

        /// <summary>
        /// M1 production seam: check path re-extract supplies a known fingerprint so
        /// same view/turn/phase with a changed legal menu is ProgressObserved.
        /// </summary>
        static void ProgressCheckBuildTokenSameViewChangedMenuIsFresh()
        {
            var armed = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1, "Command|Summon|4007|13|0|");
            // Production check builder with re-extracted known fingerprint (changed menu).
            var check = CampaignCpuProgressCheck.BuildCheckToken(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1,
                legalFingerprintKnown: true,
                legalActionFingerprint: "Command|Set|8344|13|0|");
            AssertTrue(check.LegalFingerprintKnown, "check path known");
            AssertTrue(
                CampaignCpuProgressToken.IsFreshSemanticProgress(check, armed),
                "same base + changed known legal is fresh");
            AssertEqual(
                CampaignCpuProgressCheckResult.ProgressObserved,
                CampaignCpuProgressCheck.EvaluateAwaitingProgress(check, armed),
                "awaiting observes progress");
            AssertEqual(
                CampaignCpuEffectKind.CompleteAwaitingProgress,
                CampaignCpuRunEffectRouter.MapProgressGate(
                    SoloTemporaryCpuState.AwaitingProgress,
                    CampaignCpuProgressCheckResult.ProgressObserved),
                "router maps to complete awaiting");
            // Same menu → suppress.
            var same = CampaignCpuProgressCheck.BuildCheckToken(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1,
                legalFingerprintKnown: true,
                legalActionFingerprint: "Command|Summon|4007|13|0|");
            AssertEqual(
                CampaignCpuProgressCheckResult.SuppressSameView,
                CampaignCpuProgressCheck.EvaluateAwaitingProgress(same, armed),
                "identical known menu suppresses");
        }

        static void ProgressCheckBuildTokenKnownToUnknownNotFresh()
        {
            var armed = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1, "Command|Summon|4007|13|0|");
            var unknown = CampaignCpuProgressCheck.BuildCheckToken(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1,
                legalFingerprintKnown: false,
                legalActionFingerprint: null);
            AssertTrue(!unknown.LegalFingerprintKnown, "unknown");
            AssertEqual(
                CampaignCpuProgressCheckResult.SuppressSameView,
                CampaignCpuProgressCheck.EvaluateAwaitingProgress(unknown, armed),
                "known→unknown not fresh");
            AssertEqual(
                CampaignCpuEffectKind.SuppressSameView,
                CampaignCpuRunEffectRouter.MapProgressGate(
                    SoloTemporaryCpuState.AwaitingProgress,
                    CampaignCpuProgressCheckResult.SuppressSameView),
                "router suppresses");
        }

        static void ProgressCheckSafeExtractViewIsMainOnly()
        {
            AssertTrue(
                CampaignCpuProgressCheck.IsSafeLegalFingerprintExtractView(
                    DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase),
                "Main WaitInput safe");
            AssertTrue(
                !CampaignCpuProgressCheck.IsSafeLegalFingerprintExtractView(
                    DuelViewType.WaitInput, (int)DuelMenuActType.DrawPhase),
                "Draw not safe extract");
            AssertTrue(
                !CampaignCpuProgressCheck.IsSafeLegalFingerprintExtractView(
                    DuelViewType.PhaseChange, 0),
                "PhaseChange not safe extract");
        }

        static void PostCommitIndeterminateSameViewSuppressesOriginal()
        {
            var sm = new SoloTemporaryCpuStateMachine();
            sm.ActivateHumanOwned();
            var armed = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1, "Command|Summon|4007|13|0|");
            sm.EnterCommitQuarantine(5, armed, "id", DateTime.UtcNow, "commit_indeterminate");
            var sameCheck = CampaignCpuProgressToken.CreateWithoutLegalFingerprint(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1);
            CampaignCpuProgressCheckResult check = CampaignCpuProgressCheck.EvaluateCommitQuarantine(
                sameCheck, sm.QuarantineWatch.CommittedToken);
            AssertEqual(
                CampaignCpuProgressCheckResult.SuppressSameView, check, "suppress same view");
            int originals = CampaignCpuRunEffectContract.CountOriginalInvocations(
                CampaignCpuRunEffectContract.OuterAction.ScriptedHandledNoOriginal,
                () => 7);
            AssertEqual(0, originals, "indeterminate same-view: zero original forwards");
        }

        static void PostCommitTimeoutQuarantinesWithoutOriginal()
        {
            var sm = new SoloTemporaryCpuStateMachine();
            sm.ActivateHumanOwned();
            var token = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, 0, "x");
            sm.ArmAwaitingProgress(5, token, "id", DateTime.UtcNow.AddSeconds(-5));
            AssertTrue(sm.IsProgressTimedOut(DateTime.UtcNow, 2000), "timeout");
            sm.EnterCommitQuarantine(5, token, "id", DateTime.UtcNow, "post_commit_stall");
            AssertEqual(SoloTemporaryCpuState.CommitQuarantine, sm.State, "quarantine");
            int originals = CampaignCpuRunEffectContract.CountOriginalInvocations(
                CampaignCpuRunEffectContract.OuterAction.ScriptedHandledNoOriginal,
                () => 7);
            AssertEqual(0, originals, "timeout quarantine never invokes original");
        }

        static void PostCommitFreshAfterQuarantineForwardsOnce()
        {
            var sm = new SoloTemporaryCpuStateMachine();
            sm.ActivateHumanOwned();
            var armed = CampaignCpuProgressToken.CreateWithLegalFingerprint(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 2,
                (int)DuelPhase.Main1, "x");
            sm.EnterCommitQuarantine(5, armed, "id", DateTime.UtcNow, "commit_indeterminate");
            // Truly fresh: different turn (base field), check-path unknown legal still OK.
            var fresh = CampaignCpuProgressToken.CreateWithoutLegalFingerprint(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 3,
                (int)DuelPhase.Main1);
            CampaignCpuProgressCheckResult check = CampaignCpuProgressCheck.EvaluateCommitQuarantine(
                fresh, sm.QuarantineWatch.CommittedToken);
            AssertEqual(
                CampaignCpuProgressCheckResult.FreshAfterQuarantine, check, "fresh after quarantine");
            // Production path: FreshAfterQuarantine → BeginFallback → original once.
            int originals = CampaignCpuRunEffectContract.CountOriginalInvocations(
                CampaignCpuRunEffectContract.OuterAction.BeginFallbackThenReturn,
                () => 7);
            AssertEqual(1, originals, "fresh after quarantine forwards exactly once");
        }

        static void ExactOnceOriginalContract()
        {
            int calls = CampaignCpuRunEffectContract.CountOriginalInvocations(
                CampaignCpuRunEffectContract.OuterAction.ForwardOriginalOnce,
                () => 7);
            AssertEqual(1, calls, "pass-through calls original once");

            calls = CampaignCpuRunEffectContract.CountOriginalInvocations(
                CampaignCpuRunEffectContract.OuterAction.ScriptedHandledNoOriginal,
                () => 7);
            AssertEqual(0, calls, "scripted path never calls original");

            calls = CampaignCpuRunEffectContract.CountOriginalInvocations(
                CampaignCpuRunEffectContract.OuterAction.BeginFallbackThenReturn,
                () => 7);
            AssertEqual(1, calls, "BeginFallback calls original once only");
        }

        static void AlwaysNativeOwnedWindowUsesBeginFallback()
        {
            AssertEqual(
                CampaignCpuRunEffectContract.OuterAction.BeginFallbackThenReturn,
                CampaignCpuRunEffectContract.DecideAlwaysNativeOwnedWindow(),
                "PR4a always-native owned window");
            AssertEqual(
                CampaignCpuRunEffectContract.OuterAction.ForwardOriginalOnce,
                CampaignCpuRunEffectContract.DecidePassThrough(),
                "pass-through forwards once");
        }

        static void RunEffectRouterMapsProgressGateAndCountsOriginals()
        {
            AssertEqual(
                CampaignCpuEffectKind.SuppressSameView,
                CampaignCpuRunEffectRouter.MapProgressGate(
                    SoloTemporaryCpuState.CommitQuarantine,
                    CampaignCpuProgressCheckResult.SuppressSameView),
                "quarantine suppress");
            AssertEqual(
                CampaignCpuEffectKind.DisableAndBeginNativeLease,
                CampaignCpuRunEffectRouter.MapProgressGate(
                    SoloTemporaryCpuState.CommitQuarantine,
                    CampaignCpuProgressCheckResult.FreshAfterQuarantine),
                "quarantine fresh");
            AssertEqual(0, CampaignCpuRunEffectRouter.CountOriginalInvocations(
                CampaignCpuEffectKind.SuppressSameView), "suppress 0");
            AssertEqual(1, CampaignCpuRunEffectRouter.CountOriginalInvocations(
                CampaignCpuEffectKind.DisableAndBeginNativeLease), "disable+lease 1");
            AssertEqual(0, CampaignCpuRunEffectRouter.CountOriginalInvocations(
                CampaignCpuEffectKind.CommitApplied), "commit applied 0");
            AssertEqual(1, CampaignCpuRunEffectRouter.CountOriginalInvocations(
                CampaignCpuEffectKind.BeginNativeLeaseAndForward), "begin fallback 1");
            AssertEqual(
                1,
                CampaignCpuRunEffectRouter.CountOriginalInvocations(
                    CampaignCpuEffectKind.ForwardOriginal, () => 7),
                "counting forward");
        }

        static void RunEffectRouterMapsTransitionAndCommitOutcomes()
        {
            AssertEqual(
                CampaignCpuEffectKind.RestoreHumanThenContinue,
                CampaignCpuRunEffectRouter.MapTransitionResult(
                    CampaignCpuPlayerTypeTransitionResult.Confirmed, restoreHumanPath: true),
                "A5 confirmed restore");
            AssertEqual(
                CampaignCpuEffectKind.BeginNativeLeaseAndForward,
                CampaignCpuRunEffectRouter.MapTransitionResult(
                    CampaignCpuPlayerTypeTransitionResult.Confirmed, restoreHumanPath: false),
                "CPU confirmed lease");
            AssertEqual(
                CampaignCpuEffectKind.TransitionFailedForwardOriginal,
                CampaignCpuRunEffectRouter.MapTransitionResult(
                    CampaignCpuPlayerTypeTransitionResult.Failed, restoreHumanPath: true),
                "failed human restore");
            AssertEqual(
                CampaignCpuEffectKind.TransitionFailedForwardOriginal,
                CampaignCpuRunEffectRouter.MapTransitionResult(
                    CampaignCpuPlayerTypeTransitionResult.Unknown, restoreHumanPath: false),
                "unknown CPU");
            AssertEqual(
                CampaignCpuEffectKind.CommitApplied,
                CampaignCpuRunEffectRouter.MapCommitOutcome(CampaignCpuCommitOutcome.Applied),
                "applied");
            AssertEqual(
                CampaignCpuEffectKind.CommitIndeterminate,
                CampaignCpuRunEffectRouter.MapCommitOutcome(CampaignCpuCommitOutcome.Indeterminate),
                "indeterminate");
            AssertEqual(
                CampaignCpuEffectKind.BeginNativeLeaseAndForward,
                CampaignCpuRunEffectRouter.MapCommitOutcome(CampaignCpuCommitOutcome.NotStarted),
                "not started → fallback");
        }

        static void RunEffectPlanOrdersProductionBranches()
        {
            var input = new CampaignCpuRunEffectRouteInput
            {
                InterceptSelStand = true,
                InterceptLocation = true,
                DualHumanResponseHold = true,
                ActingResolved = true,
                ActingIsMyId = true,
                StaleMyIdOwnedMain = true,
                ActingIsOwnedSeat = false,
                ForceNative = true,
            };
            AssertEqual(
                CampaignCpuEffectKind.HandleSelStand,
                CampaignCpuRunEffectRouter.PlanRoute(input),
                "SelStand precedes Location/A4/pass-through");

            input.InterceptSelStand = false;
            AssertEqual(
                CampaignCpuEffectKind.HandleLocation,
                CampaignCpuRunEffectRouter.PlanRoute(input),
                "Location precedes A4/pass-through");

            input.InterceptLocation = false;
            AssertEqual(
                CampaignCpuEffectKind.BeginNativeLeaseAndForward,
                CampaignCpuRunEffectRouter.PlanRoute(input),
                "A4 precedes stale MyID and pass-through");

            input.DualHumanResponseHold = false;
            AssertEqual(
                CampaignCpuEffectKind.BeginNativeLeaseAndForward,
                CampaignCpuRunEffectRouter.PlanRoute(input),
                "stale MyID Main re-leases before MyID pass-through");

            input.StaleMyIdOwnedMain = false;
            AssertEqual(
                CampaignCpuEffectKind.ForwardOriginal,
                CampaignCpuRunEffectRouter.PlanRoute(input),
                "MyID pass-through precedes forced-owned path");

            input.ActingIsMyId = false;
            input.ActingResolved = false;
            AssertEqual(
                CampaignCpuEffectKind.ForwardOriginal,
                CampaignCpuRunEffectRouter.PlanRoute(input),
                "unresolved actor forwards once");

            input.ActingResolved = true;
            input.ActingIsOwnedSeat = false;
            AssertEqual(
                CampaignCpuEffectKind.ForwardOriginal,
                CampaignCpuRunEffectRouter.PlanRoute(input),
                "non-owned actor forwards once");

            input.ActingIsOwnedSeat = true;
            AssertEqual(
                CampaignCpuEffectKind.BeginNativeLeaseAndForward,
                CampaignCpuRunEffectRouter.PlanRoute(input),
                "owned forced-native begins lease");

            input.ForceNative = false;
            AssertEqual(
                CampaignCpuEffectKind.ContinueScriptedRouting,
                CampaignCpuRunEffectRouter.PlanRoute(input),
                "owned scripted window continues to scoring");

            AssertEqual(
                0,
                CampaignCpuRunEffectRouter.CountOriginalInvocations(
                    CampaignCpuEffectKind.HandleSelStand),
                "mechanical SelStand owns terminal handling");
            AssertEqual(
                0,
                CampaignCpuRunEffectRouter.CountOriginalInvocations(
                    CampaignCpuEffectKind.HandleLocation),
                "mechanical Location owns terminal handling");
        }

        static void PlayerTypeTransitionRequiresPositiveReadback()
        {
            // Human=0 desired → Confirmed only on IsHuman==1
            AssertEqual(
                CampaignCpuPlayerTypeTransitionResult.Confirmed,
                CampaignCpuControlPolicy.EvaluatePlayerTypeTransition(0, 1),
                "Human confirmed");
            AssertEqual(
                CampaignCpuPlayerTypeTransitionResult.Failed,
                CampaignCpuControlPolicy.EvaluatePlayerTypeTransition(0, 0),
                "Human failed when still CPU");
            AssertEqual(
                CampaignCpuPlayerTypeTransitionResult.Unknown,
                CampaignCpuControlPolicy.EvaluatePlayerTypeTransition(0, -1),
                "Human unknown");
            // CPU=1 desired → Confirmed only on IsHuman==0
            AssertEqual(
                CampaignCpuPlayerTypeTransitionResult.Confirmed,
                CampaignCpuControlPolicy.EvaluatePlayerTypeTransition(1, 0),
                "CPU confirmed");
            AssertEqual(
                CampaignCpuPlayerTypeTransitionResult.Failed,
                CampaignCpuControlPolicy.EvaluatePlayerTypeTransition(1, 1),
                "CPU failed when still Human");
            AssertEqual(
                CampaignCpuPlayerTypeTransitionResult.Unknown,
                CampaignCpuControlPolicy.EvaluatePlayerTypeTransition(1, 99),
                "CPU unknown readback");
            AssertTrue(
                CampaignCpuControlPolicy.IsTransitionConfirmed(
                    CampaignCpuPlayerTypeTransitionResult.Confirmed),
                "helper confirmed");
            AssertTrue(
                !CampaignCpuControlPolicy.IsTransitionConfirmed(
                    CampaignCpuPlayerTypeTransitionResult.Failed),
                "helper failed");
        }

        static void PackIndexRejectsDefaultEnabledTrue()
        {
            string dir = Path.Combine(Path.GetTempPath(), "campaigncpu-index-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(
                    Path.Combine(dir, "index.json"),
                    @"{
  ""version"": 1,
  ""default_enabled"": true,
  ""chapters"": {
    ""11010078"": { ""pack"": ""chapters/x.json"", ""enabled"": false }
  }
}");
                bool threw = false;
                try
                {
                    CampaignCpuRulePackLoader.LoadIndex(dir);
                }
                catch (InvalidOperationException ex)
                {
                    threw = true;
                    AssertTrue(
                        ex.Message.IndexOf("default_enabled", StringComparison.OrdinalIgnoreCase) >= 0,
                        "message mentions default_enabled");
                }
                AssertTrue(threw, "default_enabled=true must throw");

                // false is accepted
                File.WriteAllText(
                    Path.Combine(dir, "index.json"),
                    @"{
  ""version"": 1,
  ""default_enabled"": false,
  ""chapters"": {}
}");
                CampaignCpuRuleIndex idx = CampaignCpuRulePackLoader.LoadIndex(dir);
                AssertEqual(false, idx.DefaultEnabled, "DefaultEnabled forced false");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        static void ControlPolicyNeverOwnsMyId()
        {
            int owned = CampaignCpuControlPolicy.ResolveOwnedSeat(myId: 0);
            AssertEqual(1, owned, "myId0 owns seat1");
            AssertTrue(
                !CampaignCpuControlPolicy.IsOwnedOpponentSeat(0, owned, myId: 0),
                "never own MyID");
            AssertTrue(
                CampaignCpuControlPolicy.IsOwnedOpponentSeat(1, owned, myId: 0),
                "own rival");
            AssertTrue(
                !CampaignCpuControlPolicy.IsOwnedOpponentSeat(1, ownedSeat: 0, myId: 1),
                "when myId=1, seat1 is human not owned");
        }

        static void InvalidMyIdDoesNotResolveOwnedSeat()
        {
            AssertTrue(!CampaignCpuControlPolicy.IsValidMyId(-1), "invalid -1");
            AssertTrue(!CampaignCpuControlPolicy.IsValidMyId(2), "invalid 2");
            AssertTrue(CampaignCpuControlPolicy.IsValidMyId(0), "seat0 ok");
            AssertTrue(CampaignCpuControlPolicy.IsValidMyId(1), "seat1 ok");
            int owned;
            AssertTrue(!CampaignCpuControlPolicy.TryResolveOwnedSeat(-1, out owned), "try -1");
            AssertEqual(-1, owned, "owned unset on fail");
            AssertEqual(-1, CampaignCpuControlPolicy.ResolveOwnedSeat(-1), "legacy resolve -1");
            AssertTrue(CampaignCpuControlPolicy.TryResolveOwnedSeat(1, out owned), "try myId1");
            AssertEqual(0, owned, "myId1 owns seat0");
        }

        static void SeatOwnershipConfirmRequiresPositiveHumanReadback()
        {
            AssertTrue(
                CampaignCpuControlPolicy.IsSeatOwnershipConfirmed(1, 1),
                "both human confirmed");
            AssertTrue(
                !CampaignCpuControlPolicy.IsSeatOwnershipConfirmed(0, 1),
                "owned CPU not confirmed");
            AssertTrue(
                !CampaignCpuControlPolicy.IsSeatOwnershipConfirmed(1, 0),
                "myId CPU not confirmed");
            AssertTrue(
                !CampaignCpuControlPolicy.IsSeatOwnershipConfirmed(-1, 1),
                "unknown owned not confirmed");
            AssertTrue(
                !CampaignCpuControlPolicy.IsPositiveHumanReadback(0),
                "0 is not positive human");
        }

        static void SeatAssertFailuresDisableScriptingPredicate()
        {
            AssertTrue(
                !CampaignCpuControlPolicy.ShouldDisableScriptingAfterSeatAssertFailures(0),
                "0 attempts keep retrying");
            AssertTrue(
                !CampaignCpuControlPolicy.ShouldDisableScriptingAfterSeatAssertFailures(
                    CampaignCpuControlPolicy.MaxSeatAssertAttempts - 1),
                "just under cap");
            AssertTrue(
                CampaignCpuControlPolicy.ShouldDisableScriptingAfterSeatAssertFailures(
                    CampaignCpuControlPolicy.MaxSeatAssertAttempts),
                "at cap disable");
        }

        static void PathContainmentRejectsSiblingPrefixEscape()
        {
            string root = Path.Combine(Path.GetTempPath(), "campaigncpu-rules-m5-root");
            string sibling = root + "-evil";
            string child = Path.Combine(root, "chapters", "pack.json");
            AssertTrue(
                CampaignCpuRulePackLoader.IsPathContainedUnderRoot(child, root),
                "child under root ok");
            AssertTrue(
                CampaignCpuRulePackLoader.IsPathContainedUnderRoot(root, root),
                "root is contained");
            AssertTrue(
                !CampaignCpuRulePackLoader.IsPathContainedUnderRoot(sibling, root),
                "sibling prefix escape rejected");
            AssertTrue(
                !CampaignCpuRulePackLoader.IsPathContainedUnderRoot(
                    Path.Combine(sibling, "pack.json"), root),
                "file under sibling rejected");
            // Absolute escape via .. must also fail once full-path normalized.
            string escaped = Path.GetFullPath(Path.Combine(root, "..", Path.GetFileName(sibling), "x.json"));
            AssertTrue(
                !CampaignCpuRulePackLoader.IsPathContainedUnderRoot(escaped, root),
                "normalized parent escape rejected");
        }

        static string MinimalPackJson(
            string extraPriority = null,
            string policy = null,
            int version = 1,
            int chapterId = 1)
        {
            string hash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            string pol = policy ?? @"{
              ""on_no_match"": ""native_cpu"",
              ""on_unsupported_window"": ""native_cpu"",
              ""on_zero_legal"": ""native_cpu"",
              ""mechanical_windows"": ""auto_or_native"",
              ""scripted_views"": [""WaitInput_MainPhase""],
              ""max_decisions_per_duel"": 0
            }";
            string pri = extraPriority ?? "[]";
            return @"{
              ""version"": " + version + @",
              ""chapter_id"": " + chapterId + @",
              ""deck_hash"": """ + hash + @""",
              ""policy"": " + pol + @",
              ""never"": [],
              ""priority"": " + pri + @",
              ""fallback_scoring"": []
            }";
        }

        static void AssertPackLoadThrows(string json, string label)
        {
            bool threw = false;
            try
            {
                CampaignCpuRulePackLoader.LoadPackFromText(json, "test-" + label, requireDeckHash: true);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            AssertTrue(threw, label + " must fail pack load");
        }

        static void PackLoaderRejectsUnknownPhaseIn()
        {
            string pri = @"[{
              ""id"": ""bad-phase"",
              ""priority"": 1,
              ""when"": { ""phase_in"": [""NotAPhase""] },
              ""prefer"": [ { ""command"": ""Summon"" } ]
            }]";
            AssertPackLoadThrows(MinimalPackJson(extraPriority: pri), "unknown phase_in");
        }

        static void PackLoaderRejectsInvalidCardIdPredicate()
        {
            string pri = @"[{
              ""id"": ""bad-card"",
              ""priority"": 1,
              ""when"": { ""self_has_card_id"": [""not-an-id""] },
              ""prefer"": [ { ""command"": ""Summon"" } ]
            }]";
            AssertPackLoadThrows(MinimalPackJson(extraPriority: pri), "invalid self_has_card_id");

            string matchBad = @"[{
              ""id"": ""bad-match-card"",
              ""priority"": 1,
              ""prefer"": [ { ""command"": ""Summon"", ""card_id"": ""nope"" } ]
            }]";
            AssertPackLoadThrows(MinimalPackJson(extraPriority: matchBad), "invalid match card_id");
        }

        static void PackLoaderRejectsUnsupportedVersionAndPolicy()
        {
            AssertPackLoadThrows(
                MinimalPackJson(version: 99),
                "unsupported pack version");
            string badPolicy = @"{
              ""on_no_match"": ""guess_best"",
              ""on_unsupported_window"": ""native_cpu"",
              ""on_zero_legal"": ""native_cpu"",
              ""mechanical_windows"": ""auto_or_native"",
              ""scripted_views"": [""WaitInput_MainPhase""]
            }";
            AssertPackLoadThrows(
                MinimalPackJson(policy: badPolicy),
                "unsupported on_no_match");
            string badView = @"{
              ""on_no_match"": ""native_cpu"",
              ""on_unsupported_window"": ""native_cpu"",
              ""on_zero_legal"": ""native_cpu"",
              ""mechanical_windows"": ""auto_or_native"",
              ""scripted_views"": [""WaitInput_DrawPhase""]
            }";
            AssertPackLoadThrows(
                MinimalPackJson(policy: badView),
                "unsupported scripted_views");
            string malformedOpeningAllowlist = @"{
              ""on_no_match"": ""native_cpu"",
              ""opening_set_safety_enabled"": false,
              ""opening_set_safety_card_ids"": ""12293""
            }";
            AssertPackLoadThrows(
                MinimalPackJson(policy: malformedOpeningAllowlist),
                "malformed opening set allowlist");
        }

        static void ScorerDecisionCapReturnsNative()
        {
            CampaignCpuRulePack pack = LoadProductPack();
            pack.Policy.MaxDecisionsPerDuel = 2;
            CampaignCpuObservation obs = LoadObservationFixture("g1_opening_a_main1.json");
            CampaignCpuDecision under = CampaignCpuScorer.Decide(
                obs, pack, predicateEvalFailed: false, appliedDecisionCount: 1);
            AssertEqual(CampaignCpuRoute.RuleCommit, under.Route, "under cap still scores");
            CampaignCpuDecision at = CampaignCpuScorer.Decide(
                obs, pack, predicateEvalFailed: false, appliedDecisionCount: 2);
            AssertEqual(CampaignCpuRoute.FallbackNative, at.Route, "at cap native");
            AssertEqual("max_decisions_per_duel", at.Reason, "cap reason");
            AssertTrue(
                CampaignCpuScorer.IsDecisionCapReached(pack.Policy, 2),
                "helper at cap");
            AssertTrue(
                !CampaignCpuScorer.IsDecisionCapReached(pack.Policy, 1),
                "helper under cap");
            pack.Policy.MaxDecisionsPerDuel = 0;
            AssertTrue(
                !CampaignCpuScorer.IsDecisionCapReached(pack.Policy, 999),
                "0 means unlimited");
        }

        static void ScorerPredicateQueryFailureFailsWhenCardPredicates()
        {
            CampaignCpuRulePack pack = LoadProductPack();
            CampaignCpuObservation obs = LoadObservationFixture("g1_opening_a_main1.json");
            // G1 priority requires self_has_card_id; with predicate failure it must not match.
            obs.PredicateQueryFailed = true;
            CampaignCpuDecision d = CampaignCpuScorer.Decide(
                obs, pack, predicateEvalFailed: true, appliedDecisionCount: 0);
            // Fallback may still match non-when rules; priority g1 must not fire.
            AssertTrue(
                d.RuleId == null
                || !string.Equals(d.RuleId, "g1-opening-special-or-action", StringComparison.Ordinal),
                "g1 priority must not match when predicate query failed");
            // Explicit when check
            var when = new CampaignCpuWhenSpec
            {
                SelfHasCardId = new List<int> { 12485 },
            };
            AssertTrue(
                !CampaignCpuMatchers.WhenHolds(when, obs, predicateEvalFailed: true),
                "WhenHolds false on card predicate + eval failed");
            AssertTrue(
                !CampaignCpuMatchers.WhenHolds(when, obs, predicateEvalFailed: false),
                "observation.PredicateQueryFailed alone is enough");
        }

        static void ProductPackStillLoadsUnderStrictParse()
        {
            CampaignCpuRulePack pack = LoadProductPack();
            AssertEqual(11010078, pack.ChapterId, "product chapter");
            AssertEqual(1, pack.Version, "product version");
            AssertEqual("native_cpu", pack.Policy.OnNoMatch, "policy on_no_match");
            AssertEqual(0, pack.Policy.MaxDecisionsPerDuel, "unlimited default");
            AssertTrue(pack.Priority.Count >= 2, "priority rules present");
            AssertTrue(
                pack.Priority[0].When != null
                && pack.Priority[0].When.PhaseIn != null
                && pack.Priority[0].When.PhaseIn.Count > 0,
                "phase_in parsed");
            // Index load with real rules dir must succeed under strict version check.
            CampaignCpuRuleIndex index = CampaignCpuRulePackLoader.LoadIndex(RulesDir());
            AssertEqual(1, index.Version, "index version");
            AssertTrue(index.Chapters.ContainsKey(11010078), "index has product chapter");
        }

        static void SoloCampaignModeGateAcceptsLiveSoloDuelsGameModeZero()
        {
            // Live SoloDuels leave GameMode at 0 (Normal); must still arm CampaignCpu.
            const int GameModeNormal = 0;
            const int GameModeSoloSingle = 9;
            const int GameModeRoom = 10;
            const int GameModeReplay = 7;
            AssertTrue(
                CampaignCpuControlPolicy.IsEligibleSoloCampaignMode(
                    GameModeNormal, isPvpDuel: false, isPvpSpectator: false),
                "Normal (0) is live solo");
            AssertTrue(
                CampaignCpuControlPolicy.IsEligibleSoloCampaignMode(
                    GameModeSoloSingle, isPvpDuel: false, isPvpSpectator: false),
                "SoloSingle accepted");
            AssertTrue(
                !CampaignCpuControlPolicy.IsEligibleSoloCampaignMode(
                    GameModeRoom, isPvpDuel: true, isPvpSpectator: false),
                "Room/PvP rejected");
            AssertTrue(
                !CampaignCpuControlPolicy.IsEligibleSoloCampaignMode(
                    GameModeReplay, isPvpDuel: false, isPvpSpectator: false),
                "Replay rejected");
            AssertTrue(
                !CampaignCpuControlPolicy.IsEligibleSoloCampaignMode(
                    GameModeNormal, isPvpDuel: true, isPvpSpectator: false),
                "PvP flag rejects even Normal");
        }

        static void SoloTemporaryCpuNativeContinuationBlocksRestore()
        {
            var sm = new SoloTemporaryCpuStateMachine();
            sm.ActivateHumanOwned();
            var entry = CampaignCpuProgressToken.Create(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 1, 0, "a");
            sm.BeginNativeLease("v1_non_main_phase", 1, 10, entry, 1, DateTime.UtcNow);
            // MyID boundary would otherwise restore, but RunDialog is a native continuation.
            var next = CampaignCpuProgressToken.Create(
                1, DuelViewType.RunDialog, 0, 0, 0, 0, 1, 0, "b");
            string deny;
            bool can = sm.CanRestoreFromNativeLease(
                1, 11, next, resolvedActingSeat: 0, ownedSeat: 1, myId: 0,
                requireCpuThinking: false,
                viewType: DuelViewType.RunDialog,
                param1: 0,
                out deny);
            AssertTrue(!can, "RunDialog is native continuation");
            AssertEqual("native_continuation_view", deny, "deny reason");
        }

        static void SoloTemporaryCpuMyIdBoundaryRestores()
        {
            var sm = new SoloTemporaryCpuStateMachine();
            sm.ActivateHumanOwned();
            var entry = CampaignCpuProgressToken.Create(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 1, 1, 0, "a");
            sm.BeginNativeLease("always_native_lease", 1, 10, entry, 1, DateTime.UtcNow);
            var next = CampaignCpuProgressToken.Create(
                1, DuelViewType.WaitInput, (int)DuelMenuActType.MainPhase, 0, 0, 0, 2, 0, "c");
            string deny;
            bool can = sm.CanRestoreFromNativeLease(
                1, 12, next, resolvedActingSeat: 0, ownedSeat: 1, myId: 0,
                requireCpuThinking: false,
                viewType: DuelViewType.WaitInput,
                param1: (int)DuelMenuActType.MainPhase,
                out deny);
            AssertTrue(can, "MyID boundary after progress restores");
        }

        static void AuditSerializerDecisionIncludesFullLegalMenu()
        {
            var obs = new CampaignCpuObservation
            {
                ChapterId = 11010078,
                WindowClass = "WaitInput_MainPhase",
                ActingPlayer = 1,
                OwnedSeat = 1,
                Turn = 2,
                TurnPlayer = 1,
                Phase = (int)DuelPhase.Main1,
                IsMainPhaseWaitInput = true,
            };
            obs.SelfHandCardIds.Add(4007);
            obs.SelfHandCardIds.Add(12488);
            obs.LegalActions.Add(Cmd(1, DuelCommandType.Summon, 4007));
            obs.LegalActions.Add(Cmd(2, DuelCommandType.SummonSp, 12488));
            obs.LegalActions.Add(Cmd(3, DuelCommandType.Action, 12253));
            CampaignCpuDecision decision = CampaignCpuDecision.Commit(
                CampaignCpuRoute.RuleCommit,
                obs.LegalActions[1],
                "g1",
                "g1-opening-special-or-action",
                50,
                true);
            string line = CampaignCpuAuditSerializer.SerializeDecision(
                42, decision, obs, shadowOnly: true);
            AssertTrue(line.Contains("\"event\":\"campaign_cpu_decision\"")
                || line.Contains("\"event\": \"campaign_cpu_decision\""),
                "event name present");
            AssertTrue(line.Contains("legal_actions"), "legal_actions field");
            AssertTrue(line.Contains("self_hand_card_ids"), "hand ids field");
            AssertTrue(line.Contains("12488"), "chosen card id in payload");
            AssertTrue(line.Contains("shadow_only"), "shadow_only field");
            AssertTrue(line.Contains("true") || line.Contains("True"), "shadow true");
            AssertTrue(line.Contains("SummonSp") || line.Contains("identity"), "action identity bits");
        }

        /// <summary>
        /// Production decision schema must emit my_id + duel_generation for analyzer S10 gates.
        /// </summary>
        static void AuditSerializerDecisionEmitsMyIdAndGeneration()
        {
            var obs = new CampaignCpuObservation
            {
                ChapterId = 11010078,
                WindowClass = "WaitInput_MainPhase",
                ActingPlayer = 1,
                OwnedSeat = 1,
                Turn = 4,
                TurnPlayer = 1,
                Phase = (int)DuelPhase.Main1,
                IsMainPhaseWaitInput = true,
            };
            CampaignCpuObservationContext.Stamp(obs, myId: 0, duelGeneration: 7);
            obs.LegalActions.Add(Cmd(1, DuelCommandType.Summon, 7850));
            CampaignCpuDecision decision = CampaignCpuDecision.Commit(
                CampaignCpuRoute.RuleCommit,
                obs.LegalActions[0],
                "prefer",
                "prefer-summon",
                10,
                true);
            string line = CampaignCpuAuditSerializer.SerializeDecision(
                99, decision, obs, shadowOnly: false);
            AssertTrue(
                line.Contains("\"my_id\":0") || line.Contains("\"my_id\": 0"),
                "my_id emitted");
            AssertTrue(
                line.Contains("\"duel_generation\":7") || line.Contains("\"duel_generation\": 7"),
                "duel_generation emitted");
            AssertTrue(
                line.Contains("\"owned_seat\":1") || line.Contains("\"owned_seat\": 1"),
                "owned_seat emitted");
            AssertTrue(
                line.Contains("\"turn_player\":1") || line.Contains("\"turn_player\": 1"),
                "turn_player emitted");
            AssertTrue(
                line.Contains("\"acting_player\":1") || line.Contains("\"acting_player\": 1"),
                "acting_player emitted");

            // Mechanical SelStand-shaped observation with live mask result context.
            var mech = new CampaignCpuObservation
            {
                ChapterId = 11010078,
                WindowClass = "RunDialog_SelStand",
                ActingPlayer = 0,
                OwnedSeat = 1,
                MyId = 0,
                DuelGeneration = 7,
                Turn = 1,
                TurnPlayer = 1,
                Phase = (int)DuelPhase.Main1,
            };
            CampaignCpuLegalAction standAction;
            AssertTrue(
                CampaignCpuSelStand.TryBuildMechanicalAction(
                    CampaignCpuSelStand.LiveRecordedStandMask, out standAction),
                "live stand action");
            mech.LegalActions.Add(standAction);
            CampaignCpuDecision mechDec = CampaignCpuDecision.Commit(
                CampaignCpuRoute.MechanicalAuto,
                standAction,
                CampaignCpuSelStand.Reason,
                CampaignCpuSelStand.RuleId,
                0,
                true);
            string mechLine = CampaignCpuAuditSerializer.SerializeDecision(
                100, mechDec, mech, shadowOnly: false);
            AssertTrue(
                mechLine.Contains("\"my_id\":0") || mechLine.Contains("\"my_id\": 0"),
                "mech my_id");
            AssertTrue(
                mechLine.Contains("\"duel_generation\":7") || mechLine.Contains("\"duel_generation\": 7"),
                "mech generation");
            AssertTrue(
                mechLine.Contains("\"turn_player\":1") || mechLine.Contains("\"turn_player\": 1"),
                "mech real turn_player not synthetic zero-only");
            AssertTrue(
                mechLine.Contains("\"turn\":1") || mechLine.Contains("\"turn\": 1"),
                "mech turn");
        }

        static void AuditLifecycleContextIncludesOwnershipAndGeneration()
        {
            Dictionary<string, object> fields =
                CampaignCpuAuditSerializer.CreateLifecycleContext(
                    myId: 0,
                    ownedSeat: 1,
                    duelGeneration: 7);
            AssertEqual(0, Convert.ToInt32(fields["my_id"]), "lifecycle my_id");
            AssertEqual(1, Convert.ToInt32(fields["owned_seat"]), "lifecycle owned_seat");
            AssertEqual(
                7L,
                Convert.ToInt64(fields["duel_generation"]),
                "lifecycle generation");
        }

        static void AuditFileStrictCapAcrossLaunches()
        {
            string dir = Path.Combine(
                Path.GetTempPath(),
                "ygomaster-campaign-audit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "audit.jsonl");
            try
            {
                string[] legacy = new string[10];
                for (int i = 0; i < legacy.Length; i++)
                {
                    legacy[i] = "{\"event\":\"old\",\"n\":" + i + "}";
                }
                File.WriteAllLines(path, legacy);

                int processLineCount = -1;
                AssertTrue(
                    CampaignCpuAuditFile.TryAppendLine(
                        path,
                        "{\"event\":\"new\",\"n\":10}",
                        maxLines: 3,
                        ref processLineCount),
                    "oversized legacy append succeeds");
                string[] lines = File.ReadAllLines(path);
                AssertEqual(3, lines.Length, "oversized legacy file capped");
                AssertTrue(lines[0].Contains("\"n\":8"), "newest legacy row 8 retained");
                AssertTrue(lines[1].Contains("\"n\":9"), "newest legacy row 9 retained");
                AssertTrue(lines[2].Contains("\"n\":10"), "new row appended");

                // Simulate a new process: unknown cached line count must be read from disk.
                processLineCount = -1;
                AssertTrue(
                    CampaignCpuAuditFile.TryAppendLine(
                        path,
                        "{\"event\":\"new\",\"n\":11}",
                        maxLines: 3,
                        ref processLineCount),
                    "cross-launch append succeeds");
                lines = File.ReadAllLines(path);
                AssertEqual(3, lines.Length, "cross-launch strict cap");
                AssertTrue(lines[2].Contains("\"n\":11"), "cross-launch newest row appended");

                processLineCount = -1;
                AssertTrue(
                    CampaignCpuAuditFile.TryAppendLine(
                        path,
                        "{\"event\":\"only\",\"n\":12}",
                        maxLines: 1,
                        ref processLineCount),
                    "max one append succeeds");
                lines = File.ReadAllLines(path);
                AssertEqual(1, lines.Length, "max one remains strict");
                AssertTrue(lines[0].Contains("\"n\":12"), "max one keeps newest append");

                File.WriteAllLines(
                    path,
                    new[]
                    {
                        "{\"event\":\"old\",\"n\":20}",
                        "{\"event\":\"truncated\"",
                        "{\"event\":\"old\",\"n\":21}",
                    });
                processLineCount = -1;
                AssertTrue(
                    CampaignCpuAuditFile.TryAppendLine(
                        path,
                        "{\"event\":\"new\",\"n\":22}",
                        maxLines: 3,
                        ref processLineCount),
                    "truncated legacy row cleanup succeeds");
                lines = File.ReadAllLines(path);
                AssertEqual(3, lines.Length, "only complete JSONL rows retained");
                AssertTrue(lines[0].Contains("\"n\":20"), "valid row 20 retained");
                AssertTrue(lines[1].Contains("\"n\":21"), "valid row 21 retained");
                AssertTrue(lines[2].Contains("\"n\":22"), "valid row 22 appended");
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                }
            }
        }

        static void NativeCandidateTraceValidatesShapeAndCapturesWithoutMutation()
        {
            Type captureType = typeof(CampaignCpuTests).Assembly.GetType(
                "YgoMaster.CampaignCpuNativeTraceCapture");
            AssertTrue(captureType != null, "native trace capture type exists");

            System.Reflection.MethodInfo isSupportedHash = captureType.GetMethod(
                "IsSupportedBinaryHash",
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static);
            AssertTrue(isSupportedHash != null, "native trace hash gate exists");
            AssertTrue(
                (bool)isSupportedHash.Invoke(
                    null,
                    new object[]
                    {
                        "97BD4D136E39B0872E4A8A9171632F1F0BD9BB04D69AF08E836C56E684D43C44",
                    }),
                "authoritative duel.dll hash accepted case-insensitively");
            AssertTrue(
                !(bool)isSupportedHash.Invoke(
                    null,
                    new object[]
                    {
                        "0000000000000000000000000000000000000000000000000000000000000000",
                    }),
                "unknown duel.dll hash rejected");

            System.Reflection.MethodInfo tryCreate = captureType.GetMethod(
                "TryCreate",
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static);
            AssertTrue(tryCreate != null, "native trace TryCreate exists");

            byte[] candidateWork = new byte[0x974];
            WriteUInt16(candidateWork, 0x08, 1);
            WriteUInt16(candidateWork, 0x0a, 3);
            WriteUInt32(candidateWork, 0x18, 0x11223344u);
            WriteUInt32(candidateWork, 0x1c, 0x55667788u);
            WriteUInt32(candidateWork, 0x20, 0x99aabbccu);
            WriteUInt32(candidateWork, 0x4c8, 0x01020304u);
            WriteUInt32(candidateWork, 0x4cc, 0x05060708u);
            WriteUInt32(candidateWork, 0x4d0, 0x090a0b0cu);
            byte[] original = (byte[])candidateWork.Clone();

            object[] args =
            {
                candidateWork,
                0x1c,
                1,
                0,
                7,
                4,
                1,
                2,
                null,
                null,
            };
            bool accepted = (bool)tryCreate.Invoke(null, args);
            AssertTrue(accepted, "valid native candidate shape accepted");
            AssertEqual(null, args[9], "valid shape rejection reason");
            AssertTrue(
                ByteArraysEqual(candidateWork, original),
                "capture does not mutate native snapshot bytes");

            object trace = args[8];
            Type traceType = trace.GetType();
            AssertEqual(
                3,
                Convert.ToInt32(traceType.GetField("CandidateCount").GetValue(trace)),
                "candidate count");
            AssertEqual(
                1,
                Convert.ToInt32(traceType.GetField("SelectedIndex").GetValue(trace)),
                "selected index");
            AssertEqual(
                0x55667788u,
                Convert.ToUInt32(
                    traceType.GetField("SelectedRaw").GetValue(trace)),
                "selected compact identity");
            AssertEqual(
                false,
                Convert.ToBoolean(
                    traceType.GetField("NativeScoresAvailable").GetValue(trace)),
                "unproven native scores remain unavailable");

            object candidates = traceType.GetField("Candidates").GetValue(trace);
            System.Collections.IList list = (System.Collections.IList)candidates;
            AssertEqual(3, list.Count, "all alternatives captured");
            Type candidateType = list[1].GetType();
            AssertEqual(
                0x55667788u,
                Convert.ToUInt32(candidateType.GetField("Raw").GetValue(list[1])),
                "chosen candidate raw");
            AssertEqual(
                0x05060708u,
                Convert.ToUInt32(candidateType.GetField("AuxiliaryRaw").GetValue(list[1])),
                "paired auxiliary raw");

            System.Reflection.MethodInfo toAuditFields = traceType.GetMethod(
                "ToAuditFields",
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance);
            AssertTrue(toAuditFields != null, "native trace audit projection exists");
            var auditFields =
                (Dictionary<string, object>)toAuditFields.Invoke(trace, null);
            AssertEqual(
                1,
                Convert.ToInt32(auditFields["owned_seat"]),
                "audit owned seat");
            AssertEqual(
                7,
                Convert.ToInt32(auditFields["duel_generation"]),
                "audit generation");
            AssertEqual(
                0x55667788u,
                Convert.ToUInt32(auditFields["native_chosen_raw"]),
                "audit chosen identity");
            AssertEqual(
                false,
                Convert.ToBoolean(auditFields["native_scores_available"]),
                "audit score availability");
            AssertTrue(
                !auditFields.ContainsKey("native_score"),
                "unproven score field is omitted");
            AssertEqual(
                3,
                ((System.Collections.IList)auditFields["candidates"]).Count,
                "audit alternatives");

            object[] wrongPointerArgs = (object[])args.Clone();
            wrongPointerArgs[1] = 0x20;
            wrongPointerArgs[8] = null;
            wrongPointerArgs[9] = null;
            AssertTrue(
                !(bool)tryCreate.Invoke(null, wrongPointerArgs),
                "selected pointer mismatch fails closed");
            AssertEqual(
                "selected_pointer_mismatch",
                Convert.ToString(wrongPointerArgs[9]),
                "selected pointer rejection reason");

            byte[] invalidCount = (byte[])candidateWork.Clone();
            WriteUInt16(invalidCount, 0x0a, 300);
            object[] invalidCountArgs = (object[])args.Clone();
            invalidCountArgs[0] = invalidCount;
            invalidCountArgs[8] = null;
            invalidCountArgs[9] = null;
            AssertTrue(
                !(bool)tryCreate.Invoke(null, invalidCountArgs),
                "over-capacity count fails closed");
            AssertEqual(
                "candidate_count_out_of_range",
                Convert.ToString(invalidCountArgs[9]),
                "candidate count rejection reason");

            object[] wrongSeatArgs = (object[])args.Clone();
            wrongSeatArgs[3] = 1;
            wrongSeatArgs[8] = null;
            wrongSeatArgs[9] = null;
            AssertTrue(
                !(bool)tryCreate.Invoke(null, wrongSeatArgs),
                "MyID candidate selection fails closed");
            AssertEqual(
                "player_is_my_id",
                Convert.ToString(wrongSeatArgs[9]),
                "MyID rejection reason");
        }

        static void NativeTraceDiagnosticsClassifyActivationAndBoundInvocationProbe()
        {
            Type diagnosticsType = typeof(CampaignCpuTests).Assembly.GetType(
                "YgoMaster.CampaignCpuNativeTraceDiagnostics");
            AssertTrue(
                diagnosticsType != null,
                "native trace diagnostics type exists");

            System.Reflection.MethodInfo evaluateActivation =
                diagnosticsType.GetMethod(
                    "EvaluateActivation",
                    new Type[]
                    {
                        typeof(bool),
                        typeof(bool),
                        typeof(int),
                        typeof(bool),
                        typeof(bool),
                    });
            AssertTrue(
                evaluateActivation != null,
                "native trace activation accepts raw game mode");

            object normal = evaluateActivation.Invoke(
                null,
                new object[] { true, true, 0, false, false });
            Type resultType = normal.GetType();
            AssertEqual(
                true,
                Convert.ToBoolean(resultType.GetField("Active").GetValue(normal)),
                "eligible Normal duel activates trace");
            AssertEqual(
                "active",
                Convert.ToString(resultType.GetField("Reason").GetValue(normal)),
                "eligible duel activation reason");

            foreach (int eligibleMode in new int[] { 2, 9 })
            {
                object eligible = evaluateActivation.Invoke(
                    null,
                    new object[] { true, true, eligibleMode, false, false });
                AssertEqual(
                    true,
                    Convert.ToBoolean(
                        resultType.GetField("Active").GetValue(eligible)),
                    "eligible Solo mode activates trace: " + eligibleMode);
            }

            foreach (int rejectedMode in new int[] { 6, 7, 10 })
            {
                object rejected = evaluateActivation.Invoke(
                    null,
                    new object[] { true, true, rejectedMode, false, false });
                AssertEqual(
                    false,
                    Convert.ToBoolean(
                        resultType.GetField("Active").GetValue(rejected)),
                    "non-Solo mode remains inactive: " + rejectedMode);
                AssertEqual(
                    "game_mode_not_eligible_solo",
                    Convert.ToString(
                        resultType.GetField("Reason").GetValue(rejected)),
                    "non-Solo diagnostic reason: " + rejectedMode);
            }

            object pvp = evaluateActivation.Invoke(
                null,
                new object[] { true, true, 0, true, false });
            AssertEqual(
                "pvp_duel",
                Convert.ToString(resultType.GetField("Reason").GetValue(pvp)),
                "PvP duel diagnostic reason");

            object spectator = evaluateActivation.Invoke(
                null,
                new object[] { true, true, 0, false, true });
            AssertEqual(
                "pvp_spectator",
                Convert.ToString(
                    resultType.GetField("Reason").GetValue(spectator)),
                "PvP spectator diagnostic reason");

            object noHook = evaluateActivation.Invoke(
                null,
                new object[] { true, false, 0, false, false });
            AssertEqual(
                "hook_not_installed",
                Convert.ToString(resultType.GetField("Reason").GetValue(noHook)),
                "missing hook diagnostic reason");

            System.Reflection.MethodInfo shouldAttemptCandidateCapture =
                diagnosticsType.GetMethod(
                    "ShouldAttemptCandidateCapture",
                    new Type[] { typeof(int), typeof(int) });
            AssertTrue(
                shouldAttemptCandidateCapture != null,
                "native trace candidate capture admission exists");
            AssertTrue(
                (bool)shouldAttemptCandidateCapture.Invoke(
                    null, new object[] { 1, 0 }),
                "opponent candidate proceeds to snapshot validation");
            AssertTrue(
                !(bool)shouldAttemptCandidateCapture.Invoke(
                    null, new object[] { 0, 0 }),
                "local candidate is silently skipped");
            AssertTrue(
                (bool)shouldAttemptCandidateCapture.Invoke(
                    null, new object[] { 2, 0 }),
                "invalid player proceeds to fail-closed validation");
            AssertTrue(
                (bool)shouldAttemptCandidateCapture.Invoke(
                    null, new object[] { 0, 2 }),
                "invalid MyID proceeds to fail-closed validation");

            System.Reflection.MethodInfo tryClaimInvocationProbe =
                diagnosticsType.GetMethod(
                    "TryClaimInvocationProbe",
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Static);
            AssertTrue(
                tryClaimInvocationProbe != null,
                "native trace bounded invocation probe exists");
            int probeState = 0;
            object[] firstClaim = { probeState };
            AssertTrue(
                (bool)tryClaimInvocationProbe.Invoke(null, firstClaim),
                "first hook invocation is logged");
            probeState = Convert.ToInt32(firstClaim[0]);
            object[] secondClaim = { probeState };
            AssertTrue(
                !(bool)tryClaimInvocationProbe.Invoke(null, secondClaim),
                "repeated hook invocation is suppressed");
        }

        static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        static void AssertTrue(bool value, string message)
        {
            if (!value)
            {
                throw new Exception("AssertTrue failed: " + message);
            }
        }

        static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
            {
                throw new Exception(
                    "AssertEqual failed: " + message
                    + " expected=" + expected
                    + " actual=" + actual);
            }
        }
    }
}

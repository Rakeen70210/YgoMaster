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
            PostCommitIndeterminateSameViewSuppressesOriginal();
            PostCommitTimeoutQuarantinesWithoutOriginal();
            PostCommitFreshAfterQuarantineForwardsOnce();
            ExactOnceOriginalContract();
            AlwaysNativeOwnedWindowUsesBeginFallback();
            ControlPolicyNeverOwnsMyId();
            SoloCampaignModeGateAcceptsLiveSoloDuelsGameModeZero();
            SoloTemporaryCpuNativeContinuationBlocksRestore();
            SoloTemporaryCpuMyIdBoundaryRestores();
            OneShotRestoreOnlyDrawPhaseUnderAllowScripted();
            OwnedResponseNativeWindowClassifier();
            DualHumanMyIdResponseHoldA4();
            AuditSerializerDecisionIncludesFullLegalMenu();
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
            // G2: captured opening with top SummonSp 12485 removed → Summon 12292 via g1 prefer[1]
            CampaignCpuRulePack pack = LoadProductPack();
            CampaignCpuObservation obs = LoadObservationFixture("g2_opening_a_without_top_summon_sp.json");
            Dictionary<string, object> exp = LoadExpect("g2_opening_a_without_top_summon_sp.json");
            CampaignCpuDecision d = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(CampaignCpuRoute.RuleCommit, d.Route, "G2 route");
            AssertEqual(Utils.GetValue<string>(exp, "rule_id"), d.RuleId, "G2 rule id");
            AssertTrue(d.Action != null, "G2 action");
            AssertEqual(Utils.GetValue<string>(exp, "action_identity"), d.Action.CanonicalIdentity, "G2 identity");
            AssertEqual(Utils.GetValue<int>(exp, "card_id"), d.Action.CardId, "G2 card");
            AssertEqual(DuelCommandType.Summon, d.Action.Command, "G2 command");
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

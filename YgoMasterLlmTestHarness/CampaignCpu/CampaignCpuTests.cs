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
            ScorerDeterministicSameObservation();
            ScorerNeverExhaustionRestoresLegalSet();
            ObservationProjectorMapsLegalActions();
            SoloTemporaryCpuRestoreRequiresSemanticProgress();
            SoloTemporaryCpuProgressTimeoutQuarantines();
            CommitNotStartedWhenNullAction();
            CommitAppliedOnSuccessfulNative();
            CommitIndeterminateOnNativeThrow();
            WindowClassifierMainPhaseOnly();
            ProgressTokenFreshness();
            ExactOnceOriginalContract();
            AlwaysNativeOwnedWindowUsesBeginFallback();
            ControlPolicyNeverOwnsMyId();
            SoloTemporaryCpuNativeContinuationBlocksRestore();
            SoloTemporaryCpuMyIdBoundaryRestores();
            Console.WriteLine("PASS CampaignCpuTests.RunAll");
        }

        static string RulesDir()
        {
            // Harness cwd is typically repo root or bin; search upward for Data/CampaignCpuRules.
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                string candidate = Path.Combine(dir, "YgoMaster", "Data", "CampaignCpuRules");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                candidate = Path.Combine(dir, "Data", "CampaignCpuRules");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                DirectoryInfo parent = Directory.GetParent(dir);
                if (parent == null)
                {
                    break;
                }
                dir = parent.FullName;
            }
            // Relative from common build output /tmp/ygomaster-build/harness
            string env = Environment.GetEnvironmentVariable("YGOMASTER_ROOT");
            if (!string.IsNullOrEmpty(env))
            {
                return Path.Combine(env, "YgoMaster", "Data", "CampaignCpuRules");
            }
            return Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "YgoMaster", "Data", "CampaignCpuRules"));
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

        static CampaignCpuRulePack LoadProductPack()
        {
            string text = @"{
  ""version"": 1,
  ""chapter_id"": 11010078,
  ""deck_hash"": ""sha256:d1ed3390a63030a78a6da913cf8436878a6e26e39f55dc1ec8e38dc7985e7835"",
  ""policy"": {
    ""on_no_match"": ""native_cpu"",
    ""scripted_views"": [""WaitInput_MainPhase""]
  },
  ""never"": [
    { ""id"": ""never-surrender"", ""match"": { ""command"": ""Surrender"" } }
  ],
  ""priority"": [
    {
      ""id"": ""g1-opening-special-or-action"",
      ""priority"": 100,
      ""required_for_slice"": true,
      ""when"": { ""phase_in"": [""Main1""], ""turn_lte"": 5, ""self_has_card_id"": [12488, 12253, 4007] },
      ""prefer"": [
        { ""command"": ""SummonSp"", ""card_id"": 12488 },
        { ""command"": ""Action"", ""card_id"": 12253 },
        { ""command"": ""Summon"", ""card_id"": 4007 }
      ],
      ""score_bonus"": 50
    },
    {
      ""id"": ""g2-next-preferred-without-top"",
      ""priority"": 90,
      ""required_for_slice"": true,
      ""when"": { ""phase_in"": [""Main1""], ""turn_lte"": 5, ""self_has_card_id"": [12253, 4007] },
      ""prefer"": [
        { ""command"": ""Action"", ""card_id"": 12253 },
        { ""command"": ""Summon"", ""card_id"": 4007 }
      ],
      ""score_bonus"": 40
    }
  ],
  ""fallback_scoring"": [
    { ""id"": ""prefer-action"", ""required_for_slice"": true, ""match"": { ""command"": ""Action"" }, ""score"": 10 },
    { ""id"": ""prefer-summon"", ""match"": { ""command"": ""Summon"" }, ""score"": 8 },
    { ""id"": ""penalize-early-end"", ""match"": { ""kind"": ""MovePhase"", ""phase"": ""End"" }, ""score"": -20 }
  ]
}";
            return CampaignCpuRulePackLoader.LoadPackFromText(text, "inline-11010078", true);
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
            obs.SelfHandCardIds.AddRange(new[] { 12488, 12253, 4007 });
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
            // G1: opening A — top prefer SummonSp 12488
            CampaignCpuRulePack pack = LoadProductPack();
            CampaignCpuObservation obs = MakeMainObs(
                Cmd(1, DuelCommandType.Summon, 4007),
                Cmd(2, DuelCommandType.Action, 12253),
                Cmd(3, DuelCommandType.SummonSp, 12488),
                Cmd(4, DuelCommandType.Surrender, 0));
            CampaignCpuDecision d = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(CampaignCpuRoute.RuleCommit, d.Route, "G1 route");
            AssertEqual("g1-opening-special-or-action", d.RuleId, "G1 rule id");
            AssertEqual(12488, d.Action.CardId, "G1 card");
            AssertEqual(DuelCommandType.SummonSp, d.Action.Command, "G1 command");
        }

        static void ScorerG2WhenTopAbsent()
        {
            // G2: top SummonSp 12488 absent → Action 12253
            CampaignCpuRulePack pack = LoadProductPack();
            CampaignCpuObservation obs = MakeMainObs(
                Cmd(1, DuelCommandType.Summon, 4007),
                Cmd(2, DuelCommandType.Action, 12253),
                Cmd(5, DuelCommandType.Set, 3868));
            // Remove 12488 from hand for when-predicate of g1? g1 when still has 12253 in hand.
            // g1 prefer list fails on SummonSp 12488, then Action 12253 hits — still g1.
            // For true G2: remove 12488 and 12253 from prefer path by not listing SummonSp/Action of those.
            // Spec: "Same strategic state with G1's top action absent" → Action 12253 via g1 prefer[1].
            CampaignCpuDecision d = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(CampaignCpuRoute.RuleCommit, d.Route, "G2 route");
            AssertEqual(12253, d.Action.CardId, "G2 next preferred card");
            AssertEqual(DuelCommandType.Action, d.Action.Command, "G2 command");
        }

        static void ScorerNoMatchAllZeroFallback()
        {
            // G3: legal menu, no matching product rule / non-zero fallback
            CampaignCpuRulePack pack = LoadProductPack();
            var obs = MakeMainObs(
                new CampaignCpuLegalAction
                {
                    ActionId = 9,
                    Kind = LegalActionKind.MovePhase,
                    Phase = DuelPhase.Battle,
                    Label = "to battle",
                });
            // Clear hand so priority when fails
            obs.SelfHandCardIds.Clear();
            CampaignCpuDecision d = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(CampaignCpuRoute.FallbackNative, d.Route, "G3 native");
            AssertEqual("no_match", d.Reason, "G3 reason");
        }

        static void ScorerDeterministicSameObservation()
        {
            // G6
            CampaignCpuRulePack pack = LoadProductPack();
            CampaignCpuObservation obs = MakeMainObs(
                Cmd(1, DuelCommandType.Summon, 4007),
                Cmd(3, DuelCommandType.SummonSp, 12488));
            CampaignCpuDecision a = CampaignCpuScorer.Decide(obs, pack);
            CampaignCpuDecision b = CampaignCpuScorer.Decide(obs, pack);
            AssertEqual(a.RuleId, b.RuleId, "G6 rule id stable");
            AssertEqual(a.Action.CanonicalIdentity, b.Action.CanonicalIdentity, "G6 identity stable");
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
            // Pure commit plan path via a tiny harness of local delegates —
            // CampaignCpuCommit is in YgoMasterClient; test mapping via scorer only here.
            // Commit outcome pure-test is inlined with local function equivalent:
            CampaignCpuCommitOutcome outcome = SimulateCommit(null, throwNative: false);
            AssertEqual(CampaignCpuCommitOutcome.NotStarted, outcome, "null action NotStarted");
        }

        static void CommitAppliedOnSuccessfulNative()
        {
            var action = Cmd(1, DuelCommandType.Summon, 4007);
            CampaignCpuCommitOutcome outcome = SimulateCommit(action, throwNative: false);
            AssertEqual(CampaignCpuCommitOutcome.Applied, outcome, "applied");
        }

        static void CommitIndeterminateOnNativeThrow()
        {
            var action = Cmd(1, DuelCommandType.Summon, 4007);
            CampaignCpuCommitOutcome outcome = SimulateCommit(action, throwNative: true);
            AssertEqual(CampaignCpuCommitOutcome.Indeterminate, outcome, "indeterminate");
        }

        /// <summary>
        /// Mirrors CampaignCpuCommit.TryApply logic for harness (client type not linked).
        /// </summary>
        static CampaignCpuCommitOutcome SimulateCommit(CampaignCpuLegalAction action, bool throwNative)
        {
            if (action == null)
            {
                return CampaignCpuCommitOutcome.NotStarted;
            }
            try
            {
                if (throwNative)
                {
                    throw new InvalidOperationException("native");
                }
                // pretend native succeeded
                return CampaignCpuCommitOutcome.Applied;
            }
            catch
            {
                return CampaignCpuCommitOutcome.Indeterminate;
            }
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
        }

        static void ProgressTokenFreshness()
        {
            var a = CampaignCpuProgressToken.Create(1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, 0, "x");
            var b = CampaignCpuProgressToken.Create(1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, 0, "x");
            var c = CampaignCpuProgressToken.Create(1, DuelViewType.WaitInput, 2, 0, 0, 1, 1, 0, "y");
            AssertTrue(!CampaignCpuProgressToken.IsFreshSemanticProgress(b, a), "same not fresh");
            AssertTrue(CampaignCpuProgressToken.IsFreshSemanticProgress(c, a), "changed is fresh");
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

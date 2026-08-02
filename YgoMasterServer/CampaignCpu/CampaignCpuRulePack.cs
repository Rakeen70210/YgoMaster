using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    sealed class CampaignCpuMatchSpec
    {
        public bool HasKind;
        public LegalActionKind Kind;
        public bool HasCommand;
        public DuelCommandType Command;
        public bool HasPhase;
        public DuelPhase Phase;
        public bool HasCardId;
        public int CardId;
        public bool HasPosition;
        public int Position;
        public bool PositionWildcard;
        public string ActionLabelContains;
        public bool HasIsMechanical;
        public bool IsMechanical;
        public string TargetScope;
    }

    sealed class CampaignCpuWhenSpec
    {
        public List<DuelPhase> PhaseIn;
        public int? TurnLte;
        public int? TurnGte;
        public List<int> SelfHasCardId;
        /// <summary>
        /// Fail when any listed card id is present in self hand or face-up field.
        /// Used to make lower-priority rules (e.g. G2) disjoint from higher ones (G1).
        /// </summary>
        public List<int> SelfLacksCardId;
        public List<int> SelfHasFieldCardId;
        public string WindowClass;
    }

    sealed class CampaignCpuNeverRule
    {
        public string Id;
        public CampaignCpuMatchSpec Match;
        public string Reason;
    }

    sealed class CampaignCpuPriorityRule
    {
        public string Id;
        public int Priority;
        public int ListIndex;
        public CampaignCpuWhenSpec When;
        public List<CampaignCpuMatchSpec> Prefer;
        public int ScoreBonus;
        public bool RequiredForSlice;
    }

    sealed class CampaignCpuFallbackRule
    {
        public string Id;
        public CampaignCpuMatchSpec Match;
        public int Score;
        public bool RequiredForSlice;
    }

    sealed class CampaignCpuPackPolicy
    {
        public string OnNoMatch = "native_cpu";
        public string OnUnsupportedWindow = "native_cpu";
        public string OnZeroLegal = "native_cpu";
        public string MechanicalWindows = "auto_or_native";
        public List<string> ScriptedViews;
        public int MaxDecisionsPerDuel;
        public bool SuccessiveMainRecapture;
        public int MaxRecapturesPerChain =
            CampaignCpuRecapturePolicy.DefaultMaxAttemptsPerChain;
        public int MaxRecapturesPerTurn =
            CampaignCpuRecapturePolicy.DefaultMaxAttemptsPerTurn;
        public int RecaptureTimeoutMs =
            CampaignCpuRecapturePolicy.DefaultTimeoutMs;
        public bool PositionSafetyEnabled;
        public string PhaseExitPolicy = "native_cpu";
        public List<int> PositionSafetyExceptionCardIds;
        public int? AttackTurnRaw;
        public int? DefenseTurnRaw;
        public bool OpeningSetSafetyEnabled;
        public List<int> OpeningSetSafetyCardIds;

        public CampaignCpuPackPolicy()
        {
            ScriptedViews = new List<string> { "WaitInput_MainPhase" };
            PositionSafetyExceptionCardIds = new List<int>();
            OpeningSetSafetyCardIds = new List<int>();
        }
    }

    sealed class CampaignCpuRulePack
    {
        public int Version;
        public int ChapterId;
        public string DeckHash;
        public string Name;
        public string Archetype;
        public CampaignCpuPackPolicy Policy;
        public List<CampaignCpuNeverRule> Never;
        public List<CampaignCpuPriorityRule> Priority;
        public List<CampaignCpuFallbackRule> FallbackScoring;
        public string SourcePath;

        public CampaignCpuRulePack()
        {
            Policy = new CampaignCpuPackPolicy();
            Never = new List<CampaignCpuNeverRule>();
            Priority = new List<CampaignCpuPriorityRule>();
            FallbackScoring = new List<CampaignCpuFallbackRule>();
        }
    }

    sealed class CampaignCpuChapterIndexEntry
    {
        public string PackRelativePath;
        public bool Enabled;
        public string Notes;
    }

    sealed class CampaignCpuRuleIndex
    {
        public int Version;
        public bool DefaultEnabled;
        public Dictionary<int, CampaignCpuChapterIndexEntry> Chapters;
        public string SourceDir;

        public CampaignCpuRuleIndex()
        {
            Chapters = new Dictionary<int, CampaignCpuChapterIndexEntry>();
        }
    }

    static class CampaignCpuRulePackLoader
    {
        public const int SupportedIndexVersion = 1;
        public const int SupportedPackVersion = 1;

        /// <summary>
        /// True when fullPath is the root itself or a file/dir strictly under root.
        /// Uses a separator boundary so sibling prefixes like "rules-evil" do not match root "rules".
        /// </summary>
        public static bool IsPathContainedUnderRoot(string fullPath, string root)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(root))
            {
                return false;
            }
            string full = Path.GetFullPath(fullPath);
            string rootFull = Path.GetFullPath(root);
            string fullNorm = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
            string rootNorm = rootFull.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(fullNorm, rootNorm, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            string rootPrefix = rootNorm + Path.DirectorySeparatorChar;
            string fullWithSep = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (!fullWithSep.EndsWith(Path.DirectorySeparatorChar.ToString())
                && Directory.Exists(fullWithSep))
            {
                // file path under root still compared as-is
            }
            return fullWithSep.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static CampaignCpuRuleIndex LoadIndex(string rulesDir)
        {
            if (string.IsNullOrEmpty(rulesDir) || !Directory.Exists(rulesDir))
            {
                throw new InvalidOperationException("CampaignCpu rules dir missing: " + rulesDir);
            }
            string indexPath = Path.Combine(rulesDir, "index.json");
            if (!File.Exists(indexPath))
            {
                throw new InvalidOperationException("CampaignCpu index.json missing: " + indexPath);
            }
            string text = File.ReadAllText(indexPath);
            Dictionary<string, object> root =
                MiniJSON.Json.DeserializeStripped(text) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidOperationException("CampaignCpu index.json invalid JSON");
            }

            int version = Utils.GetValue<int>(root, "version", 1);
            if (version != SupportedIndexVersion)
            {
                throw new InvalidOperationException(
                    "CampaignCpu index unsupported version " + version
                    + " (supported " + SupportedIndexVersion + ")");
            }

            // v1: default_enabled must not silently enable chapters. false is allowed (and
            // is the product default); true is rejected so authors use per-chapter enabled.
            bool defaultEnabled = Utils.GetValue<bool>(root, "default_enabled", false);
            if (defaultEnabled)
            {
                throw new InvalidOperationException(
                    "CampaignCpu index default_enabled=true is unsupported in v1; "
                    + "set per-chapter enabled explicitly (default_enabled must be false or omitted)");
            }

            var index = new CampaignCpuRuleIndex
            {
                Version = version,
                DefaultEnabled = false,
                SourceDir = Path.GetFullPath(rulesDir),
            };

            Dictionary<string, object> chapters =
                Utils.GetDictionary(root, "chapters");
            if (chapters != null)
            {
                foreach (KeyValuePair<string, object> kv in chapters)
                {
                    int chapterId;
                    if (!int.TryParse(kv.Key, out chapterId))
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu index chapter key not int: " + kv.Key);
                    }
                    Dictionary<string, object> entryDict = kv.Value as Dictionary<string, object>;
                    if (entryDict == null)
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu index chapter entry not object: " + kv.Key);
                    }
                    string packRel = Utils.GetValue<string>(entryDict, "pack");
                    if (string.IsNullOrEmpty(packRel))
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu index missing pack for chapter " + chapterId);
                    }
                    string fullPack = Path.GetFullPath(Path.Combine(index.SourceDir, packRel));
                    if (!IsPathContainedUnderRoot(fullPack, index.SourceDir))
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu pack path escapes rules root: " + packRel);
                    }
                    index.Chapters[chapterId] = new CampaignCpuChapterIndexEntry
                    {
                        PackRelativePath = packRel.Replace('\\', '/'),
                        Enabled = Utils.GetValue<bool>(entryDict, "enabled", false),
                        Notes = Utils.GetValue<string>(entryDict, "notes") ?? string.Empty,
                    };
                }
            }
            return index;
        }

        public static CampaignCpuRulePack LoadPack(
            string rulesDir,
            CampaignCpuChapterIndexEntry entry,
            bool requireDeckHash)
        {
            if (entry == null)
            {
                throw new ArgumentNullException("entry");
            }
            string root = Path.GetFullPath(rulesDir);
            string fullPack = Path.GetFullPath(Path.Combine(root, entry.PackRelativePath));
            if (!IsPathContainedUnderRoot(fullPack, root))
            {
                throw new InvalidOperationException(
                    "CampaignCpu pack path escapes rules root: " + entry.PackRelativePath);
            }
            if (!File.Exists(fullPack))
            {
                throw new InvalidOperationException("CampaignCpu pack missing: " + fullPack);
            }
            return LoadPackFromText(File.ReadAllText(fullPack), fullPack, requireDeckHash || entry.Enabled);
        }

        public static CampaignCpuRulePack LoadPackFromText(
            string text,
            string sourcePath,
            bool requireDeckHash)
        {
            Dictionary<string, object> root =
                MiniJSON.Json.DeserializeStripped(text) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidOperationException("CampaignCpu pack invalid JSON: " + sourcePath);
            }

            int version = Utils.GetValue<int>(root, "version", 1);
            if (version != SupportedPackVersion)
            {
                throw new InvalidOperationException(
                    "CampaignCpu pack unsupported version " + version
                    + " (supported " + SupportedPackVersion + ") in " + sourcePath);
            }

            int chapterId = Utils.GetValue<int>(root, "chapter_id");
            if (chapterId <= 0)
            {
                throw new InvalidOperationException(
                    "CampaignCpu pack chapter_id must be positive: " + sourcePath);
            }

            var pack = new CampaignCpuRulePack
            {
                Version = version,
                ChapterId = chapterId,
                DeckHash = Utils.GetValue<string>(root, "deck_hash") ?? string.Empty,
                SourcePath = sourcePath ?? string.Empty,
            };

            Dictionary<string, object> meta = Utils.GetDictionary(root, "meta");
            if (meta != null)
            {
                pack.Name = Utils.GetValue<string>(meta, "name") ?? string.Empty;
                pack.Archetype = Utils.GetValue<string>(meta, "archetype") ?? string.Empty;
            }

            if (requireDeckHash)
            {
                if (!CampaignCpuDeckFingerprint.IsWellFormedHash(pack.DeckHash))
                {
                    throw new InvalidOperationException(
                        "CampaignCpu pack missing/malformed deck_hash (required when enabled): "
                        + sourcePath);
                }
            }
            else if (!string.IsNullOrEmpty(pack.DeckHash)
                && !CampaignCpuDeckFingerprint.IsWellFormedHash(pack.DeckHash))
            {
                throw new InvalidOperationException(
                    "CampaignCpu pack deck_hash malformed: " + sourcePath);
            }

            Dictionary<string, object> policyDict = Utils.GetDictionary(root, "policy");
            if (policyDict != null)
            {
                ParsePolicy(policyDict, pack.Policy, sourcePath);
            }

            List<object> neverList = Utils.GetValue(root, "never", (List<object>)null);
            if (neverList != null)
            {
                for (int i = 0; i < neverList.Count; i++)
                {
                    Dictionary<string, object> ruleDict = neverList[i] as Dictionary<string, object>;
                    if (ruleDict == null)
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu never rule not object at index " + i + " in " + sourcePath);
                    }
                    pack.Never.Add(new CampaignCpuNeverRule
                    {
                        Id = Utils.GetValue<string>(ruleDict, "id") ?? ("never_" + i),
                        Match = ParseMatch(Utils.GetDictionary(ruleDict, "match"), sourcePath),
                        Reason = Utils.GetValue<string>(ruleDict, "reason") ?? string.Empty,
                    });
                }
            }

            List<object> priorityList = Utils.GetValue(root, "priority", (List<object>)null);
            if (priorityList != null)
            {
                for (int i = 0; i < priorityList.Count; i++)
                {
                    Dictionary<string, object> ruleDict = priorityList[i] as Dictionary<string, object>;
                    if (ruleDict == null)
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu priority rule not object at index " + i + " in " + sourcePath);
                    }
                    var rule = new CampaignCpuPriorityRule
                    {
                        Id = Utils.GetValue<string>(ruleDict, "id") ?? ("priority_" + i),
                        Priority = Utils.GetValue<int>(ruleDict, "priority", 0),
                        ListIndex = i,
                        When = ParseWhen(Utils.GetDictionary(ruleDict, "when"), sourcePath),
                        Prefer = new List<CampaignCpuMatchSpec>(),
                        ScoreBonus = Utils.GetValue<int>(ruleDict, "score_bonus", 0),
                        RequiredForSlice = Utils.GetValue<bool>(ruleDict, "required_for_slice", false),
                    };
                    List<object> preferList = Utils.GetValue(ruleDict, "prefer", (List<object>)null);
                    if (preferList != null)
                    {
                        for (int p = 0; p < preferList.Count; p++)
                        {
                            Dictionary<string, object> pref =
                                preferList[p] as Dictionary<string, object>;
                            if (pref == null)
                            {
                                throw new InvalidOperationException(
                                    "CampaignCpu prefer entry not object in rule "
                                    + rule.Id + " in " + sourcePath);
                            }
                            rule.Prefer.Add(ParseMatch(pref, sourcePath));
                        }
                    }
                    pack.Priority.Add(rule);
                }
            }

            List<object> fallbackList = Utils.GetValue(root, "fallback_scoring", (List<object>)null);
            if (fallbackList != null)
            {
                for (int i = 0; i < fallbackList.Count; i++)
                {
                    Dictionary<string, object> ruleDict = fallbackList[i] as Dictionary<string, object>;
                    if (ruleDict == null)
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu fallback rule not object at index " + i + " in " + sourcePath);
                    }
                    pack.FallbackScoring.Add(new CampaignCpuFallbackRule
                    {
                        Id = Utils.GetValue<string>(ruleDict, "id") ?? ("fallback_" + i),
                        Match = ParseMatch(Utils.GetDictionary(ruleDict, "match"), sourcePath),
                        Score = Utils.GetValue<int>(ruleDict, "score", 0),
                        RequiredForSlice = Utils.GetValue<bool>(ruleDict, "required_for_slice", false),
                    });
                }
            }

            return pack;
        }

        static void ParsePolicy(
            Dictionary<string, object> policyDict,
            CampaignCpuPackPolicy policy,
            string sourcePath)
        {
            if (policyDict.ContainsKey("on_no_match"))
            {
                policy.OnNoMatch = RequirePolicyToken(
                    Utils.GetValue<string>(policyDict, "on_no_match"),
                    "on_no_match",
                    sourcePath,
                    "native_cpu");
            }
            if (policyDict.ContainsKey("on_unsupported_window"))
            {
                policy.OnUnsupportedWindow = RequirePolicyToken(
                    Utils.GetValue<string>(policyDict, "on_unsupported_window"),
                    "on_unsupported_window",
                    sourcePath,
                    "native_cpu");
            }
            if (policyDict.ContainsKey("on_zero_legal"))
            {
                policy.OnZeroLegal = RequirePolicyToken(
                    Utils.GetValue<string>(policyDict, "on_zero_legal"),
                    "on_zero_legal",
                    sourcePath,
                    "native_cpu");
            }
            if (policyDict.ContainsKey("mechanical_windows"))
            {
                policy.MechanicalWindows = RequirePolicyToken(
                    Utils.GetValue<string>(policyDict, "mechanical_windows"),
                    "mechanical_windows",
                    sourcePath,
                    "auto_or_native");
            }
            if (policyDict.ContainsKey("max_decisions_per_duel"))
            {
                int max;
                if (!TryParseStrictInt(policyDict["max_decisions_per_duel"], out max) || max < 0)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu policy max_decisions_per_duel must be int >= 0 in "
                        + sourcePath);
                }
                policy.MaxDecisionsPerDuel = max;
            }
            if (policyDict.ContainsKey("successive_main_recapture"))
            {
                object raw = policyDict["successive_main_recapture"];
                if (!(raw is bool))
                {
                    throw new InvalidOperationException(
                        "CampaignCpu policy successive_main_recapture must be bool in "
                        + sourcePath);
                }
                policy.SuccessiveMainRecapture = (bool)raw;
            }
            if (policyDict.ContainsKey("max_recaptures_per_chain"))
            {
                int value;
                if (!TryParseStrictInt(policyDict["max_recaptures_per_chain"], out value)
                    || value < 1)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu policy max_recaptures_per_chain must be int >= 1 in "
                        + sourcePath);
                }
                policy.MaxRecapturesPerChain = value;
            }
            if (policyDict.ContainsKey("max_recaptures_per_turn"))
            {
                int value;
                if (!TryParseStrictInt(policyDict["max_recaptures_per_turn"], out value)
                    || value < 1)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu policy max_recaptures_per_turn must be int >= 1 in "
                        + sourcePath);
                }
                policy.MaxRecapturesPerTurn = value;
            }
            if (policyDict.ContainsKey("recapture_timeout_ms"))
            {
                int value;
                if (!TryParseStrictInt(policyDict["recapture_timeout_ms"], out value)
                    || value < 1)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu policy recapture_timeout_ms must be int >= 1 in "
                        + sourcePath);
                }
                policy.RecaptureTimeoutMs = value;
            }
            if (policyDict.ContainsKey("position_safety_enabled"))
            {
                object raw = policyDict["position_safety_enabled"];
                if (!(raw is bool))
                {
                    throw new InvalidOperationException(
                        "CampaignCpu policy position_safety_enabled must be bool in "
                        + sourcePath);
                }
                policy.PositionSafetyEnabled = (bool)raw;
            }
            if (policyDict.ContainsKey("opening_set_safety_enabled"))
            {
                object raw = policyDict["opening_set_safety_enabled"];
                if (!(raw is bool))
                {
                    throw new InvalidOperationException(
                        "CampaignCpu policy opening_set_safety_enabled must be bool in "
                        + sourcePath);
                }
                policy.OpeningSetSafetyEnabled = (bool)raw;
            }
            if (policyDict.ContainsKey("opening_set_safety_card_ids"))
            {
                List<object> openingSetCards =
                    policyDict["opening_set_safety_card_ids"]
                    as List<object>;
                if (openingSetCards == null)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu policy opening_set_safety_card_ids must be an array in "
                        + sourcePath);
                }
                if (openingSetCards.Count == 0)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu policy opening_set_safety_card_ids must not be empty in "
                        + sourcePath);
                }
                policy.OpeningSetSafetyCardIds = new List<int>();
                for (int i = 0; i < openingSetCards.Count; i++)
                {
                    int cardId;
                    if (!TryParseStrictInt(openingSetCards[i], out cardId)
                        || cardId <= 0)
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu policy opening_set_safety_card_ids entries must "
                            + "be positive card ids in " + sourcePath);
                    }
                    policy.OpeningSetSafetyCardIds.Add(cardId);
                }
            }
            if (policy.OpeningSetSafetyEnabled
                && policy.OpeningSetSafetyCardIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "CampaignCpu policy opening_set_safety_enabled requires a nonempty "
                    + "opening_set_safety_card_ids list in " + sourcePath);
            }
            if (policyDict.ContainsKey("phase_exit_policy"))
            {
                policy.PhaseExitPolicy = RequirePolicyToken(
                    Utils.GetValue<string>(policyDict, "phase_exit_policy"),
                    "phase_exit_policy",
                    sourcePath,
                    "native_cpu",
                    "battle_then_end");
            }
            List<object> exceptions =
                Utils.GetValue(policyDict, "position_safety_exceptions", (List<object>)null);
            if (exceptions != null)
            {
                policy.PositionSafetyExceptionCardIds = new List<int>();
                for (int i = 0; i < exceptions.Count; i++)
                {
                    int cardId;
                    if (!TryParseStrictInt(exceptions[i], out cardId) || cardId <= 0)
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu policy position_safety_exceptions entries must "
                            + "be positive card ids in " + sourcePath);
                    }
                    policy.PositionSafetyExceptionCardIds.Add(cardId);
                }
            }
            ParseOptionalTurnRaw(
                policyDict, "attack_turn_raw", sourcePath, out policy.AttackTurnRaw);
            ParseOptionalTurnRaw(
                policyDict, "defense_turn_raw", sourcePath, out policy.DefenseTurnRaw);
            List<object> scripted =
                Utils.GetValue(policyDict, "scripted_views", (List<object>)null);
            if (scripted != null)
            {
                if (scripted.Count == 0)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu policy scripted_views must not be empty when present in "
                        + sourcePath);
                }
                policy.ScriptedViews = new List<string>();
                for (int i = 0; i < scripted.Count; i++)
                {
                    if (scripted[i] == null || string.IsNullOrEmpty(scripted[i].ToString()))
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu policy scripted_views entry null/empty in " + sourcePath);
                    }
                    string view = scripted[i].ToString().Trim();
                    if (!IsKnownScriptedView(view))
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu policy unknown scripted_views entry '"
                            + view + "' in " + sourcePath);
                    }
                    policy.ScriptedViews.Add(view);
                }
            }
        }

        static void ParseOptionalTurnRaw(
            Dictionary<string, object> policyDict,
            string field,
            string sourcePath,
            out int? value)
        {
            value = null;
            if (!policyDict.ContainsKey(field))
            {
                return;
            }
            int parsed;
            if (!TryParseStrictInt(policyDict[field], out parsed))
            {
                throw new InvalidOperationException(
                    "CampaignCpu policy " + field + " must be int in " + sourcePath);
            }
            value = parsed;
        }

        static string RequirePolicyToken(
            string raw,
            string field,
            string sourcePath,
            params string[] allowed)
        {
            if (string.IsNullOrEmpty(raw))
            {
                throw new InvalidOperationException(
                    "CampaignCpu policy " + field + " empty in " + sourcePath);
            }
            string token = raw.Trim();
            for (int i = 0; i < allowed.Length; i++)
            {
                if (string.Equals(token, allowed[i], StringComparison.OrdinalIgnoreCase))
                {
                    return allowed[i];
                }
            }
            throw new InvalidOperationException(
                "CampaignCpu policy " + field + " unsupported value '"
                + token + "' in " + sourcePath);
        }

        static bool IsKnownScriptedView(string view)
        {
            return string.Equals(view, "WaitInput_MainPhase", StringComparison.OrdinalIgnoreCase);
        }

        static CampaignCpuMatchSpec ParseMatch(Dictionary<string, object> dict, string sourcePath)
        {
            var spec = new CampaignCpuMatchSpec();
            if (dict == null)
            {
                return spec;
            }

            if (dict.ContainsKey("kind"))
            {
                string kindRaw = Utils.GetValue<string>(dict, "kind");
                LegalActionKind kind;
                if (!CampaignCpuCommandAliases.TryResolveKind(kindRaw, out kind))
                {
                    throw new InvalidOperationException(
                        "CampaignCpu unknown kind '" + kindRaw + "' in " + sourcePath);
                }
                spec.HasKind = true;
                spec.Kind = kind;
            }

            if (dict.ContainsKey("command"))
            {
                object cmdObj = dict["command"];
                string cmdRaw = cmdObj != null ? cmdObj.ToString() : null;
                DuelCommandType cmd;
                if (!CampaignCpuCommandAliases.TryResolveCommand(cmdRaw, out cmd))
                {
                    throw new InvalidOperationException(
                        "CampaignCpu unknown command '" + cmdRaw + "' in " + sourcePath);
                }
                spec.HasCommand = true;
                spec.Command = cmd;
            }

            if (dict.ContainsKey("phase"))
            {
                string phaseRaw = Utils.GetValue<string>(dict, "phase");
                if (phaseRaw == null && dict["phase"] != null)
                {
                    phaseRaw = dict["phase"].ToString();
                }
                DuelPhase phase;
                if (!CampaignCpuCommandAliases.TryResolvePhase(phaseRaw, out phase))
                {
                    throw new InvalidOperationException(
                        "CampaignCpu unknown phase '" + phaseRaw + "' in " + sourcePath);
                }
                spec.HasPhase = true;
                spec.Phase = phase;
            }

            if (dict.ContainsKey("card_id"))
            {
                int cardId;
                if (!TryParseStrictInt(dict["card_id"], out cardId) || cardId <= 0)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu match card_id must be positive int in " + sourcePath);
                }
                spec.HasCardId = true;
                spec.CardId = cardId;
            }

            string zoneOrPos = null;
            if (dict.ContainsKey("zone"))
            {
                zoneOrPos = Utils.GetValue<string>(dict, "zone");
            }
            else if (dict.ContainsKey("position"))
            {
                object posObj = dict["position"];
                zoneOrPos = posObj != null ? posObj.ToString() : null;
            }
            if (!string.IsNullOrEmpty(zoneOrPos))
            {
                int position;
                bool wildcard;
                if (!CampaignCpuCommandAliases.TryResolveZone(zoneOrPos, out position, out wildcard))
                {
                    throw new InvalidOperationException(
                        "CampaignCpu unknown zone/position '" + zoneOrPos + "' in " + sourcePath);
                }
                if (!wildcard)
                {
                    spec.HasPosition = true;
                    spec.Position = position;
                }
                else
                {
                    spec.PositionWildcard = true;
                }
            }

            if (dict.ContainsKey("action_label_contains"))
            {
                spec.ActionLabelContains = Utils.GetValue<string>(dict, "action_label_contains");
            }
            if (dict.ContainsKey("is_mechanical"))
            {
                spec.HasIsMechanical = true;
                spec.IsMechanical = Utils.GetValue<bool>(dict, "is_mechanical");
            }
            if (dict.ContainsKey("target_scope"))
            {
                spec.TargetScope = Utils.GetValue<string>(dict, "target_scope");
            }
            return spec;
        }

        static CampaignCpuWhenSpec ParseWhen(Dictionary<string, object> dict, string sourcePath)
        {
            var when = new CampaignCpuWhenSpec();
            if (dict == null)
            {
                return when;
            }
            List<object> phaseIn = Utils.GetValue(dict, "phase_in", (List<object>)null);
            if (phaseIn != null)
            {
                if (phaseIn.Count == 0)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu when.phase_in must not be empty when present in " + sourcePath);
                }
                when.PhaseIn = new List<DuelPhase>();
                for (int i = 0; i < phaseIn.Count; i++)
                {
                    if (phaseIn[i] == null)
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu when.phase_in entry null in " + sourcePath);
                    }
                    DuelPhase phase;
                    if (!CampaignCpuCommandAliases.TryResolvePhase(phaseIn[i].ToString(), out phase))
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu when.phase_in unknown phase '"
                            + phaseIn[i] + "' in " + sourcePath);
                    }
                    when.PhaseIn.Add(phase);
                }
            }
            if (dict.ContainsKey("turn_lte"))
            {
                int turn;
                if (!TryParseStrictInt(dict["turn_lte"], out turn))
                {
                    throw new InvalidOperationException(
                        "CampaignCpu when.turn_lte must be int in " + sourcePath);
                }
                when.TurnLte = turn;
            }
            if (dict.ContainsKey("turn_gte"))
            {
                int turn;
                if (!TryParseStrictInt(dict["turn_gte"], out turn))
                {
                    throw new InvalidOperationException(
                        "CampaignCpu when.turn_gte must be int in " + sourcePath);
                }
                when.TurnGte = turn;
            }
            if (dict.ContainsKey("self_has_card_id"))
            {
                when.SelfHasCardId = ParsePositiveIntList(
                    dict["self_has_card_id"], "self_has_card_id", sourcePath);
            }
            if (dict.ContainsKey("self_lacks_card_id"))
            {
                when.SelfLacksCardId = ParsePositiveIntList(
                    dict["self_lacks_card_id"], "self_lacks_card_id", sourcePath);
            }
            if (dict.ContainsKey("self_has_field_card_id"))
            {
                when.SelfHasFieldCardId = ParsePositiveIntList(
                    dict["self_has_field_card_id"], "self_has_field_card_id", sourcePath);
            }
            when.WindowClass = Utils.GetValue<string>(dict, "window_class");
            return when;
        }

        static List<int> ParsePositiveIntList(object raw, string field, string sourcePath)
        {
            List<object> items = raw as List<object>;
            if (items == null)
            {
                throw new InvalidOperationException(
                    "CampaignCpu when." + field + " must be array in " + sourcePath);
            }
            if (items.Count == 0)
            {
                throw new InvalidOperationException(
                    "CampaignCpu when." + field + " must not be empty when present in " + sourcePath);
            }
            var result = new List<int>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu when." + field + " entry null in " + sourcePath);
                }
                int id;
                if (!TryParseStrictInt(items[i], out id) || id <= 0)
                {
                    throw new InvalidOperationException(
                        "CampaignCpu when." + field + " entry must be positive int (got '"
                        + items[i] + "') in " + sourcePath);
                }
                result.Add(id);
            }
            return result;
        }

        static bool TryParseStrictInt(object raw, out int value)
        {
            value = 0;
            if (raw == null)
            {
                return false;
            }
            if (raw is int)
            {
                value = (int)raw;
                return true;
            }
            if (raw is long)
            {
                long asLong = (long)raw;
                if (asLong < int.MinValue || asLong > int.MaxValue)
                {
                    return false;
                }
                value = (int)asLong;
                return true;
            }
            if (raw is double)
            {
                double d = (double)raw;
                if (d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue)
                {
                    return false;
                }
                value = (int)d;
                return true;
            }
            return int.TryParse(raw.ToString(), out value);
        }
    }
}

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

        public CampaignCpuPackPolicy()
        {
            ScriptedViews = new List<string> { "WaitInput_MainPhase" };
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

            var index = new CampaignCpuRuleIndex
            {
                Version = Utils.GetValue<int>(root, "version", 1),
                DefaultEnabled = Utils.GetValue<bool>(root, "default_enabled", false),
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
                        continue;
                    }
                    string packRel = Utils.GetValue<string>(entryDict, "pack");
                    if (string.IsNullOrEmpty(packRel))
                    {
                        throw new InvalidOperationException(
                            "CampaignCpu index missing pack for chapter " + chapterId);
                    }
                    // Path traversal guard
                    string fullPack = Path.GetFullPath(Path.Combine(index.SourceDir, packRel));
                    if (!fullPack.StartsWith(index.SourceDir, StringComparison.OrdinalIgnoreCase))
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
            if (!fullPack.StartsWith(root, StringComparison.OrdinalIgnoreCase))
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

            var pack = new CampaignCpuRulePack
            {
                Version = Utils.GetValue<int>(root, "version", 1),
                ChapterId = Utils.GetValue<int>(root, "chapter_id"),
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
                pack.Policy.OnNoMatch = Utils.GetValue<string>(policyDict, "on_no_match")
                    ?? pack.Policy.OnNoMatch;
                pack.Policy.OnUnsupportedWindow =
                    Utils.GetValue<string>(policyDict, "on_unsupported_window")
                    ?? pack.Policy.OnUnsupportedWindow;
                pack.Policy.OnZeroLegal = Utils.GetValue<string>(policyDict, "on_zero_legal")
                    ?? pack.Policy.OnZeroLegal;
                pack.Policy.MechanicalWindows =
                    Utils.GetValue<string>(policyDict, "mechanical_windows")
                    ?? pack.Policy.MechanicalWindows;
                pack.Policy.MaxDecisionsPerDuel =
                    Utils.GetValue<int>(policyDict, "max_decisions_per_duel", 0);
                List<object> scripted =
                    Utils.GetValue(policyDict, "scripted_views", (List<object>)null);
                if (scripted != null && scripted.Count > 0)
                {
                    pack.Policy.ScriptedViews = new List<string>();
                    for (int i = 0; i < scripted.Count; i++)
                    {
                        if (scripted[i] != null)
                        {
                            pack.Policy.ScriptedViews.Add(scripted[i].ToString());
                        }
                    }
                }
            }

            List<object> neverList = Utils.GetValue(root, "never", (List<object>)null);
            if (neverList != null)
            {
                for (int i = 0; i < neverList.Count; i++)
                {
                    Dictionary<string, object> ruleDict = neverList[i] as Dictionary<string, object>;
                    if (ruleDict == null)
                    {
                        continue;
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
                        continue;
                    }
                    var rule = new CampaignCpuPriorityRule
                    {
                        Id = Utils.GetValue<string>(ruleDict, "id") ?? ("priority_" + i),
                        Priority = Utils.GetValue<int>(ruleDict, "priority", 0),
                        ListIndex = i,
                        When = ParseWhen(Utils.GetDictionary(ruleDict, "when")),
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
                            if (pref != null)
                            {
                                rule.Prefer.Add(ParseMatch(pref, sourcePath));
                            }
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
                        continue;
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
                spec.HasCardId = true;
                spec.CardId = Utils.GetValue<int>(dict, "card_id");
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

        static CampaignCpuWhenSpec ParseWhen(Dictionary<string, object> dict)
        {
            var when = new CampaignCpuWhenSpec();
            if (dict == null)
            {
                return when;
            }
            List<object> phaseIn = Utils.GetValue(dict, "phase_in", (List<object>)null);
            if (phaseIn != null && phaseIn.Count > 0)
            {
                when.PhaseIn = new List<DuelPhase>();
                for (int i = 0; i < phaseIn.Count; i++)
                {
                    if (phaseIn[i] == null)
                    {
                        continue;
                    }
                    DuelPhase phase;
                    if (CampaignCpuCommandAliases.TryResolvePhase(phaseIn[i].ToString(), out phase))
                    {
                        when.PhaseIn.Add(phase);
                    }
                }
            }
            if (dict.ContainsKey("turn_lte"))
            {
                when.TurnLte = Utils.GetValue<int>(dict, "turn_lte");
            }
            if (dict.ContainsKey("turn_gte"))
            {
                when.TurnGte = Utils.GetValue<int>(dict, "turn_gte");
            }
            List<object> selfCards = Utils.GetValue(dict, "self_has_card_id", (List<object>)null);
            if (selfCards != null)
            {
                when.SelfHasCardId = new List<int>();
                for (int i = 0; i < selfCards.Count; i++)
                {
                    if (selfCards[i] == null)
                    {
                        continue;
                    }
                    int id;
                    if (int.TryParse(selfCards[i].ToString(), out id))
                    {
                        when.SelfHasCardId.Add(id);
                    }
                }
            }
            List<object> fieldCards = Utils.GetValue(dict, "self_has_field_card_id", (List<object>)null);
            if (fieldCards != null)
            {
                when.SelfHasFieldCardId = new List<int>();
                for (int i = 0; i < fieldCards.Count; i++)
                {
                    if (fieldCards[i] == null)
                    {
                        continue;
                    }
                    int id;
                    if (int.TryParse(fieldCards[i].ToString(), out id))
                    {
                        when.SelfHasFieldCardId.Add(id);
                    }
                }
            }
            when.WindowClass = Utils.GetValue<string>(dict, "window_class");
            return when;
        }
    }
}

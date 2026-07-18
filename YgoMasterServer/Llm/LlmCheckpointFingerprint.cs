using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// Allowed-information checkpoint fingerprint (public + controlled self only).
    /// Never includes opponent hand/set/deck/extra private identities.
    /// </summary>
    public sealed class LlmCheckpointFingerprint
    {
        public string AllowedHash { get; set; }
        public int ControlledPlayer { get; set; }
        public Dictionary<string, object> PublicState { get; set; }
        public Dictionary<string, object> SelfResources { get; set; }

        public LlmCheckpointFingerprint()
        {
            PublicState = new Dictionary<string, object>();
            SelfResources = new Dictionary<string, object>();
            AllowedHash = string.Empty;
        }

        public string ToCanonical()
        {
            Dictionary<string, object> root = new Dictionary<string, object>()
            {
                { "AllowedHash", AllowedHash ?? string.Empty },
                { "ControlledPlayer", ControlledPlayer },
                { "PublicState", PublicState ?? new Dictionary<string, object>() },
                { "SelfResources", SelfResources ?? new Dictionary<string, object>() },
            };
            return MiniJSON.Json.Serialize(root);
        }

        public override string ToString()
        {
            return ToCanonical();
        }
    }

    public sealed class LlmCheckpointFingerprintBuilder
    {
        public static object FromScriptedFixture(string fixtureId)
        {
            return BuildFixtureState(fixtureId);
        }

        public static object CreateFixture(string fixtureId)
        {
            return BuildFixtureState(fixtureId);
        }

        public static object BuildFixtureState(string fixtureId)
        {
            return BuildFixtureState(fixtureId, 0, null, 0, null, 0, null, 0, null);
        }

        public static object FromFrozen(string fixtureId)
        {
            return BuildFixtureState(fixtureId);
        }

        /// <summary>
        /// Scripted checkpoint state. Opponent hidden args are accepted for fixture pairing
        /// but intentionally ignored in the fingerprint (information-set safety).
        /// </summary>
        public static object BuildFixtureState(
            string fixtureId,
            int opponentHandId,
            string opponentHandName,
            int opponentSetId,
            string opponentSetName,
            int opponentDeckId,
            string opponentDeckName,
            int opponentExtraId,
            string opponentExtraName)
        {
            Dictionary<string, object> state = LoadVisibleState(fixtureId);
            // Stash ignored hidden ids only under private key never hashed into fingerprint.
            state["_private_opponent_hidden_ignored"] = new Dictionary<string, object>()
            {
                { "hand_id", opponentHandId },
                { "hand_name", opponentHandName ?? string.Empty },
                { "set_id", opponentSetId },
                { "set_name", opponentSetName ?? string.Empty },
                { "deck_id", opponentDeckId },
                { "deck_name", opponentDeckName ?? string.Empty },
                { "extra_id", opponentExtraId },
                { "extra_name", opponentExtraName ?? string.Empty },
            };
            return state;
        }

        public LlmCheckpointFingerprint Build(object fixtureState)
        {
            return Compute(fixtureState);
        }

        public LlmCheckpointFingerprint Compute(object fixtureState)
        {
            return Create(fixtureState);
        }

        public LlmCheckpointFingerprint Fingerprint(object fixtureState)
        {
            return Create(fixtureState);
        }

        public static LlmCheckpointFingerprint Create(object fixtureState)
        {
            Dictionary<string, object> state = fixtureState as Dictionary<string, object>
                ?? new Dictionary<string, object>();

            Dictionary<string, object> publicState = GetDict(state, "visible_public")
                ?? GetDict(state, "PublicState")
                ?? new Dictionary<string, object>();
            Dictionary<string, object> self = GetDict(state, "controlled_self")
                ?? GetDict(state, "SelfResources")
                ?? new Dictionary<string, object>();

            // Strip any accidental private keys from hash inputs.
            publicState = CloneWithoutPrivate(publicState);
            self = CloneWithoutPrivate(self);

            int controlled = 1;
            object playerObj;
            if (self.TryGetValue("player", out playerObj))
            {
                try { controlled = Convert.ToInt32(playerObj); }
                catch { controlled = 1; }
            }

            string material = MiniJSON.Json.Serialize(new Dictionary<string, object>()
            {
                { "controlled_player", controlled },
                { "public", publicState },
                { "self", self },
            });
            string hash = Sha256Hex(material);

            return new LlmCheckpointFingerprint()
            {
                AllowedHash = hash,
                ControlledPlayer = controlled,
                PublicState = publicState,
                SelfResources = self,
            };
        }

        static Dictionary<string, object> LoadVisibleState(string fixtureId)
        {
            string path = LlmSlice5Paths.ResolveFixturePath(fixtureId);
            if (!File.Exists(path))
            {
                // Synthetic empty allowed state for unknown fixture ids.
                return new Dictionary<string, object>()
                {
                    { "fixture_id", fixtureId ?? string.Empty },
                    {
                        "visible_public", new Dictionary<string, object>()
                        {
                            { "lp0", 8000 },
                            { "lp1", 8000 },
                            { "turn", 1 },
                            { "phase", "Main1" },
                            { "known_public_cards", new List<object>() },
                        }
                    },
                    {
                        "controlled_self", new Dictionary<string, object>()
                        {
                            { "player", 1 },
                            { "hand_count", 0 },
                            { "field", new List<object>() },
                        }
                    },
                };
            }
            string text = File.ReadAllText(path, Encoding.UTF8);
            Dictionary<string, object> root = MiniJSON.Json.Deserialize(text) as Dictionary<string, object>
                ?? new Dictionary<string, object>();
            // Transcript fixtures without visible_public still fingerprint duel_settings only.
            if (!root.ContainsKey("visible_public"))
            {
                Dictionary<string, object> settings = GetDict(root, "duel_settings")
                    ?? new Dictionary<string, object>();
                root["visible_public"] = new Dictionary<string, object>()
                {
                    { "duel_settings_seed", settings.ContainsKey("seed") ? settings["seed"] : 0 },
                    { "first_player", settings.ContainsKey("first_player") ? settings["first_player"] : 0 },
                    { "limited_type", settings.ContainsKey("limited_type") ? settings["limited_type"] : 0 },
                };
                root["controlled_self"] = new Dictionary<string, object>()
                {
                    { "player", 1 },
                    { "hand_count", 0 },
                    { "field", new List<object>() },
                };
            }
            return root;
        }

        static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            if (d == null)
            {
                return null;
            }
            object v;
            if (!d.TryGetValue(key, out v))
            {
                return null;
            }
            return v as Dictionary<string, object>;
        }

        static Dictionary<string, object> CloneWithoutPrivate(Dictionary<string, object> src)
        {
            Dictionary<string, object> dst = new Dictionary<string, object>();
            if (src == null)
            {
                return dst;
            }
            foreach (KeyValuePair<string, object> kv in src)
            {
                if (kv.Key != null && kv.Key.StartsWith("_private", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (kv.Key != null && kv.Key.IndexOf("hidden", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                if (kv.Key != null && kv.Key.IndexOf("opponent_hidden", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                dst[kv.Key] = kv.Value;
            }
            return dst;
        }

        static string Sha256Hex(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>Alias state type some tests may discover.</summary>
    public static class LlmReplayCheckpointState
    {
        public static object Create(
            string fixtureId,
            int opponentHandId,
            string opponentHandName,
            int opponentSetId,
            string opponentSetName,
            int opponentDeckId,
            string opponentDeckName,
            int opponentExtraId,
            string opponentExtraName)
        {
            return LlmCheckpointFingerprintBuilder.BuildFixtureState(
                fixtureId, opponentHandId, opponentHandName, opponentSetId, opponentSetName,
                opponentDeckId, opponentDeckName, opponentExtraId, opponentExtraName);
        }

        public static object FromScriptedFixture(string fixtureId)
        {
            return LlmCheckpointFingerprintBuilder.FromScriptedFixture(fixtureId);
        }

        public static object Build(string fixtureId)
        {
            return FromScriptedFixture(fixtureId);
        }
    }
}

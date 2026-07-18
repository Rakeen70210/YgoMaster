using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    /// <summary>
    /// YDK-backed card catalog. Structured-fact mapping lives in the partial
    /// <c>YdkLlmCardCatalog.StructuredFacts.cs</c> so harnesses can link-compile
    /// the pure mapping without YdkHelper/Utils.
    /// </summary>
    static partial class YdkLlmCardCatalog
    {
        const int DefaultMaxTextLength = 1000;
        static readonly object locker = new object();
        static ILlmCardCatalog instance;

        public static ILlmCardCatalog Instance
        {
            get
            {
                lock (locker)
                {
                    if (instance == null)
                    {
                        instance = new LlmCardCatalog(LoadCards, DefaultMaxTextLength);
                    }
                    return instance;
                }
            }
        }

        static Dictionary<int, LlmCardMetadata> LoadCards()
        {
            List<string> baseDirectories = GetBaseDirectoryCandidates();
            string dataDir = LlmCardDataDirectoryResolver.ResolveClientDataDirectory(baseDirectories);
            if (string.IsNullOrEmpty(dataDir))
            {
                Utils.LogWarning("LLM card catalog failed to find Data/CardData from: " + string.Join(", ", baseDirectories.ToArray()));
                return new Dictionary<int, LlmCardMetadata>();
            }

            Dictionary<int, YdkHelper.GameCardInfo> gameCards;
            try
            {
                gameCards = YdkHelper.LoadCardDataFromGame(dataDir);
            }
            catch (Exception e)
            {
                Utils.LogWarning("LLM card catalog failed to load '" + dataDir + "': " + e.Message);
                return new Dictionary<int, LlmCardMetadata>();
            }

            Dictionary<int, LlmCardMetadata> result = new Dictionary<int, LlmCardMetadata>();
            foreach (KeyValuePair<int, YdkHelper.GameCardInfo> pair in gameCards)
            {
                YdkHelper.GameCardInfo card = pair.Value;
                if (card == null)
                {
                    continue;
                }
                CardFrame frame = card.Frame;
                bool usesRank = frame == CardFrame.Xyz || frame == CardFrame.XyzPend;
                result[pair.Key] = new LlmCardMetadata()
                {
                    CardId = card.Id,
                    Name = card.Name,
                    Text = card.Desc,
                    Kind = card.Kind.ToString(),
                    Attribute = card.Attr.ToString(),
                    Level = card.Level,
                    Atk = card.Atk,
                    Def = card.Def,
                    Scale = card.Scale,
                    Frame = frame.ToString(),
                    SummonFamily = ResolveSummonFamily(frame),
                    IsExtraDeck = card.IsExtraDeck,
                    IsTuner = IsTunerKind(card.Kind),
                    UsesRank = usesRank,
                    // GameCardInfo has no grounded link-rating field distinct from DEF.
                    // Leave null rather than misusing DEF/display text.
                    LinkRating = null,
                };
            }
            return result;
        }

        static List<string> GetBaseDirectoryCandidates()
        {
            List<string> result = new List<string>();
            AddDirectoryCandidate(result, AppDomain.CurrentDomain.BaseDirectory);
            AddDirectoryCandidate(result, Path.GetDirectoryName(typeof(YdkLlmCardCatalog).Assembly.Location));
            AddDirectoryCandidate(result, Environment.CurrentDirectory);
            return result;
        }

        static void AddDirectoryCandidate(List<string> result, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }
            if (!result.Contains(directory))
            {
                result.Add(directory);
            }
        }
    }
}

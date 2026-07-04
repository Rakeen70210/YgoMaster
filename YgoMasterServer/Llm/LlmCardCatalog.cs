using System;
using System.Collections.Generic;
using System.Text;

namespace YgoMaster
{
    class LlmCardCatalog : ILlmCardCatalog
    {
        readonly object locker = new object();
        readonly Func<Dictionary<int, LlmCardMetadata>> loadCards;
        readonly int maxTextLength;
        bool loaded;
        Dictionary<int, LlmCardMetadata> cards;

        public LlmCardCatalog(Func<Dictionary<int, LlmCardMetadata>> loadCards, int maxTextLength)
        {
            if (loadCards == null)
            {
                throw new ArgumentNullException("loadCards");
            }
            this.loadCards = loadCards;
            this.maxTextLength = maxTextLength;
        }

        public LlmCardMetadata GetCard(int cardId)
        {
            if (cardId <= 0)
            {
                return null;
            }

            EnsureLoaded();
            if (cards == null)
            {
                return null;
            }

            LlmCardMetadata card;
            return cards.TryGetValue(cardId, out card) ? card : null;
        }

        void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            lock (locker)
            {
                if (loaded)
                {
                    return;
                }

                try
                {
                    cards = NormalizeCards(loadCards());
                }
                catch
                {
                    cards = null;
                }
                loaded = true;
            }
        }

        Dictionary<int, LlmCardMetadata> NormalizeCards(Dictionary<int, LlmCardMetadata> rawCards)
        {
            if (rawCards == null)
            {
                return null;
            }

            Dictionary<int, LlmCardMetadata> result = new Dictionary<int, LlmCardMetadata>();
            foreach (KeyValuePair<int, LlmCardMetadata> pair in rawCards)
            {
                if (pair.Value == null)
                {
                    continue;
                }
                LlmCardMetadata card = pair.Value;
                result[pair.Key] = new LlmCardMetadata()
                {
                    CardId = card.CardId,
                    Name = NormalizeWhitespace(card.Name),
                    Text = CapText(NormalizeWhitespace(card.Text)),
                    Kind = NormalizeWhitespace(card.Kind),
                    Attribute = NormalizeWhitespace(card.Attribute),
                    Level = card.Level,
                    Atk = card.Atk,
                    Def = card.Def,
                    Scale = card.Scale,
                };
            }
            return result;
        }

        string CapText(string text)
        {
            if (string.IsNullOrEmpty(text) || maxTextLength <= 0 || text.Length <= maxTextLength)
            {
                return text;
            }
            return text.Substring(0, maxTextLength).TrimEnd() + "...";
        }

        static string NormalizeWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            StringBuilder result = new StringBuilder();
            bool pendingSpace = false;
            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    result.Append(' ');
                    pendingSpace = false;
                }
                result.Append(c);
            }
            return result.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGamesServer.Helpers
{
    public static class CardGamesHelpers
    {
        public static void DealCards(Game game)
        {
            var allCards = GetAllCardsInPack();

            if (game.Players.Count * game.NumberOfCardsToDeal > allCards.Count)
            {
                throw new Exception($"Dealing {game.NumberOfCardsToDeal} cards to {game.Players.Count} players results in too many cards being used.");
            }
                
            allCards.Shuffle();

            var currentIndex = 0;
            foreach (var player in game.Players)
            {
                var hand = allCards.Skip(currentIndex).Take(game.NumberOfCardsToDeal).ToList();
                
                game.Hands.Add(new Hand { PlayerUsername = player.Username, Cards = hand });

                currentIndex += game.NumberOfCardsToDeal;
            }

            var remainingCardsDeck = game.Decks.FirstOrDefault(x => x.InitialCardDeck);
            if (remainingCardsDeck != null)
            {
                remainingCardsDeck.Cards = allCards.Skip(currentIndex).ToList();
            }

            var playDeckCard = remainingCardsDeck.Cards.FirstOrDefault(x => !IsActionCard(x));
            var otherCardsDeck = game.Decks.FirstOrDefault(x => !x.InitialCardDeck);
            if (playDeckCard != null && otherCardsDeck != null)
            {
                remainingCardsDeck.Cards.Remove(playDeckCard);
                otherCardsDeck.Cards.Add(playDeckCard);
                game.CurrentRank = playDeckCard.Rank();
                game.CurrentSuit = playDeckCard.Suit();
            }
        }

        public static List<string> GetAllCardsInPack()
        {
            var allCards = new List<string>();
            foreach (var suit in new[] { "C", "D", "H", "S" })
            {
                foreach (var rank in Enumerable.Range(2, 9).Select(x => Convert.ToString(x)).Concat(new[] { "J", "Q", "K", "A" }))
                {
                    allCards.Add($"{rank}{suit}");
                }
            }

            return allCards;
        }

        public static bool IsActionCard(string card)
        {
            return card.StartsWith("2") || card.StartsWith("3") || card.StartsWith("J") || card.StartsWith("Q") || card.StartsWith("K") || card.StartsWith("A");
        }
    }
}

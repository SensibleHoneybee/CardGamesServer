using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGamesServer.Helpers
{
    public static class CardGamesExtensions
    {
        public static void Shuffle<T>(this IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = ThreadSafeRandom.ThisThreadsRandom.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        public static string Rank(this string card)
        {
            return card.Substring(0, card.Length - 1);
        }

        public static string Suit(this string card)
        {
            return card.Substring(card.Length - 1, 1);
        }

        public static string CardDescription(this string card)
        {
            return $"{card.Rank().RankName()} of {card.Suit().SuitName()}";
        }

        public static string RankName(this string rank)
        {
            return rank == "2" ? "two" :
                rank == "3" ? "three" :
                rank == "4" ? "four" :
                rank == "5" ? "five" :
                rank == "6" ? "six" :
                rank == "7" ? "seven" :
                rank == "8" ? "eight" :
                rank == "9" ? "nine" :
                rank == "10" ? "ten" :
                rank == "J" ? "jack" :
                rank == "Q" ? "queen" :
                rank == "K" ? "king" :
                rank == "A" ? "ace" : "<unknown>";
        }

        public static string SuitName(this string suit)
        {
            return suit == "C" ? "clubs" :
                suit == "D" ? "diamonds" :
                suit == "H" ? "hearts" :
                suit == "S" ? "spades" : "<unknown>";
        }

        public static List<VisibleDeck> GetVisibleDecks(this List<Deck> decks)
        {
            return decks.Select(deck => new VisibleDeck
            {
                Id = deck.Id,
                HasCards = deck.Cards.Any(),
                TopCard = deck.IsFaceUp ? deck.Cards.FirstOrDefault() : null,
                IsFaceUp = deck.IsFaceUp,
                CanDragToHand = deck.CanDragToHand,
                CanDropFromHand = deck.CanDropFromHand
            }).ToList();
        }

        public static List<Deck> CreateDecks(this List<DeckDefinition> decks)
        {
            return decks.Select(deck => new Deck
            {
                Id = deck.Id,
                InitialCardDeck = deck.InitialCardDeck,
                IsFaceUp = deck.IsFaceUp,
                CanDragToHand = deck.CanDragToHand,
                CanDropFromHand = deck.CanDropFromHand
            }).ToList();
        }

        public static Player MoveToNextPlayer(this Game game, Player player, bool isEndOfTurn)
        {
            var currentPlayerIndex = game.Players.IndexOf(player);
            var nextPlayerIndex = game.PlayDirection == PlayDirection.Up ? currentPlayerIndex - 1 : currentPlayerIndex + 1;
            if (nextPlayerIndex == game.Players.Count)
            {
                // Last player reached. Go back to the beginning.
                nextPlayerIndex = 0;
            }
            if (nextPlayerIndex == -1)
            {
                // First player reached. Go back to the end.
                nextPlayerIndex = game.Players.Count - 1;
            }

            var nextPlayerInfo = game.Players[nextPlayerIndex];
            game.PlayerToMoveUsername = nextPlayerInfo.Username;
            
            if (isEndOfTurn)
            {
                // When ending turn, always set cardy to false. Has to be reset if neccessary.
                player.Cardy = false;
            }

            return nextPlayerInfo;
        }

        public static PlayerInfo ToPlayerInfo(this Player player, IDictionary<string, Hand> handsByPlayerId)
        {
            var hand = handsByPlayerId.ContainsKey(player.Username) ? handsByPlayerId[player.Username] : default(Hand);

            return new PlayerInfo
            {
                Username = player.Username,
                PlayerName = player.PlayerName,
                IsAdmin = player.IsAdmin,
                Cardy = player.Cardy,
                Winner = player.Winner,
                CardCount = hand?.Cards.Count ?? 0
            };
        }
    }
}

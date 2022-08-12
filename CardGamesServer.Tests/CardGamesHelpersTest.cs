using CardGamesServer.Helpers;
using System.Linq;
using Xunit;

namespace CardGamesServer.Tests
{
    public class CardGamesHelpersTest
    {
        [Fact]
        public void TestDealCards()
        {
            var players = new[]
            {
                new Player { Username = "P1" },
                new Player { Username = "P2" },
                new Player { Username = "P3" }
            }.ToList();

            var game = new Game
            {
                NumberOfCardsToDeal = 4,
                Players = players,
                Decks = new[] { new Deck { Id = "1", InitialCardDeck = false }, new Deck { Id = "2", InitialCardDeck = true } }.ToList()
            };

            CardGamesHelpers.DealCards(game);

            // Make sure there are 4 cards in each hand
            Assert.Equal(4, game.Hands.Single(x => x.PlayerUsername == "P1").Cards.Count);
            Assert.Equal(4, game.Hands.Single(x => x.PlayerUsername == "P2").Cards.Count);
            Assert.Equal(4, game.Hands.Single(x => x.PlayerUsername == "P3").Cards.Count);

            // And 40 in the remaining deck
            Assert.Equal(40, game.Decks.Single(x => x.InitialCardDeck).Cards.Count);

            // And 0 in the empty deck
            Assert.Empty(game.Decks.Single(x => !x.InitialCardDeck).Cards);

            var allCards = game.Hands.SelectMany(x => x.Cards).Concat(game.Decks.SelectMany(x => x.Cards)).ToList();

            Assert.Equal(52, allCards.Count);

            // Make sure no card is repeated
            Assert.Equal(1, allCards.GroupBy(x => x).Select(x => x.Count()).Max());

            // And spot check for a few selected cards
            Assert.Single(allCards.Where(x => x == "7H"));
            Assert.Single(allCards.Where(x => x == "QS"));
            Assert.Single(allCards.Where(x => x == "10D"));
            Assert.Single(allCards.Where(x => x == "KC"));
            Assert.Single(allCards.Where(x => x == "AS"));
            Assert.Single(allCards.Where(x => x == "2H"));
        }

        [Fact]
        public void TestGetAllCardsInPack()
        {
            var allCards = CardGamesHelpers.GetAllCardsInPack();

            // Make sure there are 52 cards
            Assert.Equal(52, allCards.Count);

            // And test for a few selected cards, including bounds
            Assert.Single(allCards.Where(x => x == "3H"));
            Assert.Single(allCards.Where(x => x == "2S"));
            Assert.Single(allCards.Where(x => x == "10D"));
            Assert.Single(allCards.Where(x => x == "JC"));
            Assert.Single(allCards.Where(x => x == "AS"));

            // Make sure it hasn't created silly cards
            Assert.Empty(allCards.Where(x => x == "1H"));
            Assert.Empty(allCards.Where(x => x == "11C"));
        }
    }
}

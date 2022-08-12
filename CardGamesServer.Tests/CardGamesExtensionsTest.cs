using CardGamesServer.Helpers;
using Xunit;

namespace CardGamesServer.Tests
{
    public class CardGamesExtensionsTest
    {
        [Fact]
        public void TestCardDescription()
        {
            var card1 = "3H";
            var card2 = "10D";
            var card3 = "AS";

            Assert.Equal("three of hearts", card1.CardDescription());
            Assert.Equal("ten of diamonds", card2.CardDescription());
            Assert.Equal("ace of spades", card3.CardDescription());
        }
    }
}

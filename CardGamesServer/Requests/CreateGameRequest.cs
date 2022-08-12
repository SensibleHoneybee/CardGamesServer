using System.Collections.Generic;

namespace CardGamesServer.Requests
{
    public class CreateGameRequest
    {
        public CreateGameRequest()
        {
            this.Decks = new List<DeckDefinition>();
        }

        public string GameId { get; set; }

        public string GameName { get; set; }

        public string Username { get; set; }

        public string PlayerName { get; set; }

        public List<DeckDefinition> Decks { get; set; }

        public int NumberOfCardsToDeal { get; set; }
    }
}

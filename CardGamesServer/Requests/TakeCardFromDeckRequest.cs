namespace CardGamesServer.Requests
{
    public class TakeCardFromDeckRequest
    {
        public string GameCode { get; set; }

        public string Username { get; set; }

        public string DeckId { get; set; }
    }
}

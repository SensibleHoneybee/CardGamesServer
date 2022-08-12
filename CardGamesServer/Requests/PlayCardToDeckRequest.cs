namespace CardGamesServer.Requests
{
    public class PlayCardToDeckRequest
    {
        public string GameCode { get; set; }

        public string Username { get; set; }

        public string Card { get; set; }
    }
}

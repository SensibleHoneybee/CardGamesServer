namespace CardGamesServer.Requests
{
    public class ChooseSuitRequest
    {
        public string GameCode { get; set; }

        public string Username { get; set; }

        public string Suit { get; set; }
    }
}

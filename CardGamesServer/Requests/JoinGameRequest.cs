namespace CardGamesServer.Requests
{
    public class JoinGameRequest
    {
        public string GameCode { get; set; }

        public string Username { get; set; }

        public string PlayerName { get; set; }
    }
}

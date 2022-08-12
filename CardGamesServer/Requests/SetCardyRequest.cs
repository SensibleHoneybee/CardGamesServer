namespace CardGamesServer.Requests
{
    public class SetCardyRequest
    {
        public string GameCode { get; set; }

        public string Username { get; set; }

        public bool Cardy { get; set; }
    }
}

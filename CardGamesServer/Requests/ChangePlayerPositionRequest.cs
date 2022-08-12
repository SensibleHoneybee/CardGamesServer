namespace CardGamesServer.Requests
{
    public class ChangePlayerPositionRequest
    {
        public string GameCode { get; set; }

        public string RequestPlayerUsername { get; set; }

        public string PlayerToSetUsername { get; set; }

        public string PlayDirection { get; set; }
    }
}

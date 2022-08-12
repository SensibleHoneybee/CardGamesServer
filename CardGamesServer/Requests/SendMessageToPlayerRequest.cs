namespace CardGamesServer.Requests
{
    public class SendMessageToPlayerRequest
    {
        public string GameCode { get; set; }

        public string RequestPlayerUsername { get; set; }

        public string PlayerToMessageUsername { get; set; }

        public string Message { get; set; }
    }
}

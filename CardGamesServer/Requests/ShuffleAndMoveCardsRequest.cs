namespace CardGamesServer.Requests
{
    public class ShuffleAndMoveCardsRequest
    {
        public string GameCode { get; set; }

        public string Username { get; set; }

        public string FromDeckId { get; set; }

        public string ToDeckId { get; set; }
    }
}

namespace CardGamesServer.Requests
{
    public class UndoLastMoveRequest
    {
        public string GameCode { get; set; }

        public string Username { get; set; }
    }
}

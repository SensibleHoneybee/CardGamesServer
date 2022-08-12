namespace CardGamesServer.Requests
{
    public class RespondToJumpRequest
    {
        public string GameCode { get; set; }

        public string Username { get; set; }

        public bool BlockJump { get; set; }
    }
}

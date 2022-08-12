namespace CardGamesServer
{
    public class Player
    {
        public string Username { get; set; }

        public string PlayerName { get; set; }

        public bool IsAdmin { get; set; }

        public bool Cardy { get; set; }

        public bool Winner { get; set; }

        public string ConnectionId { get; set; }
    }
}

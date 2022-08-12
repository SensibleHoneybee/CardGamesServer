namespace CardGamesServer
{
    public class PlayerInfo
    {
        public string Username { get; set; }
        
        public string PlayerName { get; set; }

        public bool IsAdmin { get; set; }

        public bool Cardy { get; set; }

        public bool Winner { get; set; }

        public int CardCount { get; set; }
    }
}

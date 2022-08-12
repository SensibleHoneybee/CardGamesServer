namespace CardGamesServer
{
    public class Move
    {
        public string PlayerUsername { get; set; }

        public string Card { get; set; }
        
        public string DeckId { get; set; }

        public string MoveDirection { get; set; }
    }
}

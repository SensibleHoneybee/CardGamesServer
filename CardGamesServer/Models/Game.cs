using System.Collections.Generic;

namespace CardGamesServer
{
    public class Game
    {
        public Game()
        {
            this.Players = new List<Player>();
            this.Decks = new List<Deck>();
            this.Hands = new List<Hand>();
            this.Moves = new List<Move>();
            this.UndoneMoves = new List<Move>();
            this.Messages = new List<Message>();
        }

        public string Id { get; set; }

        public string GameName { get; set; }

        public string GameCode { get; set; }

        public string GameState { get; set; }

        public int NumberOfCardsToDeal { get; set; }

        public List<Player> Players { get; set; }

        public List<Deck> Decks { get; set; }

        public List<Hand> Hands { get; set; }

        public List<Move> Moves { get; set; }

        public List<Move> UndoneMoves { get; set; }

        public List<Message> Messages { get; set; }

        public string PlayerToMoveUsername { get; set; }

        public string PlayDirection { get; set; }

        public string CurrentRank { get; set; }

        public string CurrentSuit { get; set; }
         
        public string MoveState { get; set; }

        public int TwoOrThreeMoveCardsToBePicked { get; set; }

        public string WinnerName { get; set; }
    }
}

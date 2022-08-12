using Newtonsoft.Json;
using System.Collections.Generic;

namespace CardGamesServer.Responses
{
    public class FullGameResponse : IResponse
    {
        public FullGameResponse()
        {
            this.AllPlayers = new List<PlayerInfo>();
            this.Hand = new List<string>();
            this.Decks = new List<VisibleDeck>();
            this.Messages = new List<string>();
        }

        public string GameId { get; set; }

        public string GameCode { get; set; }

        public string GameName { get; set; }

        public string GameState { get; set; }

        public PlayerInfo PlayerInfo { get; set; }

        public List<PlayerInfo> AllPlayers { get; set; }

        public List<string> Hand { get; set; }

        public List<VisibleDeck> Decks { get; set; }

        public bool MoveCanBeUndone { get; set; }

        public List<string> Messages { get; set; }

        public string PlayerToMoveUsername { get; set; }

        public string PlayDirection { get; set; }

        public string MoveState { get; set; }

        public string WinnerName { get; set; }

        [JsonIgnore]
        public string GetSendMessageResponseType => SendMessageResponseType.FullGame;
    }
}

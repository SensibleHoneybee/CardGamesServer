using Newtonsoft.Json;
using System.Collections.Generic;

namespace CardGamesServer.Responses
{
    public class PlayerJoinedGameResponse : IResponse
    {
        public PlayerJoinedGameResponse()
        {
            this.AllPlayers = new List<PlayerInfo>();
        }

        public string GameCode { get; set; }

        public List<PlayerInfo> AllPlayers { get; set; }

        [JsonIgnore]
        public string GetSendMessageResponseType => SendMessageResponseType.PlayerJoinedGame;
    }
}

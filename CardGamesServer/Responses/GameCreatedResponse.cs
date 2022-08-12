using Newtonsoft.Json;

namespace CardGamesServer.Responses
{
    public class GameCreatedResponse : IResponse
    {
        public string GameId { get; set; }

        public string GameCode { get; set; }

        public string GameName { get; set; }

        [JsonIgnore]
        public string GetSendMessageResponseType => SendMessageResponseType.GameCreated;
    }
}

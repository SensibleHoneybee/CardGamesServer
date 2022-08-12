using Newtonsoft.Json;

namespace CardGamesServer.Responses
{
    public class MessageToPlayerResponse : IResponse
    {
        public string Message { get; set; }

        [JsonIgnore]
        public string GetSendMessageResponseType => SendMessageResponseType.MessageToPlayer;
    }
}

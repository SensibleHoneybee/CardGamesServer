using Newtonsoft.Json;

namespace CardGamesServer.Responses
{
    public class ErrorResponse : IResponse
    {
        public string Message { get; set; }

        [JsonIgnore]
        public string GetSendMessageResponseType => SendMessageResponseType.Error;
    }
}

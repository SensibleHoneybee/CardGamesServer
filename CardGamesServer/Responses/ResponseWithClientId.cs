namespace CardGamesServer.Responses
{
    public class ResponseWithClientId
    {
        public ResponseWithClientId(IResponse response, string clientId)
        {
            this.Response = response;
            this.ClientId = clientId;
        }

        public IResponse Response { get; }

        public string ClientId { get; }
    }
}

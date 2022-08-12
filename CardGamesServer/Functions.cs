using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Amazon;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Runtime;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;

using Newtonsoft.Json;

using CardGamesServer.Requests;
using CardGamesServer.Responses;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.LambdaJsonSerializer))]

namespace CardGamesServer
{
    public class Functions
    {
        public const string ConnectionIdField = "connectionId";

        // This const is the name of the environment variable that the serverless.template will use to set
        // the name of the DynamoDB table used to store tokens.
        const string TABLENAME_ENVIRONMENT_VARIABLE_LOOKUP = "GameTable";

        /// <summary>
        /// DynamoDB table used to store the open connection ids. More advanced use cases could store logged on user map to their connection id to implement direct message chatting.
        /// </summary>
        string ConnectionMappingTable { get; }

        /// <summary>
        /// DynamoDB service client used to store and retieve connection information from the ConnectionMappingTable
        /// </summary>
        IAmazonDynamoDB DDBClient { get; }

        IDynamoDBContext GameTableDDBContext { get; }

        /// <summary>
        /// Factory func to create the AmazonApiGatewayManagementApiClient. This is needed to created per endpoint of the a connection. It is a factory to make it easy for tests
        /// to moq the creation.
        /// </summary>
        Func<string, IAmazonApiGatewayManagementApi> ApiGatewayManagementApiClientFactory { get; }

        ICardGamesEngine CardGamesEngine { get; }


        /// <summary>
        /// Default constructor that Lambda will invoke.
        /// </summary>
        public Functions()
        {
            this.DDBClient = new AmazonDynamoDBClient();

            // Grab the name of the DynamoDB from the environment variable setup in the CloudFormation template serverless.template
            this.ConnectionMappingTable = System.Environment.GetEnvironmentVariable("TABLE_NAME");

            this.ApiGatewayManagementApiClientFactory = (Func<string, AmazonApiGatewayManagementApiClient>)((endpoint) =>
            {
                return new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig
                {
                    ServiceURL = endpoint
                });
            });

            // Check to see if a table name was passed in through environment variables and if so 
            // add the table mapping.
            var gameTableName = System.Environment.GetEnvironmentVariable(TABLENAME_ENVIRONMENT_VARIABLE_LOOKUP);
            if (!string.IsNullOrEmpty(gameTableName))
            {
                AWSConfigsDynamoDB.Context.TypeMappings[typeof(GameStorage)] = new Amazon.Util.TypeMapping(typeof(GameStorage), gameTableName);
            }
            var config = new DynamoDBContextConfig { Conversion = DynamoDBEntryConversion.V2 };
            this.GameTableDDBContext = new DynamoDBContext(this.DDBClient, config);

            this.CardGamesEngine = new CardGamesEngine(this.GameTableDDBContext);
        }

        /// <summary>
        /// Constructor used for testing allow tests to pass in moq versions of the service clients.
        /// </summary>
        /// <param name="ddbClient"></param>
        /// <param name="apiGatewayManagementApiClientFactory"></param>
        /// <param name="connectionMappingTable"></param>
        public Functions(IAmazonDynamoDB ddbClient, Func<string, IAmazonApiGatewayManagementApi> apiGatewayManagementApiClientFactory, ICardGamesEngine cardGamesEngine, string connectionMappingTable, string gameTableName)
        {
            this.DDBClient = ddbClient;
            this.ApiGatewayManagementApiClientFactory = apiGatewayManagementApiClientFactory;
            this.ConnectionMappingTable = connectionMappingTable;

            if (!string.IsNullOrEmpty(gameTableName))
            {
                AWSConfigsDynamoDB.Context.TypeMappings[typeof(GameStorage)] = new Amazon.Util.TypeMapping(typeof(GameStorage), gameTableName);
            }
            var config = new DynamoDBContextConfig { Conversion = DynamoDBEntryConversion.V2 };
            this.GameTableDDBContext = new DynamoDBContext(this.DDBClient, config);
        }

        public async Task<APIGatewayProxyResponse> OnConnectHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                var connectionId = request.RequestContext.ConnectionId;
                context.Logger.LogLine($"ConnectionId: {connectionId}");

                var ddbRequest = new PutItemRequest
                {
                    TableName = ConnectionMappingTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        {ConnectionIdField, new AttributeValue{ S = connectionId }}
                    }
                };

                await DDBClient.PutItemAsync(ddbRequest);

                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = "Connected."
                };
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error connecting: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = $"Failed to connect: {e.Message}"
                };
            }
        }

        public async Task<APIGatewayProxyResponse> SendMessageHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                // Construct the API Gateway endpoint that incoming message will be broadcasted to.
                var domainName = request.RequestContext.DomainName;
                var stage = request.RequestContext.Stage;
                var endpoint = $"https://{domainName}/{stage}";
                context.Logger.LogLine($"API Gateway management endpoint: {endpoint}");

                var connectionId = request.RequestContext.ConnectionId;
                context.Logger.LogLine($"ConnectionId: {connectionId}");

                try {
                    JsonDocument message = JsonDocument.Parse(request.Body);

                    // Grab the data from the JSON body which is the message to broadcasted.
                    JsonElement dataProperty;
                    if (!message.RootElement.TryGetProperty("data", out dataProperty))
                    {
                        context.Logger.LogLine("Failed to find data element in JSON document");
                        return new APIGatewayProxyResponse
                        {
                            StatusCode = (int)HttpStatusCode.BadRequest
                        };
                    }
                    context.Logger.LogLine($"JSON Data: {dataProperty.GetString()}");

                    var sendMessageRequest = JsonConvert.DeserializeObject<SendMessageRequest>(dataProperty.GetString());
                    context.Logger.LogLine($"Send Message Request. Type: {sendMessageRequest.SendMessageRequestType}. Content: {sendMessageRequest.Content}");

                    List<ResponseWithClientId> responsesWithClientIds;
                    switch (sendMessageRequest.SendMessageRequestType)
                    {
                        case SendMessageRequestType.CreateGame:
                            var createGameRequest = JsonConvert.DeserializeObject<CreateGameRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.CreateGameAsync(createGameRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.JoinGame:
                            var joinedGameRequest = JsonConvert.DeserializeObject<JoinGameRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.JoinGameAsync(joinedGameRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.RejoinGame:
                            var rejoinedGameRequest = JsonConvert.DeserializeObject<RejoinGameRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.RejoinGameAsync(rejoinedGameRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.StartGame:
                            var startGameRequest = JsonConvert.DeserializeObject<StartGameRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.StartGameAsync(startGameRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.PlayCardToDeck:
                            var playCardToDeckRequest = JsonConvert.DeserializeObject<PlayCardToDeckRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.PlayCardToDeckAsync(playCardToDeckRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.TakeCardFromDeck:
                            var takeCardFromDeckRequest = JsonConvert.DeserializeObject<TakeCardFromDeckRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.TakeCardFromDeckAsync(takeCardFromDeckRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.ShuffleAndMoveCards:
                            var shuffleAndMoveCardsRequest = JsonConvert.DeserializeObject<ShuffleAndMoveCardsRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.ShuffleAndMoveCardsAsync(shuffleAndMoveCardsRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.UndoLastMove:
                            var undoLastMoveRequest = JsonConvert.DeserializeObject<UndoLastMoveRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.UndoLastMoveAsync(undoLastMoveRequest, connectionId, context.Logger);
                            break;
                        ////case SendMessageRequestType.EndTurn:
                        ////    var endTurnRequest = JsonConvert.DeserializeObject<EndTurnRequest>(sendMessageRequest.Content);
                        ////    responsesWithClientIds = await this.CardGamesEngine.EndTurnAsync(endTurnRequest, connectionId, context.Logger);
                        ////    break;
                        case SendMessageRequestType.SetCardy:
                            var setCardyRequest = JsonConvert.DeserializeObject<SetCardyRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.SetCardyAsync(setCardyRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.ChooseSuit:
                            var chooseSuitRequest = JsonConvert.DeserializeObject<ChooseSuitRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.ChooseSuitAsync(chooseSuitRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.RespondToJump:
                            var respondToJumpRequest = JsonConvert.DeserializeObject<RespondToJumpRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.RespondToJumpAsync(respondToJumpRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.SetPlayerTurn:
                            var setPlayerTurnRequest = JsonConvert.DeserializeObject<SetPlayerTurnRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.SetPlayerTurnAsync(setPlayerTurnRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.ChangePlayerPosition:
                            var changePlayerPositionRequest = JsonConvert.DeserializeObject<ChangePlayerPositionRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.ChangePlayerPositionAsync(changePlayerPositionRequest, connectionId, context.Logger);
                            break;
                        case SendMessageRequestType.SendMessageToPlayer:
                            var sendMessageToPlayerRequest = JsonConvert.DeserializeObject<SendMessageToPlayerRequest>(sendMessageRequest.Content);
                            responsesWithClientIds = await this.CardGamesEngine.SendMessageToPlayerAsync(sendMessageToPlayerRequest, connectionId, context.Logger);
                            break;
                        default:
                            throw new Exception($"Unknown message request type: {sendMessageRequest.SendMessageRequestType}");
                    }

                    context.Logger.LogLine($"Game responses: {responsesWithClientIds.Count}");

                    // List all of the current connections. In a more advanced use case the table could be used to grab a group of connection ids for a chat group.
                    var scanRequest = new ScanRequest
                    {
                        TableName = ConnectionMappingTable,
                        ProjectionExpression = ConnectionIdField
                    };

                    var scanResponse = await DDBClient.ScanAsync(scanRequest);

                    // Construct the IAmazonApiGatewayManagementApi which will be used to send the message to.
                    var apiClient = ApiGatewayManagementApiClientFactory(endpoint);

                    var responsesByClientId = responsesWithClientIds.GroupBy(x => x.ClientId).ToDictionary(x => x.Key, x => x.Select(x => x.Response).ToList());

                    // Loop through all of the connections and broadcast the message out to the connections.
                    var count = 0;
                    foreach (var item in scanResponse.Items)
                    {
                        var connectedClientConnectionId = item[ConnectionIdField].S;

                        List<IResponse> responses;
                        if (!responsesByClientId.TryGetValue(connectedClientConnectionId, out responses))
                        {
                            // This connection isn't amongst those to receive a message response.
                            continue;
                        }

                        foreach (var response in responses)
                        {
                            var encodedResponse = new { SendMessageResponseType = response.GetSendMessageResponseType, Content = JsonConvert.SerializeObject(response) };
                            var responseJson = JsonConvert.SerializeObject(encodedResponse);
                            context.Logger.LogLine($"Sending JSON response {responseJson} to client {connectedClientConnectionId}.");

                            await SendMessageToClient(connectedClientConnectionId, responseJson, apiClient, context);

                            count++;
                        }
                    }

                    return new APIGatewayProxyResponse
                    {
                        StatusCode = (int)HttpStatusCode.OK,
                        Body = "Data sent to " + count + " connection" + (count == 1 ? "" : "s")
                    };
                }
                catch (Exception e1)
                {
                    var apiClient = ApiGatewayManagementApiClientFactory(endpoint);
                    var errorResponse = new ErrorResponse { Message = e1.Message };
                    var encodedResponse = new { SendMessageResponseType = errorResponse.GetSendMessageResponseType, Content = JsonConvert.SerializeObject(errorResponse) };
                    var responseJson = JsonConvert.SerializeObject(encodedResponse);
                    await SendMessageToClient(connectionId, responseJson, apiClient, context);
                    throw;
                }
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error in send message handler: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Body = $"Failed to send message: {e.Message}"
                };
            }
        }

        public async Task<APIGatewayProxyResponse> OnDisconnectHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                var connectionId = request.RequestContext.ConnectionId;
                context.Logger.LogLine($"ConnectionId: {connectionId}");

                var ddbRequest = new DeleteItemRequest
                {
                    TableName = ConnectionMappingTable,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        {ConnectionIdField, new AttributeValue {S = connectionId}}
                    }
                };

                await DDBClient.DeleteItemAsync(ddbRequest);

                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = "Disconnected."
                };
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error disconnecting: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = $"Failed to disconnect: {e.Message}"
                };
            }
        }

        private async Task SendMessageToClient(string connectionId, string responseJson, IAmazonApiGatewayManagementApi apiClient, ILambdaContext context)
        {
            var stream = new MemoryStream(UTF8Encoding.UTF8.GetBytes(responseJson));

            var postConnectionRequest = new PostToConnectionRequest
            {
                ConnectionId = connectionId,
                Data = stream
            };

            try
            {
                context.Logger.LogLine($"Post to connection: {postConnectionRequest.ConnectionId}");
                stream.Position = 0;
                await apiClient.PostToConnectionAsync(postConnectionRequest);
            }
            catch (AmazonServiceException e)
            {
                // API Gateway returns a status of 410 GONE then the connection is no
                // longer available. If this happens, delete the identifier
                // from our DynamoDB table.
                if (e.StatusCode == HttpStatusCode.Gone)
                {
                    var ddbDeleteRequest = new DeleteItemRequest
                    {
                        TableName = ConnectionMappingTable,
                        Key = new Dictionary<string, AttributeValue>
                                {
                                    {ConnectionIdField, new AttributeValue {S = postConnectionRequest.ConnectionId}}
                                }
                    };

                    context.Logger.LogLine($"Deleting gone connection: {postConnectionRequest.ConnectionId}");
                    await DDBClient.DeleteItemAsync(ddbDeleteRequest);
                }
                else
                {
                    context.Logger.LogLine($"Error posting message to {postConnectionRequest.ConnectionId}: {e.Message}");
                    context.Logger.LogLine(e.StackTrace);
                }
            }
        }
    }
}
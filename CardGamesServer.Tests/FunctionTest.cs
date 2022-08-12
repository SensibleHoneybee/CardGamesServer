using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.APIGatewayEvents;

using Moq;

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Newtonsoft.Json;
using CardGamesServer.Requests;

namespace CardGamesServer.Tests
{
    public class FunctionTest
    {
        public FunctionTest()
        {
        }

        [Fact]
        public async Task TestConnect()
        {
            Mock<IAmazonDynamoDB> _mockDDBClient = new Mock<IAmazonDynamoDB>();
            Mock<IAmazonApiGatewayManagementApi> _mockApiGatewayClient = new Mock<IAmazonApiGatewayManagementApi>();
            string tableName = "mocktable";
            string gameTableName = "gametable";
            string connectionId = "test-id";

            _mockDDBClient.Setup(client => client.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PutItemRequest, CancellationToken>((request, token) =>
                {
                    Assert.Equal(tableName, request.TableName);
                    Assert.Equal(connectionId, request.Item[Functions.ConnectionIdField].S);
                });

            Mock<ICardGamesEngine> _mockCardGamesEngine = new Mock<ICardGamesEngine>();

            var functions = new Functions(_mockDDBClient.Object, (endpoint) => _mockApiGatewayClient.Object, _mockCardGamesEngine.Object, tableName, gameTableName);

            var lambdaContext = new TestLambdaContext();

            var request = new APIGatewayProxyRequest
            {
                RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
                {
                    ConnectionId = connectionId
                }
            };
            var response = await functions.OnConnectHandler(request, lambdaContext);
            Assert.Equal(200, response.StatusCode);
        }


        [Fact]
        public async Task TestDisconnect()
        {
            Mock<IAmazonDynamoDB> _mockDDBClient = new Mock<IAmazonDynamoDB>();
            Mock<IAmazonApiGatewayManagementApi> _mockApiGatewayClient = new Mock<IAmazonApiGatewayManagementApi>();
            string tableName = "mocktable";
            string gameTableName = "gametable";
            string connectionId = "test-id";

            _mockDDBClient.Setup(client => client.DeleteItemAsync(It.IsAny<DeleteItemRequest>(), It.IsAny<CancellationToken>()))
                .Callback<DeleteItemRequest, CancellationToken>((request, token) =>
                {
                    Assert.Equal(tableName, request.TableName);
                    Assert.Equal(connectionId, request.Key[Functions.ConnectionIdField].S);
                });

            Mock<ICardGamesEngine> _mockCardGamesEngine = new Mock<ICardGamesEngine>();

            var functions = new Functions(_mockDDBClient.Object, (endpoint) => _mockApiGatewayClient.Object, _mockCardGamesEngine.Object, tableName, gameTableName);

            var lambdaContext = new TestLambdaContext();

            var request = new APIGatewayProxyRequest
            {
                RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
                {
                    ConnectionId = connectionId
                }
            };
            var response = await functions.OnDisconnectHandler(request, lambdaContext);
            Assert.Equal(200, response.StatusCode);
        }

        [Fact]
        public async Task TestSendMessage()
        {
            Mock<IAmazonDynamoDB> _mockDDBClient = new Mock<IAmazonDynamoDB>();
            Mock<IAmazonApiGatewayManagementApi> _mockApiGatewayClient = new Mock<IAmazonApiGatewayManagementApi>();
            string tableName = "mocktable";
            string gameTableName = "gametable";
            string connectionId = "test-id";
            string message = "hello world";

            _mockDDBClient.Setup(client => client.ScanAsync(It.IsAny<ScanRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ScanRequest, CancellationToken>((request, token) =>
                {
                    Assert.Equal(tableName, request.TableName);
                    Assert.Equal(Functions.ConnectionIdField, request.ProjectionExpression);
                })
                .Returns((ScanRequest r, CancellationToken token) =>
                {
                    return Task.FromResult(new ScanResponse
                    {
                        Items = new List<Dictionary<string, AttributeValue>>
                        {
                            { new Dictionary<string, AttributeValue>{ {Functions.ConnectionIdField, new AttributeValue { S = connectionId } } } }
                        }
                    });
                });

            Func<string, IAmazonApiGatewayManagementApi> apiGatewayFactory = ((endpoint) =>
            {
                Assert.Equal("https://test-domain/test-stage", endpoint);
                return _mockApiGatewayClient.Object;
            });

            _mockApiGatewayClient.Setup(client => client.PostToConnectionAsync(It.IsAny<PostToConnectionRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PostToConnectionRequest, CancellationToken>((request, token) =>
                {
                    var actualMessage = new StreamReader(request.Data).ReadToEnd();
                    Assert.Equal(message, actualMessage);
                });

            Mock<ICardGamesEngine> _mockCardGamesEngine = new Mock<ICardGamesEngine>();

            var functions = new Functions(_mockDDBClient.Object, apiGatewayFactory, _mockCardGamesEngine.Object, tableName, gameTableName);

            var lambdaContext = new TestLambdaContext();

            var request = new APIGatewayProxyRequest
            {
                RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
                {
                    ConnectionId = connectionId,
                    DomainName = "test-domain",
                    Stage = "test-stage"
                },
                Body = "{\"message\":\"sendmessage\", \"data\":\"" + message + "\"}"
            };
            var response = await functions.SendMessageHandler(request, lambdaContext);
            Assert.Equal(200, response.StatusCode);
        }

        [Fact]
        public void DoStuff()
        {
            var createGameRequest = new CreateGameRequest { GameId = Guid.NewGuid().ToString(), GameName = "My Game", Username = "steve", PlayerName = "Stephen Holt" };
            var sendMessageRequest = new SendMessageRequest { SendMessageRequestType = "CreateGame", Content = JsonConvert.SerializeObject(createGameRequest).ToString() };

            var result1a = JsonConvert.SerializeObject(sendMessageRequest).ToString();
            var result1b = JsonConvert.SerializeObject(new { message = "sendmessage", data = result1a }).ToString();

            System.IO.File.WriteAllLines(@"C:\Temp\JsonResult.txt", new[] { result1b });

            var joinGameRequest = new JoinGameRequest { GameCode = "EFMFVK", Username = "tom", PlayerName = "Tom Holt" };
            var sendMessageRequest2 = new SendMessageRequest { SendMessageRequestType = "JoinGame", Content = JsonConvert.SerializeObject(joinGameRequest).ToString() };
            var result2a = JsonConvert.SerializeObject(sendMessageRequest2).ToString();
            var result2b = JsonConvert.SerializeObject(new { message = "sendmessage", data = result2a }).ToString();

            System.IO.File.WriteAllLines(@"C:\Temp\JsonResult2.txt", new[] { result2b });
        }

        public interface IFoo
        {
            string Prop1 { get; set; }

            string Prop2 { get; set; }
        }

        public class Foo : IFoo
        {
            public string Prop1 { get; set; }

            public string Prop2 { get; set; }

            public string OtherProp { get; set; }
        }

        [Fact]
        public void DoStuff2()
        {
            IFoo foo1 = new Foo { Prop1 = "P1", Prop2 = "P2", OtherProp = "OP" };

            var result1a = JsonConvert.SerializeObject(foo1).ToString();

        }
    }
}

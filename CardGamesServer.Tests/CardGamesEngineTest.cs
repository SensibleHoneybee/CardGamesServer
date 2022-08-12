// NOTE - this unit test uses wrapped versions of the DB context, because the AsyncSearch method cannot be
// mocked out. This requires changes to the production code, which I don't want to make, so I have disabled it for now.

////using Amazon.DynamoDBv2.DataModel;
////using Amazon.Lambda.Core;
////using CardGamesServer.Responses;
////using Moq;
////using Newtonsoft.Json;
////using System.Linq;
////using System.Threading.Tasks;
////using Xunit;
////using System.Collections.ObjectModel;
////using System.Collections.Generic;
////using System.Threading;
////using CardGamesServer.Requests;

////namespace CardGamesServer.Tests
////{
////    public class CardGamesEngineTest
////    {
////        [Fact]
////        public async Task TestJoinGame()
////        {
////            // The main user is not in the game and trying to join. Two others are already in the game.
////            var gameCode = "AAAAAA";
////            var username = "the-user";
////            var anotherUsername1 = "another-user-1";
////            var anotherUsername2 = "another-user-2";
////            var userConnection = "1111";
////            var anotherConnection1 = "2222";
////            var anotherConnection2 = "3333";
////            var playerName = "The Player";

////            // Setup game details
////            var players = new[]
////            {
////                new Player { PlayerInfo = new PlayerInfo { Username = anotherUsername1, PlayerName = "Another1", IsAdmin = true }, ConnectionId = anotherConnection1 },
////                new Player { PlayerInfo = new PlayerInfo { Username = anotherUsername2, PlayerName = "Another2", IsAdmin = false }, ConnectionId = anotherConnection2 }
////            }.ToList();
////            var game = new Game { Id = "1", GameCode = gameCode, GameName = "The Game", GameState = GameState.Created, Players = players };
////            var gameStorage = new GameStorage { Id = "1", Version = 1, GameCode = gameCode, Content = JsonConvert.SerializeObject(game) };

////            // Setup mocks
////            var asyncSearch = new Mock<IAsyncSearch<GameStorage>>();
////            var resultList = (IReadOnlyList<GameStorage>)new ReadOnlyCollection<GameStorage>(new[] { gameStorage }.ToList());
////            asyncSearch.Setup(x => x.GetRemainingAsync()).Returns(Task.FromResult(resultList));

////            var gameTableDDBContext = new Mock<IDynamoDBContext>();
////            gameTableDDBContext.Setup(x => x.LoadAsync<GameStorage>(It.IsAny<object>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(gameStorage));

////            var wrappedGameTableDDBContext = new Mock<IWrappedDbContext>();
////            wrappedGameTableDDBContext.Setup(x => x.QueryAsync<GameStorage>(It.IsAny<object>(), It.IsAny<DynamoDBOperationConfig>())).Returns(asyncSearch.Object);
////            wrappedGameTableDDBContext.Setup(x => x.Context).Returns(gameTableDDBContext.Object);

////            var logger = new Mock<ILambdaLogger>();

////            // Setup inputs
////            var joinGameRequest = new JoinGameRequest { GameCode = gameCode, Username = username, PlayerName = playerName };

////            var cardGamesEngine = new CardGamesEngine(wrappedGameTableDDBContext.Object);

////            var result = await cardGamesEngine.JoinGameAsync(joinGameRequest, userConnection, logger.Object);

////            Assert.Equal(3, result.Count);

////            var fullGameResults = result.Where(x => x.Response.GetSendMessageResponseType == SendMessageResponseType.FullGame).ToList();
////            Assert.Single(fullGameResults);
////            Assert.Equal(userConnection, fullGameResults.Single().ClientId);

////            var playerJoinedGameResults = result.Where(x => x.Response.GetSendMessageResponseType == SendMessageResponseType.PlayerJoinedGame).ToList();
////            Assert.Equal(2, playerJoinedGameResults.Count);

////            var playerJoinedGameResults1 = playerJoinedGameResults.Where(x => x.ClientId == anotherConnection1).ToList();
////            Assert.Single(playerJoinedGameResults1);

////            var playerJoinedGameResults2 = playerJoinedGameResults.Where(x => x.ClientId == anotherConnection2).ToList();
////            Assert.Single(playerJoinedGameResults2);
////        }
////    }
////}

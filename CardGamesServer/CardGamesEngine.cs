using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.Core;
using CardGamesServer.Helpers;
using CardGamesServer.Requests;
using CardGamesServer.Responses;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CardGamesServer
{
    public class CardGamesEngine : ICardGamesEngine
    {
        public CardGamesEngine(IDynamoDBContext gameTableDDBContext)
        {
            this.GameTableDDBContext = gameTableDDBContext;
        }

        IDynamoDBContext GameTableDDBContext { get; }

        public async Task<List<ResponseWithClientId>> CreateGameAsync(CreateGameRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameId))
            {
                throw new Exception("CreateGameRequest.GameId must be supplied");
            }

            if (string.IsNullOrEmpty(request.GameName))
            {
                throw new Exception("CreateGameRequest.GameName must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("CreateGameRequest.Username must be supplied");
            }

            if (string.IsNullOrEmpty(request.PlayerName))
            {
                throw new Exception("CreateGameRequest.PlayerName must be supplied");
            }

            // Get a unique ID and code for this game
            var secondsSinceY2K = (long)DateTime.UtcNow.Subtract(new DateTime(2000, 1, 1)).TotalSeconds;
            var gameCode = CreateGameCode(secondsSinceY2K);

            // The first player is automatically an admin
            var player = new Player { Username = request.Username, PlayerName = request.PlayerName, IsAdmin = true, ConnectionId = connectionId };

            var game = new Game {
                Id = request.GameId,
                GameName = request.GameName,
                GameCode = gameCode,
                GameState = GameState.Created,
                Players = new[] { player }.ToList(),
                Decks = request.Decks.CreateDecks(),
                NumberOfCardsToDeal = request.NumberOfCardsToDeal,
                MoveState = MoveState.Normal
            };

            logger.LogLine($"Created game bits. ID: {game.Id}. Code: {gameCode}");

            // And create wrapper to store it in DynamoDB
            var gameStorage = new GameStorage
            {
                Id = request.GameId,
                GameCode = gameCode,
                CreatedTimestamp = DateTime.UtcNow,
                Content = JsonConvert.SerializeObject(game)
            };

            logger.LogLine($"Saving game with id {gameStorage.Id}");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            var response = new GameCreatedResponse { GameId = request.GameId, GameCode = gameCode, GameName = request.GameName };

            // Response should be sent only to the caller
            return new[] { new ResponseWithClientId(response, connectionId) }.ToList();
        }

        public async Task<List<ResponseWithClientId>> JoinGameAsync(JoinGameRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("JoinGameRequest.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("JoinGameRequest.Username must be supplied");
            }

            if (string.IsNullOrEmpty(request.PlayerName))
            {
                throw new Exception("JoinGameRequest.PlayerName must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState == GameState.Started)
            {
                throw new Exception("The game you are trying to join has already started. Please click \"Rejoin Game\" if you're an existing player.");
            }
            if (game.GameState == GameState.Completed)
            {
                throw new Exception("The game you are trying to join has already finished.");
            }
            if (game.Players.Any(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase)))
            {
                throw new Exception("There is already a player with that user-name in the game. Please click \"Rejoin Game\" if you're an existing player.");
            }

            var player = new Player { Username = request.Username, PlayerName = request.PlayerName, IsAdmin = false, ConnectionId = connectionId };
            game.Players.Add(player);

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}. Added player {request.Username}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            var results = new List<ResponseWithClientId>();

            var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
            var allPlayerInfos = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList();
            var thisPlayerInfo = allPlayerInfos.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase));
            var otherPlayerInfos = allPlayerInfos.Where(x => !string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase)).ToList();

            // Send a full game info to the connecting player, and a new player response to all other clients
            var myHand = handsByPlayerId.ContainsKey(request.Username) ? handsByPlayerId[request.Username] : default(Hand);
            var fullGameResponse = new FullGameResponse
            {
                GameId = game.Id,
                GameCode = game.GameCode,
                GameName = game.GameName,
                GameState = game.GameState,
                PlayerInfo = thisPlayerInfo,
                AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                Hand = myHand?.Cards,
                Decks = game.Decks.GetVisibleDecks(),
                MoveCanBeUndone = game.Moves.Any(),
                Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(request.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                PlayerToMoveUsername = game.PlayerToMoveUsername,
                PlayDirection = game.PlayDirection,
                MoveState = game.MoveState,
                WinnerName = game.WinnerName
            };
            results.Add(new ResponseWithClientId(fullGameResponse, connectionId));

            // Send the joined game response to all other listeners. Each should see a list of players which excludes themselves.
            foreach (var particularPlayer in game.Players)
            {
                if (string.Equals(particularPlayer.Username, request.Username, StringComparison.OrdinalIgnoreCase))
                {
                    // The caller deoesn't get this message
                    continue;
                }

                var playerJoinedGameResponse = new PlayerJoinedGameResponse
                {
                    GameCode = game.GameCode,
                    AllPlayers = allPlayerInfos.ToList()
                };

                results.Add(new ResponseWithClientId(playerJoinedGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        public async Task<List<ResponseWithClientId>> RejoinGameAsync(RejoinGameRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("RejoinGameRequest.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("RejoinGameRequest.Username must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState == GameState.Completed)
            {
                throw new Exception("The game you are trying to rejoin has already finished.");
            }

            var player = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase));
            if (player == null)
            {
                throw new Exception("There is no player with that user-name in the game. Please click \"Join Game\" if you wish to join as a new player.");
            }

            // Update the connection data in the database
            player.ConnectionId = connectionId;

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}. Re-added player {request.Username}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to the connecting player
            var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
            var myHand = handsByPlayerId.ContainsKey(request.Username) ? handsByPlayerId[request.Username] : default(Hand);
            var fullGameResponse = new FullGameResponse
            {
                GameId = game.Id,
                GameCode = game.GameCode,
                GameName = game.GameName,
                GameState = game.GameState,
                PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                Hand = myHand?.Cards ?? new List<string>(),
                Decks = game.Decks.GetVisibleDecks(),
                MoveCanBeUndone = game.Moves.Any(),
                Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(request.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                PlayerToMoveUsername = game.PlayerToMoveUsername,
                PlayDirection = game.PlayDirection,
                MoveState = game.MoveState,
                WinnerName = game.WinnerName
            };

            return new[] { new ResponseWithClientId(fullGameResponse, connectionId) }.ToList();
        }

        public async Task<List<ResponseWithClientId>> StartGameAsync(StartGameRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("StartGameRequest.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("StartGameRequest.Username must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState != GameState.Created)
            {
                throw new Exception($"The game you are trying to start is not in the correct state. State: {game.GameState}.");
            }

            var player = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase));
            if (player == null)
            {
                throw new Exception($"The user {request.Username} was not found in the game. Please click \"Join Game\" if you wish to join as a new player.");
            }

            // Update the connection data in case it's changed
            player.ConnectionId = connectionId;

            CardGamesHelpers.DealCards(game);

            game.PlayerToMoveUsername = game.Players.FirstOrDefault()?.Username ?? string.Empty;
            game.PlayDirection = PlayDirection.Down;

            // Add a message for everyone that the game started
            game.Messages.Add(new Message
            {
                Content = $"{player.PlayerName} started the game. It is {game.Players.FirstOrDefault()?.PlayerName ?? "<unknown>"}'s turn, and the play direction is down.",
                ToPlayerUsernames = game.Players.Select(x => x.Username).ToList()
            });

            game.GameState = GameState.Started;

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to all players, as most of the info has changed
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        public async Task<List<ResponseWithClientId>> PlayCardToDeckAsync(PlayCardToDeckRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("PlayCardToDeckRequest.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("PlayCardToDeckRequest.Username must be supplied");
            }

            if (string.IsNullOrEmpty(request.Card))
            {
                throw new Exception("PlayCardToDeckRequest.Card must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState != GameState.Started)
            {
                throw new Exception($"The game in which you are trying to play a card is not in the started state. State: {game.GameState}.");
            }

            if (!string.Equals(request.Username, game.PlayerToMoveUsername, StringComparison.OrdinalIgnoreCase))
            {
                var playerToMove = game.Players.SingleOrDefault(x => string.Equals(x.Username, game.PlayerToMoveUsername, StringComparison.OrdinalIgnoreCase));
                var playerToMoveNameDisplay = playerToMove != null ? playerToMove.PlayerName : $"<unknown player {game.PlayerToMoveUsername}>";
                throw new Exception($"You may not play a card to the deck, because it is not your turn. It is {playerToMoveNameDisplay}'s turn.");
            }

            var player = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase));
            if (player == null)
            {
                throw new Exception($"The user {request.Username} was not found in the game. Please click \"Join Game\" if you wish to join as a new player.");
            }

            var hand = game.Hands.SingleOrDefault(x => string.Equals(x.PlayerUsername, request.Username, StringComparison.OrdinalIgnoreCase));
            if (hand == null)
            {
                throw new Exception($"The user {request.Username} does not have a hand in the game.");
            }

            var cardIndex = hand.Cards.IndexOf(request.Card);
            if (cardIndex == -1)
            {
                throw new Exception($"The card {request.Card} was not in {request.Username}'s hand.");
            }

            // Check that the move is legal, and apply any extra logic dependent on which card is played,
            // including setting the next player as active, if appropriate.
            var extraMessage = CheckMoveAndSetMoveState(game, request.Card, player);

            // Now perform the card operation itself.
            hand.Cards.RemoveAt(cardIndex);

            var deck = game.Decks.FirstOrDefault(x => x.CanDropFromHand);
            if (deck == null)
            {
                throw new Exception($"There are no decks which accept cards from your hand.");
            }

            deck.Cards.Insert(0, request.Card);

            // Update the connection data in case it's changed
            player.ConnectionId = connectionId;

            // Add a message for everyone that the game started
            var msg = $"{player.PlayerName} played the {request.Card.CardDescription()}.";
            if (!string.IsNullOrEmpty(extraMessage))
            {
                msg += $" {extraMessage}";
            }
            game.Messages.Add(new Message
            {
                Content = msg,
                ToPlayerUsernames = game.Players.Select(x => x.Username).ToList()
            });

            game.Moves.Add(new Move
            {
                PlayerUsername = request.Username,
                Card = request.Card,
                DeckId = deck.Id,
                MoveDirection = MoveDirection.HandToDeck
            });

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to all players, as most of the info has changed
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        public async Task<List<ResponseWithClientId>> TakeCardFromDeckAsync(TakeCardFromDeckRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("TakeCardFromDeckAsync.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("TakeCardFromDeckAsync.Username must be supplied");
            }

            if (string.IsNullOrEmpty(request.DeckId))
            {
                throw new Exception("TakeCardFromDeckAsync.DeckId must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState != GameState.Started)
            {
                throw new Exception($"The game in which you are trying to take a card from the deck is not in the started state. State: {game.GameState}.");
            }

            if (!string.Equals(request.Username, game.PlayerToMoveUsername, StringComparison.OrdinalIgnoreCase))
            {
                var playerToMove = game.Players.SingleOrDefault(x => string.Equals(x.Username, game.PlayerToMoveUsername, StringComparison.OrdinalIgnoreCase));
                var playerToMoveNameDisplay = playerToMove != null ? playerToMove.PlayerName : $"<unknown player {game.PlayerToMoveUsername}>";
                throw new Exception($"You may not take a card from the deck, because it is not your turn. It is {playerToMoveNameDisplay}'s turn.");
            }

            var player = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase));
            if (player == null)
            {
                throw new Exception($"The user {request.Username} was not found in the game. Please click \"Join Game\" if you wish to join as a new player.");
            }

            var hand = game.Hands.SingleOrDefault(x => string.Equals(x.PlayerUsername, request.Username, StringComparison.OrdinalIgnoreCase));
            if (hand == null)
            {
                throw new Exception($"The user {request.Username} does not have a hand in the game.");
            }

            var deck = game.Decks.FirstOrDefault(x => x.Id == request.DeckId);
            if (deck == null || !deck.CanDragToHand)
            {
                throw new Exception($"There are no decks with ID {request.DeckId} which allow dragging cards to your hand.");
            }
            if (deck.Cards.Count == 0)
            {
                throw new Exception($"The deck has no cards.");
            }

            if (game.MoveState == MoveState.JumpWasPlayed)
            {
                throw new Exception("You may not take a card from the deck, as you have been jumped.");
            }

            if (game.MoveState == MoveState.WaitingForSuit)
            {
                throw new Exception("You may not take a card from the deck, as you have played your turn already.");
            }

            var card = deck.Cards.First();
            hand.Cards.Add(deck.Cards.First());
            deck.Cards.RemoveAt(0);

            // Ordinarily, picking a card ends the turn and moves to the next player, including if there was a question
            // asked. However, if a two or three was played, we only end the turn once all necessary cards are picked.
            var extraMessage = string.Empty;
            if (game.MoveState == MoveState.TwoWasPlayed || game.MoveState == MoveState.ThreeWasPlayed)
            {
                game.TwoOrThreeMoveCardsToBePicked -= 1;
                if (game.TwoOrThreeMoveCardsToBePicked == 0)
                {
                    // All cards now picked satisfactorily.
                    game.MoveState = MoveState.Normal;
                    var nextPlayer = game.MoveToNextPlayer(player, true);
                    extraMessage = $" It is now {nextPlayer.PlayerName}'s turn.";
                }
                else
                {
                    extraMessage = $" {game.TwoOrThreeMoveCardsToBePicked} more cards must be picked.";
                }
            }
            else
            {
                // Not in a two/three loop. Just play normally.
                game.MoveState = MoveState.Normal;
                game.MoveToNextPlayer(player, true);
            }
            
            // Update the connection data in case it's changed
            player.ConnectionId = connectionId;

            game.Moves.Add(new Move
            {
                PlayerUsername = request.Username,
                Card = card,
                DeckId = deck.Id,
                MoveDirection = MoveDirection.DeckToHand
            });

            // Add a message for everyone that the game started
            game.Messages.Add(new Message
            {
                Content = $"{player.PlayerName} took a card from the deck.{extraMessage}",
                ToPlayerUsernames = game.Players.Select(x => x.Username).ToList()
            });

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to all players, as most of the info has changed
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        public async Task<List<ResponseWithClientId>> UndoLastMoveAsync(UndoLastMoveRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("UndoLastMoveAsync.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("UndoLastMoveAsync.Username must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState != GameState.Started)
            {
                throw new Exception($"The game in which you are trying to undo a move is not in the started state. State: {game.GameState}.");
            }

            var undoingPlayer = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase));
            if (undoingPlayer == null)
            {
                throw new Exception($"The request user {request.Username} was not found in the game.");
            }

            var moveToUndo = game.Moves.LastOrDefault();
            if (moveToUndo == null)
            {
                throw new Exception("No move was found to undo.");
            }

            var movePlayer = game.Players.SingleOrDefault(x => string.Equals(x.Username, moveToUndo.PlayerUsername, StringComparison.OrdinalIgnoreCase));
            if (movePlayer == null)
            {
                throw new Exception($"The move user {request.Username} was not found in the game.");
            }

            var hand = game.Hands.SingleOrDefault(x => string.Equals(x.PlayerUsername, moveToUndo.PlayerUsername, StringComparison.OrdinalIgnoreCase));
            if (hand == null)
            {
                throw new Exception($"The user {moveToUndo.PlayerUsername} does not have a hand in the game.");
            }

            var deck = game.Decks.SingleOrDefault(x => x.Id == moveToUndo.DeckId);
            if (deck == null)
            {
                throw new Exception($"The deck ({moveToUndo.DeckId}) does not exist.");
            }

            var message = string.Empty;
            if (moveToUndo.MoveDirection == MoveDirection.HandToDeck)
            {
                if (deck.Cards.Count == 0 || deck.Cards[0] != moveToUndo.Card)
                {
                    throw new Exception($"The wrong card ({(deck.Cards.Count > 0 ? deck.Cards[0] : "<no card>")} instead of {moveToUndo.Card}) was at the front of the deck.");
                }

                hand.Cards.Add(moveToUndo.Card);
                deck.Cards.RemoveAt(0);

                message = $"The card {moveToUndo.Card.CardDescription()} was moved back to the player's hand.";
            }
            else
            {
                var cardLocationInHand = hand.Cards.IndexOf(moveToUndo.Card);
                if (cardLocationInHand == -1)
                {
                    throw new Exception("Card not found in hand");
                }

                hand.Cards.RemoveAt(cardLocationInHand);
                deck.Cards.Insert(0, moveToUndo.Card);

                message = $"The taken card was put back on to the face-down deck.";
            }

            game.Moves.Remove(moveToUndo);
            game.UndoneMoves.Add(moveToUndo);

            // Update the connection data in case it's changed
            undoingPlayer.ConnectionId = connectionId;

            // Add a message for everyone that the game started
            game.Messages.Add(new Message
            {
                Content = $"{undoingPlayer.PlayerName} undid the last move by player {movePlayer.PlayerName}. {message}",
                ToPlayerUsernames = game.Players.Select(x => x.Username).ToList()
            });

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to all players, as most of the info has changed
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        public async Task<List<ResponseWithClientId>> ShuffleAndMoveCardsAsync(ShuffleAndMoveCardsRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("ShuffleAndMoveCardsAsync.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("ShuffleAndMoveCardsAsync.Username must be supplied");
            }

            if (string.IsNullOrEmpty(request.FromDeckId))
            {
                throw new Exception("ShuffleAndMoveCardsAsync.FromDeckId must be supplied");
            }

            if (string.IsNullOrEmpty(request.ToDeckId))
            {
                throw new Exception("ShuffleAndMoveCardsAsync.ToDeckId must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState != GameState.Started)
            {
                throw new Exception($"The game in which you are trying to shuffle and move is not in the started state. State: {game.GameState}.");
            }

            var player = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase));
            if (player == null)
            {
                throw new Exception($"The user {request.Username} was not found in the game. Please click \"Join Game\" if you wish to join as a new player.");
            }

            var hand = game.Hands.SingleOrDefault(x => string.Equals(x.PlayerUsername, request.Username, StringComparison.OrdinalIgnoreCase));
            if (hand == null)
            {
                throw new Exception($"The user {request.Username} does not have a hand in the game.");
            }

            var fromDeck = game.Decks.FirstOrDefault(x => x.Id == request.FromDeckId);
            var toDeck = game.Decks.FirstOrDefault(x => x.Id == request.ToDeckId);

            if (fromDeck == null || toDeck == null)
            {
                throw new Exception($"Either the from deck ({request.FromDeckId}) or the to deck ({request.ToDeckId}) does not exist.");
            }
            if (fromDeck.Cards.Count == 0)
            {
                throw new Exception($"The from deck has no cards.");
            }
            if (fromDeck.Cards.Count == 1)
            {
                throw new Exception($"The from deck has only one card.");
            }

            var newFromDeck = fromDeck.Cards.Take(1).ToList();
            var newToDeck = toDeck.Cards.Concat(fromDeck.Cards.Skip(1)).ToList();
            newToDeck.Shuffle();

            fromDeck.Cards = newFromDeck;
            toDeck.Cards = newToDeck;

            // Update the connection data in case it's changed
            player.ConnectionId = connectionId;

            // Add a message for everyone that the game started
            game.Messages.Add(new Message
            {
                Content = $"{player.PlayerName} shuffled the pack and moved it to the other deck.",
                ToPlayerUsernames = game.Players.Select(x => x.Username).ToList()
            });

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to all players, as most of the info has changed
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        ////public async Task<List<ResponseWithClientId>> EndTurnAsync(EndTurnRequest request, string connectionId, ILambdaLogger logger)
        ////{
        ////    if (string.IsNullOrEmpty(request.GameCode))
        ////    {
        ////        throw new Exception("EndTurnAsync.GameCode must be supplied");
        ////    }

        ////    if (string.IsNullOrEmpty(request.Username))
        ////    {
        ////        throw new Exception("EndTurnAsync.Username must be supplied");
        ////    }

        ////    // Find the game in question
        ////    var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

        ////    var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

        ////    if (queryResult.Count == 0)
        ////    {
        ////        throw new Exception($"Game with code {request.GameCode} was not found.");
        ////    }

        ////    if (queryResult.Count > 1)
        ////    {
        ////        throw new Exception($"More than one game with code {request.GameCode} was found.");
        ////    }

        ////    var gameStorageIds = queryResult.Single();

        ////    var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

        ////    var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

        ////    if (game.GameState != GameState.Started)
        ////    {
        ////        throw new Exception($"The game in which you are trying to end a turn is not in the started state. State: {game.GameState}.");
        ////    }

        ////    if (!string.Equals(game.PlayerToMoveUsername, request.Username, StringComparison.OrdinalIgnoreCase))
        ////    {
        ////        throw new Exception("You cannot end your turn, because it is not your turn.");
        ////    }

        ////    var player = game.Players.SingleOrDefault(x => string.Equals(x.PlayerInfo.Username, request.Username, StringComparison.OrdinalIgnoreCase));
        ////    if (player == null)
        ////    {
        ////        throw new Exception($"The request user {request.Username} was not found in the game.");
        ////    }

        ////    if (request.ReverseDirection)
        ////    {
        ////        game.PlayDirection = game.PlayDirection == PlayDirection.Up ? PlayDirection.Down : PlayDirection.Up;
        ////    }

        ////    game.JumpWasPlayed = request.JumpPlayer;
        ////    player.PlayerInfo.Cardy = request.Cardy;

        ////    if (request.Finished)
        ////    {
        ////        var currentMaxFinishingPosition = game.Players.Select(x => x.PlayerInfo.FinishingPosition).Max();
        ////        player.PlayerInfo.FinishingPosition = currentMaxFinishingPosition + 1;
        ////    }

        ////    var currentPlayerIndex = game.Players.IndexOf(player);
        ////    var nextPlayerIndex = game.PlayDirection == PlayDirection.Up ? currentPlayerIndex - 1 : currentPlayerIndex + 1;
        ////    if (nextPlayerIndex == game.Players.Count)
        ////    {
        ////        // Last player reached. Go back to the beginning.
        ////        nextPlayerIndex = 0;
        ////    }
        ////    if (nextPlayerIndex == -1)
        ////    {
        ////        // First player reached. Go back to the end.
        ////        nextPlayerIndex = game.Players.Count - 1;
        ////    }

        ////    var nextPlayerInfo = game.Players[nextPlayerIndex].PlayerInfo;
        ////    game.PlayerToMoveUsername = nextPlayerInfo.Username;

        ////    // Update the connection data in case it's changed
        ////    player.ConnectionId = connectionId;

        ////    // Build up the appropriate message.
        ////    var sb = new StringBuilder();
        ////    sb.Append($"{player.PlayerInfo.PlayerName} finished their turn");
        ////    if (request.ReverseDirection)
        ////    {
        ////        sb.Append(" and reversed the order of play");
        ////    }
        ////    sb.Append(".");
        ////    if (request.Finished)
        ////    {
        ////        if (player.PlayerInfo.FinishingPosition == 1)
        ////        {
        ////            sb.Append($" {player.PlayerInfo.PlayerName} has won the game!");
        ////        }
        ////        else
        ////        {
        ////            sb.Append($" They have finished, in position {player.PlayerInfo.FinishingPosition}");
        ////        }
        ////    }
        ////    if (request.Cardy)
        ////    {
        ////        sb.Append($" {player.PlayerInfo.PlayerName} is Cardy!");
        ////    }
        ////    if (request.JumpPlayer)
        ////    {
        ////        sb.Append(" A jump card has been played.");
        ////    }

        ////    game.Messages.Add(new Message
        ////    {
        ////        Content = sb.ToString(),
        ////        ToPlayerUsernames = game.Players.Select(x => x.PlayerInfo.Username).ToList()
        ////    });

        ////    // Update the game in the DB
        ////    gameStorage.Content = JsonConvert.SerializeObject(game);

        ////    // And save it back
        ////    logger.LogLine($"Saving game with id {gameStorage.Id}.");
        ////    await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

        ////    // Send a full game info to all players, as most of the info has changed
        ////    var results = new List<ResponseWithClientId>();
        ////    foreach (var particularPlayer in game.Players)
        ////    {
        ////        var fullGameResponse = new FullGameResponse
        ////        {
        ////            GameId = game.Id,
        ////            GameCode = game.GameCode,
        ////            GameName = game.GameName,
        ////            GameState = game.GameState,
        ////            PlayerInfo = game.Players.Select(x => x.PlayerInfo).SingleOrDefault(x => string.Equals(x.Username, particularPlayer.PlayerInfo.Username, StringComparison.OrdinalIgnoreCase)),
        ////            AllPlayers = game.Players.Select(x => x.PlayerInfo).ToList(),
        ////            Hand = game.Hands.SingleOrDefault(x => string.Equals(x.PlayerUsername, particularPlayer.PlayerInfo.Username, StringComparison.OrdinalIgnoreCase))?.Cards ?? new List<string>(),
        ////            Decks = game.Decks.GetVisibleDecks(),
        ////            MoveCanBeUndone = game.Moves.Any(),
        ////            Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.PlayerInfo.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
        ////            PlayerToMoveUsername = game.PlayerToMoveUsername,
        ////            PlayDirection = game.PlayDirection,
        ////            JumpWasPlayed = game.JumpWasPlayed
        ////        };

        ////        results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
        ////    }

        ////    return results;
        ////}

        public async Task<List<ResponseWithClientId>> SetCardyAsync(SetCardyRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("SetCardyAsync.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("SetCardyAsync.Username must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState != GameState.Started)
            {
                throw new Exception($"The game in which you are trying to end a turn is not in the started state. State: {game.GameState}.");
            }

            var player = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase));
            if (player == null)
            {
                throw new Exception($"The request user {request.Username} was not found in the game.");
            }

            if (player.Cardy == request.Cardy)
            {
                // Cardy state already set
                throw new Exception("You were already cardy and tried to set it as cardy again. Or you were not cardy, and tried to set it again as not cardy.");
            }

            player.Cardy = request.Cardy;

            // Update the connection data in case it's changed
            player.ConnectionId = connectionId;

            // Build up the appropriate message.
            var msg = $"{player.PlayerName} is {(request.Cardy ? string.Empty : "not ")}cardy{(request.Cardy ? "!" : ".")}";

            game.Messages.Add(new Message
            {
                Content = msg,
                ToPlayerUsernames = game.Players.Select(x => x.Username).ToList()
            });

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to all players, as most of the info has changed
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        public async Task<List<ResponseWithClientId>> ChooseSuitAsync(ChooseSuitRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("ChooseSuitAsync.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("ChooseSuitAsync.Username must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState != GameState.Started)
            {
                throw new Exception($"The game in which you are trying to end a turn is not in the started state. State: {game.GameState}.");
            }

            var player = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase));
            if (player == null)
            {
                throw new Exception($"The request user {request.Username} was not found in the game.");
            }

            if (game.MoveState != MoveState.WaitingForSuit)
            {
                throw new Exception("You may only choose a suit after playing an ace.");
            }

            game.CurrentSuit = request.Suit;
            game.MoveState = MoveState.Normal;
            game.MoveToNextPlayer(player, true);

            // Update the connection data in case it's changed
            player.ConnectionId = connectionId;

            // Build up the appropriate message.
            var msg = $"{player.PlayerName} set the suit to {request.Suit.SuitName()}";

            game.Messages.Add(new Message
            {
                Content = msg,
                ToPlayerUsernames = game.Players.Select(x => x.Username).ToList()
            });

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to all players, as most of the info has changed
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        public async Task<List<ResponseWithClientId>> RespondToJumpAsync(RespondToJumpRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("RespondToJumpAsync.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.Username))
            {
                throw new Exception("RespondToJumpAsync.Username must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState != GameState.Started)
            {
                throw new Exception($"The game in which you are trying to respond to a jump is not in the started state. State: {game.GameState}.");
            }

            if (!string.Equals(game.PlayerToMoveUsername, request.Username, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("You cannot respond to a jump, because it is not your turn.");
            }

            var player = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.Username, StringComparison.OrdinalIgnoreCase));
            if (player == null)
            {
                throw new Exception($"The request user {request.Username} was not found in the game.");
            }

            if (game.MoveState != MoveState.JumpWasPlayed)
            {
                throw new Exception($"Attempting to block a jump when the game is not in jump mode.");
            }

            var nextPlayerInfo = player;
            if (!request.BlockJump)
            {
                // If the jump is not being blocked, proceed to the next player.
                nextPlayerInfo = game.MoveToNextPlayer(player, false);
            }

            // Cancel the jump-played flag
            game.MoveState = MoveState.Normal;

            // Update the connection data in case it's changed
            player.ConnectionId = connectionId;

            // Add a message for everyone that the game started
            var thingyMessage = request.BlockJump ? " blocked the jump" : $" was unable to block the jump. It is now {nextPlayerInfo.PlayerName}'s turn";
            game.Messages.Add(new Message
            {
                Content = $"{player.PlayerName} {thingyMessage}. ",
                ToPlayerUsernames = game.Players.Select(x => x.Username).ToList()
            });

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to all players, as most of the info has changed
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        public async Task<List<ResponseWithClientId>> SetPlayerTurnAsync(SetPlayerTurnRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("SetPlayerTurnAsync.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.RequestPlayerUsername))
            {
                throw new Exception("SetPlayerTurnAsync.RequestPlayerUsername must be supplied");
            }

            if (string.IsNullOrEmpty(request.PlayerToSetUsername))
            {
                throw new Exception("SetPlayerTurnAsync.PlayerToSetUsername must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState != GameState.Started)
            {
                throw new Exception($"The game in which you are trying to set a player's turn is not in the started state. State: {game.GameState}.");
            }

            var requestPlayer = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.RequestPlayerUsername, StringComparison.OrdinalIgnoreCase));
            if (requestPlayer == null)
            {
                throw new Exception($"The request user {request.RequestPlayerUsername} was not found in the game.");
            }

            var playerToSet = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.PlayerToSetUsername, StringComparison.OrdinalIgnoreCase));
            if (playerToSet == null)
            {
                throw new Exception($"The user you wish to set, {request.PlayerToSetUsername}, was not found in the game.");
            }

            if (request.PlayDirection != PlayDirection.Up && request.PlayDirection != PlayDirection.Down)
            {
                throw new Exception($"Unknown play direction: {request.PlayDirection}");
            }

            game.PlayDirection = request.PlayDirection;
            game.PlayerToMoveUsername = request.PlayerToSetUsername;

            // Update the connection data in case it's changed
            requestPlayer.ConnectionId = connectionId;

            // Add a message for everyone that the game started
            game.Messages.Add(new Message
            {
                Content = $"{requestPlayer.PlayerName} decided it is {playerToSet.PlayerName}'s turn, with a play direction of {request.PlayDirection}.",
                ToPlayerUsernames = game.Players.Select(x => x.Username).ToList()
            });

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to all players, as most of the info has changed
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        public async Task<List<ResponseWithClientId>> ChangePlayerPositionAsync(ChangePlayerPositionRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("ChangePlayerPositionAsync.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.RequestPlayerUsername))
            {
                throw new Exception("ChangePlayerPositionAsync.RequestPlayerUsername must be supplied");
            }

            if (string.IsNullOrEmpty(request.PlayerToSetUsername))
            {
                throw new Exception("ChangePlayerPositionAsync.PlayerToSetUsername must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            if (game.GameState != GameState.Created)
            {
                throw new Exception($"The game in which you are trying to set a player's turn is not in the created state. State: {game.GameState}.");
            }

            var requestPlayer = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.RequestPlayerUsername, StringComparison.OrdinalIgnoreCase));
            if (requestPlayer == null)
            {
                throw new Exception($"The request user {request.RequestPlayerUsername} was not found in the game.");
            }

            var playerToSet = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.PlayerToSetUsername, StringComparison.OrdinalIgnoreCase));
            if (playerToSet == null)
            {
                throw new Exception($"The user you wish to set, {request.PlayerToSetUsername}, was not found in the game.");
            }

            if (request.PlayDirection != PlayDirection.Up && request.PlayDirection != PlayDirection.Down)
            {
                throw new Exception($"Unknown play direction: {request.PlayDirection}");
            }

            var playerToSetIndex = game.Players.IndexOf(playerToSet);
            
            if (request.PlayDirection == PlayDirection.Up && playerToSetIndex == 0)
            {
                throw new Exception("Can't move player up - they are at the top of the list.");
            }
            if (request.PlayDirection == PlayDirection.Down && playerToSetIndex == game.Players.Count - 1)
            {
                throw new Exception("Can't move player down - they are at the bottom of the list.");
            }

            var playerToSwapIndex = request.PlayDirection == PlayDirection.Up ? playerToSetIndex - 1 : playerToSetIndex + 1;

            game.Players[playerToSetIndex] = game.Players[playerToSwapIndex];
            game.Players[playerToSwapIndex] = playerToSet;

            // Update the connection data in case it's changed
            requestPlayer.ConnectionId = connectionId;

            // Add a message for everyone that the game started
            game.Messages.Add(new Message
            {
                Content = $"{requestPlayer.PlayerName} moved {playerToSet.PlayerName} {request.PlayDirection} the list.",
                ToPlayerUsernames = game.Players.Select(x => x.Username).ToList()
            });

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to all players, as most of the info has changed
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        public async Task<List<ResponseWithClientId>> SendMessageToPlayerAsync(SendMessageToPlayerRequest request, string connectionId, ILambdaLogger logger)
        {
            if (string.IsNullOrEmpty(request.GameCode))
            {
                throw new Exception("SetPlayerTurnAsync.GameCode must be supplied");
            }

            if (string.IsNullOrEmpty(request.RequestPlayerUsername))
            {
                throw new Exception("SetPlayerTurnAsync.RequestPlayerUsername must be supplied");
            }

            // Find the game in question
            var config = new DynamoDBOperationConfig { IndexName = "GameCodeIndex" };

            var queryResult = await this.GameTableDDBContext.QueryAsync<GameStorage>(request.GameCode.ToUpper(), config).GetRemainingAsync();

            if (queryResult.Count == 0)
            {
                throw new Exception($"Game with code {request.GameCode} was not found.");
            }

            if (queryResult.Count > 1)
            {
                throw new Exception($"More than one game with code {request.GameCode} was found.");
            }

            var gameStorageIds = queryResult.Single();

            var gameStorage = await this.GameTableDDBContext.LoadAsync<GameStorage>(gameStorageIds.Id);

            var game = JsonConvert.DeserializeObject<Game>(gameStorage.Content);

            var requestPlayer = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.RequestPlayerUsername, StringComparison.OrdinalIgnoreCase));
            if (requestPlayer == null)
            {
                throw new Exception($"The request user {request.RequestPlayerUsername} was not found in the game.");
            }

            var playerToMessageName = string.Empty;
            if (!string.IsNullOrEmpty(request.PlayerToMessageUsername))
            {
                var playerToMessage = game.Players.SingleOrDefault(x => string.Equals(x.Username, request.PlayerToMessageUsername, StringComparison.OrdinalIgnoreCase));
                if (playerToMessage == null)
                {
                    throw new Exception($"The user to message, {request.PlayerToMessageUsername}, was not found in the game.");
                }

                playerToMessageName = playerToMessage.PlayerName;
            }

            // Update the connection data in case it's changed
            requestPlayer.ConnectionId = connectionId;

            // Add the message for the two player concerneds
            // If no to user is specified, then message everybody
            var toPlayerUsernames = !string.IsNullOrEmpty(request.PlayerToMessageUsername)
                ? new[] { request.RequestPlayerUsername, request.PlayerToMessageUsername }.ToList()
                : game.Players.Select(x => x.Username).ToList();
            var toPlayerMessage = !string.IsNullOrEmpty(playerToMessageName) ? $" to {playerToMessageName}" : string.Empty;
            game.Messages.Add(new Message
            {
                Content = $"Message from {requestPlayer.PlayerName}{toPlayerMessage}: {request.Message}",
                ToPlayerUsernames = toPlayerUsernames
            });

            // Update the game in the DB
            gameStorage.Content = JsonConvert.SerializeObject(game);

            // And save it back
            logger.LogLine($"Saving game with id {gameStorage.Id}.");
            await this.GameTableDDBContext.SaveAsync<GameStorage>(gameStorage);

            // Send a full game info to the two players concerned
            var results = new List<ResponseWithClientId>();
            foreach (var particularPlayer in game.Players)
            {
                var handsByPlayerId = game.Hands.ToDictionary(x => x.PlayerUsername, StringComparer.OrdinalIgnoreCase);
                var thePlayersHand = handsByPlayerId.ContainsKey(particularPlayer.Username) ? handsByPlayerId[particularPlayer.Username] : default(Hand);
                var fullGameResponse = new FullGameResponse
                {
                    GameId = game.Id,
                    GameCode = game.GameCode,
                    GameName = game.GameName,
                    GameState = game.GameState,
                    PlayerInfo = game.Players.SingleOrDefault(x => string.Equals(x.Username, particularPlayer.Username, StringComparison.OrdinalIgnoreCase)).ToPlayerInfo(handsByPlayerId),
                    AllPlayers = game.Players.Select(x => x.ToPlayerInfo(handsByPlayerId)).ToList(),
                    Hand = thePlayersHand?.Cards ?? new List<string>(),
                    Decks = game.Decks.GetVisibleDecks(),
                    MoveCanBeUndone = game.Moves.Any(),
                    Messages = game.Messages.Where(x => x.ToPlayerUsernames.Contains(particularPlayer.Username, StringComparer.OrdinalIgnoreCase)).Select(x => x.Content).ToList(),
                    PlayerToMoveUsername = game.PlayerToMoveUsername,
                    PlayDirection = game.PlayDirection,
                    MoveState = game.MoveState,
                    WinnerName = game.WinnerName
                };

                results.Add(new ResponseWithClientId(fullGameResponse, particularPlayer.ConnectionId));
            }

            return results;
        }

        private static string CreateGameCode(long number)
        {
            var result = new StringBuilder();
            for (var i = 0; i < 6; i++)
            {
                result.Append(NumberToCharBase36(number % 36));
                number /= 36;
            }

            return result.ToString();
        }

        private static char NumberToCharBase36(long number)
        {
            if (number >= 0 && number < 26)
            {
                // A to Z
                return (char)(number + 'A');
            }
            else if (number >= 26 && number < 36)
            {
                // 0 to 9
                return (char)(number - 26 + '0');
            }
            else
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Return value is any extra message to display to the user.
        /// </summary>
        private static string CheckMoveAndSetMoveState(Game game, string card, Player player)
        {
            var rank = card.Rank();
            var suit = card.Suit();

            // Rule 0 - No playing cards when waiting for suit
            if (game.MoveState == MoveState.WaitingForSuit)
            {
                throw new Exception("You may not play a card. You have played your turn already.");
            }

            // Rule 1 - If a jump was played, only a jack can be used to cancel the jump
            if (game.MoveState == MoveState.JumpWasPlayed)
            {
                if (rank != "J")
                {
                    throw new Exception($"If you are being jumped, you may only play a jack to cancel the jump. The {card.CardDescription()} is not valid.");
                }

                // Jack was played on a jack, so "jump was played" mode remains active. Ends turn.
                game.CurrentSuit = suit;
                game.MoveToNextPlayer(player, true);
                return "The jump was blocked with a jack, and the next player is now jumped.";
            }

            // Rule 2 - Other than in blocking a jump, an ace may be played at any time, including to block a two or a three.
            if (rank == "A")
            {
                if (game.MoveState == MoveState.TwoWasPlayed || game.MoveState == MoveState.ThreeWasPlayed)
                {
                    // When blocking 2 or 3, the player may not set a suit.
                    // Other than that, we just cancel the repeat card and resume normal play.
                    game.CurrentSuit = suit;
                    game.CurrentRank = "A";
                    game.MoveState = MoveState.Normal;
                    game.TwoOrThreeMoveCardsToBePicked = 0;
                    game.MoveToNextPlayer(player, true);
                    return $"The {(game.MoveState == MoveState.TwoWasPlayed ? "two" : "three")} was blocked with an ace. The suit is now {suit.SuitName()}.";
                }

                // Otherwise, the same player must set a suit. Don't move to next player because it's still their turn.
                game.CurrentRank = "A";
                game.MoveState = MoveState.WaitingForSuit;
                return "They must now choose a suit.";
            }

            // Rule 3 - If a two or three was played before, the new card must be of that same rank
            if (game.MoveState == MoveState.TwoWasPlayed)
            {
                if (rank != "2")
                {
                    throw new Exception($"A two was played before, so only another two or an ace may be played. The {card.CardDescription()} is not valid.");
                }

                // Another two was played. Increase the count of cards to be picked and continue.
                game.TwoOrThreeMoveCardsToBePicked += 2;
                game.CurrentSuit = suit;
                game.MoveToNextPlayer(player, true);
                return $"The next player must pick {game.TwoOrThreeMoveCardsToBePicked} cards!";
            }
            if (game.MoveState == MoveState.ThreeWasPlayed)
            {
                if (rank != "3")
                {
                    throw new Exception($"A three was played before, so only another three or an ace may be played. The {card.CardDescription()} is not valid.");
                }

                // Another three was played. Increase the count of cards to be picked and continue.
                game.TwoOrThreeMoveCardsToBePicked += 3;
                game.MoveToNextPlayer(player, true);
                return $"The next player must pick {game.TwoOrThreeMoveCardsToBePicked} cards!";
            }

            // For all other rules, the card must match the previous card in either suit or rank, so enforce that here.
            if (rank != game.CurrentRank && suit != game.CurrentSuit)
            {
                throw new Exception($"Either a {game.CurrentRank.RankName()} or a {game.CurrentSuit.SuitName()} must be played.");
            }

            // The current suit and rank are updated.
            game.CurrentRank = rank;
            game.CurrentSuit = suit;

            // Rule 4 - A two or three played
            if (rank == "2")
            {
                game.MoveState = MoveState.TwoWasPlayed;
                game.TwoOrThreeMoveCardsToBePicked = 2;
                game.MoveToNextPlayer(player, true);
                return "The next player must pick 2 cards!";
            }
            if (rank == "3")
            {
                game.MoveState = MoveState.ThreeWasPlayed;
                game.TwoOrThreeMoveCardsToBePicked = 3;
                game.MoveToNextPlayer(player, true);
                return $"The next player must pick 3 cards!";
            }

            // Rule 5 - A queen was played. Question must be answered by the same player.
            if (rank == "Q")
            {
                game.MoveState = MoveState.QuestionWasAsked;
                return "The question requires an answer!";
            }

            // Rule 6 - King kicks back.
            if (rank == "K")
            {
                game.PlayDirection = game.PlayDirection == PlayDirection.Up ? PlayDirection.Down : PlayDirection.Up;
                var nextPlayerInfo = game.MoveToNextPlayer(player, true);
                return $"Kick back! It is now {nextPlayerInfo.PlayerName}'s turn.";
            }

            // Rule 7 - Jack jumps.
            if (rank == "J")
            {
                game.MoveState = MoveState.JumpWasPlayed;
                game.MoveToNextPlayer(player, true);
                return "Jump!";
            }

            // Rule 8 - Winner
            if (player.Cardy)
            {
                var hand = game.Hands.SingleOrDefault(x => string.Equals(x.PlayerUsername, player.Username, StringComparison.OrdinalIgnoreCase));
                if (hand != null && hand.Cards.Count == 1)
                {
                    player.Winner = true;
                    game.MoveState = MoveState.GameWon;
                    game.WinnerName = player.PlayerName;
                    return $"{player.PlayerName} is the winner!!!";
                }
            }

            // Any other situation is a normal card. Just return with no special action.
            game.MoveToNextPlayer(player, true);
            return string.Empty;
        }
    }
}

using Amazon.Lambda.Core;
using CardGamesServer.Requests;
using CardGamesServer.Responses;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CardGamesServer
{
    public interface ICardGamesEngine
    {
        Task<List<ResponseWithClientId>> CreateGameAsync(CreateGameRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> JoinGameAsync(JoinGameRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> RejoinGameAsync(RejoinGameRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> StartGameAsync(StartGameRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> PlayCardToDeckAsync(PlayCardToDeckRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> TakeCardFromDeckAsync(TakeCardFromDeckRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> ShuffleAndMoveCardsAsync(ShuffleAndMoveCardsRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> UndoLastMoveAsync(UndoLastMoveRequest request, string connectionId, ILambdaLogger logger);

        ////Task<List<ResponseWithClientId>> EndTurnAsync(EndTurnRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> SetCardyAsync(SetCardyRequest request, string connectionId, ILambdaLogger logger);
        
        Task<List<ResponseWithClientId>> ChooseSuitAsync(ChooseSuitRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> RespondToJumpAsync(RespondToJumpRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> SetPlayerTurnAsync(SetPlayerTurnRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> ChangePlayerPositionAsync(ChangePlayerPositionRequest request, string connectionId, ILambdaLogger logger);

        Task<List<ResponseWithClientId>> SendMessageToPlayerAsync(SendMessageToPlayerRequest request, string connectionId, ILambdaLogger logger);
    }
}

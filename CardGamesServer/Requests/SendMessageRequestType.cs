namespace CardGamesServer
{
    public static class SendMessageRequestType
    {
        public const string CreateGame = "CreateGame";

        public const string JoinGame = "JoinGame";

        public const string RejoinGame = "RejoinGame";

        public const string StartGame = "StartGame";

        public const string PlayCardToDeck = "PlayCardToDeck";

        public const string TakeCardFromDeck = "TakeCardFromDeck";

        public const string ShuffleAndMoveCards = "ShuffleAndMoveCards";

        public const string UndoLastMove = "UndoLastMove";

        ////public const string EndTurn = "EndTurn";

        public const string SetCardy = "SetCardy";

        public const string ChooseSuit = "ChooseSuit";

        public const string RespondToJump = "RespondToJump";

        public const string SetPlayerTurn = "SetPlayerTurn";

        public const string ChangePlayerPosition = "ChangePlayerPosition";

        public const string SendMessageToPlayer = "SendMessageToPlayer";

        public const string TransferDeck = "TransferDeck";

        public const string CompleteGame = "CompleteGame";
    }
}

namespace CardGamesServer
{
    public class DeckDefinition
    {
        public string Id { get; set; }

        public bool InitialCardDeck { get; set; }
 
        public bool IsFaceUp { get; set; }

        public bool CanDragToHand { get; set; }

        public bool CanDropFromHand { get; set; }
    }
}

using System.Collections.Generic;

namespace CardGamesServer
{
    public class VisibleDeck
    {
        public string Id { get; set; }

        public bool HasCards { get; set; }

        public string TopCard { get; set; }

        public bool IsFaceUp { get; set; }

        public bool CanDragToHand { get; set; }

        public bool CanDropFromHand { get; set; }
    }
}

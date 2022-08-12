using System;
using System.Collections.Generic;

namespace CardGamesServer
{
    public class Deck
    {
        public Deck()
        {
            this.Cards = new List<string>();
        }

        public string Id { get; set; }

        public List<string> Cards { get; set; }

        public bool IsFaceUp { get; set; }

        public bool InitialCardDeck { get; set; }

        public bool CanDragToHand { get; set; }

        public bool CanDropFromHand { get; set; }

        internal object Take(int v)
        {
            throw new NotImplementedException();
        }
    }
}

using System.Collections.Generic;

namespace CardGamesServer
{
    public class Hand
    {
        public string PlayerUsername { get; set; }

        public List<string> Cards { get; set; }
    }
}

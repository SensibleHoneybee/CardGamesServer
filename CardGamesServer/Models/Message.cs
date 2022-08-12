using System;
using System.Collections.Generic;

namespace CardGamesServer
{
    public class Message
    {
        public string FromPlayerUsername { get; set; }

        public List<string> ToPlayerUsernames { get; set; }
        
        public string Content { get; set; }

        public DateTime TimestampUtc { get; set; }
    }
}

using Amazon.DynamoDBv2.DataModel;
using System;

namespace CardGamesServer
{
    public class GameStorage
    {
        public string Id { get; set; }

        [DynamoDBGlobalSecondaryIndexHashKey("GameCodeIndex")]
        public string GameCode { get; set; }

        public DateTime CreatedTimestamp { get; set; }

        public string Content { get; set; }

        [DynamoDBVersion]
        public long? Version { get; set; }
    }
}

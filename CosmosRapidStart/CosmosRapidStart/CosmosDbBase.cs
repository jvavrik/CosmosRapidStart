using System;
using Newtonsoft.Json;

namespace PrimeConsulting.CosmosRapidStart
{
    public class CosmosDbBase
    {
        public static string PartitionKeyValue = "6b749853-253f-47ee-8021-e82ef3842470";

        private Guid _cosmosId;
        [JsonProperty(PropertyName = "id")]
        public Guid CosmosId { get { if (_cosmosId == Guid.Empty) _cosmosId = Guid.NewGuid(); return _cosmosId; } set { if (_cosmosId == Guid.Empty) _cosmosId = value; } }
        private string _partitionKey;
        public string PartitionKey { get { if (string.IsNullOrWhiteSpace(_partitionKey)) _partitionKey = PartitionKeyValue; return _partitionKey; } set { if (string.IsNullOrWhiteSpace(_partitionKey)) _partitionKey = value; } }
    }
}


using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using System.Dynamic;
using Newtonsoft.Json;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;



namespace PrimeConsulting.CosmosRapidStart
{
    public class CosmosSqlDbService<T>
    {
        private readonly Microsoft.Azure.Cosmos.Container _container;


        public static CosmosSqlDbService<T> Connect(string database, string endpoint, string key, string container = null)
        {
            var current = new CosmosSqlDbService<T>(database, endpoint, key, string.IsNullOrWhiteSpace(container) ? typeof(T).Name : container);
          
            return current;
        }

        private CosmosSqlDbService(string databaseName, string endpoint, string key, string container)
        {
            var client = new Microsoft.Azure.Cosmos.CosmosClient(endpoint, key);
            var database = client.CreateDatabaseIfNotExistsAsync(databaseName).Result;
            database.Database.CreateContainerIfNotExistsAsync(container, "/PartitionKey");
            _container = client.GetContainer(databaseName, container);
        }

        public async Task AddAsync<T>(T item)
        {
            ExpandoObject expando = CreateDynamicObject(item);
            var dynamicExpando = expando as dynamic;
            await _container.CreateItemAsync(expando, new PartitionKey(dynamicExpando.PartitionKey.ToString()));
        }

        public async Task<T> GetAsync<T>(Guid id) where T : class
        {
            try
            {
                var response = await GetAsync<T>($"SELECT * FROM c WHERE c.Id = \"{id}\"");
                return response.FirstOrDefault();
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return default;
            }
        }

        public async Task<T> GetAsync<T>(int id) where T : class
        {
            try
            {
                var response = await GetAsync<T>($"SELECT * FROM c WHERE c.Id = {id}");
                return response.FirstOrDefault();
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return default;
            }
        }

        public async Task<CosmosDbEntity<T>> GetForUpdateAsync<T>(int id) where T : class
        {
            try
            {
                var response = await GetTrackedAsync<T>($"SELECT * FROM c WHERE c.Id = {id}");

                return response.FirstOrDefault();
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }


        public async Task<CosmosDbEntity<T>> GetForUpdateAsync<T>(Guid id) where T : class
        {
            try
            {
                var response = await GetTrackedAsync<T>($"SELECT * FROM c WHERE c.Id = \"{id}\"");

                return response.FirstOrDefault();
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<IEnumerable<T>> GetAsync<T>(string queryString = "SELECT * FROM c") where T : class
        {
            var response = await GetTrackedAsync<T>(queryString);

            return response.Select(x => x.Entity);

        }

        public async Task<IEnumerable<CosmosDbEntity<T>>> GetTrackedAsync<T>(string queryString = "SELECT * FROM c") where T : class
        {
            var results = new List<CosmosDbEntity<T>>();

            using (FeedIterator setIterator = _container.GetItemQueryStreamIterator(new QueryDefinition(queryString)))
            {
                while (setIterator.HasMoreResults)
                {
                    using (ResponseMessage response = await setIterator.ReadNextAsync())
                    {
                        if (response.Diagnostics != null)
                        {
                            Console.WriteLine($"ItemStreamFeed Diagnostics: {response.Diagnostics}");
                        }

                        response.EnsureSuccessStatusCode();
                        using (StreamReader sr = new StreamReader(response.Content))
                        using (JsonTextReader jtr = new JsonTextReader(sr))
                        {

                            Newtonsoft.Json.JsonSerializer jsonSerializer = new Newtonsoft.Json.JsonSerializer();
                            var items = jsonSerializer.Deserialize<dynamic>(jtr).Documents;

                            //somewhat hacky. We need to remove the Cosmos "id" since it collides with our "Id" property since there is no case sensitivity.
                            //maybe a cleaner way to do this?
                            foreach (var item in items)
                            {
                                var cosmosId = item["id"];
                                var itemAsJObject = ((JObject)item);
                                itemAsJObject.Remove("id");
                                T castedItem = (item).ToObject<T>();
                                //wrap the item in a CosmosDbEntity
                                CosmosDbEntity<T> cosmosEntity = new CosmosDbEntity<T>
                                {
                                    Entity = castedItem,
                                    CosmosDbBase = new CosmosDbBase
                                    {
                                        CosmosId = cosmosId
                                    }
                                };
                                results.Add(cosmosEntity);
                            }
                        }
                    }
                }

            }

            return results;
        }

        public async Task UpdateAsync(CosmosDbEntity<T> item)
        {
            var expando = CreateDynamicObject(item.Entity, item.CosmosDbBase);

            var dynamicExpando = expando as dynamic;
            await _container.UpsertItemAsync(dynamicExpando, new PartitionKey(CosmosDbBase.PartitionKeyValue));
        }

        private ExpandoObject CreateDynamicObject<T>(T item, CosmosDbBase cosmosDbBase = null)
        {
            var expando = new ExpandoObject();
            foreach (PropertyInfo prop in typeof(T).GetProperties())
            {
                expando.TryAdd(prop.Name, prop.GetValue(item));
            }
            var cosmosBase = cosmosDbBase ?? new CosmosDbBase();
            foreach (PropertyInfo prop in typeof(CosmosDbBase).GetProperties())
            {
                var attributes = prop.GetCustomAttributes(false);
                var jsonProperty = attributes.OfType<JsonPropertyAttribute>();

                //this isn't my preferred way, but we just need to make sure the property we are sending to Cosmos is "id" and doesn't clash with our "Id" properties.
                //CosmosDbBase will only ever have *ONE* JsonPropertyAttribute on *ONE* of it's properties(CosmosId)
                if (jsonProperty.Any())
                {
                    expando.TryAdd("id", prop.GetValue(cosmosBase));
                }
                else
                {
                    expando.TryAdd(prop.Name, prop.GetValue(cosmosBase));
                }

            }

            return expando;
        }
    }
}
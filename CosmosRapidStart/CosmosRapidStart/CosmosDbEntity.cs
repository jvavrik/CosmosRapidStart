
namespace PrimeConsulting.CosmosRapidStart
{
    public class CosmosDbEntity<T>
    {
        public T Entity { get; set; }
        public CosmosDbBase CosmosDbBase { get; set; }
    }
}

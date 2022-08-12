using Amazon.DynamoDBv2.DataModel;

namespace CardGamesServer.Tests.Helpers
{
    /// <summary>
    /// Use wrapped version of DbContext for the QueryAsync method, because the AsyncSearch
    /// return type cannot be mocked out for unit testing
    /// </summary>
    public interface IWrappedDbContext
    {
        IDynamoDBContext Context { get; }

        IAsyncSearch<T> QueryAsync<T>(object hashKeyValue, DynamoDBOperationConfig operationConfig = null);
    }

    /// <summary>
    /// Use wrapped version of DbContext for the QueryAsync method, because the AsyncSearch
    /// return type cannot be mocked out for unit testing
    /// </summary>
    public sealed class WrappedDbContext : IWrappedDbContext
    {
        private readonly IDynamoDBContext context;

        public WrappedDbContext(IDynamoDBContext context)
        {
            this.context = context;
        }

        public IDynamoDBContext Context { get; }

        public IAsyncSearch<T> QueryAsync<T>(object hashKeyValue, DynamoDBOperationConfig operationConfig = null)
        {
            var search = this.context.QueryAsync<T>(hashKeyValue, operationConfig);
            return new AsyncSearchWrapper<T>(search);
        }
    }
}

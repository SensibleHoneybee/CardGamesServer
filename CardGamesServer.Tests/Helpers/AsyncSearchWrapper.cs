using Amazon.DynamoDBv2.DataModel;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CardGamesServer.Tests.Helpers
{
    public interface IAsyncSearch<T>
    {
        bool IsDone { get; }

        Task<IReadOnlyList<T>> GetNextSetAsync();

        Task<IReadOnlyList<T>> GetRemainingAsync();
    }

    public sealed class AsyncSearchWrapper<T> : IAsyncSearch<T>
    {
        private readonly AsyncSearch<T> asyncSearch;

        public AsyncSearchWrapper(AsyncSearch<T> asyncSearch)
        {
            this.asyncSearch = asyncSearch;
        }

        public bool IsDone => this.asyncSearch.IsDone;

        public async Task<IReadOnlyList<T>> GetNextSetAsync()
        {
            return await this.asyncSearch.GetNextSetAsync();
        }

        public async Task<IReadOnlyList<T>> GetRemainingAsync()
        {
            return await this.asyncSearch.GetRemainingAsync();
        }
    }
}

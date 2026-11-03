using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Caching;
using StackExchange.Redis;

namespace UnitTest.Platform;

/// <summary>02-02: cache-aside de los catalogos y degradacion cuando Redis no responde (RNF-15).</summary>
public sealed class CachingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AppCache Cache(IDistributedCache? store = null) =>
        new(store ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<AppCache>.Instance);

    /// <summary>Categorias que cuentan cuantas veces se leyo la fuente.</summary>
    private sealed class CountingCategories : ICategoryRepository
    {
        private readonly InMemoryCategoryRepository inner = new();

        public int Reads { get; private set; }

        public Task<IReadOnlyList<CategoryDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken)
        {
            Reads++;
            return inner.ListActiveAsync(communityId, cancellationToken);
        }
    }

    /// <summary>Redis caido: cada operacion falla como lo hace StackExchange.Redis.</summary>
    private sealed class DownCache : IDistributedCache
    {
        private static RedisException Down() => new("Redis no responde");

        public byte[]? Get(string key) => throw Down();

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();

        public void Refresh(string key) => throw Down();

        public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();

        public void Remove(string key) => throw Down();

        public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();
    }

    [Fact]
    public async Task SecondRead_OfCategories_DoesNotTouchTheSource()
    {
        var source = new CountingCategories();
        var categories = new CachedCategoryRepository(source, Cache());

        var first = await categories.ListActiveAsync(InMemoryCommunityRepository.UtttId, Ct);
        var second = await categories.ListActiveAsync(InMemoryCommunityRepository.UtttId, Ct);

        Assert.Equal(1, source.Reads);
        Assert.Equal(first.Select(c => c.Slug), second.Select(c => c.Slug));
        Assert.Equal("Electrónica", second[^1].Name);
    }

    [Fact]
    public async Task EachCommunity_HasItsOwnEntry()
    {
        var source = new CountingCategories();
        var categories = new CachedCategoryRepository(source, Cache());

        await categories.ListActiveAsync(InMemoryCommunityRepository.UtttId, Ct);
        var other = await categories.ListActiveAsync("000000000000000000000002", Ct);

        Assert.Equal(2, source.Reads);
        Assert.Empty(other);
    }

    [Fact]
    public async Task RedisDown_ReadsTheSource_AndNeverFails()
    {
        var source = new CountingCategories();
        var categories = new CachedCategoryRepository(source, Cache(new DownCache()));

        var first = await categories.ListActiveAsync(InMemoryCommunityRepository.UtttId, Ct);
        await categories.ListActiveAsync(InMemoryCommunityRepository.UtttId, Ct);
        await Cache(new DownCache()).RemoveAsync("categories:x", Ct);

        Assert.Equal(6, first.Count);
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public async Task SourceFailure_IsNotHiddenByTheCache()
    {
        var cache = Cache();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync<string>("x", TimeSpan.FromMinutes(1), _ => throw new InvalidOperationException("MongoDB caido"), Ct));
    }

    [Fact]
    public async Task Remove_ForcesTheNextReadToTheSource()
    {
        var cache = Cache();
        var reads = 0;
        Task<int> Read(CancellationToken _) => Task.FromResult(++reads);

        await cache.GetOrCreateAsync("n", TimeSpan.FromMinutes(1), Read, Ct);
        await cache.RemoveAsync("n", Ct);
        var again = await cache.GetOrCreateAsync("n", TimeSpan.FromMinutes(1), Read, Ct);

        Assert.Equal(2, again);
    }
}

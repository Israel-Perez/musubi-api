using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.TestShared.Listings;

namespace UnitTest.Data;

/// <summary>HU-06: los fakes en memoria cumplen el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryListingRepositoryTests : ListingRepositoryContract
{
    internal override ICategoryRepository Categories { get; } = new InMemoryCategoryRepository();

    internal override IProductRepository Products { get; } = new InMemoryProductRepository();

    internal override Task<string> UtttIdAsync() => Task.FromResult(InMemoryCommunityRepository.UtttId);
}

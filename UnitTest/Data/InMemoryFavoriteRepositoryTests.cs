using Musubi.Api.Modules.Listings.Data;
using Musubi.TestShared.Listings;

namespace UnitTest.Data;

/// <summary>HU-53: el fake en memoria cumple el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryFavoriteRepositoryTests : FavoriteRepositoryContract
{
    internal override IFavoriteRepository Favorites { get; } = new InMemoryFavoriteRepository();
}

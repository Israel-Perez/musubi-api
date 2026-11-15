using Musubi.Api.Modules.Listings.Data;
using Musubi.TestShared.Listings;

namespace UnitTest.Data;

/// <summary>HU-12: el fake en memoria cumple el mismo contrato que MongoDB.</summary>
public sealed class InMemorySavedSearchRepositoryTests : SavedSearchRepositoryContract
{
    internal override ISavedSearchRepository Searches { get; } = new InMemorySavedSearchRepository();
}

using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared.Reputation;

namespace UnitTest.Data;

/// <summary>HU-27/HU-45: el fake en memoria cumple el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryRatingRepositoryTests : RatingRepositoryContract
{
    internal override IRatingRepository Ratings { get; } = new InMemoryRatingRepository();
}

using Musubi.Api.Modules.Matching.Data;
using Musubi.TestShared.Matching;

namespace UnitTest.Data;

/// <summary>RF-25: el fake en memoria cumple el mismo contrato que MongoDB.</summary>
public sealed class InMemoryMatchRepositoryTests : MatchRepositoryContract
{
    private readonly InMemoryMatchRepository matches = new();

    internal override IMatchRepository Matches => matches;
}

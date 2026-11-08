using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared.Reputation;

namespace UnitTest.Data;

/// <summary>HU-28 y HU-57: los fakes en memoria cumplen el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryReputationRepositoryTests : ReputationRepositoryContract
{
    internal override IReportRepository Reports { get; } = new InMemoryReportRepository();

    internal override IBlockRepository Blocks { get; } = new InMemoryBlockRepository();
}

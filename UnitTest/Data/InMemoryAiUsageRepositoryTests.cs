using Musubi.Api.Modules.GenAi.Data;
using Musubi.TestShared.GenAi;

namespace UnitTest.Data;

/// <summary>HU-42: el fake en memoria cumple el mismo contrato que MongoDB.</summary>
public sealed class InMemoryAiUsageRepositoryTests : AiUsageRepositoryContract
{
    internal override IAiUsageRepository Usage { get; } = new InMemoryAiUsageRepository();
}

using Musubi.Api.Modules.Recommendations.Data;
using Musubi.TestShared.Recommendations;

namespace UnitTest.Data;

/// <summary>HU-35: el repositorio en memoria cumple el contrato.</summary>
public sealed class InMemoryInteractionRepositoryTests : InteractionRepositoryContract
{
    internal override IInteractionRepository Interactions { get; } = new InMemoryInteractionRepository();
}

using Musubi.Api.Modules.Matching.Data;
using Musubi.TestShared.Matching;

namespace UnitTest.Data;

/// <summary>HU-60: el fake en memoria cumple el mismo contrato que MongoDB.</summary>
public sealed class InMemoryEmbeddingRepositoryTests : EmbeddingRepositoryContract
{
    internal override IEmbeddingRepository Embeddings { get; } = new InMemoryEmbeddingRepository();
}

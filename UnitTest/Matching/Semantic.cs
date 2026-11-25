using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.Matching.Data;

namespace UnitTest.Matching;

/// <summary>HU-60: el comparador por significado para las pruebas de M-06.</summary>
internal static class Semantic
{
    /// <summary>Sin proveedor: deciden las palabras, como antes de HU-60 (RNF-15).</summary>
    public static SemanticMatcher Off() => new(new OffTextEmbedder(), new InMemoryEmbeddingRepository(), TimeProvider.System, NullLogger<SemanticMatcher>.Instance);

    /// <summary>Con la IA en memoria (los conceptos de <see cref="InMemoryTextEmbedder"/>).</summary>
    /// <param name="embeddings">Donde se guardan los vectores.</param>
    /// <param name="embedder">El proveedor (en memoria si no se da).</param>
    /// <param name="clock">El reloj (el del sistema si no se da).</param>
    /// <returns>El comparador.</returns>
    public static SemanticMatcher On(IEmbeddingRepository? embeddings = null, ITextEmbedder? embedder = null, TimeProvider? clock = null) =>
        new(embedder ?? new InMemoryTextEmbedder(), embeddings ?? new InMemoryEmbeddingRepository(), clock ?? TimeProvider.System, NullLogger<SemanticMatcher>.Instance);
}

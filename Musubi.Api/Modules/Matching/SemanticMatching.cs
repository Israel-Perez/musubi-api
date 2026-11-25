using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.Matching.Data;

namespace Musubi.Api.Modules.Matching;

/// <summary>Una publicacion o una solicitud, con el texto que se compara.</summary>
/// <param name="Kind"><see cref="EmbeddingKinds"/>.</param>
/// <param name="Id">La publicacion o la solicitud.</param>
/// <param name="OwnerId">Quien la publico.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="Text">Su texto, ya sin datos de contacto (<see cref="Embeddings.Text"/>).</param>
internal sealed record MatchText(string Kind, string Id, string OwnerId, string CommunityId, string Text)
{
    /// <summary>El id de su vector.</summary>
    public string Key => EmbeddingDocument.KeyOf(Kind, Id);

    /// <summary>Busqueda (solicitud) o documento (publicacion).</summary>
    public EmbeddingRole Role => Kind == EmbeddingKinds.Wanted ? EmbeddingRole.Query : EmbeddingRole.Document;

    /// <summary>Una publicacion.</summary>
    /// <param name="id">Id.</param>
    /// <param name="sellerId">Quien la vende.</param>
    /// <param name="communityId">Campus.</param>
    /// <param name="title">Nombre.</param>
    /// <param name="description">Descripcion.</param>
    /// <returns>El texto.</returns>
    public static MatchText Product(string id, string sellerId, string communityId, string title, string? description) =>
        new(EmbeddingKinds.Product, id, sellerId, communityId, Embeddings.Text(title, description));

    /// <summary>Una solicitud "Se busca".</summary>
    /// <param name="id">Id.</param>
    /// <param name="requesterId">Quien busca.</param>
    /// <param name="communityId">Campus.</param>
    /// <param name="title">Lo que busca.</param>
    /// <returns>El texto.</returns>
    public static MatchText Wanted(string id, string requesterId, string communityId, string title) =>
        new(EmbeddingKinds.Wanted, id, requesterId, communityId, Embeddings.Text(title, null));
}

/// <summary>
/// HU-60: que tanto se parecen por significado una publicacion y una solicitud (similitud coseno de sus vectores). Los
/// vectores se guardan y solo se recalculan si cambia el texto o el modelo. En los workers de M-06 (segundo plano,
/// RNF-11) se calculan los que falten; en una consulta de la app solo se usan los guardados. Sin proveedor o si falla,
/// no hay similitud y las reglas por palabras deciden (RNF-15).
/// </summary>
internal sealed partial class SemanticMatcher(ITextEmbedder embedder, IEmbeddingRepository embeddings, TimeProvider time, ILogger<SemanticMatcher> logger)
    : IAccountDataEraser
{
    private static readonly IReadOnlyDictionary<string, double> None = new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>Si hay proveedor (sin el, las coincidencias son por palabras).</summary>
    public bool Enabled => embedder.Enabled;

    /// <summary>La similitud de <paramref name="anchor"/> con cada candidata.</summary>
    /// <param name="anchor">La publicacion o la solicitud de la que se buscan coincidencias.</param>
    /// <param name="candidates">Las candidatas.</param>
    /// <param name="computeMissing">Calcular los vectores que falten (solo en segundo plano).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Por id de candidata, su similitud; sin las que no tienen vector.</returns>
    public async Task<IReadOnlyDictionary<string, double>> SimilaritiesAsync(
        MatchText anchor,
        IReadOnlyList<MatchText> candidates,
        bool computeMissing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(candidates);
        if (!embedder.Enabled || candidates.Count == 0)
        {
            return None;
        }

        try
        {
            var vectors = await VectorsAsync([anchor, .. candidates], computeMissing, cancellationToken).ConfigureAwait(false);
            if (!vectors.TryGetValue(anchor.Key, out var target))
            {
                return None;
            }

            return candidates
                .Where(c => vectors.ContainsKey(c.Key))
                .GroupBy(c => c.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => Embeddings.Cosine(target, vectors[g.First().Key]), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La base o el proveedor fallaron: deciden las palabras.
            LogFailed(logger, ex.GetType().Name);
            return None;
        }
    }

    /// <summary>Calcula y guarda el vector de una publicacion o solicitud nueva o editada (segundo plano).</summary>
    /// <param name="item">El texto.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si quedo guardado.</returns>
    public async Task<bool> RememberAsync(MatchText item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!embedder.Enabled)
        {
            return false;
        }

        try
        {
            return (await VectorsAsync([item], computeMissing: true, cancellationToken).ConfigureAwait(false)).ContainsKey(item.Key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex.GetType().Name);
            return false;
        }
    }

    /// <inheritdoc />
    public Task EraseAsync(string userId, CancellationToken cancellationToken) => embeddings.DeleteByOwnerAsync(userId, cancellationToken);

    /// <summary>Huella del texto, el papel y el modelo.</summary>
    /// <param name="item">El texto.</param>
    /// <param name="model">El modelo.</param>
    /// <returns>32 hex.</returns>
    public static string Hash(MatchText item, string model)
    {
        ArgumentNullException.ThrowIfNull(item);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{model}\n{item.Role}\n{item.Text}"));
        return Convert.ToHexStringLower(bytes)[..32];
    }

    private async Task<Dictionary<string, IReadOnlyList<float>>> VectorsAsync(IReadOnlyList<MatchText> items, bool computeMissing, CancellationToken cancellationToken)
    {
        var unique = items.GroupBy(i => i.Key, StringComparer.Ordinal).Select(g => g.First()).ToList();
        var hashes = unique.ToDictionary(i => i.Key, i => Hash(i, embedder.Model), StringComparer.Ordinal);
        var stored = await embeddings.FindAsync([.. hashes.Keys], cancellationToken).ConfigureAwait(false);
        var vectors = stored
            .Where(e => hashes.TryGetValue(e.Id, out var hash) && hash == e.TextHash && e.Vector.Count > 0)
            .ToDictionary(e => e.Id, e => e.Vector, StringComparer.Ordinal);
        if (!computeMissing)
        {
            return vectors;
        }

        // Una llamada por papel con todos los que faltan (Gemini acepta 100 por llamada).
        foreach (var group in unique.Where(i => !vectors.ContainsKey(i.Key) && i.Text.Length > 0).GroupBy(i => i.Role))
        {
            var missing = group.ToList();
            var computed = await embedder.EmbedAsync([.. missing.Select(i => i.Text)], group.Key, cancellationToken).ConfigureAwait(false);
            if (computed is null || computed.Count != missing.Count)
            {
                continue;
            }

            var now = time.GetUtcNow().UtcDateTime;
            for (var i = 0; i < missing.Count; i++)
            {
                var item = missing[i];
                vectors[item.Key] = computed[i];
                await embeddings.UpsertAsync(
                    new EmbeddingDocument
                    {
                        Id = item.Key,
                        Kind = item.Kind,
                        ItemId = item.Id,
                        CommunityId = item.CommunityId,
                        OwnerId = item.OwnerId,
                        TextHash = hashes[item.Key],
                        Vector = computed[i],
                        UpdatedAt = now,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return vectors;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se compararon las coincidencias por significado ({Error}): deciden las palabras")]
    private static partial void LogFailed(ILogger logger, string error);
}

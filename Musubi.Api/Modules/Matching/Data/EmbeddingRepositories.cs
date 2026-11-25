using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Matching.Data;

// HU-60: el vector de cada publicacion y cada solicitud (coleccion matchEmbeddings, db/035). Mongo* e InMemory* pasan
// la misma bateria (TestShared/Matching/EmbeddingRepositoryContract).

/// <summary>De que es un vector.</summary>
internal static class EmbeddingKinds
{
    /// <summary>Una publicacion.</summary>
    public const string Product = "product";

    /// <summary>Una solicitud "Se busca".</summary>
    public const string Wanted = "wanted";
}

/// <summary>
/// El vector de un texto publico del campus. <see cref="TextHash"/> dice de que texto y modelo salio: si la publicacion
/// cambia o se cambia el modelo, ya no sirve y se vuelve a calcular.
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class EmbeddingDocument
{
    /// <summary><c>{kind}:{id}</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary><see cref="EmbeddingKinds"/>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>La publicacion o la solicitud.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ItemId { get; init; } = string.Empty;

    /// <summary>Campus.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Quien la publico (para borrarlo con su cuenta, HU-46).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string OwnerId { get; init; } = string.Empty;

    /// <summary>Huella del texto y el modelo (SHA-256, 32 hex).</summary>
    public string TextHash { get; init; } = string.Empty;

    /// <summary>El vector.</summary>
    public IReadOnlyList<float> Vector { get; init; } = [];

    /// <summary>Cuando se calculo (UTC).</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>El id de un vector.</summary>
    /// <param name="kind"><see cref="EmbeddingKinds"/>.</param>
    /// <param name="itemId">La publicacion o la solicitud.</param>
    /// <returns><c>{kind}:{id}</c>.</returns>
    public static string KeyOf(string kind, string itemId) => $"{kind}:{itemId}";
}

/// <summary>Los vectores guardados.</summary>
internal interface IEmbeddingRepository
{
    /// <summary>Los vectores de varias publicaciones o solicitudes.</summary>
    /// <param name="keys">Ids <c>{kind}:{id}</c>.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los que existen.</returns>
    Task<IReadOnlyList<EmbeddingDocument>> FindAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);

    /// <summary>Guarda o reemplaza un vector.</summary>
    /// <param name="embedding">El vector.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task UpsertAsync(EmbeddingDocument embedding, CancellationToken cancellationToken);

    /// <summary>Borra los vectores de lo que publico una persona (HU-46).</summary>
    /// <param name="ownerId">Quien.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se borraron.</returns>
    Task<long> DeleteByOwnerAsync(string ownerId, CancellationToken cancellationToken);
}

/// <summary>Vectores en MongoDB (coleccion <c>matchEmbeddings</c>, db/035).</summary>
internal sealed class MongoEmbeddingRepository(IMongoDatabase database) : IEmbeddingRepository
{
    private readonly IMongoCollection<EmbeddingDocument> embeddings = database.GetCollection<EmbeddingDocument>(MongoSchema.MatchEmbeddings);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbeddingDocument>> FindAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return keys.Count == 0
            ? []
            : await embeddings.Find(Builders<EmbeddingDocument>.Filter.In(e => e.Id, keys)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task UpsertAsync(EmbeddingDocument embedding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        return embeddings.ReplaceOneAsync(e => e.Id == embedding.Id, embedding, new ReplaceOptions { IsUpsert = true }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> DeleteByOwnerAsync(string ownerId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(ownerId, out _)
            ? (await embeddings.DeleteManyAsync(e => e.OwnerId == ownerId, cancellationToken).ConfigureAwait(false)).DeletedCount
            : 0;
}

/// <summary>Vectores en memoria (Development y pruebas).</summary>
internal sealed class InMemoryEmbeddingRepository : IEmbeddingRepository
{
    private readonly Lock gate = new();

    private readonly Dictionary<string, EmbeddingDocument> embeddings = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<IReadOnlyList<EmbeddingDocument>> FindAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<EmbeddingDocument>>([.. keys.Distinct(StringComparer.Ordinal).Where(embeddings.ContainsKey).Select(k => embeddings[k])]);
        }
    }

    /// <inheritdoc />
    public Task UpsertAsync(EmbeddingDocument embedding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        lock (gate)
        {
            embeddings[embedding.Id] = embedding;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> DeleteByOwnerAsync(string ownerId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var gone = embeddings.Values.Where(e => e.OwnerId == ownerId).Select(e => e.Id).ToList();
            foreach (var id in gone)
            {
                embeddings.Remove(id);
            }

            return Task.FromResult((long)gone.Count);
        }
    }
}

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

namespace Musubi.Api.Modules.GenAi.Data;

// HU-42 (indicador "Uso y precision de la IA"): el acuerdo se borra al crear el pedido, al descartarlo o a los 30 dias
// (RN-44), asi que cuantos resumenes se pidieron y cuantos se confirmaron sin correcciones se anotan aparte. Sin
// conversacion, sin personas y sin el texto: solo el campus, que paso y cuando (coleccion aiUsage, db/036).

/// <summary>Que se anota.</summary>
internal static class AiUsageKinds
{
    /// <summary>Alguien pidio "Resumir acuerdo".</summary>
    public const string Summary = "summary";

    /// <summary>Los dos confirmaron el acuerdo.</summary>
    public const string Confirmed = "confirmed";
}

/// <summary>Un uso de la IA del acuerdo, sin datos de nadie.</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class AiUsageDocument
{
    /// <summary>Id.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Campus.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary><see cref="AiUsageKinds"/>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Solo al confirmar: si la propuesta se corrigio antes (revision mayor que 1).</summary>
    public bool Corrected { get; init; }

    /// <summary>Cuando (UTC).</summary>
    public DateTime At { get; init; }

    /// <summary>Cuando se borra solo (TTL de un ano).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>HU-42: el uso de la IA en un periodo.</summary>
/// <param name="Summaries">Resumenes pedidos.</param>
/// <param name="Confirmed">Acuerdos confirmados por los dos.</param>
/// <param name="Uncorrected">De esos, los que se confirmaron tal como los propuso la IA.</param>
internal sealed record AiUsage(long Summaries, long Confirmed, long Uncorrected);

/// <summary>El registro del uso de la IA.</summary>
internal interface IAiUsageRepository
{
    /// <summary>Anota un uso.</summary>
    /// <param name="usage">El uso.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task RecordAsync(AiUsageDocument usage, CancellationToken cancellationToken);

    /// <summary>El uso de un campus en el periodo.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="from">Inicio (UTC, inclusivo).</param>
    /// <param name="to">Fin (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los conteos.</returns>
    Task<AiUsage> UsageAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken);
}

/// <summary>En MongoDB (coleccion <c>aiUsage</c>, db/036).</summary>
internal sealed class MongoAiUsageRepository(IMongoDatabase database) : IAiUsageRepository
{
    private static readonly FilterDefinitionBuilder<AiUsageDocument> Filter = Builders<AiUsageDocument>.Filter;

    private readonly IMongoCollection<AiUsageDocument> usages = database.GetCollection<AiUsageDocument>(MongoSchema.AiUsage);

    /// <inheritdoc />
    public Task RecordAsync(AiUsageDocument usage, CancellationToken cancellationToken) =>
        usages.InsertOneAsync(usage, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<AiUsage> UsageAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(communityId, out _))
        {
            return new AiUsage(0, 0, 0);
        }

        var inPeriod = Filter.Eq(u => u.CommunityId, communityId) & Filter.Gte(u => u.At, from) & Filter.Lt(u => u.At, to);
        var summaries = await usages.CountDocumentsAsync(inPeriod & Filter.Eq(u => u.Kind, AiUsageKinds.Summary), cancellationToken: cancellationToken).ConfigureAwait(false);
        var confirmed = await usages.CountDocumentsAsync(inPeriod & Filter.Eq(u => u.Kind, AiUsageKinds.Confirmed), cancellationToken: cancellationToken).ConfigureAwait(false);
        var uncorrected = await usages.CountDocumentsAsync(
            inPeriod & Filter.Eq(u => u.Kind, AiUsageKinds.Confirmed) & Filter.Eq(u => u.Corrected, false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new AiUsage(summaries, confirmed, uncorrected);
    }
}

/// <summary>En memoria (Development y pruebas).</summary>
internal sealed class InMemoryAiUsageRepository : IAiUsageRepository
{
    private readonly Lock gate = new();

    private readonly List<AiUsageDocument> usages = [];

    /// <inheritdoc />
    public Task RecordAsync(AiUsageDocument usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usage);
        lock (gate)
        {
            usages.Add(usage);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<AiUsage> UsageAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var mine = usages.Where(u => u.CommunityId == communityId && u.At >= from && u.At < to).ToList();
            var confirmed = mine.Where(u => u.Kind == AiUsageKinds.Confirmed).ToList();
            return Task.FromResult(new AiUsage(mine.Count(u => u.Kind == AiUsageKinds.Summary), confirmed.Count, confirmed.Count(u => !u.Corrected)));
        }
    }
}

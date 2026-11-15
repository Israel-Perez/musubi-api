using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Matching.Data;

namespace Musubi.Api.Modules.Matching;

/// <summary>
/// Contrato publico de M-06 (RF-25): registra cada coincidencia, a quien se aviso y si termino en pedido. Nunca falla
/// hacia quien lo llama: es una metrica, no parte del flujo.
/// </summary>
internal interface IMatchLedger
{
    /// <summary>Registra una coincidencia.</summary>
    /// <param name="match">La coincidencia.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task RecordAsync(MatchRecord match, CancellationToken cancellationToken);

    /// <summary>Quien compra hizo un pedido de esa publicacion (M-08): sus coincidencias terminaron en pedido.</summary>
    /// <param name="buyerId">Quien compra.</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task OrderedAsync(string buyerId, string productId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre <see cref="IMatchRepository"/>; tambien borra las de una cuenta (HU-46).</summary>
internal sealed partial class MatchLedger(IMatchRepository matches, TimeProvider time, ILogger<MatchLedger> logger) : IMatchLedger, IAccountDataEraser
{
    /// <summary>Cuanto se guarda una coincidencia.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    /// <inheritdoc />
    public async Task RecordAsync(MatchRecord match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        var now = time.GetUtcNow().UtcDateTime;
        try
        {
            await matches.RecordAsync(match, now, now + Retention, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            LogNotRecorded(logger, ex.GetType().Name);
        }
    }

    /// <inheritdoc />
    public async Task OrderedAsync(string buyerId, string productId, CancellationToken cancellationToken)
    {
        try
        {
            await matches.MarkOrderedAsync(buyerId, productId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            LogNotRecorded(logger, ex.GetType().Name);
        }
    }

    /// <inheritdoc />
    public Task EraseAsync(string userId, CancellationToken cancellationToken) => matches.DeleteByUserAsync(userId, cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se registro una coincidencia ({Error})")]
    private static partial void LogNotRecorded(ILogger logger, string error);
}

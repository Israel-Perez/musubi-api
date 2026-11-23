using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Recommendations;

/// <summary>"¿Cuánto pido?" al publicar (HU-58, <c>Vender-3</c>).</summary>
/// <param name="Title">Nombre que lleva la publicacion.</param>
/// <param name="Category">Slug de la categoria.</param>
/// <param name="Condition"><c>new</c> o <c>used</c>.</param>
/// <param name="ConditionDetail">Solo usado: <c>likeNew</c>, <c>good</c> o <c>fair</c>.</param>
public sealed record PriceSuggestionRequest(string? Title, string? Category, string? Condition, string? ConditionDetail);

/// <summary>"Precio sugerido: $X – $Y" (sin datos suficientes, los dos son <c>null</c>).</summary>
/// <param name="MinCents">Desde, o <c>null</c>.</param>
/// <param name="MaxCents">Hasta, o <c>null</c>.</param>
/// <param name="Samples">Precios de esa categoria en el campus.</param>
public sealed record PriceSuggestionResponse(long? MinCents, long? MaxCents, int Samples);

/// <summary>La estimacion de una publicacion y como queda su precio (HU-58, <c>Producto</c>).</summary>
/// <param name="MinCents">Desde, o <c>null</c> sin datos suficientes.</param>
/// <param name="MaxCents">Hasta, o <c>null</c>.</param>
/// <param name="Samples">Precios de esa categoria en el campus.</param>
/// <param name="Verdict"><c>good</c> (abajo del rango), <c>fair</c> (dentro) o <c>high</c> (arriba); <c>null</c> sin rango.</param>
public sealed record PriceEstimateResponse(long? MinCents, long? MaxCents, int Samples, string? Verdict);

/// <summary>Veredictos de un precio frente a la estimacion.</summary>
internal static class PriceVerdicts
{
    /// <summary>Abajo del rango.</summary>
    public const string Good = "good";

    /// <summary>Dentro del rango.</summary>
    public const string Fair = "fair";

    /// <summary>Arriba del rango.</summary>
    public const string High = "high";

    /// <summary>Como queda un precio.</summary>
    /// <param name="priceCents">El precio.</param>
    /// <param name="range">El rango.</param>
    /// <returns>El veredicto.</returns>
    public static string For(long priceCents, PriceRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (priceCents < range.MinCents)
        {
            return Good;
        }

        return priceCents > range.MaxCents ? High : Fair;
    }
}

/// <summary>Errores del precio sugerido (catalogo canonico: docs/errores.md).</summary>
internal static class PricingErrors
{
    /// <summary>Faltan el nombre (3 a 80), la categoria o el estado.</summary>
    public static readonly ApiError InvalidRequest = new(
        "recommendations.invalid_price_request", StatusCodes.Status400BadRequest, "Escribe el nombre, la categoría y el estado para sugerirte un precio.");
}

/// <summary>
/// Un modelo de precios por campus en memoria (HU-58). El primero se entrena al pedirlo; despues, cuando tiene mas de 30
/// minutos, se responde con el vigente y se vuelve a entrenar en segundo plano (RNF-11). Registra la medicion fuera de
/// linea de cada entrenamiento (MAE del modelo contra la mediana de la categoria).
/// </summary>
internal sealed partial class PriceModelStore(
    IPriceCatalog catalog,
    ISoldPrices sold,
    TimeProvider time,
    ILogger<PriceModelStore> logger)
{
    /// <summary>Cada cuanto se vuelve a entrenar.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(30);

    /// <summary>Publicaciones como maximo.</summary>
    public const int MaxListings = 2000;

    /// <summary>Pedidos completados como maximo.</summary>
    public const int MaxSold = 2000;

    private readonly ConcurrentDictionary<string, PriceModel> models = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);

    /// <summary>El modelo vigente del campus.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El modelo.</returns>
    public async Task<PriceModel> GetAsync(string communityId, CancellationToken cancellationToken)
    {
        if (models.TryGetValue(communityId, out var current))
        {
            if (time.GetUtcNow().UtcDateTime - current.TrainedAt >= Freshness)
            {
                RefreshInBackground(communityId);
            }

            return current;
        }

        var gate = gates.GetOrAdd(communityId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return models.TryGetValue(communityId, out var trained)
                ? trained
                : await TrainAsync(communityId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Entrena y guarda el modelo del campus.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El modelo.</returns>
    internal async Task<PriceModel> TrainAsync(string communityId, CancellationToken cancellationToken)
    {
        var samples = await SamplesAsync(communityId, cancellationToken).ConfigureAwait(false);
        var model = PriceModel.Train(samples, time.GetUtcNow().UtcDateTime, evaluate: true);
        if (model.Evaluation is { } e)
        {
            var paid = samples.Count(s => s.Sold);
            LogTrained(logger, model.SampleCount, paid, e.Test, e.ModelMae, e.BaselineMae, model.Spread);
        }

        models[communityId] = model;
        return model;
    }

    /// <summary>Lo publicado (disponible, agotado o vendido) y lo que se pago en los pedidos completados.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los precios.</returns>
    internal async Task<IReadOnlyList<PriceSample>> SamplesAsync(string communityId, CancellationToken cancellationToken)
    {
        var listed = await catalog.PricedAsync(communityId, MaxListings, cancellationToken).ConfigureAwait(false);
        var paid = await sold.RecentAsync(communityId, MaxSold, cancellationToken).ConfigureAwait(false);
        var known = listed.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var missing = paid.Select(p => p.ProductId).Where(id => !known.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var product in await catalog.ByIdsAsync(missing, cancellationToken).ConfigureAwait(false))
        {
            known[product.Id] = product;
        }

        // La clave es la publicacion: su precio publicado y sus ventas caen del mismo lado de la prueba fuera de linea.
        return
        [
            .. listed.Select(p => new PriceSample(p.Id, p.Title, p.CategorySlug, p.Condition, p.PriceCents, Sold: false)),
            .. paid.Where(p => known.ContainsKey(p.ProductId)).Select(p =>
            {
                var product = known[p.ProductId];
                return new PriceSample(product.Id, product.Title, product.CategorySlug, product.Condition, p.PriceCents, Sold: true);
            }),
        ];
    }

    private void RefreshInBackground(string communityId)
    {
        var gate = gates.GetOrAdd(communityId, _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await TrainAsync(communityId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException)
            {
                // Sin base se queda el modelo anterior; se intenta en la siguiente peticion.
                LogNotTrained(logger, ex.GetType().Name);
            }
            finally
            {
                gate.Release();
            }
        });
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Precios entrenados: {Samples} precios ({Sold} pagados), MAE ${ModelMae:0} contra ${BaselineMae:0} de la mediana con {Test} apartados; rango +-{Spread:0.00} (log)")]
    private static partial void LogTrained(ILogger logger, int samples, int sold, int test, double modelMae, double baselineMae, double spread);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Modelo de precios sin reentrenar ({Error})")]
    private static partial void LogNotTrained(ILogger logger, string error);
}

/// <summary>
/// "Precio sugerido" al publicar o editar y la estimacion en <c>Producto</c> (HU-58). Es solo una sugerencia: nunca cambia
/// un precio (RN-38).
/// </summary>
internal sealed class PriceSuggestionService(PriceModelStore store, IPriceCatalog catalog)
{
    /// <summary>Caracteres del nombre, como al publicar.</summary>
    public const int MinTitle = 3;

    /// <summary>Caracteres del nombre, como al publicar.</summary>
    public const int MaxTitle = 80;

    /// <summary>El rango para lo que se va a publicar.</summary>
    /// <param name="communityId">Campus de quien publica (del token).</param>
    /// <param name="body">Nombre, categoria y estado.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El rango (o sin rango) o el error.</returns>
    public async Task<Result<PriceSuggestionResponse>> SuggestAsync(string communityId, PriceSuggestionRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var title = (body.Title ?? string.Empty).Trim();
        var category = (body.Category ?? string.Empty).Trim();
        if (title.Length is < MinTitle or > MaxTitle
            || category.Length == 0
            || body.Condition is not (ProductConditions.New or ProductConditions.Used))
        {
            return PricingErrors.InvalidRequest;
        }

        var model = await store.GetAsync(communityId, cancellationToken).ConfigureAwait(false);
        var range = model.Estimate(title, category, PriceCatalog.ConditionKey(body.Condition, body.ConditionDetail));
        return Result<PriceSuggestionResponse>.Ok(range is null
            ? new PriceSuggestionResponse(null, null, model.SampleCount)
            : new PriceSuggestionResponse(range.MinCents, range.MaxCents, range.Samples));
    }

    /// <summary>La estimacion de una publicacion que quien consulta puede ver, con su veredicto.</summary>
    /// <param name="userId">Quien consulta.</param>
    /// <param name="communityId">Su campus.</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La estimacion o <c>listings.product_not_found</c>.</returns>
    public async Task<Result<PriceEstimateResponse>> EstimateAsync(string userId, string communityId, string productId, CancellationToken cancellationToken)
    {
        if (await catalog.FindVisibleAsync(userId, communityId, productId, cancellationToken).ConfigureAwait(false) is not { } product)
        {
            return ListingsErrors.ProductNotFound;
        }

        var model = await store.GetAsync(product.CommunityId, cancellationToken).ConfigureAwait(false);
        var range = model.Estimate(product.Title, product.CategorySlug, product.Condition);
        return Result<PriceEstimateResponse>.Ok(range is null
            ? new PriceEstimateResponse(null, null, model.SampleCount, null)
            : new PriceEstimateResponse(range.MinCents, range.MaxCents, range.Samples, PriceVerdicts.For(product.PriceCents, range)));
    }
}

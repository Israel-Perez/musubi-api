using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Recommendations.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Recommendations;

/// <summary>Recomendaciones (HU-35).</summary>
/// <param name="Items">Publicaciones, de la mas a la menos recomendada.</param>
/// <param name="Basis">
/// <c>personal</c> (segun lo que la persona vio, guardo, escribio y compro), <c>general</c> (sin historial: lo mas
/// activo del campus y lo mas nuevo, RN-46) o <c>similar</c> ("Ver parecidos").
/// </param>
public sealed record RecommendationPage(IReadOnlyList<ProductResponse> Items, string Basis);

/// <summary>Bases de una recomendacion.</summary>
internal static class RecommendationBasis
{
    /// <summary>Segun el historial de la persona.</summary>
    public const string Personal = "personal";

    /// <summary>Sin historial (RN-46).</summary>
    public const string General = "general";

    /// <summary>"Ver parecidos".</summary>
    public const string Similar = "similar";
}

/// <summary>
/// Un modelo por campus en memoria, que se vuelve a entrenar cuando tiene mas de 30 minutos (cada instancia entrena el
/// suyo; con el volumen de un campus tarda poco). Registra la evaluacion de cada entrenamiento (observabilidad, L-16i).
/// </summary>
internal sealed partial class RecommendationModelStore(
    IRecommendationCatalog catalog,
    IInteractionRepository interactions,
    TimeProvider time,
    ILogger<RecommendationModelStore> logger)
{
    /// <summary>Cada cuanto se vuelve a entrenar.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(30);

    /// <summary>Publicaciones del catalogo como maximo.</summary>
    public const int MaxCatalog = 2000;

    /// <summary>Interacciones del campus como maximo para entrenar.</summary>
    public const int MaxInteractions = 20000;

    private readonly ConcurrentDictionary<string, RecommendationModel> models = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);

    /// <summary>El modelo vigente del campus (lo entrena si no hay o ya vencio).</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El modelo.</returns>
    public async Task<RecommendationModel> GetAsync(string communityId, CancellationToken cancellationToken)
    {
        if (Fresh(communityId) is { } fresh)
        {
            return fresh;
        }

        var gate = gates.GetOrAdd(communityId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Fresh(communityId) is { } trained)
            {
                return trained;
            }

            var now = time.GetUtcNow().UtcDateTime;
            var items = await catalog.AvailableAsync(communityId, MaxCatalog, cancellationToken).ConfigureAwait(false);
            var history = await interactions.FindByCommunityAsync(communityId, now - InteractionRecorder.Retention, MaxInteractions, cancellationToken)
                .ConfigureAwait(false);
            var model = RecommendationModel.Train(communityId, items, history, now, evaluate: true);
            if (model.Evaluation is { } evaluation)
            {
                LogTrained(logger, items.Count, history.Count, evaluation.K, evaluation.HitRate, evaluation.RandomBaseline, evaluation.Users, model.UsesCollaborative);
            }

            models[communityId] = model;
            return model;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Olvida el modelo de un campus (las pruebas lo usan para ver un cambio al momento).</summary>
    /// <param name="communityId">Campus.</param>
    public void Invalidate(string communityId) => models.TryRemove(communityId, out _);

    private RecommendationModel? Fresh(string communityId) =>
        models.TryGetValue(communityId, out var model) && time.GetUtcNow().UtcDateTime - model.TrainedAt < Freshness ? model : null;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Recomendaciones entrenadas: {Items} publicaciones, {Interactions} interacciones, hit@{K} = {HitRate:0.00} (al azar {Baseline:0.00}) con {Users} personas, colaborativo = {Collaborative}")]
    private static partial void LogTrained(ILogger logger, int items, int interactions, int k, double hitRate, double baseline, int users, bool collaborative);
}

/// <summary>
/// "Recomendado para ti" en <c>Inicio</c> y "Ver parecidos" en <c>Producto</c> (HU-35, decisiones de la usuaria del
/// 13-Nov-2026). Nunca se recomienda lo propio, lo que ya no esta disponible ni lo de personas bloqueadas (RN-45, HU-57).
/// </summary>
internal sealed class RecommendationService(
    IRecommendationCatalog catalog,
    IInteractionRepository interactions,
    IBlockDirectory blocks,
    RecommendationModelStore store,
    TimeProvider time)
{
    /// <summary>Recomendaciones si no se pide otro numero.</summary>
    public const int DefaultLimit = 10;

    /// <summary>Recomendaciones como maximo.</summary>
    public const int MaxLimit = 20;

    /// <summary>Interacciones recientes de la persona que forman su perfil.</summary>
    public const int HistoryLimit = 200;

    /// <summary>"Recomendado para ti".</summary>
    /// <param name="userId">Quien (del token).</param>
    /// <param name="communityId">Su campus (del token).</param>
    /// <param name="limit">De 1 a 20 (10 si no se manda).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las recomendaciones.</returns>
    public async Task<RecommendationPage> ForYouAsync(string userId, string communityId, int? limit, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var model = await store.GetAsync(communityId, cancellationToken).ConfigureAwait(false);
        var hidden = await blocks.HiddenForAsync(userId, cancellationToken).ConfigureAwait(false);
        var history = await interactions.FindByUserAsync(userId, HistoryLimit, cancellationToken).ConfigureAwait(false);
        var seen = history.Select(h => h.ProductId).ToHashSet(StringComparer.Ordinal);
        bool Allowed(CatalogItem item) => !string.Equals(item.SellerId, userId, StringComparison.Ordinal) && !hidden.Contains(item.SellerId);

        var ids = model.ForUser(userId, history, Allowed, size, time.GetUtcNow().UtcDateTime).ToList();
        var basis = ids.Count > 0 ? RecommendationBasis.Personal : RecommendationBasis.General;
        if (ids.Count < size)
        {
            // RN-46: lo que falte se completa con lo mas activo y lo mas nuevo que aun no ha visto.
            var chosen = ids.ToHashSet(StringComparer.Ordinal);
            ids.AddRange(model.General(item => Allowed(item) && !seen.Contains(item.Id) && !chosen.Contains(item.Id), size - ids.Count));
        }

        return new RecommendationPage(await catalog.CardsAsync(ids, includeSeller: true, cancellationToken).ConfigureAwait(false), basis);
    }

    /// <summary>"Ver parecidos": con las mismas reglas de visibilidad del detalle (HU-11).</summary>
    /// <param name="viewerId">Quien consulta, o <c>null</c> sin sesion.</param>
    /// <param name="viewerCommunityId">Su campus, o <c>null</c> sin sesion.</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="limit">De 1 a 20 (10 si no se manda).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las parecidas o <c>listings.product_not_found</c>.</returns>
    public async Task<Result<RecommendationPage>> SimilarAsync(
        string? viewerId,
        string? viewerCommunityId,
        string productId,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (await catalog.FindVisibleAsync(viewerId, viewerCommunityId, productId, cancellationToken).ConfigureAwait(false) is not { } target)
        {
            return ListingsErrors.ProductNotFound;
        }

        var size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var model = await store.GetAsync(target.CommunityId, cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<string> hidden = viewerId is null ? [] : await blocks.HiddenForAsync(viewerId, cancellationToken).ConfigureAwait(false);
        bool Allowed(CatalogItem item) => !string.Equals(item.SellerId, viewerId, StringComparison.Ordinal) && !hidden.Contains(item.SellerId);
        var ids = model.Similar(target, Allowed, size);
        return Result<RecommendationPage>.Ok(new RecommendationPage(
            await catalog.CardsAsync(ids, includeSeller: viewerCommunityId is not null, cancellationToken).ConfigureAwait(false),
            RecommendationBasis.Similar));
    }
}

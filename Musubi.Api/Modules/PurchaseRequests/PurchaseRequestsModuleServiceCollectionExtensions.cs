using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Platform.Caching;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>Registro DI del modulo M-05 (Solicitudes de compra).</summary>
internal static class PurchaseRequestsModuleServiceCollectionExtensions
{
    /// <summary>Registra el repositorio (Mongo o en memoria, lazy), el servicio y el borrado de la cuenta.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddPurchaseRequestsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var requests = new InMemoryWantedRepository();
        services.AddSingleton<IWantedRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? requests
            : new MongoWantedRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<WantedService>();
        services.AddSingleton<IAccountDataEraser, WantedAccountEraser>();

        // HU-16: ofertas "Yo tengo este producto".
        var offers = new InMemoryOfferRepository();
        services.AddSingleton<IOfferRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? offers
            : new MongoOfferRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<OfferService>();
        services.AddSingleton<IAccountDataEraser, OfferAccountEraser>();
        services.AddSingleton<IListingPhotoReferences, OfferPhotoReferences>();

        // HU-23: el pedido sale de una oferta aceptada.
        services.AddSingleton<IOfferDirectory, OfferDirectory>();
        services.AddSingleton<IWantedReminders, WantedReminders>();
    }
}

/// <summary>Borra las solicitudes de una cuenta que se elimina (HU-46, R11) y las saca del tablero en cache (HU-15).</summary>
internal sealed class WantedAccountEraser(IWantedRepository requests, IOutputCacheStore outputCache) : IAccountDataEraser
{
    /// <inheritdoc />
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        await requests.DeleteByRequesterAsync(userId, cancellationToken).ConfigureAwait(false);
        await outputCache.EvictByTagAsync(CachingServiceCollectionExtensions.WantedBoardTag, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Borra las ofertas que hizo una cuenta y las que recibio en sus solicitudes (HU-46, R11).</summary>
internal sealed class OfferAccountEraser(IOfferRepository offers) : IAccountDataEraser
{
    /// <inheritdoc />
    public Task EraseAsync(string userId, CancellationToken cancellationToken) =>
        offers.DeleteByPersonAsync(userId, cancellationToken);
}

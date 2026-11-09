using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.PurchaseRequests.Data;
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
    }
}

/// <summary>Borra las solicitudes de una cuenta que se elimina (HU-46, R11).</summary>
internal sealed class WantedAccountEraser(IWantedRepository requests) : IAccountDataEraser
{
    /// <inheritdoc />
    public Task EraseAsync(string userId, CancellationToken cancellationToken) =>
        requests.DeleteByRequesterAsync(userId, cancellationToken);
}

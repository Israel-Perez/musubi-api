using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Matching.Data;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Matching;

/// <summary>
/// Registro de M-06 (coincidencias). HU-19: al publicar una solicitud, avisa en segundo plano a quien tenga una
/// publicacion compatible; HU-29: al publicar una publicacion, a quien tenga una solicitud compatible; HU-18: "Ya
/// coinciden con lo que buscas"; RF-25: el registro de cada coincidencia.
/// </summary>
internal static class MatchingModuleServiceCollectionExtensions
{
    /// <summary>Registra la cola, la busqueda y el worker.</summary>
    /// <param name="services">Contenedor.</param>
    /// <returns>El mismo contenedor.</returns>
    public static IServiceCollection AddMatchingModule(this IServiceCollection services)
    {
        var matches = new InMemoryMatchRepository();
        services.AddSingleton<IMatchRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? matches
            : new MongoMatchRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<MatchLedger>();
        services.AddSingleton<IMatchLedger>(provider => provider.GetRequiredService<MatchLedger>());
        services.AddSingleton<IAccountDataEraser>(provider => provider.GetRequiredService<MatchLedger>());
        services.AddSingleton<WantedMatchesService>();
        services.AddSingleton<ProductMatchQueue>();
        services.AddSingleton<IProductPublications>(provider => provider.GetRequiredService<ProductMatchQueue>());
        services.AddSingleton<ProductMatchNotifier>();
        services.AddHostedService<ProductMatchWorker>();
        services.AddSingleton<WantedMatchQueue>();
        services.AddSingleton<IWantedMatching>(provider => provider.GetRequiredService<WantedMatchQueue>());
        services.AddSingleton<WantedMatchNotifier>();
        services.AddHostedService<WantedMatchWorker>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace Musubi.Api.Modules.Matching;

/// <summary>
/// Registro de M-06 (coincidencias). HU-19: al publicar una solicitud, avisa en segundo plano a quien tenga una
/// publicacion compatible.
/// </summary>
internal static class MatchingModuleServiceCollectionExtensions
{
    /// <summary>Registra la cola, la busqueda y el worker.</summary>
    /// <param name="services">Contenedor.</param>
    /// <returns>El mismo contenedor.</returns>
    public static IServiceCollection AddMatchingModule(this IServiceCollection services)
    {
        services.AddSingleton<WantedMatchQueue>();
        services.AddSingleton<IWantedMatching>(provider => provider.GetRequiredService<WantedMatchQueue>());
        services.AddSingleton<WantedMatchNotifier>();
        services.AddHostedService<WantedMatchWorker>();
        return services;
    }
}

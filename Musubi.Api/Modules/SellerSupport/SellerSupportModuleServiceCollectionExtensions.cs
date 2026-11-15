using Microsoft.Extensions.DependencyInjection;

namespace Musubi.Api.Modules.SellerSupport;

/// <summary>
/// Registro de M-12 (apoyo a vendedores). Sin colecciones propias: lee lo de M-03 (publicaciones y vistas), M-06
/// (demanda), M-07 (conversaciones) y M-08 (pedidos) por sus contratos publicos, y avisa con M-09.
/// </summary>
internal static class SellerSupportModuleServiceCollectionExtensions
{
    /// <summary>Registra el servicio y el aviso de las 3 semanas.</summary>
    /// <param name="services">Contenedor.</param>
    /// <returns>El mismo contenedor.</returns>
    public static IServiceCollection AddSellerSupportModule(this IServiceCollection services)
    {
        services.AddSingleton<SellerSupportService>();
        services.AddSingleton<SlowListingJob>();
        services.AddHostedService<SlowListingWorker>();
        return services;
    }
}

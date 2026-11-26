using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;

namespace Musubi.Api.Platform.Hosting;

/// <summary>
/// Tope global de peticiones (SCRUM-125, OWASP API4), ademas de los topes por ruta de <c>/auth</c>, fotos, chat,
/// reportes e IA. Con sesion cuenta por persona (el <c>sub</c> del token); sin sesion, por IP. Asi una red
/// compartida (el wifi del campus sale a internet con una sola IP) no deja sin servicio a quien ya entro.
/// Corre despues de la autenticacion (<c>UseRateLimiter</c> va detras de <c>UseAuthentication</c>) y despues
/// de las cabeceras de CloudFront, asi la IP es la del cliente real. Las sondas de <c>/health</c> no cuentan.
/// </summary>
internal static class GlobalRateLimit
{
    /// <summary>Peticiones por ventana de una persona con sesion.</summary>
    public const string UserPermitLimitKey = "RateLimiting:Global:UserPermitLimit";

    /// <summary>Peticiones por ventana de una IP sin sesion.</summary>
    public const string AnonymousPermitLimitKey = "RateLimiting:Global:AnonymousPermitLimit";

    /// <summary>Largo de la ventana, en segundos.</summary>
    public const string WindowSecondsKey = "RateLimiting:Global:WindowSeconds";

    /// <summary>
    /// 300 por minuto por persona: abrir una pantalla pide de 3 a 10 rutas, asi que alcanza para uso intenso y
    /// corta un script. 600 por IP sin sesion, el doble, porque el campus comparte IP.
    /// </summary>
    public const int DefaultUserPermitLimit = 300;

    /// <summary>Peticiones por minuto de una IP sin sesion, por defecto.</summary>
    public const int DefaultAnonymousPermitLimit = 600;

    /// <summary>Registra el limitador global con los valores de <c>RateLimiting:Global</c>.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddMusubiGlobalRateLimit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, config) =>
        {
            var limits = new Limits(
                config.GetValue(UserPermitLimitKey, DefaultUserPermitLimit),
                config.GetValue(AnonymousPermitLimitKey, DefaultAnonymousPermitLimit),
                TimeSpan.FromSeconds(config.GetValue(WindowSecondsKey, 60)));
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => Partition(context, limits));
        });
    }

    /// <summary>La particion de una peticion: ninguna para <c>/health</c>, la persona o la IP.</summary>
    /// <param name="context">La peticion.</param>
    /// <param name="limits">Los topes.</param>
    /// <returns>La particion con su limitador.</returns>
    internal static RateLimitPartition<string> Partition(HttpContext context, Limits limits)
    {
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter("health");
        }

        var subject = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst(MusubiClaims.Subject)?.Value : null;
        return subject is null
            ? FixedWindow("ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "desconocido"), limits.AnonymousPermitLimit, limits.Window)
            : FixedWindow("user:" + subject, limits.UserPermitLimit, limits.Window);
    }

    private static RateLimitPartition<string> FixedWindow(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
        });

    /// <summary>Los topes del limitador global.</summary>
    /// <param name="UserPermitLimit">Peticiones por ventana de una persona con sesion.</param>
    /// <param name="AnonymousPermitLimit">Peticiones por ventana de una IP sin sesion.</param>
    /// <param name="Window">Largo de la ventana.</param>
    internal sealed record Limits(int UserPermitLimit, int AnonymousPermitLimit, TimeSpan Window);
}

using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Caching;
using StackExchange.Redis;

namespace Musubi.Api.Platform.Realtime;

/// <summary>
/// Tiempo real con SignalR (HU-20, RNF-10). Con 2 instancias detras del balanceador, un mensaje que llega
/// a la instancia A tiene que salir por la conexion que vive en la B: con <c>Cache:ConnectionString</c>
/// el backplane de Redis las une. Sin la cadena (local y pruebas) cada instancia entrega solo a sus
/// conexiones.
/// </summary>
internal static class RealtimeServiceCollectionExtensions
{
    /// <summary>Prefijo de las rutas de los hubs: solo ahi se acepta el token en la query.</summary>
    public const string HubsPrefix = "/hubs";

    /// <summary>Prefijo de los canales de Redis (no choca con las llaves de la cache, <c>musubi:</c>).</summary>
    public const string ChannelPrefix = "musubi-signalr";

    /// <summary>Registra SignalR, el backplane de Redis (si hay cadena) y la identidad de cada conexion.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    /// <param name="configuration">
    /// La configuracion. Se lee al registrar (no lazy, a diferencia de 00-04 regla 3): SignalR elige su
    /// HubLifetimeManager aqui. Las pruebas no configuran Redis y usan el de memoria.
    /// </param>
    public static void AddMusubiRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var signalR = services.AddSignalR(options =>
        {
            // La app solo escucha: los mensajes se envian por REST (validacion y moderacion en un solo lugar).
            options.MaximumReceiveMessageSize = 4 * 1024;
            options.EnableDetailedErrors = false;
        });

        var connection = configuration[CachingServiceCollectionExtensions.ConnectionStringKey];
        if (!string.IsNullOrWhiteSpace(connection))
        {
            signalR.AddStackExchangeRedis(connection, options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal(ChannelPrefix);
                // Si Redis no responde al arrancar, la API arranca igual y reintenta en segundo plano.
                options.Configuration.AbortOnConnectFail = false;
            });
        }

        services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();

        // Un WebSocket no lleva encabezados desde el cliente movil: el token viaja en ?access_token=, solo en /hubs.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Events ??= new JwtBearerEvents();
            var previous = options.Events.OnMessageReceived;
            options.Events.OnMessageReceived = async context =>
            {
                await previous(context).ConfigureAwait(false);
                if (context.Token is null && TokenFromQuery(context.HttpContext) is { } token)
                {
                    context.Token = token;
                }
            };
        });
    }

    /// <summary>El token de la query, solo en las rutas de los hubs.</summary>
    /// <param name="context">La peticion.</param>
    /// <returns>El token o <c>null</c>.</returns>
    internal static string? TokenFromQuery(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(HubsPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var token = context.Request.Query["access_token"].ToString();
        return string.IsNullOrEmpty(token) ? null : token;
    }
}

/// <summary>El usuario de una conexion es el <c>sub</c> del JWT (<c>MapInboundClaims = false</c>).</summary>
internal sealed class SubjectUserIdProvider : IUserIdProvider
{
    /// <inheritdoc />
    public string? GetUserId(HubConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.User?.FindFirst(MusubiClaims.Subject)?.Value;
    }
}

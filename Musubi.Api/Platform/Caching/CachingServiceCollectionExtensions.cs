using System;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Musubi.Api.Platform.Caching;

/// <summary>
/// Cache en 2 niveles de la API (02-02): datos (cache-aside, <see cref="AppCache"/>) y salida HTTP (output
/// caching de respuestas anonimas). El tercer nivel vive en musubi-app (ETag y 304).
/// </summary>
internal static class CachingServiceCollectionExtensions
{
    /// <summary>Clave de la cadena de Redis (Parameter Store <c>/musubi/prod/Cache/ConnectionString</c>).</summary>
    public const string ConnectionStringKey = "Cache:ConnectionString";

    /// <summary>Politica de salida de los catalogos (categorias): cambian poco.</summary>
    public const string CatalogPolicy = "catalog";

    /// <summary>Politica de salida de la vitrina del visitante: vida corta, se invalida al publicar.</summary>
    public const string ShowcasePolicy = "showcase";

    /// <summary>Etiqueta de las respuestas de la vitrina (se borran al publicar o eliminar una cuenta).</summary>
    public const string ShowcaseTag = "showcase";

    /// <summary>Vida de los catalogos en cache.</summary>
    public static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Vida de la vitrina en la cache de salida. Es por instancia: la otra instancia ve una publicacion nueva
    /// a mas tardar en este tiempo.
    /// </summary>
    public static readonly TimeSpan ShowcaseTtl = TimeSpan.FromSeconds(30);

    /// <summary>Registra la cache distribuida (lazy: las pruebas sobrescriben la configuracion) y la de salida.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddMusubiCaching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDistributedCache>(provider =>
        {
            var connection = provider.GetRequiredService<IConfiguration>()[ConnectionStringKey];
            if (string.IsNullOrWhiteSpace(connection))
            {
                return new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            }

            // Tiempos cortos: si Redis no responde, la peticion sigue por MongoDB sin esperar.
            var redis = ConfigurationOptions.Parse(connection);
            redis.AbortOnConnectFail = false;
            redis.ConnectTimeout = 2000;
            redis.SyncTimeout = 1000;
            redis.AsyncTimeout = 1000;
            return new RedisCache(Options.Create(new RedisCacheOptions { ConfigurationOptions = redis, InstanceName = "musubi:" }));
        });
        services.AddSingleton<AppCache>();

        // La politica base no guarda peticiones con sesion (Authorization) ni respuestas con cookies.
        services.AddOutputCache(options =>
        {
            options.AddPolicy(CatalogPolicy, policy => policy.Expire(CatalogTtl).SetVaryByQuery("campus"));
            options.AddPolicy(ShowcasePolicy, policy => policy.Expire(ShowcaseTtl).SetVaryByQuery("*").Tag(ShowcaseTag));
        });
    }
}

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Musubi.Api.Platform.Caching;

/// <summary>
/// Cache-aside sobre <see cref="IDistributedCache"/> (02-02): Redis compartido por las 2 instancias si hay
/// <c>Cache:ConnectionString</c>; si no, memoria de la instancia. Si la cache falla, se lee de la fuente y la
/// peticion nunca falla por la cache (RNF-15).
/// </summary>
internal sealed partial class AppCache(IDistributedCache cache, ILogger<AppCache> logger)
{
    /// <summary>Prefijo de version: cambiar la forma de lo guardado invalida las entradas viejas sin borrarlas.</summary>
    public const string KeyPrefix = "v1:";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>El valor guardado o, si no esta (o la cache no responde), el de <paramref name="factory"/>.</summary>
    /// <typeparam name="T">Tipo del valor (serializable a JSON).</typeparam>
    /// <param name="key">Llave sin prefijo (<c>categories:{communityId}</c>).</param>
    /// <param name="ttl">Vida de la entrada.</param>
    /// <param name="factory">Lectura de la fuente (MongoDB).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El valor.</returns>
    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var fullKey = KeyPrefix + key;
        try
        {
            var cached = await cache.GetAsync(fullKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return JsonSerializer.Deserialize<T>(cached, Json)!;
            }
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            LogUnavailable(logger, ex, fullKey);
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
            await cache.SetAsync(fullKey, JsonSerializer.SerializeToUtf8Bytes(value, Json), entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            LogUnavailable(logger, ex, fullKey);
        }

        return value;
    }

    /// <summary>Borra una entrada (al escribir lo que guarda). Si la cache no responde, vence sola con su TTL.</summary>
    /// <param name="key">Llave sin prefijo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(KeyPrefix + key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            LogUnavailable(logger, ex, KeyPrefix + key);
        }
    }

    // Solo las fallas de la cache se degradan; un error de la fuente (MongoDB) se propaga.
    private static bool IsCacheFailure(Exception ex) => ex is RedisException or TimeoutException or JsonException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cache no disponible ({Key}): se lee de la fuente")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string key);
}

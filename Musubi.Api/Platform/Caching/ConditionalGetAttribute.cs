using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Platform.Caching;

/// <summary>
/// GET condicional (02-02): agrega un <c>ETag</c> (hash del JSON) y <c>Cache-Control: no-cache</c>; si el
/// cliente manda el mismo <c>If-None-Match</c>, responde <c>304</c> sin cuerpo. La app reutiliza su copia y
/// no vuelve a bajar la lista. <c>no-cache</c> obliga a revalidar siempre: un cambio se ve al instante.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class ConditionalGetAttribute : Attribute, IAsyncResultFilter
{
    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method)
            && context.Result is ObjectResult { Value: not null } result
            && result.StatusCode is null or StatusCodes.Status200OK)
        {
            var json = context.HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
            var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(result.Value, result.Value.GetType(), json));
            var etag = $"\"{Convert.ToBase64String(hash, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_')}\"";
            var headers = context.HttpContext.Response.Headers;
            headers.ETag = etag;
            headers.CacheControl = "no-cache";
            var sent = request.Headers.IfNoneMatch.ToString();
            if (sent.Split(',').Select(t => t.Trim()).Any(t => t == etag || t == "W/" + etag))
            {
                context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
            }
        }

        await next().ConfigureAwait(false);
    }
}

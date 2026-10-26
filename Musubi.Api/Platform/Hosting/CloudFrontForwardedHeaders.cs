using System;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Musubi.Api.Platform.Hosting;

/// <summary>
/// Detras de CloudFront y el balanceador (02-05): toma la IP real del cliente y el esquema que vio CloudFront.
/// Sin esto, la API veria la IP del balanceador y el rate limit por IP seria uno solo para todos los usuarios.
/// Solo se activa con <c>Proxy:TrustCloudFrontHeaders</c>: el balanceador rechaza lo que no llega por CloudFront
/// (cabecera secreta) y las instancias solo aceptan trafico del balanceador, asi que nadie puede inventar estas cabeceras.
/// </summary>
internal static class CloudFrontForwardedHeaders
{
    /// <summary>Clave de configuracion que activa el middleware (solo en la nube).</summary>
    public const string TrustKey = "Proxy:TrustCloudFrontHeaders";

    /// <summary>IP y puerto del cliente: <c>198.51.100.10:46532</c> o <c>2001:db8::1:46532</c>.</summary>
    public const string ViewerAddressHeader = "CloudFront-Viewer-Address";

    /// <summary>Esquema con el que el cliente llego a CloudFront.</summary>
    public const string ForwardedProtoHeader = "CloudFront-Forwarded-Proto";

    /// <summary>Registra el middleware si la configuracion lo pide.</summary>
    /// <param name="app">La aplicacion.</param>
    public static void UseCloudFrontForwardedHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Configuration.GetValue<bool>(TrustKey))
        {
            return;
        }

        app.Use((context, next) =>
        {
            Apply(context);
            return next(context);
        });
    }

    /// <summary>Aplica las cabeceras de CloudFront a la peticion.</summary>
    /// <param name="context">La peticion.</param>
    internal static void Apply(HttpContext context)
    {
        var headers = context.Request.Headers;
        if (TryParseViewerAddress(headers[ViewerAddressHeader].ToString(), out var address))
        {
            context.Connection.RemoteIpAddress = address;
        }

        var proto = headers[ForwardedProtoHeader].ToString();
        if (string.Equals(proto, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            context.Request.Scheme = Uri.UriSchemeHttps;
        }
        else if (string.Equals(proto, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            context.Request.Scheme = Uri.UriSchemeHttp;
        }
    }

    /// <summary>Separa la IP del puerto (el puerto va despues del ultimo <c>:</c>, tambien en IPv6).</summary>
    /// <param name="value">Valor de la cabecera.</param>
    /// <param name="address">La IP, si es valida.</param>
    /// <returns><c>true</c> si hubo una IP valida.</returns>
    internal static bool TryParseViewerAddress(string? value, out IPAddress? address)
    {
        address = null;
        var colon = value?.LastIndexOf(':') ?? -1;
        return colon > 0 && IPAddress.TryParse(value.AsSpan(0, colon), out address);
    }
}

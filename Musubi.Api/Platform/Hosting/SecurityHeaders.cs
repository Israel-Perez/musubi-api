using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

namespace Musubi.Api.Platform.Hosting;

/// <summary>
/// Encabezados de seguridad en cada respuesta (02-04 requisito 5, SCRUM-123). En la nube CloudFront ya los agrega
/// (<c>Managed-SecurityHeadersPolicy</c>); la API los pone tambien, como defensa por si alguien llega sin pasar
/// por CloudFront. Lo encontro el DAST (OWASP ZAP 10021 y 90004).
/// </summary>
internal static class SecurityHeaders
{
    /// <summary>Valor de <c>X-Content-Type-Options</c>: el navegador no adivina el tipo del contenido.</summary>
    public const string NoSniff = "nosniff";

    /// <summary>
    /// Valor de <c>Cross-Origin-Resource-Policy</c>: ningun otro origen puede incrustar las respuestas (no aplica a
    /// las peticiones con CORS ni a la app nativa). ZAP 90004 solo acepta <c>same-origin</c>.
    /// </summary>
    public const string SameOrigin = "same-origin";

    /// <summary>Valor de <c>Referrer-Policy</c>: la API nunca manda de donde viene una peticion.</summary>
    public const string NoReferrer = "no-referrer";

    /// <summary>Agrega los encabezados justo antes de enviar cada respuesta (tambien errores y 304).</summary>
    /// <param name="app">La aplicacion.</param>
    public static void UseMusubiSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = NoSniff;
                headers["Cross-Origin-Resource-Policy"] = SameOrigin;
                headers["Referrer-Policy"] = NoReferrer;
                return Task.CompletedTask;
            });
            return next(context);
        });
    }
}

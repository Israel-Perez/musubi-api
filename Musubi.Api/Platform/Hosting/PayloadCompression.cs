using System;
using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Platform.Hosting;

/// <summary>
/// Compresion de las respuestas (SCRUM-120): Brotli o Gzip segun <c>Accept-Encoding</c>, tambien sobre HTTPS.
/// En la nube CloudFront ya comprime en el borde; la API comprime cuando el cliente se lo pide directo (el ALB o
/// un ambiente local). Las respuestas de <c>/auth</c> nunca se comprimen: llevan tokens y aceptan datos del
/// cliente, la combinacion que explota BREACH.
/// </summary>
internal static class PayloadCompression
{
    /// <summary>Tipos que se comprimen: los de ASP.NET Core mas <c>problem+json</c>.</summary>
    public static readonly string[] MimeTypes = [.. ResponseCompressionDefaults.MimeTypes, "application/problem+json"];

    /// <summary>Registra los proveedores con el nivel de <see cref="LevelKey"/>.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddMusubiCompression(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = MimeTypes;
        });
        // Decide por ruta, sin importar el esquema: IHttpsCompressionFeature solo aplica a peticiones HTTPS y detras
        // del balanceador la peticion puede llegar en HTTP.
        services.Replace(ServiceDescriptor.Singleton<IResponseCompressionProvider, SessionSafeCompressionProvider>());
        // Compression:Level (01-04): Optimal por defecto, el nivel medido en 02-10 (la vitrina de 20 baja de 9.5 KB a
        // 1.3 KB con Brotli sin costo medible; SmallestSize agrega ~18 ms). Lectura lazy (regla de AGENTS.md).
        services.AddOptions<BrotliCompressionProviderOptions>().Configure<IConfiguration>((options, config) => options.Level = Level(config));
        services.AddOptions<GzipCompressionProviderOptions>().Configure<IConfiguration>((options, config) => options.Level = Level(config));
    }

    /// <summary>Clave del nivel de compresion (<c>Fastest</c>, <c>Optimal</c> o <c>SmallestSize</c>).</summary>
    public const string LevelKey = "Compression:Level";

    /// <summary>El nivel configurado; <c>Optimal</c> si falta o no es valido.</summary>
    /// <param name="config">Configuracion.</param>
    /// <returns>El nivel.</returns>
    public static CompressionLevel Level(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Enum.TryParse<CompressionLevel>(config[LevelKey], ignoreCase: true, out var level) && Enum.IsDefined(level) && level != CompressionLevel.NoCompression
            ? level
            : CompressionLevel.Optimal;
    }

    /// <summary>Comprime las respuestas (las de sesion las descarta <see cref="SessionSafeCompressionProvider"/>).</summary>
    /// <param name="app">La aplicacion.</param>
    public static void UseMusubiCompression(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseResponseCompression();
    }

    /// <summary>Rutas de sesion (<c>/api/v{n}/auth/...</c>): login, refresh, registro, contrasenas.</summary>
    /// <param name="path">Ruta de la peticion.</param>
    /// <returns><c>true</c> si la respuesta no debe comprimirse.</returns>
    public static bool CarriesSecrets(PathString path)
    {
        var segments = (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1][0] is 'v' or 'V'
            && segments[2].Equals("auth", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Proveedor de ASP.NET Core que nunca comprime una respuesta de sesion (BREACH).</summary>
/// <param name="services">Contenedor (lo pide la clase base).</param>
/// <param name="options">Opciones de compresion.</param>
internal sealed class SessionSafeCompressionProvider(IServiceProvider services, IOptions<ResponseCompressionOptions> options)
    : ResponseCompressionProvider(services, options)
{
    /// <inheritdoc />
    public override bool ShouldCompressResponse(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !PayloadCompression.CarriesSecrets(context.Request.Path) && base.ShouldCompressResponse(context);
    }
}

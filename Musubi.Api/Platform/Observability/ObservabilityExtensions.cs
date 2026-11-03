using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Musubi.Api.Platform.Observability;

/// <summary>
/// Observabilidad (02-06): logs en JSON hacia CloudWatch (el contenedor escribe a la consola y el driver
/// <c>awslogs</c> los envia), con el <c>TraceId</c> de cada peticion, y una linea por peticion con metodo,
/// ruta, codigo y duracion. Nunca se registran cuerpos, query, encabezados ni tokens (RNF-05).
/// </summary>
internal static class ObservabilityExtensions
{
    /// <summary>Configura el logging: JSON fuera de Development y el log de cada peticion.</summary>
    /// <param name="builder">El builder de la aplicacion.</param>
    public static void AddMusubiObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // El TraceId tambien viaja en el problem+json de cada error (02-03): una queja se busca en CloudWatch.
        builder.Logging.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            });
        }

        // Solo lo necesario para operar: sin query (puede llevar un correo), sin encabezados ni cuerpos.
        builder.Services.AddHttpLogging(options =>
        {
            options.LoggingFields = HttpLoggingFields.RequestMethod
                | HttpLoggingFields.RequestPath
                | HttpLoggingFields.ResponseStatusCode
                | HttpLoggingFields.Duration;
            options.CombineLogs = true;
        });
    }

    /// <summary>Registra cada peticion, salvo los health checks del balanceador (llegan cada pocos segundos).</summary>
    /// <param name="app">La aplicacion.</param>
    public static void UseMusubiRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase), branch => branch.UseHttpLogging());
    }
}

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace Musubi.Api.Platform.Hosting;

/// <summary>Pipeline HTTP en orden fijo (01-02). No reordenar sin actualizar la spec.</summary>
internal static class PlatformPipelineExtensions
{
    /// <summary>Ruta del liveness probe.</summary>
    public const string HealthPath = "/health";

    /// <summary>Ruta del readiness probe.</summary>
    public const string ReadyPath = "/health/ready";

    /// <summary>Configura middleware y endpoints de la plataforma.</summary>
    /// <param name="app">La aplicacion.</param>
    public static void UseMusubiPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseForwardedHeaders();

        if (!app.Environment.IsDevelopment())
        {
            // ProblemDetails generico, sin stack trace (01-03).
            app.UseExceptionHandler();
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseCors(PlatformServiceCollectionExtensions.CorsPolicyName);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().WithDocumentPerVersion();
            app.MapScalarApiReference();
        }

        app.UseAuthorization();

        app.MapControllers();
        app.MapHealthChecks(HealthPath, new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(PlatformServiceCollectionExtensions.ReadyTag),
        });
    }
}

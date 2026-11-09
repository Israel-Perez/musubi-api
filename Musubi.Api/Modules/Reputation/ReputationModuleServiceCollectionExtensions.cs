using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Reputation;

/// <summary>Registro DI del modulo M-13 (Reputacion: reportes HU-28 y bloqueos HU-57).</summary>
internal static class ReputationModuleServiceCollectionExtensions
{
    /// <summary>Politica de rate limit de los reportes (por usuario).</summary>
    public const string ReportRateLimit = "reports";

    /// <summary>Registra repositorios (Mongo o en memoria, lazy), servicios, el limite de reportes y el borrado de la cuenta.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddReputationModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var reports = new InMemoryReportRepository();
        var blocks = new InMemoryBlockRepository();
        services.AddSingleton<IReportRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? reports
            : new MongoReportRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<IBlockRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? blocks
            : new MongoBlockRepository(provider.GetRequiredService<IMongoDatabase>()));

        services.AddOptions<ReportOptions>().BindConfiguration(ReportOptions.Section);
        services.AddSingleton<IBlockDirectory, BlockDirectory>();
        services.AddSingleton<BlockService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<IAccountDataEraser, ReputationAccountEraser>();

        // Cada reporte manda un correo al equipo: un tope por usuario evita el abuso (OWASP API4).
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, config) =>
        {
            var permits = config.GetValue("RateLimiting:Reports:PermitLimit", 10);
            var window = TimeSpan.FromSeconds(config.GetValue("RateLimiting:Reports:WindowSeconds", 3600));
            options.AddPolicy(ReportRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(MusubiClaims.Subject)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));
        });
    }
}

/// <summary>
/// Borra los bloqueos (en las dos direcciones) y los reportes que hizo una cuenta que se elimina (HU-46, R11). Los
/// reportes que otros hicieron sobre ella se quedan para la revision del equipo (HU-39): solo guardan su id.
/// </summary>
internal sealed class ReputationAccountEraser(IBlockRepository blocks, IReportRepository reports) : IAccountDataEraser
{
    /// <inheritdoc />
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        await blocks.DeleteByUserAsync(userId, cancellationToken).ConfigureAwait(false);
        await reports.DeleteByReporterAsync(userId, cancellationToken).ConfigureAwait(false);
    }
}

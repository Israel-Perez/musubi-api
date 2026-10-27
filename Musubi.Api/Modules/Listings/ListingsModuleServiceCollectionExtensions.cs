using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Listings;

/// <summary>Registro DI del modulo M-03 (Publicaciones).</summary>
internal static class ListingsModuleServiceCollectionExtensions
{
    /// <summary>Politica de rate limit de la subida de fotos (por usuario).</summary>
    public const string UploadRateLimit = "uploads";

    /// <summary>Registra repositorios (Mongo o en memoria, lazy), servicios y el limite de subidas.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddListingsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var categories = new InMemoryCategoryRepository();
        var products = new InMemoryProductRepository();
        services.AddSingleton<ICategoryRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? categories
            : new MongoCategoryRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<IProductRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? products
            : new MongoProductRepository(provider.GetRequiredService<IMongoDatabase>()));

        services.AddOptions<CatalogOptions>().BindConfiguration(CatalogOptions.Section);
        services.AddSingleton<ListingService>();
        services.AddSingleton<PhotoUploadService>();

        // Subir fotos cuesta CPU (SkiaSharp) y espacio en S3: un tope por usuario evita el abuso (OWASP API4).
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, config) =>
        {
            var permits = config.GetValue("RateLimiting:Uploads:PermitLimit", 30);
            var window = TimeSpan.FromSeconds(config.GetValue("RateLimiting:Uploads:WindowSeconds", 60));
            options.AddPolicy(UploadRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(MusubiClaims.Subject)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));
        });
    }
}

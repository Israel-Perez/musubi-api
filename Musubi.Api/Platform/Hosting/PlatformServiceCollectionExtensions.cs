using System;
using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Platform.Hosting;

/// <summary>Registro DI de la plataforma (01-02). Los modulos M-01…M-14 registran sus servicios en su propia extension.</summary>
internal static class PlatformServiceCollectionExtensions
{
    /// <summary>Nombre de la politica CORS restringida por configuracion.</summary>
    public const string CorsPolicyName = "MusubiCors";

    /// <summary>Nombre de la base InMemory usada en local y pruebas.</summary>
    public const string InMemoryDatabaseName = "musubi";

    /// <summary>Tag de los health checks que forman parte de la readiness.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Carga <c>appsettings.Local.json</c> (ignorado por Git) solo en Development.</summary>
    /// <param name="builder">El builder de la aplicacion.</param>
    public static void AddMusubiLocalConfiguration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
        }
    }

    /// <summary>Registra los servicios fundacionales: API versionada, CORS, HSTS, datos y health checks.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    /// <param name="configuration">La configuracion de la aplicacion.</param>
    public static void AddMusubiPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<KestrelServerOptions>(configuration.GetSection("Kestrel"));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // NOTE (02-05): registrar KnownNetworks/KnownProxies del balanceador de AWS al definir el despliegue.
        });

        services.AddProblemDetails();
        services.AddControllers();
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddMvc()
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        })
        .AddOpenApi();

        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            policy.WithOrigins(origins)
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                .WithHeaders("Authorization", "Content-Type");
        }));

        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });

        // NOTE (HU-02): FallbackPolicy RequireAuthenticatedUser cuando exista el esquema JWT; hoy la auth es explicita por controller.
        services.AddAuthorization();

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            // Lectura lazy (no eager en el registro): respeta los overrides de WebApplicationFactory (00-04, regla 3).
            var config = provider.GetRequiredService<IConfiguration>();
            if (config.GetValue("UseInMemoryDatabase", false))
            {
                options.UseInMemoryDatabase(InMemoryDatabaseName);
                return;
            }

            var connectionString = config.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Default es obligatorio cuando UseInMemoryDatabase=false (usar user-secrets o variable de entorno).");
            }

            options.UseNpgsql(connectionString);
        });

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: [ReadyTag]);
    }
}

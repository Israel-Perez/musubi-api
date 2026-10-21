using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SecurityTest.Common;

/// <summary>Factory WAF por ambiente: siempre UseInMemoryDatabase (sin MongoDB), un origen CORS permitido y el probe de excepciones de prueba.</summary>
public abstract class SecurityApiFactory(string environment) : WebApplicationFactory<Program>
{
    /// <summary>Origen que la configuracion de prueba permite en CORS.</summary>
    public const string AllowedOrigin = "https://admin.musubi.example";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UseInMemoryDatabase"] = "true",
            ["Cors:AllowedOrigins:0"] = AllowedOrigin,
        }));
        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ThrowingProbeController).Assembly));
    }
}

/// <summary>Production: HSTS activo, handler de excepciones generico, sin OpenAPI ni Scalar.</summary>
public sealed class ProductionApiFactory() : SecurityApiFactory("Production");

/// <summary>Development: contraste para los checks que solo aplican fuera de Dev.</summary>
public sealed class DevelopmentApiFactory() : SecurityApiFactory("Development");

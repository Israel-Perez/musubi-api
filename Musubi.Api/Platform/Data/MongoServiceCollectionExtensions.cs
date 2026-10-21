using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;

namespace Musubi.Api.Platform.Data;

/// <summary>Registro de MongoDB (Atlas M0 en la nube, contenedor <c>mongo</c> en local) y su readiness.</summary>
/// <remarks>NOTE (02-01): convenciones BSON (camelCase), colecciones, indices y repositorios nacen en la spec 02-01.</remarks>
internal static class MongoServiceCollectionExtensions
{
    /// <summary>Clave de la cadena de conexion (secreto: user-secrets o variable de entorno).</summary>
    public const string ConnectionStringName = "Mongo";

    /// <summary>Nombre de la base cuando <c>Mongo:DatabaseName</c> no esta configurado.</summary>
    public const string DefaultDatabaseName = "musubi";

    /// <summary>Nombre del health check de datos (tag ready).</summary>
    public const string HealthCheckName = "database";

    /// <summary>Tiempo maximo del ping de readiness.</summary>
    public static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Registra <see cref="IMongoClient"/>, <see cref="IMongoDatabase"/> y el check de readiness.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    /// <param name="readyTag">Tag de los checks que forman parte de la readiness.</param>
    public static void AddMusubiMongo(this IServiceCollection services, string readyTag)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Lectura lazy (no eager en el registro): respeta los overrides de WebApplicationFactory (00-04, regla 3).
        services.AddSingleton<IMongoClient>(provider =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{ConnectionStringName} es obligatorio cuando UseInMemoryDatabase=false (usar user-secrets o variable de entorno).");
            }

            return new MongoClient(connectionString);
        });

        services.AddSingleton(provider =>
        {
            var name = provider.GetRequiredService<IConfiguration>()["Mongo:DatabaseName"];
            return provider.GetRequiredService<IMongoClient>()
                .GetDatabase(string.IsNullOrWhiteSpace(name) ? DefaultDatabaseName : name);
        });

        services.AddHealthChecks().Add(new HealthCheckRegistration(
            HealthCheckName,
            provider => provider.GetRequiredService<IConfiguration>().GetValue("UseInMemoryDatabase", false)
                ? new InMemoryDatabaseHealthCheck()
                : new MongoHealthCheck(provider.GetRequiredService<IMongoDatabase>()),
            HealthStatus.Unhealthy,
            [readyTag],
            HealthCheckTimeout));
    }
}

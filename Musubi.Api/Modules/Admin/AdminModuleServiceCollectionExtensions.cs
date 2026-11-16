using System;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Admin.Data;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Admin;

/// <summary>Registro DI del modulo M-14 (Administracion: moderar reportes HU-39 y suspender cuentas HU-40).</summary>
internal static class AdminModuleServiceCollectionExtensions
{
    /// <summary>Registra la bitacora (Mongo o en memoria, lazy) y el servicio de moderacion.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddAdminModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var log = new InMemoryModerationLogRepository();
        services.AddSingleton<IModerationLogRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? log
            : new MongoModerationLogRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<ModerationService>();
    }
}

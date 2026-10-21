using System;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Auth;

/// <summary>Registro DI del modulo M-01 (Autenticacion y usuarios).</summary>
internal static class AuthModuleServiceCollectionExtensions
{
    /// <summary>Registra los repositorios de identidad: Mongo o en memoria segun <c>UseInMemoryDatabase</c> (lazy).</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddAuthModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Los fakes son singletons: en memoria, el estado vive mientras viva el proceso.
        var communities = new InMemoryCommunityRepository();
        var users = new InMemoryUserRepository();
        var codes = new InMemoryVerificationCodeRepository();
        var tokens = new InMemoryRefreshTokenRepository();

        services.AddSingleton<ICommunityRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? communities
            : new MongoCommunityRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<IUserRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? users
            : new MongoUserRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<IVerificationCodeRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? codes
            : new MongoVerificationCodeRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<IRefreshTokenRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? tokens
            : new MongoRefreshTokenRepository(provider.GetRequiredService<IMongoDatabase>()));
    }
}

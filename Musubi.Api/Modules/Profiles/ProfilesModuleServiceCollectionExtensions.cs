using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Platform.Caching;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Media;

namespace Musubi.Api.Modules.Profiles;

/// <summary>Registro DI del modulo M-02 (Perfil).</summary>
internal static class ProfilesModuleServiceCollectionExtensions
{
    /// <summary>Registra repositorios (Mongo o en memoria, lazy), servicios y el borrado de la cuenta.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddProfilesModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var careers = new InMemoryCareerRepository();
        var profiles = new InMemoryProfileRepository();
        services.AddSingleton<ICareerRepository>(provider => new CachedCareerRepository(
            MongoServiceCollectionExtensions.UseInMemory(provider)
                ? careers
                : new MongoCareerRepository(provider.GetRequiredService<IMongoDatabase>()),
            provider.GetRequiredService<AppCache>()));
        services.AddSingleton<IProfileRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? profiles
            : new MongoProfileRepository(provider.GetRequiredService<IMongoDatabase>()));
        var preferences = new InMemoryPreferenceRepository();
        services.AddSingleton<IPreferenceRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? preferences
            : new MongoPreferenceRepository(provider.GetRequiredService<IMongoDatabase>()));

        services.AddSingleton<IProfileDirectory, ProfileDirectory>();
        services.AddSingleton<IProfileRatings, ProfileRatings>();
        services.AddSingleton<IProfileTrades, ProfileTrades>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<ProfilePhotoService>();
        services.AddSingleton<IPreferencesService, PreferencesService>();
        services.AddSingleton<IAccountDataEraser, ProfileAccountEraser>();
    }
}

/// <summary>Borra el perfil y todas las fotos de perfil de una cuenta que se elimina (HU-46, R11).</summary>
internal sealed class ProfileAccountEraser(
    IProfileRepository profiles,
    IPreferenceRepository preferences,
    IMediaStorage media) : IAccountDataEraser
{
    /// <inheritdoc />
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        await profiles.DeleteAsync(userId, cancellationToken).ConfigureAwait(false);
        await preferences.DeleteAsync(userId, cancellationToken).ConfigureAwait(false);
        await media.DeletePrefixAsync(ProfileService.AvatarPrefix(userId), cancellationToken).ConfigureAwait(false);
    }
}

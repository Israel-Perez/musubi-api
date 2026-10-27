using System;
using Amazon;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Platform.Media;

/// <summary>Registro del almacen de imagenes (HU-06, R12).</summary>
internal static class MediaServiceCollectionExtensions
{
    /// <summary>Registra <see cref="IMediaStorage"/>: en memoria en Development, S3 fuera (validado al arrancar).</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddMusubiMedia(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MediaOptions>().BindConfiguration(MediaOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<MediaOptions>, MediaOptionsValidator>();
        services.AddSingleton<InMemoryMediaStorage>();

        // El cliente de AWS solo se crea si el proveedor es S3 (lazy); las credenciales salen del rol de la instancia.
        services.AddSingleton<IAmazonS3>(provider =>
            new AmazonS3Client(RegionEndpoint.GetBySystemName(provider.GetRequiredService<IOptions<MediaOptions>>().Value.Region)));
        services.AddSingleton<IMediaStorage>(provider =>
            provider.GetRequiredService<IOptions<MediaOptions>>().Value.Provider == MediaOptions.InMemoryProvider
                ? provider.GetRequiredService<InMemoryMediaStorage>()
                : new S3MediaStorage(provider.GetRequiredService<IAmazonS3>(), provider.GetRequiredService<IOptions<MediaOptions>>()));
    }
}

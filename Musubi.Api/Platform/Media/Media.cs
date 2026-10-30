using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Platform.Media;

/// <summary>Configuracion de las imagenes (seccion <c>Media</c>; HU-06, R12).</summary>
internal sealed class MediaOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Media";

    /// <summary>Solo Development: las imagenes viven en memoria y las sirve la API en <c>/media/*</c>.</summary>
    public const string InMemoryProvider = "InMemory";

    /// <summary>Bucket privado de S3, servido por CloudFront en <c>/media/*</c> (02-05).</summary>
    public const string S3Provider = "S3";

    /// <summary><c>InMemory</c> o <c>S3</c>.</summary>
    public string Provider { get; set; } = S3Provider;

    /// <summary>Nombre del bucket (salida <c>media_bucket</c> de Terraform).</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Region del bucket.</summary>
    public string Region { get; set; } = "us-east-1";
}

/// <summary>Fail-fast: fuera de Development solo S3 y con bucket (una foto nunca se pierde en la memoria de una instancia).</summary>
internal sealed class MediaOptionsValidator(IHostEnvironment environment) : IValidateOptions<MediaOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MediaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Provider == MediaOptions.InMemoryProvider)
        {
            return environment.IsDevelopment()
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("Media:Provider=InMemory solo se permite en Development.");
        }

        if (options.Provider != MediaOptions.S3Provider)
        {
            return ValidateOptionsResult.Fail("Media:Provider debe ser InMemory o S3.");
        }

        return string.IsNullOrWhiteSpace(options.Bucket)
            ? ValidateOptionsResult.Fail("Media:Bucket es obligatorio con S3 (salida media_bucket de Terraform).")
            : ValidateOptionsResult.Success;
    }
}

/// <summary>Almacen de imagenes. La llave empieza con <c>media/</c>: CloudFront la sirve tal cual en <c>/media/*</c>.</summary>
internal interface IMediaStorage
{
    /// <summary>Guarda (o reemplaza) un archivo.</summary>
    /// <param name="key">Llave (<c>media/products/...</c>).</param>
    /// <param name="content">Bytes.</param>
    /// <param name="contentType">Tipo MIME.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken);

    /// <summary>Indica si la llave existe.</summary>
    /// <param name="key">Llave.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si existe.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    /// <summary>Borra todos los archivos cuya llave empieza con el prefijo (HU-46).</summary>
    /// <param name="prefix">Prefijo, terminado en <c>/</c> (<c>media/products/{userId}/</c>).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se borraron.</returns>
    Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken);
}

/// <summary>Archivo guardado en memoria.</summary>
/// <param name="Content">Bytes.</param>
/// <param name="ContentType">Tipo MIME.</param>
internal sealed record StoredMedia(byte[] Content, string ContentType);

/// <summary>Imagenes en memoria (Development y pruebas). <see cref="MediaController"/> las sirve.</summary>
internal sealed class InMemoryMediaStorage : IMediaStorage
{
    private readonly ConcurrentDictionary<string, StoredMedia> files = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        files[key] = new StoredMedia(content, contentType);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) => Task.FromResult(files.ContainsKey(key));

    /// <inheritdoc />
    public Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var deleted = 0;
        foreach (var key in files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            deleted += files.TryRemove(key, out _) ? 1 : 0;
        }

        return Task.FromResult(deleted);
    }

    /// <summary>Lee un archivo.</summary>
    /// <param name="key">Llave.</param>
    /// <returns>El archivo o <c>null</c>.</returns>
    public StoredMedia? Find(string key) => files.TryGetValue(key, out var file) ? file : null;
}

/// <summary>Imagenes en el bucket privado de S3 (el rol de la instancia solo puede leer, escribir y borrar ahi; 02-05).</summary>
internal sealed class S3MediaStorage(IAmazonS3 s3, IOptions<MediaOptions> options) : IMediaStorage
{
    /// <inheritdoc />
    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        var request = new PutObjectRequest
        {
            BucketName = options.Value.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
        };
        // La llave nunca se reutiliza (id nuevo por foto): CloudFront y el telefono la guardan para siempre.
        request.Headers.CacheControl = "public, max-age=31536000, immutable";
        await s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await s3.GetObjectMetadataAsync(options.Value.Bucket, key, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var request = new ListObjectsV2Request { BucketName = options.Value.Bucket, Prefix = prefix };
        ListObjectsV2Response page;
        do
        {
            page = await s3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
            if (page.S3Objects is { Count: > 0 } objects)
            {
                // Una pagina trae hasta 1000 llaves: el maximo de DeleteObjects.
                await s3.DeleteObjectsAsync(
                    new DeleteObjectsRequest
                    {
                        BucketName = options.Value.Bucket,
                        Objects = objects.Select(o => new KeyVersion { Key = o.Key }).ToList(),
                    },
                    cancellationToken).ConfigureAwait(false);
                deleted += objects.Count;
            }

            request.ContinuationToken = page.NextContinuationToken;
        }
        while (page.IsTruncated == true);

        return deleted;
    }
}

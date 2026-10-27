using System;
using System.IO;
using SkiaSharp;

namespace Musubi.Api.Platform.Media;

/// <summary>Foto lista para guardar: WebP sin metadatos.</summary>
/// <param name="Content">Bytes WebP.</param>
/// <param name="Width">Ancho final.</param>
/// <param name="Height">Alto final.</param>
internal sealed record ProcessedPhoto(byte[] Content, int Width, int Height);

/// <summary>Motivo por el que una foto no se acepta.</summary>
internal enum PhotoRejection
{
    /// <summary>No es una imagen que se pueda decodificar (JPEG, PNG, WebP, GIF o BMP).</summary>
    UnsupportedFormat,

    /// <summary>Demasiado chica para verse bien en la vitrina.</summary>
    TooSmall,

    /// <summary>Demasiados pixeles: protege la memoria de la instancia (bomba de descompresion).</summary>
    TooLarge,
}

/// <summary>
/// Normaliza una foto de producto (HU-06, RNF-13): la decodifica, la endereza segun su EXIF, la reduce a
/// <see cref="MaxSide"/> px por lado y la vuelve a codificar a WebP. La salida no lleva metadatos: la ubicacion
/// GPS que guarda la camara del telefono nunca llega a S3 (RNF-05).
/// </summary>
internal static class PhotoProcessor
{
    /// <summary>Lado mayor maximo de la foto guardada.</summary>
    public const int MaxSide = 1600;

    /// <summary>Lado menor minimo aceptado.</summary>
    public const int MinSide = 200;

    /// <summary>Pixeles maximos de la foto original (50 MP).</summary>
    public const long MaxPixels = 50_000_000;

    /// <summary>Calidad WebP (0-100).</summary>
    public const int Quality = 80;

    /// <summary>Tipo MIME de la salida.</summary>
    public const string ContentType = "image/webp";

    /// <summary>Procesa la foto.</summary>
    /// <param name="input">Bytes del archivo subido.</param>
    /// <param name="photo">La foto lista, si se acepto.</param>
    /// <returns><c>null</c> si se acepto; si no, el motivo.</returns>
    public static PhotoRejection? TryProcess(byte[] input, out ProcessedPhoto? photo)
    {
        ArgumentNullException.ThrowIfNull(input);
        photo = null;
        // El codec se queda con el stream y lo libera al liberarse.
        using var codec = SKCodec.Create(new MemoryStream(input, writable: false), out var opened);
        if (opened != SKCodecResult.Success)
        {
            return PhotoRejection.UnsupportedFormat;
        }

        var info = codec.Info;
        if ((long)info.Width * info.Height > MaxPixels)
        {
            return PhotoRejection.TooLarge;
        }

        if (Math.Min(info.Width, info.Height) < MinSide)
        {
            return PhotoRejection.TooSmall;
        }

        using var decoded = SKBitmap.Decode(codec, info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul));
        if (decoded is null)
        {
            return PhotoRejection.UnsupportedFormat;
        }

        using var oriented = Orient(decoded, codec.EncodedOrigin);
        using var resized = Resize(oriented);
        using var image = SKImage.FromBitmap(resized);
        using var webp = image.Encode(SKEncodedImageFormat.Webp, Quality);
        photo = new ProcessedPhoto(webp.ToArray(), resized.Width, resized.Height);
        return null;
    }

    /// <summary>Aplica la orientacion EXIF a los pixeles (la salida ya no la necesita).</summary>
    /// <param name="source">Pixeles tal como estan guardados.</param>
    /// <param name="origin">Orientacion EXIF.</param>
    /// <returns>Una copia nueva y derecha.</returns>
    public static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(source);
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;
        var target = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(target);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // espejo horizontal
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // 180 grados
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // espejo vertical
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // transpuesta
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop: // 90 grados a la derecha (foto vertical del telefono)
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // transversa
                canvas.Translate(width, height);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom: // 90 grados a la izquierda
                canvas.Translate(0, height);
                canvas.RotateDegrees(270);
                break;
            default: // TopLeft: ya esta derecha
                break;
        }

        // Giros y espejos exactos: no hace falta interpolar.
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint: null);
        return target;
    }

    private static SKBitmap Resize(SKBitmap source)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= MaxSide)
        {
            return source.Copy();
        }

        var scale = (double)MaxSide / longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        return source.Resize(new SKImageInfo(width, height, source.ColorType, source.AlphaType), sampling)
            ?? throw new InvalidOperationException("SkiaSharp no pudo reducir la foto.");
    }
}

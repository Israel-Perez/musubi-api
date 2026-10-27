using System.Buffers.Binary;
using System.Text;
using Musubi.Api.Platform.Media;
using SkiaSharp;

namespace UnitTest.Listings;

/// <summary>HU-06 / RNF-13 / RNF-05: la foto sale derecha, reducida, en WebP y sin metadatos (ubicacion GPS incluida).</summary>
public sealed class PhotoProcessorTests
{
    /// <summary>Imagen con la mitad izquierda roja y la derecha azul.</summary>
    private static SKBitmap Halves(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var red = new SKPaint { Color = SKColors.Red };
        using var blue = new SKPaint { Color = SKColors.Blue };
        canvas.DrawRect(0, 0, width / 2f, height, red);
        canvas.DrawRect(width / 2f, 0, width / 2f, height, blue);
        return bitmap;
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    /// <summary>
    /// JPEG con un segmento APP1 (EXIF) como el de la camara de un telefono: orientacion y una etiqueta GPS.
    /// TIFF big-endian: IFD0 (Orientation + puntero a GPS) y un IFD de GPS con GPSLatitudeRef = "N".
    /// </summary>
    private static byte[] JpegWithExif(SKBitmap bitmap, ushort orientation)
    {
        var tiff = new byte[56];
        "MM"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(8), 2);
        WriteEntry(tiff, 10, tag: 0x0112, type: 3, value: (uint)orientation << 16);
        WriteEntry(tiff, 22, tag: 0x8825, type: 4, value: 38);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(34), 0);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(38), 1);
        WriteEntry(tiff, 40, tag: 0x0001, type: 2, value: (uint)'N' << 24, count: 2);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(52), 0);

        var exif = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var segment = new byte[4 + exif.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(exif.Length + 2));
        exif.CopyTo(segment, 4);

        var jpeg = Encode(bitmap, SKEncodedImageFormat.Jpeg);
        return [.. jpeg.AsSpan(0, 2), .. segment, .. jpeg.AsSpan(2)];
    }

    private static void WriteEntry(byte[] tiff, int at, ushort tag, ushort type, uint value, uint count = 1)
    {
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), tag);
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at + 2), type);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at + 4), count);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at + 8), value);
    }

    private static SKBitmap Decode(ProcessedPhoto photo) => SKBitmap.Decode(photo.Content);

    private static bool IsRed(SKColor c) => c.Red > 200 && c.Blue < 60;

    private static bool IsBlue(SKColor c) => c.Blue > 200 && c.Red < 60;

    [Fact]
    public void PhoneExif_Orientation6_IsRotatedAndTheGpsIsDropped()
    {
        using var landscape = Halves(400, 300);
        var input = JpegWithExif(landscape, orientation: 6);
        using (var codec = SKCodec.Create(new MemoryStream(input)))
        {
            Assert.Equal(SKEncodedOrigin.RightTop, codec.EncodedOrigin);
        }

        Assert.Null(PhotoProcessor.TryProcess(input, out var photo));

        Assert.NotNull(photo);
        Assert.Equal((300, 400), (photo.Width, photo.Height));
        using var output = Decode(photo);
        // Girada 90 grados a la derecha: lo que estaba a la izquierda (rojo) queda arriba.
        Assert.True(IsRed(output.GetPixel(150, 50)));
        Assert.True(IsBlue(output.GetPixel(150, 350)));
        var text = Encoding.ASCII.GetString(photo.Content);
        Assert.DoesNotContain("Exif", text, StringComparison.Ordinal);
        Assert.DoesNotContain("EXIF", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_IsWebP()
    {
        using var bitmap = Halves(400, 300);

        Assert.Null(PhotoProcessor.TryProcess(Encode(bitmap, SKEncodedImageFormat.Png), out var photo));

        Assert.NotNull(photo);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(photo.Content, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(photo.Content, 8, 4));
        Assert.Equal((400, 300), (photo.Width, photo.Height));
    }

    [Fact]
    public void BigPhoto_IsReducedTo1600OnItsLongestSide()
    {
        using var bitmap = Halves(4000, 3000);

        Assert.Null(PhotoProcessor.TryProcess(Encode(bitmap, SKEncodedImageFormat.Jpeg), out var photo));

        Assert.Equal((1600, 1200), (photo!.Width, photo.Height));
    }

    [Fact]
    public void TinyPhoto_IsRejected()
    {
        using var bitmap = Halves(150, 150);

        Assert.Equal(PhotoRejection.TooSmall, PhotoProcessor.TryProcess(Encode(bitmap, SKEncodedImageFormat.Png), out _));
    }

    [Fact]
    public void NotAnImage_IsRejected()
    {
        var text = "no soy una foto, soy un archivo de texto"u8.ToArray();

        Assert.Equal(PhotoRejection.UnsupportedFormat, PhotoProcessor.TryProcess(text, out var photo));
        Assert.Null(photo);
    }
}

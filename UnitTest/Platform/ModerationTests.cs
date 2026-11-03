using Amazon.Rekognition.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;
using SkiaSharp;

namespace UnitTest.Platform;

/// <summary>02-07: que etiquetas y que toxicidad rechazan, la copia JPEG para Rekognition y el fail-fast fuera de Development.</summary>
public sealed class ModerationTests
{
    private static readonly ModerationOptions Options = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Jpeg(SKColor color)
    {
        using var bitmap = new SKBitmap(300, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    [Theory]
    [InlineData("Graphic Male Nudity", "Explicit", 99f, "Explicit")]
    [InlineData("Explicit Nudity", "", 95f, "Explicit Nudity")]
    [InlineData("Weapon Violence", "Violence", 90f, "Violence")]
    [InlineData("Pills", "Drugs & Tobacco", 88f, "Drugs & Tobacco")]
    [InlineData("Swimwear or Underwear", "", 99f, null)]
    [InlineData("Alcoholic Beverages", "Alcohol", 99f, null)]
    [InlineData("Weapon Violence", "Violence", 60f, null)]
    public void BlockedLabel_DependsOnTheLabelAndItsConfidence(string name, string parent, float confidence, string? blocked)
    {
        var labels = new[] { new ModerationLabel { Name = name, ParentName = parent, Confidence = confidence } };

        Assert.Equal(blocked, AwsContentModerator.BlockedLabel(labels, Options));
    }

    [Theory]
    [InlineData(0.9f, true)]
    [InlineData(0.7f, true)]
    [InlineData(0.2f, false)]
    [InlineData(null, false)]
    public void Toxicity_FromTheThreshold_IsRejected(float? toxicity, bool rejected)
    {
        Assert.Equal(rejected, AwsContentModerator.IsToxic(toxicity, Options));
    }

    [Fact]
    public async Task InMemory_RejectsTheMarkerAndMagentaPhotos_Only()
    {
        var moderator = new InMemoryContentModerator();

        var texts = await moderator.CheckTextsAsync(["Calculadora Casio", $"Algo {InMemoryContentModerator.RejectMarker}", ""], Ct);

        Assert.Equal([ModerationVerdict.Allowed, ModerationVerdict.Rejected, ModerationVerdict.Allowed], texts);
        Assert.Equal(ModerationVerdict.Rejected, await moderator.CheckImageAsync(Jpeg(new SKColor(255, 0, 255)), Ct));
        Assert.Equal(ModerationVerdict.Allowed, await moderator.CheckImageAsync(Jpeg(SKColors.Teal), Ct));
    }

    [Fact]
    public void ProcessedPhoto_CarriesAJpegCopy_ForRekognition()
    {
        using var bitmap = new SKBitmap(400, 300);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);

        Assert.Null(PhotoProcessor.TryProcess(png.ToArray(), out var photo));

        Assert.Equal([0xFF, 0xD8], photo!.ModerationCopy.Take(2));
        Assert.Equal("RIFF"u8.ToArray(), photo.Content.Take(4));
    }

    [Theory]
    [InlineData("Production", "InMemory", false)]
    [InlineData("Production", "Aws", true)]
    [InlineData("Development", "InMemory", true)]
    [InlineData("Development", "Otro", false)]
    public void Options_OutsideDevelopment_RequireAws(string environment, string provider, bool valid)
    {
        IHostEnvironment host = new HostingEnvironment { EnvironmentName = environment };

        var result = new ModerationOptionsValidator(host).Validate(null, new ModerationOptions { Provider = provider });

        Assert.Equal(valid, result.Succeeded);
    }
}

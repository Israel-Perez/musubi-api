using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Musubi.Api.Platform.Hosting;

namespace UnitTest.Hosting;

/// <summary>SCRUM-120 / 02-04: que se comprime, que nunca (las rutas de sesion, BREACH) y con que nivel.</summary>
public sealed class PayloadCompressionTests
{
    [Theory]
    [InlineData("/api/v1/auth/login", true)]
    [InlineData("/api/v1/auth/password/change", true)]
    [InlineData("/API/V1/AUTH/refresh", true)]
    [InlineData("/api/v1/products", false)]
    [InlineData("/api/v1/authors", false)]
    [InlineData("/health", false)]
    [InlineData("/auth/login", false)]
    [InlineData("/api/x/auth/login", false)]
    public void OnlyTheSessionRoutes_AreNeverCompressed(string path, bool skipped) =>
        Assert.Equal(skipped, PayloadCompression.CarriesSecrets(new PathString(path)));

    [Fact]
    public void ProblemJson_IsCompressedToo() =>
        Assert.Contains("application/problem+json", PayloadCompression.MimeTypes);

    [Theory]
    [InlineData(null, CompressionLevel.Optimal)]
    [InlineData("", CompressionLevel.Optimal)]
    [InlineData("fastest", CompressionLevel.Fastest)]
    [InlineData("SmallestSize", CompressionLevel.SmallestSize)]
    [InlineData("NoCompression", CompressionLevel.Optimal)]
    [InlineData("99", CompressionLevel.Optimal)]
    [InlineData("rapido", CompressionLevel.Optimal)]
    public void Level_ComesFromConfiguration_OptimalByDefault(string? configured, CompressionLevel expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [PayloadCompression.LevelKey] = configured })
            .Build();

        Assert.Equal(expected, PayloadCompression.Level(config));
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnitTest.Common;

/// <summary>01-04: los appsettings versionados solo llevan placeholders, nunca secretos.</summary>
public sealed partial class AppSettingsTests
{
    public static TheoryData<string> TrackedSettingsFiles =>
    [
        "appsettings.json",
        "appsettings.Development.json",
        "appsettings.Production.json",
        "appsettings.Example.json",
    ];

    [Theory]
    [MemberData(nameof(TrackedSettingsFiles))]
    public void TrackedSettings_AreValidJson(string fileName)
    {
        var raw = File.ReadAllText(RepoPaths.ApiFile(fileName));

        using var doc = JsonDocument.Parse(raw);

        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Theory]
    [MemberData(nameof(TrackedSettingsFiles))]
    public void TrackedSettings_ContainNoSecrets(string fileName)
    {
        var raw = File.ReadAllText(RepoPaths.ApiFile(fileName));

        Assert.DoesNotMatch(PasswordInConnectionString(), raw);
        Assert.DoesNotMatch(CredentialsInMongoUri(), raw);
        Assert.DoesNotMatch(PasswordProperty(), raw);
        Assert.DoesNotContain("-----BEGIN", raw, StringComparison.Ordinal);
        Assert.DoesNotMatch(LongHexOrBase64(), raw);
    }

    [Fact]
    public void ExampleSettings_DocumentEveryImplementedKey()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoPaths.ApiFile("appsettings.Example.json")));
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("UseInMemoryDatabase", out _));
        Assert.True(root.GetProperty("ConnectionStrings").TryGetProperty("Mongo", out _));
        Assert.True(root.GetProperty("Mongo").TryGetProperty("DatabaseName", out _));
        Assert.True(root.GetProperty("Kestrel").GetProperty("Limits").TryGetProperty("MaxRequestBodySize", out _));
        Assert.True(root.GetProperty("Kestrel").GetProperty("Limits").TryGetProperty("KeepAliveTimeout", out _));
        Assert.True(root.GetProperty("Cors").GetProperty("AllowedOrigins").GetArrayLength() > 0);
    }

    [Fact]
    public void ExampleSettings_ContainNoFutureKeys()
    {
        var raw = File.ReadAllText(RepoPaths.ApiFile("appsettings.Example.json"));

        // Las claves futuras solo se documentan en la tabla de 01-04 hasta que haya codigo que las lea.
        Assert.DoesNotContain("\"Jwt\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("MercadoPago", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Firebase", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GenAi", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionSettings_DisableInMemoryDatabase()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoPaths.ApiFile("appsettings.Production.json")));

        Assert.False(doc.RootElement.GetProperty("UseInMemoryDatabase").GetBoolean());
    }

    [GeneratedRegex(@"(?i)(password|pwd)\s*=\s*[^;""\s]+")]
    private static partial Regex PasswordInConnectionString();

    [GeneratedRegex(@"(?i)mongodb(\+srv)?://[^/\s:@""]+:[^@\s""]+@")]
    private static partial Regex CredentialsInMongoUri();

    [GeneratedRegex(@"(?i)""(password|secret|apikey|api_key|token)""\s*:\s*""[^""]+""")]
    private static partial Regex PasswordProperty();

    [GeneratedRegex("[A-Za-z0-9+/=]{48,}")]
    private static partial Regex LongHexOrBase64();
}

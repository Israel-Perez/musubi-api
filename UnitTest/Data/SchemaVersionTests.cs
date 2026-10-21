using System.Text.RegularExpressions;
using Musubi.Api.Platform.Data;
using UnitTest.Common;

namespace UnitTest.Data;

/// <summary>02-01: los scripts de db/ y MongoSchema.ExpectedVersion no se desalinean (drift guard).</summary>
public sealed partial class SchemaVersionTests
{
    private static string[] Scripts() =>
        [.. Directory.GetFiles(Path.Combine(RepoPaths.Root, "db"), "*.js").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Fact]
    public void Scripts_AreNumberedConsecutivelyFrom001()
    {
        var numbers = Scripts().Select(name => int.Parse(name[..3], System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        Assert.NotEmpty(numbers);
        Assert.Equal(Enumerable.Range(1, numbers.Length), numbers);
        Assert.All(Scripts(), name => Assert.Matches(ScriptName(), name));
    }

    [Fact]
    public void LastScript_DeclaresTheVersionTheApiExpects()
    {
        var last = Scripts()[^1];
        var content = File.ReadAllText(Path.Combine(RepoPaths.Root, "db", last));

        Assert.Equal(MongoSchema.ExpectedVersion, int.Parse(last[..3], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains($"const SCHEMA_VERSION = {MongoSchema.ExpectedVersion};", content, StringComparison.Ordinal);
        Assert.Contains($"const SCRIPT_ID = '{Path.GetFileNameWithoutExtension(last)}';", content, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^\d{3}-[a-z0-9-]+\.js$")]
    private static partial Regex ScriptName();
}

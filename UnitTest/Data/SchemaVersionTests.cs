using System.Reflection;
using System.Text.RegularExpressions;
using Musubi.Api.Modules.Notifications;
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

    [Fact]
    public void TheLastNotificationsValidator_AcceptsEveryTypeAndTargetOfTheCode()
    {
        // Un tipo nuevo sin su script hace que el aviso no se guarde en MongoDB (el validador lo rechaza) y solo se nota
        // en produccion. collMod reemplaza el validador completo: manda el ultimo script que lo define.
        var last = Scripts().Last(name => File.ReadAllText(Path.Combine(RepoPaths.Root, "db", name)).Contains("ensureCollection('notifications'", StringComparison.Ordinal));
        var content = File.ReadAllText(Path.Combine(RepoPaths.Root, "db", last));
        var validator = content[content.IndexOf("ensureCollection('notifications'", StringComparison.Ordinal)..];

        Assert.All(Constants(typeof(NotificationTypes)).Where(t => t != NotificationTypes.Message), t => Assert.Contains($"'{t}'", validator, StringComparison.Ordinal));
        Assert.All(Constants(typeof(NotificationTargets)), t => Assert.Contains($"'{t}'", validator, StringComparison.Ordinal));
    }

    private static IEnumerable<string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    [GeneratedRegex(@"^\d{3}-[a-z0-9-]+\.js$")]
    private static partial Regex ScriptName();
}

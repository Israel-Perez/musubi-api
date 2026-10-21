using System.Reflection;
using System.Text.RegularExpressions;

namespace UnitTest.Common;

/// <summary>Lineamiento 11: la version de la API coincide con la ultima fila de la matriz (<c>docs/versionado.md</c>).</summary>
public sealed partial class VersioningTests
{
    private static string ApiVersion() =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];

    [Fact]
    public void ApiVersion_IsSemVer()
    {
        Assert.Matches(SemVer(), ApiVersion());
    }

    [Fact]
    public void ApiVersion_MatchesLatestRowOfCompatibilityMatrix()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoPaths.Root, "docs", "versionado.md"));
        var start = Array.FindIndex(lines, l => l.StartsWith("## 3.", StringComparison.Ordinal));
        var rows = lines.Skip(start + 1)
            .TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal))
            .Where(l => DataRow().IsMatch(l))
            .ToList();

        Assert.NotEmpty(rows);
        var apiColumn = rows[^1].Split('|')[2].Trim();
        Assert.Equal(ApiVersion(), apiColumn);
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$")]
    private static partial Regex SemVer();

    [GeneratedRegex(@"^\| \d{2}-[A-Z][a-z]{2}-\d{4} \|")]
    private static partial Regex DataRow();
}

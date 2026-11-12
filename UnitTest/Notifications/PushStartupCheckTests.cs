using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Notifications;

namespace UnitTest.Notifications;

/// <summary>
/// SCRUM-96: al arrancar, el log dice si el push sale por FCM o por que no (llave vacia, cortada o incompleta),
/// sin imprimir nunca la llave.
/// </summary>
public sealed class PushStartupCheckTests
{
    // Una llave de mentira: los campos que lee la API, sin una llave privada real.
    private const string PrivateKey = "-----BEGIN PRIVATE KEY-----\\nNO-ES-UNA-LLAVE-REAL\\n-----END PRIVATE KEY-----\\n";

    private const string Compact =
        "{\"type\":\"service_account\",\"project_id\":\"musubi-prueba\",\"private_key_id\":\"abc123def456\"," +
        $"\"private_key\":\"{PrivateKey}\",\"client_email\":\"push@musubi-prueba.iam.gserviceaccount.com\"}}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FirebaseOptions Fcm(string serviceAccount) => new() { Provider = FirebaseOptions.FcmProvider, ServiceAccount = serviceAccount };

    [Fact]
    public void Inspect_ACompactKey_IsActive_WithTheProjectAndTheStartOfTheKeyId()
    {
        Assert.Equal((PushStartupState.Active, "musubi-prueba", "abc123"), PushStartupCheck.Inspect(Fcm(Compact)));
    }

    [Theory]
    [InlineData("", nameof(PushStartupState.MissingKey))]
    [InlineData("   ", nameof(PushStartupState.MissingKey))]
    // 11-Nov-2026: un JSON de varias lineas en el api.env de user-data llega como su primera linea.
    [InlineData("{", nameof(PushStartupState.UnreadableKey))]
    [InlineData("no es json", nameof(PushStartupState.UnreadableKey))]
    [InlineData("{\"project_id\":\"musubi-prueba\"}", nameof(PushStartupState.IncompleteKey))]
    public void Inspect_AKeyThatCannotSend_SaysWhy(string serviceAccount, string expected)
    {
        Assert.Equal(expected, PushStartupCheck.Inspect(Fcm(serviceAccount)).State.ToString());
    }

    [Fact]
    public void Inspect_InMemory_IsInMemory_EvenWithAKey()
    {
        var options = new FirebaseOptions { Provider = FirebaseOptions.InMemoryProvider, ServiceAccount = Compact };

        Assert.Equal(PushStartupState.InMemory, PushStartupCheck.Inspect(options).State);
    }

    [Theory]
    [InlineData("{", "Production", LogLevel.Warning)]
    [InlineData("", "Production", LogLevel.Warning)]
    [InlineData(null, "Production", LogLevel.Information)]
    public async Task Start_LogsOnce_AndNeverTheKey(string? serviceAccount, string environment, LogLevel level)
    {
        var logger = new RecordingLogger();
        var check = new PushStartupCheck(Options.Create(Fcm(serviceAccount ?? Compact)), new Env(environment), logger);

        await check.StartAsync(Ct);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(level, entry.Level);
        Assert.DoesNotContain("NO-ES-UNA-LLAVE-REAL", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123def456", entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", 0)]
    [InlineData("Production", 1)]
    public async Task Start_InMemory_OnlyWarnsOutsideDevelopment(string environment, int warnings)
    {
        var logger = new RecordingLogger();
        var options = new FirebaseOptions { Provider = FirebaseOptions.InMemoryProvider };
        var check = new PushStartupCheck(Options.Create(options), new Env(environment), logger);

        await check.StartAsync(Ct);

        Assert.Equal(warnings, logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Musubi.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class RecordingLogger : ILogger<PushStartupCheck>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SecurityTest.Common;

namespace SecurityTest.Observability;

/// <summary>
/// 02-06 / RNF-05: cada peticion deja una linea con metodo, ruta, codigo y duracion, y nunca la query (puede
/// llevar un correo), el token ni los cuerpos. Los health checks del balanceador no se registran.
/// </summary>
public sealed class RequestLoggingTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Guarda cada linea de log (categoria y texto ya formateado, con sus valores).</summary>
    private sealed class CaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capture(categoryName, Lines);

        public void Dispose()
        {
        }

        private sealed class Capture(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}"))
                    : string.Empty;
                lines.Enqueue($"{category} | {formatter(state, exception)} | {values}");
            }
        }
    }

    [Fact]
    public async Task EachRequest_IsLogged_WithoutQueryTokenOrBody()
    {
        using var capture = new CaptureProvider();
        using var app = production.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(capture)));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-que-no-debe-salir");

        await client.GetAsync(new Uri("/api/v1/ping?email=ana.ramirez@uttt.edu.mx", UriKind.Relative), Ct);
        await client.GetAsync(new Uri("/health", UriKind.Relative), Ct);

        var http = capture.Lines.Where(l => l.StartsWith("Microsoft.AspNetCore.HttpLogging", StringComparison.Ordinal)).ToList();
        Assert.Contains(http, l => l.Contains("/api/v1/ping", StringComparison.Ordinal) && l.Contains("StatusCode", StringComparison.Ordinal));
        Assert.DoesNotContain(http, l => l.Contains("/health", StringComparison.Ordinal));
        Assert.DoesNotContain(capture.Lines, l => l.Contains("ana.ramirez", StringComparison.Ordinal));
        Assert.DoesNotContain(capture.Lines, l => l.Contains("token-que-no-debe-salir", StringComparison.Ordinal));
    }
}

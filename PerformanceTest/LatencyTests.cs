using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PerformanceTest;

/// <summary>API en Development y en memoria: mide la API, no la red ni MongoDB.</summary>
public sealed class PerformanceApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["UseInMemoryDatabase"] = "true" }));
    }
}

/// <summary>
/// SCRUM-94 (RNF-09, L-15i): latencia p95 de las rutas publicas mas usadas bajo carga concurrente. En el CI
/// corre en proceso. Con <c>PERF_BASE_URL</c> (por ejemplo, <c>https://api.musubiapp.com.mx</c>) mide la API
/// desplegada con el umbral de <c>PERF_P95_MS</c>.
/// </summary>
public sealed class LatencyTests(PerformanceApiFactory factory) : IClassFixture<PerformanceApiFactory>
{
    /// <summary>Peticiones por ruta y peticiones simultaneas.</summary>
    private const int Requests = 400;
    private const int Concurrency = 20;

    /// <summary>
    /// Umbral en proceso. Medido el 03-Nov-2026 en la laptop de desarrollo (2 corridas): p95 de 1.1 ms (ping),
    /// 2.7 ms (categorias) y 4.6 ms (vitrina). Se deja holgura para el runner del CI, que es mas lento.
    /// </summary>
    private const double InProcessP95Ms = 100;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> PublicRoutes() => new()
    {
        "/api/v1/ping",
        "/api/v1/categories",
        "/api/v1/products?limit=20",
        // HU-09 (RNF-09): la busqueda por palabra clave.
        "/api/v1/products?q=calc",
        // HU-10 (RNF-09): rango de precio y estado.
        "/api/v1/products?category=calculadoras&minPriceCents=10000&maxPriceCents=60000&condition=used",
    };

    private HttpClient Client()
    {
        var baseUrl = Environment.GetEnvironmentVariable("PERF_BASE_URL");
        return string.IsNullOrWhiteSpace(baseUrl)
            ? factory.CreateClient()
            : new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    private static double Threshold() =>
        double.TryParse(Environment.GetEnvironmentVariable("PERF_P95_MS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            ? ms
            : InProcessP95Ms;

    private static double Percentile(List<double> sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];

    [Theory]
    [MemberData(nameof(PublicRoutes))]
    public async Task P95_UnderConcurrentLoad_IsWithinTheThreshold(string route)
    {
        using var client = Client();
        var uri = new Uri(route, UriKind.Relative);

        // Calentamiento: JIT, cache de salida y conexiones.
        for (var i = 0; i < 10; i++)
        {
            using var warm = await client.GetAsync(uri, Ct);
            warm.EnsureSuccessStatusCode();
        }

        var durations = new ConcurrentBag<double>();
        var failures = 0;
        await Parallel.ForEachAsync(
            Enumerable.Range(0, Requests),
            new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = Ct },
            async (_, ct) =>
            {
                var watch = Stopwatch.StartNew();
                using var response = await client.GetAsync(uri, ct).ConfigureAwait(false);
                watch.Stop();
                if (!response.IsSuccessStatusCode)
                {
                    Interlocked.Increment(ref failures);
                }

                durations.Add(watch.Elapsed.TotalMilliseconds);
            });

        var sorted = durations.Order().ToList();
        var p50 = Percentile(sorted, 0.50);
        var p95 = Percentile(sorted, 0.95);
        TestContext.Current.TestOutputHelper?.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"{route}: {Requests} peticiones, {Concurrency} simultaneas, p50 {p50:F1} ms, p95 {p95:F1} ms"));

        Assert.Equal(0, failures);
        Assert.True(p95 <= Threshold(), string.Create(CultureInfo.InvariantCulture, $"{route}: p95 {p95:F1} ms supera {Threshold()} ms"));
    }
}

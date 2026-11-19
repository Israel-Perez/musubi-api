using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;

namespace Musubi.TestCoverage;

/// <summary>
/// Cobertura de endpoints por suite (SCRUM-120). Escucha el evento de ASP.NET Core
/// <c>Microsoft.AspNetCore.Routing.EndpointMatched</c> de todas las fabricas de la suite y, al terminar, escribe
/// <c>TestResults/endpoint-coverage/&lt;Suite&gt;.txt</c> con una linea <c>VERBO plantilla</c> por endpoint que alguna
/// prueba toco. <c>scripts/check_endpoint_tests.ps1</c> la compara con <c>docs/endpoints.md</c>: una HU nueva no
/// entra sin pruebas de integracion y de seguridad de cada endpoint. Se registra como fixture de ensamblado en
/// IntegrationTest y SecurityTest (este archivo se enlaza en los dos).
/// </summary>
public sealed class EndpointCoverage : IDisposable, IObserver<DiagnosticListener>
{
    private const string ListenerName = "Microsoft.AspNetCore";
    private const string EndpointMatched = "Microsoft.AspNetCore.Routing.EndpointMatched";

    private readonly ConcurrentDictionary<string, byte> hits = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<IDisposable> subscriptions = [];

    public EndpointCoverage() => subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));

    /// <summary>Endpoints tocados hasta ahora (<c>VERBO plantilla</c>).</summary>
    public IReadOnlyCollection<string> Hits => [.. hits.Keys];

    public void OnNext(DiagnosticListener value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Name == ListenerName)
        {
            subscriptions.Add(value.Subscribe(new MatchObserver(this), name => name == EndpointMatched));
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose()
    {
        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }

        var suite = Assembly.GetExecutingAssembly().GetName().Name!;
        var file = Path.Combine(FindRepoRoot(), "TestResults", "endpoint-coverage", suite + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllLines(file, hits.Keys.Order(StringComparer.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Musubi.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontro la raiz del repo (Musubi.slnx).");
    }

    private void Record(HttpContext context)
    {
        if (context.GetEndpoint() is not RouteEndpoint endpoint || endpoint.RoutePattern.RawText is not { } template)
        {
            return;
        }

        // Un hub de SignalR es la fila WS: su handshake es un GET (con o sin el upgrade a WebSocket).
        var verb = endpoint.Metadata.GetMetadata<HubMetadata>() is not null || context.WebSockets.IsWebSocketRequest
            ? "WS"
            : context.Request.Method.ToUpperInvariant();
        hits.TryAdd(verb + " " + template, 0);
    }

    private sealed class MatchObserver(EndpointCoverage owner) : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is HttpContext context)
            {
                owner.Record(context);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.Api.Modules.GenAi;

// HU-60: coincidencias por significado. Un embedding es un vector que acerca los textos que dicen lo mismo con otras
// palabras ("calculadora cientifica" y "Casio fx-991ES"). El proveedor va detras de ITextEmbedder (Gemini, el mismo
// de HU-32/33/37/59, RNF-15). Solo salen textos publicos del campus (el nombre y la descripcion de una publicacion o
// lo que dice una solicitud "Se busca"), sin correos, telefonos, cuentas ni enlaces (TranscriptRedactor).

/// <summary>Que papel juega el texto: lo que alguien busca o lo que se ofrece (Gemini los codifica distinto).</summary>
internal enum EmbeddingRole
{
    /// <summary>Una solicitud "Se busca" (<c>RETRIEVAL_QUERY</c>).</summary>
    Query,

    /// <summary>Una publicacion (<c>RETRIEVAL_DOCUMENT</c>).</summary>
    Document,
}

/// <summary>Contrato publico de M-10 para M-06: convierte textos en vectores.</summary>
internal interface ITextEmbedder
{
    /// <summary>Si hay proveedor configurado (sin el, las coincidencias siguen por palabras).</summary>
    bool Enabled { get; }

    /// <summary>Modelo y dimensiones; si cambian, los vectores guardados ya no sirven.</summary>
    string Model { get; }

    /// <summary>Los vectores de varios textos, en el mismo orden.</summary>
    /// <param name="texts">Los textos (ya sin datos de contacto).</param>
    /// <param name="role">Busqueda o publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los vectores, o <c>null</c> si el proveedor no respondio.</returns>
    Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken cancellationToken);
}

/// <summary>Lo comun a cualquier proveedor: el texto que se manda y la similitud.</summary>
internal static class Embeddings
{
    /// <summary>Largo maximo del texto que se manda (el nombre y la descripcion caben de sobra).</summary>
    public const int MaxText = 600;

    /// <summary>El texto de una publicacion o una solicitud, sin datos de contacto (RNF-06).</summary>
    /// <param name="title">Nombre o lo que se busca.</param>
    /// <param name="description">Descripcion (vacia en una solicitud).</param>
    /// <returns>El texto.</returns>
    public static string Text(string title, string? description)
    {
        var text = string.IsNullOrWhiteSpace(description) ? title.Trim() : $"{title.Trim()}. {description.Trim()}";
        text = TranscriptRedactor.Redact(text.ReplaceLineEndings(" "), null, null);
        return text.Length > MaxText ? text[..MaxText] : text;
    }

    /// <summary>Similitud coseno, de -1 a 1 (0 si algun vector esta vacio o no coinciden las dimensiones).</summary>
    /// <param name="a">Un vector.</param>
    /// <param name="b">Otro.</param>
    /// <returns>La similitud.</returns>
    public static double Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Count == 0 || a.Count != b.Count)
        {
            return 0;
        }

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Count; i++)
        {
            dot += a[i] * (double)b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }

        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }
}

/// <summary>
/// La IA de Development y de las pruebas: sin red y determinista. Cada palabra (normalizada, sus primeras 5 letras) va
/// a una dimension; las palabras de un mismo grupo de <see cref="Concepts"/> comparten otra con mas peso. Asi "calculadora
/// cientifica" y "Casio fx-991" se parecen sin compartir palabras, como con Gemini, pero sin llamar a nadie.
/// </summary>
internal sealed class InMemoryTextEmbedder : ITextEmbedder
{
    /// <summary>Dimensiones del vector.</summary>
    public const int Dimensions = 64;

    /// <summary>Grupos de palabras que significan lo mismo en el campus (solo para Development y pruebas).</summary>
    public static readonly IReadOnlyList<string[]> Concepts =
    [
        ["calculadora", "casio", "fx", "cientifica", "graficadora", "texas"],
        ["laptop", "computadora", "portatil", "lenovo", "macbook", "notebook"],
        ["bata", "filipina", "mandil"],
        ["arduino", "microcontrolador", "raspberry", "protoboard"],
        ["libro", "stewart", "serway", "edicion"],
    ];

    /// <inheritdoc />
    public bool Enabled => true;

    /// <inheritdoc />
    public string Model => "en-memoria-64";

    /// <inheritdoc />
    public Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return Task.FromResult<IReadOnlyList<float[]>?>([.. texts.Select(Vector)]);
    }

    private static float[] Vector(string text)
    {
        var vector = new float[Dimensions];
        foreach (var word in SearchWord.Words(text))
        {
            vector[Slot(word)] += 1;
            var concept = Concepts.Select((group, i) => (group, i)).FirstOrDefault(c => c.group.Any(g => word.StartsWith(g, StringComparison.Ordinal) || g.StartsWith(word, StringComparison.Ordinal) && word.Length >= 4));
            if (concept.group is not null)
            {
                vector[concept.i] += 3;
            }
        }

        return vector;
    }

    // Las primeras dimensiones son de los conceptos; las demas, de las palabras.
    private static int Slot(string word)
    {
        var stem = word.Length > 5 ? word[..5] : word;
        var hash = 17;
        foreach (var c in stem)
        {
            hash = unchecked((hash * 31) + c);
        }

        var free = Dimensions - Concepts.Count;
        return Concepts.Count + (int)((uint)hash % (uint)free);
    }
}

/// <summary>Sin proveedor: nunca sale nada del servidor y las coincidencias siguen por palabras (RNF-15).</summary>
internal sealed class OffTextEmbedder : ITextEmbedder
{
    /// <inheritdoc />
    public bool Enabled => false;

    /// <inheritdoc />
    public string Model => "off";

    /// <inheritdoc />
    public Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<float[]>?>(null);
}

/// <summary>
/// Adaptador de Gemini (<c>batchEmbedContents</c>, hasta 100 textos por llamada). Cualquier falla devuelve <c>null</c>
/// y se registra solo el motivo, nunca los textos (RNF-15). Seguro entre hilos: pide un cliente por llamada.
/// </summary>
internal sealed partial class GeminiTextEmbedder(
    IHttpClientFactory clients,
    IOptions<GenAiOptions> options,
    IOptions<GeminiOptions> gemini,
    ILogger<GeminiTextEmbedder> logger) : ITextEmbedder
{
    /// <summary>Nombre del cliente HTTP.</summary>
    public const string ClientName = "gemini-embeddings";

    /// <summary>Textos por llamada (el limite de Gemini).</summary>
    public const int BatchSize = 100;

    /// <inheritdoc />
    public bool Enabled => !string.IsNullOrWhiteSpace(gemini.Value.ApiKey);

    /// <inheritdoc />
    public string Model => $"{options.Value.EmbeddingModel}:{options.Value.EmbeddingDimensions.ToString(CultureInfo.InvariantCulture)}";

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (!Enabled)
        {
            return null;
        }

        var all = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            var vectors = await BatchAsync(batch, role, cancellationToken).ConfigureAwait(false);
            if (vectors is null)
            {
                return null;
            }

            all.AddRange(vectors);
        }

        return all;
    }

    /// <summary>El cuerpo de <c>batchEmbedContents</c> (expuesto para las pruebas).</summary>
    /// <param name="texts">Los textos.</param>
    /// <param name="role">Busqueda o publicacion.</param>
    /// <param name="model">El modelo.</param>
    /// <param name="dimensions">Dimensiones del vector.</param>
    /// <returns>El cuerpo.</returns>
    internal static object Body(IReadOnlyList<string> texts, EmbeddingRole role, string model, int dimensions) => new
    {
        requests = texts.Select(text => new
        {
            model = $"models/{model}",
            content = new { parts = new[] { new { text } } },
            taskType = role == EmbeddingRole.Query ? "RETRIEVAL_QUERY" : "RETRIEVAL_DOCUMENT",
            outputDimensionality = dimensions,
        }).ToArray(),
    };

    private async Task<IReadOnlyList<float[]>?> BatchAsync(string[] texts, EmbeddingRole role, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"models/{Uri.EscapeDataString(settings.EmbeddingModel)}:batchEmbedContents", UriKind.Relative))
        {
            Content = JsonContent.Create(Body(texts, role, settings.EmbeddingModel, settings.EmbeddingDimensions), options: GeminiApi.Json),
        };
        request.Headers.Add("x-goog-api-key", gemini.Value.ApiKey);
        try
        {
            using var http = clients.CreateClient(ClientName);
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogUnavailable(logger, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                return null;
            }

            var reply = await response.Content.ReadFromJsonAsync<BatchResponse>(GeminiApi.Json, timeout.Token).ConfigureAwait(false);
            if (reply?.Embeddings is not { } embeddings || embeddings.Count != texts.Length
                || embeddings.Any(e => e.Values is not { Count: > 0 } values || values.Count != settings.EmbeddingDimensions))
            {
                LogUnavailable(logger, "forma distinta");
                return null;
            }

            return [.. embeddings.Select(e => e.Values!.ToArray())];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnavailable(logger, ex.GetType().Name);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini no dio los vectores ({Reason}): las coincidencias siguen por palabras")]
    private static partial void LogUnavailable(ILogger logger, string reason);

    private sealed record BatchResponse(IReadOnlyList<Embedding>? Embeddings);

    private sealed record Embedding(IReadOnlyList<float>? Values);
}

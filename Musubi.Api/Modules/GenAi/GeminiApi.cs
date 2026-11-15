using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Musubi.Api.Modules.GenAi;

/// <summary>
/// Lo que devolvio Gemini. Solo uno de los campos tiene valor: el texto, el estado HTTP que no fue 2xx, el motivo del
/// bloqueo o el tipo de la excepcion (sin red o tiempo agotado). Nunca trae lo que se envio (RNF-06).
/// </summary>
/// <param name="Text">El texto de la primera parte de la respuesta.</param>
/// <param name="Status">El estado HTTP cuando no fue exitoso (429: cuota excedida).</param>
/// <param name="Blocked">El motivo del bloqueo, o <c>sin respuesta</c>.</param>
/// <param name="Error">El tipo de la excepcion.</param>
internal sealed record GeminiReply(string? Text, int? Status = null, string? Blocked = null, string? Error = null);

/// <summary>
/// La llamada REST <c>generateContent</c> que comparten los adaptadores de Gemini (el acuerdo de HU-33 y las ideas para
/// vender de HU-37). Cada adaptador arma el cuerpo, lee el texto y registra su propio motivo (RNF-15).
/// </summary>
internal static class GeminiApi
{
    /// <summary>Base de la API (fija: no se configura, asi no se puede apuntar a otro host).</summary>
    public static readonly Uri BaseAddress = new("https://generativelanguage.googleapis.com/v1beta/");

    /// <summary>Serializacion del cuerpo y de la respuesta.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Manda el cuerpo al modelo configurado.</summary>
    /// <param name="http">Cliente con <see cref="BaseAddress"/>.</param>
    /// <param name="options">Modelo y espera maxima.</param>
    /// <param name="apiKey">La llave (va en el header <c>x-goog-api-key</c>, nunca en la URL).</param>
    /// <param name="body">El cuerpo de <c>generateContent</c>.</param>
    /// <param name="cancellationToken">Cancelacion de quien llama.</param>
    /// <returns>El texto o el motivo por el que no hay.</returns>
    public static async Task<GeminiReply> GenerateAsync(HttpClient http, GenAiOptions options, string apiKey, object body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"models/{Uri.EscapeDataString(options.Model)}:generateContent", UriKind.Relative))
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Add("x-goog-api-key", apiKey);
        try
        {
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new GeminiReply(null, Status: (int)response.StatusCode);
            }

            var reply = await response.Content.ReadFromJsonAsync<GenerateResponse>(Json, timeout.Token).ConfigureAwait(false);
            if (reply?.Candidates is not [var candidate, ..])
            {
                return new GeminiReply(null, Blocked: reply?.PromptFeedback?.BlockReason ?? "sin respuesta");
            }

            if (candidate.FinishReason is not (null or "STOP"))
            {
                return new GeminiReply(null, Blocked: candidate.FinishReason);
            }

            return new GeminiReply(candidate.Content?.Parts is [var part, ..] ? part.Text : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new GeminiReply(null, Error: ex.GetType().Name);
        }
    }

    /// <summary>Un campo de texto del esquema de respuesta.</summary>
    /// <param name="type">Tipo de Gemini (<c>STRING</c>, <c>NUMBER</c>, <c>INTEGER</c>).</param>
    /// <param name="nullable">Si puede venir <c>null</c>.</param>
    /// <param name="description">Pista para el modelo.</param>
    /// <returns>El campo.</returns>
    public static Dictionary<string, object> Field(string type, bool nullable, string? description = null)
    {
        var field = new Dictionary<string, object> { ["type"] = type };
        if (nullable)
        {
            field["nullable"] = true;
        }

        if (description is not null)
        {
            field["description"] = description;
        }

        return field;
    }

    private sealed record GenerateResponse(IReadOnlyList<Candidate>? Candidates, PromptFeedback? PromptFeedback);

    private sealed record Candidate(Content? Content, string? FinishReason);

    private sealed record Content(IReadOnlyList<Part>? Parts);

    private sealed record Part(string? Text);

    private sealed record PromptFeedback(string? BlockReason);
}

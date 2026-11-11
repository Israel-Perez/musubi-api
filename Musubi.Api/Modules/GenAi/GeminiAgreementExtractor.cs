using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Modules.GenAi;

/// <summary>Configuracion de la IA (seccion <c>GenAi</c>; la llave va aparte en <c>Gemini:ApiKey</c>).</summary>
internal sealed class GenAiOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "GenAi";

    /// <summary>La API de Gemini (capa gratuita, decision de la usuaria del 10-Nov-2026).</summary>
    public const string GeminiProvider = "Gemini";

    /// <summary>Development y pruebas: <see cref="InMemoryAgreementExtractor"/>, sin red.</summary>
    public const string InMemoryProvider = "InMemory";

    /// <summary>Sin IA: "Resumir acuerdo" responde <c>genai.unavailable</c> y la app abre el resumen a mano.</summary>
    public const string OffProvider = "Off";

    /// <summary><c>Gemini</c>, <c>InMemory</c> u <c>Off</c>.</summary>
    public string Provider { get; set; } = GeminiProvider;

    /// <summary>
    /// Modelo de Gemini. Flash-Lite: la cuota gratuita mas amplia y ~1.2 s por resumen (medido el 10-Nov-2026 con
    /// <c>gemini-3.5-flash-lite</c>, 234 tokens).
    /// </summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>Espera maxima de la respuesta.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Mensajes de texto que se mandan como maximo (los mas recientes).</summary>
    public int MaxMessages { get; set; } = 60;

    /// <summary>Resumenes por persona en la ventana (protege la cuota gratuita, OWASP API4).</summary>
    public int PermitLimit { get; set; } = 6;

    /// <summary>Ventana del limite, en minutos.</summary>
    public int WindowMinutes { get; set; } = 60;
}

/// <summary>La llave de Gemini (user-secrets en local; Parameter Store <c>/musubi/prod/Gemini/ApiKey</c> en la nube).</summary>
internal sealed class GeminiOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Gemini";

    /// <summary>La llave de la API (secreto: nunca en appsettings versionados ni en los logs).</summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Adaptador de Gemini (API REST <c>generateContent</c> con salida JSON y esquema). Cualquier falla (sin red, tiempo
/// agotado, cuota excedida 429, respuesta bloqueada o con otra forma) devuelve <c>null</c> y se registra solo el
/// motivo, nunca el chat (RNF-15, RNF-06).
/// </summary>
internal sealed partial class GeminiAgreementExtractor(
    HttpClient http,
    IOptions<GenAiOptions> options,
    IOptions<GeminiOptions> gemini,
    ILogger<GeminiAgreementExtractor> logger) : IAgreementExtractor
{
    /// <summary>Base de la API (fija: no se configura, asi no se puede apuntar a otro host).</summary>
    public static readonly Uri BaseAddress = new("https://generativelanguage.googleapis.com/v1beta/");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public bool Enabled => !string.IsNullOrWhiteSpace(gemini.Value.ApiKey);

    /// <inheritdoc />
    public async Task<ExtractedAgreement?> ExtractAsync(AgreementPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (!Enabled)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"models/{Uri.EscapeDataString(options.Value.Model)}:generateContent", UriKind.Relative))
        {
            Content = JsonContent.Create(Body(prompt), options: Json),
        };
        request.Headers.Add("x-goog-api-key", gemini.Value.ApiKey);
        try
        {
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<GenerateResponse>(Json, timeout.Token).ConfigureAwait(false);
            if (body?.Candidates is not [var candidate, ..])
            {
                LogBlocked(logger, body?.PromptFeedback?.BlockReason ?? "sin respuesta");
                return null;
            }

            if (candidate.FinishReason is not (null or "STOP"))
            {
                LogBlocked(logger, candidate.FinishReason);
                return null;
            }

            var parsed = ExtractionJson.Parse(candidate.Content?.Parts is [var part, ..] ? part.Text : null);
            if (parsed is null)
            {
                LogBlocked(logger, "forma distinta");
            }

            return parsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnavailable(logger, ex.GetType().Name);
            return null;
        }
    }

    private static object Body(AgreementPrompt prompt)
    {
        static Dictionary<string, object> Nullable(string type, string? description = null)
        {
            var field = new Dictionary<string, object> { ["type"] = type, ["nullable"] = true };
            if (description is not null)
            {
                field["description"] = description;
            }

            return field;
        }

        var point = Nullable("STRING", "id del punto de entrega");
        if (prompt.Points.Count > 0)
        {
            point["enum"] = prompt.Points.Select(p => p.Slug).ToArray();
        }

        return new
        {
            systemInstruction = new { parts = new[] { new { text = AgreementPromptText.System } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = AgreementPromptText.User(prompt) } } } },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = 0,
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new Dictionary<string, object>
                    {
                        ["product"] = Nullable("STRING"),
                        ["priceMxn"] = Nullable("NUMBER", "precio por pieza en pesos"),
                        ["quantity"] = Nullable("INTEGER"),
                        ["meetingPoint"] = point,
                        ["meetingDetail"] = Nullable("STRING"),
                        ["date"] = Nullable("STRING", "AAAA-MM-DD"),
                        ["time"] = Nullable("STRING", "HH:MM de 24 horas"),
                        ["conditions"] = Nullable("STRING"),
                    },
                    required = ExtractionJson.Fields,
                    propertyOrdering = ExtractionJson.Fields,
                },
            },
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini respondio {Status}: el resumen se llena a mano")]
    private static partial void LogFailed(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini no dio una propuesta ({Reason}): el resumen se llena a mano")]
    private static partial void LogBlocked(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gemini no disponible ({Error}): el resumen se llena a mano")]
    private static partial void LogUnavailable(ILogger logger, string error);

    private sealed record GenerateResponse(IReadOnlyList<Candidate>? Candidates, PromptFeedback? PromptFeedback);

    private sealed record Candidate(Content? Content, string? FinishReason);

    private sealed record Content(IReadOnlyList<Part>? Parts);

    private sealed record Part(string? Text);

    private sealed record PromptFeedback(string? BlockReason);
}

/// <summary>Sin proveedor: nunca sale nada del servidor.</summary>
internal sealed class OffAgreementExtractor : IAgreementExtractor
{
    /// <inheritdoc />
    public bool Enabled => false;

    /// <inheritdoc />
    public Task<ExtractedAgreement?> ExtractAsync(AgreementPrompt prompt, CancellationToken cancellationToken) =>
        Task.FromResult<ExtractedAgreement?>(null);
}

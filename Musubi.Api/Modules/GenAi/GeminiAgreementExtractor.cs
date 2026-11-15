using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    /// <summary>Base de la API (la de <see cref="GeminiApi"/>).</summary>
    public static readonly Uri BaseAddress = GeminiApi.BaseAddress;

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

        var reply = await GeminiApi.GenerateAsync(http, options.Value, gemini.Value.ApiKey, Body(prompt), cancellationToken).ConfigureAwait(false);
        if (reply.Status is { } status)
        {
            LogFailed(logger, status);
            return null;
        }

        if (reply.Error is { } error)
        {
            LogUnavailable(logger, error);
            return null;
        }

        if (reply.Blocked is { } blocked)
        {
            LogBlocked(logger, blocked);
            return null;
        }

        var parsed = ExtractionJson.Parse(reply.Text);
        if (parsed is null)
        {
            LogBlocked(logger, "forma distinta");
        }

        return parsed;
    }

    private static object Body(AgreementPrompt prompt)
    {
        static Dictionary<string, object> Nullable(string type, string? description = null) => GeminiApi.Field(type, nullable: true, description);

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

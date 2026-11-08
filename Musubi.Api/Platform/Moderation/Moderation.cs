using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Musubi.Api.Platform.Errors;
using SkiaSharp;

namespace Musubi.Api.Platform.Moderation;

/// <summary>Configuracion de la moderacion (seccion <c>Moderation</c>; 02-07).</summary>
internal sealed class ModerationOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Moderation";

    /// <summary>Solo Development y pruebas: reglas deterministas, sin llamar a AWS.</summary>
    public const string InMemoryProvider = "InMemory";

    /// <summary>Rekognition (fotos); como <see cref="TextProvider"/>, ademas Translate + Comprehend (texto).</summary>
    public const string AwsProvider = "Aws";

    /// <summary>Texto solo con el lexico local (<see cref="SpanishLexicon"/>), sin costo.</summary>
    public const string LexiconTextProvider = "Lexicon";

    /// <summary><c>InMemory</c> o <c>Aws</c>.</summary>
    public string Provider { get; set; } = AwsProvider;

    /// <summary>
    /// Texto con el proveedor <c>Aws</c>: <c>Lexicon</c> (por defecto) o <c>Aws</c> (el lexico y despues
    /// Translate + Comprehend). El plan gratuito de AWS no incluye Translate ni Comprehend (08-Nov-2026).
    /// </summary>
    public string TextProvider { get; set; } = LexiconTextProvider;

    /// <summary>Palabras o frases que se suman al lexico sin desplegar (una frase lleva espacios).</summary>
    public IReadOnlyList<string> ExtraBlockedTerms { get; set; } = [];

    /// <summary>Region de los servicios de AWS.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Confianza minima (0-100) de una etiqueta de Rekognition para rechazar la foto.</summary>
    public float MinImageConfidence { get; set; } = 80;

    /// <summary>Toxicidad minima (0-1) de Comprehend para rechazar un texto (solo con <c>TextProvider=Aws</c>).</summary>
    public float MaxTextToxicity { get; set; } = 0.7f;

    /// <summary>
    /// Etiquetas de Rekognition que rechazan una foto (nivel 1 de la taxonomia y nombres anteriores). Se
    /// permiten, por ejemplo, ropa de bano o alcohol: un marketplace de campus vende ropa y vasos.
    /// </summary>
    public IReadOnlyList<string> BlockedImageLabels { get; set; } =
    [
        "Explicit",
        "Explicit Nudity",
        "Non-Explicit Nudity of Intimate parts and Kissing",
        "Violence",
        "Visually Disturbing",
        "Drugs & Tobacco",
        "Drugs",
        "Hate Symbols",
        "Rude Gestures",
    ];
}

/// <summary>Fail-fast: fuera de Development solo AWS (nada se publica sin moderar en la nube).</summary>
internal sealed class ModerationOptionsValidator(IHostEnvironment environment) : IValidateOptions<ModerationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ModerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TextProvider is not (ModerationOptions.LexiconTextProvider or ModerationOptions.AwsProvider))
        {
            return ValidateOptionsResult.Fail("Moderation:TextProvider debe ser Lexicon o Aws.");
        }

        return options.Provider switch
        {
            ModerationOptions.AwsProvider => ValidateOptionsResult.Success,
            ModerationOptions.InMemoryProvider when environment.IsDevelopment() => ValidateOptionsResult.Success,
            ModerationOptions.InMemoryProvider => ValidateOptionsResult.Fail("Moderation:Provider=InMemory solo se permite en Development."),
            _ => ValidateOptionsResult.Fail("Moderation:Provider debe ser InMemory o Aws."),
        };
    }
}

/// <summary>Resultado de la moderacion.</summary>
internal enum ModerationVerdict
{
    /// <summary>Se puede guardar.</summary>
    Allowed,

    /// <summary>Inapropiado: no se guarda.</summary>
    Rejected,

    /// <summary>El proveedor no respondio: no se guarda (nunca se publica sin moderar).</summary>
    Unavailable,
}

/// <summary>
/// Revisa texto y fotos <b>antes</b> de guardarlos (02-07, requisito 16 de la materia). Las decisiones se
/// registran sin el contenido (solo el motivo), para no guardar lo rechazado.
/// </summary>
internal interface IContentModerator
{
    /// <summary>Revisa una foto (JPEG o PNG, como la pide Rekognition).</summary>
    /// <param name="image">Bytes de la foto.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La decision.</returns>
    Task<ModerationVerdict> CheckImageAsync(byte[] image, CancellationToken cancellationToken);

    /// <summary>Revisa textos en espanol (titulo, descripcion, mensajes).</summary>
    /// <param name="texts">Textos; los vacios se ignoran.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La decision para cada texto, en el mismo orden.</returns>
    Task<IReadOnlyList<ModerationVerdict>> CheckTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

/// <summary>
/// Moderacion para Development y pruebas: rechaza el marcador <see cref="RejectMarker"/> o el lexico local (en
/// un texto, como en produccion) y las fotos magenta (<c>#FF00FF</c> en el centro), para probar el rechazo de
/// punta a punta. Nunca llama a AWS.
/// </summary>
/// <param name="lexicon">Lexico local; por defecto <see cref="SpanishLexicon.Default"/>.</param>
internal sealed class InMemoryContentModerator(SpanishLexicon? lexicon = null) : IContentModerator
{
    private readonly SpanishLexicon lexicon = lexicon ?? SpanishLexicon.Default;

    /// <summary>Un texto con este marcador se rechaza.</summary>
    public const string RejectMarker = "[[inapropiado]]";

    /// <inheritdoc />
    public Task<ModerationVerdict> CheckImageAsync(byte[] image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var bitmap = SKBitmap.Decode(image);
        var center = bitmap?.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        var magenta = center is { } c && c.Red > 230 && c.Green < 30 && c.Blue > 230;
        return Task.FromResult(magenta ? ModerationVerdict.Rejected : ModerationVerdict.Allowed);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModerationVerdict>> CheckTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return Task.FromResult<IReadOnlyList<ModerationVerdict>>(texts
            .Select(t => t is not null && (t.Contains(RejectMarker, StringComparison.OrdinalIgnoreCase) || lexicon.IsOffensive(t))
                ? ModerationVerdict.Rejected
                : ModerationVerdict.Allowed)
            .ToList());
    }
}

/// <summary>Errores de la moderacion (catalogo canonico: docs/errores.md).</summary>
internal static class ModerationErrors
{
    /// <summary>Foto inapropiada.</summary>
    public static readonly ApiError ImageRejected = new(
        "moderation.image_rejected", StatusCodes.Status422UnprocessableEntity, "Esta foto no cumple las reglas de la comunidad. Usa otra.");

    /// <summary>El servicio de moderacion no respondio.</summary>
    public static readonly ApiError Unavailable = new(
        "moderation.unavailable", StatusCodes.Status503ServiceUnavailable, "No pudimos revisar tu contenido. Inténtalo en unos minutos.");

    /// <summary>Texto inapropiado en un campo.</summary>
    /// <param name="field">Campo en espanol ("el nombre", "la descripción").</param>
    /// <returns>El error con el campo en el mensaje.</returns>
    public static ApiError TextRejected(string field) => new(
        "moderation.text_rejected", StatusCodes.Status422UnprocessableEntity, $"Revisa {field}: no cumple las reglas de la comunidad.");

    /// <summary>El error de una decision, o <c>null</c> si se permite.</summary>
    /// <param name="verdict">Decision.</param>
    /// <param name="rejected">Error si se rechazo.</param>
    /// <returns>El error o <c>null</c>.</returns>
    public static ApiError? For(ModerationVerdict verdict, ApiError rejected) => verdict switch
    {
        ModerationVerdict.Rejected => rejected,
        ModerationVerdict.Unavailable => Unavailable,
        _ => null,
    };
}

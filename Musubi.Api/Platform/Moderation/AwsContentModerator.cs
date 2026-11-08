using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Comprehend;
using Amazon.Comprehend.Model;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Runtime;
using Amazon.Translate;
using Amazon.Translate.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Platform.Moderation;

/// <summary>
/// Moderacion de produccion (02-07): Rekognition <c>DetectModerationLabels</c> para fotos y, para texto, el
/// lexico local (<see cref="SpanishLexicon"/>). Con <c>TextProvider=Aws</c>, lo que el lexico deja pasar va
/// ademas a Translate (espanol a ingles) + Comprehend <c>DetectToxicContent</c>, porque la toxicidad de
/// Comprehend solo acepta ingles. Si AWS no responde, la decision es <see cref="ModerationVerdict.Unavailable"/>.
/// </summary>
internal sealed partial class AwsContentModerator(
    IAmazonRekognition rekognition,
    IAmazonTranslate translate,
    IAmazonComprehend comprehend,
    SpanishLexicon lexicon,
    IOptions<ModerationOptions> options,
    ILogger<AwsContentModerator> logger) : IContentModerator
{
    /// <inheritdoc />
    public async Task<ModerationVerdict> CheckImageAsync(byte[] image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        try
        {
            using var bytes = new MemoryStream(image, writable: false);
            var response = await rekognition.DetectModerationLabelsAsync(
                new DetectModerationLabelsRequest
                {
                    Image = new Image { Bytes = bytes },
                    MinConfidence = options.Value.MinImageConfidence,
                },
                cancellationToken).ConfigureAwait(false);
            var blocked = BlockedLabel(response.ModerationLabels ?? [], options.Value);
            if (blocked is null)
            {
                return ModerationVerdict.Allowed;
            }

            LogRejected(logger, "imagen", blocked);
            return ModerationVerdict.Rejected;
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException)
        {
            LogUnavailable(logger, ex, "Rekognition");
            return ModerationVerdict.Unavailable;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModerationVerdict>> CheckTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var verdicts = Enumerable.Repeat(ModerationVerdict.Allowed, texts.Count).ToArray();
        var present = new List<int>(texts.Count);
        for (var i = 0; i < texts.Count; i++)
        {
            if (lexicon.IsOffensive(texts[i]))
            {
                verdicts[i] = ModerationVerdict.Rejected;
                LogRejected(logger, "texto", "lexico");
            }
            else if (!string.IsNullOrWhiteSpace(texts[i]))
            {
                present.Add(i);
            }
        }

        if (present.Count == 0 || options.Value.TextProvider != ModerationOptions.AwsProvider)
        {
            return verdicts;
        }

        try
        {
            var english = new List<string>(present.Count);
            foreach (var i in present)
            {
                var translated = await translate.TranslateTextAsync(
                    new TranslateTextRequest { Text = texts[i], SourceLanguageCode = "es", TargetLanguageCode = "en" },
                    cancellationToken).ConfigureAwait(false);
                english.Add(translated.TranslatedText);
            }

            var toxic = await comprehend.DetectToxicContentAsync(
                new DetectToxicContentRequest
                {
                    LanguageCode = LanguageCode.En,
                    TextSegments = english.Select(t => new TextSegment { Text = t }).ToList(),
                },
                cancellationToken).ConfigureAwait(false);
            var results = toxic.ResultList ?? [];
            for (var r = 0; r < results.Count && r < present.Count; r++)
            {
                if (IsToxic(results[r].Toxicity, options.Value))
                {
                    verdicts[present[r]] = ModerationVerdict.Rejected;
                    LogRejected(logger, "texto", "toxicidad");
                }
            }

            return verdicts;
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException)
        {
            LogUnavailable(logger, ex, "Translate/Comprehend");
            foreach (var i in present)
            {
                verdicts[i] = ModerationVerdict.Unavailable;
            }

            return verdicts;
        }
    }

    /// <summary>La primera etiqueta prohibida (por nombre o por su padre), o <c>null</c>.</summary>
    /// <param name="labels">Etiquetas de Rekognition (ya filtradas por confianza).</param>
    /// <param name="options">Configuracion.</param>
    /// <returns>El nombre de la etiqueta o <c>null</c>.</returns>
    public static string? BlockedLabel(IEnumerable<ModerationLabel> labels, ModerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(options);
        return labels
            .Where(l => (l.Confidence ?? 0) >= options.MinImageConfidence)
            .Select(l => options.BlockedImageLabels.FirstOrDefault(b =>
                string.Equals(b, l.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(b, l.ParentName, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(b => b is not null);
    }

    /// <summary>Indica si la toxicidad general alcanza el umbral.</summary>
    /// <param name="toxicity">Toxicidad de Comprehend (0-1).</param>
    /// <param name="options">Configuracion.</param>
    /// <returns><c>true</c> si se rechaza.</returns>
    public static bool IsToxic(float? toxicity, ModerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return (toxicity ?? 0) >= options.MaxTextToxicity;
    }

    // Sin el contenido rechazado (R11, RNF-05): solo el tipo y el motivo.
    [LoggerMessage(Level = LogLevel.Information, Message = "Moderacion: {Kind} rechazado ({Reason})")]
    private static partial void LogRejected(ILogger logger, string kind, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moderacion no disponible ({Service}): el contenido no se guarda")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string service);
}

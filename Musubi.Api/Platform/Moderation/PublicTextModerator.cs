using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Musubi.Api.Platform.Moderation;

/// <summary>
/// SCRUM-127 (lineamiento 16: el texto se evalua con una API de la nube antes de guardarse). El texto publico pasa
/// primero por el moderador de siempre (el lexico) y lo que este deja pasar va a un <see cref="ITextSafetyClassifier"/>
/// en la nube (Gemini). El texto privado (chat, acuerdo, pedido) y las fotos no cambian: nunca salen a Gemini.
/// Si el clasificador no responde (cuota, red o tiempo agotado), decide el lexico: la app sigue funcionando (RNF-15)
/// y lo inapropiado mas comun ya lo detiene el lexico.
/// </summary>
internal sealed partial class PublicTextModerator(
    IContentModerator inner,
    ITextSafetyClassifier classifier,
    ILogger<PublicTextModerator> logger) : IContentModerator
{
    /// <inheritdoc />
    public Task<ModerationVerdict> CheckImageAsync(byte[] image, CancellationToken cancellationToken) =>
        inner.CheckImageAsync(image, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ModerationVerdict>> CheckTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
        inner.CheckTextsAsync(texts, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModerationVerdict>> CheckPublicTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var verdicts = (await inner.CheckPublicTextsAsync(texts, cancellationToken).ConfigureAwait(false)).ToArray();
        var pending = Enumerable.Range(0, texts.Count)
            .Where(i => verdicts[i] == ModerationVerdict.Allowed && !string.IsNullOrWhiteSpace(texts[i]))
            .ToList();
        if (pending.Count == 0)
        {
            return verdicts;
        }

        var flags = await classifier.FindInappropriateAsync([.. pending.Select(i => texts[i])], cancellationToken).ConfigureAwait(false);
        if (flags is null || flags.Count != pending.Count)
        {
            LogLexiconOnly(logger);
            return verdicts;
        }

        for (var k = 0; k < pending.Count; k++)
        {
            if (flags[k])
            {
                verdicts[pending[k]] = ModerationVerdict.Rejected;
                LogRejected(logger);
            }
        }

        return verdicts;
    }

    // Sin el contenido (R11, RNF-05): solo el motivo.
    [LoggerMessage(Level = LogLevel.Information, Message = "Moderacion: texto publico rechazado (IA en la nube)")]
    private static partial void LogRejected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moderacion: la IA no respondio; el texto publico quedo solo con el lexico")]
    private static partial void LogLexiconOnly(ILogger logger);
}

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Musubi.Api.Platform.Email;

namespace Musubi.TestShared;

/// <summary>Correo de pruebas: guarda los mensajes en lugar de enviarlos y extrae el codigo de 6 digitos.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "TestShared se enlaza en varias suites; no todas lo usan.")]
internal sealed partial class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Ultimo codigo enviado a ese correo.</summary>
    public string LastCodeFor(string email) =>
        Sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase))
            .Select(m => SixDigits().Match(m.TextBody).Value)
            .Last(code => code.Length == 6);

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}

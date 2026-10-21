using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Platform.Email;

/// <summary>Correo a enviar (texto obligatorio; HTML opcional).</summary>
/// <param name="To">Destinatario.</param>
/// <param name="Subject">Asunto.</param>
/// <param name="TextBody">Cuerpo en texto plano.</param>
/// <param name="HtmlBody">Cuerpo HTML opcional.</param>
internal sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>Envio de correo (02-03). La HU-03 lo usa para el codigo de verificacion.</summary>
internal interface IEmailSender
{
    /// <summary>Envia el correo.</summary>
    /// <param name="message">El correo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Configuracion del correo (seccion <c>Email</c>).</summary>
internal sealed class EmailOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Email";

    /// <summary>Solo Development: escribe el correo en el log.</summary>
    public const string LogProvider = "Log";

    /// <summary>Amazon SES (nube).</summary>
    public const string SesProvider = "Ses";

    /// <summary><c>Log</c> o <c>Ses</c>.</summary>
    public string Provider { get; set; } = SesProvider;

    /// <summary>Remitente verificado en SES (<c>Musubi &lt;no-reply@dominio&gt;</c>).</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Region de SES.</summary>
    public string Region { get; set; } = "us-east-1";
}

/// <summary>Fail-fast: fuera de Development solo SES y con remitente (un correo con codigo nunca termina en un log de produccion).</summary>
internal sealed class EmailOptionsValidator(IHostEnvironment environment) : IValidateOptions<EmailOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Provider == EmailOptions.LogProvider)
        {
            return environment.IsDevelopment()
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("Email:Provider=Log solo se permite en Development.");
        }

        if (options.Provider != EmailOptions.SesProvider)
        {
            return ValidateOptionsResult.Fail("Email:Provider debe ser Log o Ses.");
        }

        return string.IsNullOrWhiteSpace(options.From) || string.IsNullOrWhiteSpace(options.Region)
            ? ValidateOptionsResult.Fail("Email:From y Email:Region son obligatorios con SES.")
            : ValidateOptionsResult.Success;
    }
}

/// <summary>Development: escribe el correo en el log para ver el codigo sin enviar nada.</summary>
internal sealed partial class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogEmail(logger, message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Correo (modo Log, solo Development) para {To}: {Subject}\n{Body}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string body);
}

/// <summary>Costura minima sobre el SDK de SES para poder probar el mapeo sin AWS.</summary>
internal interface ISesClient
{
    /// <summary>Envia la solicitud a SES.</summary>
    /// <param name="request">Solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task SendAsync(SendEmailRequest request, CancellationToken cancellationToken);
}

/// <summary>Cliente real de SES v2 (credenciales por el rol de la instancia; nunca llaves en config).</summary>
internal sealed class AwsSesClient(IAmazonSimpleEmailServiceV2 ses) : ISesClient
{
    /// <inheritdoc />
    public Task SendAsync(SendEmailRequest request, CancellationToken cancellationToken) => ses.SendEmailAsync(request, cancellationToken);
}

/// <summary>Envio con Amazon SES (lineamiento 15: los servicios en la nube elegida).</summary>
internal sealed class SesEmailSender(ISesClient client, IOptions<EmailOptions> options) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = new Body { Text = new Content { Data = message.TextBody, Charset = "UTF-8" } };
        if (message.HtmlBody is not null)
        {
            body.Html = new Content { Data = message.HtmlBody, Charset = "UTF-8" };
        }

        return client.SendAsync(
            new SendEmailRequest
            {
                FromEmailAddress = options.Value.From,
                Destination = new Destination { ToAddresses = [message.To] },
                Content = new EmailContent
                {
                    Simple = new Message { Subject = new Content { Data = message.Subject, Charset = "UTF-8" }, Body = body },
                },
            },
            cancellationToken);
    }
}

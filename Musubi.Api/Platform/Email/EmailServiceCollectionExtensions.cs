using System;
using Amazon;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Platform.Email;

/// <summary>Registro del envio de correo (02-03).</summary>
internal static class EmailServiceCollectionExtensions
{
    /// <summary>Registra <see cref="IEmailSender"/>: <c>Log</c> en Development, SES fuera (validado al arrancar).</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddMusubiEmail(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();
        services.AddSingleton<LogEmailSender>();

        // El cliente de AWS solo se crea si el proveedor es SES (lazy).
        services.AddSingleton<IAmazonSimpleEmailServiceV2>(provider =>
            new AmazonSimpleEmailServiceV2Client(RegionEndpoint.GetBySystemName(provider.GetRequiredService<IOptions<EmailOptions>>().Value.Region)));
        services.AddSingleton<ISesClient>(provider => new AwsSesClient(provider.GetRequiredService<IAmazonSimpleEmailServiceV2>()));

        services.AddSingleton<IEmailSender>(provider =>
            provider.GetRequiredService<IOptions<EmailOptions>>().Value.Provider == EmailOptions.LogProvider
                ? provider.GetRequiredService<LogEmailSender>()
                : new SesEmailSender(provider.GetRequiredService<ISesClient>(), provider.GetRequiredService<IOptions<EmailOptions>>()));
    }
}

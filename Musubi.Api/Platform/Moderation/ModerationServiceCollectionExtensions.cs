using System;
using Amazon;
using Amazon.Comprehend;
using Amazon.Rekognition;
using Amazon.Translate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Platform.Moderation;

/// <summary>Registro de la moderacion (02-07).</summary>
internal static class ModerationServiceCollectionExtensions
{
    /// <summary>Registra <see cref="IContentModerator"/>: en memoria en Development, AWS fuera (validado al arrancar).</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddMusubiModeration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<ModerationOptions>().BindConfiguration(ModerationOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ModerationOptions>, ModerationOptionsValidator>();

        // Los clientes de AWS solo se crean con el proveedor AWS (lazy); las credenciales salen del rol de la instancia.
        services.AddSingleton<IContentModerator>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ModerationOptions>>();
            var lexicon = new SpanishLexicon(options.Value.ExtraBlockedTerms);
            if (options.Value.Provider == ModerationOptions.InMemoryProvider)
            {
                return new InMemoryContentModerator(lexicon);
            }

            // Crear los clientes no llama a AWS: Translate y Comprehend solo se usan con TextProvider=Aws.
            var region = RegionEndpoint.GetBySystemName(options.Value.Region);
            return new AwsContentModerator(
                new AmazonRekognitionClient(region),
                new AmazonTranslateClient(region),
                new AmazonComprehendClient(region),
                lexicon,
                options,
                provider.GetRequiredService<ILogger<AwsContentModerator>>());
        });
    }
}

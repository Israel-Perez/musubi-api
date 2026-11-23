using System;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Recommendations.Data;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Recommendations;

/// <summary>Registro DI del modulo M-11 (Recomendaciones con ML, HU-35).</summary>
internal static class RecommendationsModuleServiceCollectionExtensions
{
    /// <summary>Registra el repositorio (Mongo o en memoria, lazy), el registro de interacciones, el modelo y el servicio.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    /// <returns>La misma coleccion.</returns>
    public static IServiceCollection AddRecommendationsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var interactions = new InMemoryInteractionRepository();
        services.AddSingleton<IInteractionRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? interactions
            : new MongoInteractionRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<InteractionRecorder>();
        services.AddSingleton<IInteractionRecorder>(provider => provider.GetRequiredService<InteractionRecorder>());
        services.AddSingleton<IInteractionHistory>(provider => provider.GetRequiredService<InteractionRecorder>());
        services.AddSingleton<IAccountDataEraser>(provider => provider.GetRequiredService<InteractionRecorder>());
        services.AddSingleton<RecommendationModelStore>();
        services.AddSingleton<RecommendationService>();
        // HU-58: el precio sugerido (regresion de ML.NET por campus).
        services.AddSingleton<PriceModelStore>();
        services.AddSingleton<PriceSuggestionService>();
        return services;
    }
}

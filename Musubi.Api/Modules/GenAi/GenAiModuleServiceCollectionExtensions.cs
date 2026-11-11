using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.GenAi.Data;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.GenAi;

/// <summary>Registro DI del modulo M-10 (IA generativa).</summary>
internal static class GenAiModuleServiceCollectionExtensions
{
    /// <summary>Politica de rate limit de "Resumir acuerdo" (por usuario).</summary>
    public const string SummaryRateLimit = "genai";

    /// <summary>Registra el repositorio, el proveedor de IA (lazy, segun <c>GenAi:Provider</c>), el servicio y el borrado.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddGenAiModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<GenAiOptions>().BindConfiguration(GenAiOptions.Section);
        services.AddOptions<GeminiOptions>().BindConfiguration(GeminiOptions.Section);

        var agreements = new InMemoryAgreementRepository();
        services.AddSingleton<IAgreementRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? agreements
            : new MongoAgreementRepository(provider.GetRequiredService<IMongoDatabase>()));

        services.AddHttpClient<GeminiAgreementExtractor>(client => client.BaseAddress = GeminiAgreementExtractor.BaseAddress);
        services.AddSingleton<InMemoryAgreementExtractor>();
        // El proveedor se elige al resolverlo (las pruebas sobrescriben la configuracion despues del registro).
        services.AddTransient<IAgreementExtractor>(provider => provider.GetRequiredService<IOptions<GenAiOptions>>().Value.Provider switch
        {
            GenAiOptions.InMemoryProvider => provider.GetRequiredService<InMemoryAgreementExtractor>(),
            GenAiOptions.GeminiProvider => provider.GetRequiredService<GeminiAgreementExtractor>(),
            _ => new OffAgreementExtractor(),
        });
        services.AddTransient<AgreementService>();
        services.AddSingleton<IAgreementDirectory, AgreementDirectory>();
        services.AddSingleton<IAccountDataEraser, AgreementAccountEraser>();

        // Cada resumen gasta cuota de la capa gratuita de Gemini: un tope por usuario evita el abuso (OWASP API4).
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<GenAiOptions>>((options, genAi) =>
            options.AddPolicy(SummaryRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(MusubiClaims.Subject)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = genAi.Value.PermitLimit,
                    Window = TimeSpan.FromMinutes(genAi.Value.WindowMinutes),
                    QueueLimit = 0,
                })));
    }
}

/// <summary>Lo que el pedido necesita de un acuerdo (HU-34, RF-47).</summary>
/// <param name="Id">Id.</param>
/// <param name="ConversationId">La conversacion.</param>
/// <param name="BuyerId">Quien compra.</param>
/// <param name="SellerId">Quien vende.</param>
/// <param name="Confirmed">Si los dos lo confirmaron (RN-41).</param>
/// <param name="Title">Producto.</param>
/// <param name="PriceCents">Precio acordado por pieza.</param>
/// <param name="Quantity">Piezas.</param>
/// <param name="PointSlug">Punto de entrega.</param>
/// <param name="Detail">Precision del lugar.</param>
/// <param name="At">Fecha y hora (UTC).</param>
internal sealed record AgreementForOrder(
    string Id,
    string ConversationId,
    string BuyerId,
    string SellerId,
    bool Confirmed,
    string Title,
    long? PriceCents,
    int? Quantity,
    string? PointSlug,
    string? Detail,
    DateTime? At);

/// <summary>
/// Contrato publico de M-10 para los pedidos (Modules/README: un modulo no lee las colecciones de otro): leer el
/// acuerdo confirmado y borrarlo cuando ya es pedido (decision de la usuaria: no se conserva).
/// </summary>
internal interface IAgreementDirectory
{
    /// <summary>Un acuerdo vigente.</summary>
    /// <param name="agreementId">Id.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo o <c>null</c>.</returns>
    Task<AgreementForOrder?> FindAsync(string agreementId, CancellationToken cancellationToken);

    /// <summary>El acuerdo ya es pedido: se borra y se avisa a los dos.</summary>
    /// <param name="agreement">El acuerdo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task ConsumeAsync(AgreementForOrder agreement, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de acuerdos.</summary>
internal sealed class AgreementDirectory(IAgreementRepository agreements, IConversationNotices notices, TimeProvider time) : IAgreementDirectory
{
    /// <inheritdoc />
    public async Task<AgreementForOrder?> FindAsync(string agreementId, CancellationToken cancellationToken)
    {
        var a = await agreements.FindByIdAsync(agreementId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        return a is null
            ? null
            : new AgreementForOrder(
                a.Id,
                a.ConversationId,
                a.BuyerId,
                a.SellerId,
                a.Status == AgreementStatuses.Confirmed,
                a.Proposal.Title,
                a.Proposal.PriceCents,
                a.Proposal.Quantity,
                a.Proposal.PointSlug,
                a.Proposal.Detail,
                a.Proposal.At);
    }

    /// <inheritdoc />
    public async Task ConsumeAsync(AgreementForOrder agreement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        await agreements.DeleteAsync(agreement.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> participants = [agreement.BuyerId, agreement.SellerId];
        await notices.AgreementChangedAsync(participants, agreement.ConversationId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Borrado de una cuenta (HU-46, R11): sus acuerdos desaparecen para los dos.</summary>
internal sealed class AgreementAccountEraser(IAgreementRepository agreements) : IAccountDataEraser
{
    /// <inheritdoc />
    public Task EraseAsync(string userId, CancellationToken cancellationToken) =>
        agreements.DeleteByParticipantAsync(userId, cancellationToken);
}

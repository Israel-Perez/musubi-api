using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Recommendations.Data;

namespace Musubi.Api.Modules.Recommendations;

/// <summary>
/// Las senales que alimentan "Recomendado para ti" (decision de la usuaria, 13-Nov-2026): ver el detalle, guardar en
/// favoritos, escribirle al vendedor y comprar. No se guardan textos de busqueda.
/// </summary>
internal static class InteractionKinds
{
    /// <summary>Abrio el detalle (HU-11).</summary>
    public const string View = "view";

    /// <summary>La guardo en favoritos (HU-53).</summary>
    public const string Favorite = "favorite";

    /// <summary>Le escribio al vendedor desde la publicacion (HU-20).</summary>
    public const string Chat = "chat";

    /// <summary>La compro: pedido completado (HU-24).</summary>
    public const string Purchase = "purchase";

    /// <summary>Peso de cada senal: comprar &gt; chat &gt; favorito &gt; ver.</summary>
    /// <param name="kind">Tipo.</param>
    /// <returns>El peso.</returns>
    public static double Weight(string kind) => kind switch
    {
        Purchase => 5,
        Chat => 3,
        Favorite => 2,
        _ => 1,
    };
}

/// <summary>Una interaccion por registrar.</summary>
/// <param name="UserId">Quien.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="ProductId">Publicacion.</param>
/// <param name="Kind">Ver <see cref="InteractionKinds"/>.</param>
internal sealed record Interaction(string UserId, string CommunityId, string ProductId, string Kind);

/// <summary>
/// Contrato publico de M-11 (HU-35, RF-48) para los demas modulos: registra una interaccion. Nunca falla hacia quien lo
/// llama: si la base no responde, el detalle, el favorito, el chat o el pedido siguen su curso (RNF-15).
/// </summary>
internal interface IInteractionRecorder
{
    /// <summary>Registra una interaccion.</summary>
    /// <param name="interaction">La interaccion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task RecordAsync(Interaction interaction, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de interacciones.</summary>
internal sealed partial class InteractionRecorder(IInteractionRepository interactions, TimeProvider time, ILogger<InteractionRecorder> logger)
    : IInteractionRecorder, IAccountDataEraser
{
    /// <summary>Lo que vive el historial (decision de la usuaria, 13-Nov-2026).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    /// <inheritdoc />
    public async Task RecordAsync(Interaction interaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        if (!ObjectId.TryParse(interaction.UserId, out _)
            || !ObjectId.TryParse(interaction.CommunityId, out _)
            || !ObjectId.TryParse(interaction.ProductId, out _))
        {
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;
        try
        {
            await interactions.RecordAsync(
                interaction.UserId,
                interaction.CommunityId,
                interaction.ProductId,
                interaction.Kind,
                now,
                now + Retention,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            LogNotRecorded(logger, interaction.Kind);
        }
    }

    /// <summary>HU-46: el historial de la persona se borra con su cuenta.</summary>
    /// <param name="userId">Id de la cuenta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public Task EraseAsync(string userId, CancellationToken cancellationToken) => interactions.DeleteByUserAsync(userId, cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se registro una interaccion {Kind}: base no disponible")]
    private static partial void LogNotRecorded(ILogger logger, string kind);
}

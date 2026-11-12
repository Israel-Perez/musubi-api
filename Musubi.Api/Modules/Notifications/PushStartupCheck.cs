using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Modules.Notifications;

/// <summary>Como quedo el push al arrancar (SCRUM-96).</summary>
internal enum PushStartupState
{
    /// <summary>FCM con una cuenta de servicio legible.</summary>
    Active,

    /// <summary>Proveedor en memoria (Development y pruebas): no sale nada a los telefonos.</summary>
    InMemory,

    /// <summary><c>Firebase:ServiceAccount</c> vacio: nunca llego la llave.</summary>
    MissingKey,

    /// <summary>La llave no es JSON (p. ej. solo la primera linea de un JSON de varias lineas).</summary>
    UnreadableKey,

    /// <summary>Es JSON pero le falta <c>project_id</c>, <c>client_email</c> o <c>private_key</c>.</summary>
    IncompleteKey,
}

/// <summary>
/// SCRUM-96: al arrancar deja en el log, una sola vez, si el push sale por FCM o por que no. Sin esto, una llave que
/// falta o que llega cortada apaga el push sin ningun rastro (11-Nov-2026: la llave estaba en otra region y en
/// varias lineas, y la API arranco sin push y sin advertencias). Nunca imprime la llave: solo su estado, el proyecto
/// y el inicio del id de la llave.
/// </summary>
internal sealed partial class PushStartupCheck(
    IOptions<FirebaseOptions> options,
    IHostEnvironment environment,
    ILogger<PushStartupCheck> logger) : IHostedService
{
    /// <summary>Clasifica la configuracion sin imprimirla.</summary>
    /// <param name="options">La seccion <c>Firebase</c>.</param>
    /// <returns>El estado, el proyecto y el inicio del id de la llave (o <c>null</c>).</returns>
    internal static (PushStartupState State, string? ProjectId, string? KeyIdPrefix) Inspect(FirebaseOptions options)
    {
        System.ArgumentNullException.ThrowIfNull(options);
        if (options.Provider == FirebaseOptions.InMemoryProvider)
        {
            return (PushStartupState.InMemory, null, null);
        }

        if (string.IsNullOrWhiteSpace(options.ServiceAccount))
        {
            return (PushStartupState.MissingKey, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(options.ServiceAccount);
            if (ServiceAccount.Parse(options.ServiceAccount) is not { } account)
            {
                return (PushStartupState.IncompleteKey, null, null);
            }

            var keyId = doc.RootElement.TryGetProperty("private_key_id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            return (PushStartupState.Active, account.ProjectId, keyId is { Length: >= 6 } ? keyId[..6] : null);
        }
        catch (JsonException)
        {
            return (PushStartupState.UnreadableKey, null, null);
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var (state, project, keyId) = Inspect(options.Value);
        switch (state)
        {
            case PushStartupState.Active:
                LogActive(logger, project ?? "?", keyId ?? "?");
                break;
            case PushStartupState.InMemory when environment.IsDevelopment():
                break;
            case PushStartupState.InMemory:
                LogInMemory(logger);
                break;
            default:
                LogDisabled(logger, state);
                break;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Push por FCM activo (proyecto {Project}, llave {KeyId}...)")]
    private static partial void LogActive(ILogger logger, string project, string keyId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Push en memoria fuera de Development: los avisos no llegan a los telefonos (Firebase:Provider=InMemory)")]
    private static partial void LogInMemory(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Push apagado ({State}): revisa Firebase:ServiceAccount (JSON en una sola linea, en la region de la infra). Los avisos quedan solo en el historial")]
    private static partial void LogDisabled(ILogger logger, PushStartupState state);
}

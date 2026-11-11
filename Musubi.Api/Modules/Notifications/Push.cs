using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Notifications.Data;

namespace Musubi.Api.Modules.Notifications;

// HU-31: avisos push con FCM directo (API HTTP v1, decision de la usuaria del 10-Nov-2026). La llave de la cuenta de
// servicio vive solo en el back (Parameter Store /musubi/prod/Firebase/ServiceAccount). El envio va en segundo plano
// (R14): una cola en memoria por instancia; si la instancia se reinicia con avisos en cola, esos push se pierden, pero
// el historial ya quedo guardado y la app lo muestra.

/// <summary>Configuracion de los avisos (seccion <c>Firebase</c>).</summary>
internal sealed class FirebaseOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Firebase";

    /// <summary>FCM (nube).</summary>
    public const string FcmProvider = "Fcm";

    /// <summary>Development y pruebas: los push se registran en memoria (<see cref="RecordingPushSender"/>).</summary>
    public const string InMemoryProvider = "InMemory";

    /// <summary><c>Fcm</c> o <c>InMemory</c>.</summary>
    public string Provider { get; set; } = FcmProvider;

    /// <summary>El JSON completo de la llave de la cuenta de servicio (secreto). Sin el, los push no salen.</summary>
    public string ServiceAccount { get; set; } = string.Empty;
}

/// <summary>Un push: titulo, texto y a donde lleva al tocarlo.</summary>
/// <param name="Title">Titulo.</param>
/// <param name="Body">Texto.</param>
/// <param name="Data">Datos para la app (<c>type</c>, <c>targetType</c>, <c>targetId</c>).</param>
internal sealed record PushMessage(string Title, string Body, IReadOnlyDictionary<string, string> Data);

/// <summary>Como termino un envio.</summary>
internal enum PushResult
{
    /// <summary>FCM lo acepto.</summary>
    Sent,

    /// <summary>El token ya no existe (la app se desinstalo): se borra.</summary>
    InvalidToken,

    /// <summary>Otra falla (red, cuota, configuracion): se registra y se sigue.</summary>
    Failed,
}

/// <summary>El proveedor de push (RNF-15: si falla, el resto de la API sigue igual).</summary>
internal interface IPushSender
{
    /// <summary>Si hay proveedor configurado.</summary>
    bool Enabled { get; }

    /// <summary>Envia un push a un dispositivo.</summary>
    /// <param name="token">Token de FCM.</param>
    /// <param name="message">El push.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El resultado.</returns>
    Task<PushResult> SendAsync(string token, PushMessage message, CancellationToken cancellationToken);
}

/// <summary>Development y pruebas: guarda lo que se habria enviado.</summary>
internal sealed class RecordingPushSender : IPushSender
{
    /// <summary>Lo enviado (token y push).</summary>
    public ConcurrentQueue<(string Token, PushMessage Message)> Sent { get; } = new();

    /// <summary>Tokens que responden como desinstalados.</summary>
    public ConcurrentDictionary<string, bool> Invalid { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool Enabled => true;

    /// <inheritdoc />
    public Task<PushResult> SendAsync(string token, PushMessage message, CancellationToken cancellationToken)
    {
        if (Invalid.ContainsKey(token))
        {
            return Task.FromResult(PushResult.InvalidToken);
        }

        Sent.Enqueue((token, message));
        return Task.FromResult(PushResult.Sent);
    }
}

/// <summary>
/// FCM por la API HTTP v1. Se autentica con la cuenta de servicio: firma un JWT RS256 y lo cambia por un access token
/// de una hora (se guarda hasta 5 minutos antes de vencer). Las URLs son fijas: no se configuran.
/// </summary>
internal sealed partial class FcmPushSender(HttpClient http, IOptions<FirebaseOptions> options, TimeProvider time, ILogger<FcmPushSender> logger) : IPushSender, IDisposable
{
    /// <summary>Donde se cambia el JWT por el access token.</summary>
    public static readonly Uri TokenUri = new("https://oauth2.googleapis.com/token");

    /// <summary>Base de la API de FCM.</summary>
    public static readonly Uri FcmBase = new("https://fcm.googleapis.com/v1/");

    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly SemaphoreSlim tokenGate = new(1, 1);
    private readonly Lazy<ServiceAccount?> account = new(() => ServiceAccount.Parse(options.Value.ServiceAccount));
    private string? accessToken;
    private DateTimeOffset accessTokenExpiresAt;

    /// <inheritdoc />
    public bool Enabled => account.Value is not null;

    /// <inheritdoc />
    public async Task<PushResult> SendAsync(string token, PushMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (account.Value is not { } sa)
        {
            return PushResult.Failed;
        }

        try
        {
            var bearer = await AccessTokenAsync(sa, cancellationToken).ConfigureAwait(false);
            if (bearer is null)
            {
                return PushResult.Failed;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(FcmBase, $"projects/{Uri.EscapeDataString(sa.ProjectId)}/messages:send"))
            {
                Content = JsonContent.Create(
                    new
                    {
                        message = new
                        {
                            token,
                            notification = new { title = message.Title, body = message.Body },
                            data = message.Data,
                            android = new { priority = "HIGH", notification = new { channel_id = "musubi" } },
                        },
                    },
                    options: Json),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return PushResult.Sent;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound || body.Contains("UNREGISTERED", StringComparison.Ordinal))
            {
                return PushResult.InvalidToken;
            }

            LogFailed(logger, (int)response.StatusCode);
            return PushResult.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or CryptographicException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnavailable(logger, ex.GetType().Name);
            return PushResult.Failed;
        }
    }

    /// <inheritdoc />
    public void Dispose() => tokenGate.Dispose();

    /// <summary>El JWT firmado que se cambia por el access token (publico para las pruebas).</summary>
    /// <param name="sa">La cuenta de servicio.</param>
    /// <param name="now">Ahora.</param>
    /// <returns>El JWT.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5542", Justification = "RS256 (RFC 7518) firma con RSASSA-PKCS1-v1_5: es lo que exige Google para el JWT de la cuenta de servicio; no es cifrado.")]
    internal static string SignedAssertion(ServiceAccount sa, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sa);
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = sa.ClientEmail,
            scope = Scope,
            aud = TokenUri.ToString(),
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddHours(1).ToUnixTimeSeconds(),
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(sa.PrivateKey);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{claims}.{B64(signature)}";
    }

    private async Task<string?> AccessTokenAsync(ServiceAccount sa, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (accessToken is not null && now < accessTokenExpiresAt)
        {
            return accessToken;
        }

        await tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (accessToken is not null && now < accessTokenExpiresAt)
            {
                return accessToken;
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = SignedAssertion(sa, now),
            });
            using var response = await http.PostAsync(TokenUri, content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode);
                return null;
            }

            var token = await response.Content.ReadFromJsonAsync<OAuthToken>(Json, cancellationToken).ConfigureAwait(false);
            if (token?.AccessToken is not { Length: > 0 } value)
            {
                return null;
            }

            accessToken = value;
            accessTokenExpiresAt = now.AddSeconds(Math.Max(60, token.ExpiresIn - 300));
            return value;
        }
        finally
        {
            tokenGate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FCM respondio {Status}: el aviso queda solo en el historial")]
    private static partial void LogFailed(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FCM no disponible ({Error}): el aviso queda solo en el historial")]
    private static partial void LogUnavailable(ILogger logger, string error);

    private sealed record OAuthToken(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>Lo que se usa de la llave de la cuenta de servicio.</summary>
/// <param name="ProjectId">Proyecto de Firebase.</param>
/// <param name="ClientEmail">Correo de la cuenta de servicio.</param>
/// <param name="PrivateKey">Llave privada en PEM.</param>
internal sealed record ServiceAccount(string ProjectId, string ClientEmail, string PrivateKey)
{
    /// <summary>Lee el JSON de la llave; <c>null</c> si falta o no tiene los campos.</summary>
    /// <param name="json">El JSON.</param>
    /// <returns>La cuenta o <c>null</c>.</returns>
    public static ServiceAccount? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return Get("project_id") is { Length: > 0 } project && Get("client_email") is { Length: > 0 } email && Get("private_key") is { Length: > 0 } key
                ? new ServiceAccount(project, email, key)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"ServiceAccount {{ ProjectId = {ProjectId}, ClientEmail = {ClientEmail} }}";
}

/// <summary>Un push pendiente para una persona.</summary>
/// <param name="UserId">A quien.</param>
/// <param name="Message">El push.</param>
internal sealed record PushJob(string UserId, PushMessage Message);

/// <summary>La cola de push de esta instancia (acotada: si se llena, el push se descarta y el historial queda).</summary>
internal sealed class PushQueue
{
    private readonly Channel<PushJob> channel = Channel.CreateBounded<PushJob>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    /// <summary>Lector de la cola.</summary>
    public ChannelReader<PushJob> Reader => channel.Reader;

    /// <summary>Encola sin esperar.</summary>
    /// <param name="job">El push.</param>
    /// <returns><c>true</c> si entro.</returns>
    public bool TryEnqueue(PushJob job) => channel.Writer.TryWrite(job);
}

/// <summary>Envia los push de la cola a todos los dispositivos de cada persona y borra los tokens que ya no existen.</summary>
internal sealed partial class PushWorker(PushQueue queue, IServiceScopeFactory scopes, IDeviceTokenRepository tokens, TimeProvider time, ILogger<PushWorker> logger) : BackgroundService
{
    /// <summary>Envia un push ya (las pruebas lo llaman sin esperar a la cola).</summary>
    /// <param name="sender">El proveedor.</param>
    /// <param name="tokens">Los tokens.</param>
    /// <param name="job">El push.</param>
    /// <param name="now">Ahora.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos dispositivos lo recibieron.</returns>
    internal static async Task<int> DeliverAsync(IPushSender sender, IDeviceTokenRepository tokens, PushJob job, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(job);
        if (!sender.Enabled)
        {
            return 0;
        }

        var sent = 0;
        foreach (var token in await tokens.FindByUserAsync(job.UserId, now, cancellationToken).ConfigureAwait(false))
        {
            switch (await sender.SendAsync(token, job.Message, cancellationToken).ConfigureAwait(false))
            {
                case PushResult.Sent:
                    sent++;
                    break;
                case PushResult.InvalidToken:
                    await tokens.RemoveAsync(token, null, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    break;
            }
        }

        return sent;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();
                await DeliverAsync(sender, tokens, job, time.GetUtcNow().UtcDateTime, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un push que falla no detiene la cola (RNF-15).
                LogFailed(logger, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se envio un push ({Error})")]
    private static partial void LogFailed(ILogger logger, string error);
}

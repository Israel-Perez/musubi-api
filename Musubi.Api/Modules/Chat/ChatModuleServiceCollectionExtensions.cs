using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Chat;

/// <summary>Registro DI y hub del modulo M-07 (Chat).</summary>
internal static class ChatModuleServiceCollectionExtensions
{
    /// <summary>Politica de rate limit del envio de mensajes (por usuario).</summary>
    public const string MessageRateLimit = "chat";

    /// <summary>Registra repositorios (Mongo o en memoria, lazy), servicios y el limite de mensajes.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddChatModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var conversations = new InMemoryConversationRepository();
        var messages = new InMemoryMessageRepository();
        services.AddSingleton<IConversationRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? conversations
            : new MongoConversationRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<IMessageRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? messages
            : new MongoMessageRepository(provider.GetRequiredService<IMongoDatabase>()));

        services.AddSingleton<IConversationDirectory, ConversationDirectory>();
        services.AddSingleton<IConversationOpener, ConversationOpener>();
        services.AddSingleton<IConversationNotices, ConversationNotices>();
        services.AddSingleton<ChatNotifier>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<IAccountDataEraser, ChatAccountEraser>();

        // Cada mensaje cuesta moderacion (AWS) y una escritura: un tope por usuario evita el abuso (OWASP API4).
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, config) =>
        {
            var permits = config.GetValue("RateLimiting:Chat:PermitLimit", 30);
            var window = TimeSpan.FromSeconds(config.GetValue("RateLimiting:Chat:WindowSeconds", 60));
            options.AddPolicy(MessageRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(MusubiClaims.Subject)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));
        });
    }

    /// <summary>
    /// Publica el hub. Solo WebSockets: la app se conecta sin negociar (<c>skipNegotiation</c>), asi la
    /// conexion no depende de caer en la misma instancia que la negociacion (sin sesiones pegajosas en el ALB).
    /// </summary>
    /// <param name="app">La aplicacion.</param>
    public static void MapChatModule(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapHub<ChatHub>(ChatHub.Path, options => options.Transports = HttpTransportType.WebSockets);
    }
}

/// <summary>
/// Borra las conversaciones y los mensajes de una cuenta que se elimina (HU-46, R11): los textos son datos
/// personales de los dos participantes, asi que la conversacion completa desaparece para ambos.
/// </summary>
internal sealed class ChatAccountEraser(IConversationRepository conversations, IMessageRepository messages) : IAccountDataEraser
{
    /// <inheritdoc />
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        // Primero los mensajes: si algo falla a medias, la conversacion sigue y el reintento la encuentra.
        var ids = await conversations.FindIdsByParticipantAsync(userId, cancellationToken).ConfigureAwait(false);
        await messages.DeleteByConversationsAsync(ids, cancellationToken).ConfigureAwait(false);
        await conversations.DeleteByParticipantAsync(userId, cancellationToken).ConfigureAwait(false);
    }
}

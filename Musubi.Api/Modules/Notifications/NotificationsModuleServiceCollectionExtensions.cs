using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Notifications.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Notifications;

/// <summary>Registro DI del modulo M-09 (Notificaciones).</summary>
internal static class NotificationsModuleServiceCollectionExtensions
{
    /// <summary>Registra los repositorios, el proveedor de push (segun <c>Firebase:Provider</c>), la cola y los recordatorios.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddNotificationsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<FirebaseOptions>().BindConfiguration(FirebaseOptions.Section);
        services.AddOptions<ReminderOptions>().BindConfiguration(ReminderOptions.Section);

        var notifications = new InMemoryNotificationRepository();
        services.AddSingleton<INotificationRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? notifications
            : new MongoNotificationRepository(provider.GetRequiredService<IMongoDatabase>()));
        var tokens = new InMemoryDeviceTokenRepository();
        services.AddSingleton<IDeviceTokenRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? tokens
            : new MongoDeviceTokenRepository(provider.GetRequiredService<IMongoDatabase>()));

        services.AddHttpClient<FcmPushSender>(client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<RecordingPushSender>();
        // El proveedor se elige al resolverlo (las pruebas sobrescriben la configuracion despues del registro).
        services.AddTransient<IPushSender>(provider => provider.GetRequiredService<IOptions<FirebaseOptions>>().Value.Provider == FirebaseOptions.InMemoryProvider
            ? provider.GetRequiredService<RecordingPushSender>()
            : provider.GetRequiredService<FcmPushSender>());

        services.AddSingleton<PushQueue>();
        services.AddHostedService<PushWorker>();
        services.AddSingleton<INotificationCenter, NotificationCenter>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<WantedReminderJob>();
        services.AddHostedService<WantedReminderWorker>();
        services.AddSingleton<IAccountDataEraser, NotificationAccountEraser>();
    }
}

/// <summary>Cada cuanto se revisan las solicitudes que vencen (seccion <c>Notifications</c>).</summary>
internal sealed class ReminderOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Notifications";

    /// <summary>Minutos entre revisiones; 0 las apaga (pruebas).</summary>
    public int ReminderMinutes { get; set; } = 60;
}

/// <summary>
/// "Tu solicitud vence manana" y "Tu solicitud vencio" (maqueta <c>Notificaciones</c>, RF-41). Con 2 instancias las
/// dos revisan, pero cada aviso lleva una <c>dedupeKey</c> y el indice unico deja pasar solo uno.
/// </summary>
internal sealed class WantedReminderJob(IWantedReminders requests, INotificationCenter center)
{
    /// <summary>Cuantas solicitudes se revisan por vuelta.</summary>
    public const int BatchSize = 200;

    /// <summary>Revisa una vez.</summary>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas solicitudes revisó.</returns>
    public async Task<int> RunAsync(DateTime now, CancellationToken cancellationToken)
    {
        var day = TimeSpan.FromDays(1);
        var expiring = await requests.ExpiringAsync(now, now + day, BatchSize, cancellationToken).ConfigureAwait(false);
        foreach (var r in expiring)
        {
            await center.NotifyAsync(
                new Notice(
                    r.RequesterId,
                    NotificationTypes.RequestExpiring,
                    "Tu solicitud vence mañana",
                    $"{r.Title}. ¿Quieres extender la fecha?",
                    NotificationTargets.Wanted,
                    r.Id,
                    NotificationCategories.Requests,
                    $"{NotificationTypes.RequestExpiring}:{r.Id}:{r.ExpiresAt.Ticks.ToString(CultureInfo.InvariantCulture)}"),
                cancellationToken).ConfigureAwait(false);
        }

        var expired = await requests.ExpiringAsync(now - day, now, BatchSize, cancellationToken).ConfigureAwait(false);
        foreach (var r in expired)
        {
            await center.NotifyAsync(
                new Notice(
                    r.RequesterId,
                    NotificationTypes.RequestExpired,
                    "Tu solicitud venció",
                    $"{r.Title}. Puedes publicarla de nuevo si todavía lo necesitas.",
                    NotificationTargets.Wanted,
                    r.Id,
                    NotificationCategories.Requests,
                    $"{NotificationTypes.RequestExpired}:{r.Id}:{r.ExpiresAt.Ticks.ToString(CultureInfo.InvariantCulture)}"),
                cancellationToken).ConfigureAwait(false);
        }

        return expiring.Count + expired.Count;
    }
}

/// <summary>Corre <see cref="WantedReminderJob"/> cada <see cref="ReminderOptions.ReminderMinutes"/>.</summary>
internal sealed partial class WantedReminderWorker(
    WantedReminderJob job,
    IOptions<ReminderOptions> options,
    TimeProvider time,
    ILogger<WantedReminderWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.ReminderMinutes <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.ReminderMinutes), time);
        do
        {
            try
            {
                await job.RunAsync(time.GetUtcNow().UtcDateTime, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException)
            {
                // Sin base, se intenta en la siguiente vuelta.
                LogSkipped(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Revision de solicitudes por vencer omitida ({Error})")]
    private static partial void LogSkipped(ILogger logger, string error);
}

/// <summary>Borrado de una cuenta (HU-46, R11): sus avisos y sus dispositivos.</summary>
internal sealed class NotificationAccountEraser(INotificationRepository notifications, IDeviceTokenRepository tokens) : IAccountDataEraser
{
    /// <inheritdoc />
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        await tokens.DeleteByUserAsync(userId, cancellationToken).ConfigureAwait(false);
        await notifications.DeleteByUserAsync(userId, cancellationToken).ConfigureAwait(false);
    }
}

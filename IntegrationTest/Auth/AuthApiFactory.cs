using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;

namespace IntegrationTest.Auth;

/// <summary>API en Development, en memoria, con reloj controlable y correos capturados (HU-01/03/47).</summary>
public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    internal ManualTimeProvider Clock { get; } = new(Start);

    internal RecordingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UseInMemoryDatabase"] = "true",
            // Muchas peticiones a /auth desde la misma "IP" de pruebas: el limite se prueba en SecurityTest.
            ["RateLimiting:Auth:PermitLimit"] = "10000",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IEmailSender>(Emails);
        });
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace IntegrationTest.Common;

/// <summary>Factory WAF en Development con UseInMemoryDatabase (sin MongoDB).</summary>
public sealed class MusubiApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UseInMemoryDatabase"] = "true",
            // Los user-secrets de quien corre las pruebas pueden encender FCM o Gemini reales: aqui siempre van los simulados.
            ["Firebase:Provider"] = "InMemory",
            ["GenAi:Provider"] = "InMemory",
        }));
    }
}

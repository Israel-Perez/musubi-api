using Microsoft.AspNetCore.Builder;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Hosting;
using Musubi.Api.Platform.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.AddMusubiLocalConfiguration();
builder.AddMusubiObservability();
builder.Services.AddMusubiPlatform(builder.Configuration);
builder.Services.AddAuthModule();
builder.Services.AddListingsModule();
builder.Services.AddProfilesModule();
builder.Services.AddChatModule();
builder.Services.AddReputationModule();
builder.Services.AddPurchaseRequestsModule();
builder.Services.AddOrdersModule();

var app = builder.Build();

app.UseMusubiPipeline();
app.MapChatModule();

await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point expuesto para <c>WebApplicationFactory&lt;Program&gt;</c> en las suites de pruebas.</summary>
#pragma warning disable S1118 // El entry point top-level no puede ser static; este partial solo existe para WebApplicationFactory.
#pragma warning disable CA1515 // Referenciado por los ensamblados de prueba via WebApplicationFactory.
public partial class Program
{
}
#pragma warning restore CA1515
#pragma warning restore S1118

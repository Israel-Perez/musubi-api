using System;
using System.Globalization;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Musubi.Api.Platform.Auth;

/// <summary>Autenticacion JWT, autorizacion por defecto y limite de intentos (02-03).</summary>
internal static class AuthPlatformServiceCollectionExtensions
{
    /// <summary>Politica de rate limit para los endpoints de <c>/auth/*</c> (<c>[EnableRateLimiting(AuthRateLimit)]</c>).</summary>
    public const string AuthRateLimit = "auth";

    /// <summary>Tolerancia de reloj entre instancias al validar <c>exp</c>/<c>nbf</c>.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>Registra JWT Bearer, la politica de respaldo (sesion obligatoria) y el rate limiter.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddMusubiAuth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section).ValidateOnStart();
        services.AddSingleton<JwtOptionsSetup>();
        services.AddSingleton<IPostConfigureOptions<JwtOptions>>(sp => sp.GetRequiredService<JwtOptionsSetup>());
        services.AddSingleton<IValidateOptions<JwtOptions>>(sp => sp.GetRequiredService<JwtOptionsSetup>());
        services.AddSingleton<AccessTokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        // Lectura lazy: respeta los overrides de WebApplicationFactory (00-04, regla 3).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    // Solo HS256: rechaza alg=none y la confusion de algoritmos (OWASP API2).
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = ClockSkew,
                    NameClaimType = MusubiClaims.Subject,
                    RoleClaimType = MusubiClaims.Role,
                };
            });

        // Rechazar por defecto: cada endpoint exige sesion salvo [AllowAnonymous] (constitucion §2, principio 1).
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, config) =>
        {
            var permits = config.GetValue("RateLimiting:Auth:PermitLimit", 10);
            var window = TimeSpan.FromSeconds(config.GetValue("RateLimiting:Auth:WindowSeconds", 60));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Particion por IP del cliente. NOTE (02-05): detras del ALB depende de ForwardedHeaders con KnownProxies.
            options.AddPolicy(AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
        });
    }
}

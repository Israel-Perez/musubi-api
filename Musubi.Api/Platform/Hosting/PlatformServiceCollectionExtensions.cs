using System;
using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Email;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Platform.Hosting;

/// <summary>Registro DI de la plataforma (01-02). Los modulos M-01…M-14 registran sus servicios en su propia extension.</summary>
internal static class PlatformServiceCollectionExtensions
{
    /// <summary>Nombre de la politica CORS restringida por configuracion.</summary>
    public const string CorsPolicyName = "MusubiCors";

    /// <summary>Tag de los health checks que forman parte de la readiness.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Carga <c>appsettings.Local.json</c> (ignorado por Git) solo en Development.</summary>
    /// <param name="builder">El builder de la aplicacion.</param>
    public static void AddMusubiLocalConfiguration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
        }
    }

    /// <summary>Registra los servicios fundacionales: API versionada, CORS, HSTS, datos y health checks.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    /// <param name="configuration">La configuracion de la aplicacion.</param>
    public static void AddMusubiPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<KestrelServerOptions>(configuration.GetSection("Kestrel"));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Solo proxies locales. En AWS la IP del cliente y el esquema vienen de las cabeceras de CloudFront
            // (CloudFrontForwardedHeaders, 02-05): el X-Forwarded-For que arma el ALB termina en la IP de CloudFront.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        });

        // Contrato de errores (02-03): cada error sale como problem+json con code, titulo en espanol y traceId.
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            ApiErrors.Decorate(context.ProblemDetails, context.HttpContext));
        services.AddControllers();
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
        {
            var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
            var problem = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState, ApiErrors.ValidationFailed.Status);
            problem.Extensions[ApiErrors.CodeExtension] = ApiErrors.ValidationFailed.Code;
            problem.Title = ApiErrors.ValidationFailed.Message;
            return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
        });
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddMvc()
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        })
        .AddOpenApi();

        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            policy.WithOrigins(origins)
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                .WithHeaders("Authorization", "Content-Type");
        }));

        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });

        services.AddMusubiAuth();
        services.AddMusubiEmail();

        services.AddMusubiMongo(ReadyTag);
    }
}

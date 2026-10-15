# 01-02 — Pipeline de middleware y DI

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) · contraparte: `musubi-app:01-02`

## Contexto
Registro DI y pipeline HTTP de la plataforma, en un orden fijo y explícito, sobre los que cada módulo se monta sin reordenar nada. **Depende de**: `01-01`.

## Requisitos
1. Registro y pipeline en métodos de extensión (`AddMusubiPlatform`, `UseMusubiPipeline`); `Program.cs` mínimo con `public partial class Program`.
2. Middleware en el orden de la tabla de Diseño.
3. HSTS (365 días, subdominios) solo fuera de Development.
4. CORS restringido a `Cors:AllowedOrigins` (vacío = ningún origen), sin comodín de origen, métodos ni headers.
5. API versionada por segmento de URL (`api/v1`) con OpenAPI por versión y Scalar solo en Development.
6. Límites de Kestrel leídos de la configuración.

## Diseño

| Orden | Componente | Estado | Nota |
|---|---|---|---|
| 1 | `UseForwardedHeaders` (X-Forwarded-For/Proto) | ✅ | `NOTE (02-05)`: KnownNetworks del balanceador de AWS |
| 2 | `UseExceptionHandler` + `ProblemDetails` (fuera de Dev) | ✅ | 01-03 |
| 3 | `UseHsts` (fuera de Dev) | ✅ | `AddHsts`: 365 d + subdominios |
| 4 | `UseHttpsRedirection` | ✅ | RNF-02 |
| 5 | `UseCors("MusubiCors")` | ✅ | Orígenes desde la configuración |
| 6 | `MapOpenApi().WithDocumentPerVersion()` + `MapScalarApiReference()` (solo Dev) | ✅ | `/openapi/v1.json`, `/scalar` |
| 7 | `UseAuthorization` | ✅ | `NOTE (HU-02)`: esquema JWT y FallbackPolicy |
| 8 | `MapControllers` + `MapHealthChecks` | ✅ | 01-03 |
| — | Headers de seguridad, rate limiting | Pendiente | `02-04` |
| — | Logs estructurados, correlación | Pendiente | `02-06` |
| — | Hub de SignalR | Pendiente | `NOTE (HU-20)` |

Archivos: `Musubi.Api/Program.cs`, `Musubi.Api/Platform/Hosting/PlatformServiceCollectionExtensions.cs`, `Musubi.Api/Platform/Hosting/PlatformPipelineExtensions.cs`.

## Contratos
- `Cors:AllowedOrigins` (array; por defecto `[]`).
- `Kestrel:Limits:MaxRequestBodySize` = 10485760; `Kestrel:Limits:KeepAliveTimeout` = `00:02:00`.
- `Asp.Versioning`: `DefaultApiVersion=1.0`, `UrlSegmentApiVersionReader`, header `api-supported-versions`.

## Tests
- `UnitTest/DI/ServiceRegistrationTests.cs` (6): CORS sin orígenes por defecto; CORS solo con el origen configurado y sin comodines; DbContext InMemory/Npgsql según la bandera; fail-fast sin connection string; límites de Kestrel enlazados.
- `IntegrationTest/Middleware/MiddlewareTests.cs` (3): sin HSTS en Dev; OpenAPI v1 expuesto en Dev; 404 en una ruta desconocida.
- `SecurityTest/Hsts/HstsTests.cs` (3): opciones de 365 días en Production; header HSTS en el wire en Production; sin HSTS en Dev.
- `SecurityTest/Cors/CorsTests.cs` (3): preflight de un origen desconocido rechazado; origen configurado reflejado exacto; sin header en una petición simple desconocida.

## Criterios
- `dotnet build Musubi.slnx -c Release` → 0/0 (SonarAnalyzer S5122 bloquea `AllowAnyOrigin`).
- `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` → verde (19/19 en total).
- `dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build` → verde (8/8).
- `dotnet test --project SecurityTest/SecurityTest.csproj -c Release --no-build` → verde (11/11).
- Self-test: quitar `UseHsts` hace fallar `Production_HttpsResponse_CarriesHstsHeader` (medido el 15-Oct-2026).

## Límites
No hay autenticación (HU-02), headers de seguridad ni rate limiting (02-04), ni hub de SignalR (HU-20). En AWS falta registrar el proxy en ForwardedHeaders (02-05).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 15-Oct-2026
- **Detalle:** Desviación respecto de la referencia: integración oficial `Asp.Versioning.OpenApi` en lugar de suprimir AV0029/AV0030.

# 01-03 — Arranque, health y ping

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) · contraparte: `musubi-app:01-03` (consume `GET /api/v1/ping`)

## Contexto
Sondas de salud para el orquestador, manejo de errores sin fugas y el primer endpoint versionado que consume el front. **Depende de**: `01-02`.

## Requisitos
1. `/health` (liveness): 200 mientras el proceso corra, sin ejecutar checks.
2. `/health/ready` (readiness): incluye el check `database`: `ping` a MongoDB con timeout de 5 s, o Healthy sin consulta si `UseInMemoryDatabase=true`.
3. `GET /api/v1/ping` anónimo explícito → `{ "status": "pong", "version": "v1" }`.
4. Fuera de Development, una excepción no controlada responde `application/problem+json` genérico, sin mensaje, tipo ni stack.
5. `IMongoClient`/`IMongoDatabase` registrados de forma lazy, sin colecciones, con fail-fast si falta `ConnectionStrings:Mongo` cuando `UseInMemoryDatabase=false`.

## Diseño

| Componente | Estado | Archivo |
|---|---|---|
| `/health` (Predicate = ninguno) | ✅ | `Platform/Hosting/PlatformPipelineExtensions.cs` |
| `/health/ready` (tag `ready` → check `database`: `MongoHealthCheck` o `InMemoryDatabaseHealthCheck` según la bandera) | ✅ | `Platform/Data/MongoServiceCollectionExtensions.cs` |
| `PingController` (`[AllowAnonymous]`, `api/v{version:apiVersion}/ping`) | ✅ | `Platform/Ping/PingController.cs` |
| `UseExceptionHandler` + `AddProblemDetails` (no Dev) / DeveloperExceptionPage (Dev) | ✅ | pipeline |
| `IMongoClient` / `IMongoDatabase` (sin colecciones) | ✅ | `Platform/Data/MongoServiceCollectionExtensions.cs` — `NOTE (02-01)` |
| Readiness contra un MongoDB real (Testcontainers) | Pendiente | `02-01` |

## Contratos
- Filas canónicas en [`docs/endpoints.md`](../../../docs/endpoints.md): `GET /health`, `GET /health/ready`, `GET /api/v1/ping` (3 filas; no se duplican aquí).
- Fixture del contrato: `ContractTest/Fixtures/ping.json`.
- Configuración: `UseInMemoryDatabase`, `ConnectionStrings:Mongo`, `Mongo:DatabaseName` (ver 01-04).

## Tests
- `IntegrationTest/Health/HealthTests.cs` (2): `/health` 200 `Healthy`; `/health/ready` 200 `Healthy` en modo en memoria.
- `UnitTest/Data/DatabaseHealthCheckTests.cs` (2): readiness Healthy en modo en memoria; con MongoDB inalcanzable (`127.0.0.1:1`), el check `database` es Unhealthy con descripción genérica. Self-test: si el check devuelve Healthy al fallar el ping, la prueba falla (20-Oct-2026).
- `IntegrationTest/Ping/PingTests.cs` (3): ping 200 JSON; header `api-supported-versions` con 1.0; versión no soportada (`/api/v9/ping`) no se sirve.
- `SecurityTest/Errors/ExceptionHandlingTests.cs` (2): Production oculta el detalle; Development (grupo de control) lo muestra.
- `SecurityTest/Auth/ExplicitAuthTests.cs` (2 + 1 por controller): cada acción declara `[Authorize]` o `[AllowAnonymous]`; OpenAPI y Scalar no se exponen en Production.
- `ContractTest/PingContractTests.cs` (2): forma igual al fixture; propiedades en camelCase.

## Criterios
- `dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build` → 8/8.
- `dotnet test --project SecurityTest/SecurityTest.csproj -c Release --no-build` → 11/11.
- `dotnet test --project ContractTest/ContractTest.csproj -c Release --no-build` → 2/2.
- `pwsh scripts/check_endpoints.ps1` → `OK (3 rutas con fila, 0 huerfanas)`.

## Límites
La readiness contra un MongoDB real (contenedor o Atlas) se valida en `02-01` (Testcontainers). Sin health UI ni check de integridad de ensamblado (decisión de la plantilla: no aportan al MVP; ver `00-03`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 20-Oct-2026
- **Detalle:** Ping JSON camelCase sin el `/ping` raíz legacy de la referencia. 20-Oct-2026 (SCRUM-66): `AppDbContext` (EF Core) → cliente de MongoDB y readiness por `ping`.

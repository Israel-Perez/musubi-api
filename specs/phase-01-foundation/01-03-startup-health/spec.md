# 01-03 — Arranque, health y ping

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) · contraparte: `musubi-app:01-03` (consume `GET /api/v1/ping`)

## Contexto
Sondas de salud para el orquestador, manejo de errores sin fugas y el primer endpoint versionado que consume el front. **Depende de**: `01-02`.

## Requisitos
1. `/health` (liveness): 200 mientras el proceso corra, sin ejecutar checks.
2. `/health/ready` (readiness): incluye el check `database` de `AppDbContext`.
3. `GET /api/v1/ping` anónimo explícito → `{ "status": "pong", "version": "v1" }`.
4. Fuera de Development, una excepción no controlada responde `application/problem+json` genérico, sin mensaje, tipo ni stack.
5. `AppDbContext` registrado sin entidades, con la bandera `UseInMemoryDatabase` y fail-fast si falta `ConnectionStrings:Default` con PostgreSQL.

## Diseño

| Componente | Estado | Archivo |
|---|---|---|
| `/health` (Predicate = ninguno) | ✅ | `Platform/Hosting/PlatformPipelineExtensions.cs` |
| `/health/ready` (tag `ready` → `AddDbContextCheck<AppDbContext>`) | ✅ | `Platform/Hosting/PlatformServiceCollectionExtensions.cs` |
| `PingController` (`[AllowAnonymous]`, `api/v{version:apiVersion}/ping`) | ✅ | `Platform/Ping/PingController.cs` |
| `UseExceptionHandler` + `AddProblemDetails` (no Dev) / DeveloperExceptionPage (Dev) | ✅ | pipeline |
| `AppDbContext` (sin entidades) | ✅ | `Platform/Data/AppDbContext.cs` — `NOTE (02-01)` |
| Migraciones y health de BD con PostgreSQL real | Pendiente | `02-01`/`02-02` |

## Contratos
- Filas canónicas en [`docs/endpoints.md`](../../../docs/endpoints.md): `GET /health`, `GET /health/ready`, `GET /api/v1/ping` (3 filas; no se duplican aquí).
- Fixture del contrato: `ContractTest/Fixtures/ping.json`.
- Configuración: `UseInMemoryDatabase`, `ConnectionStrings:Default` (ver 01-04).

## Tests
- `IntegrationTest/Health/HealthTests.cs` (2): `/health` 200 `Healthy`; `/health/ready` 200 `Healthy` con la BD disponible.
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
La readiness con PostgreSQL real se valida en `02-01` (Testcontainers). Sin health UI ni check de integridad de ensamblado (decisión de la plantilla: no aportan al MVP; ver `00-03`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 15-Oct-2026
- **Detalle:** Ping JSON camelCase sin el `/ping` raíz legacy de la referencia.

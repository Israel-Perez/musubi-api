# Catálogo de endpoints de musubi-api (canónico)

Fuente única de verdad del contrato HTTP de MUSUBI: método, ruta, auth, DTOs, códigos y quién lo consume.

- Las specs (de este repo y de `musubi-app`) **enlazan** sus filas; nunca copian la tabla.
- Lo verifica `scripts/check_endpoints.ps1` (job `endpoints` del CI) en ambos sentidos: cada ruta del código tiene su fila y cada fila existe en el código.
- El front consume una fila enlazándola desde su spec y desde `lib/core/network/api_paths.dart` (skill `api-contract` de `musubi-app`).
- Para cambiar una fila: actualizar el código, esta tabla, el fixture de `ContractTest/Fixtures/` (si cambia la forma JSON) y avisar en el PR del front.

## Convenciones

- Base versionada `api/v1/...` (`api/v{version:apiVersion}/...` + `UrlSegmentApiVersionReader`; las respuestas incluyen el header `api-supported-versions`).
- JSON en **camelCase** (serializador por defecto de ASP.NET Core), con códigos en inglés y mensajes al usuario en español (RNF-18).
- Auth **explícita** en cada controller: `[Authorize]` / `[Authorize(Policy = ...)]` / `[AllowAnonymous]` (bloqueante B3 del critic + `SecurityTest/Auth/ExplicitAuthTests.cs`).
- Errores: `application/problem+json` con `code` estable, título en español y `traceId`, sin detalle interno. Catálogo canónico: [`docs/errores.md`](errores.md) (02-03).
- Sesión obligatoria por defecto: un endpoint sin `[AllowAnonymous]` exige `Authorization: Bearer <access token>` (02-03). Una ruta inexistente sin sesión responde 401.
- Solo en Development (no se cuentan como filas porque no son endpoints de la API): `/openapi/v1.json` (OpenAPI) y `/scalar` (referencia interactiva).

## Plataforma

| Método | Ruta | Auth | Spec | Request | Response | Códigos | Consumidor |
|---|---|---|---|---|---|---|---|
| GET | /health | Anónimo (health check, fuera de MVC) | 01-03 | — | `Healthy` (texto) | 200/503 | Balanceador / orquestador (liveness) |
| GET | /health/ready | Anónimo (health check, fuera de MVC) | 01-03 | — | `Healthy` / `Unhealthy` (texto); incluye el check `database` | 200/503 | Balanceador / orquestador (readiness) |
| GET | /api/v1/ping | `[AllowAnonymous]` | 01-03 | — | `PingResponse { status: "pong", version: "v1" }` | 200 | `musubi-app` 01-03 (`StatusRepository`) |

Total: 3 filas (3 de plataforma, 0 de módulos). Las filas de los módulos M-01…M-14 se agregan con la spec de cada HU.

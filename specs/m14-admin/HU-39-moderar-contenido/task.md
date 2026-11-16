# HU-39 — Revisar reportes y moderar contenido (tareas)

## T1 — Detallar la spec
- **Qué:** decisiones, endpoints, errores y datos (`plan.md`).
- **Estado:** ✅ Hecho (16-Nov-2026).

## T2 — Datos
- **Qué:**
  - `db/030-moderacion.js` (revisión en `reports`, `moderationLog`, avisos).
  - Repositorios `Mongo*`/`InMemory*` con sus baterías: `ReputationRepositoryContract`, `ListingRepositoryContract` y `ModerationLogRepositoryContract`.
- **Estado:** ✅ Hecho. `DatabaseTest` 184/184 contra MongoDB 8.0 local; en el CI corre con Testcontainers.

## T3 — Servicio y endpoints
- **Qué:** `ModerationService`, `AdminReportsController`, `AdminErrors`, los contratos `IReportInbox` e `IProductModeration`, `Admin:Emails` y `user.roles`.
- **Pruebas:**
  - `IntegrationTest/Admin/ModerationFlowTests.cs`
  - `SecurityTest/Auth/AdminAuthTests.cs`
  - `ContractTest/AdminContractTests.cs`
- **Estado:** ✅ Hecho.

## T4 — Documentación
- **Qué:**
  - `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `docs/trazabilidad.md` y `docs/versionado.md`, esta última compartida con musubi-app.
  - `01-04` (`Admin:Emails`), `db/README.md` (dar o quitar el rol) y `Memoria.md`.
- **Estado:** ✅ Hecho.

## T5 — Despliegue
- **Qué:** crear `/musubi/prod/Admin/Emails/0` con el correo del equipo en Parameter Store y correr `infra-up` con `0.5.0-alpha.38` (aplica `db/030`).
- **Estado:** ⏳ Pendiente (responsable del despliegue).

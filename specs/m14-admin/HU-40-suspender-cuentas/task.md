# HU-40 — Suspender y reactivar cuentas (tareas)

## T1 — Detallar la spec
- **Qué:** decisiones, endpoints y errores, con HU-39 (`../HU-39-moderar-contenido/plan.md`).
- **Estado:** ✅ Hecho (16-Nov-2026).

## T2 — Datos
- **Qué:** `IUserRepository.ChangeStatusAsync`, `FindByStatusAsync` y `AddRoleAsync`, más `IProductRepository.HideBySellerAsync`, con sus baterías en `TestShared/`. Sin script nuevo para `users`: `db/001` ya admitía `suspended` y `admin`.
- **Estado:** ✅ Hecho.

## T3 — Servicio y endpoints
- **Qué:** `AdminAccountsController`, `IAccountModeration` y las suspensiones de `ModerationService`.
- **Pruebas:** `IntegrationTest/Admin/ModerationFlowTests.cs`, `SecurityTest/Auth/AdminAuthTests.cs` y `ContractTest/AdminContractTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Documentación
- **Qué:** filas de M-14 en `docs/endpoints.md`, errores `admin.account_*`, `docs/trazabilidad.md` y `Memoria.md`.
- **Estado:** ✅ Hecho.

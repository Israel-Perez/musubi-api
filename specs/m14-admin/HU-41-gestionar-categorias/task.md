# HU-41 — Gestionar categorías (tareas)

## T1 — Detallar la spec
- **Qué:** decisiones (desactivar en vez de borrar, slug fijo, repetidas, 30 por campus), endpoints, errores y datos.
- **Estado:** ✅ Hecho (16-Nov-2026).

## T2 — Datos
- **Qué:** `ICategoryRepository.ListAllAsync`, `TryInsertAsync` y `UpdateAsync` (Mongo y en memoria) con la batería de `TestShared/Listings/ListingRepositoryContract.cs`; `db/031-categorias.js` para `moderationLog`.
- **Estado:** ✅ Hecho. `DatabaseTest` 194/194.

## T3 — Servicio y endpoints
- **Qué:** `CategoryCatalog` (M-03), `CategoryAdminService`, `AdminCategoriesController`, `AdminGate`, errores y pruebas Unit, Integration, Security y Contract.
- **Estado:** ✅ Hecho. Unit 915, Integration 207, Security 305, Contract 31.

## T4 — Documentación
- **Qué:** `docs/endpoints.md` (125 filas), `docs/errores.md`, `docs/modelo-datos.md`, `docs/trazabilidad.md`, `docs/versionado.md`, `db/README.md`, `AGENTS.md` y `Memoria.md`.
- **Estado:** ✅ Hecho.

## Pendientes con dueño
- `NOTE (HU-41)` en `Modules/Profiles` (carreras y periodo del campus): Jira solo pide categorías. Falta decidir si se amplía HU-41 o se abre otra HU.

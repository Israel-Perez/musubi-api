# HU-13 — Publicar una necesidad de compra (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (09-Nov-2026): campos y límites, 5 activas, 30 días y "Otro".

## T2 — Datos
- **Crear:** `wantedRequests` en `db/009-solicitudes.js` y los repositorios Mongo e InMemory.
- **Verificar:** `WantedRepositoryContract` (en memoria; en Mongo en el CI) y `SchemaScriptTests`.
- **Estado:** ✅ Hecho (09-Nov-2026).

## T3 — Servicio y endpoints
- **Crear:** `WantedService`, `POST /wanted`, `GET /wanted/mine`, `GET /wanted/{id}` e `ICategoryDirectory`.
- **Verificar:** `WantedServiceTests`, `WantedFlowTests`, `WantedAuthTests` y `WantedContractTests`.
- **Estado:** ✅ Hecho (09-Nov-2026).

## T4 — Documentación
- **Estado:** ✅ Hecho (09-Nov-2026).

## Pendientes
- La app: `musubi-app:HU-13` (`Necesidad-1…3`, `Necesidad-publicada` y `Mis-solicitudes`).
- `NOTE (HU-14)`, `NOTE (HU-15)`, `NOTE (HU-16)` y `NOTE (HU-18)`.

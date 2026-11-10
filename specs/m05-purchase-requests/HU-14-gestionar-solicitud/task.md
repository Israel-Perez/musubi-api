# HU-14 — Editar, cerrar o cancelar mi solicitud (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (09-Nov-2026): con ofertas el producto no cambia, cerrar y cancelar son idempotentes y una vencida ya no cambia.

## T2 — Datos
- **Crear:** `UpdateDetailsAsync` y `SetStatusAsync` en Mongo y en memoria (sin script: `db/009` ya admite los estados).
- **Verificar:** `WantedRepositoryContract` (en memoria; en Mongo en el CI).
- **Estado:** ✅ Hecho (09-Nov-2026).

## T3 — Servicio y endpoints
- **Crear:** `UpdateAsync`, `CloseAsync`, `CancelAsync` y las 3 rutas.
- **Verificar:** `WantedServiceTests`, `WantedFlowTests`, `ManageWantedAuthTests`, `WantedAuthTests` y `WantedContractTests`.
- **Mutación:** sin el dueño en el filtro en memoria (`!string.IsNullOrEmpty(requesterId)`), 3 Unit y 2 Security en rojo.
- **Estado:** ✅ Hecho (09-Nov-2026).

## T4 — Documentación
- **Estado:** ✅ Hecho (09-Nov-2026).

## Pendientes
- La app: `musubi-app:HU-14`.
- `NOTE (HU-25)` (RN-15) y `NOTE (HU-30)`.

# HU-50 — Manejar la cantidad disponible de un producto (tareas)

## T1 — Detallar la spec
- **Qué:** conciliar Jira (SCRUM-67 y el comentario del PO) con la maqueta y el código de HU-24; dejar con evidencia los tres criterios que ya existían.
- **Estado:** ✅ Hecho (12-Nov-2026). Decisiones de la usuaria: menú más "Reponer" en la app y choque aceptado.

## T2 — API
- **Crear:** `SetStockAsync` (repositorio y servicio), `PUT /products/{id}/stock`, `SetStockRequest` y `InvalidStockChange`.
- **Verificar:**
  - `SetStock_OnlyTheOwner_KeepsPaused_AndDecidesSoldOut` (batería);
  - 3 pruebas en `MyListingsServiceTests` (5 casos);
  - `SetStock_ToZeroGoesToSoldOut_AndRestockBringsItBack`;
  - +5 casos de seguridad;
  - el contrato.
- **Estado:** ✅ Hecho (12-Nov-2026).

## T3 — Documentación y versión
- **Qué:** `docs/endpoints.md` (82 filas), `docs/errores.md`, `docs/versionado.md` + `0.5.0-alpha.21`, la fila de HU-50 en `docs/trazabilidad.md` y `Memoria.md`.
- **Estado:** ✅ Hecho (12-Nov-2026).

## T4 — App
- **Qué:** "Cambiar piezas" en el menú y "Reponer" en Agotados (`musubi-app:HU-50`).
- **Estado:** 🚧 En curso.

## Pendientes
- `NOTE (HU-52)`: un apartado vencido regresa la pieza solo.

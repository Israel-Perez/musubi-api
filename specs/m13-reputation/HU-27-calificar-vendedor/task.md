# HU-27 — Calificar al vendedor al terminar un pedido (tareas, API)

## T1 — Spec
- **Estado:** ✅ Hecho (12-Nov-2026), con HU-45.

## T2 — Datos
- **Prueba:** `RatingRepositoryContract` (3), `MarkRated_*`, `AddRating_*` y `Ratings_HaveTheirIndexes_AndRejectAnInvalidRating`.
- **Estado:** ✅ Hecho.

## T3 — Servicio y rutas
- **Prueba:** `UnitTest/Reputation/RatingServiceTests.cs` (15).
- **Estado:** ✅ Hecho.

## T4 — Integración, seguridad y contrato
- **Estado:** ✅ Hecho: Integration 168 · Security 220 · Contract 22.

## T5 — Documentación y versión
- **Estado:** ✅ Hecho: `0.5.0-alpha.25`, esquema 20, `check_endpoints` OK (85).

## T6 — Prueba en el celular
- **Qué:** con la API desplegada (`infra-up` con `db/020`), calificar un pedido completado desde las dos cuentas y ver la reputación y las opiniones.
- **Estado:** ⏳ Por hacer.

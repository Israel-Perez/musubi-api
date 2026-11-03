# 02-02 — Caché en 3 niveles y paginación (tareas)

## T1 — Detallar
- **Qué:** proveedor de Redis con costo cero, llaves, TTL y políticas de salida.
- **Estado:** ✅ Hecho (03-Nov-2026): Redis Cloud gratuito y opcional; llaves `v1:categories:{comunidad}` y `v1:careers:{comunidad}` (10 min); salida `catalog` (10 min) y `showcase` (30 s).

## T2 — Caché de datos
- **Qué:** `Platform/Caching` (`AppCache`) y los decoradores de categorías y carreras, con su degradación.
- **Estado:** ✅ Hecho. `CachingTests` (5).

## T3 — Caché HTTP
- **Qué:** caché de salida, `ETag` y `304` en las rutas públicas y en carreras.
- **Estado:** ✅ Hecho. `CachingFlowTests` (4) y `OutputCacheTests` (1).

## T4 — Outbox
- **Estado:** ➡️ Movido a su primer consumidor (`NOTE (HU-18)`, `NOTE (HU-31)`).

## T5 — Documentación y despliegue
- **Qué:** `docs/endpoints.md`, `01-02`, `01-04`, `infra/README.md`, `docs/versionado.md` (`0.4.0-alpha.2`), `Memoria.md` e `infra-up`.
- **Estado:** 🚧 Docs listas; falta `infra-up` y, si se quiere Redis compartido, el secreto `/musubi/prod/Cache/ConnectionString`.

# HU-22 — Bandeja de conversaciones (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (06-Nov-2026).

## T2 — Datos
- **Crear:** `db/006-chat-bandeja.js`. **Cambiar:** documentos y repositorios del chat; `MongoSchema.ExpectedVersion = 6`.
- **Verificar:** `ChatRepositoryContract` (en memoria 9/9; en Mongo en el CI) y `SchemaScriptTests`.
- **Estado:** ✅ Hecho (06-Nov-2026).

## T3 — Servicio y endpoints
- **Crear:** `InboxAsync`, `MarkReadAsync`, `GET /conversations` y `POST /conversations/{id}/read`.
- **Verificar:** `ChatFlowTests` (+5), `ChatAuthTests` y `ChatContractTests`.
- **Estado:** ✅ Hecho (06-Nov-2026).

## T4 — Documentación
- **Estado:** ✅ Hecho (06-Nov-2026): `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `docs/trazabilidad.md`, `docs/versionado.md` y `Memoria.md`.

## Pendientes
- La app: `musubi-app:HU-22` (pantalla `Mensajes`).
- `NOTE (HU-16)`: pestañas de solicitudes y ofertas.

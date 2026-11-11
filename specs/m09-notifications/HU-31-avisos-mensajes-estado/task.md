# task.md — HU-31 (API)

- **T1 · Datos:** `db/017-avisos.js` y los repositorios con su batería compartida. ✅
- **T2 · Push:** `FcmPushSender`, `PushQueue` y `PushWorker`. ✅
- **T3 · Centro y rutas:** `NotificationService` y `NotificationsController` (5 rutas). ✅
- **T4 · Disparadores:** chat, aviso de la IA, pedido, acuerdo, ofertas y recordatorios. ✅
- **T5 · Documentos:** endpoints (76), errores, modelo de datos, 01-04, versionado (`0.5.0-alpha.18`), trazabilidad, lineamientos, AGENTS y Memoria. ✅

## Pendientes con dueño
- `NOTE (HU-24)`: avisos de pedido confirmado, cancelado y completado.
- Despliegue: la llave en `/musubi/prod/Firebase/ServiceAccount` antes de `infra-up`.

# HU-31 — Avisos de mensajes y cambios de estado

**HU / Jira / Sprint / Módulo / Contraparte:** HU-31 / SCRUM-49 / Sprint 4 (9 – 20 nov 2026) / M-09 / [musubi-app:HU-31](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m09-notifications/HU-31-avisos-mensajes-estado)

## Contexto
Jira (SCRUM-49): Como **usuario**, quiero **recibir notificaciones de mensajes nuevos y de cambios de estado en mis solicitudes y pedidos**, para **dar seguimiento a mis tratos**.

- Maqueta: `Notificaciones` (pestañas Todas, Coincidencias, Pedidos y Solicitudes; "Marcar leídas"; Hoy y Ayer).
- Trazabilidad: RF-41, RF-42, RF-43 · CU-14 · RNF-15 · L-16c.
- Decisión de la usuaria (10-Nov-2026): **FCM directo** desde la API (HTTP v1), sin otro servicio en medio.

**Depende de:** `HU-20` (chat), `HU-16`/`HU-17` (ofertas), `HU-23` (pedidos), `HU-34` (acuerdo).

## Requisitos
1. **R1 · Mensaje nuevo (RF-42):** push a la otra persona con el primer nombre de quien escribe y el texto (hasta 140). No va al historial: su historial es la bandeja (HU-22). El aviso "Ana pidió un resumen con IA" también llega por push.
2. **R2 · Cambios de estado (RF-41)**, con push y en el historial:
   - pedido nuevo, a quien vende ("Pedidos");
   - la otra persona confirmó el acuerdo, o los dos ya lo confirmaron ("Pedidos");
   - oferta nueva a mi solicitud, oferta aceptada u oferta no elegida ("Solicitudes");
   - mi solicitud vence en menos de 24 horas, o ya venció ("Solicitudes"). Lo revisa un proceso cada hora; con 2 instancias, `dedupeKey` deja pasar un solo aviso.
3. **R3 · Centro de notificaciones (RF-43):** `GET /notifications` por pestaña, del más nuevo al más viejo, con cursor y los no leídos; marcar uno o todos como leídos. Cada aviso dice qué abre (`conversation`, `order` o `wanted`). Se borran solos a los 60 días.
4. **R4 · Dispositivos:** la app registra su token de FCM al iniciar sesión y lo quita al cerrarla; si el token era de otra cuenta, pasa a la nueva. Un token que FCM reporta como desinstalado se borra. Sin renovarse, se borra a los 60 días.
5. **R5 · Degradación (RNF-15):** sin la llave de la cuenta de servicio, o con FCM caído, el aviso queda en el historial y el mensaje, el pedido o la oferta siguen su curso. El envío va en segundo plano (no hace esperar a quien escribe).
6. **R6 · Privacidad:** la llave de la cuenta de servicio vive solo en el back (Parameter Store `/musubi/prod/Firebase/ServiceAccount`); cada persona solo ve, marca o quita lo suyo.

## Diseño
- Módulo nuevo `Modules/Notifications/`:
  - `INotificationCenter` y `Notice` (contrato para los demás módulos);
  - `NotificationService` y `NotificationsController` (5 rutas);
  - `IPushSender`: `FcmPushSender` (JWT RS256 firmado con la llave → access token de una hora → `messages:send`, URLs fijas) y `RecordingPushSender` (Development y pruebas);
  - `PushQueue` (cola acotada por instancia) y `PushWorker`;
  - `WantedReminderJob` y `WantedReminderWorker`.
- Solicitudes expone `IWantedReminders` (las activas que vencen en una ventana).
- Datos: `db/017-avisos.js` (esquema 17): `notifications`, `deviceTokens` e `ix_status_expires` en `wantedRequests`.
- Configuración (01-04): `Firebase:Provider`, `Firebase:ServiceAccount` y `Notifications:ReminderMinutes`.

## Contratos
Filas M-09 de [`docs/endpoints.md`](../../../docs/endpoints.md#m-09--notificaciones), errores en [`docs/errores.md`](../../../docs/errores.md) y `ContractTest/Fixtures/notification-page.json`.

## Tests
- `UnitTest/Notifications/NotificationTests.cs`: historial y push, solo push en mensajes, `dedupeKey`, lista por pestaña y no leídos, marcar solo lo mío, tokens, entrega y borrado de tokens desinstalados, la llave, el JWT RS256 verificado con la llave pública, FCM con un `HttpMessageHandler` falso y los recordatorios.
- Disparadores: `ChatServiceTests`, `OrderServiceTests`, `OfferServiceTests` y `AgreementServiceTests`.
- `TestShared/Notifications/NotificationRepositoryContract.cs` (memoria y MongoDB real), `SchemaScriptTests` (índices y validación de 017).
- `IntegrationTest/Notifications/NotificationFlowTests.cs`, `SecurityTest/Auth/NotificationsAuthTests.cs` y `ContractTest/NotificationsContractTests.cs`.

## Criterios
- [x] Se notifica cada mensaje nuevo (R1, RF-42).
- [x] Se notifican los cambios de estado de solicitudes (próxima a expirar, expirada) y de pedidos (R2, RF-41). Pedido confirmado, cancelado y completado: `NOTE (HU-24)`.
- [x] El centro de notificaciones muestra el historial; al seleccionar uno se abre el elemento relacionado (R3, RF-43; la pantalla está en `musubi-app:HU-31`).
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- `NOTE (HU-24)`: pedido confirmado, cancelado y completado (los estados llegan con esa HU; se avisan con el mismo `INotificationCenter`).
- `NOTE (HU-29/30)`: "Coincidencias" y "lleva 34 días sin venderse".
- `NOTE (HU-54)`: apagar avisos por tipo en Ajustes; hoy se apagan desde Android.
- Si una instancia se reinicia con push en cola, esos push se pierden; el historial ya quedó guardado.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 11-Nov-2026 (detallado e implementado)
- **Detalle:** FCM directo con la cuenta de servicio, historial con pestañas y recordatorios de vencimiento.

# db — scripts de la base (MongoDB)

Scripts `mongosh`, numerados e idempotentes, que crean las colecciones, sus validaciones `$jsonSchema`, sus índices y los datos semilla. Juntos forman el **script de la base de datos** que se entrega en la evaluación (lineamiento 19). El modelo completo está en [`docs/modelo-datos.md`](../docs/modelo-datos.md).

| Script | Qué crea | Versión del esquema |
|---|---|---|
| `001-identidad.js` | `communities` (+ UTTT), `users`, `verificationCodes`, `refreshTokens`, `_meta` | 1 |
| `002-publicaciones.js` | `categories` (+ 6 de la UTTT), `products` | 2 |
| `003-dominio-revision.js` | Dominio `musubiapp.com.mx` en la UTTT, para la cuenta de los revisores de Google Play (SCRUM-88) | 3 |
| `004-perfiles.js` | `profiles`, `careers` (+ 38 de la UTTT, a validar) y `communities.academicTerm` (UTTT: 11 cuatrimestres) (HU-04) | 4 |
| `005-chat.js` | `conversations` y `messages` (HU-20) | 5 |
| `006-chat-bandeja.js` | No leídos por lado y copia de nombres y portada en `conversations` (HU-22) | 6 |
| `007-busqueda.js` | Palabras normalizadas en `products.searchWords` + índice `ix_community_status_search` (HU-09) | 7 |
| `008-reportes-bloqueos.js` | `reports` (HU-28) y `blocks` (HU-57) | 8 |
| `009-solicitudes.js` | `wantedRequests` (HU-13) | 9 |
| `010-filtros-precio.js` | Índices de precio asc/desc, con y sin categoría, para ordenar sin memoria (HU-10) | 10 |
| `011-ofertas.js` | `offers` ("Yo tengo este producto", HU-16): una por persona y solicitud | 11 |
| `012-ofertas-chat.js` | Conversaciones de una oferta (`context.type` `offer`) y ofertas aceptadas o rechazadas (HU-17) | 12 |
| `013-favoritos.js` | `favorites` (HU-53): un favorito por usuario y publicación | 13 |
| `014-pedidos.js` | `orders` (HU-23: uno abierto por conversación), `meetingPoints` (los 14 edificios de la UTTT con sus carreras) y `counters` (el número MS-NNNN) | 14 |
| `015-acuerdos.js` | `agreements` (HU-32/33/34: el acuerdo con IA, uno por conversación, con TTL de 30 días) y `messages.kind` (avisos de la app en el chat) | 15 |
| `016-publicacion-vendida.js` | Publicación vendida: `products.status` admite `sold` y `soldAt` (HU-08, "Marcar como vendido") | 16 |
| `017-avisos.js` | `notifications` (HU-31: el centro de notificaciones, TTL de 60 días, un aviso por `dedupeKey`), `deviceTokens` (tokens de FCM, TTL de 60 días) e `ix_status_expires` en `wantedRequests` (recordatorios de vencimiento) | 17 |
| `018-preferencias.js` | `preferences` (HU-54): tema de apariencia por cuenta | 18 |
| `019-pedidos-estados.js` | Pedidos aceptados, cancelados y completados (HU-24): `orders.handover` (la entrega confirmada por cada parte) y `orders.cancellation` (quién y el motivo de la lista); `notifications.type` admite los cuatro avisos de los pedidos | 19 |
| `020-calificaciones.js` | Calificaciones (HU-27/45) | 20 |
| `021-ventas-compras.js` | `orders`/`profiles` con historial de ventas y compras (HU-26) | 21 |
| `022-interacciones.js` | `interactions` (HU-35: lo que cada persona vio, guardó, escribió y compró, TTL de 90 días) y `preferences.recommendations` | 22 |
| `023-popularidad.js` | `productStatsDaily` (HU-49): vistas agregadas por producto/día UTC, TTL de 30 días | 23 |
| `024-coincidencias.js` | `matches` (RF-25: cada coincidencia entre una solicitud y una publicación, a quién se avisó y si terminó en pedido, TTL de 90 días) y el validador de `notifications` con `wanted_match`, `offer_closed` y `product_match` | 24 |
| `025-busquedas-guardadas.js` | `savedSearches` (HU-12: "Avísame cuando aparezca", privadas, TTL de 30 días) y `saved_search_match` en el validador de `notifications` | 25 |
| `026-apoyo-vendedores.js` | `ix_status_created` en `products` (HU-36: el aviso de las 3 semanas) y `slow_listing` / `my_products` en el validador de `notifications` | 26 |
| `027-pagos.js` | `payoutAccounts` (HU-55: la cuenta para recibir pagos con la CLABE cifrada), `orders.payment` con la transferencia y el código de entrega, `ix_transfer_reported` y `transfer_reported` / `transfer_not_received` / `transfer_reminder` en el validador de `notifications` | 27 |
| `028-apartados.js` | `reservations` (HU-52: apartar 24 h, uno activo por persona con `ux_buyer_active`, `ix_status_expires` para el vencimiento) y `reservation_created` / `reservation_cancelled` / `reservation_expired` / destino `reservation` en el validador de `notifications` | 28 |
| `029-avisos-disponibilidad.js` | `stockAlerts` (HU-51: "Avísame" en una agotada, uno por persona y publicación con `ux_user_product`, TTL de 90 días) y `back_in_stock` en el validador de `notifications` | 29 |
| `030-moderacion.js` | Revisión de reportes en `reports` (`dismissed`/`upheld`, `subjectId`, quién y cuándo lo revisó; `ix_subject_status`), `moderationLog` (HU-39/40: cada acción de un administrador, RNF-04) y `report_reviewed` / `product_removed` en el validador de `notifications` | 30 |
| `031-categorias.js` | Las acciones de categorías en el validador de `moderationLog` (HU-41: agregar, renombrar, desactivar y volver a activar; `targetType: category`). `categories` no cambia | 31 |
| `032-recordatorio-entrega.js` | `orders.handover` con "Ya llegué" de cada parte (`buyerArrivedAt`, `sellerArrivedAt`), `ix_confirmed_delivery` (parcial: los confirmados por hora de entrega, para el recordatorio una hora antes) y los avisos `delivery_reminder` y `order_arrived` (HU-62) | 32 |

## Aplicar

Se aplican **en orden** contra la base que indica la cadena de conexión. Correrlos otra vez no cambia nada.

**En la nube los aplica `infra-up`** antes de reemplazar las instancias (paso "Esquema de la base", con la cadena de Parameter Store). A mano solo hace falta en local o para revisar un script antes del despliegue.

```powershell
# Local (deploy/docker-compose.local.yml)
Get-ChildItem db/*.js | Sort-Object Name | ForEach-Object {
  mongosh "mongodb://musubi:<clave>@localhost:27017/musubi?authSource=admin" --quiet --file $_.FullName
}
# Atlas: la misma línea con la cadena mongodb+srv:// (secreto; nunca en el repo)
```

## Reglas

- Un cambio de esquema es un **script nuevo** (`NNN-<slug>.js`); nunca se edita uno ya aplicado en la nube.
- Cada script sube `_meta.version` con `$max` y se registra en `_meta.applied`.
- `MongoSchema.ExpectedVersion` (en `Musubi.Api/Platform/Data/`) debe ser igual al número del último script. Lo verifica `UnitTest/Data/SchemaVersionTests.cs`. Si la base tiene una versión menor, `/health/ready` responde Unhealthy y el balanceador no le manda tráfico a esa instancia.
- `DatabaseTest` corre los scripts reales contra un MongoDB en contenedor (Testcontainers) antes de probar los repositorios.

## Dar o quitar el rol de administrador (HU-39)

El rol `admin` abre "Administración" en la app (reportes y cuentas). Se da de dos formas:

- **Por configuración:** el correo en `Admin:Emails` (Parameter Store `/musubi/prod/Admin/Emails/0`) recibe el rol la próxima vez que inicia sesión.
- **En la base**, con `mongosh` contra la cadena de Parameter Store:

```js
db.users.updateOne({ emailNormalized: 'correo@uttt.edu.mx' }, { $addToSet: { roles: 'admin' } });
// Quitarlo (también hay que sacarlo de Admin:Emails, o lo recibe otra vez al entrar):
db.users.updateOne({ emailNormalized: 'correo@uttt.edu.mx' }, { $pull: { roles: 'admin' } });
```

El cambio en la base aplica de inmediato en la API (cada acción vuelve a leer la cuenta). La app lo muestra cuando la persona vuelve a iniciar sesión.

# Memoria del proyecto — musubi-api

## Trabajo en curso — HU-05 (SCRUM-34)

- **Qué:** API de “Consultar perfil y reputación de otro usuario”. `GET /profiles/{id}` ya existía (HU-04) con nombre, foto, campus, `memberSince`, carrera/periodo y `stats { ratingAvg, ratingCount, sales }`, sin correo y con 404 para otro campus/bloqueo. Faltaba la pestaña “Publicaciones (N)” de `Perfil-vendedor`.
- **Cómo:** se reutiliza `GET /products` con un filtro `sellerId` (nuevo en `ProductQuery`); con `hideSoldOut=true` devuelve solo las `active` y `total` es el contador. La app compone el perfil con `GET /profiles/{id}` + `GET /products?sellerId=…`. No se creó ruta nueva ni se agregó `activeListings` al perfil.
- **Reputación:** `ratings` sigue siendo de HU-27/HU-45; sin calificaciones el perfil devuelve `ratingAvg: null` y `ratingCount: 0` (RN-26) y la app muestra “Sin calificaciones”. `NOTE (HU-27)`.
- **Datos:** sin colección, script `db/`, índice ni cambio de `MongoSchema`; `ix_seller_id` de `products` cubre el filtro.
- **Versión compartida:** `docs/versionado.md` es una sección compartida. La fila `0.5.0-alpha.22` (renumerada el 12-Nov-2026: `alpha.21` quedó para HU-50 de Kazumi, que se fusionó antes) se replicó en `musubi-app` y se registró en su `Memoria.md`; `0.5.0-alpha.20` es de HU-24, ya en `main` con el esquema 19.
- **Verificación local (11-Nov-2026, `main` `b7bdb1d`, ya con HU-24):** build Release 0/0; Unit 564, Integration 165, Security 207 y Contract 20 al 100 % con `--no-build`; `check_endpoints` OK (81 rutas); `critic` PASS (28 archivos, 0 avisos); `check_shared_sections` OK (6 bloques); `VersioningTests` con `0.5.0-alpha.21`. `DatabaseTest` queda para el CI (Docker apagado en local). `security-reviewer` PASS y `traceability-clerk` OK.
- **Pendiente:** app de HU-05; `NOTE (HU-27)`/`NOTE (HU-45)` (promedio y opiniones). En la revisión del PR (12-Nov-2026) la app quedó igual a la maqueta: “Mensaje” del perfil (app) abre la conversación con `POST /conversations` de HU-20, sin ruta nueva; “Seguir” y “Compartir” avisan que llegan pronto (la API no tiene seguidores) y “A tiempo” va con “—” hasta HU-27.

## Trabajo en curso — HU-54 (SCRUM-71)

- Se implementó la API de preferencias de apariencia en M-02: GET/PUT autenticados, tema por cuenta (`system` por defecto; `system`/`light`/`dark`), API `0.5.0-alpha.19` y esquema 18 (`db/018-preferencias.js`), borrado al eliminar la cuenta y pruebas en Unit/Integration/Security/Contract/Database.
- La app consume el contrato, aplica `ThemeMode`, ofrece selector en Ajustes y revierte ante fallo de guardado. Sin preferencias de notificaciones, cuyo MVP sigue sin definición.
- Verificación final tras rebase (10-Nov-2026): API con .NET SDK 10.0.400: build Release 0 errores/0 advertencias; Unit 450, Integration 152, Security 166, Contract 17 y Database 109 pasaron. `critic` PASS (28 archivos, 0 avisos) y `check_endpoints` OK (67 rutas). Se ajustó el script de preferencias a 015 para no colisionar con el esquema 014 de HU-23.
- **Renumerado el 11-Nov-2026** a `db/018` y `alpha.19`, porque mientras el PR esperaba entraron a `main` el acuerdo con IA (`db/015`), HU-08 (`db/016`) y HU-31 (`db/017`). App con Flutter 3.47.7/Dart 3.13.5 sobre `main` actualizado: `pub get`, análisis sin issues, formato sin cambios y 492 pruebas pasaron; critic PASS (15 archivos, 0 avisos).

## Trabajo en curso — HU-27 y HU-45 (SCRUM-46, SCRUM-50)

- **Qué:** calificar un pedido completado. `POST /orders/{id}/rating` (quien compra califica a quien vende y al revés), `GET /profiles/{id}/ratings` ("Opiniones"), `stats.onTime` en el perfil público y `ratedByMe`/`rateUntil` en `OrderResponse`.
- **Decisiones de la usuaria (12-Nov-2026):** un solo promedio (lo recibido como quien vende y como quien compra; cada calificación guarda su rol), 7 días para calificar, "A tiempo" = porcentaje con "Puntual", y quien compra recibe sus propios chips (Puntual, Buena comunicación, Amable, Pagó lo acordado).
- **Cómo:**
  - `ratings` (`db/020`) con `ux_order_rater` y `ix_rated_id`; el pedido guarda `ratings { buyerAt, sellerAt }` y se marca primero (atómico), así que dos toques a la vez dejan una sola calificación.
  - El perfil suma con `$inc` (`ratingSum`, `ratingCount`, `onTimeCount`) y después fija `ratingAvg = ratingSum / ratingCount` con un pipeline: dos calificaciones a la vez terminan igual.
  - M-13 usa contratos de los otros módulos: `IOrderRatingGate` (M-08) e `IProfileRatings` (M-02).
  - Al borrar una cuenta se borran las que recibió; en las que dio queda "Usuario eliminado".
- **Versión:** `0.5.0-alpha.25`, esquema 20 (`alpha.24` es SCRUM-97, en revisión).
- **Verificación (12-Nov-2026):** build Release 0/0; Unit 610, Integration 168, Security 220 y Contract 22; `check_endpoints` OK (85). `DatabaseTest` corre en el CI (sin Docker local).

## Despliegue del 12-Nov-2026 (SCRUM-87)

- `infra-up` con `0.5.0-alpha.23` y `db/019` (esquema 19): HU-24, SCRUM-96 (aviso del push al arrancar), dependencias de noviembre, HU-50, HU-05 y HU-25.
- La app `0.9.0+11` va a la prueba cerrada de Google Play con esa API (fila compartida en `docs/versionado.md`).

## Trabajo en curso — HU-25 (SCRUM-44)

- **Qué:** `GET /api/v1/orders/mine`: mis compras por pestaña (`active` = pendientes y confirmados, `completed`, `cancelled`; sin pestaña, todas), con `counts` de las tres, `total` de la pestaña y cursor. Cada elemento es un `OrderResponse`.
- **Decisiones de la usuaria (12-Nov-2026):** solo compras (las ventas son de HU-26) y pestañas por estado en "Ver todos".
- **Cómo:** `FindByBuyerAsync`/`CountByBuyerAsync` sobre `ix_buyer_id`; sin script, el esquema sigue en 19. Ruta propia porque `GET /orders` ya exige `conversationId` (HU-23).
- **Versión:** `0.5.0-alpha.23` (fila compartida en `docs/versionado.md`).
- **Verificación (12-Nov-2026):** build Release 0/0; Unit 590, Integration 167, Security 216 y Contract 21 al 100 %; `check_endpoints` OK (83).
- **Siguiente:** la app de HU-25 y desplegar (`infra-up`) cuando la usuaria lo autorice: `alpha.20` a `alpha.23` van juntas.

Bitácora viva: estado, decisiones con su porqué y errores a evitar. Reglas de mantenimiento en `AGENTS.md` §2. Las lecciones permanentes viven en `specs/phase-00-constitution/00-04-lessons-learned/spec.md`.

## Estado actual (11-Nov-2026)

- **En producción desde el 11-Nov-2026:** API `0.5.0-alpha.19`, esquema 18 (`infra-up` con `db/017` y `db/018`), con push (HU-31), modo noche (HU-54), HU-07, HU-08, el acuerdo con IA y los pedidos. La app `0.8.0+10` va a Google Play con ese lote.
- **Antes, en producción:** API `0.5.0-alpha.11`, infra `0.3.5` y esquema 12 (`infra-up` del 09-Nov-2026), con Redis Cloud en las 2 instancias. Incluía:
  - chat y bandeja (HU-20, HU-22), detalle (HU-11), búsqueda (HU-09) y filtros (HU-10);
  - reportar y bloquear (HU-28, HU-57), con aviso de reportes a `/musubi/prod/Reports/NotifyEmail`;
  - solicitudes de compra (HU-13), el tablero "Se busca" (HU-15) y las ofertas: enviarlas (HU-16) y elegirlas (HU-17).
  - La moderación del texto usa el léxico local (el plan gratuito de AWS no incluye Translate ni Comprehend); las fotos siguen con Rekognition.
- **App en Google Play:** `0.6.0+8` en la prueba cerrada (lote de HU-09 a HU-17; ver la Memoria de musubi-app).
- **En `main`, sin desplegar:**
  - HU-07 (`0.5.0-alpha.12`, Kazumi) y HU-14 (`0.5.0-alpha.13`), sin script nuevo;
  - HU-53 (favoritos, Eliuht; `0.5.0-alpha.14`), que sí trae `db/013`;
  - HU-05 (perfil del vendedor, `0.5.0-alpha.22`), sin script nuevo; API en revisión.
- **HU-08 (Tanis):** PR #61, renumerado a `db/016` y `alpha.17` el 11-Nov-2026 porque el acuerdo con IA entró antes con `db/015` y `alpha.16`.
- **HU-23 (pedidos):** en `main` (`0.5.0-alpha.15`, esquema 14), API y app. Sin desplegar.
- **HU-32/33/34 (acuerdo con IA):** API `0.5.0-alpha.16` y esquema 15 (`db/015-acuerdos.js`) en `feature/SCRUM-53-acuerdo-con-ia`. La llave de Gemini ya está en user-secrets y en `/musubi/prod/Gemini/ApiKey` (la puso la usuaria).
- **HU-31 (push):** API `0.5.0-alpha.18` y esquema 17 (`db/017-avisos.js`) en `feature/SCRUM-49-avisos-push`. La llave ya está en `/musubi/prod/Firebase/ServiceAccount` y en user-secrets (11-Nov-2026).
  - **Error a evitar:** las fábricas de prueba en Development cargan los user-secrets de quien las corre. Con `Firebase:Provider=Fcm` en local, `NotificationFlowTests` usó el FCM real y falló (en el CI pasaba). Las fábricas fijan `Firebase:Provider` y `GenAi:Provider` en `InMemory`.
- **HU-24 (aceptar y actualizar pedidos):** API `0.5.0-alpha.20` y esquema 19 (`db/019-pedidos-estados.js`) en `main`; falta la app. Ver T41.
- **Push en producción (11-Nov-2026):** verificado en un celular real. Antes falló dos veces. La llave estaba en us-east-2 y en varias líneas (el `api.env` de `user-data` es de una línea por variable), y después Google rechazó la llave `8e06c5` (`invalid_grant`). Hoy usa la llave nueva `5270b9` en us-east-1. Pendiente SCRUM-96: avisar al arrancar si el push queda apagado.
- **SCRUM-96 (12-Nov-2026):** `PushStartupCheck` deja en el log, al arrancar, si el push está activo (proyecto y el inicio del id de la llave) o por qué está apagado (`MissingKey`, `UnreadableKey`, `IncompleteKey`; o `InMemory` fuera de Development). Va sin subir versión, en el mismo `0.5.0-alpha.20` que HU-24, porque ese tag todavía no existe. `infra/README.md` documenta la región, la regla de una línea y cómo compactar la llave. Lección 23 de `00-04`.
- **Siguiente:** desplegar `alpha.20` (HU-24 y SCRUM-96) cuando la usuaria lo autorice, y luego HU-25.
- **Pendientes del usuario:**
  - proyecto de Firebase (HU-31);
  - validar con el equipo la lista de carreras de la UTTT;
  - rotar la contraseña de Atlas y borrar la llave de Google (diferido por el usuario).
- **Dependabot (12-Nov-2026):** los parches de AWSSDK y `setup-dotnet` (#75 a #80) van juntos en un PR. `dependabot.yml` agrupa `AWSSDK.*` y deja de proponer versiones mayores de `dotnet/sdk` y `dotnet/aspnet`.
  - **Error a evitar:** #73 (`aspnet:11`) pasaba el CI porque `docker-build` solo construye la imagen; una app `net10.0` no arranca sobre el runtime 11. Una versión mayor de .NET se cambia completa (target, `global.json` y las dos imágenes) y con su propia HU.
- **HU-07 (Kazumi):** la API está en `main` (`0.5.0-alpha.12`); falta la app.
- **En curso (10-Nov-2026):** HU-08 (estado de mis productos), PR #61: API `0.5.0-alpha.17` y esquema 16 (`db/016-publicacion-vendida.js`); falta la app.

## T42 — Manejar la cantidad disponible (12-Nov-2026, SCRUM-67, HU-50)

**Qué se hizo**
- `PUT /api/v1/products/{id}/stock` (`MyProductsController.SetStock` → `MyListingsService.SetStockAsync` → `IProductRepository.SetStockAsync`).
  - Piezas de 0 a 10: con 0 queda `soldOut` y con más vuelve a `active`.
  - Una pausada sigue `hidden`; una vendida responde 409 y una ajena 404.
- Los otros tres criterios ya existían (HU-06, HU-10, HU-11 y HU-24) y quedaron con evidencia en la spec.
- API `0.5.0-alpha.21`, sin script en `db/`. Unit 569 · Integration 165 · Security 212 · Contract 20 · endpoints 82.

**Decisiones (con su porqué)** (usuaria y PO, 12-Nov-2026)
- **`PUT { stock }` fija el número:** el vendedor dice lo que tiene a la mano. Si un pedido aparta piezas en el mismo instante, gana la última escritura. Es poco probable, se corrige volviendo a cambiarlas y quedó en Límites.
- **El estado se decide en la misma escritura** (update con pipeline, como `SetPausedAsync`): sin leer antes y sin perder la pausa.
- **Mismo código `listings.invalid_stock` con otro texto** ("0 a 10"): la app traduce por código, y al publicar sigue siendo de 1 a 10.

**Errores a evitar**
- **Un build que falla en un solo proyecto deja corriendo los binarios viejos de ese proyecto:** CA1062 rompió `SecurityTest` y la suite "pasó" con 207. Revisa `0 Errores` antes de leer los conteos.
- **Ya existía un `WithStock` en `InMemoryListingRepositories`:** S4136 pide sobrecargas juntas. Se reutilizó con `StatusWithStock` para el estado.

**Pendientes con dueño**
- `NOTE (HU-52)`: un apartado vencido regresa la pieza solo.

## T41 — Aceptar y actualizar mis pedidos (11-Nov-2026, SCRUM-43, HU-24)

**Qué se hizo**
- `POST /orders/{id}/accept`: lo hace quien vende, sobre un pedido pendiente con el pago ya elegido. Aparta las piezas.
- `POST /orders/{id}/cancel`: lo hace cualquiera de las partes, con un motivo de `CancelReasons`. Si el pedido estaba confirmado, devuelve las piezas.
- `POST /orders/{id}/handover`: cada parte confirma su lado. Con las dos confirmaciones el pedido queda completado y, si la publicación se quedó sin piezas, pasa a vendida.
- `OrderResponse` agrega `handover`, `cancellation` y `updatedAt`.
- Avisos `order_confirmed`, `order_cancelled`, `order_handover` y `order_completed`.
- `db/019`: validadores de `orders` y `notifications`. `MongoSchema.ExpectedVersion = 19`, versión `0.5.0-alpha.20`.

**Decisiones**
- **RN-19, decisión de la usuaria:** se completa cuando confirman **las dos** partes. El código de entrega de HU-52 contará como la confirmación de quien vende.
- **Aceptar exige el pago elegido**, también decisión de la usuaria, porque en la maqueta el pedido se confirma después del pago. El filtro de la escritura incluye `payment`.
- **Motivos en lista cerrada y por rol:** "Ya no lo necesito" solo para quien compra y "Ya no tengo el producto" solo para quien vende. No se modera nada, porque no hay texto libre.
- **Apartar = restar de `stock`**, sin agregar un contador `reserved`. La vitrina, "Mis productos" y el pedido nuevo ya leen `stock` como disponible, así que funcionan sin cambios.
  - Con todo apartado, la publicación queda en `soldOut` (sigue visible y puede volver si se cancela).
  - Pasa a `sold` solo al completar el último pedido abierto.
- **`IProductStock` vive en Listings** y Orders lo consume. Un módulo no escribe en la colección de otro. Cada cambio de piezas invalida `ShowcaseTag`.
- **Un servicio aparte (`OrderLifecycleService`)**, porque `OrderService` ya mide 460 líneas.

**Errores a evitar**
- **El validador de `notifications` (db/017) tiene la lista de tipos.** Un tipo nuevo sin su script falla en MongoDB: `NotificationService` lo registra y el aviso se pierde, y las pruebas en memoria no lo detectan. `db/019` la amplía.
- En `NewProduct(...)` de `TestShared`, el tercer parámetro posicional es la **categoría**, no el estado. Usa `status:` con nombre.
- En el build local, NU1900 (NuGetAudit sin red) deja los binarios viejos. Si el build falla, no corras las pruebas con `--no-build`.

## T39 — Consultar el estado de mis productos (09-Nov-2026, SCRUM-25, HU-08)

**Qué se hizo**
- `GET /products/mine?tab=active|sold|soldOut` con cursor y los conteos de las tres pestañas (`MyProductPage`); sin `tab` responde lo de antes.
- `POST /products/{id}/sold` ("Marcar como vendido"): estado `sold` y `soldAt`. `ProductResponse` agrega `soldAt`.
- `db/016-publicacion-vendida.js` (esquema 16; era `013`, pero `013`, `014` y `015` los tomaron favoritos, pedidos y el acuerdo con IA): repite el validador de `products` con `sold` y `soldAt`.
- Errores nuevos: `listings.product_sold` (409) y `listings.invalid_tab` (400).
- API `0.5.0-alpha.17`, con main del 11-Nov-2026 fusionado (HU-14, HU-53, HU-23 y HU-32/33/34). Unit 502 · Integration 156 · Security 182 · Contract 17 · endpoints 71.
- Revisión del PR #61: `favorites.json` con `soldAt` (favoritos devuelve un `ProductResponse`), y "Mis productos" pide una publicación de más para no dar un `nextCursor` que lleve a una página vacía.

**Decisiones (con su porqué)** (de la usuaria, 09-Nov-2026)
- **Manda la maqueta sobre Jira:** pestañas Activos (disponibles y pausadas), Vendidos y Agotados. "Reservada" llega con HU-52/HU-23.
- **"Marcar como vendido" es un estado propio (`sold`):** sin él, vendido y agotado serían lo mismo y la pestaña Vendidos quedaría vacía hasta los pedidos.
- **El contador +/− y "Reponer" se quedan en HU-50.**
- **Una vendida no se edita, no se pausa ni se reactiva (409), pero sí se elimina:** reactivarla la devolvería a la vitrina.
- **Una sola ruta con `?tab=` y los conteos en cada respuesta:** la app no hace llamadas de más, y la versión publicada sigue funcionando sin `tab`.

**Errores a evitar**
- **Un estado nuevo cambia más de lo que parece:** se revisaron cada `Status ==` y cada filtro (vitrina, detalle, chat, ofertas, editar y pausar). Sin excluir `sold` en el filtro de pausar, reactivar "des-vendía".
- **El script de HU-08 (hoy `db/015`) se probó con un `mongod` temporal** (puerto 27099, datos en el scratchpad) porque Docker estaba apagado: se aplicó dos veces y aceptó `sold` con `soldAt`, y rechazó un estado desconocido y el precio 0. La batería del repositorio contra Mongo queda para el job `database`.
- **Números compartidos (revisión del PR #61):** entre la revisión y la fusión entró otra HU (pedidos) que tomó el número que proponía el revisor (`db/014`, `alpha.15`). Antes de renumerar, revisa el `main` de ese momento, no el de la revisión.

**Pendientes con dueño**
- `NOTE (HU-23)`: vender sola al completar pedidos y llegar a 0 piezas (RN-05). RN-07 (no eliminar con pedidos abiertos) ya lo resolvió HU-23 (`listings.product_has_orders`).
- `NOTE (HU-52)`: estado "Reservada".
- `NOTE (HU-25)`: en Vendidos, el comprador y el monto real del pedido.
- Se cierra `NOTE (HU-08)` de HU-06 (paginar "Mis productos"): ya pagina con cursor.

## T40 — Avisos push (11-Nov-2026, SCRUM-49, HU-31)

**Qué se hizo**
- Módulo nuevo `Modules/Notifications/` (M-09) con 5 rutas: `GET /notifications`, `POST /notifications/read`, `POST /notifications/{id}/read`, `PUT /notifications/devices` y `POST /notifications/devices/remove`.
- `INotificationCenter` para los demás módulos: con pestaña guarda el aviso (60 días, `dedupeKey`), y siempre encola el push. Lo llaman el chat (mensaje y aviso de la IA), pedidos (pedido nuevo), el acuerdo (confirmado) y las ofertas (recibida, aceptada, no elegida).
- `FcmPushSender`: API HTTP v1 con un JWT RS256 firmado con la llave de la cuenta de servicio (sin SDK). `PushWorker` envía en segundo plano y borra los tokens desinstalados.
- `WantedReminderWorker`: cada hora, "vence mañana" y "venció" (nueva consulta `IWantedReminders`).
- `db/017-avisos.js` (esquema 17). API `0.5.0-alpha.18`. Unit 529 · Integration 158 · Security 192 · Contract 18. Endpoints 76.

**Decisiones (con su porqué)**
- **FCM directo** (usuaria). Sin el SDK de Firebase Admin: el JWT se firma con `System.Security.Cryptography` y las URLs de Google son fijas.
- **Los mensajes solo van por push:** su historial es la bandeja; el centro de avisos es para pedidos y solicitudes, como la maqueta.
- **Cola en memoria por instancia:** quien escribe no espera a FCM. Si la instancia se reinicia, se pierden los push en cola, pero el historial ya quedó.
- **Dos instancias revisan los vencimientos:** `ux_user_dedupe` deja pasar un solo aviso.
- **Los tipos y pestañas son contrato público** (`Musubi.Api.Modules.Notifications`), no de `Data`: los demás módulos no leen el `Data` de otro.

**Errores a evitar**
- **S5542 marca la firma RS256:** PKCS#1 v1.5 es lo que pide el estándar del JWT; se suprime con justificación en ese método.
- **Al agregar una dependencia a un servicio**, las pruebas que lo construyen a mano fallan al compilar: usa `RecordingNotificationCenter` (`UnitTest/Common`).

## T38 — Acuerdo con IA (10-Nov-2026, SCRUM-52/53/54, HU-32/33/34)

**Qué se hizo**
- Módulo nuevo `Modules/GenAi/` (M-10) con 5 rutas: `POST /agreements` ("Resumir acuerdo"), `GET /agreements?conversationId=`, `PUT /agreements/{id}`, `POST /agreements/{id}/confirm` y `DELETE /agreements/{id}`.
- `POST /orders` acepta `agreementId`: el pedido sale del acuerdo confirmado por los dos, con el precio acordado, y el acuerdo se borra.
- Chat: mensajes `kind: notice` ("Ana pidió un resumen con IA"), evento `agreementChanged` del hub y el contrato `IConversationNotices`.
- Proveedor detrás de `IAgreementExtractor`: Gemini por REST (`gemini-3.5-flash-lite`, salida JSON con esquema, temperatura 0), en memoria en Development y `Off`.
- `db/015-acuerdos.js` (esquema 15): `agreements` con TTL de 30 días y `messages.kind`.
- API `0.5.0-alpha.16`. Unit 496 · Integration 152 · Security 174 · Contract 17. Endpoints 70.

**Decisiones (con su porqué)**
- **Decisiones de la usuaria (10-Nov-2026):** Gemini en la capa gratuita (registrado contra L-15c en la bitácora de lineamientos); aviso la primera vez y línea en el chat (RN-40); del chat no se guarda nada y el acuerdo se borra al crear el pedido o a los 30 días (RN-44).
- **Lo que sale a Gemini:** los últimos 60 textos de esa conversación, con "Comprador"/"Vendedor" en lugar de los nombres y sin correos, teléfonos, cuentas, enlaces ni usuarios. La capa gratuita permite a Google usarlo para mejorar sus productos: por eso el filtro y el aviso de privacidad (HU-47).
- **El aviso del chat sale antes de llamar a la IA:** si el texto salió, los dos lo saben aunque la IA falle. Sin llave no sale nada y no hay aviso.
- **`revision`:** confirmar o editar una versión vieja responde `agreements.changed`; así "los dos confirmaron" siempre es sobre lo mismo (RN-41).
- **Cada valor de la IA pasa las reglas del pedido** (cantidad, punto, ventana de fecha, precio) y la moderación; si no, queda "No identificado".
- **El nombre del día del texto para la IA se escribe a mano:** la imagen corre con `InvariantGlobalization`, así que `CultureInfo("es-MX")` no existe en producción.
- **Prueba real:** con un chat inventado, Gemini respondió en 1.2 s (234 tokens) y no inventó la fecha que solo se había preguntado.

**Errores a evitar**
- **Heredocs largos en Git Bash** con comillas o barras mezcladas fallaron dos veces ("unexpected EOF"): para archivos grandes usa la herramienta Write o un `.py` en el scratchpad.
- **Python en Windows escribe CRLF** con `open(..., 'w')`: pasa `newline='\n'` (git lo normaliza, pero los diffs se ensucian).
- **`StatusCode(204, valor)`** no es un 204 limpio: para DELETE usa `NoContent()`, como los demás controllers.
- **Un acuerdo con IA no es un pedido:** el número `MS-NNNN` solo se gasta al crear el pedido.

**Pendientes con dueño**
- `NOTE (HU-47)`: el aviso de privacidad dice qué sale a Gemini y cuánto se conserva.
- `NOTE (HU-31)`: push al otro participante cuando se pide, se confirma o se descarta el acuerdo.
- Desplegar (con permiso de la usuaria): `infra-up` aplica `db/014` y `db/015` y las instancias leen `Gemini__ApiKey`.

## T37 — Confirmar la compra (10-Nov-2026, SCRUM-42, HU-23)

**Qué se hizo**
- Módulo nuevo `Modules/Orders/` (M-08), con 5 rutas en `OrdersController`:
  - `GET /orders/meeting-points`;
  - `POST /orders`;
  - `GET /orders?conversationId=` y `GET /orders/{id}`;
  - `PUT /orders/{id}/payment`.
- `db/014-pedidos.js` (esquema 14):
  - `orders` con `ux_conversation_open` (único parcial);
  - `meetingPoints` con los 14 edificios de la UTTT y las carreras de cada uno;
  - `counters` para el número `MS-NNNN`.
- Contratos entre módulos: `IConversationDirectory.FindAsync`, `ProductSummary.Stock`, `IOfferDirectory` (nuevo) e `IProfileDirectory.FindCareerSlugAsync`.
- RN-07: `MyListingsService.DeleteAsync` responde `409 listings.product_has_orders` con pedidos abiertos, y antes revisa que la publicación sea de quien la elimina.
- HU-46: `OrderAccountEraser` cancela los pedidos abiertos y deja "Usuario eliminado".
- API `0.5.0-alpha.15`. Unit 443 · Integration 149 · Security 161 · Contract 16.

**Decisiones (con su porqué)**
- **Decisiones de la usuaria (10-Nov-2026):**
  - el pedido sale del chat, sin IA (el resumen se llena con el producto o la oferta aceptada);
  - el lugar es un edificio de la lista, más un detalle libre, con el de la carrera recomendado;
  - el pago es solo en efectivo al entregar.
- **Carreras → edificio:** la relación la propuse yo y la usuaria la aprobó (Energías Renovables en Química y Administración en Contaduría son supuestos). Los puntos comunes son Biblioteca, Cafetería, Idiomas, Multimedios, Rectoría, Villas y Vinculación.
- **El vendedor no crea el pedido (`403 orders.only_buyer`):** RN-16 dice que lo acepta, y eso llega en HU-24.
- **Crear no aparta piezas:** RF-34 las aparta al aceptar. RN-17 se valida contra lo disponible hoy; en una oferta directa, contra la cantidad que pidió la solicitud.
- **Uno abierto por conversación:** se revisa antes (para no gastar números) y lo asegura el índice parcial.
- **Ventana de entrega:** de 30 minutos a 14 días, entre las 7:00 y las 21:00 del campus (UTC-6).
- **`transferAvailable: false` en la respuesta:** la app muestra "Transferencia" deshabilitada, como en la maqueta, hasta HU-55.

**Errores a evitar**
- **`git checkout` no restaura un archivo nuevo** después de una mutación: el archivo no está en git. Respáldalo antes (`cp`) y compáralo con `cmp` al terminar.
- **S6964:** un `int` de un DTO de entrada debe ser `int?`, para que un cuerpo sin ese campo no llegue como 0.
- **CA2007 en los métodos auxiliares de las pruebas:** cada `await` lleva `ConfigureAwait(false)` (en los `[Fact]` no hace falta).

**Pendientes con dueño**
- `NOTE (HU-24)`: aceptar, rechazar, completar y cancelar.
- `NOTE (HU-25)`: "Mis pedidos".
- `NOTE (HU-34)`: el pedido sale del acuerdo.
- `NOTE (HU-55)`: transferencia y código de entrega.
- `NOTE (HU-44)`: gestionar los puntos de entrega.
- `NOTE (HU-31)`: avisar a quien vende.

## T35 — Editar, cerrar o cancelar mi solicitud (09-Nov-2026, SCRUM-36, HU-14)

**Qué se hizo**
- `PUT /api/v1/wanted/{id}`, `POST /api/v1/wanted/{id}/close` y `POST /api/v1/wanted/{id}/cancel` en `WantedController`.
- `WantedService`: `UpdateAsync`, `CloseAsync` y `CancelAsync`. La validación de publicar pasó a `ValidateAsync` y la moderación a `ModerateAsync`, que comparten publicar y editar.
- `IWantedRepository`: `UpdateDetailsAsync` y `SetStatusAsync`, en Mongo y en memoria. Sin script en `db/`: `db/009` ya admitía `closed` y `cancelled`.
- API `0.5.0-alpha.13`. Unit 377 · Integration 139 · Security 140 · Contract 14.

**Decisiones (con su porqué)**
- **Con ofertas, el producto y la categoría no cambian** (`409 wanted.has_offers`): quien ofreció respondió a ese producto. El resto sí cambia.
- **El vencimiento se recalcula, pero los 30 días cuentan desde que se publicó** (RN-10): editar no alarga una solicitud sin fecha.
- **Cerrar y cancelar son idempotentes; una vencida ya no cambia** (`409 wanted.not_active`): un doble toque no da error, y una vencida ya salió del tablero.
- **Las ofertas pendientes no se tocan al cerrar:** HU-16 y HU-17 ya revisan que la solicitud esté activa. Avisar a quien ofreció es HU-30.
- **Dueño, estado y vencimiento en el filtro de cada escritura** (OWASP API1). Si cambia el producto, el filtro pide además `offersCount = 0`: una oferta que llega a la mitad gana.

**Errores a evitar**
- **"todo" en un comentario dispara S1135** aunque no sea un TODO ("el filtro lleva todo eso"): escribe otra palabra.
- **La mutación debe compilar:** quitar un parámetro deja CA1801/IDE0060 y `--no-build` corre los binarios viejos. La que sí mordió: `!string.IsNullOrEmpty(requesterId)` en lugar del dueño en el filtro en memoria (3 Unit y 2 Security en rojo).

**Pendientes con dueño**
- `NOTE (HU-25)`: proponer cerrar la solicitud al completarse su pedido (RN-15).
- `NOTE (HU-30)`: avisar a quienes ofrecieron que la solicitud se cerró o se canceló.

## T34 — Editar o eliminar mis publicaciones (05-Nov-2026, SCRUM-24, HU-07)

**Qué se hizo**
- `MyProductsController` con 4 rutas: `PUT /products/{id}`, `POST /products/{id}/pause`, `POST /products/{id}/resume` y `DELETE /products/{id}`.
- `MyListingsService`, y `ProductRules`, que reúne las reglas que comparten publicar y editar.
- `IProductRepository`: `UpdateDetailsAsync`, `SetPausedAsync` y `DeleteAsync`, en Mongo y en memoria.
- Reutiliza `listings.product_not_found` (HU-11). Sin script en `db/`: el esquema ya aceptaba `hidden`.
- API `0.5.0-alpha.12`. Conteos al final de la fusión con main (ver el PR).

**Decisiones (con su porqué)** (aprobadas por el usuario)
- **`PUT` con los campos editables completos:** la app ya los tiene de "Mis productos", y no hay que adivinar qué significa un `null`.
- **Al eliminar no se borra la foto que copió una oferta vinculada (HU-16)** (usuario, 09-Nov-2026): Listings pregunta con `IListingPhotoReferences` y las ofertas responden (índice `ix_seller`, sin script).
- **Eliminar es borrado real, con las fotos de S3 (una por llave):** la maqueta dice "no se puede deshacer". El chat ya muestra `available: false` y el pedido guardará su copia (HU-23).
- **Las fotos y las piezas no se editan:** la maqueta no muestra `Vender-1`, y "Reponer" es de HU-50. Así la edición no pisa el descuento atómico de un pedido (R7).
- **Pausar entra en la API aunque la maqueta no lo tenga:** `hidden` ya existía; la app decide si lo muestra.
- **El dueño va en el filtro de cada escritura** (`_id` + `sellerId` + no quitada) y otra persona recibe 404 (OWASP API1).
- **Solo se modera el texto que cambió:** el texto que no cambió ya se moderó al publicar.

**Errores a evitar**
- **Una mutación que no compila "pasa":** `x.Length >= 0` (siempre verdadero), un método que deja de usar la instancia (CA1822/S2325) y un `using` sin usar (IDE0005) rompen el build, y `--no-build` corre los binarios viejos. Las mutaciones que sí mordieron: `!string.IsNullOrEmpty(sellerId)` en el filtro del dueño (4 Security y 5 Unit en rojo) y otra etiqueta en `EvictByTagAsync` (3 Integration en rojo).
- **`ProductDocument` es inmutable (`init`):** el repositorio en memoria reemplaza el documento, como Mongo con `ReturnDocument.After`.
- **Reactivar sin leer antes:** update con pipeline (`$cond` sobre `$stock`) para decidir entre `active` y `soldOut` en la misma operación.
- **En un producto nuevo, `conditionDetail` se quita con `$unset`:** el `$jsonSchema` no acepta `null`.
- **TestServer no aplica `[RequestSizeLimit]`:** lee el cuerpo completo (no tiene `IHttpMaxRequestBodySizeFeature`), y la prueba llegó hasta la moderación. El tope se verifica por reflexión; en la nube lo aplica Kestrel.
- **`docs/endpoints.md` decía "28 filas" y había 33:** al sumar filas, recalcula el total con `check_endpoints`.

**Pendientes con dueño**
- `NOTE (HU-18)`: volver a buscar coincidencias al editar el nombre, la categoría, el precio o la descripción (RF-08).
- `NOTE (HU-23)`: no eliminar con pedidos pendientes o confirmados (RN-07) → `409 listings.product_has_open_orders`.
- `NOTE (02-05)`: fotos de una publicación eliminada que quedan en S3 si el borrado falla a medias. La regla de ciclo de vida del bucket (pendiente desde HU-06 para las fotos que nunca se publican) debe cubrir también este caso.
## T36 — Favoritos (09-Nov-2026, SCRUM-68, HU-53)

**Qué se hizo**
- API `0.5.0-alpha.14` (era `alpha.13`; HU-14 la tomó primero en main), esquema 13: `GET /api/v1/favorites`, `PUT /favorites/{productId}` y `DELETE /favorites/{productId}`; solo los favoritos del token.
- `favorites` (`db/013-favoritos.js`) con `ux_user_product` e `ix_user_id`; `FavoriteService`, `FavoritesController`, DTOs y repositorios Mongo/InMemory.
- Tres estados: `available`, `soldOut` y `unavailable`; `unavailable` devuelve `product: null`.
- HU-57: un bloqueo en cualquier dirección impide guardar y marca el favorito como `unavailable`.
- HU-46: `ListingAccountEraser` borra los favoritos de la cuenta; los de otras personas quedan `unavailable` si el producto desaparece.
- Enfoque B: sin `isFavorite` en productos; la app arma el corazón con `GET /favorites`.
- Unit 383 · Integration 138 · Security 129 · Contract 13 · Database 97; `critic` PASS, endpoints OK (57 rutas) y shared OK (5 bloques).
- Docs: `endpoints.md` (57 filas), `errores.md`, `modelo-datos.md` (`favorites` ✅), `versionado.md` y `AGENTS.md`.
- Sección compartida `docs/versionado.md`: las filas `0.5.0-alpha.12` (HU-07) y `0.5.0-alpha.14` (HU-53; `alpha.13` es HU-14) se replicaron en `musubi-app` y se registraron en las dos `Memoria.md`.

**Decisiones (con su porqué)**
- **Tope 200 sin paginar:** mismo patrón que "Usuarios bloqueados"; `NOTE (HU-53)` y se pagina si alguien lo supera.
- **Se reutiliza `listings.product_not_found`:** guardar una publicación no visible es el mismo 404; no se agregan códigos `favorites.*`.
- **DELETE con id mal formado responde 204:** el filtro de Mongo no puede intentar serializar un ObjectId inválido; el repositorio lo ignora.
- **Consulta en lote:** `FindByIdsAsync` y `HiddenForAsync` evitan N+1 al listar.
- **Sin copia de datos:** título, precio y foto se leen de `products`; `favorites` solo guarda ids y fecha.

**Errores a evitar**
- **`[BsonRepresentation(ObjectId)]` en un filtro con id mal formado:** un DELETE con `abc` lanzaría 500; se valida con `ObjectId.TryParse` antes de consultar.
- **No crear `Modules/Marketplace/`:** M-04 reutiliza `Modules/Listings/`.
- **No agregar `isFavorite` a los DTOs de productos:** rompe el enfoque B y la caché de la vitrina.
- **Los helpers de prueba que devuelven `Task` fallan CA1859:** declarar `Task<bool>` si el método devuelve el resultado de un repo.
- **`ListingAccountEraser` cambió de constructor:** DI resuelve `IFavoriteRepository`; no registrar un segundo eraser.
- **DatabaseTest necesita Docker corriendo:** el daemon se detuvo entre corridas; se inició Docker Desktop y las 97 pruebas pasaron en ~1.5 min.

## T35 — Specs de favoritos (09-Nov-2026, SCRUM-68, HU-53)

**Qué se hizo**
- `specs/m04-marketplace/HU-53-favoritos/` queda detallada: `spec.md`, `plan.md` y `task.md`.
- Contrato: `GET /favorites`, `PUT /favorites/{productId}` y `DELETE /favorites/{productId}`; solo el usuario del token.
- Módulo real: `Modules/Listings/`, no `Modules/Marketplace/` (`docs/modulos.md`).
- Decisión de Jira aplicada: sin modo sin conexión.
- Enfoque B: la app arma el corazón con `GET /favorites`; no cambian `ProductResponse` ni `ProductDetailResponse`.
- Colección `favorites` (`db/013-favoritos.js`, esquema 13) con `ux_user_product` e `ix_user_id`.
- Se incorporan HU-57 (bloqueos) y HU-46 (borrado de cuenta).

**Decisiones (con su porqué)**
- **Sin `isFavorite` en productos:** la vitrina sigue pública y cacheable; no se agrega una consulta de favoritos a cada página. La app carga la lista una vez por sesión.
- **Tres estados en la lista:** `available`, `soldOut` y `unavailable`; `unavailable` no expone el producto (inexistente, `hidden`, `removed`, otro campus o bloqueo).
- **Tope 200 sin paginar:** mismo patrón que "Usuarios bloqueados"; `NOTE (HU-53)` y se pagina si alguien lo supera.
- **Se reutiliza `listings.product_not_found`:** no se agregan códigos `favorites.*` por un 404 que significa lo mismo.
- **Favoritos huérfanos:** si el producto se elimina, el favorito queda como `unavailable`; no se borra en cascada.

**Errores a evitar**
- **No crear `Modules/Marketplace/`:** M-04 reutiliza `Modules/Listings/`; duplicaría la vitrina.
- **No agregar `isFavorite` a los DTOs de productos:** rompe el enfoque B y obliga a consultar favoritos en cada página.
- **No leer `favorites` desde otro módulo:** vive en `Listings`; si otro módulo lo necesita, se expone un contrato como `IBlockDirectory`.
- **No olvidar HU-46:** `ListingAccountEraser` debe borrar los favoritos del usuario.

## T33 — Ver y elegir ofertas (09-Nov-2026, SCRUM-39, HU-17)

**Qué se hizo**
- `GET /wanted/{requestId}/offers` (solo quien publicó; pendientes y aceptadas, sin bloqueados) con nombre, foto, carrera visible y reputación de quien ofrece.
- `POST .../offers/{offerId}/reject`, `accept` (abre la conversación) y `chat` (la abre sin decidir).
- M-07:
  - `IConversationOpener` y `context.type` `offer`;
  - quien busca es el comprador;
  - el contexto copia nombre y foto, y `ChatService` no busca publicación para él.
- Una oferta aceptada o rechazada ya no se edita (`409 offers.not_editable`). `db/012-ofertas-chat.js` (esquema 12).
- API `0.5.0-alpha.11`. Unit 350 · Integration 129 · Security 106 · Contract 13; critic PASS y endpoints OK (50 filas).

**Decisiones (con su porqué)**
- **Aceptar abre el chat y el pedido es HU-23:** así lo muestra la maqueta ("Abriremos el chat para que acuerden la entrega").
- **Decisión atómica desde `sent`:** evita aceptar y rechazar la misma oferta a la vez.
- **"Deshacer" del rechazo en la app:** la API rechaza de forma definitiva; la app espera a que termine el aviso.
- **Orden en la app:** pocas ofertas por solicitud y los tres órdenes de la maqueta usan datos que la app ya tiene.

**Errores a evitar**
- **Al ampliar un validador de Mongo, se repite el validador completo:** `collMod` reemplaza el anterior; `db/012` copia `db/006` (conversaciones) y `db/011` (ofertas) con los cambios.
- **Un helper `async` de las pruebas también necesita `ConfigureAwait(false)`:** CA2007 rompe el build aunque no sea un `[Fact]`.

## T32 — "Yo tengo este producto" (09-Nov-2026, SCRUM-38, HU-16)

**Qué se hizo**
- `GET` y `PUT /api/v1/wanted/{requestId}/offers/mine`: vincular una publicación propia disponible (copia nombre, precio, estado y portada) u ofrecer sin publicar (descripción, precio, estado y foto opcional de `POST /products/photos`).
- Reglas:
  - una por persona y solicitud, editable (RN-14): la primera da 201 y suma `offersCount`, las siguientes la reemplazan (200);
  - nunca a la propia (`409 offers.own_request`, RN-06);
  - solicitud no visible → `404 wanted.not_found`;
  - `overBudget` si el precio pasa el presupuesto (se permite);
  - descripción y mensaje moderados.
- `offers` (`db/011-ofertas.js`, esquema 11) con `ux_request_seller`; `IListingPhotos` y `ProductSummary.Available`/`PhotoKey` en M-03; borrado de cuenta de las ofertas hechas y recibidas.
- API `0.5.0-alpha.10`. Unit 342 · Integration 127 · Security 102 · Contract 12; critic PASS y endpoints OK (46 filas).

**Decisiones (con su porqué)**
- **`PUT` que crea o reemplaza:** RN-14 deja una oferta editable por persona; la app no necesita distinguir "nueva" de "editar" y el índice único resuelve dos envíos a la vez (el perdedor queda como edición).
- **Sin conversación al enviar:** `Yo-tengo-enviada` dice "te escribirá por el chat"; quien busca la abre desde la oferta en HU-17.
- **Foto por la subida de productos:** reutiliza moderación, WebP y el prefijo por usuario; la oferta solo valida que la llave sea suya.
- **Aviso RF-40 en HU-30:** sin notificaciones push todavía.

**Errores a evitar**
- **`ProductSummary` creció con parámetros opcionales:** `Available` y `PhotoKey` tienen valor por defecto para no romper los fakes de `IProductDirectory` en las pruebas del chat y de reputación.
- **S3358 también salta con un ternario dentro del `new` de otro ternario:** en `ProductDirectory.FindAsync` se usa `if` y una variable.

## T31 — Tablero "Se busca" (09-Nov-2026, SCRUM-37, HU-15)

**Qué se hizo**
- `GET /api/v1/wanted` público con sesión opcional (`Se-busca` y `Se-busca-visitante`): solo activas y sin vencer del campus, de la más nueva a la más vieja, con `category` repetido, `minBudgetCents`/`maxBudgetCents`, `neededWithinDays`, `q`, `cursor` y `limit`.
- Con sesión: el campus del token, sin las propias (RN-06) ni las de personas bloqueadas (HU-57), y `requester` con nombre completo, foto y carrera visible. Visitante: nombre corto, sin id, foto ni carrera (RNF-05).
- Lecturas en lote nuevas: `IUserRepository.FindManyAsync` + `IMemberDirectory.FindMembersAsync` (nombres) e `IProfileDirectory.FindCardsAsync` (foto y carrera visible).
- Caché de salida `wanted-board` (30 s) para el visitante, que se borra al publicar una solicitud y al eliminar una cuenta; `ETag` en todas.
- Errores `wanted.invalid_budget_range`, `wanted.invalid_deadline`, `wanted.invalid_query` e `wanted.invalid_cursor`; `listings.unknown_campus` reutilizado.
- API `0.5.0-alpha.9`, esquema 10 sin cambios. Unit 317 · Integration 121 · Security 99 · Contract 11; critic PASS y endpoints OK (44 filas; `AGENTS.md` decía 36 desde hace varias HU).
- La columna Sprint de `docs/trazabilidad.md` pasa a S4 en las 13 HU del Sprint 4 (Jira, 09-Nov-2026).

**Decisiones (con su porqué)**
- **Nombre completo y carrera solo con sesión:** así lo muestran las maquetas (`Se-busca` frente a `Se-busca-visitante`) y es lo que ya da `GET /profiles/{id}` dentro del campus. La decisión de HU-13 ("el tablero no lee otras colecciones") se cambió por fidelidad: se leen en lote, dos consultas por página.
- **Sin las propias:** RN-06 no deja ofertar a una solicitud propia y ya están en "Mis solicitudes".
- **Presupuesto con mínimo y máximo:** los rangos de la maqueta los arma la app; la API no los fija.
- **Búsqueda sin `searchWords`:** el tablero de un campus es chico (RN-12) y ya va acotado por `ix_community_status_id`; así no hubo script ni relleno. Si crece, se hace como `db/007-busqueda.js`.
- **Patrón con clases de vocales acentuadas y separadores explícitos:** `\b` y `\p{L}` dependen de UTF/UCP en PCRE; la consulta solo deja letras y dígitos (hasta 5 términos).

**Errores a evitar**
- **`ProfileDirectory` ahora pide `ICareerRepository`:** las pruebas que lo construyen a mano pasan `new InMemoryCareerRepository()`.
- **`WantedRequesterResponse.Id` es anulable:** en `mine` y `{id}` siempre trae el id; solo el visitante del tablero lo recibe en `null`.
- **`ShortName` usa la inicial del último apellido:** "Fernanda Ortiz Salinas" → "Fernanda S."; las pruebas del visitante deben usar nombres de dos palabras si esperan "Fernanda O.".

## T30 — Filtros de la búsqueda por categoría, precio, condición y orden (08-Nov-2026, SCRUM-27, HU-10)

**Qué se hizo**
- `GET /api/v1/products` acepta `category` repetido (OR), `minPriceCents`/`maxPriceCents` inclusivos, `condition` (`new`/`used`) y `sort` (`relevance`, `newest`, `oldest`, `priceAsc`, `priceDesc`). Se combinan con `q`, `campus` y `hideSoldOut`.
- Validación de borde: `400 listings.invalid_price_range`, `listings.invalid_condition` (reutilizado), `listings.invalid_sort` e `listings.invalid_page`.
- `nextCursor` pasa a token con el orden (`newest:<id>`, `priceAsc:<priceCents>:<id>`); el ObjectId crudo anterior solo se acepta para `newest`/`relevance`.
- `db/010-filtros-precio.js` crea cuatro índices: `ix_community_status_price_id`, `ix_community_status_price_desc_id`, `ix_community_category_status_price_id` e `ix_community_category_status_price_desc_id`; `MongoSchema.ExpectedVersion = 10`.
- Se quitó la opción `"i"` del regex de `searchWords` (las palabras ya están normalizadas) y el fake en memoria usa `StartsWith` ordinal; el `security-reviewer` lo señaló como aviso de rendimiento/fidelidad.
- `page` (base 1, máximo 50) suma paginación por número para la app: `skip = (page - 1) * limit` en `long`, ignora `cursor` y devuelve `nextCursor: null`; fuera de 1 a 50 es `listings.invalid_page`. El tope acota el costo de `skip` (OWASP API4).
- `hideSoldOut` pasa a opcional: sin el parámetro la búsqueda oculta los agotados (HU-09) y la vitrina los muestra; `false` explícito los incluye aunque haya `q`.
- API `0.5.0-alpha.8`, esquema 10 y fila en `docs/versionado.md`. Se renumeró de `db/009`/`alpha.7` a `db/010`/`alpha.8` porque HU-13 (musubi-api#54) se fusionó antes con ese número (09-Nov-2026). Unit 269 · Integration 107 · Security 92 · Contract 9 · Performance 4; critic PASS y endpoints OK. `DatabaseTest` sin Docker en local: lo corre el CI.
- **Prueba manual (API en Development + InMemory, 08-Nov-2026):** `/health` y `/health/ready` Healthy; registro y verificación con el código del log; 4 publicaciones; filtro combinado devolvió las 2 esperadas; `priceDesc` ordenó 80k, 30k, 20k y 10k; paginación `priceAsc` 2+2 con cursor; rango/condición/orden inválidos y cursor viejo con precio devolvieron 400 con su código.
- **Code review (08-Nov-2026):** se agregó el caso de cursor `priceDesc` que cruza el empate, 19 pruebas del codec (legacy, orden ajeno, precio fuera de rango, id inválido, mayúsculas canonicalizadas), aserciones de la dirección de los cuatro índices y el valor extraño de categoría en seguridad. Unit pasó a 269 al rebasar sobre HU-28/HU-57, con 19 casos del codec, el cursor `priceDesc` y el tope de página.

**Decisiones (con su porqué)**
- **Alcance de la maqueta, no solo Jira:** el usuario eligió `Busqueda-filtros` completa (varias categorías, condición y orden); Jira solo pedía categoría y precio.
- **Extender `GET /products`:** la app ya consume esa ruta y comparte auth, caché, `ETag` y cursor; una ruta nueva duplicaría controller y pruebas.
- **Cursor compuesto:** ordenar por precio con un cursor solo de `_id` salta o repite resultados; el token lleva el orden y la clave.
- **`page` y `cursor` son excluyentes:** la app usa `page` para la barra numérica y el scroll continuo usa `cursor`; si llegan ambos, gana `page` (`NOTE (HU-10)`).
- **`relevance` es alias de `newest`:** no hay puntaje de relevancia; HU-09 lo difirió. Se conserva la etiqueta de la maqueta con un límite documentado.
- **Índices de precio en `db/010`:** sin ellos `priceAsc`/`priceDesc` ordenan en memoria. Hacen falta las dos direcciones porque el desempate es `_id` descendente en ambos; un solo índice asc no cubre `priceDesc`. El script solo crea índices, no reescribe documentos.
- **Reutilizar `listings.invalid_condition`:** el valor inválido es el mismo al publicar y al filtrar; no se duplica el código.
- **`category` repetido en lugar de coma:** una sola categoría sigue funcionando y no hay formato que parsear.
- **Sin tope propio de categorías:** el límite es la línea de petición; si se vuelve abuso, se agrega tope y error (`NOTE (HU-10)`).
- **Orden por precio con `q`:** el índice de prefijo resuelve la búsqueda, pero el orden puede resolverse en memoria sobre las coincidencias; `limit <= 50` acota el costo (`NOTE (HU-10)`).
- **Plan con `$in` de estado:** con agotados incluidos el plan puede usar `SORT_MERGE`; los índices cubren el orden y confirmar el plan con `explain` en CI queda pendiente (`NOTE (HU-10)`).

**Errores a evitar**
- **El cursor viejo no sirve con otro orden:** un ObjectId crudo solo vale para `newest`/`relevance`; con `priceAsc` se rechaza con `400`.
- **`ProductQuery` ya no tiene `CategorySlug` ni `BeforeId`:** las llamadas nuevas usan `CategorySlugs` y `After`; no reintroducir los campos viejos.
- **El orden en Mongo y en memoria debe coincidir:** en empate por precio, `_id` descendente en ambos; si se cambia uno, la batería compartida falla.
- **`ProductQuery.Sort` por defecto es `newest`:** no cambiar el orden de la vitrina sin actualizar `docs/endpoints.md` y la app.
- **El precio está en centavos:** la maqueta usa pesos; la app convierte antes de llamar y omite `maxPriceCents` cuando no hay techo.
- **S3358 rompe el build:** no anidar ternarios en `nextCursor`; usar un `if`.
## T29 — Publicar lo que necesito (09-Nov-2026, SCRUM-35, HU-13)

**Qué se hizo**
- Módulo nuevo `Modules/PurchaseRequests` (M-05):
  - `POST /wanted`, `GET /wanted/mine` y `GET /wanted/{id}`;
  - `wantedRequests` (`db/009-solicitudes.js`, esquema 9);
  - `ICategoryDirectory` (M-03) para validar la categoría.
- Reglas:
  - RN-09: producto, categoría, presupuesto y cantidad son obligatorios;
  - RN-10: vence al terminar `neededBy` en el campus (UTC-6) o a los 30 días; el vencimiento se calcula al leer;
  - RN-12: máximo 5 activas;
  - el texto se modera (02-07);
  - otro campus, vencida o con bloqueo responde 404 para quien no es el dueño.
- API `0.5.0-alpha.7`. Unit 274 · Integration 110 · Security 95 · Contract 10.

**Decisiones (con su porqué)**
- **Los valores [POR DEFINIR] de la spec:** 5 activas (RN-12) y 30 días sin fecha (RN-10). Son conservadores para el piloto; se ajustan con datos.
- **"Vencida" no se guarda:** se calcula con `expiresAt`. No hace falta un proceso en segundo plano y el conteo de RN-12 la excluye solo.
- **El día del campus es UTC-6 fijo:** México quitó el horario de verano en 2022. No depende de `tzdata` en la imagen chiseled.
- **"Otro" como categoría:** la maqueta lo ofrece en `Necesidad-1` aunque no es una categoría de publicaciones.

**Errores a evitar**
- **`SchemaVersionTests` exige scripts consecutivos:** un `010` sin `009` rompe la prueba. Si dos ramas crean el mismo número, la que se fusione después renumera.

## T28 — Reportar y bloquear (08-Nov-2026, SCRUM-57 y SCRUM-95, HU-28 y HU-57)

**Qué se hizo**
- Módulo nuevo `Modules/Reputation` (M-13):
  - `POST /reports` (motivos de la maqueta; `general`, `user` con conversación opcional, `product`). Avisa al equipo por correo si existe `Reports:NotifyEmail`. Rate limit `reports` de 10/hora;
  - `GET`/`POST /blocks` y `DELETE /blocks/{userId}`.
- Un bloqueo en cualquier dirección:
  - corta el chat (`403 chat.blocked`, `canReply: false`);
  - oculta las publicaciones (vitrina y búsqueda con `ProductQuery.ExcludeSellers`, detalle 404) y el perfil público (404).
- La foto de perfil de la otra persona en el chat (`counterpart.photo`), por lote con `IProfileDirectory`. La pidió el usuario: solo se veían iniciales.
- Contratos entre módulos: `IBlockDirectory` (M-13), `IProfileDirectory` (M-02) e `IConversationDirectory` (M-07).
- `db/008-reportes-bloqueos.js`, esquema 8, API `0.5.0-alpha.6`. Unit 244 · Integration 103 · Security 91 · Contract 9.

**Decisiones (con su porqué)**
- **HU-28 adelantada y HU-57 nueva:** Google Play exige reportar y bloquear en apps con contenido de usuarios antes de producción.
- **El detalle del reporte no se modera:** se rechazaría el reporte que cita el insulto; no se publica.
- **Bloqueo en las dos direcciones y `canReply` sin decir quién bloqueó:** evita volver a tener contacto por otra publicación y no expone a quien bloqueó.
- **Sin caché del bloqueo:** lista corta con dos índices; desbloquear aplica al instante en las dos instancias.

**Errores a evitar**
- **`[IO.File]::ReadAllText` con ruta relativa usa el directorio del proceso, no el de PowerShell:** un script de edición falló y `WriteAllText` dejó tres `.cs` vacíos en la raíz de `Musubi.Api`. `check_endpoints` revienta con un `.cs` vacío (`Get-Content -Raw` devuelve `$null`). Usa rutas absolutas (`"$PWD\..."`).
- **El parámetro de la ruta debe llamarse igual en el código y en `docs/endpoints.md`** (`{userId}`): `check_endpoints` compara el texto.

## T27 — Búsqueda de productos por palabra clave (07-Nov-2026, SCRUM-26, HU-09)

**Qué se hizo**
- `GET /api/v1/products?q=` (API): coincidencia por **prefijo de palabra** en `title` y `description`, sin distinguir mayúsculas ni acentos, solo disponibles, de la más nueva a la más vieja, con el cursor `_id` de la vitrina. `calc` y `calcula` encuentran `Calculadora`.
- Error nuevo `listings.invalid_query` (400): más de 80 caracteres o sin letras ni dígitos. `q` vacío equivale a no buscar.
- Campo nuevo `searchWords[]` (palabras normalizadas de título y descripción) e índice `ix_community_status_search`; `db/007-busqueda.js` y esquema 7, con relleno de lo existente.
- API `0.5.0-alpha.5` (subida al integrar `main`, que ya desplegó `0.5.0-alpha.4`). Unit 181 · Integration 100 · Security 84 · Contract 8 · Performance 4; critic PASS y endpoints OK. `DatabaseTest` sin Docker en local: lo corre el CI.

**Decisiones (con su porqué)**
- **Prefijo en lugar de palabra completa:** el requisito pide encontrar `Calculadora` escribiendo `calc`; `$text` no soporta prefijos y Atlas Search no existe en MongoDB Community, así que se compara por prefijo.
- **Campo `searchWords` indexado:** un `$regex` sobre `title`/`description` recorría toda la comunidad; con el campo multikey y el índice `ix_community_status_search` (comunidad, estado, palabras) el `^prefijo` es un rango indexado. El costo es el campo derivado y su relleno.
- **Acentos, mayúsculas y plural resueltos al escribir:** `SearchWord.Normalize`/`Words` dejan las palabras en minúsculas, sin acentos y sin `s`/`es` final, tanto en el documento como en la consulta; no hay variantes en el índice.
- **El campo se calcula en `ProductDocument`:** el serializador lo escribe al insertar, así no hay servicio ni *backfill* dentro de la API; el script rellena solo lo anterior.
- **Solo disponibles cuando hay `q`, orden por `_id`:** cumple el criterio de Jira y reutiliza el cursor de la vitrina; la relevancia se difiere.
- **La exclusión de agotados vive en el repositorio, no en el servicio:** cualquier consulta con `Search` devuelve solo `active`, aunque el llamador pida los agotados.
- **Título y descripción, sin categoría ni vendedor:** cubre "nombre o palabra clave" y evita mezclar búsqueda con filtros (HU-10).
- **`SearchTerms` deja solo letras, dígitos y espacios:** nada del usuario se vuelve sintaxis del patrón (OWASP API3).
- **Sin persistir `q`:** RNF-07; el historial y las búsquedas guardadas son de HU-12. El texto sí vive hasta 30 s como clave de la caché de salida, que no es historial; queda escrito en los límites de la spec.
- **`searchWords` se calcula al insertar:** si algún día se edita el título o la descripción hay que recalcularlo; hoy no hay edición (`NOTE (HU-09)`).
- **Orden por `_id`:** el índice resuelve el prefijo, pero el orden de las coincidencias se hace en memoria; aceptable con `limit ≤ 50` (`NOTE (HU-09)`).
- **`q` se acota a 5 términos:** el `$regex` se evalúa por término en dos campos y `CountAsync` repite el filtro; con 80 caracteres se llegaban a 40 términos por petición. El tope mantiene el costo acotado sin afectar el uso real (`security-reviewer`, API4).
- **Rendimiento medido contra el *fake* en memoria:** el p95 de `PerformanceTest` no cubre el `$regex` de MongoDB; el RNF-09 real se confirma con `PERF_BASE_URL` contra el despliegue.
- **M-04 reutiliza `Modules/Listings/`:** la vitrina, el repositorio y los DTO ya viven ahí; un módulo paralelo duplicaría el endpoint y las pruebas. Se corrigió la fila de `docs/modulos.md`.

**Errores a evitar**
- **`$text` no hace prefijos:** si alguien vuelve a "simplificar" con `$text`, `calc` deja de encontrar `Calculadora`; la decisión y su motivo están en `plan.md`.
- **El prefijo necesita `^` y las palabras normalizadas:** un regex sin `^` no aprovecha el índice; y si el documento guarda `Calculadoras` con mayúscula o acento, el prefijo normalizado no coincide.
- **`SearchWord.Normalize`/`Words` es compartida:** si cambia el plural o los acentos, deben cambiar juntos el API, el script `db/007` (que rellena) y la batería de `TestShared`.
- **CA1865 rompe el build:** `EndsWith("s", StringComparison.Ordinal)` debe ser `EndsWith('s')`.

## T26 — Moderación de texto con léxico local (08-Nov-2026, SCRUM-92, 02-07)

**Qué se hizo**
- En producción, el chat y las publicaciones respondían `503 moderation.unavailable`. El log decía `AmazonTranslateException: The AWS Access Key Id needs a subscription for the service`, y Comprehend daba lo mismo.
- `SpanishLexicon` modera el texto sin costo. `Moderation:TextProvider` vale `Lexicon` por defecto; con `Aws`, el texto pasa por el léxico y después por Translate + Comprehend. `Moderation:ExtraBlockedTerms` suma términos sin desplegar.
- El moderador en memoria también aplica el léxico.
- `HttpRequestException` también da `503`.
- API `0.5.0-alpha.4`. Unit 210 · Integration 96 · Security 83 · Contract 8.

**Decisiones (con su porqué)**
- **Léxico local en lugar del plan de pago de AWS:** lo eligió el usuario por el lineamiento de costo cero. Pasar al plan de pago no se puede deshacer y cobra al acabarse el crédito.
- **Palabras completas y normalización:** las subcadenas rechazan "computadora" y "disputa". Se normaliza lo que se usa para esquivar el filtro (repetidas, leet, letras sueltas) y se conserva la ñ ("cono" ≠ "coño").
- **Fuera las palabras con usos inocentes comunes** ("perra", "zorra", "pinche", "coger", la raíz "verg" por "vergüenza"): un falso positivo bloquea una venta real.

**Errores a evitar**
- **La cuenta de AWS está en el plan gratuito (FREE, crédito hasta el 26-Abr-2027).** Ese plan no incluye todos los servicios: antes de agregar uno, pruébalo con el CLI (`SubscriptionRequiredException`). Rekognition, S3, SES y CloudWatch sí están incluidos.
- **El SDK v4 de AWS no envuelve los errores de red:** llegan como `HttpRequestException`, que no es `AmazonClientException`. Atrápala junto con las de AWS.

## T25 — Detalle de un producto (06-Nov-2026, SCRUM-28, HU-11)

**Qué se hizo**
- `GET /api/v1/products/{id}`:
  - es público, y el vendedor `{ id, name }` solo aparece con sesión;
  - la publicación pausada solo la ve su dueño;
  - la quitada, o la de otro campus con sesión, responde `404 listings.product_not_found`.
- API `0.5.0-alpha.3`. Unit 174 · Integration 96 · Security 83 · Contract 8.

**Decisiones (con su porqué)**
- **El vendedor solo con `{ id, name }`:** su tarjeta completa sale de `GET /profiles/{id}`, y así M-03 no lee perfiles. "Quién te ve" (HU-04) se respeta en un solo lugar.

**Errores a evitar**
- **En un controller con la propiedad `userId`, una variable local `userId` rompe el build** (Sonar S1117): usa otro nombre (`viewer`).

## T24 — Bandeja de conversaciones (06-Nov-2026, SCRUM-31, HU-22)

**Qué se hizo**
- `GET /conversations`: Todos, Compras y Ventas, ordenados por actividad, con cursor y no leídos.
- `POST /conversations/{id}/read`.
- `db/006-chat-bandeja.js` (esquema 6): no leídos por lado y copia de los nombres.
- API `0.5.0-alpha.2`. Unit 174 · Integration 93 · Security 82 · Contract 8.

**Decisiones (con su porqué)**
- **Solo conversaciones con mensajes en la bandeja:** tocar "Mensaje" sin escribir no llena la bandeja del vendedor.
- **No leídos por lado (`buyerUnread` / `sellerUnread`) con `$inc`:** atómico entre las 2 instancias y validable con `$jsonSchema`.
- **Copia de los nombres (R8):** la bandeja no consulta `users` por fila.

**Errores a evitar**
- **Redis en producción (06-Nov-2026):** quedaron tres fallas encadenadas, todas resueltas.
  - En Parameter Store iba el comando `redis-cli` en vez de la cadena, y la contraseña terminó en los logs.
  - El grupo de seguridad no dejaba salir al puerto de Redis Cloud.
  - La contraseña guardada no era la vigente: `NOAUTH`, que se diagnosticó reproduciendo la conexión con la misma librería.
- **Para diagnosticar Redis sin entrar a las instancias:** un programa local con StackExchange.Redis y la cadena de Parameter Store, con la contraseña tapada en la salida. El log de la conexión dice exactamente en qué paso falla.
- **El plan gratuito de Redis Cloud no tiene TLS:** la caché y los mensajes del backplane viajan sin cifrar entre AWS y Redis Cloud (ambos en us-east-1), protegidos por la contraseña. Riesgo aceptado para el piloto.

## T23 — Iniciar una conversación con el vendedor (04-Nov-2026, SCRUM-29, HU-20)

**Qué se hizo**
- Módulo `Chat` (M-07):
  - 4 rutas de `/conversations`;
  - hub `/hubs/chat` (evento `messageCreated`);
  - `conversations` y `messages` (`db/005-chat.js`, esquema 5).
- `Platform/Realtime`: SignalR con backplane de Redis cuando existe `Cache:ConnectionString`.
- M-03 expone `IProductDirectory`, para que el chat no lea `products`.
- `check_endpoints` ahora también compara los hubs (`| WS | /ruta |`).
- API `0.5.0-alpha.1`. Unit 172 · Integration 87 · Security 80 · Contract 8.

**Decisiones (con su porqué)**
- **SignalR + Redis** (decisión del usuario): sin backplane, un mensaje que entra por una instancia no llega a quien está conectado a la otra.
- **Solo WebSockets y sin negociar:** así no hacen falta sesiones pegajosas en el ALB.
- **El token va en `?access_token=` solo en `/hubs`:** el WebSocket del celular no lleva encabezados. En REST la query se ignora.
- **Se envía por REST y se entrega por el hub:** validación, moderación y rate limit en un solo lugar.
- **Si Redis falla al entregar, el mensaje ya está guardado:** se registra un aviso y la app lo ve al recargar.

**Errores a evitar**
- **`dotnet add package` dice "no es compatible con los marcos de trabajo"** cuando una dependencia transitiva no está en el source mapping de `nuget.config`. Con SignalR+Redis faltaba `MessagePack`. No es un problema del TFM.
- **Sonar S1135 lee "Todo" en español como un TODO:** en un comentario que empieza con "Todo con sesión", escribe "Siempre con sesión".
- **`IHubContext<T>.Clients` y `IHubClients.Clients(...)` chocan en un fake** que implementa los dos: implementa los miembros de `IHubContext` de forma explícita.
- **Un `Id` en un subdocumento se guarda como `_id`** (convención de id del driver de C#). La validación de `db/005-chat.js` pedía `context.id` y rechazó la conversación, pero solo en `DatabaseTest` (CI): los repositorios en memoria no serializan. Se resolvió con `[BsonNoId]` + `[BsonElement("id")]` y con `MongoConventionsTests.Conversation_KeepsContextIdAsId_AndParticipantsAsObjectIds`, que no necesita Docker.
- **El grupo de seguridad de la API solo dejaba salir 443, 27017 y 80:** Redis Cloud (puerto 15092) respondía `UnableToConnect (0-read, 0-sent)`. Se agregó la salida `api_redis` con la variable `redis_port` (infra `0.3.5`).
- **En Parameter Store va la cadena de StackExchange.Redis, no el comando `redis-cli -u redis://default:<clave>@host:puerto`** que muestra Redis Cloud. Con el comando, la API lo toma como nombre de host y **la contraseña termina en los logs** dentro del mensaje de error. Hubo que rotarla (06-Nov-2026).
- **La prueba del hub tiene que morder:** se comprobó que falla (timeout) si cambia el nombre del evento.

## T22 — Mutación y rendimiento (03-Nov-2026, SCRUM-94, 02-09)

**Qué se hizo**
- `PerformanceTest`: p95 de `ping`, categorías y vitrina con 400 peticiones (20 a la vez); job `performance`.
- `MutationTest`: Stryker.NET 5 sobre `Passwords.cs` y `EmailAddresses.cs`, con puntaje de 87.5 %; workflow `mutation.yml`.
- Con eso, la solución completa los 8 proyectos de la plantilla `WebAPIDevSecOps`.

**Mediciones (03-Nov-2026)**
- **En proceso:** p95 de 1.1 ms (ping), 2.7 ms (categorías) y 4.6 ms (vitrina).
- **Contra AWS** (`0.4.0-alpha.1`, sin caché): p95 de 127 ms (ping), 895 ms (categorías) y 994 ms (vitrina); p50 de la vitrina, 908 ms.
- **Contra AWS con caché** (`0.4.0-alpha.4`, 2 corridas desde la laptop de desarrollo): p95 de 216–251 ms (ping), 143–178 ms (categorías) y 158–164 ms (vitrina); p50 de ~100–113 ms en las tres. La vitrina bajó de 994 a ~160 ms de p95. El ping sin caché queda más alto que antes por la variación de la red doméstica.

**Errores a evitar**
- **Stryker no funciona con xUnit v3:** no soporta MTP (#3094) y activa los mutantes dentro del proceso de VSTest, pero xUnit v3 corre las pruebas en otro proceso. El síntoma: los mutantes "sobreviven" y el puntaje sale en 0 %.
- **Un mutante en el inicializador de un campo** solo se activa si la prueba crea la instancia, no con una estática.
- **`dotnet test` con `global.json` en MTP no corre proyectos VSTest:** `MutationTest` se corre con `dotnet stryker`.
- **No escribas un SHA de una action de memoria:** cópialo de un workflow verificado o de `git ls-remote`.

## T21 — Observabilidad (03-Nov-2026, SCRUM-93, 02-06)

**Qué se hizo**
- Logs JSON fuera de Development con `TraceId`, y una línea por petición (método, ruta, código y duración) sin query, encabezados ni cuerpos, y sin `/health*`.
- `observability.tf` (infra `0.3.4`):
  - SNS con correo a `alert_email`;
  - filtro `Musubi/ApiErrors`;
  - 4 alarmas;
  - dashboard `musubi-api`.
- Trivy `0.76.1` (por digest) en `docker-build`. API `0.4.0-alpha.4`.

**Decisiones (con su porqué)**
- **`Musubi` en `Information` en producción:** antes el nivel era `Warning` y los eventos del negocio (cuenta eliminada, moderación) no llegaban a CloudWatch.
- **Sin query en el log de peticiones:** puede llevar un correo (RNF-05).

**Errores a evitar**
- **`terraform validate` en local** usa el `.terraform` ya inicializado contra S3: valida en una copia limpia con `../VERSION`, como el job del CI.
- **La suscripción de SNS por correo queda pendiente** hasta que alguien confirme el correo de AWS.

## T20 — Moderación antes de guardar (03-Nov-2026, SCRUM-92, 02-07)

**Qué se hizo**
- `Platform/Moderation`: `IContentModerator` con dos adaptadores:
  - AWS: Rekognition para las fotos y Translate + Comprehend para el texto;
  - en memoria: el marcador `[[inapropiado]]` y las fotos magenta.
- Se modera antes de guardar en tres lugares:
  - fotos de productos;
  - foto de perfil;
  - título y descripción al publicar.
- Statement `Moderar` en el rol de las instancias (infra `0.3.3`). API `0.4.0-alpha.3`. Unit 151 · Integration 70.

**Decisiones (con su porqué)**
- **Translate antes de Comprehend:** la toxicidad de Comprehend solo acepta inglés (documentación de AWS, 03-Nov-2026).
- **Copia JPEG para Rekognition:** no acepta WebP; la copia no se guarda.
- **Si AWS falla, 503 y nada se guarda:** nunca se publica sin moderar, sin la complejidad de una cola de pendientes (eso llega con HU-39).

**Errores a evitar**
- **Un marcador de imagen en bytes crudos nunca llega al moderador:** `PhotoProcessor` rechaza antes lo que no es imagen. La regla de prueba tiene que sobrevivir al procesamiento (color magenta).

## T19 — Caché en 3 niveles (03-Nov-2026, SCRUM-91, 02-02)

**Qué se hizo**
- `Platform/Caching`:
  - `AppCache` (cache-aside sobre `IDistributedCache`, Redis o memoria, con degradación);
  - caché de salida (`catalog` y `showcase`);
  - `[ConditionalGet]` (`ETag` y 304).
- `CachedCategoryRepository` y `CachedCareerRepository`. Publicar o eliminar una cuenta borra la vitrina de la caché (`EvictByTagAsync`).
- API `0.4.0-alpha.2`. Unit 139 · Integration 70 · Security 69.

**Decisiones (con su porqué)**
- **Redis opcional (Redis Cloud gratuito):** sin la cadena en Parameter Store, cada instancia usa su memoria y la API funciona igual. Con ella, la caché de datos es compartida (requisito "Redis distribuido").
- **La caché de salida solo es para anónimos y dura poco (30 s en la vitrina):** es por instancia; una publicación nueva se ve al instante en la instancia que la recibió y en 30 s en la otra.
- **`no-cache` + `ETag`:** el cliente revalida siempre, así que no hay datos viejos, y un 304 no manda cuerpo.

**Errores a evitar**
- **Una prueba de mordida con un build que falla** corre contra binarios viejos y "pasa": verifica que el build compile antes de leer el resultado.
- **`StackExchange.Redis` 3.x depende de `RESPite`:** el mapeo de `nuget.config` necesita los dos patrones (`Pipelines.Sockets.Unofficial` ya no).
- **El constructor `RedisConnectionException(tipo, mensaje)` es obsoleto (CS0618):** en las pruebas usa `RedisException`.

## T18 — Gobernanza: ruleset, MCP y spec de CI/CD (03-Nov-2026, SCRUM-90)

**Qué se hizo**
- Ruleset "Main Proteccion" en `main` de api, app y landing:
  - no borrar la rama ni hacer force push;
  - PR con 1 aprobación;
  - checks obligatorios;
  - revisión de Copilot.
- Spec `02-08-ci-cd`, que junta el CI (`01-05`) y el CD (`02-05`).
- `docs/mcp.md` con los MCP que usa el equipo (Atlassian y AWS) y su configuración en Claude Code y OpenCode.

**Decisiones (con su porqué)**
- **El administrador puede saltar la aprobación solo desde un PR** (`bypass_mode: pull_request`): hoy no hay un revisor fijo y sin esto nada se podría fusionar. El CI sigue siendo obligatorio.
- **El ruleset convive con la protección clásica**, que ya exigía los checks.

**Errores a evitar**
- **`infra-up` ya aplica `db/*.js`:** no hace falta correr los scripts a mano antes de desplegar. Son idempotentes, así que correrlos dos veces no daña.
- **Documentar un MCP con su configuración real:** el de AWS es el proxy remoto (`mcp-proxy-for-aws`), no el servidor local de AWS Labs.

## T17 — HU-04 Editar mi perfil: API (03-Nov-2026, SCRUM-22)

**Qué se hizo**
- Módulo `Modules/Profiles/` (M-02): `ProfileService`, `ProfilePhotoService`, `ProfilesController` (`/profiles/me`, `/me/visibility`, `/me/photo`, `/{id}`), `CareersController`, `ProfilesErrors` y `ProfileAccountEraser` (HU-46).
- `POST /auth/password/change` en `PasswordChangeController`: pide la actual, revoca todas las sesiones y devuelve una nueva.
- `db/004-perfiles.js`: `profiles`, `careers` (38 de la UTTT), `communities.academicTerm` (cuatrimestre, 11) y el `$jsonSchema` de `communities` con ese campo.
- `IMemberDirectory.FindAccountAsync` (cuenta y campus para M-02). `PhotoProcessor.TryProcessSquare` (512 px), `IMediaStorage.DeleteAsync` y `PhotoUploadService.ReadAsync` (compartido entre las dos fotos).
- `s3:ListBucket` también en `media/avatars/*` (infra `0.3.2`).
- Build 0/0 · Unit 134 · Integration 66 · Security 68 · Contract 7 · endpoints OK (28) · critic PASS · Semgrep 0.

**Decisiones (con su porqué)**
- **Perfil con upsert atómico (`FindOneAndUpdate` + `$setOnInsert`):** el primer cambio crea el documento sin leerlo antes, y dos escrituras a la vez no se pisan los valores por defecto.
- **Un campo vacío se quita con `$unset`:** el `$jsonSchema` no acepta `null` (y la convención `IgnoreIfNull` ya los omite al insertar).
- **El perfil público responde 404 igual para inexistente, inactiva y de otro campus** (OWASP API3): no deja saber si la cuenta existe.
- **Controlador aparte para cambiar la contraseña:** Sonar S6960 marcó `PasswordController` (anónimo) con dos responsabilidades.
- **Sin cuenta activa, las rutas del perfil responden 403 `auth.forbidden`**, igual que publicar (HU-06).

**Errores a evitar**
- **B2 del critic también revisa el nombre del record:** `ChangePasswordRequest(` necesita su waiver en esa línea.
- **`FindOneAndUpdateAsync<TProjection>` no acepta lambda como filtro:** usa `Builders<T>.Filter.Eq`.
- **La fila de `docs/versionado.md` es sección compartida:** agrégala también en `musubi-app` (va en el PR de HU-04 de la app).

## T11 — HU-04 Editar mi perfil: spec (27-Oct-2026, SCRUM-22)

**Qué se hizo**
- Se concilió Jira con la maqueta: `Perfil`, `Ajustes` ("Datos personales", "Correo institucional", "Cambiar contraseña") y `Privacidad` ("Quién te ve"). Los "campos visibles [POR DEFINIR]" de Jira quedaron resueltos por la maqueta.
- Spec con 8 rutas nuevas (7 de M-02 y `POST /auth/password/change`), las colecciones `profiles` y `careers` y `communities.academicTerm`. El diseño de `profiles` y `careers` ya está en `docs/modelo-datos.md` (📐).

**Decisiones (con su porqué)**
- **Cuatrimestre configurable por campus** (usuario): la UTTT cursa 11 (TSU del 1 al 6, ingeniería y licenciatura del 7 al 11) y otra universidad podría usar semestres (RNF-21).
- **Carrera de una lista del campus** (usuario): datos uniformes, y el periodo se valida contra la carrera. Semilla propuesta desde el sitio oficial de la UTTT (38 carreras), **a validar con el equipo**.
- **Alcance completo de la maqueta más la foto de perfil** (usuario). La foto reutiliza `PhotoProcessor` (cuadrada, 512 px, sin metadatos) y borra la anterior de S3.
- **La visibilidad se aplica en la API**, no solo en la app (OWASP API3). El perfil público solo existe dentro del campus y responde 404 fuera de él.
- **`auth.wrong_password` responde 400 y no 401**, porque la app trata un 401 como sesión vencida.

## T16 — HU-46 Eliminar mi cuenta y mis datos (30-Oct-2026, SCRUM-32)

**Qué se hizo**
- `POST /api/v1/account/delete` (`AccountController`, con sesión y rate limit `auth`) pide la contraseña actual. Hace borrado real de cuenta, sesiones, códigos, publicaciones y fotos (S3 por prefijo).
- `IAccountDataEraser`: cada módulo borra sus propios datos. Listings es el primero (`ListingAccountEraser`).
- Repositorios: `DeleteAllForUserAsync` en códigos y sesiones, y `DeleteBySellerAsync`. `IMediaStorage.DeletePrefixAsync` en S3 y en memoria.
- `iam.tf`: `s3:ListBucket` en `media/products/*` (infra `0.3.1`). API `0.3.0-alpha.4`.
- Pruebas: Unit 112 · Integration 57 · Security 52 · Contract 6 · endpoints 20.

**Decisiones (con su porqué)**
- **Pedir la contraseña** antes de borrar: con una sesión abierta en un teléfono ajeno no se puede borrar la cuenta.
- **El usuario se borra al final:** sin transacciones, así un fallo a medias permite reintentar.
- **`POST .../delete` y no `DELETE` con cuerpo:** algunos proxies descartan el cuerpo de un `DELETE`.
- **`auth.wrong_password` responde 400:** la app trata un 401 como sesión vencida.

**Errores a evitar**
- **La palabra "todo" en un comentario de C#** ("Borra todo lo del usuario") rompió el build por S1135. Usa "los datos", "cada", etc.
- **Sin `s3:ListBucket`**, S3 responde 403 (no 404) a una llave que no existe, y no se puede listar un prefijo.

## T15 — Cuenta de revisión de Google Play (29-Oct-2026, SCRUM-88)

**Qué se hizo**
- `db/003-dominio-revision.js` agrega `musubiapp.com.mx` a los dominios de la UTTT (esquema 3; `MongoSchema.ExpectedVersion = 3`). La semilla en memoria hace lo mismo.
- API `0.3.0-alpha.3`. Prueba nueva: `Campus_TeamReviewDomain_BelongsToUttt`; `DatabaseTest` revisa el dominio y la idempotencia.

**Decisiones (con su porqué)**
- **Un dominio del equipo dentro de la UTTT, no un campus aparte:** el revisor de Google ve el catálogo real del campus, y nadie de fuera puede registrarse. Solo existen las direcciones que se crean en Cloudflare Email Routing (catch-all apagado), y el registro exige el código enviado a esa dirección.
- **HU-04 usará `db/004-perfiles.js`**, no `003` como decía su spec: hay que ajustarla al detallarla.

## T14 — Specs en borrador de todo el backlog (29-Oct-2026, SCRUM-86)

**Qué se hizo**
- 47 HU pendientes con spec ligera (🚧 Borrador) en los dos repos: 94 carpetas en `specs/mNN-*/HU-NN-*/` con `spec.md`, `plan.md` y `task.md`.
- Las generó un script con datos de cuatro fuentes:
  - Jira: historia, criterios, RF/RNF y decisiones pendientes;
  - la maqueta: las pantallas de cada HU;
  - `docs/modelo-datos.md`: las colecciones;
  - `docs/modulos.md`: la carpeta de cada módulo.
- Specs técnicas en borrador:
  - musubi-api: 02-02 caché y `outbox`, 02-04 rendimiento HTTP, 02-06 observabilidad y 02-07 moderación;
  - musubi-app: 02-02 Google Play.
- `docs/trazabilidad.md` en "Spec en borrador", y la regla de `specs/README.md` (sección compartida) actualizada.

**Decisiones (con su porqué)**
- **Spec ligera para todo y detalle al iniciar cada HU:** el docente pide las specs desde el inicio. Detallar diseño y contratos semanas antes los desactualiza; así lo hacen Spec Kit y Kiro (constitución al inicio, specs por funcionalidad).
- **HU-43 sin spec** (fuera de alcance, D-01). **HU-42** con spec, marcada como posterior al MVP. **HU-48** en la API se cubre con HU-06 (vitrina pública).

**Errores a evitar**
- Una spec ligera **no** es una spec aprobada. Antes de programar una HU, completa su `plan.md` y resuelve las decisiones pendientes de su sección Límites.

## T12 — Dominio propio musubiapp.com.mx (29-Oct-2026, SCRUM-84/85)

**Qué se hizo**
- **DNS en Cloudflare:**
  - `contacto@` con Email Routing;
  - SES verificado con DKIM, más MAIL FROM `mail.` y DMARC;
  - sitio `musubi-landing` en la raíz.
- **Remitente:** `/musubi/prod/Email/From` = `Musubi <no-reply@musubiapp.com.mx>`. Se aplica al reemplazar las instancias.
- **API:** certificado ACM para `api.musubiapp.com.mx` (pedido con la CLI). Terraform usa la variable `api_domain`, alias de CloudFront, `sni-only`, `TLSv1.2_2021` y la salida `cloudfront_domain`. Infra `0.3.0`.

**Decisiones (con su porqué)**
- **Certificado fuera de Terraform y buscado por nombre:** su validación es un CNAME en Cloudflare, que no maneja Terraform. Si el apply lo creara, se quedaría esperando la validación.
- **CNAME de `api` en modo DNS only:** CloudFront ya da TLS, caché e IPv6. Con el proxy de Cloudflare habría doble CDN y el SNI no coincidiría con el certificado.

**Errores a evitar**
- **Git Bash convierte `/musubi/prod/...` en una ruta de Windows** y la CLI de AWS responde `ParameterNotFound`. Usa `MSYS_NO_PATHCONV=1`.
- **`data.aws_acm_certificate` solo busca llaves RSA_2048 por defecto** (igual que `aws acm list-certificates`). Un certificado EC (`EC_prime256v1`) necesita `key_types`; sin eso falla con "empty result" (infra-up del 29-Oct).
- **La sesión de `aws login` dura poco:** un vigilante largo falla con `ExpiredTokenException`, que no es un error del recurso.
## T13 — Guía del equipo y agentes de OpenCode (27-Oct-2026, SCRUM-83)

**Qué se hizo**
- `docs/guia-equipo.md`, idéntica en los dos repos (sección compartida): accesos, herramientas, cómo lee el proyecto cada herramienta, el flujo de una HU en 9 pasos, prompts para copiar, recursos que chocan entre personas, cómo probar en el celular y reglas del CI.
- `.opencode/agents/` con `security-reviewer`, `traceability-clerk` y `slice-scaffolder`. Remiten a `.claude/agents/`, que sigue siendo la fuente única.

**Decisiones (con su porqué)**
- **Mismas reglas para Claude Code y OpenCode:** OpenCode lee `AGENTS.md` y las skills de `.claude/skills/` (verificado en su documentación), pero no `.claude/agents/`. Por eso los agentes tienen una versión corta en `.opencode/agents/`.
- **La solo lectura en OpenCode se impone por `permission`:** `edit: deny` y bash solo para `git diff`, `git log` y `git show`, con la última regla que coincide ganando. Los hooks de Claude Code no corren en OpenCode.
- **Una sesión de IA por repo:** abierta en la carpeta `musubi/`, ninguna de las dos herramientas carga el `AGENTS.md` de los repos.

**Errores a evitar**
- **Números compartidos entre ramas:** `db/NNN`, `ExpectedVersion`, versiones, "T" de esta memoria y totales de `endpoints.md`. Quien fusiona segundo rebasa y renumera (guía §5).

## T10 — HU-06 Publicar un producto y vitrina pública (27-Oct-2026, SCRUM-23)

**Qué se hizo**
- `Platform/Media/`: `IMediaStorage` (S3 o en memoria), `PhotoProcessor` (SkiaSharp: endereza por EXIF, ≤ 1600 px, WebP sin metadatos) y `MediaController` (solo Development).
- `Modules/Listings/`: publicar, vitrina pública con cursor y filtros, "mis productos", rango de precios y categorías. M-01 expone `IMemberDirectory`.
- `db/002-publicaciones.js`; `user-data` recibe `Media__*`; filas en `endpoints`, `errores`, `modelo-datos`, `versionado` y `01-04`.

**Decisiones (con su porqué)**
- **Excepción al stack (aprobada por el usuario):** SkiaSharp (MIT) y AWSSDK.S3. Sin reprocesar, la ubicación GPS de la cámara llegaba a S3 y las fotos verticales salían giradas. Ver el `plan.md` de HU-06.
- **Rutas de imagen relativas (`/media/...`):** CloudFront sirve la API y las imágenes en el mismo dominio, y en Development la misma ruta la sirve la API.
- **"Populares" queda fuera** hasta HU-11 (vistas), por decisión del usuario.

**Errores a evitar**
- `scripts/check_endpoints.ps1` lee **una ruta por archivo**: un segundo controller en el mismo archivo no se detecta. Un controller por archivo.
- `AuthApiFactory` emite el JWT con su reloj manual (21-oct), pero JwtBearer lo valida con la hora real: alinear el reloj antes de llamar endpoints con sesión.
- En `.tftpl`, editar con Python una línea que termina en `\` puede comerse el salto: revisar el `git diff`.
- `SKCodec.Create(SKData)` no tiene la sobrecarga con `out SKCodecResult`: usar la de `Stream`. `SKCanvas.DrawBitmap` está obsoleto (`DrawImage` con `SKSamplingOptions`).

## T9 — 02-05 Despliegue en AWS (25-Oct-2026, SCRUM-80)

**Qué se hizo**
- `scripts/bootstrap-state.ps1` creó `musubi-tfstate-<cuenta>` (versionado, cifrado, sin acceso público). `infra/bootstrap` (ECR, OIDC, rol, presupuesto) quedó con `plan` de 6 recursos.
- `infra/stack`: VPC con 2 subredes públicas, ALB (solo CloudFront + cabecera secreta), ASG de 2 `t3.micro`, CloudFront (HTTPS, SecurityHeadersPolicy), S3 de imágenes con OAC, IAM mínimo de las instancias y CloudWatch.
- Parámetros SSM `/musubi/prod/{ConnectionStrings/Mongo, Jwt/SigningKey, Email/From}`; identidad de SES pendiente de confirmar; entorno `produccion` de GitHub (solo `main`) con `AWS_DEPLOY_ROLE_ARN`.
- `CloudFrontForwardedHeaders` + `SecurityTest/Hosting/CloudFrontHeadersTests.cs` (11). Job `terraform` en el CI.

**Decisiones (con su porqué)**
- **Sin Identity Center ni Organizations:** una cuenta del plan gratuito que entra a Organizations pasa al plan de pago y pierde los créditos. Se usa un usuario IAM con MFA + `aws login`.
- **IP real de `CloudFront-Viewer-Address`:** el `X-Forwarded-For` del ALB termina en la IP de CloudFront, y sin esto el rate limit de `/auth` sería uno solo para todos.
- **Desplegado el 26-Oct-2026:** `api-v0.2.0-alpha.1` + `infra-v0.1.0`; 2 instancias sanas, HTTPS por CloudFront, el ALB directo y el HTTP plano rechazados.
- **Sin NAT, sin Secrets Manager, sin ECS:** costo (ver `infra/README.md`, ~50 USD/mes encendido).
- **Presupuesto sin créditos:** si los contara, el gasto neto sería 0 y las alertas nunca sonarían.

**Self-tests**
- Si el middleware no se registra → `BehindCloudFront_EachViewerHasItsOwnRateLimit` en rojo.

**Errores a evitar**
- En una plantilla de Terraform (`.tftpl`), un `${` en un comentario también se interpreta: escribir `$${` o no usarlo.
- El clasificador de permisos bloquea `terraform apply` desde el agente: lo autoriza el administrador.
- **Primer `infra-up` (26-Oct-2026):** el primer Auto Scaling Group de la cuenta crea `AWSServiceRoleForAutoScaling` y el primer intento falla por propagación (se resuelve solo; volver a correr `infra-up`).
- **`t3` en créditos `standard` arranca sin créditos** (10 % de CPU): el arranque pasaba de 5 minutos y el ALB reemplazaba la instancia. Se usa `unlimited` y 10 minutos de margen.
- **GitHub usa sujetos OIDC inmutables** en este repo (`repo:Owner@id/repo@id:...`). Un rol que confía en `repo:Owner/repo:...` falla con "Not authorized to perform sts:AssumeRoleWithWebIdentity". Ver el formato real con `gh api repos/<owner>/<repo>/actions/oidc/customization/sub`.
- Probar las actions por SHA del commit, no del tag anotado (`git ls-remote ... refs/tags/vX^{}`).

## T8 — HU-02 + HU-56 Sesión y recuperación de contraseña (21-Oct-2026, SCRUM-20/78)

**Qué se hizo**
- `SessionController` (`login`, `refresh`, `logout`) y `PasswordController` (`password/forgot`, `password/reset`); 5 filas nuevas en `docs/endpoints.md` (12 en total).
- `LoginService` (señuelo para correo inexistente, estado revelado solo con la contraseña correcta, rehash), `PasswordRecoveryService` (respuesta genérica, cambio de hash y revocación de todas las sesiones).
- Extraídos de `RegistrationService`: `SessionIssuer` (una sola forma de `SessionResponse`) y `VerificationCodeService` (códigos por propósito para HU-03 y HU-56).
- `IUserRepository.UpdatePasswordHashAsync` en Mongo e InMemory + su prueba en la batería de `TestShared/`.
- Pruebas: `IntegrationTest/Auth/SessionFlowTests.cs` (9), `PasswordRecoveryFlowTests.cs` (9), helper `AuthFlow.cs`, y el contrato de login/refresh contra `session.json`.

**Decisiones (con su porqué)**
- **Tres controllers en `/auth`**: S6960 (SonarAnalyzer) marca un controller con varias responsabilidades y el build es TWAE. `this.Respond(result, status)` pasó a `ApiProblemExtensions` para no duplicarlo.
- **Logout idempotente** (204 aunque el token no exista): la app limpia su sesión local de todos modos.
- **El pedido de recuperación nunca reporta la espera de 60 s**, a diferencia del reenvío de HU-03: allí la cuenta pendiente ya se conoce, aquí no se debe revelar.

**Self-tests**
- Sin `RevokeAllForUserAsync` en el reset → `ForgotThenReset_ChangesThePasswordAndClosesEverySession` en rojo.
- Revelar `email_not_verified` con la contraseña equivocada → `Login_PendingUser_IsAskedToVerifyOnlyWithTheRightPassword` en rojo.

**Errores a evitar**
- **PR apilados: fusionar en orden (padre → hijo).** El #7 (que ya incluía el #6) se fusionó antes que el #6; al fusionar después el #6, la resolución tomó su versión y `main` perdió parte de HU-02 en 12 archivos (build rojo). Se restauró con `fix/SCRUM-20-restaurar-hu02` (árbol idéntico al merge del #7). Después de fusionar un padre con squash, rebasar el hijo sobre `main` para que solo lleve su commit.
- Una mutación que deja un parámetro del constructor primario sin usar no compila: las pruebas corren sobre binarios viejos y salen verdes. Mutar con `_ = campo;` y revisar que el build diga 0 errores.
- `ArgumentNullException.ThrowIfNull` sobre un `struct` (`Result<T>`) es CA2264.
- La salida de `dotnet test` está en español: el resumen dice `error: N`, no `failed`.

## T7 — HU-01/03/47 Registro, verificación y consentimientos (21-Oct-2026, SCRUM-19/21/33)

**Qué se hizo**
- `AuthController` (`/api/v1/auth`): `campus`, `register`, `verification/resend` y `verification/confirm`; anónimo y con rate limit `auth`. Filas en `docs/endpoints.md` (7 rutas en total).
- `RegistrationService` con `Result<T>`: campus por dominio (solo comunidades activas), registro `pendingVerification` con consentimientos versionados (`Legal:*`), código de 6 dígitos (10 min, 5 intentos, reenvío cada 60 s) y confirmación que abre la sesión (access + refresh de 02-03).
- `EmailAddresses` (normalización), `VerificationCodes` (CSPRNG, hash ligado al usuario, comparación en tiempo constante) y `AuthEmails` (nombre codificado en el HTML).
- Pruebas: `IntegrationTest/Auth/` con `AuthApiFactory` (reloj y correo controlados vía `TestShared/`), `UnitTest/Auth/RegistrationTests.cs` y `ContractTest/AuthContractTests.cs` (fixtures `campus`, `register` y `session`).

**Decisiones (con su porqué)**
- **Sin sesión hasta verificar** (RN-01): el registro no da tokens; la confirmación sí.
- **El controller resuelve el servicio de `RequestServices`**: los servicios del módulo son `internal` y MVC exige un controller público (CS0051 con un constructor público).
- **Consentimientos `bool?` + `[Required]`** (S6964): omitirlos es un 400 de validación y no se toman como "falso".
- **El 409 de registro revela si un correo existe** (la UX de la maqueta lo pide); el reenvío y la confirmación no revelan nada.

**Self-tests**
- Sin el bloqueo de 5 intentos → `Confirm_WrongCode_FailsAndLocksAfterFiveAttempts` en rojo.

**Errores a evitar**
- En los tests de integración que pegan muchas veces a `/auth`, subir `RateLimiting:Auth:PermitLimit` en la factory: el limitador comparte la "IP" de TestServer entre pruebas.
- Una línea de documentación con la palabra de la contraseña en `*Dtos.cs` también necesita `critic-allow: input-credential (HU-NN)` (B2a revisa cada línea).

## T6 — 02-03 Contrato de errores, autenticación y correo (21-Oct-2026, SCRUM-77)

**Qué se hizo**
- Errores: `problem+json` con `code`, título en español y `traceId` en cualquier error (validación, 401/403/404/405/429 vía `UseStatusCodePages`, 500). Catálogo canónico `docs/errores.md`; los controllers usan `this.Problem(AuthErrors.X)`.
- Sesión: JWT HS256 de 15 min (`AccessTokenIssuer`, claims mínimos) + refresh opaco y rotativo de 60 días (`RefreshTokenService`: solo hash, consumo atómico, la reutilización revoca la familia, el logout revoca la familia del dispositivo).
- Política de respaldo (sesión obligatoria por defecto), rate limit `auth` por IP (10/min, 429 + `Retry-After`) y salud/ping/OpenAPI anónimos.
- Contraseñas: `PasswordHasher<T>` de Identity (PBKDF2-SHA512, 210 000 iteraciones) y la política de la maqueta.
- Correo: `IEmailSender` → `LogEmailSender` (solo Development) o `SesEmailSender` (AWSSDK.SimpleEmailV2 4.0.106.4) sobre `ISesClient`; `EmailOptionsValidator` impide Log fuera de Development.

**Decisiones (con su porqué)**
- **Refresh opaco en la base** (no JWT): permite logout real y detectar robo por reutilización.
- **HS256 compartida en SSM** (no RS256): solo esta API emite y valida.
- **Errores como valores** (`ApiError`), no excepciones: sin `try/catch` en los controllers y con el mismo comportamiento en todos los ambientes.
- **Ruta inexistente sin sesión → 401** (la política de respaldo cubre las peticiones sin endpoint): no se pueden enumerar rutas. Se ajustaron las pruebas de 01-02 y 01-03 que esperaban 404.
- **Fail-fast fuera de Development** sin `Jwt:SigningKey` y sin SES/`Email:From`. Las factories de SecurityTest pasan una llave de 64 bytes generada al vuelo.

**Self-tests**
- Aceptar HS512 + política de respaldo permisiva → 4 pruebas en rojo (JWT, respaldo, OpenAPI anónimo e integración).

**Errores a evitar**
- La palabra "todo" en comentarios ("todo endpoint", "todo error") vuelve a romper el build (S1135): escribir "cada".
- `AddRateLimiter` vive en `Microsoft.AspNetCore.Builder` (no en `Microsoft.Extensions.DependencyInjection`).
- HS512 con `JsonWebTokenHandler` exige una llave de 64 bytes o más: la llave de pruebas es de 64 para poder probar la confusión de algoritmos.
- Un restore en paralelo puede fallar con "No se puede crear un archivo que ya existe": reintentar con `--disable-parallel`.
- Una mutación con un `using` sobrante no compila (IDE0005) y deja binarios viejos: mutar sin cambiar las dependencias.

## T5 — 02-01 Modelo de datos MongoDB e identidad (21-Oct-2026, SCRUM-76)

**Qué se hizo**
- `docs/modelo-datos.md`: catálogo canónico con las reglas R1–R14 (escalabilidad) y las 27 colecciones de las 55 HU (📐 Diseño, salvo las de identidad).
- `db/001-identidad.js` (script de entrega, idempotente): `communities` (+ UTTT), `users`, `verificationCodes`, `refreshTokens` y `_meta`, con `$jsonSchema` estricto, índices con nombre (únicos y TTL) y `_meta.version = 1`.
- `Platform/Data/{MongoConventions,MongoSchema}.cs`; `MongoHealthCheck` lee `_meta`: la readiness da Unhealthy si la base tiene una versión menor que `ExpectedVersion`.
- `Modules/Auth/Data/`: documentos, contratos, `Mongo*` e `InMemory*`; `AddAuthModule()` elige la implementación de forma lazy.
- `TestShared/Auth/AuthRepositoryContract.cs` (13 pruebas) enlazado en UnitTest (fakes) y DatabaseTest (Mongo real). DatabaseTest ya no es un esqueleto: Testcontainers.MongoDb 4.16.0 y job `database` en el CI.

**Decisiones (con su porqué)**
- **El script es la fuente del esquema** (no la API al arrancar): dos instancias no compiten por crear índices, y el script es el entregable. La API solo verifica la versión.
- **Atomicidad de un documento** (`findOneAndUpdate` con condición) en lugar de transacciones: igual en Atlas M0 y con N instancias. Ejemplo: el refresh token se consume una sola vez aunque lleguen 10 peticiones a la vez (probado).
- **`users` separado de `profiles`**: las credenciales nunca viajan con los datos públicos (R10).
- **Estados como `string` en camelCase**, no enums de C#: coinciden con el `enum` del validador.
- **Contraseña según la maqueta:** al menos 8 caracteres, una letra y un número (la maqueta manda sobre D-02).

**Self-tests**
- Fake que permite consumir dos veces un refresh token → 2 pruebas de contrato en rojo.

**Errores a evitar**
- CA2007 aplica a los métodos auxiliares de las pruebas (los `[Fact]` están exentos): `ConfigureAwait(false)` fuera de los métodos de prueba.
- DatabaseTest no corre sin Docker: en esta máquina solo compila; la evidencia es el job `database` del CI.
- `mongod` 8.0 se niega a arrancar en kernels Linux >= 6.19 ("known incompatibility", SERVER-121912) y algunos runners `ubuntu-latest` ya los traen: el job `database` corre en `ubuntu-22.04` e imprime `uname -r`. No afecta a Atlas ni a WSL2. Revisarlo al subir la imagen de `mongo`.
- En `mongosh`, `db._meta` da `undefined` (los nombres con `_` no se resuelven como propiedad): usar `db.getCollection('_meta')`. Lo detectó el job `database` en su primera corrida; el script de entrega habría fallado en Atlas.

## T4 — Backlog alineado con la maqueta (21-Oct-2026, SCRUM-75)

- **D-01 decidido por el usuario:** el pago es solo el de la maqueta (transferencia a la CLABE del vendedor o efectivo con código de entrega, sin comisión). HU-43 (Mercado Pago, SCRUM-55) queda fuera de alcance (etiqueta `fuera-de-alcance` + comentario en Jira) y la reemplaza **HU-55** (SCRUM-72). Constitución §1 (compartida) y §6/§11, 01-04 (`Payments:*` en lugar de `MercadoPago:*`), AGENTS, skills y Semgrep actualizados.
- **HU nuevas de la maqueta** (Historias en SCRUM con épica y etiqueta `maqueta`, sin sprint): HU-48 visitante (SCRUM-73), HU-49 populares (SCRUM-69), HU-50 inventario (SCRUM-67), HU-51 comparar/Avísame (SCRUM-70), HU-52 apartados QR (SCRUM-74), HU-53 favoritos (SCRUM-68), HU-54 modo noche (SCRUM-71), HU-55 pago (SCRUM-72). `docs/trazabilidad.md` pasa a 55 HU.
- Dependabot: `mongo` solo recibe parches de la línea 8.0 (`ignore` de minor/major); se cerró el PR que subía a 8.3.11.
- **Error a evitar:** el MCP de Atlassian da timeout en `createJiraIssue`/`editJiraIssue` aunque la operación sí se aplique. Antes de reintentar, buscar con JQL (`key > SCRUM-NN`): un reintento ciego duplicó HU-48 (la copia SCRUM-74 se reutilizó para HU-52).

## T3 — Lineamientos de la materia y stack NoSQL (20-Oct-2026, SCRUM-66)

**Qué se hizo**
- Lineamientos del docente (PDF del 20-Oct-2026) transcritos en `docs/bitacora/lineamientos-materia.md` con el checklist L-01…L-19 (canónico; el front lo enlaza). La constitución §1 (compartida) dice que **mandan sobre la constitución** y fija la evaluación el **02-Dic-2026**.
- PostgreSQL/EF Core → **MongoDB** (lineamiento 14): `MongoDB.Driver` 3.12.0 (Apache-2.0, publicado el 17-Sep-2026); `Platform/Data/MongoServiceCollectionExtensions.cs` (cliente y base lazy, fail-fast sin `ConnectionStrings:Mongo`), `MongoHealthCheck` (`ping`, timeout de 5 s) e `InMemoryDatabaseHealthCheck` (con `UseInMemoryDatabase=true`). Se borró `AppDbContext`.
- Claves: `ConnectionStrings:Default` → `ConnectionStrings:Mongo` + `Mongo:DatabaseName` (default `musubi`). Compose local con `mongo:8.0.32` (`MONGO_PASSWORD` en `deploy/.env`).
- Source Mapping: se quitaron `Npgsql*`; entran `MongoDB.*`, `DnsClient`, `SharpCompress`, `Snappier`, `ZstdSharp.Port` (transitivos del driver, leídos de su nuspec). Verificado con caché vacía: 64 paquetes, sin NU1100 (20-Oct-2026).
- critic B2b, Semgrep (`musubi-connection-string-with-password`) y `AppSettingsTests` detectan URIs `mongodb(+srv)://usuario:clave@`.
- Matriz de versionamiento `docs/versionado.md` (sección compartida, idéntica en el front) + `<Version>0.1.0</Version>` en `Musubi.Api.csproj` + `UnitTest/Common/VersioningTests.cs` (la versión debe coincidir con la última fila de la matriz).
- Dependabot: ecosistema `docker-compose` para `/deploy`.
- Roadmap de la fase 02 rehecho en `specs/README.md`: 02-01 documentos e índices + `mongosh` · 02-02 caché y paginación · 02-03 errores + JWT/refresh/logout · 02-04 rate limit y compresión · 02-05 AWS con Terraform · 02-06 CloudWatch · 02-07 moderación (Rekognition/Comprehend).

**Decisiones (con su porqué)**
- **MongoDB Atlas M0** (decisión del usuario): NoSQL obligatorio, gratis y externo permitido. DynamoDB descartado (filtros combinados de HU-10 y script portable para la entrega).
- **`UseInMemoryDatabase` se conserva** como "sin MongoDB": `dotnet run` y las suites WAF no necesitan una base. Los repositorios de 02-01 tendrán un fake en memoria.
- **Health check por factory** (`HealthCheckRegistration` con `IServiceProvider`): la bandera se lee lazy (00-04, regla 3).
- **`mongo:8.0.32` y no 8.0.34**: misma línea que Atlas y respeta el `cooldown` de 7 días (8.0.34 salió el 17-Oct).
- **Matriz como sección compartida + prueba** en cada repo: el lineamiento 11 pide aplicarla en cada despliegue; la prueba evita que la matriz y el código se desalineen. La automatización del tag queda en `NOTE (02-05)` / `NOTE (musubi-app:02-02)`.

**Self-tests**
- `MongoHealthCheck` mutado para devolver Healthy al fallar → `Readiness_WithUnreachableMongo_IsUnhealthy` en rojo.
- URI `mongodb+srv://u:p@…` en `appsettings.Production.json` → `TrackedSettings_ContainNoSecrets` en rojo; en un appsettings de prueba → critic FAIL (B2) y Semgrep FAIL.
- Matriz con otra versión → `ApiVersion_MatchesLatestRowOfCompatibilityMatrix` en rojo.

**Errores a evitar**
- Una mutación que no compila (los analizadores la rechazan) deja binarios viejos: `--no-build` da un falso verde. Mutar de forma que compile.
- `HealthCheckResult.Healthy(desc, ex)` no existe (el 2.º parámetro es `data`).
- El restore con caché vacía puede fallar por timeouts de nuget.org (no es NU1100): reintentar antes de concluir.
- Heredocs muy largos en el Bash de la herramienta pueden fallar al parsear: escribir el script a un archivo del scratchpad.

## T2 — Publicación en GitHub (15-Oct-2026)

- Repos **públicos**: `https://github.com/Israel-Perez/musubi-api` (y `musubi-app`). En GitHub gratuito, público habilita la protección de rama, minutos de Actions ilimitados y secret scanning sin costo. La plantilla ya parte de que no hay nada secreto en el repo.
- **Sin nombres de personas en el repo público** (decisión del usuario): se quitaron del §1 de la constitución (sección compartida, los dos repos), del crédito del README y del prompt archivado. Los nombres viven en Jira y Confluence.
- **Commit inicial sin clave Jira** (decisión del usuario: la tarea de plantilla no tiene clave). Desde la primera HU, cada commit y PR empieza con `SCRUM-NN`.
- Protección de `main`: ver el README (sección "Protección de rama recomendada") y la configuración aplicada con `gh`.

## T1 — Plantilla inicial SDD + DevSecOps (15-Oct-2026)

**Qué se hizo**
- Solución `Musubi.slnx` con `Musubi.Api` (monolito modular: `Platform/` + `Modules/`) y 5 suites (`DatabaseTest` es un esqueleto).
- Pipeline 01-02 en extensiones (`AddMusubiPlatform`/`UseMusubiPipeline`); `/health`, `/health/ready` (check de `AppDbContext`) y `GET /api/v1/ping`.
- Gobernanza: constitución (§1–§5 compartidas con `musubi-app`), plantilla, specs 00-01…00-04 y 01-01…01-05 (spec/plan/task), `AGENTS.md`, `CLAUDE.md`, `docs/{endpoints,trazabilidad,modulos}.md`.
- Guardarraíles: `critic-guardrails.ps1` (B1–B4), `check_endpoints.ps1` (bidireccional), `check_shared_sections.ps1`, Semgrep (5 reglas), CI de 9 jobs, Dependabot (nuget/actions/docker con `cooldown`), plantilla de PR.
- Claude Code: 3 agentes (los 2 revisores con solo lectura forzada por `disallowedTools` + hook `PreToolUse` en PowerShell) y 10 skills portadas de `.opencode/`.
- Prompt original archivado en `docs/bitacora/prompt-plantilla-inicial.md`.

**Versiones medidas (15-Oct-2026)**
- .NET SDK 10.0.401 (local) · imágenes `sdk:10.0.402` / `aspnet:10.0.13` · `postgres:18.6-alpine` (la 19 sigue en beta).
- Asp.Versioning.Mvc/ApiExplorer 10.2.1 · Asp.Versioning.OpenApi 10.2.3 · Microsoft.AspNetCore.OpenApi, EF Core InMemory y HealthChecks.EFCore 10.0.13 · Npgsql.EFCore.PostgreSQL 10.0.3 · Scalar.AspNetCore 2.18.0 · SonarAnalyzer.CSharp 10.35.0.4138.
- Pruebas: xunit.v3 4.0.2 · coverlet.MTP 10.1.0 · Microsoft.AspNetCore.Mvc.Testing 10.0.13.
- CI: checkout v7.0.1, setup-dotnet v6.0.0, setup-python v7.0.0 (SHAs en `ci-pr.yml`) · Semgrep 1.181.0 · actionlint 1.7.12 (local) · pwsh 7.6.7 · Python 3.13.15.

**Decisiones (con su porqué)**
- **xUnit v3 + Microsoft.Testing.Platform** en lugar de xUnit 2.9 + VSTest: es la línea mantenida y el modo nativo de .NET 10; el comando cambia a `dotnet test --project`.
- **Asp.Versioning.OpenApi** en lugar de suprimir AV0029/AV0030 (la referencia los suprimía): un documento OpenAPI por versión sin deuda.
- **Source Mapping explícito** (9 patrones derivados de `project.assets.json`) en lugar de `*`; verificado con caché vacía (70 paquetes, sin NU1100).
- **CORS vacío por defecto**: la app Android no usa CORS; solo aplica a un panel web futuro.
- **`UseInMemoryDatabase=false` en la base y `true` en Development**: fail-fast si falta la BD en Production y `dotnet run` funciona sin PostgreSQL.
- **Waiver `critic-allow: input-credential (HU-NN)`** por línea: HU-01/HU-02 necesitan la contraseña en el request; la excepción queda visible (WARN) y trazable.
- **Guard de endpoints bidireccional**: la referencia solo detectaba rutas sin fila; ahora también filas huérfanas.
- **Job `docker-build`** en el CI: no hay Docker en la máquina de creación (pedido del usuario).
- **Solo lectura forzada** en `security-reviewer`/`traceability-clerk` (pedido del usuario): campos verificados en la documentación oficial de Claude Code (`disallowedTools`, `hooks` en el frontmatter, `shell: powershell`).
- No se instalaron Stryker, NBomber, Pact, RESTler ni chaos (quedan en `00-03` con su fase dueña). SonarCloud queda para `02-06` (requiere la organización y `SONAR_TOKEN`).

**Self-tests (los guardarraíles muerden)**
- Sin `UseHsts` → Security 10/11 (falla `Production_HttpsResponse_CarriesHstsHeader`).
- `AllowAnyOrigin()` → el build falla con S5122 (SonarAnalyzer).
- critic con un scratch (TODO + controller sin auth + `catch` + DTO con password + appsettings con password) → `FAIL (5 bloqueantes, 2 avisos)`, exit 1.
- endpoints con una ruta sin fila → FAIL; con una fila renombrada → FAIL (1 faltante + 1 huérfana).
- Semgrep con un scratch → 5/5 reglas; sobre el repo → 0. `--config=auto` detectó la falta de `cooldown` en Dependabot (corregido).
- Hook `readonly-git-guard.ps1` con 12 payloads → `git diff/log/show` permitidos; `push`, `rm`, `;`, `>`, `--output`, `-c`, `$(…)` y `Remove-Item` bloqueados (exit 2); `Read` ignorado.

**Errores a evitar**
- La palabra española "todo" en comentarios C# rompe el build (S1135).
- `dotnet test` posicional con xunit.v3 en .NET 10 → "Testing with VSTest target is no longer supported": usar `--project` con el runner MTP en `global.json`.
- En Git Bash, `/p:` se convierte en una ruta (MSB1008): usar `-p:`.
- Si la versión mutada no compila, `--no-build` prueba binarios viejos y el resultado es falso.
- `git fetch --depth` sobre un clon completo puede volverlo superficial: el critic hace `fetch` sin `--depth`.

**Confluence (15-Oct-2026)**
- Publicado con confirmación del usuario en el espacio `DDS` ("Desarrollo de software") de `uttt-startup-musubi.atlassian.net`: página padre "MUSUBI · Documentación técnica" (id 4947970) con las hijas "00 · Constitución y gobernanza SDD", "01 · Fundación musubi-api", "01 · Fundación musubi-app", "Catálogo de endpoints" y "Trazabilidad HU ↔ specs".
- Regla: las páginas resumen y **enlazan** al repo; nunca copian tablas vivas. Los enlaces a GitHub funcionan después del primer push. Al cerrar una fase o HU relevante, actualizar el resumen de la página correspondiente.

**Pendientes con dueño**
- `02-01` modelo de documentos, índices, script `mongosh`, readiness contra MongoDB real, Testcontainers (`NOTE (02-01)`).
- `HU-02` JWT + FallbackPolicy (`NOTE (HU-02)`). `HU-20` SignalR. `HU-31` FCM. `HU-32` IA. `HU-35` ML. `HU-43` Mercado Pago.
- `02-02` caché (Redis/Valkey) y paginación. `02-04` headers, rate limit y compresión. `02-05` AWS con Terraform (ALB + 2 EC2 + CloudFront), Atlas, ForwardedHeaders (`NOTE (02-05)`), `AllowedHosts`, Trivy, ZAP, respaldos y tags de versión. `02-06` CloudWatch, cobertura y SonarCloud. `02-07` moderación. Landing page (repo propio).
- Equipo: roles PO/SM, revisores de las specs, umbrales "Por definir con el PO" (RNF-09, 10, 14, 16, 22), protección de `main` en GitHub y la clave Jira de la tarea de plantilla (commit `SCRUM-XX`).

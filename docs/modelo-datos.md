# Modelo de datos — MongoDB

Catálogo canónico de las colecciones de MUSUBI (spec `02-01`). Las specs de cada HU **enlazan** su fila y no la copian. Cuando una HU crea o cambia una colección, actualiza:
- la fila de este catálogo;
- un script nuevo `db/NNN-<slug>.js`;
- `MongoSchema.ExpectedVersion`.

Base: MongoDB 8.0 (Atlas M0 en la nube, `mongo:8.0.32` en local). Base de datos: `musubi`.

## 1. Reglas de diseño (escalabilidad)

Cada regla dice qué problema evita. Una colección nueva que rompa alguna regla lo justifica en su spec.

| # | Regla | Por qué |
|---|---|---|
| R1 | **`communityId` en cada documento de negocio** y como primer campo de los índices de consulta | Multi-comunidad (RNF-21): agregar una universidad es insertar un documento en `communities`, sin rediseño. Es también la clave de partición natural si algún día se usa *sharding* |
| R2 | **`_id` = ObjectId** generado por el driver | Ordenado por tiempo y sin secuencia central: escala horizontalmente con 2 o más instancias y permite **paginación por cursor** (`_id < último`), que no se degrada como `skip` |
| R3 | **Sin arreglos sin límite.** Un arreglo solo se embebe si tiene tope conocido (fotos ≤ 8, dominios de un campus, roles) | Un documento crece hasta 16 MB y se reescribe completo. Lo que crece sin límite (mensajes, ofertas, calificaciones, vistas) va en su propia colección con referencia al padre |
| R4 | **`schemaVersion` en cada documento** | Migraciones perezosas: el código lee versiones viejas y las actualiza al escribir; nunca hay que detener el servicio para migrar |
| R5 | **Índices con la regla ESR** (igualdad → orden → rango) y **solo los que usa una consulta real**, cada uno con nombre | Consultas cubiertas por índice y escrituras baratas. Cada índice cuesta en cada inserción, así que se justifica en el catálogo |
| R6 | **TTL para lo efímero** (códigos, sesiones, apartados vencidos, eventos procesados) | La base no crece con basura. El monitor TTL borra cada ~60 s, así que el código **también** filtra por `expiresAt` |
| R7 | **Atomicidad de un solo documento** (`findOneAndUpdate` con condición, `$inc`) en lugar de transacciones | Funciona igual en 1 o N instancias y en Atlas M0. Ejemplo: descontar inventario con `{ stock: { $gte: 1 } }` + `$inc: { stock: -1 }` nunca vende de más (HU-50) |
| R8 | **Copias pequeñas para lectura** (nombre y reputación del vendedor dentro de la publicación) con consistencia eventual | Un listado se resuelve con una consulta y sin joins. La copia se refresca por evento (`outbox`) cuando cambia el original |
| R9 | **Contadores separados de los documentos calientes** (`productStatsDaily` y no `product.views++`) | Evita la contención de escritura sobre el mismo documento cuando muchos usuarios ven un producto |
| R10 | **Datos sensibles aparte y cifrados**: credenciales en `users` (nunca proyectadas a respuestas) y CLABE cifrada en `payoutAccounts` | Mínimo privilegio (OWASP API3) y privacidad por diseño (RNF-05) |
| R11 | **Borrado real** de datos personales (HU-46) y TTL donde aplique; sin "borrado lógico" de personas | Privacidad (RNF-08). Lo que debe conservarse (pedidos, calificaciones) se **anonimiza** |
| R12 | **Archivos fuera de la base**: las imágenes van a S3 + CloudFront; en la base solo su llave | Atlas M0 tiene 512 MB; los binarios en la base no escalan |
| R13 | **Validación `$jsonSchema` en el servidor** (`validationLevel: strict`) además de la validación del API | Ningún proceso, ni un script manual, deja documentos corruptos |
| R14 | **Trabajo pesado vía `outbox`** (evento escrito junto con el cambio, procesado en segundo plano) | Coincidencias, notificaciones, IA y refresco de copias sin bloquear la petición (RNF-11/12) |

Convenciones del driver (`Platform/Data/MongoConventions.cs`):
- campos en camelCase;
- enums como texto;
- campos `null` omitidos;
- campos extra ignorados, para que una versión vieja de la API no falle con documentos nuevos.

Las fechas se guardan en UTC.

## 2. Colecciones

Estados: ✅ Implementada (script aplicado y repositorio con pruebas) · 📐 Diseño (se implementa con su HU).

| Colección | Módulo / HU | Contenido (campos clave) | Índices | Estado |
|---|---|---|---|---|
| `_meta` | 02-01 | `{ _id: "schema", version, applied[] }` | — | ✅ |
| `communities` | M-01 · HU-01/03 | `slug`, `name`, `shortName`, `emailDomains[]` (tope ~10; la UTTT incluye `musubiapp.com.mx` para la cuenta de revisión de Google Play, `db/003`), `status`, `academicTerm{unit, count}` (opcional; sin él, semestre y 12; la UTTT: cuatrimestre y 11, `db/004`) | `ux_slug` (único), `ux_emailDomains` (único, multikey: un dominio pertenece a un solo campus) | ✅ |
| `users` | M-01 · HU-01/02/03/40/46/47 | `communityId`, `email`, `emailNormalized`, `passwordHash`, `fullName`, `status` (`pendingVerification`/`active`/`suspended`), `emailVerifiedAt`, `consents{termsVersion, privacyVersion, acceptedAt}`, `roles[]` (tope 3) | `ux_emailNormalized` (único), `ix_community_status` | ✅ |
| `verificationCodes` | M-01 · HU-03 (y recuperar contraseña) | `userId`, `purpose`, `codeHash`, `attempts`, `lastSentAt`, `expiresAt` | `ux_user_purpose` (único: un código vigente por propósito), `ttl_expiresAt` | ✅ |
| `refreshTokens` | M-01 · HU-02 | `userId`, `familyId`, `tokenHash`, `expiresAt`, `usedAt`, `revokedAt`, `device` | `ux_tokenHash` (único), `ix_user`, `ix_family`, `ttl_expiresAt` | ✅ |
| `profiles` | M-02 · HU-04/05 | `_id` = `userId`, `communityId`, `fullName` (copia R8), `careerSlug`, `term` (1 a 20, dentro de la carrera), `avatarKey` (`media/avatars/<userId>/<id>.webp`), `visibility{career, sales, online}`, `stats{ratingAvg, ratingCount, ratingSum, onTimeCount, sales, purchases}` (desde HU-27: el promedio es `ratingSum / ratingCount` y "A tiempo" sale de `onTimeCount`); se crea con el primer cambio (upsert con `$setOnInsert`; sin documento valen los valores por defecto) | `ix_community` | ✅ (HU-04, `db/004`; HU-27, `db/020`) |
| `careers` | M-02 · HU-04 (las administra HU-41) | `communityId`, `slug`, `name`, `shortName`, `level` (`tsu`/`engineering`/`bachelor`), `firstTerm`, `lastTerm`, `order`, `active` (semilla: 38 de la UTTT, a validar con el equipo) | `ux_community_slug` | ✅ (HU-04, `db/004`) |
| `preferences` | M-02 · HU-54 | `_id` = `userId`, `theme` (`system`/`light`/`dark`); preferencias de notificaciones fuera del MVP hasta decisión del PO | `_id` único | ✅ (`db/018-preferencias.js`) |
| `categories` | M-03 · HU-06 (las administra HU-41) | `communityId`, `slug`, `name`, `order`, `active` (semilla: las 6 de la maqueta para la UTTT) | `ux_community_slug` | ✅ |
| `products` | M-03 · HU-06/07/08/50 · M-04 · HU-09/10/11 | `communityId`, `sellerId`, `seller{name}` (copia R8; `ratingAvg` con HU-27), `title` (3–80), `description` (≤ 500), `categorySlug`, `priceCents` (entero, 1 a 99 999 900), `condition` (`new`/`used`), `conditionDetail` (`likeNew`/`good`/`fair`, solo usado), `stock`, `status` (`active`/`soldOut`/`hidden`/`removed`/`sold`; `sold` desde `db/015`, HU-08), `soldAt` (fecha de "Marcar como vendido", HU-08), `photos[]` (1 a 8 llaves de S3 `media/products/<sellerId>/<id>.webp`), `searchWords[]` (palabras normalizadas de título y descripción: minúsculas, sin acentos y sin plural; HU-09); después: `moderation{status, checkedAt}` (02-07) | `ix_community_status_id` (vitrina, cursor), `ix_community_category_status_id` ("Filtrar" y rango de precios), `ix_community_status_price_id` / `ix_community_status_price_desc_id` (orden por precio, HU-10), `ix_community_category_status_price_id` / `ix_community_category_status_price_desc_id` (orden por precio con categoría, HU-10), `ix_seller_id` ("Mis productos"), `ix_community_status_search` (búsqueda por prefijo sobre `searchWords`; HU-09, `db/007`). Editar, pausar y eliminar (HU-07) filtran por `_id` + `sellerId` en la misma operación; editar recalcula `searchWords`; eliminar es borrado real (fotos incluidas) | ✅ (HU-06; búsqueda HU-09; filtros HU-10, `db/010`; editar y eliminar HU-07; vendida HU-08, `db/015`) |
| `productStatsDaily` | M-04 · HU-49 | `productId`, `communityId`, `day`, `views` (`$inc`) | `ux_product_day`, `ix_community_day_views`, TTL de 30 días | 📐 |
| `favorites` | M-04 · HU-53 | `userId`, `productId`, `communityId`, `createdAt` | `ux_user_product`, `ix_user_id` | ✅ (HU-53, `db/013`) |
| `alerts` | M-04 · HU-12/29/51 | `userId`, `communityId`, `type` (`savedSearch`/`backInStock`), `query` o `productId`, `active` | `ix_community_type_active`, `ix_user` | 📐 |
| `wantedRequests` | M-05 · HU-13/14/15 | `communityId`, `requesterId`, `requesterName` (copia R8), `title`, `categorySlug` + `categoryName` (copia; u "otro"), `description?`, `features[]` (≤ 5), `budgetCents`, `quantity`, `neededBy?`, `expiresAt` (RN-10), `status` (`active`, `closed` o `cancelled`, HU-14; vencida se calcula con `expiresAt`, que se recalcula al editar), `offersCount` (`$inc`, HU-16), `createdAt` | `ix_community_status_id` (tablero, HU-15), `ix_requester_id` (mis solicitudes, RN-12 y borrado) | ✅ (HU-13, `db/009`; HU-14 sin script nuevo: el esquema ya admitía `closed` y `cancelled`) |
| `offers` | M-05 · HU-16/17 · M-06 · HU-18/19 | `communityId`, `requestId`, `requesterId` (copia de la solicitud), `sellerId`, `sellerName` (copia R8), `kind` (`listing`/`direct`, RN-13), `productId?`, `title` (copia del nombre de la publicación o descripción), `priceCents`, `condition`, `conditionDetail?` (copia de la publicación), `photoKey?`, `message?` (≤ 300), `status` (`sent`, `accepted`, `rejected`; HU-17), `decidedAt?`, `createdAt`, `updatedAt` | `ux_request_seller` (una por persona y solicitud, RN-14), `ix_request_id` (HU-17), `ix_seller` e `ix_requester` (borrado HU-46) | ✅ (HU-16, `db/011`; HU-17, `db/012`) |
| `conversations` | M-07 · HU-20/21/22 | `communityId`, `buyerId`, `sellerId`, `participants[2]`, `context{type (product; offer desde HU-17), id, title y photo (copias R8; photo desde HU-22)}`, `buyerName`/`sellerName` (copias R8; HU-22), `lastMessage{senderId, text (≤ 120), at}` (copia R8, se omite sin mensajes), `lastMessageAt`, `buyerUnread`/`sellerUnread` (≥ 0; HU-22), `createdAt` | `ux_context_buyer` (una por comprador y elemento), `ix_participant_lastAt` (bandeja HU-22 y borrado HU-46) | ✅ (HU-20, `db/005`; HU-22, `db/006`; HU-17, `db/012`) |
| `messages` | M-07 · HU-20/21 | `conversationId`, `communityId`, `senderId`, `text` (1 a 1000, moderado antes de guardar, 02-07), `kind?` (`notice`: un aviso de la app como "Ana pidió un resumen con IA", HU-32; sin `kind` es texto), `createdAt` | `ix_conversation_id` (`{conversationId, _id: -1}`, cursor) | ✅ (HU-20, `db/005`; `kind` en `db/015`) |
| `agreements` | M-10 · HU-32/33/34 | `communityId`, `conversationId`, `buyerId`, `sellerId`, `requestedBy`, `proposal { title, priceCents?, quantity?, pointSlug?, pointName?, detail?, at?, conditions? }` (lo que falta es "No identificado", RN-42), `notIdentified[]` (lo que la IA no encontró), `revision` (una confirmación vale para la versión que se vio), `status` (`proposed`/`confirmed`), `buyerConfirmedAt?`, `sellerConfirmedAt?`, `expiresAt`. Del chat no se guarda nada; se borra al crear el pedido, al descartarlo o a los 30 días (RN-44) | `ux_conversation` (uno por conversación), `ix_buyer_id`, `ix_seller_id` (HU-46), `ttl_expires_at` (TTL) | ✅ (HU-32/33/34, `db/015`) |
| `reservations` | M-08 · HU-52 | `communityId`, `productId`, `buyerId`, `sellerId`, `pickup{place, at}`, `codeHash`, `attempts`, `status`, `expiresAt` | `ix_buyer_status`, `ix_seller_status`, `ttl_expiresAt` (solo vencidas) | 📐 |
| `orders` | M-08 · HU-23/24/25/26/44/55 | `communityId`, `number` (MS-NNNN por campus), `buyerId`, `sellerId`, `buyerName`/`sellerName` (copias R8; "Usuario eliminado" al borrar la cuenta, R11), `conversationId` (RN-21), `item { source (product/offer), sourceId, productId?, title, photo?, priceCents, quantity (1 a 10), totalCents }` (copia R8), `delivery { pointSlug, pointName, detail?, at }`, `payment? { method (cash/transfer), chosenAt }`, `status` (`pending`/`confirmed`/`completed`/`cancelled`), `history[]` (≤ 10 transiciones con `reason?`), `handover? { buyerAt?, sellerAt? }` (cada parte confirma la entrega; con las dos se completa, RN-19), `ratings? { buyerAt?, sellerAt? }` (quién ya calificó, HU-27/HU-45) y `cancellation? { by (buyer/seller/system), reason (lista cerrada, RN-20), at }` (HU-24). Aceptar resta las piezas de `products.stock` y cancelar un confirmado las regresa (HU-24). HU-55 agrega la referencia y `deliveryCodeHash` | `ux_conversation_open` (único parcial: uno `pending`/`confirmed` por conversación), `ix_conversation_id`, `ix_buyer_id`, `ix_seller_id`, `ix_product_status` (RN-07) | ✅ (HU-23, `db/014`; HU-24, `db/019`; HU-27, `db/020`) |
| `meetingPoints` | M-08 · HU-23/44 | `communityId`, `slug`, `name`, `careerSlugs[]` (las carreras de ese edificio: se recomienda a quien estudia ahí), `order`, `active`. Semilla: los 14 edificios de la UTTT | `ux_community_slug` | ✅ (HU-23, `db/014`) |
| `counters` | M-08 · HU-23 | `_id` (`orders:<communityId>`), `seq` (`$inc` atómico; el número es 1000 + `seq`) | — | ✅ (HU-23, `db/014`) |
| `payoutAccounts` | M-08 · HU-55 | `_id` = `userId`, `holderName`, `bank`, `clabeCiphertext`, `clabeLast4`, `verifiedAt` | — | 📐 |
| `ratings` | M-13 · HU-27/45 | `communityId`, `orderId`, `raterId`, `raterName` (nombre corto; "Usuario eliminado" al borrar la cuenta, R11), `ratedId`, `ratedRole` (`seller`/`buyer`), `stars` (1 a 5, RN-25), `tags[]` (≤ 5, lista cerrada por rol), `comment?` (≤ 280, moderado), `createdAt`. Una por pedido y por quien califica (RN-24), hasta 7 días después de completado; al borrar una cuenta se borran las que recibió | `ux_order_rater` (único), `ix_rated_id` (`{ ratedId, _id: -1 }`) | ✅ (HU-27, `db/020`) |
| `reports` | M-13 · HU-28 · M-14 · HU-39 | `communityId`, `reporterId`, `target { type (general/user/product), id? }`, `conversationId?`, `reason` (los 5 de la maqueta), `details?` (≤ 1000, sin moderar: no se publica), `status` (`open`), `createdAt` | `ix_community_status` (`{communityId, status, createdAt: -1}`, bandeja de HU-39), `ix_reporter` (borrado, HU-46) | ✅ (HU-28, `db/008`) |
| `blocks` | M-13 · HU-57 | `communityId`, `blockerId`, `blockedId`, `blockedName` (copia R8), `createdAt` | `ux_blocker_blocked` (único: idempotencia y "a quiénes bloqueé"), `ix_blocked` ("quiénes me bloquearon") | ✅ (HU-57, `db/008`) |
| `notifications` | M-09 · HU-31 | `userId`, `type`, `category` (`orders`/`requests`/`matches`), `title`, `body` (≤ 140), `target { type, id }`, `dedupeKey?` (un aviso una sola vez, p. ej. "vence mañana"), `readAt?`, `createdAt`, `expiresAt` (60 días). Los mensajes del chat no se guardan aquí | `ix_user_id`, `ix_user_category_id`, `ux_user_dedupe` (único parcial), `ttl_expires_at` | ✅ (HU-31, `db/017`) |
| `deviceTokens` | M-09 · HU-31 | `userId`, `token` (FCM, único), `createdAt`, `lastSeenAt`, `expiresAt` (60 días sin renovarse) | `ux_token`, `ix_user_id`, `ttl_expires_at` | ✅ (HU-31, `db/017`) |
| `recommendations` | M-11 · HU-35 · M-12 · HU-36/37/38 | `userId` o `productId`, `items[]` (≤ 20), `model`, `generatedAt` | `ux_subject`, TTL | 📐 |
| `moderationLog` / `auditLog` | M-14 · HU-39/40 (RNF-04) | `actorId`, `action`, `target`, `at`, `reason` | `ix_target`, `ix_actor_at` | 📐 |
| `outbox` | 02-02 | `type`, `payload`, `status`, `attempts`, `nextAttemptAt`, `processedAt` | `ix_status_next`, TTL de 7 días sobre procesados | 📐 |

## 3. Escala prevista y límites

- **Piloto:** Atlas M0 (512 MB, 100 operaciones/s aprox.). Con documentos de ~1–2 KB y las imágenes en S3 (R12), alcanza para decenas de miles de publicaciones.
- **Siguiente escalón sin cambiar el código:** otro *tier* de Atlas (M10 o superior). Los índices y las consultas son los mismos.
- **Sharding** (si alguna vez hace falta): clave `{ communityId: 1, _id: 1 }` en las colecciones grandes (`products`, `messages`, `orders`), que ya tienen ese prefijo en sus índices (R1, R2).
- **Lecturas calientes:** caché de Redis/Valkey (cache-aside, spec `02-02`) delante de categorías, populares y perfiles públicos; la base sigue siendo la fuente de verdad.

## 4. Scripts

Los scripts viven en `db/` (ver `db/README.md`): son numerados e idempotentes, y cada uno sube `_meta.version`. Son el **script de la base** que se entrega en la evaluación (lineamiento 19). La API no arranca como lista (`/health/ready` responde Unhealthy) si la base tiene una versión menor que `MongoSchema.ExpectedVersion`.

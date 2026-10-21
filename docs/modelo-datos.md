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
| R3 | **Sin arreglos sin límite.** Un arreglo solo se embebe si tiene tope conocido (fotos ≤ 6, dominios de un campus, roles) | Un documento crece hasta 16 MB y se reescribe completo. Lo que crece sin límite (mensajes, ofertas, calificaciones, vistas) va en su propia colección con referencia al padre |
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
| `communities` | M-01 · HU-01/03 | `slug`, `name`, `shortName`, `emailDomains[]` (tope ~10), `status` | `ux_slug` (único), `ux_emailDomains` (único, multikey: un dominio pertenece a un solo campus) | ✅ |
| `users` | M-01 · HU-01/02/03/40/46/47 | `communityId`, `email`, `emailNormalized`, `passwordHash`, `fullName`, `status` (`pendingVerification`/`active`/`suspended`), `emailVerifiedAt`, `consents{termsVersion, privacyVersion, acceptedAt}`, `roles[]` (tope 3) | `ux_emailNormalized` (único), `ix_community_status` | ✅ |
| `verificationCodes` | M-01 · HU-03 (y recuperar contraseña) | `userId`, `purpose`, `codeHash`, `attempts`, `lastSentAt`, `expiresAt` | `ux_user_purpose` (único: un código vigente por propósito), `ttl_expiresAt` | ✅ |
| `refreshTokens` | M-01 · HU-02 | `userId`, `familyId`, `tokenHash`, `expiresAt`, `usedAt`, `revokedAt`, `device` | `ux_tokenHash` (único), `ix_user`, `ix_family`, `ttl_expiresAt` | ✅ |
| `profiles` | M-02 · HU-04/05 | `_id` = `userId`, `communityId`, `fullName` (copia), `career`, `semester`, `avatarKey`, `stats{ratingAvg, ratingCount, sales, purchases}` | `ix_community` | 📐 |
| `preferences` | M-02 · HU-54 | `_id` = `userId`, `theme`, `notifications{…}` | — | 📐 |
| `categories` | M-14 · HU-41 | `communityId`, `slug`, `name`, `order`, `active` | `ux_community_slug` | 📐 |
| `products` | M-03 · HU-06/07/08/50 · M-04 · HU-09/10/11 | `communityId`, `sellerId`, `seller{name, ratingAvg}` (copia R8), `title`, `description`, `categoryId`, `price` (centavos, entero), `condition`, `stock`, `status` (`active`/`soldOut`/`hidden`/`removed`), `photos[]` (≤ 6 llaves de S3), `moderation{status, checkedAt}`, `version` | `ix_community_status_id` (recién publicados, cursor), `ix_community_category_price`, `ix_seller_status`, `tx_title_description` (texto, idioma español, HU-09) | 📐 |
| `productStatsDaily` | M-04 · HU-49 | `productId`, `communityId`, `day`, `views` (`$inc`) | `ux_product_day`, `ix_community_day_views`, TTL de 30 días | 📐 |
| `favorites` | M-04 · HU-53 | `userId`, `productId`, `communityId`, `createdAt` | `ux_user_product`, `ix_user_id` | 📐 |
| `alerts` | M-04 · HU-12/29/51 | `userId`, `communityId`, `type` (`savedSearch`/`backInStock`), `query` o `productId`, `active` | `ix_community_type_active`, `ix_user` | 📐 |
| `wantedRequests` | M-05 · HU-13/14/15 | `communityId`, `requesterId`, `title`, `details`, `categoryId`, `budgetMax`, `neededBy`, `status`, `offersCount` (`$inc`) | `ix_community_status_id`, `ix_requester_status`, texto | 📐 |
| `offers` | M-05 · HU-16/17 · M-06 · HU-18/19 | `requestId`, `communityId`, `sellerId`, `productId?`, `price`, `message`, `status` | `ix_request_status`, `ix_seller` | 📐 |
| `conversations` | M-07 · HU-20/21/22 | `communityId`, `participants[2]`, `context{type, id, title}`, `lastMessage{…}` (copia), `unread{userId: n}` | `ix_participant_lastAt` (bandeja) | 📐 |
| `messages` | M-07 · HU-20/21 | `conversationId`, `senderId`, `text`, `moderation`, `createdAt` | `ix_conversation_id` (cursor) | 📐 |
| `agreements` | M-10 · HU-32/33/34 | `conversationId`, `proposal{item, price, place, date}` (con "No identificado"), `confirmations{buyer, seller}`, `status` | `ux_conversation_active` | 📐 |
| `reservations` | M-08 · HU-52 | `communityId`, `productId`, `buyerId`, `sellerId`, `pickup{place, at}`, `codeHash`, `attempts`, `status`, `expiresAt` | `ix_buyer_status`, `ix_seller_status`, `ttl_expiresAt` (solo vencidas) | 📐 |
| `orders` | M-08 · HU-23/24/25/26/44/55 | `communityId`, `buyerId`, `sellerId`, `item{productId, title, price}` (copia), `payment{method, status, reference}`, `deliveryCodeHash`, `status` (`pending`/`confirmed`/`completed`/`cancelled`), `history[]` (≤ 10 transiciones) | `ix_buyer_id`, `ix_seller_id` | 📐 |
| `payoutAccounts` | M-08 · HU-55 | `_id` = `userId`, `holderName`, `bank`, `clabeCiphertext`, `clabeLast4`, `verifiedAt` | — | 📐 |
| `ratings` | M-13 · HU-27/45 | `orderId`, `raterId`, `ratedId`, `role`, `stars`, `comment` | `ux_order_rater`, `ix_rated_id` | 📐 |
| `reports` | M-13 · HU-28 · M-14 · HU-39 | `communityId`, `reporterId`, `target{type, id}`, `reason`, `status` | `ix_community_status` | 📐 |
| `notifications` | M-09 · HU-29/30/31 | `userId`, `type`, `payload`, `readAt`, `expiresAt` | `ix_user_id`, TTL de 90 días | 📐 |
| `deviceTokens` | M-09 · HU-31 | `userId`, `token` (FCM), `platform`, `lastSeenAt` | `ux_token`, `ix_user` | 📐 |
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

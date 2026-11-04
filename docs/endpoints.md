# Catálogo de endpoints de musubi-api (canónico)

Fuente única de verdad del contrato HTTP de MUSUBI: método, ruta, auth, DTOs, códigos y quién lo consume.

- Las specs (de este repo y de `musubi-app`) **enlazan** sus filas; nunca copian la tabla.
- Lo verifica `scripts/check_endpoints.ps1` (job `endpoints` del CI) en ambos sentidos: cada ruta del código tiene su fila y cada fila existe en el código.
- El front consume una fila enlazándola desde su spec y desde `lib/core/network/api_paths.dart` (skill `api-contract` de `musubi-app`).
- Para cambiar una fila: actualizar el código, esta tabla, el fixture de `ContractTest/Fixtures/` (si cambia la forma JSON) y avisar en el PR del front.

## Convenciones

- Base versionada `api/v1/...` (`api/v{version:apiVersion}/...` + `UrlSegmentApiVersionReader`; las respuestas incluyen el header `api-supported-versions`).
- JSON en **camelCase** (serializador por defecto de ASP.NET Core), con códigos en inglés y mensajes al usuario en español (RNF-18).
- Auth **explícita** en cada controller: `[Authorize]` / `[Authorize(Policy = ...)]` / `[AllowAnonymous]` (bloqueante B3 del critic + `SecurityTest/Auth/ExplicitAuthTests.cs`).
- Errores: `application/problem+json` con `code` estable, título en español y `traceId`, sin detalle interno. Catálogo canónico: [`docs/errores.md`](errores.md) (02-03).
- Sesión obligatoria por defecto: un endpoint sin `[AllowAnonymous]` exige `Authorization: Bearer <access token>` (02-03). Una ruta inexistente sin sesión responde 401.
- **Caché (02-02):** las filas con `ETag` responden `304` sin cuerpo si el cliente manda el mismo `If-None-Match` (`Cache-Control: no-cache`: siempre se revalida). La vitrina y las categorías anónimas además salen de la caché de salida (30 s y 10 min); con sesión nunca.
- **Moderación (02-07):** las fotos (productos y perfil) y los textos que publica una persona se revisan con AWS antes de guardarse; lo inapropiado responde `422 moderation.*` y no se guarda.
- Solo en Development (no se cuentan como filas porque no son endpoints de la API): `/openapi/v1.json` (OpenAPI) y `/scalar` (referencia interactiva).

## Plataforma

| Método | Ruta | Auth | Spec | Request | Response | Códigos | Consumidor |
|---|---|---|---|---|---|---|---|
| GET | /health | Anónimo (health check, fuera de MVC) | 01-03 | — | `Healthy` (texto) | 200/503 | Balanceador / orquestador (liveness) |
| GET | /health/ready | Anónimo (health check, fuera de MVC) | 01-03 | — | `Healthy` / `Unhealthy` (texto); incluye el check `database` | 200/503 | Balanceador / orquestador (readiness) |
| GET | /api/v1/ping | `[AllowAnonymous]` | 01-03 | — | `PingResponse { status: "pong", version: "v1" }` | 200 | `musubi-app` 01-03 (`StatusRepository`) |
| GET | /media/{**path} | `[AllowAnonymous]` | HU-06 | — | La imagen (`image/webp`) guardada en memoria | 200 / 404 `resource.not_found` | Solo Development (`Media:Provider=InMemory`). En la nube CloudFront sirve `/media/*` desde S3 y la API responde 404 |

Total: 28 filas (4 de plataforma, 24 de módulos). Las filas de los módulos M-01…M-14 se agregan con la spec de cada HU.

## M-01 · Autenticación y usuarios

Públicos (aún no hay sesión) y con rate limit `auth` por IP (429 + `Retry-After`). Errores del catálogo [`docs/errores.md`](errores.md). Las respuestas nunca llevan hashes ni códigos.

| Método | Ruta | Auth | Spec | Request | Response | Códigos | Consumidor |
|---|---|---|---|---|---|---|---|
| POST | /api/v1/auth/campus | `[AllowAnonymous]` + rate limit | HU-01 | `CampusRequest { email }` | `CampusResponse { shortName, name }` | 200 / 400 `validation.failed` / 422 `auth.email_not_institutional` / 429 | `musubi-app` HU-01 ("Campus detectado" mientras se escribe) |
| POST | /api/v1/auth/register | `[AllowAnonymous]` + rate limit | HU-01, HU-47 | `RegisterRequest { email, fullName, password, acceptTerms, acceptPrivacy }` | `RegisterResponse { email, codeExpiresAt, resendAvailableAt }` | 201 / 400 `validation.failed` · `auth.consent_required` · `auth.weak_password` / 409 `auth.email_already_registered` / 422 `auth.email_not_institutional` / 429 | `musubi-app` HU-01 (pantalla `Registro`) |
| POST | /api/v1/auth/verification/resend | `[AllowAnonymous]` + rate limit | HU-03 | `ResendCodeRequest { email }` | `ResendCodeResponse { resendAvailableAt }` (igual exista o no la cuenta) | 202 / 429 `auth.resend_too_soon` · `rate_limit.exceeded` | `musubi-app` HU-03 ("Reenviar código") |
| POST | /api/v1/auth/verification/confirm | `[AllowAnonymous]` + rate limit | HU-03 | `ConfirmCodeRequest { email, code }` | `SessionResponse { accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt, user { id, fullName, email, emailVerified, campus { shortName, name } } }` | 200 / 400 `auth.invalid_code` · `validation.failed` / 429 `auth.code_attempts_exceeded` | `musubi-app` HU-03 (`Verificacion` → `Verificacion-lista`) |
| POST | /api/v1/auth/login | `[AllowAnonymous]` + rate limit | HU-02 | `LoginRequest { email, password }` | `SessionResponse` (misma forma que `verification/confirm`) | 200 / 400 `validation.failed` / 401 `auth.invalid_credentials` (no revela si el correo existe) / 403 `auth.email_not_verified` · `auth.account_suspended` (solo con la contraseña correcta) / 429 | `musubi-app` HU-02 (pantalla `Iniciar sesion`) |
| POST | /api/v1/auth/refresh | `[AllowAnonymous]` + rate limit | HU-02 (02-03) | `RefreshRequest { refreshToken }` | `SessionResponse` (misma forma que `verification/confirm`); el refresh token rota en cada uso | 200 / 400 `validation.failed` / 401 `auth.session_expired` (vencido, revocado o reutilizado: la reutilización revoca la familia) / 429 | `musubi-app` 02-01 (`AuthInterceptor` ante un 401) |
| POST | /api/v1/auth/logout | `[AllowAnonymous]` + rate limit | HU-02 | `LogoutRequest { refreshToken }` | — (idempotente: un token desconocido también responde 204) | 204 / 400 `validation.failed` / 429 | `musubi-app` HU-02 (Perfil → "Cerrar sesión") |
| POST | /api/v1/auth/password/forgot | `[AllowAnonymous]` + rate limit | HU-56 | `RecoveryRequest { email }` | `ResendCodeResponse { resendAvailableAt }` (igual exista o no la cuenta; solo se envía a cuentas activas) | 202 / 400 `validation.failed` / 429 | `musubi-app` HU-56 ("¿Olvidaste tu contraseña?") |
| POST | /api/v1/auth/password/reset | `[AllowAnonymous]` + rate limit | HU-56 | `RecoveryResetRequest { email, code, newPassword }` | — (cierra todas las sesiones abiertas de la cuenta) | 204 / 400 `auth.invalid_code` · `auth.weak_password` · `validation.failed` / 429 `auth.code_attempts_exceeded` · `rate_limit.exceeded` | `musubi-app` HU-56 (código + contraseña nueva) |
| POST | /api/v1/auth/password/change | `[Authorize]` + rate limit `auth` | HU-04 | `ChangePasswordRequest { currentPassword, newPassword }` | `SessionResponse` (misma forma que `verification/confirm`): sesión nueva; las demás sesiones de la cuenta quedan cerradas | 200 / 400 `auth.wrong_password` · `auth.weak_password` · `validation.failed` / 401 `auth.session_expired` / 429 | `musubi-app` HU-04 (`Ajustes` → "Cambiar contraseña") |
| POST | /api/v1/account/delete | `[Authorize]` + rate limit `auth` | HU-46 | `DeleteAccountRequest { password }` (contraseña actual) | — (borra la cuenta, sus sesiones, sus códigos, sus publicaciones y sus fotos; borrado real R11) | 204 / 400 `auth.wrong_password` · `validation.failed` / 401 `auth.session_expired` (la cuenta ya no existe) / 429 | `musubi-app` HU-46 (`Privacidad` → "Eliminar mi cuenta") |

## M-02 · Perfil

Con sesión. Las rutas de escritura no llevan id: solo se edita el perfil del token (OWASP API1). El perfil público solo existe dentro del campus y oculta lo que la persona apagó en "Quién te ve" (OWASP API3). Las rutas de imagen (`/media/...`) son relativas a la URL base de la API. Errores del catálogo [`docs/errores.md`](errores.md).

`MyProfileResponse { id, fullName, email, emailVerifiedAt, campus { shortName, name }, academicTerm { unit, count }, career { slug, name, shortName, firstTerm, lastTerm } o null, term o null, photo o null, visibility { career, sales, online }, stats { ratingAvg (o null), ratingCount, sales, purchases } }`.

| Método | Ruta | Auth | Spec | Request | Response | Códigos | Consumidor |
|---|---|---|---|---|---|---|---|
| GET | /api/v1/profiles/me | `[Authorize]` | HU-04 | — | `MyProfileResponse` (sin perfil guardado: los valores por defecto) | 200 / 401 / 403 `auth.forbidden` | `musubi-app` HU-04 (`Perfil`, `Ajustes`, `Privacidad`) |
| PUT | /api/v1/profiles/me | `[Authorize]` | HU-04 | `UpdateProfileRequest { career, term }` (slug de `GET /careers` y periodo; `null` los borra) | `MyProfileResponse` | 200 / 400 `profiles.invalid_career` · `profiles.invalid_term` · `validation.failed` / 401 / 403 `auth.forbidden` | `musubi-app` HU-04 (`Ajustes` → "Datos personales") |
| PUT | /api/v1/profiles/me/visibility | `[Authorize]` | HU-04 | `VisibilityRequest { career, sales, online }` (los tres obligatorios; por defecto `true`, `true` y `false`) | `MyProfileResponse` | 200 / 400 `validation.failed` / 401 / 403 `auth.forbidden` | `musubi-app` HU-04 (`Privacidad` → "Quién te ve") |
| PUT | /api/v1/profiles/me/photo | `[Authorize]` + rate limit `uploads` (30/min por usuario) | HU-04 | `multipart/form-data` con `photo` (JPG, PNG o WebP; hasta 10 MB) | `MyProfileResponse`; la foto se guarda cuadrada de 512 px, en WebP y sin metadatos, y se borra la anterior | 200 / 401 / 403 `auth.forbidden` / 413 `listings.photo_too_large` · `request.too_large` / 415 `listings.photo_unsupported` / 422 `listings.photo_too_small` · `moderation.image_rejected` / 429 / 503 `moderation.unavailable` | `musubi-app` HU-04 (`Perfil` → foto) |
| DELETE | /api/v1/profiles/me/photo | `[Authorize]` | HU-04 | — | — (idempotente: sin foto también responde 204; borra el archivo) | 204 / 401 / 403 `auth.forbidden` | `musubi-app` HU-04 (`Perfil` → "Quitar foto") |
| GET | /api/v1/profiles/{id} | `[Authorize]` | HU-04 (HU-05 lo amplía) | — | `PublicProfileResponse { id, fullName, campus, photo, memberSince, career, term, stats { ratingAvg, ratingCount, sales } }`, sin correo; `career` y `term` en `null` si "Mostrar mi carrera" está apagado, y `sales` si "Mostrar mis ventas" lo está | 200 / 401 / 404 `profiles.not_found` (inexistente, inactiva o de otro campus: no revela cuál) | `musubi-app` HU-05 (`Perfil-vendedor`) |
| GET | /api/v1/careers | `[Authorize]` | HU-04 | — | `CareersResponse { academicTerm { unit, count }, careers: [{ slug, name, shortName, level, firstTerm, lastTerm }] }` (activas del campus del token, en su orden); `ETag` | 200 / 304 / 401 / 403 `auth.forbidden` | `musubi-app` HU-04 ("Datos personales": carrera y periodo) |

## M-03 · Publicaciones

Con sesión salvo la vitrina y las categorías, que son públicas (HU-48). Las fotos se suben primero y la publicación lleva sus llaves; solo se aceptan llaves que subió el mismo usuario (OWASP API1). Las rutas de imagen (`/media/...`) son relativas a la URL base de la API. Errores del catálogo [`docs/errores.md`](errores.md).

| Método | Ruta | Auth | Spec | Request | Response | Códigos | Consumidor |
|---|---|---|---|---|---|---|---|
| GET | /api/v1/categories | `[AllowAnonymous]` | HU-06 | `?campus=` (visitante; por defecto `uttt`) | `CategoryResponse[] { slug, name }` (del campus del token si hay sesión); `ETag` | 200 / 304 / 404 `listings.unknown_campus` | `musubi-app` HU-06 (`Vender-3`) y "Filtrar" |
| GET | /api/v1/products | `[AllowAnonymous]` | HU-48 fase 2 | `?campus=&category=&hideSoldOut=&cursor=&limit=` (1 a 50; 20 por defecto) | `ProductPage { items: ProductResponse[], nextCursor, total }`; de la más nueva a la más vieja; `seller` es `null` sin sesión (RNF-05); `ETag` | 200 / 304 / 400 `listings.invalid_cursor` / 404 `listings.unknown_campus` | `musubi-app` HU-48 (`Inicio-visitante`, `Inicio`, "Filtrar") |
| GET | /api/v1/products/mine | `[Authorize]` | HU-06 | — | `ProductPage` (las del usuario, también pausadas y agotadas; hasta 50) | 200 / 401 | `musubi-app` HU-06 ("Ver mis productos") |
| GET | /api/v1/products/price-range | `[Authorize]` | HU-06 | `?category=` | `PriceRangeResponse { lowCents, highCents, count }` (percentiles 25 y 75 de las activas de la categoría) | 200 / 204 (menos de 3 publicaciones) / 401 | `musubi-app` HU-06 (`Vender-3`: "Productos parecidos se venden entre…") |
| POST | /api/v1/products | `[Authorize]` | HU-06, HU-50 | `CreateProductRequest { title, description, category, priceCents, condition, conditionDetail, stock, photos[] }` | `ProductResponse { id, title, description, category, priceCents, condition, conditionDetail, stock, status, photos[], seller { name }, publishedAt }` | 201 / 400 `listings.invalid_photos` · `listings.invalid_title` · `listings.invalid_description` · `listings.invalid_price` · `listings.invalid_category` · `listings.invalid_condition` · `listings.invalid_stock` · `validation.failed` / 401 / 403 `auth.forbidden` / 422 `moderation.text_rejected` / 503 `moderation.unavailable` | `musubi-app` HU-06 (`Vender-4` → `Vender-listo`) |
| POST | /api/v1/products/photos | `[Authorize]` + rate limit `uploads` (30/min por usuario) | HU-06 | `multipart/form-data` con `photo` (JPG, PNG o WebP; hasta 10 MB) | `PhotoResponse { key, path }`; la foto se guarda en WebP de 1600 px como máximo y sin metadatos | 201 / 401 / 413 `listings.photo_too_large` · `request.too_large` / 415 `listings.photo_unsupported` / 422 `listings.photo_too_small` · `moderation.image_rejected` / 429 / 503 `moderation.unavailable` | `musubi-app` HU-06 (`Vender-1`: "Tomar foto" y "Galería") |

## M-07 · Chat

Siempre con sesión. Solo los dos participantes leen o escriben una conversación; para cualquier otra persona responde `404 chat.conversation_not_found`, igual que si no existiera (RN-22, OWASP API1). Los mensajes se envían por REST (validación, moderación y rate limit en un solo lugar) y se **entregan en tiempo real** por el hub. Con 2 instancias, el backplane de Redis (`Cache:ConnectionString`) las une; sin él, cada instancia entrega solo a sus conexiones. Errores del catálogo [`docs/errores.md`](errores.md).

| Método | Ruta | Auth | Spec | Request | Response | Códigos | Consumidor |
|---|---|---|---|---|---|---|---|
| POST | /api/v1/conversations | `[Authorize]` | HU-20 | `StartConversationRequest { productId }` | `ConversationResponse { id, role (buyer/seller), counterpart { id, name }, context { type: product, id, title, priceCents, condition, conditionDetail, photo, available }, createdAt }`. Si el comprador ya tenía una conversación sobre esa publicación, devuelve la misma | 201 (nueva) / 200 (ya existía) / 400 `chat.own_product` / 401 / 403 `auth.forbidden` / 404 `chat.product_unavailable` (no existe, de otro campus, pausada o quitada) | `musubi-app` HU-20 (`Producto` → "Mensaje") |
| GET | /api/v1/conversations/{id} | `[Authorize]` | HU-20 (HU-21 lo amplía) | — | `ConversationResponse`; el precio, el estado y la foto del contexto son los actuales. Si la publicación ya no se puede ver: `available: false` y solo el nombre guardado | 200 / 401 / 404 `chat.conversation_not_found` | `musubi-app` HU-20 (`Chat-Valeria`: encabezado y tarjeta) |
| GET | /api/v1/conversations/{id}/messages | `[Authorize]` | HU-20 | `?cursor=&limit=` (1 a 100; 30 por defecto) | `MessagePage { items: MessageResponse[], nextCursor }`, del más nuevo al más viejo | 200 / 400 `chat.invalid_cursor` / 401 / 404 `chat.conversation_not_found` | `musubi-app` HU-20 (historial y "cargar anteriores") |
| POST | /api/v1/conversations/{id}/messages | `[Authorize]` + rate limit `chat` (30/min por usuario) | HU-20 | `SendMessageRequest { text }` (1 a 1000 caracteres; se recortan los espacios de los extremos) | `MessageResponse { id, conversationId, senderId, text, sentAt }`; también se emite `messageCreated` por el hub a los dos participantes | 201 / 400 `chat.invalid_message` / 401 / 403 `auth.forbidden` / 404 `chat.conversation_not_found` / 422 `moderation.text_rejected` / 429 `rate_limit.exceeded` / 503 `moderation.unavailable` | `musubi-app` HU-20 (`Chat-Valeria`: "Mensaje") |
| WS | /hubs/chat | `[Authorize]` (token en `?access_token=`, que solo se acepta en `/hubs`) | HU-20 | Solo WebSockets y **sin negociar** (`skipNegotiation: true`): la conexión no depende de caer en la misma instancia que la negociación. El cliente no llama métodos del hub, solo escucha | Evento `messageCreated` con un `MessageResponse` (a todos los dispositivos de los dos participantes) | 101 (WebSocket abierto) / 401 sin token o con un token inválido | `musubi-app` HU-20 (chat abierto en tiempo real) |

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
- Solo en Development (no se cuentan como filas porque no son endpoints de la API): `/openapi/v1.json` (OpenAPI) y `/scalar` (referencia interactiva).

## Plataforma

| Método | Ruta | Auth | Spec | Request | Response | Códigos | Consumidor |
|---|---|---|---|---|---|---|---|
| GET | /health | Anónimo (health check, fuera de MVC) | 01-03 | — | `Healthy` (texto) | 200/503 | Balanceador / orquestador (liveness) |
| GET | /health/ready | Anónimo (health check, fuera de MVC) | 01-03 | — | `Healthy` / `Unhealthy` (texto); incluye el check `database` | 200/503 | Balanceador / orquestador (readiness) |
| GET | /api/v1/ping | `[AllowAnonymous]` | 01-03 | — | `PingResponse { status: "pong", version: "v1" }` | 200 | `musubi-app` 01-03 (`StatusRepository`) |
| GET | /media/{**path} | `[AllowAnonymous]` | HU-06 | — | La imagen (`image/webp`) guardada en memoria | 200 / 404 `resource.not_found` | Solo Development (`Media:Provider=InMemory`). En la nube CloudFront sirve `/media/*` desde S3 y la API responde 404 |

Total: 20 filas (4 de plataforma, 16 de módulos). Las filas de los módulos M-01…M-14 se agregan con la spec de cada HU.

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
| POST | /api/v1/account/delete | `[Authorize]` + rate limit `auth` | HU-46 | `DeleteAccountRequest { password }` (contraseña actual) | — (borra la cuenta, sus sesiones, sus códigos, sus publicaciones y sus fotos; borrado real R11) | 204 / 400 `auth.wrong_password` · `validation.failed` / 401 `auth.session_expired` (la cuenta ya no existe) / 429 | `musubi-app` HU-46 (`Privacidad` → "Eliminar mi cuenta") |

## M-03 · Publicaciones

Con sesión salvo la vitrina y las categorías, que son públicas (HU-48). Las fotos se suben primero y la publicación lleva sus llaves; solo se aceptan llaves que subió el mismo usuario (OWASP API1). Las rutas de imagen (`/media/...`) son relativas a la URL base de la API. Errores del catálogo [`docs/errores.md`](errores.md).

| Método | Ruta | Auth | Spec | Request | Response | Códigos | Consumidor |
|---|---|---|---|---|---|---|---|
| GET | /api/v1/categories | `[AllowAnonymous]` | HU-06 | `?campus=` (visitante; por defecto `uttt`) | `CategoryResponse[] { slug, name }` (del campus del token si hay sesión) | 200 / 404 `listings.unknown_campus` | `musubi-app` HU-06 (`Vender-3`) y "Filtrar" |
| GET | /api/v1/products | `[AllowAnonymous]` | HU-48 fase 2 | `?campus=&category=&hideSoldOut=&cursor=&limit=` (1 a 50; 20 por defecto) | `ProductPage { items: ProductResponse[], nextCursor, total }`; de la más nueva a la más vieja; `seller` es `null` sin sesión (RNF-05) | 200 / 400 `listings.invalid_cursor` / 404 `listings.unknown_campus` | `musubi-app` HU-48 (`Inicio-visitante`, `Inicio`, "Filtrar") |
| GET | /api/v1/products/mine | `[Authorize]` | HU-06 | — | `ProductPage` (las del usuario, también pausadas y agotadas; hasta 50) | 200 / 401 | `musubi-app` HU-06 ("Ver mis productos") |
| GET | /api/v1/products/price-range | `[Authorize]` | HU-06 | `?category=` | `PriceRangeResponse { lowCents, highCents, count }` (percentiles 25 y 75 de las activas de la categoría) | 200 / 204 (menos de 3 publicaciones) / 401 | `musubi-app` HU-06 (`Vender-3`: "Productos parecidos se venden entre…") |
| POST | /api/v1/products | `[Authorize]` | HU-06, HU-50 | `CreateProductRequest { title, description, category, priceCents, condition, conditionDetail, stock, photos[] }` | `ProductResponse { id, title, description, category, priceCents, condition, conditionDetail, stock, status, photos[], seller { name }, publishedAt }` | 201 / 400 `listings.invalid_photos` · `listings.invalid_title` · `listings.invalid_description` · `listings.invalid_price` · `listings.invalid_category` · `listings.invalid_condition` · `listings.invalid_stock` · `validation.failed` / 401 / 403 `auth.forbidden` | `musubi-app` HU-06 (`Vender-4` → `Vender-listo`) |
| POST | /api/v1/products/photos | `[Authorize]` + rate limit `uploads` (30/min por usuario) | HU-06 | `multipart/form-data` con `photo` (JPG, PNG o WebP; hasta 10 MB) | `PhotoResponse { key, path }`; la foto se guarda en WebP de 1600 px como máximo y sin metadatos | 201 / 401 / 413 `listings.photo_too_large` · `request.too_large` / 415 `listings.photo_unsupported` / 422 `listings.photo_too_small` / 429 | `musubi-app` HU-06 (`Vender-1`: "Tomar foto" y "Galería") |

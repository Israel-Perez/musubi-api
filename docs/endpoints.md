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

Total: 12 filas (3 de plataforma, 9 de módulos). Las filas de los módulos M-01…M-14 se agregan con la spec de cada HU.

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

# 02-03 — Contrato de errores, autenticación y correo

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-77 / S2 / M-01 / contraparte: `musubi-app:02-01` (sesión segura, interceptor que renueva el token y traducción por `code`)

## Contexto
El login (HU-01, HU-02, HU-03 y HU-47) necesita un formato de errores que el front pueda traducir, contraseñas guardadas de forma segura, sesiones que no expiren mientras se usen (L-16g) con logout real (L-10), JWT (L-15h), protección contra fuerza bruta y envío del código de verificación. Esta spec deja esa base para que las HU solo agreguen sus endpoints. **Depende de**: `01-02` (pipeline), `01-03` (errores genéricos) y `02-01` (`users`, `refreshTokens`).

## Requisitos
1. **Formato de errores:** cada error sale como `application/problem+json` con `code` estable, `title` en español y `traceId`, sin detalle interno (RNF-18). Esto incluye los errores de validación (400 + `errors`) y los 401, 403, 404, 405 y 429 sin cuerpo. Catálogo canónico: [`docs/errores.md`](../../../docs/errores.md).
2. **Contraseñas:** hash adaptativo PBKDF2-HMAC-SHA512 con al menos 210 000 iteraciones y sal aleatoria (RNF-01). Política de la maqueta: entre 8 y 128 caracteres, con al menos una letra y un número.
3. **Access token:** JWT HS256 de 15 minutos con claims mínimos (`sub`, `community`, `role`, `jti`; sin correo ni nombre). Solo se acepta HS256, del emisor y la audiencia configurados y vigente (±30 s), lo que rechaza `alg=none` y la confusión de algoritmos.
4. **Refresh token:** opaco, de 256 bits y rotativo (60 días desde la última rotación). En la base solo vive su hash. Si se presenta uno ya usado, se revoca toda la familia. El logout revoca la familia de ese dispositivo.
5. **Sesión obligatoria por defecto:** cada endpoint exige sesión salvo `[AllowAnonymous]`, que se aplica a las sondas de salud, al ping y a OpenAPI/Scalar en Development. Una ruta inexistente sin sesión responde 401.
6. **Límite de intentos:** política `auth` por IP (10 por minuto, configurable). Al superarla responde 429 con `Retry-After`.
7. **Correo:** se envía por `IEmailSender`. En Development se escribe en el log; en la nube se usa Amazon SES. Fuera de Development solo se acepta SES con remitente configurado (fail-fast).
8. **Fail-fast al arrancar fuera de Development:** sin `Jwt:SigningKey` (base64 de al menos 32 bytes) la API no arranca. En Development se usa una llave efímera.

## Diseño
- **Errores:** `Platform/Errors/ApiError.cs`.
  - `ApiErrors.Decorate` personaliza cada ProblemDetails (lo usan `AddProblemDetails`, la validación de entrada y `UseStatusCodePages`).
  - Los controllers devuelven `this.Problem(AuthErrors.X)`, sin `try/catch` (critic B4).
- **Autenticación:** `Platform/Auth/`.
  - `JwtOptions` incluye la validación y la llave efímera de Development.
  - `AccessTokenIssuer` usa `JsonWebTokenHandler`.
  - `AddMusubiAuth()` registra JwtBearer con `TokenValidationParameters` estrictos, la política de respaldo y el rate limiter.
  - Toda la configuración se lee de forma lazy (00-04, regla 3).
- **Dominio M-01:** `Modules/Auth/`.
  - `Passwords.cs` contiene la política y `PasswordHashing`, que envuelve `PasswordHasher<T>` de ASP.NET Core Identity con `IterationCount = 210 000`.
  - `RefreshTokenService` hace la rotación con `TryConsumeAsync` atómico (02-01, R7).
  - `AuthErrors` es el catálogo de M-01.
- **Correo:** `Platform/Email/`.
  - `IEmailSender`, `LogEmailSender` (solo en Development) y `SesEmailSender` sobre `ISesClient`, una costura mínima que permite probar sin AWS.
  - `EmailOptionsValidator` hace el fail-fast.
  - El cliente de SES se crea solo con `Provider=Ses` y toma las credenciales del rol de la instancia (02-05).
- **Pipeline (01-02):** ForwardedHeaders → ExceptionHandler + HSTS (fuera de Dev) → **StatusCodePages** → HttpsRedirection → CORS → OpenAPI/Scalar (Dev, anónimos) → **Authentication** → **RateLimiter** → Authorization → Controllers → Health (anónimos).

## Contratos
- Errores: [`docs/errores.md`](../../../docs/errores.md) y `ContractTest/Fixtures/problem.json`.
- Endpoints: sin filas nuevas. Login, registro, refresh y logout nacen con HU-01, HU-02 y HU-03.
- Configuración (ver 01-04):
  - `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenMinutes` y `Jwt:RefreshTokenDays`;
  - `Jwt:SigningKey` (secreto);
  - `Email:Provider`, `Email:From` y `Email:Region`;
  - `RateLimiting:Auth:PermitLimit` y `RateLimiting:Auth:WindowSeconds`.

## Tests
- `SecurityTest/Auth/JwtTests.cs` (10):
  - política de respaldo (401 con `code`);
  - token válido con claims mínimos;
  - 403 por rol;
  - rechazo de tokens vencidos, firmados con otra llave, `alg=none`, HS512 con la misma llave, y de otro emisor o audiencia;
  - las sondas de salud siguen siendo anónimas.
- `SecurityTest/Auth/RateLimitTests.cs` (1): 429 con `Retry-After` y `rate_limit.exceeded`.
- `SecurityTest/Errors/ErrorContractTests.cs` (3): validación (400 + `errors`), error de negocio con su propio código y 500 genérico; nunca `detail`.
- `UnitTest/Auth/PasswordTests.cs` (10): política y formato del hash (PBKDF2-SHA512, 210 000 iteraciones, sal).
- `UnitTest/Auth/SessionTests.cs` (10):
  - duración del access token;
  - solo se guarda el hash;
  - rotación con vencimiento deslizante;
  - la reutilización revoca la familia;
  - token vencido o desconocido;
  - logout por dispositivo;
  - User-Agent recortado;
  - llave obligatoria fuera de Development.
- `UnitTest/Auth/EmailTests.cs` (13): el proveedor Log solo se acepta en Development; mapeo a SES; códigos genéricos por estado.
- `IntegrationTest/Middleware/MiddlewareTests.cs` (+1): ruta inexistente → 401 sin sesión y 404 con sesión.
- `ContractTest/ProblemContractTests.cs` (1): forma del problem+json.

## Criterios
- `dotnet build Musubi.slnx -c Release` → 0/0.
- Unit 80/80, Integration 9/9, Security 25/25, Contract 3/3, Database 18/18 (CI).
- Self-test: si se acepta HS512 y la política de respaldo deja pasar a todos, fallan 4 pruebas (3 de seguridad y 1 de integración) (21-Oct-2026).
- `pwsh scripts/critic-guardrails.ps1` → PASS. Semgrep → 0.

## Límites
- El rate limit parte por IP. Detrás del ALB necesita `ForwardedHeaders` con `KnownProxies`, o todos compartirían la IP del balanceador (`NOTE (02-05)`).
- `LogEmailSender` escribe el destinatario y el código en el log: solo en Development, y el validador lo impide en los demás ambientes.
- SES empieza en *sandbox* (solo envía a correos verificados). Pedir el acceso de producción y verificar el remitente o dominio es tarea de `02-05`.
- La llave es HS256 compartida por las instancias, guardada en SSM Parameter Store. Rotarla invalida todos los access tokens (15 min como máximo); los refresh tokens no dependen de ella.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 21-Oct-2026
- **Detalle:** Paquetes nuevos (licencia, publicación ≥ 7 días):
  - Microsoft.AspNetCore.Authentication.JwtBearer 10.0.13 (MIT);
  - AWSSDK.SimpleEmailV2 4.0.106.4 (Apache-2.0).

  Política de contraseña tomada de la maqueta (manda sobre la propuesta D-02).

# 02-03 — Contrato de errores, autenticación y correo (tareas)

## T1 — Contrato de errores
- **Crear:** `Platform/Errors/{ApiError,ApiProblemExtensions}.cs`, `docs/errores.md`, `ContractTest/Fixtures/problem.json`.
- **Verificar:** `SecurityTest/Errors/ErrorContractTests.cs` y `ContractTest/ProblemContractTests.cs`.
- **Estado:** ✅ Hecho (21-Oct-2026).

## T2 — JWT, política de respaldo y rate limit
- **Crear:** `Platform/Auth/{JwtOptions,AccessTokenIssuer,AuthPlatformServiceCollectionExtensions}.cs`.
- **Verificar:** `SecurityTest/Auth/{JwtTests,RateLimitTests}.cs` y self-test de mutación (4 pruebas en rojo).
- **Estado:** ✅ Hecho (21-Oct-2026).

## T3 — Contraseñas y refresh tokens
- **Crear:** `Modules/Auth/{AuthErrors,Passwords,RefreshTokenService}.cs`.
- **Verificar:** `UnitTest/Auth/{PasswordTests,SessionTests}.cs`.
- **Estado:** ✅ Hecho (21-Oct-2026).

## T4 — Correo
- **Crear:** `Platform/Email/{Email,EmailServiceCollectionExtensions}.cs`.
- **Verificar:** `UnitTest/Auth/EmailTests.cs`.
- **Estado:** ✅ Hecho (21-Oct-2026).

## Pendientes
- `NOTE (HU-01)`, `NOTE (HU-02)`, `NOTE (HU-03)`: endpoints de registro, login, refresh, logout y verificación sobre estos servicios.
- `NOTE (02-05)`:
  - `KnownProxies` del ALB, para que el rate limit vea la IP real;
  - `Jwt__SigningKey` y `Email__From` en SSM;
  - acceso de producción de SES y verificación del remitente o dominio.
- `NOTE (musubi-app:02-01)`: guardar los tokens en almacenamiento seguro, renovar el token ante un 401 y traducir los errores por `code`.

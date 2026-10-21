# HU-02 — Iniciar y cerrar sesión

**HU / Jira / Sprint / Módulo / Contraparte:** HU-02 / SCRUM-20 / S2 / M-01 / [musubi-app:HU-02](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m01-auth/HU-02-iniciar-cerrar-sesion)

## Contexto
El estudiante entra con su correo institucional y su contraseña (maqueta `Iniciar sesion`) y sale desde Perfil. La sesión no caduca mientras use la app (L-16g): el access token dura 15 min y se renueva con el refresh token rotativo de `02-03`. **Depende de**: `02-03`, `HU-01` y `HU-03`.

## Requisitos
1. **Login:** correo (sin distinguir mayúsculas) y contraseña. La respuesta es la misma `SessionResponse` que la de `verification/confirm`.
2. **Sin enumeración (OWASP API2):** un correo inexistente y una contraseña equivocada responden lo mismo (`auth.invalid_credentials`, 401). Si el correo no existe, se verifica igual contra un hash señuelo para que el tiempo de respuesta no lo delate.
3. **Estado de la cuenta:** solo **después** de una contraseña correcta se dice por qué no puede entrar: `auth.email_not_verified` (pendiente, la app lo lleva a verificar) o `auth.account_suspended` (HU-40).
4. **Rehash:** si el hash guardado usa parámetros viejos, se recalcula al entrar.
5. **Renovar:** `refresh` rota el token en cada uso. Si se reutiliza un token ya rotado, se revoca toda la familia (posible robo). Un token vencido, revocado o desconocido, o una cuenta borrada o suspendida, responde `auth.session_expired`.
6. **Cerrar sesión (L-10):** revoca solo la sesión de ese dispositivo. Es idempotente: un token desconocido también responde 204.
7. Los tres endpoints son públicos (aún no hay un access token válido) y llevan el rate limit `auth` por IP.

## Diseño
- `LoginService` (`LoginAsync`, `RefreshAsync`, `LogoutAsync`) y `SessionController`.
- `SessionIssuer` arma la `SessionResponse` (access JWT + refresh token + usuario con campus) y la comparten registro y login.
- `PasswordHashing.VerifyAgainstDecoy` cubre el correo inexistente.
- `IUserRepository.UpdatePasswordHashAsync` (Mongo + InMemory, misma batería de `TestShared/`) para el rehash.
- S6960 obligó a separar los controllers por responsabilidad: `AuthController` (registro), `SessionController` (sesión) y `PasswordController` (HU-56). El helper `this.Respond(result, status)` vive en `Platform/Errors/ApiProblemExtensions.cs`.

## Contratos
- Filas en [`docs/endpoints.md`](../../../docs/endpoints.md): `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh` y `POST /api/v1/auth/logout`.
- Fixture: `ContractTest/Fixtures/session.json` (login y refresh tienen la misma forma que la confirmación).
- Errores: `auth.invalid_credentials`, `auth.email_not_verified`, `auth.account_suspended` y `auth.session_expired` en [`docs/errores.md`](../../../docs/errores.md).

## Tests
- `IntegrationTest/Auth/SessionFlowTests.cs` (9):
  - login de una cuenta activa (correo en mayúsculas, `sub` del JWT = id del usuario);
  - contraseña equivocada igual a correo inexistente;
  - cuenta pendiente: `email_not_verified` solo con la contraseña correcta;
  - cuenta suspendida;
  - falta la contraseña → `validation.failed`;
  - refresh rota el token y la reutilización revoca la familia;
  - token desconocido;
  - cuenta borrada después del login;
  - el logout cierra solo ese dispositivo y es idempotente.
- `ContractTest/AuthContractTests.cs`: `LoginAndRefresh_ReturnTheSameSessionShape`.
- `TestShared/Auth/AuthRepositoryContract.cs`: `Users_UpdatePasswordHash_ReplacesOnlyTheHash` (InMemory en UnitTest; Mongo en DatabaseTest).
- `SecurityTest/Auth/ExplicitAuthTests.cs` ya cubre los controllers nuevos (auth explícita).
- Self-test (21-Oct-2026): si `email_not_verified` se revela con la contraseña equivocada, falla `Login_PendingUser_IsAskedToVerifyOnlyWithTheRightPassword`.

## Criterios
- Build Release 0/0 · Unit 91/91 · Integration 46/46 · Security 28/28 · Contract 5/5.
- `check_endpoints`: 12 rutas con fila · critic PASS · Semgrep 0 hallazgos.

## Límites
- El señuelo iguala el costo del hash, no cada microsegundo (la búsqueda en la base sigue ahí). El rate limit por IP acota la medición.
- Ver y cerrar las sesiones de otros dispositivos no entra en esta HU.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 21-Oct-2026
- **Detalle:** Sprint movido de S1 a S2 (sprint activo en Jira).

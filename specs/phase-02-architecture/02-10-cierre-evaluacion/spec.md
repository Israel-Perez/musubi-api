# 02-10 — Cierre de brechas de la evaluación (Prompts para evaluar v1)

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-120 / S5 / Plataforma / [musubi-app:02-10](https://github.com/Israel-Perez/musubi-app/tree/main/specs/phase-02-architecture/02-10-cierre-evaluacion)

## Contexto
El documento "Prompts para evaluar v1" (16-Nov-2026) de la materia pregunta, entre otras cosas:

- si el JWT se maneja con Redis o algo similar;
- si hay compresión de la respuesta;
- qué endpoints no tienen prueba unitaria, de seguridad o de integración.

La revisión del 17-Nov-2026 encontró tres cosas:

- El access token solo vencía a los 15 minutos. Un logout o una suspensión revocaban el refresh token, pero no el access token que ya circulaba.
- Solo CloudFront comprimía.
- 13 filas de `docs/endpoints.md` no tenían alguna de las tres pruebas, y nada impedía que una HU nueva entrara sin ellas.

**Depende de:** `02-02` (Redis), `02-03` (sesión y JWT), `02-04` (compresión) y `02-08` (CI).

## Requisitos
1. **Lista de revocación en Redis.** Cada petición con sesión consulta en Redis si su `jti` está revocado o si su emisión (`iat_ms`) es anterior al "revocado desde" de la persona. Se alimenta en estos casos:
   - logout del dispositivo (su `jti`);
   - suspensión (HU-40);
   - cambio y recuperación de contraseña (HU-04, HU-56);
   - cuenta eliminada (HU-46);
   - refresh token reutilizado (por la persona).
2. Las entradas viven lo que un access token (15 min + tolerancia). Si Redis no responde, el token se acepta (RNF-15), porque el refresh token ya se revocó en MongoDB.
3. **Compresión** Brotli o Gzip según `Accept-Encoding`, también en `problem+json`, con el nivel `Compression:Level` (por defecto `Optimal`, medido abajo). Nunca se comprime `/api/v1/auth/*` (BREACH), sin importar el esquema de la petición.
4. **Cada fila con sus tres pruebas.**
   - Integración y seguridad: las suites registran los endpoints que tocan (evento `EndpointMatched`) y `scripts/check_endpoint_tests.ps1` compara esa lista con `docs/endpoints.md` en el CI.
   - Unitaria: la HU de la fila debe tener una ruta `UnitTest/` en `docs/trazabilidad.md`.
   - Las excepciones se documentan en el script con su motivo.

## Diseño
- `Platform/Auth/AccessTokenRevocations.cs`: `IAccessTokenRevocations` sobre `IDistributedCache`, con las llaves `auth:revoked:jti:{jti}` y `auth:revoked:user:{id}` (milisegundos). Lo consulta `JwtBearerEvents.OnTokenValidated`, encadenado al evento del hub.
- `AccessTokenIssuer` agrega `iat_ms`, porque `iat` solo tiene segundos. Un token emitido en el mismo instante que la revocación sigue valido: es la sesión nueva del cambio de contraseña.
- `Platform/Hosting/PayloadCompression.cs`: `SessionSafeCompressionProvider` excluye `/auth`. `IHttpsCompressionFeature` solo aplica a HTTPS, y detrás del balanceador la petición puede llegar por HTTP.
- `IntegrationTest/Common/EndpointCoverage.cs` (enlazado en SecurityTest) escribe `TestResults/endpoint-coverage/<Suite>.txt`.

## Medición del nivel (17-Nov-2026)
Se midió con la API local (Development, en memoria): una página de 20 publicaciones de la vitrina (`GET /api/v1/products?campus=uttt&limit=20`), el mejor de 15 intentos por nivel.

| Nivel | Sin comprimir | Gzip | Brotli | Tiempo con Brotli |
|---|---|---|---|---|
| `Fastest` | 9 526 B | 1 932 B | 1 497 B | 2.8 ms |
| `Optimal` | 9 526 B | 1 405 B | 1 323 B | 2.5 ms |
| `SmallestSize` | 9 525 B | 1 407 B | 1 164 B | 20.4 ms |

- `Optimal` reduce la página 86 % sin costo medible.
- `SmallestSize` ahorra 159 B más, pero agrega unos 18 ms por respuesta.
- Por eso el nivel por defecto es `Optimal`.

## Contratos
- Sin filas nuevas. `POST /auth/logout` también revoca el access token de `Authorization` si viene.
- El 401 por revocación es el mismo `401` genérico, sin decir por qué.
- Configuración: `Compression:Level` (`01-04`).

## Tests
- **UnitTest:**
  - `Auth/AccessTokenRevocationTests.cs`;
  - `Hosting/PayloadCompressionTests.cs`;
  - `Admin/ModerationServiceTests.cs` (HU-39/40);
  - `Listings/PopularProductsTests.cs` (HU-49);
  - `Auth/PasswordRecoveryTests.cs` (HU-56).
- **IntegrationTest:**
  - `Auth/TokenRevocationFlowTests.cs`;
  - `Admin/ModerationFlowTests.cs` (la suspensión corta el access token);
  - `Orders/ReservationFlowTests.cs` (`GET /reservations/{id}`);
  - `Orders/PaymentFlowTests.cs` (bancos y quitar cuenta).
- **SecurityTest:**
  - `Auth/TokenRevocationTests.cs`;
  - `Auth/AuthRoutesSecurityTests.cs` (las 9 rutas anónimas de auth en su ruta real);
  - `Hosting/PayloadCompressionTests.cs`.

## Criterios
- [x] El access token se revoca en logout, suspensión, contraseña, cuenta eliminada y reutilización.
- [x] Las respuestas se comprimen, salvo `/auth`.
- [x] `check_endpoint_tests.ps1` da OK en UnitTest, IntegrationTest y SecurityTest (125 filas; 1 excepción documentada: `/media` solo en Development).
- [x] Build 0/0, pruebas al 100 %, `check_endpoints` y critic PASS.

## Límites
- La revocación cubre los access tokens de los últimos 15 minutos; los refresh tokens se revocan en MongoDB (02-03).
- La compresión en la nube la hace CloudFront. La de la API aplica cuando el cliente llega directo (el ALB o un ambiente local).

## Aprobación y Control de Cambios
- **Estado:** Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 17-Nov-2026
- **Detalle:** cierra los puntos 2, 4 y 5 de la revisión contra "Prompts para evaluar v1". Los puntos 1 y 3 (app) están en `musubi-app:02-10`.

# HU-46 — Eliminar mi cuenta y mis datos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-46 / SCRUM-32 / S3 / M-01 / [musubi-app:HU-46](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m01-auth/HU-46-eliminar-cuenta)

## Contexto
Jira (SCRUM-32): "Como **usuario**, quiero **eliminar mi cuenta y mis datos desde la aplicación o desde una página web**, para **tener control sobre mi información personal**."

- Maqueta: `Privacidad` ("Eliminar mi cuenta" y su diálogo) y `Aviso-privacidad`.
- Trazabilidad: RNF-08, Entregable 1 §12 y LFPDPPP.
- Google Play exige las dos vías antes de publicar en producción: dentro de la app y desde una página web.
- La página web ya existe: `https://musubiapp.com.mx/eliminar-cuenta` (repo `musubi-landing`, SCRUM-84). Esta spec cubre el borrado desde la app.

**Depende de:** `02-01`, `02-03`, `HU-01`, `HU-02` y `HU-06` (publicaciones y fotos que también se borran).

## Requisitos
1. **Borrado desde la app:** quien tiene sesión borra **su** cuenta. La ruta no lleva id (OWASP API1).
2. **Confirmación con la contraseña actual:** con una sesión robada no se puede borrar la cuenta. Si no coincide, responde 400 `auth.wrong_password` y no toca nada. El endpoint tiene el rate limit de `auth`.
3. **Borrado real (R11, RNF-08)**, sin borrado lógico:
   - cuenta (`users`), sesiones (`refreshTokens`) y códigos (`verificationCodes`);
   - publicaciones (`products`, en cualquier estado);
   - fotos en S3: todo `media/products/{userId}/`, también las que se subieron y nunca se publicaron.
4. **Orden seguro:** primero los módulos, después códigos y sesiones, y la cuenta al final. Si algo falla a medias, la cuenta sigue viva y la persona puede reintentar.
5. **Módulos independientes:** M-01 no lee las colecciones de otros módulos. Cada módulo registra un `IAccountDataEraser`; Listings es el primero.
6. **El correo queda libre:** después de borrar se puede volver a registrar.
7. **Congruente con el aviso de privacidad y con `/eliminar-cuenta`:** se borran los datos personales. Lo que deba conservarse (pedidos y calificaciones, cuando existan) se anonimiza.

## Diseño
- **`Modules/Auth/AccountController.cs`:** `POST /api/v1/account/delete`, con `[Authorize]` y el rate limit `auth`.
- **`Modules/Auth/AccountDeletionService.cs`:** verifica la contraseña (con el mismo costo cuando la cuenta ya no existe), recorre los `IAccountDataEraser` y luego borra códigos, sesiones y usuario. Registra el evento solo con el id interno (RNF-05).
- **`Modules/Listings/ListingAccountEraser.cs`:** `IProductRepository.DeleteBySellerAsync` y `IMediaStorage.DeletePrefixAsync(ListingService.PhotoPrefix(userId))`.
- **Repositorios** (`Mongo*` e `InMemory*`, con la misma batería de `TestShared/`):
  - `IVerificationCodeRepository.DeleteAllForUserAsync`;
  - `IRefreshTokenRepository.DeleteAllForUserAsync`;
  - `IProductRepository.DeleteBySellerAsync`.
- **`Platform/Media`:** `DeletePrefixAsync`. En S3, `ListObjectsV2` + `DeleteObjects` por páginas de 1000.
- **Infraestructura** (`iam.tf`): `s3:ListBucket` limitado al prefijo `media/products/*`. Con este permiso S3 responde 404 (y no 403) a una foto que no existe. Infra `0.3.1`.
- **Pendientes:**
  - `NOTE (HU-04)`: perfil y foto;
  - `NOTE (HU-20)`: conversaciones;
  - `NOTE (HU-23)`: pedidos y calificaciones se anonimizan.
  Cada módulo agrega su `IAccountDataEraser`.

## Contratos
- **Fila** `POST /api/v1/account/delete` en [`docs/endpoints.md`](../../../docs/endpoints.md).
- **Error nuevo** `auth.wrong_password` (400) en [`docs/errores.md`](../../../docs/errores.md).
- **Sin fixture nuevo:** responde 204 sin cuerpo, y los errores usan la forma `problem+json` ya cubierta.

## Tests
- `IntegrationTest/Auth/AccountDeletionFlowTests.cs` (3):
  - con la contraseña equivocada responde 400 y la cuenta sigue;
  - el borrado completo: login 401, refresh 401, la publicación fuera de la vitrina, la foto en 404 y el correo registrable otra vez;
  - sin contraseña responde `validation.failed`.
- `UnitTest/Auth/AccountDeletionTests.cs` (5):
  - el orden módulos → sesiones → usuario;
  - con la contraseña equivocada no toca nada;
  - si falla un módulo, la cuenta sigue y se puede reintentar;
  - si la cuenta ya no existe responde `auth.session_expired`;
  - `DeletePrefixAsync` en memoria.
- `TestShared/` (+3, en memoria y en MongoDB real):
  - `Codes_DeleteAllForUser_LeavesOtherUsers`;
  - `Tokens_DeleteAllForUser_RemovesOnlyThatUser`;
  - `DeleteBySeller_RemovesEveryStatusOnlyForThatSeller`.
- `SecurityTest/Auth/AccountAuthTests.cs` (2): 401 sin sesión, y no hay ruta que borre por id.

## Criterios
- [x] Existe "Eliminar cuenta" dentro de la app (contraparte `musubi-app`) y su endpoint borra la cuenta y sus datos.
- [x] Existe la página web pública `https://musubiapp.com.mx/eliminar-cuenta` (SCRUM-84).
- [x] Se informa qué se borra y qué se conserva, y en qué plazo: la página web y el diálogo de la app.
- [x] Es congruente con el aviso de privacidad (borrado real y anonimización de lo que deba conservarse).
- [x] Build Release 0/0 · Unit 112 · Integration 57 · Security 52 · Contract 6 · `check_endpoints` OK (20) · critic PASS.

## Límites
- **El access token** (15 minutos) sigue siendo un JWT válido hasta vencer, pero ya no sirve: la cuenta no existe, el refresh falla y las acciones que buscan al miembro no lo encuentran.
- **Las solicitudes por correo** (`/eliminar-cuenta`) las atiende el equipo a mano en ≤ 20 días hábiles. `NOTE (HU-40)`: herramienta de administración.
- **Descargar mis datos** (maqueta `Privacidad`) no está en los criterios de Jira; queda como función por planear.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 30-Oct-2026
- **Detalle:** se pide la contraseña actual antes de borrar (agregado de seguridad sobre la maqueta, permitido por el lineamiento 4). Prioridad por el requisito de Google Play.

# HU-04 — Editar mi perfil (tareas)

## T1 — Datos
- **Crear:**
  - `db/004-perfiles.js`: `profiles`, `careers`, `communities.academicTerm` y la semilla de la UTTT;
  - `Modules/Profiles/Data/*`.
- **Cambiar:** `MongoSchema.ExpectedVersion = 4`.
- **Verificar:** `ProfileRepositoryContract` (en memoria y Mongo) y `SchemaScriptTests` (+1).
- **Estado:** ✅ Hecho (03-Nov-2026). Contrato en memoria: 8/8. En Mongo corre en el CI (`DatabaseTest`).

## T2 — Cuenta y fotos
- **Cambiar:**
  - `IMemberDirectory` → `MemberAccount`;
  - `PhotoProcessor.TryProcessSquare`;
  - `IMediaStorage.DeleteAsync`;
  - `PhotoUploadService.ReadAsync` (compartido);
  - `ProfileAccountEraser` y `media/avatars/*` en `infra/stack/iam.tf` (HU-46).
- **Verificar:** `PhotoProcessorTests` (+2).
- **Estado:** ✅ Hecho (03-Nov-2026).

## T3 — Perfil, visibilidad, foto y carreras
- **Crear:** `ProfileService`, `ProfilePhotoService`, `ProfilesController`, `CareersController`, `ProfilesErrors` y `ProfilesDtos`.
- **Verificar:** `ProfileServiceTests`, `ProfileFlowTests`, `ProfilesAuthTests` y `ProfilesContractTests`.
- **Estado:** ✅ Hecho (03-Nov-2026).

## T4 — Cambiar contraseña
- **Crear:** `PasswordChangeService` y `PasswordChangeController` (`POST /auth/password/change`).
- **Verificar:** `PasswordChangeTests`.
- **Estado:** ✅ Hecho (03-Nov-2026).

## T5 — Documentación
- **Actualizar:**
  - `docs/endpoints.md`: 8 filas, sección M-02;
  - `docs/errores.md`;
  - `docs/modelo-datos.md`;
  - `docs/versionado.md` (`0.4.0-alpha.1`, infra `0.3.2`; la misma fila va en `musubi-app`);
  - `docs/trazabilidad.md`;
  - `Memoria.md`;
  - el conteo de filas en `AGENTS.md`.
- **Verificar:** `check_endpoints` OK y `VersioningTests`.
- **Estado:** ✅ Hecho (03-Nov-2026): `check_endpoints` OK (28).

## T6 — Despliegue
- **Desplegar:** primero el script `004` en Atlas, después `infra-up` con `api-v0.4.0-alpha.1` (infra `0.3.2`). Requiere la autorización del usuario.
- **Probar desde el celular:** editar, foto, privacidad y contraseña.
- **Estado:** ⏳ Por hacer.

## Pendientes
- `NOTE (HU-27)`: reputación, promedio y opiniones en `GET /profiles/{id}`; las publicaciones activas las entrega HU-05.
- `NOTE (HU-20)`: aplicar `visibility.online`.
- `NOTE (HU-23)`/`NOTE (HU-27)`: actualizar `profiles.stats`.
- `NOTE (02-07)`: moderar la foto.
- `NOTE (HU-41)`: administrar las carreras y el periodo del campus.
- **Validar con el equipo** la lista de carreras (`plan.md` §4).

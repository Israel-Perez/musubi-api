# HU-04 — Editar mi perfil (tareas)

## T1 — Datos
- **Crear:**
  - `db/003-perfiles.js`: `profiles`, `careers`, `communities.academicTerm` y la semilla de la UTTT;
  - `Modules/Profiles/Data/*`.
- **Cambiar:** `MongoSchema.ExpectedVersion = 3`.
- **Verificar:** `ProfileRepositoryContract` (en memoria y Mongo) y `SchemaScriptTests` (+1).
- **Estado:** ⏳ Por hacer.

## T2 — Cuenta y fotos
- **Cambiar:**
  - `IMemberDirectory` → `MemberAccount`;
  - `PhotoProcessor.TryProcessSquare`;
  - `IMediaStorage.DeleteAsync`.
- **Verificar:** `PhotoProcessorTests` (+2).
- **Estado:** ⏳ Por hacer.

## T3 — Perfil, visibilidad, foto y carreras
- **Crear:** `ProfileService`, `ProfilePhotoService`, `ProfilesController`, `CareersController`, `ProfilesErrors` y `ProfilesDtos`.
- **Verificar:** `ProfileServiceTests`, `ProfileFlowTests`, `ProfilesAuthTests` y `ProfilesContractTests`.
- **Estado:** ⏳ Por hacer.

## T4 — Cambiar contraseña
- **Crear:** `PasswordChangeService` y la acción `POST /auth/password/change`.
- **Verificar:** `PasswordChangeTests`.
- **Estado:** ⏳ Por hacer.

## T5 — Documentación
- **Actualizar:**
  - `docs/endpoints.md`: 8 filas, sección M-02;
  - `docs/errores.md`;
  - `docs/modelo-datos.md`;
  - `docs/versionado.md` (`0.4.0-alpha.1`);
  - `docs/trazabilidad.md`;
  - `Memoria.md`;
  - el conteo de filas en `AGENTS.md`.
- **Verificar:** `check_endpoints` OK y `VersioningTests`.
- **Estado:** ⏳ Por hacer.

## T6 — Despliegue
- **Desplegar:** `infra-up` con `api-v0.4.0-alpha.1` y el script `003` en Atlas. Requiere la autorización del usuario.
- **Probar desde el celular:** editar, foto, privacidad y contraseña.
- **Estado:** ⏳ Por hacer.

## Pendientes
- `NOTE (HU-05)`: reputación, publicaciones y opiniones en `GET /profiles/{id}`.
- `NOTE (HU-20)`: aplicar `visibility.online`.
- `NOTE (HU-23)`/`NOTE (HU-27)`: actualizar `profiles.stats`.
- `NOTE (HU-46)`: borrar el perfil y la foto con la cuenta.
- `NOTE (02-07)`: moderar la foto.
- `NOTE (HU-41)`: administrar las carreras y el periodo del campus.
- **Validar con el equipo** la lista de carreras (`plan.md` §4).

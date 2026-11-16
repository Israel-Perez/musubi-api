# HU-40 — Suspender y reactivar cuentas

**HU / Jira / Sprint / Módulo / Contraparte:** HU-40 / SCRUM-63 / Sprint 5 / M-14 / [musubi-app:HU-40](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m14-admin/HU-40-suspender-cuentas)

## Contexto
Jira (SCRUM-63): Como **administrador**, quiero **suspender cuentas que incumplan las reglas**, para **proteger a la comunidad**.

- Maqueta: sin pantalla propia (lineamiento 4). Trazabilidad: RF-55 · CU-25 · RN-27 · RNF-04. Origen: Propuesta inicial §11.
- Va con HU-39: el diseño, las decisiones y la secuencia están en [`../HU-39-moderar-contenido/plan.md`](../HU-39-moderar-contenido/plan.md).
- **Depende de:** `HU-02` (inicio de sesión: ya rechazaba `suspended` con `auth.account_suspended`), `02-03` (sesiones), `HU-39`.

## Requisitos
1. El administrador suspende una cuenta activa de su campus con un motivo (1 a 500 caracteres) y la reactiva después.
2. Una cuenta suspendida no inicia sesión (`auth.account_suspended`) ni renueva la que tenía: se cierran todas sus sesiones. El access token vigente dura hasta 15 minutos, pero el chat y los demás módulos ya la ven inactiva.
3. Sus publicaciones disponibles y agotadas se pausan. Al reactivarla siguen pausadas: su dueño las reactiva desde "Mis productos" (HU-07).
4. RN-27: con 3 reportes procedentes o más (HU-39), la cuenta aparece con "se sugiere suspender"; el administrador decide.
5. No se puede suspender la cuenta propia ni la de otro administrador.
6. Cada suspensión y reactivación queda en la bitácora con quién, cuándo y el motivo (RNF-04), y se ve en el historial de la cuenta.

## Diseño
- `AdminAccountsController` y `ModerationService` (módulo M-14 de HU-39).
- M-01 `IAccountModeration`: `ChangeStatusAsync` atómico (`active` → `suspended` y al revés) y `RevokeAllForUserAsync` de los refresh tokens.
- M-03 `IProductModeration.HideAllAsync`: `hidden` para `active` y `soldOut` de quien vende.
- Sin cambios en `users`: `db/001` ya admitía `suspended`. `FindByStatusAsync` usa `ix_community_status`.

## Contratos
- [`GET /api/v1/admin/accounts/suspended`](../../../docs/endpoints.md), [`GET /api/v1/admin/accounts/{id}`](../../../docs/endpoints.md), [`POST` y `DELETE /api/v1/admin/accounts/{id}/suspension`](../../../docs/endpoints.md).
- Errores `admin.account_*`, `admin.invalid_account` y `admin.invalid_reason` en [`docs/errores.md`](../../../docs/errores.md); fixtures `admin-account.json` y `admin-account-page.json`.

## Tests
- `TestShared/Auth/AuthRepositoryContract.cs`: cambio de estado solo desde el esperado, suspendidas del campus y agregar un rol.
- `TestShared/Listings/ListingRepositoryContract.cs`: pausar solo lo disponible y lo agotado de quien vende.
- `IntegrationTest/Admin/ModerationFlowTests.cs`:
  - 3 procedentes sugieren suspender.
  - La suspensión cierra el inicio de sesión y la renovación, y pausa sus publicaciones.
  - Reactivar la deja entrar otra vez.
  - Validaciones: motivo, cuenta propia y de otro administrador.
- `SecurityTest/Auth/AdminAuthTests.cs`: las cuatro rutas con 401 y 403, y otro campus es 404 sin suspender.
- `ContractTest/AdminContractTests.cs`: formas de la cuenta y de las suspendidas.

## Criterios
- [x] El administrador puede suspender y reactivar cuentas: `ModerationFlowTests.ThreeUpheldReports_SuggestSuspending_AndSuspensionClosesSessionsAndPausesListings`.
- [x] Una cuenta con 3 reportes procedentes puede ser suspendida (RN-27, `suspensionSuggested`): el mismo.
- [x] Las suspensiones quedan registradas (RNF-04): `account_suspended` y `account_reactivated` en el historial, con quién y el motivo.
- [x] Build Release 0/0; Unit 848, Integration 199, Security 288 y Contract 29 en verde; `check_endpoints` OK (122); critic PASS.

## Límites
- La suspensión no tiene fecha de fin: dura hasta que un administrador la reactiva.
- Sus solicitudes "Se busca" (HU-13) siguen en el tablero hasta que vencen.
- Sus pedidos abiertos siguen su curso.
- No se le avisa por push ni por correo: al intentar entrar ve "Tu cuenta está suspendida. Escríbenos si crees que es un error."

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 revisor distinto del autor)
- **Fecha:** 16-Nov-2026
- **Detalle:** el "[POR DEFINIR]" de RN-27 se resolvió como sugerencia con 3 procedentes (decisión para revisar). API `0.5.0-alpha.38`.

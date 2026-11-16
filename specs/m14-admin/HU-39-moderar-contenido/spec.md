# HU-39 — Revisar reportes y moderar contenido

**HU / Jira / Sprint / Módulo / Contraparte:** HU-39 / SCRUM-62 / Sprint 5 / M-14 / [musubi-app:HU-39](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m14-admin/HU-39-moderar-contenido)

## Contexto
Jira (SCRUM-62): Como **administrador**, quiero **revisar los reportes**, para **retirar contenido falso o inapropiado**.

- Maqueta: sin pantalla propia. La app la arma con la guía de estilo de la maqueta (lineamiento 4: se agrega, no se quita).
- Trazabilidad: RF-54 · CU-24 · RNF-03, RNF-04. Origen: Propuesta inicial §11.
- Va junto con HU-40 (suspender cuentas): misma bandeja, mismo módulo y mismo PR.
- **Depende de:** `HU-28` (los reportes, `db/008`), `HU-06`/`HU-07` (el estado `removed` de una publicación), `HU-31` (avisos), `02-03` (roles en el JWT).

## Requisitos
1. El administrador ve los reportes de **su** campus, abiertos o revisados, con el motivo, el detalle, quién reportó, lo reportado (publicación con nombre, foto y estado, o persona con nombre y estado) y la persona responsable con sus reportes procedentes.
2. Puede **descartar** un reporte (no hay nada que retirar) o marcarlo **procedente**; con una publicación, "procedente" la **retira** (`removed`: deja de verse en la vitrina, la búsqueda, el detalle y el perfil).
3. Un reporte se revisa una sola vez, también con dos administradores a la vez.
4. Solo el rol `admin` modera (RNF-03), y la base manda: un token con el rol de una cuenta que ya no lo tiene, o suspendida, no modera.
5. Cada acción queda en la bitácora `moderationLog` (RNF-04): quién, qué, sobre qué, a quién, la nota y cuándo.
6. Quien reportó recibe "Revisamos tu reporte" y quien vende "Retiramos tu publicación" (push, sin historial).
7. El rol `admin` se da con `Admin:Emails` (al iniciar sesión) o en la base (`db/README.md`), y la sesión lo expone en `user.roles` para que la app muestre "Administración".

## Diseño
- Módulo nuevo `Musubi.Api/Modules/Admin/` (M-14): `AdminReportsController`, `AdminAccountsController` (HU-40), `ModerationService`, `AdminDtos`, `AdminErrors` y `Data/ModerationLog.cs` (`moderationLog`, Mongo e InMemory).
- Contratos de los otros módulos (Admin no lee sus colecciones):
  - M-13 `IReportInbox`: bandeja, cerrar (atómico) y procedentes por persona.
  - M-03 `IProductModeration`: buscar, retirar y pausar todo lo de quien vende (HU-40).
  - M-01 `IAccountModeration`: cuentas, suspender y reactivar (HU-40).
- `reports` agrega `subjectId` (la persona responsable, guardada al crear el reporte y al revisarlo), `reviewedBy`, `reviewedAt` y `reviewNote`. Los reportes anteriores sin `subjectId` lo calculan al mostrarse.
- Orden de "Procedente" con una publicación: primero se cierra el reporte (atómico) y solo quien lo cerró retira la publicación.
- `db/030-moderacion.js`: estados de la revisión, `ix_subject_status`, `moderationLog` con `ix_subject_at` e `ix_actor_at`, y `report_reviewed`/`product_removed` en el validador de `notifications`. Esquema 30.

## Contratos
- [`GET /api/v1/admin/reports`](../../../docs/endpoints.md) y [`POST /api/v1/admin/reports/{id}/resolution`](../../../docs/endpoints.md) (filas de M-14).
- `user.roles` en las respuestas de sesión de M-01.
- Errores `admin.*` en [`docs/errores.md`](../../../docs/errores.md); fixtures `admin-report-page.json` y `session.json` en `ContractTest/Fixtures/`.

## Tests
- `TestShared/Reputation/ReputationRepositoryContract.cs`: bandeja por campus y estado, cerrar una sola vez y contar los procedentes.
- `TestShared/Listings/ListingRepositoryContract.cs`: retirar una publicación en cualquier estado, una vez.
- `TestShared/Admin/ModerationLogRepositoryContract.cs` (+ `UnitTest/Data`, `DatabaseTest`): historial por persona.
- `IntegrationTest/Admin/ModerationFlowTests.cs`: bandeja con motivo, publicación y responsable; "Procedente" retira la publicación (vitrina y detalle) y queda en la bitácora; "Descartar" la deja; validaciones; `Admin:Emails` da el rol.
- `SecurityTest/Auth/AdminAuthTests.cs`: 401 sin sesión, 403 con un estudiante, 403 con el rol solo en el token y con un administrador suspendido, otro campus es 404.
- `ContractTest/AdminContractTests.cs`: formas de la bandeja, la revisión y la cuenta.

## Criterios
- [x] El administrador ve los reportes con su motivo y el elemento reportado: `ModerationFlowTests.Inbox_ShowsTheReasonTheReportedProductAndItsSeller_AndUpholdRemovesIt`.
- [x] Puede retirar el contenido reportado o descartar el reporte: el mismo y `Dismiss_ClosesTheReport_AndTheContentStays`.
- [x] Solo el administrador modera (RNF-03): `AdminAuthTests` (401, 403 con estudiante, con el rol solo en el token y suspendido).
- [x] Sus acciones quedan registradas (RNF-04): `report_upheld` y `product_removed` en el historial; `ModerationLogRepositoryContract`.
- [x] Build Release 0/0; Unit 848, Integration 199, Security 288 y Contract 29 en verde; `check_endpoints` OK (122); critic PASS.
- [x] `db/030` contra MongoDB 8.0 real: los 29 scripts dos veces sin error y `DatabaseTest` 184/184 (16-Nov-2026, con un MongoDB local en lugar del contenedor; en el CI corre con Testcontainers).

## Límites
- La bandeja trae los 50 más recientes, sin paginar: suficiente para el piloto de un campus.
- Retirar una publicación no cancela un pedido o un apartado que ya estaba abierto: siguen su curso.
- Un reporte de una persona desde el chat no se puede "retirar": "Procedente" cuenta para RN-27 y la suspensión es HU-40.
- La nota de la revisión es interna: quien reportó solo recibe "Revisamos tu reporte".
- La bitácora no se borra al eliminar una cuenta: es la evidencia de la moderación y solo guarda ids y la nota del administrador.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 revisor distinto del autor)
- **Fecha:** 16-Nov-2026
- **Detalle:** la spec ligera del 29-Oct-2026 se detalló con HU-40. API `0.5.0-alpha.38`, esquema 30.

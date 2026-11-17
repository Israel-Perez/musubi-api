# plan.md — HU-39 Revisar reportes y moderar contenido (con HU-40)

## 1. Componentes
- **Nuevo `Modules/Admin/`:** `AdminReportsController`, `AdminAccountsController`, `ModerationService`, `AdminDtos`, `AdminErrors`, `AdminModuleServiceCollectionExtensions` y `Data/ModerationLog.cs`. Se registra en `Program.cs` (`AddAdminModule`).
- **M-13 Reputation:**
  - `ReportDocument` agrega `subjectId`, `reviewedBy`, `reviewedAt` y `reviewNote`; `ReportStatuses` agrega `dismissed` y `upheld`.
  - `IReportRepository`: `FindByIdAsync`, `FindByStatusAsync`, `CloseAsync` y `CountUpheldAsync`.
  - Contrato `IReportInbox`. `ReportService` guarda `subjectId` al crear.
- **M-03 Listings:** `IProductRepository.RemoveAsync` y `HideBySellerAsync`; contrato `IProductModeration`, que invalida la caché de la vitrina.
- **M-01 Auth:**
  - `IUserRepository`: `ChangeStatusAsync`, `FindByStatusAsync` y `AddRoleAsync`.
  - Contrato `IAccountModeration`, `AdminOptions` (`Admin:Emails`) en `SessionIssuer`, y `UserSummary.Roles`.
- **M-09:** tipos `report_reviewed` y `product_removed`, solo push.
- **Datos:** `db/030-moderacion.js` y `MongoSchema.ExpectedVersion = 30`.

## 2. Decisiones
- **Un módulo para HU-39 y HU-40, en un PR.** Desde un reporte procedente se llega a la cuenta y se suspende: es un solo flujo y comparten la autorización y la bitácora.
- **"Procedente" con efecto por tipo.** Con una publicación la retira (`removed`, que ya existía desde HU-06). Con una persona cuenta para RN-27. Con un problema general solo se marca atendido. Así la API tiene dos acciones (`dismiss`/`uphold`) y la app pone el texto según el tipo.
- **RN-27 como sugerencia.** Con 3 reportes procedentes o más, `suspensionSuggested`. El administrador decide: el criterio de Jira dice "puede ser suspendida", y el número estaba "[POR DEFINIR]". `ModerationService.SuspensionThreshold`.
- **La base manda sobre el token.** `[Authorize(Roles = "admin")]` corta a los estudiantes sin tocar el servicio. Además, `ModerationService` vuelve a leer la cuenta del administrador: el JWT dura 15 minutos y un rol quitado o una suspensión tienen que aplicar de inmediato.
- **`Admin:Emails` para dar el rol.** No hay pantalla para nombrar administradores. Un correo en Parameter Store recibe el rol al iniciar sesión y queda en `users.roles`, así que la base sigue siendo la fuente. Quitarlo se hace en la base (`db/README.md`).
- **Cerrar primero y retirar después.** `CloseAsync` exige `status: open` en el filtro, así que con dos administradores a la vez solo uno gana. Solo quien cerró retira y avisa.
- **Avisos solo push.** No hay pestaña de "Moderación" en el historial (HU-31). El validador igual acepta los dos tipos, porque `EveryTypeAndTargetOfTheCode_CanBeStored` lo exige para cada constante.
- **`subjectId` en el reporte.** Contar los procedentes de una persona necesita saber quién vende la publicación reportada. Se guarda al crear y al revisar, con el índice parcial `ix_subject_status`.

## 3. Excepciones al stack
Ninguna.

## 4. Guardarraíles
- Campus del administrador en todo (R1): un reporte o una cuenta de otro campus es 404 (OWASP API1).
- Sin correos en las respuestas (R10). Las notas y los motivos llevan un tope de 500 caracteres en el servicio y en el validador de `db/030`.
- No se puede suspender la cuenta propia ni la de otro administrador.

## 5. Validación
- Suites: Unit 877, Integration 202, Security 295 y Contract 30. `check_endpoints` 122. Critic PASS.
- MongoDB 8.0 real: los 30 scripts dos veces, el validador acepta la revisión y la bitácora y rechaza los estados que no existen; `DatabaseTest` 190/190.

## 6. Secuencia
1. Contratos de M-13, M-03 y M-01, con sus repositorios y baterías de `TestShared/`.
2. Módulo Admin (servicio, controllers y bitácora) y `db/030`.
3. Pruebas de integración, seguridad y contrato.
4. Documentación (`endpoints`, `errores`, `modelo-datos`, `trazabilidad`, `versionado`, `01-04`, `db/README`, `Memoria.md`).

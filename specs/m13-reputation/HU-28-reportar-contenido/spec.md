# HU-28 — Reportar contenido o usuarios sospechosos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-28 / SCRUM-57 / Sprint 3 (adelantada del S5: requisito de Google Play) / M-13 / [musubi-app:HU-28](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m13-reputation/HU-28-reportar-contenido)

## Contexto
Jira (SCRUM-57): Como **usuario**, quiero **reportar una publicación, solicitud o usuario sospechoso**, para **mantener segura la comunidad**.

- Google Play exige que una app con contenido de usuarios (el chat) permita reportarlo y bloquear a la otra persona. Por eso se adelanta junto con HU-57 (Bloquear) antes de pedir producción.
- Maqueta: `chatMoreSheet` → "Reportar" y Ajustes → `aj-reportar` ("Reportar un problema"). Las dos abren `reportSheet`: el motivo (`REPORT_REASONS`), "Cuéntanos qué pasó" y "Enviar reporte". El aviso es "Tu reporte es confidencial. El equipo de Musubi lo revisa en menos de 24 horas.".
- Trazabilidad: RF-37 · CU-20 · RN-23, RN-27.

**Depende de:** `02-01`, `02-03`, `HU-20` (chat). La revisión del administrador es de `HU-39`.

## Requisitos
1. **R1 · Qué se reporta:**
   - una persona (`user`), desde el chat; opcionalmente con la conversación donde pasó;
   - una publicación (`product`);
   - un problema general (`general`), desde Ajustes.
   Solicitudes, ofertas y mensajes sueltos llegan con su HU (`NOTE (HU-13)`, `NOTE (HU-16)`).
2. **R2 · Motivo:** uno de los cinco de la maqueta, en su orden: `payment` ("El pago no llegó o no coincide"), `not_as_described`, `no_show`, `off_platform` ("Me pidió pagar fuera de Musubi") y `other`.
3. **R3 · Detalle:** "Cuéntanos qué pasó" es opcional, de hasta 1000 caracteres. Se guarda sin moderar (02-07): no se publica, y la cita del abuso es la evidencia que el equipo necesita.
4. **R4 · Reglas:**
   - nadie se reporta a sí mismo ni a su propia publicación;
   - lo reportado debe ser del mismo campus;
   - con conversación, los dos participan en ella;
   - si no se cumple, responde 404 sin revelar si existe (OWASP API1).
5. **R5 · Revisión:**
   - el reporte se guarda `open` en `reports` para HU-39;
   - si existe `Reports:NotifyEmail`, se avisa al equipo por correo con el id, el motivo y el detalle;
   - el log lleva solo el id, el tipo y el motivo (RNF-05).
6. **R6 · Abuso:** rate limit `reports` de 10 por hora por usuario (cada reporte manda un correo).
7. **R7 · Borrado de cuenta (HU-46):**
   - se borran los reportes que hizo la persona;
   - los que otros hicieron sobre ella se quedan para la revisión, y solo guardan su id.

RNF que aplican: RNF-03 (sesión), RNF-05 (datos mínimos en logs), RNF-21 (campus).

## Diseño
- **Módulo:** `Musubi.Api/Modules/Reputation/` (M-13, primera HU del módulo).
- **Archivos:** `ReportService`, `ReportsController`, `ReputationErrors`, `ReputationDtos` y `Data/` (`ReportDocument`, `IReportRepository`, Mongo e InMemory).
- **Lo que consulta de otros módulos:** `IMemberDirectory` (M-01), `IProductDirectory` (M-03) e `IConversationDirectory` (M-07, nuevo: participantes de una conversación). No lee colecciones ajenas.
- **Aviso:** `IEmailSender` (SES). Si el correo falla, el reporte ya quedó guardado y se registra el error.
- **Datos:** colección `reports` (`db/008-reportes-bloqueos.js`, esquema 8) con los índices `ix_community_status` (bandeja de HU-39) e `ix_reporter` (borrado). Ver [`docs/modelo-datos.md`](../../../docs/modelo-datos.md).

## Contratos
- `POST /api/v1/reports`: fila en [`docs/endpoints.md`](../../../docs/endpoints.md).
- Errores `reports.*` en [`docs/errores.md`](../../../docs/errores.md).
- Fixture `ContractTest/Fixtures/report.json`.
- Configuración `Reports:NotifyEmail` y `RateLimiting:Reports:*` (`01-04`).

## Tests
- `UnitTest/Reputation/ReputationServiceTests.cs`: R2–R5 (motivo, detalle, uno mismo, otro campus, conversación ajena, publicación propia, correo).
- `UnitTest/Data/InMemoryReputationRepositoryTests.cs` + `DatabaseTest/MongoReputationRepositoryTests.cs`: batería `TestShared/Reputation`.
- `IntegrationTest/Reputation/ReputationFlowTests.cs`: desde el chat, desde Ajustes, motivo inválido y conversación ajena.
- `SecurityTest/Auth/ReputationAuthTests.cs`: 401 sin sesión; el token en la query no sirve.
- `ContractTest/ReputationContractTests.cs` y `DatabaseTest/SchemaScriptTests.cs` (validación e índices).

## Criterios
- [x] Se puede reportar una publicación o a un usuario, o un problema general, indicando el motivo (R1, R2).
- [x] El reporte queda `open` para la revisión (HU-39) y se avisa al equipo por correo si está configurado (R5).
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.
- [ ] `DatabaseTest` en el CI (sin Docker en local).
- [ ] Probado en producción con `/musubi/prod/Reports/NotifyEmail` configurado.

## Límites
- **Sin bandeja del administrador:** el equipo revisa la colección `reports` y el correo hasta HU-39 (`NOTE (HU-39)`).
- **A quien reporta no se le responde desde la app:** el equipo le escribe al correo institucional.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 08-Nov-2026 (implementación)
- **Detalle:**
  - se adelanta del S5 al S3 junto con HU-57, por el requisito de Google Play;
  - los motivos son los de la maqueta;
  - el texto no se modera (R3);
  - el aviso al equipo es por correo mientras no exista HU-39.

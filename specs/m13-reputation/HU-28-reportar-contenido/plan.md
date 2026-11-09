# HU-28 — Reportar contenido o usuarios sospechosos (plan)

## Decisiones
- **Un solo endpoint con `targetType`:** la maqueta usa la misma hoja (`reportSheet`) desde el chat y desde Ajustes. `general` no lleva id; `user` y `product` sí. Así una solicitud u oferta (HU-13/16) se agrega como otro tipo sin endpoint nuevo.
- **Los motivos de la maqueta:** se respetan los cinco de `REPORT_REASONS` y su orden (lineamiento 4). Como código estable en inglés (`off_platform`), para que la app traduzca y HU-39 filtre.
- **Sin moderar el detalle:** moderar rechazaría justo el reporte que cita el insulto. El texto no se publica: lo leen solo el equipo y el correo de aviso.
- **Correo al equipo en lugar de un panel:** HU-39 todavía no existe y la maqueta promete "menos de 24 horas". El correo usa el `IEmailSender` de 02-03 (SES) y un fallo no le niega el reporte a quien lo envía.
- **404 igual para todo lo que no se puede reportar:** otro campus, inexistente o una conversación ajena dan la misma respuesta (OWASP API1, RN-22).
- **Rate limit propio (10/hora):** cada reporte manda un correo; el límite de chat (30/min) sería demasiado alto.

## Archivos
- **Nuevos:** `Modules/Reputation/{ReportService, ReportsController, ReputationErrors, ReputationDtos, ReputationModuleServiceCollectionExtensions}.cs`, `Modules/Reputation/Data/*` y `Modules/Chat/ConversationDirectory.cs`.
- **Script:** `db/008-reportes-bloqueos.js` (`reports`); `MongoSchema.Reports` y `ExpectedVersion = 8`.
- **Pruebas:** `ReputationServiceTests`, `ReputationRepositoryContract` (en memoria y Mongo), `ReputationFlowTests`, `ReputationAuthTests`, `ReputationContractTests` y `SchemaScriptTests`.

# HU-32, HU-33 y HU-34 — El acuerdo con IA (tareas de la API)

## T1 — Detallar la spec
- **Qué:** decisiones con la usuaria (Gemini gratis, aviso en el chat y 30 días) y medición de los modelos que da la llave.
- **Estado:** ✅ Hecho (10-Nov-2026).

## T2 — Chat
- **Qué:** mensajes `kind: notice`, `MessageResponse.kind`, evento `agreementChanged` e `IConversationNotices`.
- **Estado:** ✅ Hecho.

## T3 — Datos
- **Qué:** `db/015-acuerdos.js` e `IAgreementRepository` en Mongo y en memoria con la batería `TestShared/GenAi/AgreementRepositoryContract.cs`.
- **Estado:** ✅ Hecho. La batería contra Mongo real corre en el CI (`DatabaseTest`).

## T4 — IA y servicio
- **Qué:** `IAgreementExtractor` (Gemini, en memoria y apagado), el filtro de datos personales, `AgreementService` y `AgreementsController` (5 rutas). Pruebas:
  - `UnitTest/GenAi/AgreementServiceTests.cs` y `UnitTest/GenAi/AgreementExtractionTests.cs`;
  - `IntegrationTest/GenAi/AgreementFlowTests.cs`;
  - `SecurityTest/Auth/AgreementsAuthTests.cs`;
  - `ContractTest/AgreementsContractTests.cs`.
- **Estado:** ✅ Hecho.

## T5 — Pedido desde el acuerdo (HU-34)
- **Qué:** `POST /orders` con `agreementId` (`IAgreementDirectory`); el acuerdo se borra. Pruebas en `UnitTest/Orders/OrderServiceTests.cs`.
- **Estado:** ✅ Hecho.

## T6 — Documentación
- **Qué:** `docs/endpoints.md` (70 filas), `docs/errores.md`, `docs/modelo-datos.md`, `docs/trazabilidad.md`, `docs/versionado.md` (`0.5.0-alpha.16`), `db/README.md`, `01-04`, la bitácora de lineamientos, `AGENTS.md` y `Memoria.md`.
- **Estado:** ✅ Hecho.

## Pendientes con dueño
- `NOTE (HU-47)`: el aviso de privacidad (app y landing) dice qué sale a Gemini y cuánto se conserva.
- `NOTE (HU-31)`: aviso push al otro participante cuando se pide, se confirma o se descarta el acuerdo.
- Despliegue: la llave ya está en `/musubi/prod/Gemini/ApiKey`; las instancias la leen al reemplazarse (`infra-up`, con permiso de la usuaria).

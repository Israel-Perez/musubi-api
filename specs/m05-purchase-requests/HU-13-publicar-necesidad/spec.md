# HU-13 — Publicar una necesidad de compra

**HU / Jira / Sprint / Módulo / Contraparte:** HU-13 / SCRUM-35 / Sprint 3 / M-05 / [musubi-app:HU-13](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m05-purchase-requests/HU-13-publicar-necesidad)

## Contexto
Jira (SCRUM-35): Como **comprador**, quiero **publicar una solicitud indicando el producto, la descripción, las características, el presupuesto, la cantidad y la fecha en que lo necesito**, para **que otros usuarios sepan lo que busco**.

- Maqueta: `Necesidad-1`, `Necesidad-2`, `Necesidad-3`, `Necesidad-publicada` y `Mis-solicitudes`.
- Trazabilidad: RF-16, RF-22 · CU-08 · Flujo B · RN-09, RN-10, RN-12.

**Depende de:** `02-01`, `02-03`, `02-07` (moderación) y `HU-01`.

## Requisitos
1. **R1 · Campos:** producto buscado (3 a 80), categoría (del campus u "Otro"), descripción (hasta 300), características (hasta 5 de hasta 40), presupuesto máximo ($1 a $999,999), cantidad (1 a 10) y fecha (de hoy a 60 días).
2. **R2 · Obligatorios (RN-09):** producto, categoría, presupuesto y cantidad. Descripción, características y fecha son opcionales.
3. **R3 · Campos pendientes:** si falta uno, la API responde 400 con el código del campo (`wanted.invalid_*`) y la app lo señala.
4. **R4 · Activa:** se guarda `active` con el nombre corto de quien la publica ("Ana R."). La ven las personas de su campus (`GET /wanted/{id}`; el tablero es HU-15), salvo con un bloqueo de por medio (HU-57).
5. **R5 · Máximo (RN-12):** 5 solicitudes activas por persona. La sexta responde `409 wanted.too_many_active`.
6. **R6 · Vencimiento (RN-10):** vence al terminar la fecha elegida en el campus o, sin fecha, a los 30 días. "Vencida" se calcula al leer.
7. **R7 · Moderación (02-07):** producto, descripción y características se moderan antes de guardar. Si se rechaza alguno, la respuesta nombra el campo.
8. **R8 · Mis solicitudes:** `GET /wanted/mine`, de la más nueva a la más vieja, con el estado de ahora.
9. **R9 · Borrado de cuenta (HU-46):** se borran sus solicitudes.

## Diseño
- **Módulo:** `Musubi.Api/Modules/PurchaseRequests/` (M-05, primera HU): `WantedService`, `WantedController`, `WantedErrors`, `WantedDtos` y `Data/` (`WantedRequestDocument` e `IWantedRepository` en Mongo y en memoria).
- **Contratos con otros módulos:** `ICategoryDirectory` (M-03, nuevo) valida la categoría; también usa `IMemberDirectory`, `IBlockDirectory` e `IContentModerator`.
- **Día del campus:** UTC-6 fijo (México no tiene horario de verano desde 2022). Una fecha vence a las 06:00 UTC del día siguiente.
- **Datos:** `wantedRequests` (`db/009-solicitudes.js`) con `ix_community_status_id` (tablero, HU-15) e `ix_requester_id` (mis solicitudes, RN-12 y borrado). Ver [`docs/modelo-datos.md`](../../../docs/modelo-datos.md).

## Contratos
- Filas "M-05" de [`docs/endpoints.md`](../../../docs/endpoints.md): `POST /api/v1/wanted`, `GET /api/v1/wanted/mine` y `GET /api/v1/wanted/{id}`.
- Errores `wanted.*` en [`docs/errores.md`](../../../docs/errores.md).
- Fixture `ContractTest/Fixtures/wanted.json`.

## Tests
- `UnitTest/PurchaseRequests/WantedServiceTests.cs`: R1–R8, incluido el día del campus frente al día UTC.
- `UnitTest/Data/InMemoryWantedRepositoryTests.cs` + `DatabaseTest/MongoWantedRepositoryTests.cs`: batería `TestShared/PurchaseRequests`.
- `IntegrationTest/PurchaseRequests/WantedFlowTests.cs`: publicar, mis solicitudes, verla desde otra cuenta, campos que faltan y fecha pasada.
- `SecurityTest/Auth/WantedAuthTests.cs`, `ContractTest/WantedContractTests.cs` y `DatabaseTest/SchemaScriptTests.cs`.

## Criterios
- [x] El formulario captura producto, categoría, descripción, características, presupuesto, cantidad y fecha (R1).
- [x] Producto, categoría, presupuesto y cantidad son obligatorios; el resto es opcional (R2).
- [x] Si falta un dato obligatorio, se señala el campo (R3).
- [x] La solicitud se guarda activa y la ve su campus (R4).
- [x] Máximo de 5 activas (R5).
- [x] Vence en su fecha o a los 30 días (R6).
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.
- [x] `DatabaseTest` en el CI (job `database`, PR #54).

## Límites
- `NOTE (HU-14)`: editar, cerrar y cancelar.
- `NOTE (HU-15)`: el tablero "Se busca" del campus.
- `NOTE (HU-16)`: ofertas ("Yo tengo este producto") y `offersCount`.
- `NOTE (HU-18)`: avisar a quien tenga algo parecido.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador) · 09-Nov-2026 (implementación)
- **Detalle:**
  - [POR DEFINIR] → 5 activas (RN-12) y 30 días sin fecha (RN-10);
  - "Otro" como categoría, como la maqueta;
  - vencida se calcula, no se guarda.

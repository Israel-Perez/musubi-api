# HU-16 — Responder "Yo tengo este producto"

**HU / Jira / Sprint / Módulo / Contraparte:** HU-16 / SCRUM-38 / Sprint 4 (9 – 20 nov 2026) / M-05 / [musubi-app:HU-16](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m05-purchase-requests/HU-16-yo-tengo-este-producto)

## Contexto
Jira (SCRUM-38): Como **usuario**, quiero **responder a una solicitud con "Yo tengo este producto", ya sea vinculando una publicación o describiendo un producto que todavía no publico**, para **ofrecerlo directamente al comprador**.

- Maqueta: `Se-busca`, `Yo-tengo-vincular`, `Yo-tengo-oferta` y `Yo-tengo-enviada`.
- Trazabilidad: RF-20, RF-40 · CU-11 · Flujo B · RN-06, RN-13, RN-14.

**Depende de:** `02-01`, `02-03`, `02-07` (moderación), `HU-06`, `HU-13`, `HU-15` y `HU-57`.

## Requisitos
1. **R1 · Solicitud abierta:** solo se ofrece en una solicitud activa, de mi campus y sin bloqueo entre las dos personas; si no, `404 wanted.not_found` (no revela cuál).
2. **R2 · Vincular publicación:** una publicación mía, disponible y de mi campus. El nombre, el precio, el estado y la portada se copian de ella (R8).
3. **R3 · Oferta directa:** descripción (3 a 300), precio ($1 a $999,999), estado (nuevo o usado) y foto opcional (subida por `POST /products/photos` por la misma persona). No se publica en el marketplace y solo la verá quien busca (RN-13, HU-17).
4. **R4 · Una por solicitud (RN-14):** la primera crea (201) y suma `offersCount`; volver a enviar la reemplaza (200).
5. **R5 · Nunca a la propia (RN-06):** `409 offers.own_request`.
6. **R6 · Presupuesto:** un precio arriba del presupuesto se permite y la respuesta lo marca (`overBudget`).
7. **R7 · Moderación (02-07):** la descripción y el mensaje opcional (≤ 300) se moderan antes de guardar.
8. **R8 · Borrado de cuenta (HU-46):** se borran las ofertas que hizo y las que recibió en sus solicitudes.

## Diseño
- `Modules/PurchaseRequests/`:
  - `OfferService` y `OffersController` (`/wanted/{requestId}/offers/mine`);
  - `OfferDtos` y `OfferErrors`;
  - `Data/OfferDocuments` e `IOfferRepository` (Mongo y en memoria);
  - `OfferAccountEraser`.
- Contratos con M-03: `IProductDirectory` (`ProductSummary.Available` y `PhotoKey`) e `IListingPhotos` (la foto es de quien ofrece).
- **Datos:** `offers` (`db/011-ofertas.js`, esquema 11) con `ux_request_seller` (RN-14), `ix_request_id` (HU-17) e `ix_seller`/`ix_requester` (borrado). Ver [`docs/modelo-datos.md`](../../../docs/modelo-datos.md).

## Contratos
- Filas `GET` y `PUT /api/v1/wanted/{requestId}/offers/mine` de [`docs/endpoints.md`](../../../docs/endpoints.md).
- Errores `offers.*` en [`docs/errores.md`](../../../docs/errores.md).
- Fixture `ContractTest/Fixtures/offer.json`.

## Tests
- `UnitTest/PurchaseRequests/OfferServiceTests.cs`: R1–R7.
- `TestShared/PurchaseRequests/OfferRepositoryContract.cs` en memoria y en MongoDB (`DatabaseTest/MongoOfferRepositoryTests.cs`); `WantedRepositoryContract` (`IncrementOffers_*`).
- `IntegrationTest/PurchaseRequests/OfferFlowTests.cs`, `SecurityTest/Auth/WantedAuthTests.cs` (401), `ContractTest/WantedContractTests.cs` (`Offer_*`) y `DatabaseTest/SchemaScriptTests.cs` (`011-ofertas`).

## Criterios
- [x] Desde una solicitud **Activa** se elige "Yo tengo este producto" (R1).
- [x] Se puede vincular una publicación existente o crear una oferta directa sin publicarla (R2, R3).
- [x] Sin publicaciones, solo la oferta directa: la app esconde "Vincular publicación" (`musubi-app:HU-16`).
- [x] La oferta directa solo es para quien busca (RN-13): solo la lee quien la envió hasta HU-17.
- [x] Una oferta por solicitud, editable (RN-14); nunca a las propias (RN-06).
- [x] Si el precio supera el presupuesto, se advierte y se permite (R6).
- [ ] El comprador recibe una notificación (RF-40): `NOTE (HU-30)`.
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.
- [ ] `DatabaseTest` en el CI.

## Límites
- `NOTE (HU-30)`: el aviso a quien busca (RF-40) llega con las notificaciones.
- `NOTE (HU-17)`: quien busca ve las ofertas, abre el chat desde una, la acepta o la rechaza. Por eso `Yo-tengo-enviada` dice "te escribirá por el chat".
- Si la publicación vinculada cambia de precio después, la oferta conserva el de cuando se envió (copia R8); volver a enviar la actualiza.
- `offersCount` no baja si se borra la cuenta de quien ofreció.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador) · 09-Nov-2026 (implementación)
- **Detalle:**
  - la oferta directa no se convierte en publicación (decisión pendiente de Jira: se resuelve en HU-17);
  - la conversación la abre quien busca desde la oferta (HU-17).

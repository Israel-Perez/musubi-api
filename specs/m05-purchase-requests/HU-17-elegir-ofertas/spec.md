# HU-17 — Ver y elegir ofertas recibidas

**HU / Jira / Sprint / Módulo / Contraparte:** HU-17 / SCRUM-39 / Sprint 4 (9 – 20 nov 2026) / M-05 / [musubi-app:HU-17](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m05-purchase-requests/HU-17-elegir-ofertas)

## Contexto
Jira (SCRUM-39): Como **comprador**, quiero **ver las ofertas que recibí en mi solicitud**, para **comparar opciones y elegir la que más me convenga**.

- Maqueta: `Mis-solicitudes` ("Compara las ofertas": Rechazar, Abrir chat y Aceptar).
- Trazabilidad: RF-21 · CU-12 · RN-13.

**Depende de:** `HU-16` (ofertas), `HU-20` (chat), `HU-04` (carrera visible) y `HU-57` (bloqueos).

## Requisitos
1. **R1 · Ver ofertas:** quien publicó la solicitud ve sus ofertas pendientes y aceptadas, de la más nueva a la más vieja, con nombre, foto, carrera visible y reputación de quien ofrece. Nadie más las ve (RN-13); las de personas bloqueadas no salen (HU-57).
2. **R2 · Rechazar:** una oferta pendiente queda rechazada, deja de listarse y quien la hizo ya no la puede editar.
3. **R3 · Aceptar:** con la solicitud activa, la oferta pendiente queda aceptada y se abre la conversación con quien ofrece (quien busca es el comprador). Aceptar otra vez solo devuelve la conversación.
4. **R4 · Abrir chat:** abre (o reabre) la conversación de la oferta sin decidir.
5. **R5 · Conversación de una oferta:** `context.type` `offer`, con el nombre y la foto de la oferta copiados (R8); una por oferta.

## Diseño
- **`OfferService`:** `ReceivedAsync`, `RejectAsync`, `AcceptAsync` y `ChatAsync`, con las rutas en `OffersController`.
- **Repositorio de ofertas:** `IOfferRepository.FindByIdAsync`, `FindByRequestAsync` y `TryDecideAsync` (atómico: solo de `sent`).
- **M-07:** `IConversationOpener.OpenForOfferAsync` y `ConversationContextTypes.Offer`. `ChatService` no busca publicación para un contexto `offer` y responde `available: true` con la copia.
- **M-02:** `ProfileCard` suma `RatingAvg` y `RatingCount`.
- **Datos:** `db/012-ofertas-chat.js` (esquema 12). `conversations.context.type` admite `offer`; `offers.status` admite `accepted` y `rejected`, con `decidedAt` y `conditionDetail`.

## Contratos
- Filas `GET /api/v1/wanted/{requestId}/offers` y `POST .../offers/{offerId}/reject`, `accept` y `chat` de [`docs/endpoints.md`](../../../docs/endpoints.md).
- Errores `offers.offer_not_found`, `offers.not_pending`, `offers.request_closed` y `offers.not_editable` en [`docs/errores.md`](../../../docs/errores.md).
- Fixture `ContractTest/Fixtures/received-offers.json`.

## Tests
- `UnitTest/PurchaseRequests/OfferServiceTests.cs`: R1–R5.
- `TestShared/PurchaseRequests/OfferRepositoryContract.cs` (`FindByRequest_NewestFirst_AndDecideOnlyOnce`), en memoria y en MongoDB.
- `IntegrationTest/PurchaseRequests/OfferFlowTests.cs`: ver, rechazar, aceptar y escribir en la conversación como comprador y vendedor; otra persona no ve ni decide.
- `SecurityTest/Auth/WantedAuthTests.cs` (401), `ContractTest/WantedContractTests.cs` y `DatabaseTest/SchemaScriptTests.cs` (`012-ofertas-chat`).

## Criterios
- [x] El comprador ve las ofertas recibidas en cada solicitud (R1).
- [x] Desde cada oferta puede abrir un chat con quien ofrece, rechazarla o aceptarla (R2–R4).
- [ ] Aceptar una oferta inicia un pedido: `NOTE (HU-23)`. Aceptar la deja aceptada y abre la conversación; el pedido sale de ella en HU-23.
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.
- [ ] `DatabaseTest` en el CI.

## Límites
- `NOTE (HU-23)`: el pedido desde la oferta aceptada.
- `NOTE (HU-30)`: avisar a quien ofrece que su oferta se eligió o se rechazó ("Le avisaremos con un mensaje amable").
- "Recomendadas", "Menor precio" y "Mejor reputación" las ordena la app; la API las entrega de la más nueva a la más vieja.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador) · 09-Nov-2026 (implementación)
- **Detalle:**
  - aceptar no crea el pedido todavía (HU-23);
  - la oferta directa aceptada no se convierte en publicación (decisión pendiente de HU-16): el pedido se arma desde la oferta.

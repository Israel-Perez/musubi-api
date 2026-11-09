# HU-17 — Ver y elegir ofertas recibidas (plan)

## Decisiones
- **Aceptar abre la conversación y no crea el pedido:** la maqueta lleva del "Aceptar oferta" al chat ("Abriremos el chat para que acuerden la entrega"). El pedido (HU-23) sale de la oferta aceptada o del acuerdo del chat.
- **La conversación es de la oferta, no de la publicación:** una oferta directa no tiene publicación. El contexto `offer` copia nombre y foto (R8). Quien busca es el comprador y quien ofrece, el vendedor, como en Compras/Ventas de la bandeja.
- **Decisión atómica (`TryDecideAsync` solo desde `sent`):** dos toques a la vez o rechazar después de aceptar no dejan un estado mezclado.
- **Rechazar es definitivo en la API:** el "Deshacer" de la maqueta lo resuelve la app esperando a que termine el aviso antes de llamar a la API.
- **Una oferta elegida o rechazada no se edita:** si quien ofrece pudiera cambiar el precio después de aceptada, el acuerdo perdería sentido.
- **El orden lo hace la app:** son pocas ofertas por solicitud (una por persona, RN-14) y los tres órdenes de la maqueta necesitan precio, reputación y presupuesto, que la app ya tiene.

## Archivos
- **Nuevos:** `db/012-ofertas-chat.js` y `ContractTest/Fixtures/received-offers.json`.
- **Cambian:**
  - `Modules/PurchaseRequests/{OfferService, OffersController, OfferDtos, OfferErrors}.cs` y `Data/Offer*.cs`;
  - `Modules/Chat/{ConversationDirectory, ChatService, ChatModuleServiceCollectionExtensions}.cs` y `Data/ChatDocuments.cs`;
  - `Modules/Profiles/ProfileDirectory.cs`;
  - `MongoSchema` (esquema 12).
- **Docs:** `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `db/README.md`, `docs/versionado.md` (`0.5.0-alpha.11`), `docs/trazabilidad.md` y `Memoria.md`.

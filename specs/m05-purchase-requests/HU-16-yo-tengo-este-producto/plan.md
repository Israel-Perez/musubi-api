# HU-16 — Responder "Yo tengo este producto" (plan)

## Decisiones
- **`PUT .../offers/mine` crea o reemplaza:** RN-14 permite una oferta por persona y editarla. Un solo verbo idempotente evita "ya existe" en la app; el índice único resuelve dos envíos a la vez.
- **Copias de la publicación vinculada (R8):** nombre, precio, estado y portada se guardan en la oferta para que HU-17 la muestre sin leer `products`. Si el precio cambia después, la oferta conserva el de cuando se envió.
- **Foto de la oferta directa por `POST /products/photos`:** reutiliza la subida, la moderación y el prefijo por usuario de HU-06; la oferta solo guarda la llave y valida que sea de quien ofrece (`IListingPhotos`).
- **`overBudget` en la respuesta:** la maqueta dice "Está dentro de su presupuesto" o "Está arriba de su presupuesto"; la app lo calcula al escribir y la API lo confirma.
- **Sin conversación al enviar:** `Yo-tengo-enviada` dice "si le interesa, te escribirá por el chat". El chat desde una oferta es de HU-17 (quien busca la abre).
- **Aviso al comprador (RF-40):** queda en HU-30 con las notificaciones push.

## Archivos
- **Nuevos:**
  - `Modules/PurchaseRequests/{OfferService, OffersController, OfferDtos, OfferErrors}.cs`;
  - `Data/{OfferDocuments, OfferRepositories}.cs`;
  - `db/011-ofertas.js`.
- **Cambian:**
  - `ProductDirectory.cs` (`Available`, `PhotoKey` e `IListingPhotos`) y `ListingService.IsOwnPhotoKey`;
  - `IWantedRepository.IncrementOffersAsync`;
  - `MongoSchema` (esquema 11) y el registro del módulo.
- **Docs:** `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `db/README.md`, `docs/versionado.md` (`0.5.0-alpha.10`), `docs/trazabilidad.md` y `Memoria.md`.

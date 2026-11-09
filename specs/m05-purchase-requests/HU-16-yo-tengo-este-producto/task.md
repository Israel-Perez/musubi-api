# HU-16 — Responder "Yo tengo este producto" (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (09-Nov-2026): crear o reemplazar con `PUT`, copias de la publicación y sin chat al enviar.

## T2 — Datos
- **Crear:** `offers` en `db/011-ofertas.js` y los repositorios Mongo e InMemory; `IncrementOffersAsync`.
- **Verificar:** `OfferRepositoryContract` y `WantedRepositoryContract` (en memoria; en Mongo en el CI) y `SchemaScriptTests`.
- **Estado:** ✅ Hecho (09-Nov-2026).

## T3 — Servicio y endpoints
- **Crear:** `OfferService`, `GET`/`PUT /wanted/{requestId}/offers/mine` e `IListingPhotos`.
- **Verificar:** `OfferServiceTests`, `OfferFlowTests`, `WantedAuthTests` y `WantedContractTests`.
- **Estado:** ✅ Hecho (09-Nov-2026).

## T4 — Documentación
- **Estado:** ✅ Hecho (09-Nov-2026).

## Pendientes
- La app: `musubi-app:HU-16`.
- `NOTE (HU-30)` y `NOTE (HU-17)`.

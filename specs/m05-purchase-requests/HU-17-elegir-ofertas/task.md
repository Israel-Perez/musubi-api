# HU-17 — Ver y elegir ofertas recibidas (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (09-Nov-2026): aceptar abre el chat, el pedido es HU-23 y el orden lo hace la app.

## T2 — Datos
- **Crear:** `db/012-ofertas-chat.js`; `FindByIdAsync`, `FindByRequestAsync` y `TryDecideAsync`.
- **Verificar:** `OfferRepositoryContract` (en memoria; en Mongo en el CI) y `SchemaScriptTests`.
- **Estado:** ✅ Hecho (09-Nov-2026).

## T3 — Servicio y endpoints
- **Crear:** `ReceivedAsync`, `RejectAsync`, `AcceptAsync`, `ChatAsync`, las 4 rutas e `IConversationOpener`.
- **Verificar:** `OfferServiceTests`, `OfferFlowTests`, `WantedAuthTests` y `WantedContractTests`.
- **Estado:** ✅ Hecho (09-Nov-2026).

## T4 — Documentación
- **Estado:** ✅ Hecho (09-Nov-2026).

## Pendientes
- La app: `musubi-app:HU-17`.
- `NOTE (HU-23)` y `NOTE (HU-30)`.

# HU-25 — Consultar mis pedidos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-25 / SCRUM-44 / Sprint 4 (9 – 20 nov 2026) / M-08 / [musubi-app:HU-25](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-25-mis-pedidos)

## Contexto
Jira (SCRUM-44): Como **comprador**, quiero **consultar mis pedidos**, para **saber en qué estado está cada compra**.

- Maqueta: la sección "Mis pedidos" de `Perfil` (los últimos con su estado y "Ver todos"). La lista completa no tiene pantalla; la app la arma con las pestañas de `Mis-productos` (lineamiento 4: se agrega, no se quita).
- Trazabilidad: RF-32 · CU-17.
- Decisiones de la usuaria (12-Nov-2026): **solo mis compras** (las ventas son de HU-26) y "Ver todos" con **pestañas por estado**: Activos (pendientes y confirmados), Completados y Cancelados.

**Depende de:** `HU-23` (el pedido) y `HU-24` (sus estados, la entrega y la cancelación).

## Requisitos
1. **R1 · Mis compras:** `GET /api/v1/orders/mine` devuelve los pedidos donde quien consulta es quien compra, del más nuevo al más viejo. Cada uno es un `OrderResponse` (estado, producto, cantidad, precio, total, lugar, fecha, pago, entrega y cancelación), con `role: buyer`.
2. **R2 · Pestañas:** `tab` = `active` (pendientes y confirmados), `completed` o `cancelled`; sin `tab`, todos (la sección de `Perfil`). `counts { active, completed, cancelled }` llena los números de las tres y `total` es el de la pestaña pedida.
3. **R3 · Páginas:** `limit` de 1 a 50 (20 por defecto) y `cursor` opaco; si la última página llega justo llena, `nextCursor` es `null` (como HU-08).
4. **R4 · Errores:** una pestaña que no existe responde `400 orders.invalid_tab`, y un cursor que no es de la lista, `400 orders.invalid_cursor`.
5. **R5 · Privacidad (RNF-03, OWASP API1):** la lista sale del token; ningún parámetro deja ver los pedidos de otra persona ni las ventas propias.

RNF que aplican: RNF-03 (solo lo propio).

## Diseño
- **Repositorio:** `IOrderRepository.FindByBuyerAsync(buyerId, statuses?, beforeId?, limit)` y `CountByBuyerAsync(buyerId)` (un `$group` por estado), en Mongo y en memoria. Usan el índice `ix_buyer_id` (`{ buyerId, _id: -1 }`) de `db/014`: **sin script nuevo**, el esquema sigue en 19.
- **Pestañas:** `MyOrderTabs` (lista blanca) en `Orders/Data/OrderDocuments.cs`.
- **Servicio:** `OrderService.MineAsync` pide uno de más para saber si hay otra página.
- **Controller:** `OrdersController.Mine` (`GET orders/mine`); la ruta literal gana a `orders/{id}`.

## Contratos
- Fila nueva en [`docs/endpoints.md`](../../../docs/endpoints.md): `GET /api/v1/orders/mine` → `MyOrderPage { items: OrderResponse[], nextCursor, total, counts { active, completed, cancelled } }`.
- Errores nuevos en [`docs/errores.md`](../../../docs/errores.md): `orders.invalid_tab` y `orders.invalid_cursor`.
- Fixture `ContractTest/Fixtures/my-order-page.json`; cada pedido tiene la forma de `order.json`.

## Tests
- `TestShared/Orders/OrderRepositoryContract.cs` (memoria y MongoDB real): `FindByBuyer_OnlyItsPurchases_NewestFirst_ByStatus_AndCounts`.
- `UnitTest/Orders/MyOrdersTests.cs` (7): solo mis compras con los tres números, cada pestaña, cursor sin página vacía, pestaña y cursor inválidos, `limit` acotado y cuenta vacía.
- `IntegrationTest/Orders/OrderLifecycleFlowTests.cs`: `MyOrders_ShowMyPurchasesByTab_WithTheirCounts_ButNotMySales`.
- `SecurityTest/Auth/OrdersAuthTests.cs`: 401 sin sesión, `MyOrders_OnlyMyPurchases_NeverSomeoneElses` (un `buyerId` en la query no cambia nada) y 400 con pestaña o cursor inválidos.
- `ContractTest/OrdersContractTests.cs`: `MyOrders_MatchThePageFixture_AndEachItemIsAnOrder`.

## Criterios
- [x] El comprador ve la lista de sus pedidos con su estado actual → `MyOrdersTests` e integración en verde.
- [x] El detalle muestra producto, cantidad, precio, lugar y fecha de entrega → cada elemento es un `OrderResponse` (`item`, `delivery`), verificado contra `order.json`.
- [x] Solo lo propio: otra persona y quien vende no ven mis compras → `MyOrders_OnlyMyPurchases_NeverSomeoneElses`.
- [x] Build Release 0/0 · Unit 590 · Integration 167 · Security 216 · Contract 21 (100 %) · `check_endpoints` OK (83) · critic PASS.

## Límites
- Las ventas (historial de quien vende) no salen aquí: van en `GET /orders/sales` (HU-26).
- Sin búsqueda ni filtro por fecha; el orden es siempre del más nuevo al más viejo.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 12-Nov-2026 (detallado e implementado)
- **Detalle:** con las decisiones de la usuaria del 12-Nov-2026 (solo compras; pestañas por estado). Sin script de base: reutiliza `ix_buyer_id`.

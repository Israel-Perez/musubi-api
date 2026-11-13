# HU-26 — Consultar mi historial de ventas

**HU / Jira / Sprint / Módulo / Contraparte:** HU-26 / SCRUM-45 / Sprint 4 (9 – 20 nov 2026) / M-08 / [musubi-app:HU-26](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-26-historial-ventas)

## Contexto
Jira (SCRUM-45): Como **vendedor**, quiero **consultar mi historial de ventas**, para **saber qué he vendido**.

- Maqueta: la pestaña "Vendidos" de `Mis-productos`: tarjeta verde con "12 ventas" y el total, y una fila por venta con foto, nombre, "$1,400 · 30 sep" y la píldora "Vendido".
- Trazabilidad: RF-33 · CU-18.
- Decisiones de la usuaria (13-Nov-2026):
  - el historial va en "Vendidos" de `Mis-productos`, con las ventas completadas; lo marcado a mano (HU-08) sigue apareciendo, sin comprador;
  - "Ventas" y "Compras" del perfil cuentan los pedidos completados.

**Depende de:** `HU-24` (pedido completado) y `HU-08` ("Vendidos" de `Mis-productos`).

## Requisitos
1. **R1 · Mis ventas:** `GET /api/v1/orders/sales` devuelve mis ventas completadas, de la más nueva a la más vieja. Cada una es un `OrderResponse` (`role: seller`) con el producto, quien compró, las piezas, el total, la fecha y el estado.
2. **R2 · Tarjeta:** `count` y `totalCents` (todas las completadas, no solo la página) llenan "N ventas" y el total real.
3. **R3 · Sin repetir:** `GET /products/mine?tab=sold` y `counts.sold` ya no incluyen las publicaciones que tienen un pedido completado (ya salen como venta); quedan las marcadas a mano.
4. **R4 · Ventas y compras:** al completarse un pedido se suma una venta a quien vende y una compra a quien compra, una sola vez. `db/021` cuenta los pedidos completados que ya existían.
5. **R5 · Privacidad (RNF-03):** la lista sale del token; `orders.invalid_cursor` con un cursor que no es de la lista.

## Diseño
- **Pedidos:** `IOrderRepository.FindSalesAsync`, `SalesSummaryAsync` y `SoldProductIdsAsync` (índice `ix_seller_id`); `OrderService.SalesAsync`; `OrdersController.Sales`.
- **Publicaciones:** `ListingService.MineAsync` pide a M-08 `IOpenOrders.SoldProductIdsAsync` y usa `FindBySellerExceptAsync`/`CountAmongAsync`.
- **Perfil:** `IProfileRepository.AddTradeAsync` (`$inc` con `$setOnInsert` por campo) e `IProfileTrades` (M-02 → M-08), que llama `OrderLifecycleService` al completar.
- **Datos:** `db/021-ventas-compras.js` (`$set` con la cuenta real: idempotente); esquema 21.

## Contratos
- Fila nueva en [`docs/endpoints.md`](../../../docs/endpoints.md): `GET /api/v1/orders/sales` → `SalesPage { items, nextCursor, count, totalCents }`; la fila de `GET /products/mine` explica el cambio de `tab=sold`.
- Fixture `ContractTest/Fixtures/sales-page.json`; cada venta tiene la forma de `order.json`.

## Tests
- `UnitTest/Orders/MyOrdersTests.cs`: `Sales_OnlyMyCompletedSales_WithTheCountAndTheRealTotal`.
- `UnitTest/Orders/OrderLifecycleTests.cs`: completar suma una venta y una compra, una vez.
- `TestShared`: `Sales_OnlyTheCompletedOfTheSeller_WithTheSummaryAndTheirProducts` (pedidos), `SoldExcept_LeavesOutTheGivenIds_AndCountAmongCountsOnlyThatStatus` (publicaciones) y `AddTrade_CountsSalesAndPurchases_AndCreatesTheProfileIfNeeded` (perfiles), en memoria y en MongoDB real.
- `IntegrationTest`: `Sales_ShowTheCompletedOrder_AndSoldOnlyKeepsWhatWasMarkedByHand`.
- `SecurityTest`: 401 sin sesión y 400 con un cursor inválido.
- `ContractTest`: la página y cada venta.
- `DatabaseTest/SalesBackfillScriptTests.cs`: `db/021` cuenta los completados, se puede correr dos veces y no recrea un perfil borrado.

## Criterios
- [x] El vendedor consulta su historial de ventas → `SalesAsync` e integración.
- [x] Cada venta muestra producto, comprador, cantidad, precio, fecha y estado → `OrderResponse` contra `order.json`.
- [x] "Ventas" y "Compras" del perfil cuentan los pedidos completados → `OrderLifecycleTests`, integración y `SalesBackfillScriptTests`.
- [x] Build Release 0/0 · Unit 615 · Integration 169 · Security 222 · Contract 22 (100 %) · `check_endpoints` OK (86) · critic PASS.

## Límites
- Lo marcado como vendido a mano no tiene comprador ni monto real (no hubo pedido); no cuenta en "Ventas" del perfil.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 13-Nov-2026 (detallado e implementado)
- **Detalle:** con las decisiones de la usuaria del 13-Nov-2026.

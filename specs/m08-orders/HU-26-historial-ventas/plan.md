# HU-26 — Consultar mi historial de ventas (plan, API)

1. **T1 — Spec:** decisiones de la usuaria ("Vendidos" de `Mis-productos`; contadores = pedidos completados). ✅
2. **T2 — Ventas:** `FindSalesAsync`, `SalesSummaryAsync`, `GET /orders/sales`. ✅
3. **T3 — "Vendidos" sin repetir:** `IOpenOrders.SoldProductIdsAsync`, `FindBySellerExceptAsync` y `CountAmongAsync`. ✅
4. **T4 — Ventas y compras del perfil:** `AddTradeAsync`, `IProfileTrades` y `db/021`. ✅
5. **T5 — Pruebas y documentación:** todas las suites, catálogo (86), versionado (`0.5.0-alpha.26`, esquema 21) y Memoria. ✅

**Decisiones:**
- **D1 · Ruta propia (`/orders/sales`):** la tarjeta necesita el total de todas las ventas, no solo de la página.
- **D2 · El script usa `$set`:** `infra-up` corre todos los scripts en cada despliegue; con la cuenta real el resultado siempre es el mismo, y coincide con lo que la API suma después.
- **D3 · Una publicación con pedido completado sale como venta:** así "Vendidos" no la repite.

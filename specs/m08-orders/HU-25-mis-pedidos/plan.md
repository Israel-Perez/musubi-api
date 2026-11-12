# HU-25 — Consultar mis pedidos (plan, API)

1. **T1 — Spec:** decisiones de la usuaria (solo compras, pestañas por estado). ✅
2. **T2 — Repositorio:** `FindByBuyerAsync` y `CountByBuyerAsync` en Mongo y en memoria, con la batería compartida. ✅
3. **T3 — Servicio y ruta:** `MyOrderTabs`, `OrderService.MineAsync`, `GET /orders/mine`, `orders.invalid_tab` y `orders.invalid_cursor`. ✅
4. **T4 — Pruebas:** unitarias, integración, seguridad y contrato (`my-order-page.json`). ✅
5. **T5 — Documentación y versión:** `docs/endpoints.md` (83 filas), `docs/errores.md`, `docs/trazabilidad.md`, `docs/versionado.md` (`0.5.0-alpha.23`, esquema 19) y `Memoria.md`. ✅

**Decisiones:**
- **D1 · Sin índice nuevo:** `ix_buyer_id` (`{ buyerId, _id: -1 }`) cubre el filtro, el orden y el cursor; el estado se filtra sobre las compras de una sola persona.
- **D2 · Ruta propia (`/orders/mine`):** `GET /orders` ya exige `conversationId` (HU-23); cambiar su significado rompería el aviso del chat. Sigue el patrón de `/products/mine`.
- **D3 · Sin revisar si la cuenta está activa:** como `GET /orders/{id}`, leer lo propio no cambia nada.

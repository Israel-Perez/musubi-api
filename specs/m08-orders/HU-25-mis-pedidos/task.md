# HU-25 — Consultar mis pedidos (tareas, API)

## T1 — Spec
- **Estado:** ✅ Hecho (12-Nov-2026), con las decisiones de la usuaria.

## T2 — Repositorio
- **Qué:** `FindByBuyerAsync` y `CountByBuyerAsync` (Mongo y memoria).
- **Prueba:** `FindByBuyer_OnlyItsPurchases_NewestFirst_ByStatus_AndCounts` (TestShared; `DatabaseTest` en el CI).
- **Estado:** ✅ Hecho.

## T3 — Servicio y ruta
- **Qué:** `MyOrderTabs`, `OrderService.MineAsync`, `OrdersController.Mine` y los dos errores.
- **Prueba:** `UnitTest/Orders/MyOrdersTests.cs` (7).
- **Estado:** ✅ Hecho.

## T4 — Integración, seguridad y contrato
- **Prueba:** `MyOrders_ShowMyPurchasesByTab_WithTheirCounts_ButNotMySales`, `MyOrders_OnlyMyPurchases_NeverSomeoneElses`, 401 sin sesión, 400 con pestaña o cursor inválidos y `MyOrders_MatchThePageFixture_AndEachItemIsAnOrder`.
- **Estado:** ✅ Hecho.

## T5 — Documentación y versión
- **Qué:** `docs/endpoints.md`, `docs/errores.md`, `docs/trazabilidad.md`, `docs/versionado.md` (sección compartida), `AGENTS.md`, `Memoria.md` y el csproj (`0.5.0-alpha.23`).
- **Estado:** ✅ Hecho: build Release 0/0 · Unit 590 · Integration 167 · Security 216 · Contract 21 · `check_endpoints` OK (83) · critic PASS.

## T6 — Prueba en el celular
- **Qué:** con la API desplegada, "Mis pedidos" en `Perfil` y "Ver todos".
- **Estado:** ⏳ Por hacer.

# HU-26 — Consultar mi historial de ventas (tareas, API)

## T1 — Spec
- **Estado:** ✅ Hecho (13-Nov-2026).

## T2 — Ventas
- **Prueba:** `Sales_OnlyMyCompletedSales_WithTheCountAndTheRealTotal` y el contrato de pedidos.
- **Estado:** ✅ Hecho.

## T3 — "Vendidos" sin repetir
- **Prueba:** `SoldExcept_*` e integración.
- **Estado:** ✅ Hecho.

## T4 — Ventas y compras del perfil
- **Prueba:** `OrderLifecycleTests`, `AddTrade_*` y `SalesBackfillScriptTests`.
- **Estado:** ✅ Hecho.

## T5 — Pruebas y documentación
- **Estado:** ✅ Hecho: Unit 615 · Integration 169 · Security 222 · Contract 22 · `check_endpoints` OK (86).

## T6 — Prueba en el celular
- **Qué:** con la API desplegada (`db/021`), "Vendidos" y los contadores del perfil.
- **Estado:** ⏳ Por hacer.

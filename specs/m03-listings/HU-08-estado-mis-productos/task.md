# HU-08 — Consultar el estado de mis productos (tareas)

## T1 — Detallar la spec
- **Qué:** conciliar Jira (SCRUM-25) con la maqueta (`Mis-productos`) y con HU-07; definir rutas, errores y datos.
- **Verificar:** la usuaria aprueba D1–D5 de `plan.md` (los estados, `sold`, las piezas en HU-50 y la rama ya están decididos).
- **Estado:** ✅ Hecho (09-Nov-2026): D1–D5 aprobadas por la usuaria.

## T2 — Datos
- **Crear:** `db/015-publicacion-vendida.js` (validador completo de `products` con `sold` y `soldAt`); `MongoSchema.ExpectedVersion = 15`.
- **Cambiar:** `IProductRepository` (`FindBySellerAsync` por pestaña y cursor, `CountBySellerAsync`, `MarkSoldAsync`, y `sold` excluido al editar y pausar).
- **Verificar:** la batería `TestShared/Listings/` (en memoria y en MongoDB real) y `DatabaseTest/SchemaScriptTests.cs` (`Products_AcceptASoldProduct_AndStillRejectAnUnknownStatus`).
- **Estado:** ✅ Hecho (09-Nov-2026). `db/015` probado contra un `mongod` 8 temporal; `DatabaseTest` en el CI.

## T3 — Servicio y rutas
- **Cambiar:** `ListingService.MineAsync` y el detalle; `MyListingsService.MarkSoldAsync`; `GET /products/mine`; `POST /products/{id}/sold`.
- **Verificar:** `MyListingsServiceTests` (+3), `MyProductsTabsFlowTests` (4), `MyListingsAuthTests` (+8) y `ListingsContractTests` (fixture `my-product-page.json` y `soldAt` en `product.json`).
- **Estado:** ✅ Hecho (09-Nov-2026).

## T4 — Documentación y versión
- **Actualizar:**
  - `docs/endpoints.md` (1 fila cambiada y 1 nueva), `docs/errores.md`, `docs/modelo-datos.md`, `db/README.md`;
  - `docs/versionado.md` + `<Version>`, la fila de HU-08 en `docs/trazabilidad.md` y `Memoria.md`.
- **Estado:** ✅ Hecho (09-Nov-2026): `0.5.0-alpha.16`, esquema 15.

## T5 — Revisión y PR
- **Qué:** build, suites, guardarraíles, Semgrep, `security-reviewer`, `traceability-clerk`, PR y Jira a "En revisión".
- **Estado:** ⏳ Por hacer.

## Pendientes
- `NOTE (HU-23)`: venta automática al completar pedidos y llegar a 0 piezas (RN-05).
- `NOTE (HU-52)`: estado "Reservada" (apartados).
- `NOTE (HU-25)`: en Vendidos, el comprador y el monto real del pedido.

# HU-24 — Aceptar y actualizar mis pedidos (tareas)

## T1 — Detallar la spec
- **Qué:** resolver RN-19 con la usuaria y definir los endpoints, errores, datos y el `plan.md`.
- **Estado:** ✅ Hecho (11-Nov-2026).
  - RN-19: completan las dos partes.
  - Aceptar requiere el pago elegido.
  - Motivos de una lista fija.

## T2 — Piezas (Listings)
- **Qué:** `IProductRepository.TryReserveStockAsync` / `ReleaseStockAsync` / `MarkSoldIfEmptyAsync` (Mongo con update de pipeline e InMemory) y `IProductStock`, que además refresca la vitrina.
- **Prueba:** `TestShared/Listings/ListingRepositoryContract.cs` (`Stock_ReserveRelease_AndSoldWhenEmpty`).
- **Estado:** ✅ Hecho.

## T3 — Datos de los pedidos
- **Qué:**
  - `OrderHandover`, `OrderCancellation`, `OrderRoles` y `CancelReasons` (la lista con su rol).
  - Repositorio: `AcceptAsync`, `CancelAsync`, `ConfirmHandoverAsync`, `CompleteAsync` y `FindConfirmedByUserAsync`.
  - `db/019-pedidos-estados.js`: validadores de `orders` y `notifications`.
  - `MongoSchema.ExpectedVersion = 19`.
- **Prueba:** `TestShared/Orders/OrderRepositoryContract.cs` (4 pruebas nuevas) y `DatabaseTest/SchemaScriptTests.cs` (019).
- **Estado:** ✅ Hecho. `DatabaseTest` corre en el CI porque no hay Docker local.

## T4 — Servicio y endpoints
- **Qué:**
  - `OrderLifecycleService`.
  - `POST /orders/{id}/accept`, `/cancel` y `/handover`.
  - Cinco errores nuevos.
  - Cuatro tipos de aviso.
  - El borrado de la cuenta libera las piezas.
- **Pruebas:**
  - `UnitTest/Orders/OrderLifecycleTests.cs`: 22.
  - `IntegrationTest/Orders/OrderLifecycleFlowTests.cs`: 3.
  - `SecurityTest/Auth/OrdersAuthTests.cs`: 401, 404, 403 y tope del cuerpo de las tres rutas.
  - `ContractTest/OrdersContractTests.cs`: `order.json` y `order-cancelled.json`.
- **Estado:** ✅ Hecho.

## T5 — Documentación
- **Qué:**
  - `docs/endpoints.md` (80 filas), `docs/errores.md`, `docs/modelo-datos.md`, `docs/trazabilidad.md` y `db/README.md`.
  - `docs/versionado.md` (`0.5.0-alpha.20`, esquema 19).
  - `AGENTS.md` y `Memoria.md`.
  - Notas cerradas en HU-23 y HU-31.
- **Estado:** ✅ Hecho.

## T6 — App y prueba en el celular
- **Qué:** la contraparte en `musubi-app` (HU-24), el despliegue (`infra-up` con `db/019`) y la prueba de punta a punta descrita en `plan.md` §5.
- **Estado:** ⏳ Por hacer.

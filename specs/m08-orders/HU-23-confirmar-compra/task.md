# HU-23 — Confirmar la compra de un producto (tareas)

## T1 — Detallar la spec
- **Qué:** se resolvieron las decisiones con la usuaria (sin IA, lugar de una lista más un detalle, solo efectivo) y se definieron las rutas, los errores y los datos (`plan.md`).
- **Estado:** ✅ Hecho (10-Nov-2026).

## T2 — Datos
- **Qué:** `db/014-pedidos.js` (`orders`, `meetingPoints` con los 14 edificios de la UTTT y `counters`), y `IOrderRepository` e `IMeetingPointRepository` en Mongo y en memoria, con la batería `TestShared/Orders/OrderRepositoryContract.cs`.
- **Estado:** ✅ Hecho. La batería contra Mongo real corre en el CI (`DatabaseTest`).

## T3 — Servicio y endpoints
- **Qué:** `OrderService`, `OrdersController` (5 rutas), `OrdersErrors`, RN-07 en `MyListingsService` y `OrderAccountEraser`. Pruebas:
  - `UnitTest/Orders/OrderServiceTests.cs`;
  - `IntegrationTest/Orders/OrderFlowTests.cs`;
  - `SecurityTest/Auth/OrdersAuthTests.cs`;
  - `ContractTest/OrdersContractTests.cs`.
- **Estado:** ✅ Hecho. Unit 443 · Integration 149 · Security 161 · Contract 16.

## T4 — Documentación
- **Qué:**
  - `docs/endpoints.md` (65 filas), `docs/errores.md`, `docs/modelo-datos.md`, `docs/trazabilidad.md`, `db/README.md` y `AGENTS.md`;
  - `docs/versionado.md` (`0.5.0-alpha.15`, sección compartida con musubi-app);
  - `Memoria.md`.
- **Estado:** ✅ Hecho.

## Pendientes con dueño
- `NOTE (HU-24)`: aceptar o rechazar, apartar y descontar piezas, completar y cancelar.
- `NOTE (HU-25)`: "Mis pedidos" y proponer cerrar la solicitud de un pedido completado (RN-15).
- ~~`NOTE (HU-34)`~~: hecho en HU-34 (`POST /orders` con `agreementId`).
- `NOTE (HU-55)`: transferencia y código de entrega.
- `NOTE (HU-44)`: gestionar los puntos de entrega.
- `NOTE (HU-31)`: avisar a quien vende.

# HU-62 — Recibir un recordatorio de la entrega y avisar "Ya llegué" (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (23-Nov-2026). Decisiones para revisar: solo una hora antes; "Ya llegué" desde 2 h antes y una vez por parte.

## T2 — Datos
- **Qué:** `db/032-recordatorio-entrega.js` (validador de `orders` y `notifications`, `ix_confirmed_delivery`) y los métodos nuevos del repositorio.
- **Prueba:** `TestShared/Orders/OrderRepositoryContract.cs` (UnitTest y DatabaseTest), `DatabaseTest/SchemaScriptTests.cs` y `UnitTest/Data/SchemaVersionTests.cs`.
- **Estado:** ✅ Hecho.

## T3 — Servicio, worker y endpoint
- **Qué:** `OrderArrivalService`, `DeliveryReminderJob`/`DeliveryReminderWorker` y `POST /orders/{id}/arrived`.
- **Prueba:** `UnitTest/Orders/DeliveryReminderTests.cs`, `IntegrationTest/Orders/DeliveryReminderFlowTests.cs`, `SecurityTest/Auth/OrdersAuthTests.cs` y `ContractTest/OrdersContractTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Documentación
- **Qué:** `docs/endpoints.md` (126), `docs/errores.md`, `docs/modelo-datos.md`, `db/README.md`, `docs/trazabilidad.md`, `docs/versionado.md` (alpha.43), README, `AGENTS.md` y `Memoria.md`.
- **Estado:** ✅ Hecho.

## T5 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.43`: recibir el recordatorio, "Ya llegué" desde el pedido y el chat, y "Agregar al calendario".
- **Estado:** ⏳ Por hacer.

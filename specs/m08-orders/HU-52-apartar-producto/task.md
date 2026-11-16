# HU-52 — Apartar un producto y verificar la entrega con QR o código (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (15-Nov-2026), con las decisiones de la usuaria.

## T2 — Datos
- **Qué:** `db/028-apartados.js` y los repositorios `Mongo*`/`InMemory*` de `reservations`.
- **Prueba:** `TestShared/Orders/ReservationRepositoryContract.cs` (UnitTest y DatabaseTest) y `DatabaseTest/SchemaScriptTests.cs`.
- **Estado:** ✅ Hecho.

## T3 — Servicio y endpoints
- **Qué:** `ReservationService`, el vencimiento, el borrado de la cuenta y `ReservationsController`.
- **Prueba:** `UnitTest/Orders/ReservationServiceTests.cs`, `IntegrationTest/Orders/ReservationFlowTests.cs`, `SecurityTest/Auth/ReservationsAuthTests.cs` y `ContractTest/ReservationsContractTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Documentación
- **Estado:** ✅ Hecho: Build Release 0/0 · Unit 840, Integration 193, Security 266 y Contract 28 · `check_endpoints` OK (112) · critic PASS.

## T5 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.36`: apartar, verificar con la cámara y con el código, y entregar.
- **Estado:** ⏳ Por hacer.

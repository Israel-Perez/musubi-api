# HU-55 — Pagar un pedido por transferencia o en efectivo (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (15-Nov-2026), con las decisiones de la usuaria.

## T2 — Datos
- **Qué:** `db/027-pagos.js`, `payoutAccounts`, `orders.payment` y los repositorios `Mongo*`/`InMemory*`.
- **Prueba:** `TestShared/Orders/OrderRepositoryContract.cs` y `PayoutAccountRepositoryContract.cs` (UnitTest y DatabaseTest), `DatabaseTest/SchemaScriptTests.cs`.
- **Estado:** ✅ Hecho.

## T3 — Servicio y endpoints
- **Qué:** `PayoutService`, `OrderPaymentService`, `OrderLifecycleService` (aceptar y código), `TransferReminderWorker` y los controllers.
- **Prueba:** `UnitTest/Orders/PayoutTests.cs`, `OrderPaymentTests.cs`, `OrderServiceTests.cs`, `IntegrationTest/Orders/PaymentFlowTests.cs`, `SecurityTest/Auth/PaymentsAuthTests.cs` y `ContractTest/PaymentsContractTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Documentación
- **Qué:** `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `db/README.md`, `docs/versionado.md`, `docs/trazabilidad.md`, `01-04`, `infra/README.md`, `AGENTS.md` y `Memoria.md`.
- **Estado:** ✅ Hecho: Build Release 0/0 · Unit 820, Integration 191, Security 257 y Contract 27 · `check_endpoints` OK (106) · critic PASS.

## T5 — Comprobante (NOTE (HU-55))
- **Qué:** "Adjuntar comprobante · opcional" (`Pago-transferencia`) y "Comprobante de Ana · Ver" (`Pago-vendedor`) con almacenamiento privado (S3 sin CDN público, enlace firmado que vence, solo las partes).
- **Estado:** ⏳ Por hacer (decisión de la usuaria del 15-Nov-2026: después).

## T6 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.35` desplegada y `/musubi/prod/Payments/EncryptionKey` en Parameter Store: registrar la cuenta, comprar por transferencia y en efectivo con el código.
- **Estado:** ⏳ Por hacer.

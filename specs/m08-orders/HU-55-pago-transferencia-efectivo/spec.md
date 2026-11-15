# HU-55 — Pagar un pedido por transferencia o en efectivo

**HU / Jira / Sprint / Módulo / Contraparte:** HU-55 / SCRUM-72 / Sprint 5 (14 nov – 1 dic 2026) / M-08 / [musubi-app:HU-55](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-55-pago-transferencia-efectivo)

## Contexto
Jira (SCRUM-72): Como **comprador**, quiero **pagar un pedido por transferencia a la cuenta verificada del vendedor o en efectivo al recibirlo**, para **cerrar la compra sin comisiones**.

- Maqueta: `Pedido-pago`, `Pago-transferencia`, `Pago-validando`, `Pago-vendedor`, `Pedido-entrega`, `Pedido-efectivo` y `Cuenta-pagos`.
- Origen: maqueta (secciones 9 y 10). Decisión D-01 (21-Oct-2026): el modelo de pago de la maqueta **reemplaza a HU-43** (Mercado Pago, fuera de alcance). Musubi no procesa el dinero.
- Trazabilidad: RNF-03, RNF-05, RNF-15 · R10 del modelo de datos (datos sensibles cifrados).
- **Decisiones de la usuaria (15-Nov-2026):**
  - Si quien compra ya avisó "Ya transferí" y quien vende no confirma, a las 24 h se le recuerda **una vez** y el pedido **sigue pendiente** (no se cancela solo: el dinero ya está con quien vende).
  - Solo se guarda la **CLABE**; el número de tarjeta de la maqueta ("opcional") no se pide.
  - "Adjuntar comprobante · opcional" se deja para después (`NOTE (HU-55)`): hoy las fotos se sirven públicas por el CDN y un comprobante trae datos del banco.

**Depende de:** `HU-23` (el pedido y "¿Cómo quieres pagar?"), `HU-24` (aceptar y confirmar la entrega), `HU-31` (avisos), `HU-46` (borrar la cuenta).

## Requisitos
1. **R1 · Cuenta para recibir pagos:** la persona registra el titular y la CLABE. La CLABE se valida (18 dígitos, dígito verificador de Banxico y clave de banco de un participante de SPEI) y el titular debe ser ella misma: al menos dos palabras y todas en su nombre verificado, sin importar acentos ni mayúsculas. Se guarda cifrada con AES-256-GCM (llave `Payments:EncryptionKey`, solo en el back; el id de la persona es dato asociado) y solo se muestra enmascarada ("•••• 4821"). Se puede reemplazar o quitar, y se borra con la cuenta (HU-46).
2. **R2 · Elegir transferencia:** `PUT /orders/{id}/payment` acepta `transfer` solo si quien vende tiene cuenta y hay llave (`transferAvailable`, "Diego tiene su cuenta registrada"). Se puede cambiar de forma de pago hasta avisar "Ya transferí".
3. **R3 · Pagar por transferencia:** quien compra ve el monto exacto, el banco, el titular, la CLABE completa y el concepto (`MS-2481`). Quien vende ve su CLABE enmascarada. Quien compra captura la referencia (4 a 30 letras o números) y se le avisa a quien vende (`transfer_reported`).
4. **R4 · Validar la transferencia:** "Sí, me llegó el pago" es aceptar el pedido (HU-24): solo con la referencia capturada; el pedido pasa a Confirmado, se apartan las piezas y se avisa "¡Diego confirmó tu pago!". "Todavía no me llega" avisa a quien compra (`transfer_not_received`), que puede volver a mandar la referencia.
5. **R5 · Recordatorio:** a las 24 h de "Ya transferí" sin confirmar, `TransferReminderWorker` avisa a quien vende una vez (`transfer_reminder`, `dedupeKey`), sin cancelar el pedido.
6. **R6 · Efectivo con código:** al aceptar un pedido en efectivo nace un código de 4 dígitos (generador criptográfico) que solo ve quien compra, y solo ya confirmado. Quien vende lo escribe al entregar y el pedido se completa (cuenta como la entrega de los dos, RN-19; publicación vendida, ventas del perfil y señal de compra como en HU-24). Cada intento cuenta, hasta 5; luego se bloquea y la entrega se confirma con los dos botones de HU-24.
7. **R7 · Sin comisión:** Musubi no cobra ni procesa el dinero (lo dice la app).
8. **R8 · Solo las partes (RNF-03, RNF-05):** los datos de la transferencia solo los ven quien compra y quien vende; a cualquier otra persona, 404.
9. **R9 · Sin llave (RNF-15):** en Production sin `Payments:EncryptionKey` no se guardan cuentas (`503 payouts.unavailable`) y solo se paga en efectivo. En Development, sin llave, se usa una temporal.

## Diseño
- `Modules/Orders/` (M-08):
  - `Payouts.cs`: `Clabe`, `MexicanBanks` (Banxico, 15-Nov-2026), `HolderNames`, `PaymentsOptions` y `ClabeProtector`.
  - `PayoutService` (+ `IPayoutDirectory` para los pedidos), `PayoutsController` (`/payouts/*`), `PayoutAccountEraser`.
  - `OrderPaymentService`: datos de la transferencia, referencia y "todavía no me llega". También `TransferReminderJob` / `TransferReminderWorker`.
  - `OrderLifecycleService.AcceptAsync`: transferencia → `ConfirmTransferAsync`; efectivo → `AcceptAsync` con el código.
  - `OrderLifecycleService.CompleteWithCodeAsync`: el intento se cuenta antes de comparar y se compara en tiempo constante.
  - `OrderService.PaymentFor`: el código solo a quien compra; los intentos que quedan, solo a quien vende.
- Datos:
  - `payoutAccounts` (`_id` = la persona);
  - `orders.payment` con `reference`, `reportedAt`, `notReceivedAt`, `validatedAt`, `deliveryCode` y `codeAttempts`;
  - `ix_transfer_reported`;
  - `db/027-pagos.js` y esquema 27.
- Avisos: `transfer_reported`, `transfer_not_received` y `transfer_reminder` (destino `order`).

## Contratos
- Filas de M-08 en [`docs/endpoints.md`](../../../docs/endpoints.md):
  - `PUT /orders/{id}/payment` y `POST /orders/{id}/accept`, actualizadas;
  - nuevas: `GET`/`POST /orders/{id}/transfer`, `POST /orders/{id}/transfer/not-received`, `POST /orders/{id}/delivery-code` y "Cuenta para recibir pagos" (`/payouts/banks`, `/payouts/account`).
- Errores `orders.*` y `payouts.*` en [`docs/errores.md`](../../../docs/errores.md).
- Fixtures `transfer-details.json` y `payout-account.json`; `order.json` y `order-cancelled.json` con el pago nuevo.

## Tests
- `UnitTest/Orders/PayoutTests.cs`: dígito verificador, bancos, titular, cifrado ligado a su dueña, sin llave, guardar, quitar y borrar con la cuenta.
- `UnitTest/Orders/OrderPaymentTests.cs`: CLABE completa o enmascarada, referencia, "todavía no me llega", recordatorio de 24 h sin cancelar, el código solo para quien compra, código correcto, 5 intentos y bloqueo.
- `UnitTest/Orders/OrderServiceTests.cs` → `SetPayment_Transfer_WhenTheSellerHasAnAccount_AndItCanChangeUntilReported`.
- `TestShared/Orders/OrderRepositoryContract.cs` (transferencia, intentos del código, completar con código, recordatorio) y `PayoutAccountRepositoryContract.cs`, en memoria y en MongoDB real (`DatabaseTest`, validador de `db/027`).
- `IntegrationTest/Orders/PaymentFlowTests.cs`: transferencia de punta a punta y efectivo con código.
- `SecurityTest/Auth/PaymentsAuthTests.cs`: 401 sin sesión, 404 a otra persona, solo quien compra avisa y Production sin llave.
- `ContractTest/PaymentsContractTests.cs`.

## Criterios
- [x] El vendedor registra su CLABE, que se valida y se guarda cifrada; solo se muestra enmascarada → `PayoutTests`, `PaymentFlowTests`.
- [x] Por transferencia, el comprador ve la cuenta y la referencia; el vendedor valida y el pedido pasa a Confirmado → `OrderPaymentTests`, `PaymentFlowTests`.
- [x] En efectivo, el comprador recibe un código de 4 dígitos que el vendedor captura y el pedido pasa a Completado → `OrderPaymentTests`, `PaymentFlowTests`.
- [x] Musubi no cobra comisión ni procesa el dinero → la app lo dice (contraparte).
- [x] Solo las partes ven los datos de pago → `PaymentsAuthTests`, `OrderPaymentTests`.
- [x] Build Release 0/0 · Unit 820, Integration 191, Security 257 y Contract 27 · `check_endpoints` OK (106) · critic PASS.

## Límites
- `NOTE (HU-55)`: "Adjuntar comprobante · opcional" con almacenamiento privado (S3 sin CDN público y enlace firmado).
- La llave de cifrado no se rota: con otra llave, las cuentas guardadas ya no se leen y hay que registrarlas de nuevo (`infra/README.md`).
- Un pedido aceptado en efectivo antes de HU-55 no tiene código: se completa con los dos botones de HU-24 (`orders.code_locked`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 15-Nov-2026 (detallado e implementado, con las decisiones de la usuaria)

# HU-24 — Aceptar y actualizar mis pedidos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-24 / SCRUM-43 / Sprint 4 (9 – 20 nov 2026) / M-08 / [musubi-app:HU-24](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-24-actualizar-pedidos)

## Contexto
Jira (SCRUM-43): Como **vendedor**, quiero **aceptar y actualizar el estado de mis pedidos**, para **llevar control de las entregas**.

- Maqueta: `Pedido-pago` (los cuatro estados del pedido), `Pedido-entrega` ("Confirmar entrega") y `Pedido-efectivo` ("Cancelar pedido"). La validación de la transferencia (`Pago-vendedor`) y el código de entrega son de HU-55 y HU-52.
- Trazabilidad: RF-31, RF-33, RF-34 · CU-18 · RN-16, RN-19, RN-20 · RN-05 (estado de la publicación según los pedidos).
- Decisiones de la usuaria (11-Nov-2026):
  - **RN-19:** el pedido se completa cuando **las dos partes** confirman la entrega: quien vende marca "Entregué" y quien compra marca "Recibí". Cuando llegue el código de entrega (HU-52), ingresarlo contará como la confirmación de quien vende.
  - **Aceptar:** quien vende solo acepta un pedido después de que quien compra eligió cómo pagar. Así lo muestra la maqueta: el pedido pasa a Confirmado después del pago.
  - **Motivo (RN-20):** se elige de una lista fija, sin texto libre. No pasa por moderación y se puede contar.

**Depende de:** `02-01`, `02-03`, `HU-23` (pedidos), `HU-31` (avisos), `HU-08` (publicación vendida), `HU-57` (bloqueos).

## Requisitos
1. **R1 · Aceptar (RF-34):** quien vende acepta un pedido `pending` cuyo comprador ya eligió el pago. El pedido pasa a `confirmed` y se **apartan** sus piezas de la publicación: dejan de estar disponibles para otros. Si a la publicación ya no le alcanzan las piezas, responde `409 orders.not_enough_stock`. Si está pausada, vendida o quitada, responde `409 orders.product_unavailable`.
2. **R2 · Solo quien vende acepta:** quien compra recibe `403 orders.only_seller`. Si todavía no se eligió el pago, responde `409 orders.payment_missing`. Si el pedido ya no está pendiente, responde `409 orders.not_pending`. Con un bloqueo entre los dos (HU-57), responde `403 orders.blocked`.
3. **R3 · Estados (RF-31):** `pending` (Pendiente), `confirmed` (Confirmado), `completed` (Completado) y `cancelled` (Cancelado). Cada cambio queda en `history` con su hora.
4. **R4 · Rechazar o cancelar (RN-20):** cualquiera de las dos partes cancela un pedido `pending` o `confirmed`, indicando el motivo. Que quien vende cancele un pedido pendiente es "rechazarlo". Si estaba confirmado, sus piezas **se liberan**: regresan a la publicación, y una agotada vuelve a estar disponible. Un pedido completado o cancelado responde `409 orders.closed`.
5. **R5 · Motivos:** el motivo es un código de esta lista (si no es válido, o no corresponde al rol de quien cancela, responde `400 orders.invalid_reason`):

   | Código | Texto | Quién |
   |---|---|---|
   | `changed_mind` | Ya no lo necesito | quien compra |
   | `unavailable` | Ya no tengo el producto | quien vende |
   | `no_agreement` | No nos pusimos de acuerdo | los dos |
   | `schedule` | No puedo en la fecha acordada | los dos |
   | `no_show` | La otra persona no llegó | los dos |
   | `other` | Otro motivo | los dos |

   El sistema usa además `account_deleted` (HU-46), que no se puede elegir.
6. **R6 · Confirmar la entrega (RN-19):** en un pedido `confirmed`, cada parte confirma su lado: quien vende "Entregué" y quien compra "Recibí". Repetirlo no cambia nada. Cuando confirman los dos, el pedido pasa a `completed` y las piezas apartadas quedan **descontadas** para siempre. En un pedido que no está confirmado, responde `409 orders.not_confirmed`.
7. **R7 · Vendida (RN-05):** al completarse un pedido, si la publicación se quedó sin piezas y no tiene otros pedidos abiertos, pasa a **Vendida** (`sold`, con `soldAt`; HU-08). Mientras las piezas solo están apartadas, la publicación aparece como agotada (`soldOut`).
8. **R8 · Avisos (HU-31, RF-41):** la otra parte recibe un aviso (push y centro de notificaciones, pestaña Pedidos) cuando su pedido se acepta (`order_confirmed`), se rechaza o se cancela (`order_cancelled`, con el motivo), cuando la otra parte confirma su lado de la entrega (`order_handover`) y cuando el pedido se completa (`order_completed`).
9. **R9 · Lo que ve cada parte:** `OrderResponse` agrega `handover { buyerConfirmedAt, sellerConfirmedAt }`, `cancellation { by, reason, at }` (o `null`) y `updatedAt`.
10. **R10 · Solo las partes (RNF-03, OWASP API1):** para cualquier otra persona, las tres acciones responden `404 orders.not_found`. Una cuenta no activa recibe 403 (RN-01).
11. **R11 · Borrar la cuenta (HU-46):** si se cancela un pedido confirmado porque una de las partes borró su cuenta, sus piezas también se liberan.
12. **R12 · A la vez:** cada cambio de estado se aplica solo si el pedido sigue en el estado esperado (filtro en la misma escritura). Si dos personas actúan al mismo tiempo, gana la primera y la segunda recibe el error del estado nuevo. Si las piezas se apartaron pero el pedido ya había cambiado, se devuelven.

## Diseño
- **Endpoints nuevos en `OrdersController`:** `POST /orders/{id}/accept`, `POST /orders/{id}/cancel` (cuerpo `{ reason }`) y `POST /orders/{id}/handover`. Los tres responden el `OrderResponse` actualizado.
- **Piezas sin acoplar módulos:** Listings expone `IProductStock` con `TryReserveAsync` (resta solo si alcanzan y la publicación está `active`; si queda en 0, la pasa a `soldOut`), `ReleaseAsync` (suma y una `soldOut` vuelve a `active`) y `MarkSoldIfEmptyAsync` (de `soldOut` con 0 piezas a `sold`). Las tres son una sola escritura atómica en MongoDB (update con pipeline).
- **Qué publicación:** la de `item.productId`, ya sea del pedido de una publicación o de una oferta vinculada (HU-16). Una oferta directa (sin publicación) no aparta piezas.
- **Repositorio:** `AcceptAsync`, `CancelAsync`, `ConfirmHandoverAsync` y `CompleteAsync`, cada uno un `FindOneAndUpdate` filtrado por el pedido, la parte y el estado esperado.
- **Avisos:** con el mismo `INotificationCenter` de HU-31, en la pestaña `orders` y con destino `order`.
- **Datos (`db/019-pedidos-estados.js`, esquema 19):** el validador de `orders` agrega `handover` y `cancellation` (motivo de la lista de R5). No hay índices nuevos.

## Contratos
- Filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md) (M-08): `POST /api/v1/orders/{id}/accept`, `POST /api/v1/orders/{id}/cancel` y `POST /api/v1/orders/{id}/handover`. `OrderResponse` cambia en todas las filas de pedidos (R9).
- Errores nuevos en [`docs/errores.md`](../../../docs/errores.md): `orders.only_seller`, `orders.payment_missing`, `orders.invalid_reason`, `orders.closed` y `orders.not_confirmed`.
- Fixture `ContractTest/Fixtures/order.json` con los campos nuevos.
- `orders` en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md): `handover`, `cancellation` y el script 019.

## Tests
- `UnitTest/Orders/OrderLifecycleTests.cs`: R1 a R12 con repositorios en memoria y reloj fijo (aceptar, piezas, rechazar, cancelar, motivos por rol, entrega de los dos, vendida, avisos y carreras).
- `TestShared/Orders/OrderRepositoryContract.cs` y `TestShared/Listings/`: las transiciones y las piezas, en memoria y en MongoDB (`DatabaseTest`).
- `IntegrationTest/Orders/OrderLifecycleFlowTests.cs`: el flujo completo por HTTP (crear → pagar → aceptar → entregar → completado → vendida) y cancelar con piezas liberadas.
- `SecurityTest/Auth/OrdersAuthTests.cs`: 401 en las tres rutas nuevas, 404 para quien no es parte, 403 para quien compra al aceptar, ids inválidos y tope del cuerpo.
- `ContractTest/OrdersContractTests.cs`: la forma nueva de `OrderResponse`.
- `DatabaseTest/SchemaScriptTests.cs`: el script 019 acepta `handover` y `cancellation` y rechaza un motivo desconocido.

## Criterios
- [ ] El vendedor acepta o rechaza pedidos **Pendientes**; al aceptarlo pasa a **Confirmado** y la cantidad se aparta (R1, R2, R4, RF-34).
- [ ] Los estados del pedido son **Pendiente**, **Confirmado**, **Completado** y **Cancelado** (R3, RF-31).
- [ ] El pedido pasa a **Completado** cuando ambas partes confirman la entrega y la cantidad se descuenta (R6, RN-19).
- [ ] Cualquiera de las partes puede cancelar antes de completar, indicando el motivo; la cantidad se libera (R4, R5, RN-20).
- [ ] Si la cantidad llega a cero, la publicación pasa a **Vendida** (R7).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS · Semgrep sin hallazgos.
- [ ] `DatabaseTest` en el CI.

## Límites
- `NOTE (HU-52)`: el código de entrega cuenta como la confirmación de quien vende.
- `NOTE (HU-55)`: quien vende valida la transferencia antes de aceptar (`Pago-vendedor`).
- `NOTE (HU-25)`: "Mis pedidos" (la lista con los pedidos por estado).
- `NOTE (HU-27/HU-45)`: calificar a la otra parte al completar (`Calificar`).
- La maqueta no tiene pantalla para que quien vende acepte o rechace. La app usa las piezas de `Pedido-pago` (ver la spec de la app).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 11-Nov-2026 (detallado)
- **Detalle:** RN-19 se resolvió con la usuaria: completan las dos partes. Aceptar requiere que ya se haya elegido el pago, y el motivo de cancelar es de una lista fija.

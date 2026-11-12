# HU-23 — Confirmar la compra de un producto

**HU / Jira / Sprint / Módulo / Contraparte:** HU-23 / SCRUM-42 / Sprint 4 (9 – 20 nov 2026) / M-08 / [musubi-app:HU-23](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-23-confirmar-compra)

## Contexto
Jira (SCRUM-42): Como **comprador**, quiero **confirmar la compra de un producto**, para **formalizar el trato con el vendedor**.

- Maqueta: `Acuerdo-confirmado` ("Resumen del acuerdo" → "Crear pedido") y `Pedido-pago` ("Tu pedido", "¿Cómo quieres pagar?"). En la maqueta, "Comprar" (`Producto`) abre el chat y, desde ahí, "¿Ya se pusieron de acuerdo?" lleva al resumen.
- Trazabilidad: RF-30, RF-31, RF-34 · CU-17 · RN-01, RN-06, RN-16, RN-17, RN-18 (Entregable 1), RN-07.
- Decisiones de la usuaria (10-Nov-2026):
  - **Sin IA:** el resumen se llena con los datos del producto o de la oferta aceptada y el comprador lo completa. HU-32 a HU-34 agregan después la propuesta de la IA y la confirmación de los dos encima del mismo pedido.
  - **Lugar:** una lista de puntos del campus, más un campo libre para precisar ("Planta baja, junto a la entrada"). El punto del edificio de la carrera de quien compra se recomienda primero.
  - **Pago:** solo efectivo al entregar (RN-18 del Entregable 1). La transferencia, la cuenta CLABE y el código de entrega llegan con HU-55 (D-01).

**Depende de:** `02-01`, `02-03`, `02-07` (moderación), `HU-20` (conversaciones), `HU-17` (ofertas aceptadas), `HU-57` (bloqueos), `HU-04` (carrera del perfil).

## Requisitos
1. **R1 · Desde el chat:** el comprador crea el pedido desde una conversación suya (RN-21). Con una publicación (`product`), el pedido es de esa publicación a su precio. Con una oferta aceptada (`offer`, HU-17), el pedido es de la oferta a su precio. Quien vende en esa conversación no crea pedidos (`403 orders.only_buyer`).
2. **R2 · Datos (RF-30):** el pedido guarda una copia del producto (título, foto y precio; R8), la cantidad, el total, el punto de entrega, el detalle libre y la fecha y hora. El número visible es `MS-NNNN`, consecutivo por campus.
3. **R3 · Pendiente (RN-16):** todo pedido nace `pending`. Pasa a `confirmed` solo cuando el vendedor lo acepta (HU-24).
4. **R4 · Cantidad (RN-17):** de 1 a lo disponible de la publicación (y como máximo 10). Una oferta directa es de 1 a la cantidad de la solicitud. Crear el pedido **no aparta** piezas: se apartan al aceptarlo (RF-34, HU-24).
5. **R5 · Qué se puede comprar:** solo una publicación `active` del mismo campus y una oferta `accepted`. Una pausada, agotada, vendida (HU-08) o quitada responde `409 orders.product_unavailable`. Con un bloqueo en cualquier dirección (HU-57) responde `403 orders.blocked`, y no se compra lo propio (RN-06).
6. **R6 · Uno abierto por conversación:** si ya hay un pedido `pending` o `confirmed` en esa conversación, responde `409 orders.already_open` con su `orderId`. Así un doble toque no duplica el pedido.
7. **R7 · Entrega:** el punto es uno de los activos del campus. El detalle es opcional, de hasta 120 caracteres, y se modera (02-07). La fecha y hora va de 30 minutos a 14 días desde ahora, entre las 7:00 y las 21:00 del campus (UTC-6).
8. **R8 · Puntos recomendados:** `GET /orders/meeting-points` devuelve los puntos del campus en su orden y marca `suggested` el del edificio de la carrera de quien consulta (su perfil, HU-04). Sin carrera, ninguno sale marcado.
9. **R9 · Pago al entregar (RN-18):** el comprador elige "Efectivo al entregar" en un pedido `pending` y queda registrado (`payment.method = cash`). La transferencia responde `409 orders.payment_unavailable` hasta HU-55, porque nadie tiene cuenta registrada.
10. **R10 · Solo las partes (RNF-03, OWASP API1):** un pedido solo lo ven su comprador y su vendedor. Para cualquier otra persona responde `404 orders.not_found`. Una cuenta no activa recibe 403 (RN-01).
11. **R11 · No se elimina con pedidos (RN-07):** una publicación con pedidos `pending` o `confirmed` no se elimina (`409 listings.product_has_orders`). Sí se puede editar y pausar.
12. **R12 · Borrar la cuenta (HU-46, R11):** al eliminar una cuenta, sus pedidos abiertos se cancelan (motivo `account_deleted`) y su nombre se reemplaza en los pedidos que conserva la otra parte.

## Diseño
- **Módulo nuevo `Musubi.Api/Modules/Orders/`** (M-08): `OrdersController`, `OrderService`, `OrdersErrors`, `OrdersDtos`, `Data/` (documentos, `IOrderRepository` con `Mongo*` e `InMemory*`, `IMeetingPointRepository`) y `OrdersModuleServiceCollectionExtensions`.
- **Sin acoplar módulos:** lee la conversación con `IConversationDirectory` (se agrega `FindAsync`), la publicación con `IProductDirectory`, la oferta con un `IOfferDirectory` nuevo (PurchaseRequests), los bloqueos con `IBlockDirectory`, el nombre con `IMemberDirectory` y la carrera con `IProfileDirectory`. Listings pregunta por pedidos abiertos con un `IOpenOrders` que expone Orders.
- **Número `MS-NNNN`:** un contador por campus en `counters` (`$inc` atómico con `FindOneAndUpdate`, upsert). Empieza en 1001.
- **Uno abierto por conversación:** un índice único parcial `ux_conversation_open` sobre `conversationId` cuando `status` es `pending` o `confirmed`. Gana el primero; el segundo recibe la llave duplicada y responde `already_open`.
- **Datos (`db/014-pedidos.js`, esquema 14):** colecciones `orders`, `meetingPoints` (los 14 edificios de la UTTT con sus carreras) y `counters`. Si HU-08 se fusiona antes, este script pasa a `015`.
- **Moderación:** solo el detalle libre de la entrega.

## Contratos
- Filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md) (M-08): `GET /api/v1/orders/meeting-points`, `POST /api/v1/orders`, `GET /api/v1/orders/{id}`, `GET /api/v1/orders?conversationId=` y `PUT /api/v1/orders/{id}/payment`.
- Errores `orders.*` y `listings.product_has_orders` en [`docs/errores.md`](../../../docs/errores.md).
- Fixtures `ContractTest/Fixtures/order.json` y `meeting-points.json`.
- Colecciones `orders`, `meetingPoints` y `counters` en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md).

## Tests
- `UnitTest/Orders/OrderServiceTests.cs`: R1 a R12 con repositorios en memoria y reloj fijo.
- `TestShared/Orders/OrderRepositoryContract.cs`: crear, uno abierto por conversación, número consecutivo, buscar por conversación, pago y borrado de cuenta; en memoria y en MongoDB (`DatabaseTest`).
- `IntegrationTest/Orders/OrderFlowTests.cs`: desde una publicación y desde una oferta aceptada, cantidad, entrega, efectivo, `already_open` y eliminar una publicación con pedido.
- `SecurityTest/Auth/OrdersAuthTests.cs`: 401 en las 5 rutas, 404 para quien no es parte, 403 para el vendedor y para una cuenta suspendida, ids inválidos y tope del cuerpo.
- `ContractTest/OrdersContractTests.cs`: forma de `OrderResponse` y de los puntos.
- `DatabaseTest/SchemaScriptTests.cs`: el script, sus índices y los puntos de la UTTT.

## Criterios
- [x] El comprador genera un pedido desde el detalle de un producto (por su chat) o desde una oferta aceptada (R1). Desde un acuerdo confirmado: `NOTE (HU-34)`.
- [x] El pedido registra producto, cantidad, precio, lugar y fecha de entrega (R2, R7).
- [x] Todo pedido inicia como **Pendiente** (R3, RN-16).
- [x] La cantidad no puede superar la disponible (R4, RN-17) y no se pueden comprar productos propios (R5, RN-06).
- [x] El pago se realiza al momento de la entrega y queda registrado en el pedido (R9, RN-18 del Entregable 1).
- [x] Build Release 0/0 · Unit 443, Integration 149, Security 161 y Contract 16 al 100 % · `check_endpoints` OK (65) · critic PASS · Semgrep sin hallazgos.
- [ ] `DatabaseTest` en el CI.

## Límites
- ~~`NOTE (HU-24)`~~ Cerrada el 11-Nov-2026: el vendedor acepta o rechaza, se apartan y descuentan piezas, y se completa o cancela ([HU-24](../HU-24-actualizar-pedidos/spec.md)).
- `NOTE (HU-25)`: "Mis pedidos" (lista) y proponer cerrar la solicitud de un pedido completado (RN-15).
- `NOTE (HU-34)`: el pedido sale del acuerdo confirmado por los dos (RF-47), con la propuesta de la IA.
- `NOTE (HU-55)`: transferencia a la cuenta CLABE, validación del vendedor y código de entrega de 4 dígitos.
- `NOTE (HU-44)`: que el administrador gestione los puntos de entrega (hoy vienen del script).
- `NOTE (HU-31)`: avisar al vendedor del pedido nuevo (hoy lo ve en el chat y en HU-24).
- HU-43 (Mercado Pago) queda fuera de alcance (D-01).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 10-Nov-2026 (detallado e implementado)
- **Detalle:** el pedido sale del chat sin IA, el punto de entrega es de una lista con un detalle libre y el edificio de la carrera se recomienda primero, y solo se paga en efectivo al entregar.

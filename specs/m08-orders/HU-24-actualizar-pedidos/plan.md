# plan.md — HU-24 Aceptar y actualizar mis pedidos

## 1. Componentes
- `Musubi.Api/Modules/Orders/`:
  - `OrderLifecycleService.cs` (nuevo): aceptar, cancelar y confirmar la entrega.
  - `OrdersController.cs`: tres rutas.
  - `OrdersDtos.cs`: `CancelOrderRequest`, `OrderHandoverResponse` y `OrderCancellationResponse`.
  - `OrdersErrors.cs`: cinco códigos nuevos.
  - `Data/OrderDocuments.cs`: `OrderHandover`, `OrderCancellation`, `CancelReasons` y `OrderRoles`.
  - `Data/OrderRepositories.cs`: cuatro transiciones nuevas.
  - Además: el borrado de la cuenta libera las piezas.
- `Musubi.Api/Modules/Listings/`:
  - `ProductStock.cs` (nuevo): `IProductStock`, que usa `IProductRepository`.
  - `IProductRepository`: tres métodos atómicos de piezas.
- `Musubi.Api/Modules/Notifications/NotificationKinds.cs`: cuatro tipos nuevos.
- `db/019-pedidos-estados.js` y `MongoSchema.ExpectedVersion = 19`.

## 2. Decisiones
- **Un servicio aparte (`OrderLifecycleService`).** `OrderService` ya mide 460 líneas y cubre crear y pagar; las transiciones tienen sus propias dependencias (piezas y avisos).
- **Apartar = restar de `stock`.** No se agregó un contador `reserved`. La vitrina, "Mis productos" y el pedido nuevo ya leen `stock` como "disponibles", así que apartar las piezas las saca de todos esos lados sin cambiar las consultas. Completar no las vuelve a restar; cancelar un pedido confirmado las suma de nuevo.
- **Agotada mientras está apartada y vendida al completar.** La maqueta de HU-08 distingue "Agotado" (sigue visible) de "Vendido". Una publicación con todo apartado puede regresar si se cancela el pedido; por eso pasa a `sold` solo cuando ya no queda nada por entregar.
- **Las dos partes confirman (RN-19):** se guarda `handover.buyerConfirmedAt` y `handover.sellerConfirmedAt`. Quien confirma en segundo lugar dispara `CompleteAsync`, que filtra por las dos fechas: si las dos llegan al mismo tiempo, solo una completa.
- **Motivos con rol:** "Ya no lo necesito" es de quien compra y "Ya no tengo el producto" es de quien vende. Así el aviso a la otra parte siempre se lee bien.
- **Aceptar con el pago elegido:** decisión de la usuaria. El filtro de la escritura incluye `payment` existente, para que un cambio a la vez no lo salte.

## 3. Excepciones al stack
Ninguna.

## 4. Guardarraíles
- **Solo las partes:** el filtro de cada escritura incluye `buyerId` o `sellerId` (OWASP API1), y el servicio responde 404 a quien no es parte.
- **Tamaño del cuerpo:** `RequestSizeLimit(16 KB)` en `cancel`; `accept` y `handover` no llevan cuerpo.
- **El motivo solo puede ser un código de la lista.** El texto no se guarda: lo pone cada cliente.

## 5. Validación
- **Pruebas automáticas:** Unit, Integration, Security, Contract y la batería de `TestShared` en memoria y en MongoDB (CI).
- **Prueba en el celular, con la app de HU-24:**
  1. Crear el pedido y pagar en efectivo.
  2. Aceptarlo desde la otra cuenta.
  3. Confirmar la entrega desde las dos cuentas.
  4. Revisar los avisos push y que la publicación aparezca vendida en "Mis productos".

## 6. Secuencia
1. Piezas en Listings (repositorio, `IProductStock` y batería).
2. Documentos y repositorio de pedidos (transiciones y batería).
3. `OrderLifecycleService` con sus pruebas unitarias.
4. Controller, contrato, seguridad e integración.
5. `db/019`, docs, versión `0.5.0-alpha.20` y Memoria.

# plan.md — HU-52 Apartar un producto y verificar la entrega con QR o código

## 1. Componentes
- `Modules/Orders/`: `ReservationService.cs`, `ReservationsController.cs`, `ReservationDtos.cs`, `ReservationsErrors` (en `OrdersErrors.cs`) y `Data/ReservationRepositories.cs`.
- Cambios:
  - `OrderLifecycleService.AfterCompletedAsync` pasa a interno;
  - `OrderService.IsCampusHour` pasa a interno;
  - `IConversationOpener.OpenForProductAsync` es nuevo en M-07;
  - avisos y destino nuevos.
- `db/028-apartados.js`, `MongoSchema.Reservations` y `ExpectedVersion = 28`.

## 2. Decisiones (con su porqué)
- **Uno activo a la vez (usuaria):** con un índice único parcial (`ux_buyer_active`), así dos toques a la vez no crean dos apartados.
- **El código va en claro y solo lo ve quien aparta:** tiene que mostrarlo y dictarlo. Contra la fuerza bruta: 5 intentos atómicos y comparación en tiempo constante.
- **El QR lleva el id y el código:** quien vende lo escanea desde ese apartado ("Me apartaron"), así el QR de otro apartado no se acepta.
- **Pedido completado al entregar (usuaria):** primero se marca recogido (atómico) con el id del pedido y luego se inserta el pedido, para no duplicarlo.
- **Pieza guardada con `IProductStock`:** misma regla que un pedido aceptado; cancelar o vencer la regresa.
- **Vencimiento con un proceso periódico, sin TTL:** la pieza tiene que regresar y se avisa a los dos.

## 3. Excepciones al stack
En la API, ninguna. En la app: `qr_flutter` y `mobile_scanner` (ver la contraparte).

## 4. Guardarraíles
- La parte va en el filtro de cada escritura (OWASP API1).
- El código no sale a quien vende ni en una lista ajena.
- Tamaños de cuerpo de M-08.

## 5. Validación
Unit, Integration, Security, Contract y DatabaseTest; prueba en el celular (T5).

## 6. Secuencia
T1 → T2 → T3 → T4 → T5.

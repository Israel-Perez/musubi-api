# HU-62 — Recibir un recordatorio de la entrega y avisar "Ya llegué"

**HU / Jira / Sprint / Módulo / Contraparte:** HU-62 / SCRUM-102 / Sprint 5 (14 nov – 1 dic 2026) / M-08 / [musubi-app:HU-62](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-62-recordatorio-entrega)

## Contexto
Jira (SCRUM-102): Como **comprador o vendedor**, quiero **un recordatorio antes de la entrega y poder avisar que ya llegué**, para **no olvidar la cita y encontrarnos rápido en el campus**.

- Pantallas: `Pedido-pago` ("Tu pedido" / "Tu venta") y el chat. La maqueta no lo tiene: se agrega sin quitar nada (lineamiento 4).
- Origen: ideas del 13-Nov-2026 para dar un plus a la app. Trazabilidad: RF-31.
- **Decisiones (23-Nov-2026, para que la usuaria las revise):**
  - El recordatorio sale **solo una hora antes**, no la noche anterior. Así lo dice el criterio y evita un aviso de más.
  - "Ya llegué" se puede avisar **desde 2 horas antes** de la entrega y **una vez por parte**. Antes de eso no tiene sentido y así no se usa para mandar push de más.

**Depende de:** `HU-24` (pedido confirmado), `HU-31` (avisos y push), `HU-44` (punto de entrega), `HU-32` (avisos en el chat).

## Requisitos
1. **R1 · Recordatorio:** una hora antes de la entrega de un pedido **confirmado**, las dos partes reciben `delivery_reminder` ("Hoy a la 1:00 p. m. en Biblioteca con Diego"), con la hora del campus. Lo abre el pedido.
2. **R2 · Una sola vez:** `dedupeKey` `delivery_reminder:{pedido}:{parte}`. La revisión pasa cada 5 minutos y corren dos instancias, pero el índice único del historial deja pasar uno.
3. **R3 · Solo confirmados:** un pedido pendiente, cancelado o completado no recuerda nada. Tampoco una entrega que ya pasó.
4. **R4 · "Ya llegué":** `POST /orders/{id}/arrived` guarda la hora de quien llama (`handover.buyerArrivedAt` o `sellerArrivedAt`). Deja el aviso "Ana ya llegó a Biblioteca" en el chat de los dos y avisa a la otra parte con `order_arrived`.
5. **R5 · Límites de "Ya llegué":**
   - Solo las partes del pedido (BOLA: 404 a cualquier otra persona).
   - Una cuenta activa; una suspendida recibe 403.
   - Solo en un pedido confirmado.
   - Desde 2 horas antes (`orders.arrival_too_early`).
   - Una vez por parte: repetirlo responde el pedido sin avisar otra vez.
6. **R6 · Calendario:** "Agregar al calendario" es de la app (un intent de Android, sin permiso). La API no cambia.

## Diseño
- `Modules/Orders/DeliveryReminders.cs`:
  - `OrderArrivalService` ("Ya llegué");
  - `DeliveryReminderJob` y `DeliveryReminderWorker` (cada `Notifications:DeliveryReminderMinutes`, 5 por defecto);
  - `CampusClock` ("la 1:00 p. m.", sin ICU: la imagen usa `InvariantGlobalization`).
- `IOrderRepository`: `FindDeliveriesBetweenAsync` (índice `ix_confirmed_delivery`) y `MarkArrivedAsync` (atómico: solo si no estaba).
- `IConversationNotices.PostNoticeAsync` recibe el título del push. Con `null` no manda el push de mensaje, porque "Ya llegué" manda el suyo en la pestaña "Pedidos".
- `db/032-recordatorio-entrega.js`: validador de `orders` con los campos nuevos, `ix_confirmed_delivery` (parcial, `status: confirmed`) y los avisos nuevos. Esquema 32; API `0.5.0-alpha.43`.

## Contratos
- Fila `POST /api/v1/orders/{id}/arrived` de [`docs/endpoints.md`](../../../docs/endpoints.md).
- `OrderResponse.handover` suma `buyerArrivedAt` y `sellerArrivedAt` (fixtures `order.json` y `order-cancelled.json`).
- Error `orders.arrival_too_early` en [`docs/errores.md`](../../../docs/errores.md).
- Avisos `delivery_reminder` y `order_arrived` (destino `order`, pestaña `orders`).
- Configuración: `Notifications:DeliveryReminderMinutes`.

## Tests
- `UnitTest/Orders/DeliveryReminderTests.cs`:
  - el texto y los destinatarios;
  - una sola vez con el centro de avisos real;
  - los estados que no recuerdan y la ventana;
  - la hora del campus;
  - "Ya llegué": aviso, chat, una vez por parte, antes de tiempo, estados, otra persona y cuenta suspendida.
- `TestShared/Orders/OrderRepositoryContract.cs` (en memoria y en MongoDB real): la ventana y "Ya llegué" atómico.
- `UnitTest/Data/SchemaVersionTests.cs`: el último validador de `notifications` acepta cada tipo y destino del código (guard nuevo).
- `IntegrationTest/Orders/DeliveryReminderFlowTests.cs`: el flujo por HTTP con el reloj adelantado.
- `SecurityTest/Auth/OrdersAuthTests.cs`: 401, 404 a otra persona, 403 a una cuenta suspendida, id inválido y tope del cuerpo.
- `ContractTest/OrdersContractTests.cs` y `DatabaseTest/SchemaScriptTests.cs` (script 032 e índice parcial).

## Criterios
- [x] Una hora antes de la entrega de un pedido confirmado las dos partes reciben "Hoy a la 1:00 p. m. en Biblioteca con Diego." → `OneHourBefore_BothPartiesAreReminded_WithTheCampusTimeAndPlace`, `TheCampusClock_WritesTheHourLikeTheApp`.
- [x] El recordatorio sale una sola vez por pedido y parte → `TheReminder_GoesOutOnce_AlthoughTheCheckRunsEveryFewMinutes`, `DeliveryReminderFlowTests`.
- [x] Un pedido cancelado o completado ya no recuerda nada → `AnOrderThatIsNotConfirmed_IsNotReminded`, `DeliveriesBetween_OnlyConfirmedInsideTheWindow_ClosestFirst`.
- [x] "Ya llegué" avisa a la otra parte con un push y deja un aviso en el chat → `Arrived_TellsTheOtherParty_AndLeavesANoticeInTheChat`, `DeliveryReminderFlowTests`.
- [x] Solo las partes, una vez por parte y desde 2 h antes → `Arrived_*`, `MarkArrived_OncePerSide_*`, `Arrived_OnlyAPartyOfTheOrder_AndNotASuspendedAccount_AndNothingChanges`.
- [ ] "Agregar al calendario" → contraparte en musubi-app.
- [x] Build Release 0/0 · Unit 995, Integration 217, Security 340 y Contract 31 · `check_endpoints` OK (126) · `check_endpoint_tests` OK en las 3 suites · critic PASS.

## Límites
- Sin recordatorio la noche anterior (decisión para revisar).
- El recordatorio no se manda si el pedido se confirma menos de una hora antes de la entrega y la revisión no alcanza a pasar. Con 5 minutos entre revisiones, sale como máximo 5 minutos tarde.
- "Ya llegué" no comparte la ubicación: solo avisa.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 23-Nov-2026 (detallada e implementada)

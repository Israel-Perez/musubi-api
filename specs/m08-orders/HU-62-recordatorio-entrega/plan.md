# plan.md — HU-62 Recibir un recordatorio de la entrega y avisar "Ya llegué"

## 1. Componentes
- `Modules/Orders/DeliveryReminders.cs`: `OrderArrivalService`, `DeliveryReminderJob`, `DeliveryReminderWorker` y `CampusClock`.
- `OrdersController.Arrived` (`POST /orders/{id}/arrived`), `OrdersErrors.ArrivalTooEarly` y `OrderHandoverResponse` con las horas de llegada.
- `IOrderRepository.FindDeliveriesBetweenAsync` y `MarkArrivedAsync` (Mongo y en memoria).
- `NotificationTypes.DeliveryReminder` y `OrderArrived`; `ReminderOptions.DeliveryReminderMinutes`.
- `IConversationNotices.PostNoticeAsync(..., pushTitle, ...)`.
- `db/032-recordatorio-entrega.js`, `MongoSchema.ExpectedVersion = 32`.

## 2. Decisiones (con su porqué)
- **Un worker cada 5 minutos en lugar de programar cada aviso:** sigue el patrón de `TransferReminderWorker` y no necesita una cola persistente. La `dedupeKey` hace que dos instancias no repitan el aviso.
- **"Ya llegué" atómico en el repositorio:** `FindOneAndUpdate` con `handover.{parte}ArrivedAt` inexistente. Dos toques a la vez avisan una vez.
- **Un solo push por "Ya llegué":** el aviso del chat no manda el push de mensaje (`pushTitle: null`). El de `order_arrived` queda en "Pedidos" y abre el pedido.
- **La hora a mano (`CampusClock`):** la imagen corre con `InvariantGlobalization`, así que `es-MX` no está disponible. El campus es UTC-6 todo el año.
- **Servicio aparte de `OrderLifecycleService`:** sus pruebas lo construyen a mano y no necesita el chat.

## 3. Excepciones al stack
Ninguna.

## 4. Guardarraíles
- La parte va en el filtro de la escritura (OWASP API1) y la cuenta debe estar activa.
- `RequestSizeLimit` de M-08 en el endpoint nuevo.
- El texto del push no lleva datos de contacto: nombre corto y punto del campus.

## 5. Validación
Unit, Integration, Security, Contract y DatabaseTest (CI); guard nuevo del validador de avisos; prueba en el celular (T5).

## 6. Secuencia
T1 → T2 → T3 → T4 → T5.

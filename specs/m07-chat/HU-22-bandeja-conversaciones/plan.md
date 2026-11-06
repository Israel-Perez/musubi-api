# plan.md — HU-22 Bandeja de conversaciones

## 1. Componentes
- `Modules/Chat/`:
  - `ChatService.InboxAsync` y `ChatService.MarkReadAsync`;
  - `ConversationsController`: `GET /conversations` y `POST /conversations/{id}/read`;
  - `ChatDtos`: `InboxPage`, `InboxItemResponse`, `InboxContextResponse` y `LastMessageResponse`;
  - `ChatErrors.InvalidFilter`;
  - `Data/`: `InboxQuery`, `RecordMessageAsync` (reemplaza a `SetLastMessageAsync`: además suma no leídos), `MarkReadAsync` y `FindInboxPageAsync`, en `Mongo*` e `InMemory*`.
- `db/006-chat-bandeja.js` y `MongoSchema.ExpectedVersion = 6`.

## 2. Contratos
Filas en `docs/endpoints.md` (M-07). Forma de `InboxItemResponse`:
- `id` y `role` (`buyer` → "Compra", `seller` → "Venta");
- `counterpart { id, name }`;
- `context { type, id, title, photo }` (nombre y portada guardados: la miniatura de la derecha en `Mensajes`);
- `lastMessage { senderId, text, sentAt }`;
- `unread`.

## 3. Datos (`db/006-chat-bandeja.js`)
- `conversations` agrega `buyerName`, `sellerName`, `context.photo`, `buyerUnread` y `sellerUnread`. Son opcionales: las conversaciones anteriores valen 0 y la API busca el nombre si no está copiado.
- Sin índices nuevos: `ix_participant_lastAt` cubre el filtro y el orden.

## 4. Decisiones (con su porqué)
- **No leídos por lado, no por usuario:** una conversación siempre tiene un comprador y un vendedor. Dos enteros validables evitan llaves dinámicas con ids.
- **`$inc` al enviar y `$set 0` al leer:** atómico entre las 2 instancias, sin leer y reescribir.
- **Solo conversaciones con mensajes:** tocar "Mensaje" y no escribir no debe llenar la bandeja del vendedor.
- **Copia de los nombres (R8):** la bandeja de 30 filas no hace 30 consultas a `users`. El nombre no se edita.
- **Cursor `ticks_id`:** varias conversaciones pueden tener la misma hora; el id desempata.
- **La app marca leído explícitamente** (al abrir y con cada mensaje que llega con la conversación abierta). Que leer los mensajes los marcara solo mezclaría una lectura con una escritura.

## 5. Validación
Suites al 100 % y `DatabaseTest` en el CI. En AWS: dos cuentas; la bandeja del vendedor muestra el mensaje con su contador, y el contador se limpia al abrir la conversación.

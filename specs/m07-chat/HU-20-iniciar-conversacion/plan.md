# plan.md — HU-20 Iniciar una conversación con el vendedor

## 1. Componentes
- `Modules/Chat/` (M-07, nuevo):
  - `ChatService`: iniciar desde una publicación, leer, listar mensajes y enviar;
  - `ConversationsController` (`/conversations…`);
  - `ChatHub` (`/hubs/chat`) y `ChatNotifier` (entrega con degradación controlada);
  - `ChatErrors`, `ChatDtos` y `ChatModuleServiceCollectionExtensions` (DI, rate limit `chat`, `MapChatModule`);
  - `ChatAccountEraser` (HU-46);
  - `Data/`: documentos, contratos, `Mongo*` e `InMemory*`.
- `Platform/Realtime/`: SignalR, backplane de Redis, `SubjectUserIdProvider` y el token en la query solo en `/hubs`.
- M-03: `IProductRepository.FindByIdAsync` y el contrato público `IProductDirectory` / `ProductSummary` (HU-11 puede reutilizar `FindByIdAsync`).
- `db/005-chat.js` y `MongoSchema.ExpectedVersion = 5`.
- `scripts/check_endpoints.ps1`: también extrae `MapHub<T>(...)` y compara filas `| WS | /ruta |`.
- Paquetes:
  - `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.14 (API);
  - `Microsoft.AspNetCore.SignalR.Client` 10.0.14 (`IntegrationTest`);
  - `nuget.config`: `MessagePack` y `MessagePack.Annotations` en el source mapping.

## 2. Contratos (DTOs)

| Ruta | Request | Response |
|---|---|---|
| `POST /conversations` | `StartConversationRequest { productId }` | `ConversationResponse` (201 nueva, 200 si ya existía) |
| `GET /conversations/{id}` | — | `ConversationResponse` |
| `GET /conversations/{id}/messages` | `?cursor=&limit=` | `MessagePage { items, nextCursor }` |
| `POST /conversations/{id}/messages` | `SendMessageRequest { text }` + rate limit `chat` | `MessageResponse` (201) |
| `WS /hubs/chat` | `?access_token=` | evento `messageCreated` con un `MessageResponse` |

Formas de las respuestas:
- **`ConversationResponse`**:
  - `id`, `role` (`buyer`/`seller`), `createdAt`;
  - `counterpart { id, name }`: la foto y la reputación salen de `GET /profiles/{id}`;
  - `context { type, id, title, priceCents, condition, conditionDetail, photo, available }`: los datos actuales de la publicación. Si ya no se puede ver: `available: false` y solo el nombre guardado.
- **`MessageResponse`**: `id`, `conversationId`, `senderId`, `text`, `sentAt`. La app compara `senderId` con su propio id para saber si el mensaje es suyo.

## 3. Datos (`db/005-chat.js`)
- **`conversations`** (R1, R4, R8, R13):
  - campos: `communityId`, `buyerId`, `sellerId`, `participants[2]`, `context { type, id, title }`, `lastMessage { senderId, text ≤ 120, at }` (se omite sin mensajes), `lastMessageAt`, `createdAt`;
  - índices: `ux_context_buyer` (único: una por comprador y elemento) e `ix_participant_lastAt` (bandeja HU-22 y borrado HU-46).
- **`messages`**:
  - campos: `conversationId`, `communityId`, `senderId`, `text` (1 a 1000), `createdAt`;
  - índice: `ix_conversation_id` (`{ conversationId: 1, _id: -1 }`, paginación por cursor, R2).

## 4. Decisiones (con su porqué)
- **SignalR + backplane de Redis** (decisión del usuario, 03-Nov-2026). Con 2 instancias detrás del ALB, un mensaje que entra por la A tiene que salir por la conexión que vive en la B. La alternativa (consultar cada pocos segundos) metía retraso. Redis Cloud gratuito ya estaba previsto para la caché (02-02): es la misma cadena.
- **Solo WebSockets, sin negociar.** Con negociación, el `POST /negotiate` y el WebSocket podrían caer en instancias distintas y fallar. Sin negociar no hacen falta sesiones pegajosas en el ALB. CloudFront y el ALB ya dejan pasar WebSockets: la política de origen manda todos los encabezados y la query.
- **El token viaja en `?access_token=`, solo en `/hubs`.** Un WebSocket desde el celular no lleva encabezados. En la API REST la query se ignora (`ChatAuthTests`). El log de peticiones (02-06) no guarda la query, y CloudFront y el ALB no tienen logs de acceso.
- **Enviar por REST, entregar por el hub.** Validación, moderación y rate limit en un solo lugar, con códigos HTTP que la app ya sabe traducir. El hub no expone métodos que el cliente pueda llamar.
- **Moderar cada mensaje antes de guardarlo** (02-07, L-16b). Agrega la latencia de Translate + Comprehend al envío, pero nunca se guarda un texto sin moderar. Si AWS falla, `503 moderation.unavailable` y no se guarda.
- **Si Redis falla al entregar**, el mensaje ya quedó guardado: se registra un aviso y la app lo ve al recargar (RNF-15, degradación controlada).
- **Una conversación por comprador y publicación**, con un índice único. Dos toques seguidos en "Mensaje" devuelven la misma conversación (200).
- **`404` para la conversación ajena** (RN-22): misma respuesta que si no existiera, para no revelarla (OWASP API1).
- **Borrar la cuenta borra las conversaciones de los dos lados** (R11). Anonimizar dejaría los textos, que son datos personales.
- **La cadena de Redis se lee al registrar SignalR**, no de forma lazy (excepción a 00-04 regla 3): SignalR elige su `HubLifetimeManager` al registrarse. Las pruebas no configuran Redis.

## 5. Guardarraíles
- `[Authorize]` en el controller y en el hub (`ChatAuthTests.EveryHub_RequiresASession`).
- Rate limit `chat`: 30 mensajes por minuto por usuario (OWASP API4).
- `MaximumReceiveMessageSize` de 4 KB en el hub: la app no envía nada por ahí.
- Validación en el servidor (`$jsonSchema`): exactamente 2 participantes y texto de 1 a 1000.

## 6. Validación
- Suites: Unit, Integration, Security y Contract al 100 %; `DatabaseTest` en el CI (Docker).
- `check_endpoints` muerde: sin las filas nuevas fallaba con 5 rutas faltantes, incluido el hub.
- La prueba del hub falla si el evento no llega: se comprobó cambiando el nombre del evento (`TimeoutException`).
- En AWS: dos celulares en la misma conversación, con la cadena de Redis en Parameter Store.

## 7. Secuencia
1. Datos (`005-chat.js`, repositorios y contrato).
2. Plataforma de tiempo real.
3. Servicio, controller, hub y pruebas.
4. Documentación y PR de la API.
5. App (`musubi-app:HU-20`): pantalla `Chat-Valeria`, cliente del hub y botón "Mensaje".

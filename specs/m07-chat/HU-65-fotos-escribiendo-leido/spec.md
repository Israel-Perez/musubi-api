# HU-65 — Enviar fotos y ver "escribiendo…" y "leído" en el chat

**HU / Jira / Sprint / Módulo / Contraparte:** HU-65 / SCRUM-105 / Sprint 5 (14 nov – 1 dic 2026) / M-07 / [musubi-app:HU-65](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m07-chat/HU-65-fotos-escribiendo-leido)

## Contexto
Jira (SCRUM-105): Como **usuario**, quiero **mandar fotos en el chat, ver cuando la otra persona escribe o leyó mi mensaje y usar respuestas rápidas**, para **ponernos de acuerdo más rápido**.

- Pantalla: `Chat-Valeria`. Lo que la maqueta no tiene se agrega sin quitar nada (lineamiento 4).
- Trazabilidad: lineamiento **L-16b** (moderación de imágenes antes de guardar).
- **Decisiones (24-Nov-2026, para que la usuaria las revise):**
  - Las fotos del chat **no pasan por CloudFront**: se guardan fuera de `media/` y la API las entrega solo a los participantes.
  - "Leído" no se puede apagar en Privacidad (pendiente en Jira; queda para después).
  - Las respuestas rápidas son de la app: la API no cambia por ellas.

**Depende de:** `HU-20` (chat y hub), `HU-22` (bandeja y leídos), `HU-57` (bloquear), `02-07` (moderación), `HU-06` (procesar fotos).

## Requisitos
1. **R1 · Foto:**
   - `POST /conversations/{id}/photos` lee la foto como las de los productos: JPG, PNG o WebP de 200 px a 10 MB, sale en WebP y sin metadatos.
   - **La moderación de imágenes la revisa antes de guardarla.**
   - Se guarda en `chat/{conversación}/{id}.webp` y se entrega como un mensaje (`kind: photo`, `text: "📷 Foto"`): hub, bandeja y push.
2. **R2 · Descargar:** `GET /conversations/{id}/photos/{name}` solo para los participantes (RN-22).
   - El nombre debe ser `{32 hex}.webp`, así nunca sale de `chat/`.
   - `Cache-Control: private`.
3. **R3 · "Escribiendo…":** el hub suma el método `Typing(conversationId)`. Llega `typing` a la otra persona solo si quien escribe participa, está activo y no hay un bloqueo. No se guarda.
4. **R4 · "Leído":**
   - `POST /{id}/read` guarda `buyerReadAt`/`sellerReadAt` y emite `conversationRead`.
   - `ConversationResponse.counterpartReadAt` dice cuándo abrió la otra persona.
5. **R5 · Bloqueo (HU-57):**
   - No se envían fotos (`chat.blocked`).
   - No llega "escribiendo…".
   - No se avisa ni se muestra "Leído".
6. **R6 · Sin la IA:** las fotos no van al acuerdo con IA (HU-32): `RecentTextsAsync` solo lee textos.
7. **R7 · Eliminar la cuenta (HU-46):** las fotos se borran con la conversación.

## Diseño
- `ChatService`:
  - `SendPhotoAsync`, `PhotoAsync` y `TypingAsync`;
  - `DeliverAsync`, compartido con el texto;
  - `MarkReadAsync` con `now` y el evento.
- `ChatHub` con `Typing`; `ChatNotifier.TypingAsync` y `ConversationReadAsync`.
- `IMediaStorage.GetAsync` (S3 `GetObject` y en memoria).
- `ConversationDocument.BuyerReadAt`/`SellerReadAt` y `MessageDocument.Photo`.
- `db/034-chat-fotos.js`: los validadores; sin índices nuevos. Esquema 34; API `0.5.0-alpha.45`.

## Contratos
- Filas `POST /api/v1/conversations/{id}/photos`, `GET /api/v1/conversations/{id}/photos/{name}`, `POST …/read` y `WS /hubs/chat` de [`docs/endpoints.md`](../../../docs/endpoints.md).
- Error `chat.photo_not_found`.
- Fixtures `message.json`, `message-page.json`, `message-photo.json` y `conversation.json`.

## Tests
- `UnitTest/Chat/ChatServiceTests.cs`:
  - la foto se modera, se guarda fuera de la CDN y se entrega;
  - rechazada, de otra persona, vacía o con un bloqueo;
  - solo los participantes la bajan, sin rutas;
  - "escribiendo…" con y sin bloqueo;
  - "leído", su evento y el bloqueo.
- `TestShared/Chat/ChatRepositoryContract.cs` (en memoria y en MongoDB real): `MarkRead` guarda cuándo y un mensaje después no lo borra.
- `IntegrationTest/Chat/ChatFlowTests.cs`: la foto por HTTP con la moderación y quién la baja; "escribiendo…" y "leído" por el hub real.
- `SecurityTest/Auth/ChatAuthTests.cs`: 401, 404 a otra persona, un nombre que no es foto, `Cache-Control` privado y los topes.
- `ContractTest/ChatContractTests.cs` y `DatabaseTest/SchemaScriptTests.cs`.

## Criterios
- [x] Enviar una foto de la cámara o la galería; pasa por la moderación antes de guardarse → `SendPhoto_IsModerated_SavedOutsideTheCdn_AndDeliveredLikeAMessage`, `APhoto_IsModerated_AndOnlyTheParticipantsDownloadIt`.
- [x] "Escribiendo…" en tiempo real por SignalR, sin guardarse → `Typing_ReachesOnlyTheOtherPerson_AndNotWithABlock`, `Typing_AndRead_ArriveInRealTime_AndReadIsKeptForTheOtherSide`.
- [x] "Leído" cuando la otra persona abre la conversación → `Reading_SavesWhen_TellsBothInRealTime_AndTheOtherSeesItUnlessBlocked`, `MarkRead_ClearsOnlyThatSide_AndKeepsWhenItRead`.
- [x] Respuestas rápidas en la primera conversación → contraparte en musubi-app.
- [x] Con un bloqueo nada de esto funciona → `SendPhoto_Rejected_NeverReachesStorage_AndABlockOrAStrangerCannotSend`, `Typing_…`, `Reading_…`.
- [x] Build Release 0/0 · Unit 1015, Integration 221, Security 349 y Contract 32 · `check_endpoints` OK (130) · `check_endpoint_tests` OK en las 3 suites · critic PASS.

## Límites
- "Leído" no se puede desactivar todavía.
- Una foto por mensaje y sin texto al pie.
- "Escribiendo…" no se reenvía a los otros dispositivos de quien escribe.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 24-Nov-2026 (detallada e implementada)

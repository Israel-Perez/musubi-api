# plan.md — HU-65 Enviar fotos y ver "escribiendo…" y "leído" en el chat

## 1. Componentes
- `Modules/Chat/ChatService.cs` (foto, descarga, escribiendo, leído y `DeliverAsync`), `ChatHub.cs` (`Typing`, eventos), `ConversationsController.cs` (dos rutas), `ChatDtos.cs`, `ChatErrors.cs` y `Data/*` (`BuyerReadAt`, `SellerReadAt` y `Photo`).
- `Platform/Media/Media.cs`: `IMediaStorage.GetAsync`.
- `ChatAccountEraser`: borra `chat/{id}/` con cada conversación.
- `db/034-chat-fotos.js` y `MongoSchema.ExpectedVersion = 34`.

## 2. Decisiones (con su porqué)
- **Fuera de `media/`:** CloudFront sirve `media/*` público, porque así son las fotos de los productos. Las del chat son de dos personas, así que van a `chat/` y la API las entrega revisando que quien pide participe. No hay URL que se pueda compartir.
- **La foto pasa por la misma lectura y moderación que las de los productos** (`PhotoUploadService.ReadAsync` y `CheckImageAsync`). Una rechazada nunca llega a S3.
- **"Escribiendo…" por el hub y sin guardar:** es efímero. El método valida cada vez quién participa y si hay un bloqueo, y la app lo manda a lo más cada 3 s.
- **"Leído" con una fecha por lado y no por mensaje:** "leído" es "abrió la conversación después de este mensaje". Es una escritura por apertura, no una por mensaje.
- **Con un bloqueo, `counterpartReadAt` es `null` y no se emite `conversationRead`:** el bloqueo corta todo lo del chat (HU-57).

## 3. Excepciones al stack
Ninguna.

## 4. Guardarraíles
- OWASP API1: participante en cada ruta; el nombre de la foto con un patrón estricto (sin rutas).
- API4: tope del cuerpo y rate limit `uploads`.
- RNF-03: `Cache-Control: private`.
- L-16b: moderación antes de guardar.

## 5. Validación
Unit, Integration (hub real), Security, Contract y DatabaseTest; prueba en el celular (T5).

## 6. Secuencia
T1 → T2 → T3 → T4 → T5.

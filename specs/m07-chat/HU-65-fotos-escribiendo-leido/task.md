# HU-65 — Enviar fotos y ver "escribiendo…" y "leído" en el chat (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (24-Nov-2026). Decisiones para revisar: fotos fuera de CloudFront y "Leído" sin apagarse todavía.

## T2 — Datos
- **Qué:** `BuyerReadAt`/`SellerReadAt`, `MessageDocument.Photo`, `IMediaStorage.GetAsync` y `db/034-chat-fotos.js`.
- **Prueba:** `TestShared/Chat/ChatRepositoryContract.cs` y `DatabaseTest/SchemaScriptTests.cs`.
- **Estado:** ✅ Hecho.

## T3 — Servicio, hub y rutas
- **Qué:** foto, descarga, `Typing`, `conversationRead` y `counterpartReadAt`.
- **Prueba:** `UnitTest/Chat/ChatServiceTests.cs`, `IntegrationTest/Chat/ChatFlowTests.cs`, `SecurityTest/Auth/ChatAuthTests.cs` y `ContractTest/ChatContractTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Documentación
- **Estado:** ✅ Hecho: endpoints (130), errores, modelo de datos, `db/README.md`, trazabilidad, versionado (alpha.45), README, `AGENTS.md` y `Memoria.md`.

## T5 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.45`: enviar una foto (cámara y galería), una rechazada, "Escribiendo…" en el otro teléfono y "Leído".
- **Estado:** ⏳ Por hacer (siguiente lote de Google Play).

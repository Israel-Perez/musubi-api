# HU-20 — Iniciar una conversación con el vendedor

**HU / Jira / Sprint / Módulo / Contraparte:** HU-20 / SCRUM-29 / Sprint 2 (12 – 23 oct 2026) / M-07 / [musubi-app:HU-20](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m07-chat/HU-20-iniciar-conversacion)

## Contexto
Jira (SCRUM-29): Como **comprador**, quiero **iniciar una conversación con el vendedor desde la publicación o desde la oferta**, para **resolver dudas y negociar**.

- Maqueta: `Producto`, `Perfil-vendedor`, `Chat-Valeria`, `Mensajes`, `Chat`, `Acuerdo-esperando`, `Acuerdo-confirmado`.
- Trazabilidad: RF-26, RF-27 · CU-15 · RN-21, RN-22, RN-23 · RNF-10.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `02-07` (moderación) y `02-02` (Redis como backplane). En la app, del detalle del producto (`HU-11`, botón "Mensaje").

## Requisitos
1. Se puede iniciar un chat desde una publicación, una solicitud o una oferta.
2. Cada conversación es entre dos usuarios y está asociada a una publicación, solicitud u oferta (RN-21).
3. Los mensajes de texto se entregan casi en tiempo real (RNF-10).
4. Solo los participantes pueden leer la conversación (RN-22).
5. Los mensajes pueden reportarse (RN-23).

RNF que aplican: RNF-10.

## Diseño
- Módulo `Musubi.Api/Modules/Chat/` (M-07, primera HU del módulo). Detalle en [`plan.md`](plan.md).
- **Tiempo real con SignalR** (decisión del 03-Nov-2026): hub `/hubs/chat` con backplane de Redis (`Cache:ConnectionString`), para que un mensaje que llega a una instancia salga por la conexión que vive en la otra. Solo WebSockets y sin negociar: no hacen falta sesiones pegajosas en el ALB.
- **Los mensajes se envían por REST** y el hub solo entrega: la validación, la moderación (02-07) y el rate limit viven en un solo lugar.
- Colecciones `conversations` y `messages` (`db/005-chat.js`, esquema 5). Una conversación por comprador y publicación (`ux_context_buyer`).
- M-07 no lee colecciones de otros módulos: usa `IProductDirectory` (M-03) e `IMemberDirectory` (M-01).

## Contratos
- Filas canónicas: [`docs/endpoints.md`](../../../docs/endpoints.md) → M-07 · Chat (4 rutas REST + `WS /hubs/chat`).
- Errores `chat.*`: [`docs/errores.md`](../../../docs/errores.md).
- Fixtures: `ContractTest/Fixtures/conversation.json`, `message.json` (también es la carga de `messageCreated`) y `message-page.json`.

## Tests
- `UnitTest/Chat/ChatServiceTests.cs`: otro campus, publicación pausada o quitada, cuenta inactiva, contexto sin publicación, aviso a los dos participantes y Redis caído (el mensaje se guarda igual).
- `IntegrationTest/Chat/ChatFlowTests.cs`: un flujo por criterio, incluido el hub real por WebSocket con el token en la query.
- `SecurityTest/Auth/ChatAuthTests.cs`: 401 sin sesión (también el hub), token en la query solo en `/hubs`, token falsificado, conversación ajena y todo hub con `[Authorize]`.
- `ContractTest/ChatContractTests.cs`: formas JSON.
- `TestShared/Chat/ChatRepositoryContract.cs` en memoria (`UnitTest`) y en MongoDB real (`DatabaseTest`), más los índices y la validación del script (`SchemaScriptTests`).

## Criterios
- [x] Se puede iniciar un chat desde una publicación → `ChatFlowTests.Start_FromAProduct_Is201WithTheSeller_AndAgainIs200WithTheSameConversation`. Desde solicitudes y ofertas: Sprint 3 (`NOTE (HU-16)`).
- [x] Cada conversación es entre dos usuarios y está asociada a una publicación (RN-21) → `ChatRepositoryContract` (`ux_context_buyer`) y `SchemaScriptTests.Chat_HasItsIndexes_AndRejectsAnEmptyMessageAndAThirdParticipant`.
- [x] Los mensajes de texto se entregan casi en tiempo real (RNF-10) → `ChatFlowTests.Messages_ArriveInRealTime_ToBothParticipants_AndNotToOthers` (WebSocket real). Entre instancias: backplane de Redis (se verifica en AWS al configurar `/musubi/prod/Cache/ConnectionString`).
- [x] Solo los participantes pueden leer la conversación (RN-22) → `ChatFlowTests.SomeoneElse_CannotReadOrWrite_TheConversation` y `ChatAuthTests`.
- [ ] Los mensajes pueden reportarse (RN-23) → `NOTE (HU-28)`: el reporte llega con los reportes (Sprint 5).
- [x] Build Release 0/0 · Unit 171, Integration 87, Security 80 y Contract 8 al 100 % · `check_endpoints` OK (33 rutas, ahora también hubs) · critic PASS.

## Límites
- **Imágenes en el chat (RF-27):** fuera de HU-20; solo texto.
- **Reportar un mensaje (RN-23):** `NOTE (HU-28)`.
- **"En línea" y "Activa hace 5 min"** del encabezado: `NOTE (HU-21)` (respeta `visibility.online`).
- **Aviso push** si el otro no tiene la app abierta: `NOTE (HU-31)` (Firebase).
- **Sin Redis**, cada instancia entrega en tiempo real solo a sus conexiones; el mensaje siempre se guarda y la app lo ve al recargar.
- **Borrar la cuenta** (HU-46) borra las conversaciones donde participa la persona, para los dos lados: los textos son datos personales de ambos (R11).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia (API). La app queda pendiente (musubi-app:HU-20).
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 04-Nov-2026
- **Detalle:** 29-Oct-2026: spec ligera desde Jira y la maqueta (SCRUM-86). 04-Nov-2026 (SCRUM-29): se detalla e implementa la API; decisión del usuario: SignalR con backplane de Redis para que no haya retraso entre las 2 instancias.

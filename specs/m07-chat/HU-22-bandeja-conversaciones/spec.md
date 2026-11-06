# HU-22 — Bandeja de conversaciones

**HU / Jira / Sprint / Módulo / Contraparte:** HU-22 / SCRUM-31 / Sprint 2 (12 – 23 oct 2026) / M-07 / [musubi-app:HU-22](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m07-chat/HU-22-bandeja-conversaciones)

## Contexto
Jira (SCRUM-31): Como **usuario**, quiero **ver la lista de mis conversaciones**, para **dar seguimiento a varios tratos a la vez**.

- Maqueta: `Mensajes`, `Chat`, `Acuerdo-esperando`, `Acuerdo-confirmado`.
- Trazabilidad: RF-29 · CU-15.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-20`.

## Requisitos
1. Las conversaciones se ordenan por actividad reciente.
2. Se muestra un indicador de mensajes no leídos.
3. Cada conversación muestra al otro participante y el elemento de contexto.

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Chat/` (M-07), sobre las conversaciones de HU-20. Detalle en [`plan.md`](plan.md).
- **Solo aparecen conversaciones con al menos un mensaje:** el vendedor no ve chats "vacíos" de quien tocó "Mensaje" y no escribió.
- **No leídos por lado** (`buyerUnread` / `sellerUnread`, `$inc` atómico al enviar) y `POST /conversations/{id}/read` al abrir la conversación.
- **Copia de los nombres** (`buyerName` / `sellerName`, R8): la bandeja no consulta usuarios. El nombre no se edita (HU-04).
- Orden por `lastMessageAt` (y `_id` para desempatar) con cursor `ticks_id`; filtro `role` para "Compras" y "Ventas" (maqueta `Mensajes`).
- Tiempo real: la app vuelve a pedir la bandeja al recibir `messageCreated` por `/hubs/chat` (HU-20).

## Contratos
- Filas canónicas: [`docs/endpoints.md`](../../../docs/endpoints.md) → M-07 · Chat (`GET /conversations`, `POST /conversations/{id}/read`).
- Errores: `chat.invalid_filter` y `chat.invalid_cursor` en [`docs/errores.md`](../../../docs/errores.md).
- Fixture: `ContractTest/Fixtures/inbox-page.json`.

## Tests
- `TestShared/Chat/ChatRepositoryContract.cs` (memoria y MongoDB): orden, cursor, filtro por papel, sin conversaciones vacías, no leídos y marcar leído.
- `IntegrationTest/Chat/ChatFlowTests.cs`: bandeja de los dos lados con no leídos, filtros y cursor, filtro o cursor inválidos y marcar leída una ajena.
- `SecurityTest/Auth/ChatAuthTests.cs`: 401 sin sesión; la conversación ajena no aparece en mi bandeja y no se puede marcar leída.
- `ContractTest/ChatContractTests.cs` y `DatabaseTest/SchemaScriptTests.cs` (`006-chat-bandeja`, no leídos ≥ 0).

## Criterios
- [x] Las conversaciones se ordenan por actividad reciente → `ChatRepositoryContract.Inbox_HasOnlyMyConversationsWithMessages_NewestFirst_ByRole_AndTheCursorContinues` y `ChatFlowTests.Inbox_FiltersPurchasesAndSales_AndIsNewestFirstWithACursor`.
- [x] Se muestra un indicador de mensajes no leídos → `ChatFlowTests.Inbox_ShowsTheConversationToBothSides_WithUnreadForTheRecipient_UntilItIsRead` y `ChatRepositoryContract.MarkRead_ClearsOnlyThatSide`.
- [x] Cada conversación muestra al otro participante y el elemento de contexto → la misma prueba de integración (`counterpart` y `context`).
- [x] Build Release 0/0 · Unit 174, Integration 93, Security 82 y Contract 8 al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Pestañas de la maqueta para solicitudes y ofertas ("Tu solicitud", "Tu oferta"): `NOTE (HU-16)`.
- Silenciar o archivar una conversación: fuera de alcance.
- La app (pantalla `Mensajes`): `musubi-app:HU-22`.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia (API). La app queda pendiente (musubi-app:HU-22).
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 06-Nov-2026
- **Detalle:** 29-Oct-2026: spec ligera (SCRUM-86). 06-Nov-2026 (SCRUM-31): se detalla e implementa la API para que el chat de HU-20 funcione de punta a punta.

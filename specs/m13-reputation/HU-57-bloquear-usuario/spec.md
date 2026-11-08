# HU-57 — Bloquear a un usuario

**HU / Jira / Sprint / Módulo / Contraparte:** HU-57 / SCRUM-95 / Sprint 3 / M-13 / [musubi-app:HU-57](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m13-reputation/HU-57-bloquear-usuario)

## Contexto
Jira (SCRUM-95): Como **usuario**, quiero **bloquear a otra persona de mi campus**, para **que no pueda escribirme ni ver mis publicaciones**.

- HU nueva (08-Nov-2026). Con HU-28 cumple el requisito de Google Play para apps con contenido de usuarios: reportar y bloquear.
- Maqueta:
  - `chatMoreSheet` → "Bloquear a {nombre}", con el diálogo "¿Bloquear a {nombre}?" y el texto "No podrá escribirte ni ver tus publicaciones. Puedes desbloquearlo en Ajustes.";
  - Ajustes → `aj-bloqueados`, la hoja "Usuarios bloqueados" con avatar, nombre y "Desbloquear".

**Depende de:** `HU-20` (chat), `HU-06`/`HU-48` (vitrina), `HU-11` (detalle), `HU-04` (perfil público) y `HU-09` (búsqueda).

## Requisitos
1. **R1 · Bloquear:** desde el chat, a otra persona del mismo campus.
   - Es idempotente: dos toques no duplican.
   - No se puede bloquear a uno mismo.
   - Una persona inexistente o de otro campus responde 404 sin revelar cuál.
2. **R2 · Efecto en las dos direcciones:** mientras exista el bloqueo, quien bloqueó y quien fue bloqueado:
   - no inician ni reabren una conversación ni envían mensajes. Responde `403 chat.blocked`, con el mismo mensaje para los dos;
   - no ven las publicaciones del otro en la vitrina, la búsqueda ni el detalle (404);
   - no ven el perfil público del otro (404).
3. **R3 · La conversación existente:**
   - sigue en la bandeja con su historial;
   - `GET /conversations/{id}` responde `canReply: false`, sin decir quién bloqueó, para que la app cambie la caja de texto por un aviso.
4. **R4 · "Usuarios bloqueados":** la lista de a quienes bloqueé, del más reciente al más viejo, con nombre (copia R8) y foto de perfil.
5. **R5 · Desbloquear:** idempotente. Todo vuelve a la normalidad en la siguiente petición (sin caché).
6. **R6 · Borrado de cuenta (HU-46):** se borran sus bloqueos en las dos direcciones.

RNF que aplican: RNF-03 (sesión), RNF-21 (campus), OWASP API1 (las rutas no llevan el id de quien bloquea).

## Diseño
- **Módulo:** `Modules/Reputation/`.
  - `BlockService`, `BlocksController` y `Data/` (`BlockDocument`, `IBlockRepository`, Mongo e InMemory).
  - `IBlockDirectory` (`HiddenForAsync` y `BetweenAsync`) es el contrato que consultan los demás módulos.
- **Dónde se aplica:**
  - `ChatService` (iniciar, enviar y `canReply`);
  - `ListingService` (vitrina y búsqueda con `ProductQuery.ExcludeSellers`, que se traduce en `$nin` sobre `sellerId`; y el detalle);
  - `ProfileService.PublicAsync`.
- **Fotos:** `IProfileDirectory` (M-02, nuevo) entrega en una sola consulta las fotos de varias personas. Lo usan "Usuarios bloqueados" y el chat (la foto de la otra persona en la bandeja y el encabezado).
- **Datos:** colección `blocks` (`db/008-reportes-bloqueos.js`):
  - `ux_blocker_blocked` (único) resuelve la idempotencia y "a quiénes bloqueé";
  - `ix_blocked` resuelve "quiénes me bloquearon".
- **Caché:** la vitrina con sesión no pasa por la caché de salida (02-02), así que un bloqueo no se mezcla entre usuarios.

## Contratos
- **Endpoints** (filas en [`docs/endpoints.md`](../../../docs/endpoints.md)):
  - nuevos: `GET /api/v1/blocks`, `POST /api/v1/blocks` y `DELETE /api/v1/blocks/{userId}`;
  - cambian: `ConversationResponse` (`counterpart.photo` y `canReply`), `InboxItemResponse` (`counterpart.photo`) y los 404 de la vitrina, el detalle y el perfil.
- **Errores** ([`docs/errores.md`](../../../docs/errores.md)): `blocks.self`, `blocks.user_not_found` y `chat.blocked`.
- **Fixtures:** `blocked-users.json`; `conversation.json` e `inbox-page.json` actualizados.

## Tests
- `UnitTest/Reputation/ReputationServiceTests.cs`: R1, R4 y R5.
- `UnitTest/Chat/ChatServiceTests.cs`: iniciar y enviar bloqueados, `canReply` y la foto de la otra persona.
- `UnitTest/Profiles/ProfileServiceTests.cs`: el perfil con un bloqueo en cualquier dirección.
- `TestShared/Reputation` (en memoria y Mongo): idempotencia, las dos direcciones, orden y borrado.
- `TestShared/Listings`: `ExcludeSellers` en la página, la búsqueda y el conteo.
- `TestShared/Profiles`: `FindManyAsync`.
- `IntegrationTest/Reputation/ReputationFlowTests.cs`: bloquear corta el chat, oculta publicación y perfil en las dos direcciones, y desbloquear lo regresa.
- `SecurityTest/Auth/ReputationAuthTests.cs` y `ContractTest/ReputationContractTests.cs`.

## Criterios
- [x] Con un bloqueo en cualquier dirección nadie escribe y no se ven publicaciones ni perfil (R2).
- [x] La conversación existente queda sin poder responderse (R3).
- [x] "Usuarios bloqueados" lista y "Desbloquear" restablece todo (R4, R5).
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.
- [ ] `DatabaseTest` en el CI.

## Límites
- **Sin silenciar:** "Silenciar conversación" del mismo menú depende de las notificaciones push (`NOTE (HU-31)`).
- **No avisa:** nadie recibe aviso de que lo bloquearon.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 08-Nov-2026
- **Detalle:**
  - HU nueva por el requisito de Google Play;
  - el bloqueo aplica en las dos direcciones (más seguro que solo en una);
  - `chat.blocked` no revela quién bloqueó.

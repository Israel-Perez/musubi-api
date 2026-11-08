# HU-57 — Bloquear a un usuario (tareas)

## T1 — Spec
- **Estado:** ✅ Hecho (08-Nov-2026). Jira SCRUM-95.

## T2 — Datos
- **Crear:** `blocks` en `db/008-reportes-bloqueos.js`, `BlockDocument` y los repositorios Mongo e InMemory; `IProfileRepository.FindManyAsync`.
- **Verificar:** `ReputationRepositoryContract`, `ProfileRepositoryContract` y `SchemaScriptTests`.
- **Estado:** ✅ Hecho (08-Nov-2026).

## T3 — Bloqueo y su efecto
- **Crear:** `BlockService`, `BlocksController` (3 rutas) e `IBlockDirectory`.
- **Cambiar:** el chat (`chat.blocked` y `canReply`), la vitrina, la búsqueda, el detalle y el perfil público.
- **Verificar:** `ReputationServiceTests`, `ChatServiceTests`, `ProfileServiceTests`, `ListingRepositoryContract`, `ReputationFlowTests`, `ReputationAuthTests` y `ReputationContractTests`.
- **Estado:** ✅ Hecho (08-Nov-2026).

## T4 — Foto de la otra persona en el chat
- **Crear:** `IProfileDirectory`; `counterpart.photo` en la conversación y la bandeja.
- **Verificar:** `ChatServiceTests` y `ChatContractTests`.
- **Estado:** ✅ Hecho (08-Nov-2026).

## T5 — Documentación
- **Estado:** ✅ Hecho (08-Nov-2026).

## Pendientes
- La app: `musubi-app:HU-57` ("Bloquear a …" en el chat, el aviso sin caja de texto y "Usuarios bloqueados" en Privacidad).

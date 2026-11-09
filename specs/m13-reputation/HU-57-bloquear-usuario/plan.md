# HU-57 — Bloquear a un usuario (plan)

## Decisiones
- **Dos direcciones:** la maqueta dice "No podrá escribirte ni ver tus publicaciones". Además, quien bloquea tampoco ve a la otra persona, para no volver a tener contacto por las publicaciones. Es la regla común en apps de compraventa y evita que alguien se cree otra conversación desde otra publicación.
- **El bloqueo vive en M-13 y se consulta por contrato:** `IBlockDirectory` evita que el chat, la vitrina o el perfil lean la colección `blocks` (Modules/README).
- **Sin caché del bloqueo:** se consulta en cada petición con sesión. `FindRelatedAsync` usa los dos índices y la lista de una persona es corta; así el desbloqueo aplica al instante en las dos instancias.
- **`$nin` en la vitrina:** el conjunto de bloqueos de una persona es pequeño. `$nin` sobre `sellerId` se combina con los índices actuales (comunidad, estado, `_id`) y no hace falta otro índice.
- **`canReply` en lugar de quién bloqueó:** la app necesita saber si puede escribir, no quién bloqueó. Revelarlo puede provocar acoso fuera de la app.
- **Foto en el chat por lote:** la bandeja pide las fotos de toda la página en una sola consulta (`FindManyAsync`), no una por fila. La foto no se copia a la conversación porque cambia (HU-04).

## Archivos
- **Nuevos:** `Modules/Reputation/{BlockService, BlocksController, BlockDirectory}.cs` y `Modules/Profiles/ProfileDirectory.cs`.
- **Cambian:** `ChatService`, `ChatDtos`, `ChatErrors`, `ListingService`, `ProductsController`, `ProductQuery` y los repositorios de productos, `ProfileService` y `IProfileRepository` (`FindManyAsync`).
- **Script:** `blocks` en `db/008-reportes-bloqueos.js`.

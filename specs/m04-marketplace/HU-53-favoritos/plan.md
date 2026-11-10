# HU-53 — Guardar productos en favoritos (plan)

## Decisiones
- **Enfoque B: el estado del corazón no vive en los productos.** La app carga `GET /favorites` al iniciar sesión y arma un set de ids. La vitrina y el detalle siguen siendo públicos y cacheables; no se agrega una consulta de favoritos a cada página de productos y no cambian `ProductResponse` ni `ProductDetailResponse`. Costo: en un deep link el corazón puede tardar hasta que termine la carga; la app lo resuelve con su estado de carga.
- **Módulo `Modules/Listings/`:** `docs/modulos.md` dice que M-04 reutiliza la vitrina de M-03. Crear `Modules/Marketplace/` duplicaría el catálogo y rompería el patrón de HU-09/HU-10/HU-11.
- **Recurso propio, sin id del usuario en la ruta:** el usuario sale del token (OWASP API1). Nadie consulta ni modifica los favoritos de otra persona.
- **`PUT` y `DELETE` idempotentes:** el índice único `ux_user_product` resuelve dos toques simultáneos. Repetir `PUT` responde 204; repetir `DELETE` también.
- **Se reutiliza `listings.product_not_found`:** guardar una publicación inexistente, `hidden`, `removed`, de otro campus o bloqueada responde el mismo 404, sin revelar cuál. No se agregan códigos `favorites.*`.
- **La lista distingue tres estados:** `available` (`active`), `soldOut` (`soldOut`) y `unavailable` (`hidden`, `removed`, inexistente, otro campus o bloqueo). `unavailable` lleva `product: null` para no filtrar datos de una publicación que ya no se debe ver.
- **Los bloqueos de HU-57 se aplican en la lectura y en la escritura:** se consulta `IBlockDirectory`; un producto de una persona bloqueada no se guarda y, si ya estaba guardado, se muestra `unavailable`. No se revela quién bloqueó.
- **La lista no pagina:** tope 200, del más reciente al más viejo, igual que "Usuarios bloqueados" (HU-57). La lista de una persona es corta; si el tope se vuelve un problema, se agrega paginación con `NOTE (HU-53)`. No se inventa un umbral de latencia.
- **Consulta en lote:** `IProductRepository.FindByIdsAsync` trae los productos de la página en una sola consulta; `IBlockDirectory.HiddenForAsync` trae los bloqueos una vez. Nada de N+1.
- **Sin copia de datos:** `favorites` guarda solo `userId`, `productId`, `communityId` y `createdAt`; el título, el precio y la foto se leen de `products`. Así no se duplica una tabla viva y el estado refleja el producto actual.
- **Borrado de cuenta (HU-46):** `ListingAccountEraser` borra los favoritos del usuario. Los favoritos de otras personas que apuntaban a sus productos se conservan como `unavailable`; no se borran en cascada para no perder la señal de "Ya no disponible".
- **Sin modo sin conexión:** decisión de Jira (08-Nov-2026). La API es la fuente de verdad; la caché local de la app queda para después.
- **Sin "Bajó $":** la maqueta lo muestra, pero no hay historial de precios en esta HU. Se documenta como límite.
- **Una publicación propia se puede guardar:** no aporta al comprador, pero no daña y evita un error extra. Si el producto está `hidden`, se muestra `unavailable` igual que para cualquier otro.

## Componentes
- **Nuevos:**
  - `Musubi.Api/Modules/Listings/FavoritesController.cs`;
  - `Musubi.Api/Modules/Listings/FavoriteService.cs`;
  - `Musubi.Api/Modules/Listings/FavoritesDtos.cs`;
  - `db/013-favoritos.js`.
- **Cambian:**
  - `Musubi.Api/Modules/Listings/Data/ListingDocuments.cs` (`FavoriteDocument`);
  - `Musubi.Api/Modules/Listings/Data/ListingRepositories.cs` (`IFavoriteRepository` y `IProductRepository.FindByIdsAsync`);
  - `Musubi.Api/Modules/Listings/Data/MongoListingRepositories.cs` e `InMemoryListingRepositories.cs`;
  - `Musubi.Api/Modules/Listings/ListingService.cs` (`ToResponse` pasa a `internal static` para reutilizar el mapeo);
  - `Musubi.Api/Modules/Listings/ListingsModuleServiceCollectionExtensions.cs` (repositorio y servicio nuevos);
  - `Musubi.Api/Modules/Listings/ListingAccountEraser.cs` (favoritos, HU-46);
  - `Musubi.Api/Platform/Data/MongoSchema.cs` (`Favorites`, `ExpectedVersion = 13`);
  - `db/README.md` (fila del script 013).
- **No cambian:** `ProductsDtos.cs`, `ProductsController.cs`, `ContractTest/Fixtures/product*.json` ni la caché de la vitrina.

## Flujo
1. **Guardar:** `PUT /api/v1/favorites/{productId}`.
   - cuenta activa;
   - producto existente, del mismo campus y `active`/`soldOut`;
   - sin bloqueo con el vendedor;
   - `InsertIfAbsentAsync` y 204.
2. **Quitar:** `DELETE /api/v1/favorites/{productId}`. Borra si existe y responde 204; no valida el producto para que el botón siempre funcione.
3. **Listar:** `GET /api/v1/favorites`.
   - favoritos del usuario, más recientes primero, máximo 200;
   - productos en lote;
   - bloqueos una vez;
   - mapeo a `FavoriteItemResponse`.
4. **Cuenta eliminada:** `ListingAccountEraser` borra los favoritos del usuario.

## Excepciones al stack
Ninguna. Se usan MongoDB, ASP.NET Core, los repositorios en memoria y las suites existentes.

## Guardarraíles
- `[Authorize]` explícito en el controller; sin `try/catch`.
- Sin `userId` en ruta, body ni query: solo el token (OWASP API1).
- Mismo campus (RNF-21); producto visible; bloqueo de HU-57.
- Idempotencia con índice único y `InsertIfAbsentAsync`; `DELETE` idempotente.
- Respuesta sin datos de terceros: solo los favoritos del usuario y el producto que ya puede ver.
- Tope 200 en la lista; sin consultas N+1.
- `db/013-favoritos.js` con `$jsonSchema` estricto e índices `ux_user_product` e `ix_user_id`.
- Borrado de cuenta (HU-46) sin dejar favoritos del usuario.
- Sin cambios en las respuestas de productos ni en su caché (enfoque B).

## Validación
- `dotnet build Musubi.slnx -c Release` → 0 errores / 0 advertencias.
- `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build`.
- `dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build`.
- `dotnet test --project SecurityTest/SecurityTest.csproj -c Release --no-build`.
- `dotnet test --project ContractTest/ContractTest.csproj -c Release --no-build`.
- `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release --no-build` (MongoDB real; en el CI).
- `pwsh scripts/check_endpoints.ps1` → OK.
- `pwsh scripts/critic-guardrails.ps1` → PASS.
- Prueba manual en Scalar/Development: guardar, listar, quitar y repetir.
- `security-reviewer` PASS antes del push; `traceability-clerk` sin drift al cerrar.

## Secuencia
1. **Specs:** `spec.md`, `plan.md` y `task.md` detallados (T1).
2. **Datos:** `db/013-favoritos.js`, `MongoSchema.ExpectedVersion = 13`, `FavoriteDocument`, `IFavoriteRepository`, `FindByIdsAsync` y los repositorios Mongo/InMemory (T2).
3. **Servicio y endpoints:** `FavoriteService`, `FavoritesController`, DTOs, DI y `ListingAccountEraser` (T3).
4. **Pruebas:** Unit, TestShared, Integration, Security, Contract y Database (T3/T4).
5. **Documentación:** `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `docs/trazabilidad.md`, `db/README.md` y `Memoria.md` (T4).
6. **Cierre:** build, suites, guards, `security-reviewer`, `traceability-clerk` y estado "Borrador con evidencia" (T5).

## Alcanzabilidad
- **Entra en el Sprint 4:** tres endpoints, una colección, un servicio, un controller, un script y pruebas en las suites existentes.
- **No entra:** modo sin conexión, historial de precios, notificaciones por cambio de estado, paginación de la lista y limpieza de favoritos huérfanos.
- **Dependencia externa:** la pantalla y los corazones son de `musubi-app:HU-53`; la API queda lista para que la app cargue `GET /favorites` y use `PUT`/`DELETE`.

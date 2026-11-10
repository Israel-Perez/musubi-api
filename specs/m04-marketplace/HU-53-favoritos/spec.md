# HU-53 — Guardar productos en favoritos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-53 / SCRUM-68 / Sprint 4 (9–20 nov 2026) / M-04 / [musubi-app:HU-53](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-53-favoritos)

## Contexto
Jira (SCRUM-68): Como **comprador**, quiero **guardar productos en favoritos**, para **volver a encontrarlos rápido y decidir después**.

- Maqueta: `Favoritos`, `Producto` y las tarjetas de la vitrina y la búsqueda.
- Trazabilidad: sin RF/RNF en Jira (historia que nació de la maqueta, SCRUM-75).
- Origen: maqueta (sección 10, G-06).
- **Decisión de Jira (08-Nov-2026):** sin modo sin conexión. Los favoritos viven en la API; la caché local de la app queda para después.
- **Contrato del corazón (enfoque B):** la app arma el estado del corazón con `GET /favorites`; los endpoints de productos no cambian.

**Depende de:** `02-01`, `02-03`, `HU-06`/`HU-11`, `HU-57` y `HU-46`.

## Requisitos
1. **Guardar y quitar (RNF-03, OWASP API1):**
   - desde el detalle o una tarjeta, el corazón guarda o quita el producto;
   - `PUT /favorites/{productId}` y `DELETE /favorites/{productId}` son idempotentes: repetir la acción no duplica ni falla;
   - solo se administran los favoritos del usuario del token; la ruta no lleva su id.
2. **Qué se puede guardar:**
   - una publicación `active` o `soldOut` de su mismo campus;
   - no se guarda una publicación inexistente, `hidden`, `removed`, de otro campus o de una persona con la que hay un bloqueo (HU-57); responde el mismo `404 listings.product_not_found`, sin revelar cuál.
3. **Lista "Favoritos":**
   - devuelve lo guardado por el usuario, del más reciente al más viejo;
   - cada elemento indica `available`, `soldOut` o `unavailable`;
   - `soldOut` muestra "Agotado" y conserva los datos del producto;
   - `unavailable` cubre inexistente, `removed`, `hidden`, otro campus o bloqueo; no expone el producto (`product: null`) y la app muestra "Ya no disponible";
   - el tope es 200 elementos, sin paginar.
4. **Privacidad (RNF-07):**
   - nadie ve los favoritos de otra persona;
   - la respuesta no incluye cuántas personas guardaron un producto ni datos de otros usuarios;
   - el estado del corazón se calcula solo con los favoritos del token.
5. **Bloqueos (HU-57):**
   - un producto de una persona bloqueada, en cualquier dirección, no se puede guardar y aparece como `unavailable` si ya estaba guardado;
   - la lista no revela quién bloqueó a quién.
6. **Borrado de cuenta (HU-46, R11):**
   - al eliminar una cuenta se borran sus favoritos;
   - los favoritos de otras personas que apuntaban a sus productos quedan como `unavailable` (no se borran en cascada).
7. **Sin modo sin conexión:** la API es la única fuente de verdad en esta HU.

RNF que aplican: RNF-03 (sesión y propiedad), RNF-07 (privacidad), RNF-21 (campus) y OWASP API1 (BOLA).

## Diseño
- **Módulo:** `Musubi.Api/Modules/Listings/`. `docs/modulos.md` mapea M-04 a `Modules/Listings/` (reutiliza la vitrina de M-03); no se crea `Modules/Marketplace/`.
- **Archivos nuevos:**
  - `Modules/Listings/FavoritesController.cs`;
  - `Modules/Listings/FavoriteService.cs`;
  - `Modules/Listings/FavoritesDtos.cs`;
  - `db/013-favoritos.js`.
- **Archivos que cambian:**
  - `Modules/Listings/Data/ListingDocuments.cs` (`FavoriteDocument`);
  - `Modules/Listings/Data/ListingRepositories.cs` (`IFavoriteRepository` y `IProductRepository.FindByIdsAsync`);
  - `Modules/Listings/Data/MongoListingRepositories.cs` y `InMemoryListingRepositories.cs`;
  - `Modules/Listings/ListingService.cs` (`ToResponse` pasa a `internal static` para reutilizar el mapeo);
  - `Modules/Listings/ListingsModuleServiceCollectionExtensions.cs` (registro de repositorio y servicio);
  - `Modules/Listings/ListingAccountEraser.cs` (borrado de favoritos, HU-46);
  - `Platform/Data/MongoSchema.cs` (`Favorites` y `ExpectedVersion = 13`);
  - `db/README.md` (fila del script 013).
- **Servicio:** `FavoriteService` recibe `IFavoriteRepository`, `IProductRepository`, `IMemberDirectory`, `IBlockDirectory` y `TimeProvider`.
  - Guardar exige una cuenta activa, la publicación visible y el mismo campus; inserta si no existe.
  - La lista consulta los favoritos y los productos en lote (`FindByIdsAsync`, sin N+1), y los bloqueos una sola vez (`HiddenForAsync`).
  - `ProductResponse` se reutiliza para los elementos disponibles o agotados; `unavailable` no lleva producto.
- **Datos:** colección `favorites`; la forma y los índices canónicos (`ux_user_product` e `ix_user_id`) viven en la fila de [`docs/modelo-datos.md`](../../../docs/modelo-datos.md). `db/013-favoritos.js` crea el validador, esos índices y sube el esquema a 13.
- **Sin cambios en productos:** `ProductResponse` y `ProductDetailResponse` conservan su forma; la app obtiene el estado del corazón con `GET /favorites`. Así la vitrina pública sigue siendo cacheable y no se agrega una consulta de favoritos a cada página de productos.
- **Estado del corazón en la app:** al iniciar sesión carga `GET /favorites` y arma un set de ids; `PUT`/`DELETE` lo actualizan. Un deep link puede mostrar el corazón sin rellenar hasta que termine esa carga; la app debe manejarlo con su estado de carga.
- **Límite conocido:** la lista devuelve como máximo 200; se documenta con `NOTE (HU-53)` si alguien supera ese tope.

## Contratos
- **Filas nuevas** en [`docs/endpoints.md`](../../../docs/endpoints.md) (se agregan con la implementación en la sección `## M-03 · Publicaciones`, porque el código vive en `Modules/Listings/`; `Spec` = HU-53 y `Consumidor` = `musubi-app` HU-53). Esta spec no copia la tabla:
  - `GET /api/v1/favorites`;
  - `PUT /api/v1/favorites/{productId}`;
  - `DELETE /api/v1/favorites/{productId}`.
- **DTOs:**
  - `FavoritesResponse { items: FavoriteItemResponse[] }`;
  - `FavoriteItemResponse { productId, savedAt, status, product }`, con `status` `available`/`soldOut`/`unavailable` y `product` `ProductResponse?`.
- **Errores:** se reutiliza `listings.product_not_found` (404) para no guardar una publicación no visible. No se agregan códigos nuevos. La fila de [`docs/errores.md`](../../../docs/errores.md) se amplía con HU-53.
- **Fixture:** `ContractTest/Fixtures/favorites.json`.
- **Sin cambios** en `ContractTest/Fixtures/product*.json` ni en los DTOs de productos.

## Tests
- `UnitTest/Listings/FavoriteServiceTests.cs`:
  - guardar dos veces deja un solo favorito;
  - `active` y `soldOut` se guardan; `hidden`, `removed`, inexistente y otro campus dan 404;
  - un bloqueo impide guardar y marca `unavailable`;
  - la lista es del usuario, va de la más reciente a la más vieja y mapea `available`/`soldOut`/`unavailable`;
  - `unavailable` no expone el producto;
  - el tope de 200 se respeta.
- `TestShared/Listings/FavoriteRepositoryContract.cs`, con clases nuevas `UnitTest/Data/InMemoryFavoriteRepositoryTests.cs` y `DatabaseTest/MongoFavoriteRepositoryTests.cs` (las clases existentes ya heredan de `ListingRepositoryContract` y son `sealed`):
  - `InsertIfAbsentAsync` es idempotente;
  - `FindByUserAsync` ordena y limita;
  - `DeleteAsync` es idempotente, también con un id mal formado;
  - `DeleteByUserAsync` borra solo lo del usuario.
- `TestShared/Listings/ListingRepositoryContract.cs`: `FindByIdsAsync` devuelve solo los ids pedidos.
- `IntegrationTest/Listings/FavoriteFlowTests.cs`: un comprador guarda desde el detalle, lo ve en Favoritos, repite el `PUT`, lo quita con `DELETE`, y ve `soldOut`/`unavailable` cuando el producto cambia.
- `SecurityTest/Auth/ListingsAuthTests.cs`: 401 sin sesión en las tres rutas y BOLA entre dos usuarios (cada uno ve solo lo suyo).
- `ContractTest/ListingsContractTests.cs` + `Fixtures/favorites.json`: forma JSON de la lista y del elemento sin producto.
- `DatabaseTest/SchemaScriptTests.cs`: `db/013-favoritos.js` es idempotente, el validador rechaza un favorito incompleto y los índices existen.
- `UnitTest/Listings/ListingAccountEraserTests.cs`: borrar la cuenta borra los favoritos del usuario con `InMemoryFavoriteRepository` (la prueba existente de `AccountDeletionService` usa un eraser de grabación y no verifica esta regla).
- `IntegrationTest/Listings/FavoriteFlowTests.cs` también cubre el borrado de la cuenta vendedora: el favorito del comprador sigue y queda `unavailable`.

## Criterios
- [x] Guardar y quitar es idempotente y solo afecta al usuario del token → `FavoriteServiceTests`, `FavoriteFlowTests`.
- [x] La lista distingue `available`, `soldOut` y `unavailable`, y no expone el producto cuando no está disponible → `FavoriteServiceTests`, `FavoriteFlowTests`, `favorites.json`.
- [x] Un bloqueo de HU-57 impide guardar y oculta el producto como `unavailable` → `FavoriteServiceTests`, `FavoriteFlowTests`.
- [x] Ningún usuario ve los favoritos de otro → `ListingsAuthTests` (401/BOLA) y `FavoriteFlowTests`.
- [x] Borrar la cuenta borra sus favoritos → `ListingAccountEraserTests` y `FavoriteFlowTests` (vendedor eliminado).
- [x] `db/013-favoritos.js` crea el índice único y sube el esquema a 13 → `SchemaScriptTests` y `MongoFavoriteRepositoryTests`.
- [x] Build Release 0/0.
- [x] Unit 383, Integration 138, Security 129, Contract 13 y Database 97 al 100 % con `--no-build`.
- [x] `pwsh scripts/check_endpoints.ps1` → OK (57 rutas); `pwsh scripts/critic-guardrails.ps1` → PASS.
- [x] `security-reviewer` PASS; `traceability-clerk` sin drift al cerrar.
- [x] `pwsh scripts/check_shared_sections.ps1` → OK (5 bloques).

## Límites
- **Sin modo sin conexión** (decisión de Jira, 08-Nov-2026). La caché local de la app queda para otra HU.
- **Sin "Bajó $"** de la maqueta: no hay historial de precios en esta HU.
- **Sin paginación:** la lista devuelve hasta 200; si una persona supera ese tope, se pagina en una HU posterior. `NOTE (HU-53)`.
- **Sin notificaciones:** no se avisa cuando un favorito se agota o baja de precio; el estado se recalcula al consultar.
- **Sin copia de datos:** no se guardan título, precio ni foto en `favorites`; se leen de `products` para no duplicar una tabla viva.
- **Favoritos huérfanos:** si el producto se elimina, el favorito se conserva y se muestra `unavailable`; su limpieza no se incluye.
- **Sin regla contra guardar una publicación propia:** se permite para no agregar un error extra; no aporta al comprador y no daña nada.
- **La app queda pendiente** (`musubi-app:HU-53`): pantalla `Favoritos`, corazones y estados de carga.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 09-Nov-2026
- **Detalle:**
  - spec detallada al iniciar la HU (SCRUM-68, Sprint 4);
  - se resolvió la decisión de Jira: sin modo sin conexión;
  - el módulo correcto es `Modules/Listings/`, no `Modules/Marketplace/`;
  - se adopta el enfoque B: el estado del corazón se arma con `GET /favorites`, sin cambiar el contrato de productos;
  - se incorporan HU-57 (bloqueos) y HU-46 (borrado de cuenta);
  - evidencia: API `0.5.0-alpha.14` (al traer main, que ya tenía `alpha.13` de HU-14), esquema 13, Unit 383 / Integration 138 / Security 129 / Contract 13 / Database 97, `critic` PASS, endpoints OK (57 rutas) y secciones compartidas OK;
  - pendiente: PR y despliegue (el esquema 13 sube con `infra-up`).

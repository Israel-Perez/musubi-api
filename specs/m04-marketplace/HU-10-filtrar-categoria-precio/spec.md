# HU-10 — Filtrar por categoría y precio

**HU / Jira / Sprint / Módulo / Contraparte:** HU-10 / SCRUM-27 / Sprint 2 (12 – 23 oct 2026) / M-04 / [musubi-app:HU-10](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-10-filtrar-categoria-precio)

## Contexto
Jira (SCRUM-27): Como **comprador**, quiero **filtrar los resultados por categoría y precio**, para **descartar lo que no me sirve**.

- Maqueta: `Busqueda`, `Busqueda-filtros`, `Busqueda-vacia`.
- Trazabilidad: RF-13 · CU-06 · RNF-07 · RNF-09.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-02`, `02-03`, `HU-06`, `HU-09`, `HU-11`, `HU-48`.

La vitrina `GET /api/v1/products` ya existe con `campus`, `category`, `q`, `hideSoldOut`, `cursor` y `limit`. Esta HU agrega los filtros de la hoja `Busqueda-filtros`: varias categorías, rango de precio, condición y orden. No se crea un módulo nuevo: el endpoint, el repositorio y los DTO viven en `Modules/Listings/` y M-04 los reutiliza (misma decisión que HU-09). El esquema gana cuatro índices para ordenar por precio (asc/desc, con y sin categoría), creados por `db/009-filtros-precio.js`.

## Requisitos
1. `GET /api/v1/products` acepta `category` repetido (`category=libros&category=laptops`). Los valores se recortan, se descartan los vacíos y se comparan como OR. Un solo valor sigue funcionando. Sin `category`, no hay filtro de categoría.
2. Acepta `minPriceCents` y `maxPriceCents`, ambos opcionales e inclusivos. Sin mínimo no hay piso; sin máximo no hay techo.
3. El rango es válido si `0 <= minPriceCents <= maxPriceCents <= 99 999 900`. Si no: `400 listings.invalid_price_range`. Un valor no numérico o fuera del rango de `long` lo rechaza el binding como `validation.failed`.
4. Acepta `condition` con `new` o `used`. Omitido o vacío significa todas. Cualquier otro valor: `400 listings.invalid_condition`.
5. Acepta `sort` con `relevance`, `newest`, `oldest`, `priceAsc` o `priceDesc`. Omitido o vacío usa `newest`. Cualquier otro valor: `400 listings.invalid_sort`.
6. Los filtros se combinan entre sí, con `campus`, `q` y `hideSoldOut` mediante AND. Con `q` solo salen publicaciones `active`, aunque llegue `hideSoldOut=false`.
7. Orden:
   - `newest`: `_id` descendente.
   - `oldest`: `_id` ascendente.
   - `priceAsc`: `priceCents` ascendente y, en empate, `_id` descendente.
   - `priceDesc`: `priceCents` descendente y, en empate, `_id` descendente.
   - `relevance`: hoy se comporta como `newest`; no hay puntaje de relevancia (HU-09 lo difirió).
8. `nextCursor` es opaco y lleva el orden y la última clave: `newest:<id>`, `relevance:<id>` (se comporta como `newest`), `oldest:<id>`, `priceAsc:<priceCents>:<id>` o `priceDesc:<priceCents>:<id>`. Se acepta el ObjectId crudo anterior solo para `newest` y `relevance`. Un cursor malformado, de otro orden o con un id que no es ObjectId: `400 listings.invalid_cursor`. Un cursor de `newest` no sirve para `relevance` ni al revés, aunque ordenen igual.
9. La página siguiente conserva los mismos filtros y el mismo `sort`. `total` cuenta todas las coincidencias sin aplicar el cursor; `nextCursor` es `null` si la página no se llenó.
10. La ruta sigue `[AllowAnonymous]`, con sesión opcional, `ETag`/`304` y la caché de salida actuales. La política `showcase` varía por todos los query params, así que los nuevos entran en la clave sin cambios.
11. Los índices `ix_community_status_price_id`, `ix_community_status_price_desc_id`, `ix_community_category_status_price_id` e `ix_community_category_status_price_desc_id` los crea `db/009-filtros-precio.js`; `MongoSchema.ExpectedVersion` pasa a `9`. El script no modifica documentos.
12. Los filtros no se persisten ni se registran en logs o analítica (RNF-07).
13. La respuesta no cambia: `ProductPage { items: ProductResponse[], nextCursor, total }`. No hace falta fixture nuevo.
14. RNF-09: `priceAsc` y `priceDesc` usan sus índices (asc/desc, con desempate `_id` incluido); con `q` el orden puede resolverse en memoria sobre las coincidencias y `limit <= 50` acota el costo.

## Diseño
- **Módulo:** `Musubi.Api/Modules/Listings/` (M-03), reutilizado por M-04. No se crea `Modules/Marketplace/`.
- **Controller:** `ProductsController.Showcase` recibe `[FromQuery] string[]? category`, `long? minPriceCents`, `long? maxPriceCents`, `string? condition` y `string? sort`. No hay lógica de negocio ni `try/catch`.
- **Servicio:** `ListingService.ShowcaseAsync` normaliza categorías y `q`, valida rango, condición y orden, resuelve el cursor y devuelve el `nextCursor`.
- **Consulta:** `ProductQuery` agrega `CategorySlugs`, `MinPriceCents`, `MaxPriceCents`, `Condition`, `Sort` y `After`. `After` es un `ProductCursor` ya validado.
- **Cursor:** `ProductSorts` (constantes) y `ProductCursorCodec` (codificar/decodificar) viven junto a `ProductQuery` en `Data/ListingRepositories.cs`. El token no se firma: solo pagina y se valida.
- **MongoDB:** `Showcase` agrega `$in` de categorías, `$gte`/`$lte` de precio y `$eq` de condición. `FindPageAsync` aplica el filtro de cursor compuesto y el `Sort` correspondiente. `CountAsync` ignora el cursor.
- **En memoria:** `InMemoryProductRepository` aplica los mismos filtros, el mismo orden y la misma comparación de cursor que Mongo.
- **Errores:** `ListingsErrors.InvalidPriceRange` y `ListingsErrors.InvalidSort` (400). La condición inválida reutiliza `InvalidCondition`.
- **Índices:** `db/009-filtros-precio.js` crea `{ communityId, status, priceCents: 1, _id: -1 }`, `{ communityId, status, priceCents: -1, _id: -1 }`, `{ communityId, categorySlug, status, priceCents: 1, _id: -1 }` y `{ communityId, categorySlug, status, priceCents: -1, _id: -1 }`; sube `_meta.version` a 9.
- **Caché:** sin cambios; `SetVaryByQuery("*")` ya incluye los parámetros nuevos.
- **Rama:** `feature/SCRUM-27-filtrar-categoria-precio`.

## Contratos
- Fila canónica: [`docs/endpoints.md`](../../../docs/endpoints.md) → `GET /api/v1/products`. Cambia a `?campus=&category=&minPriceCents=&maxPriceCents=&condition=&sort=&q=&hideSoldOut=&cursor=&limit=`. Se enlaza, no se copia la tabla.
- Mapa para la app: `relevancia` → `relevance`, `recientes` → `newest`, `antiguos` → `oldest`, `precio-asc` → `priceAsc`, `precio-desc` → `priceDesc`; categorías como parámetros repetidos; precio en centavos; sin máximo = omitir `maxPriceCents`.
- Respuesta sin cambios: `ProductPage`. Se reutiliza `ContractTest/Fixtures/product-page.json`.
- Errores nuevos en [`docs/errores.md`](../../../docs/errores.md): `listings.invalid_price_range` (400) y `listings.invalid_sort` (400). `listings.invalid_condition` y `listings.invalid_cursor` se reutilizan.
- Esquema: fila `products` de [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); agrega los cuatro índices de precio con `db/009`.
- Configuración: sin claves nuevas.

## Tests
Conteos reales al cerrar (08-Nov-2026, rebasado sobre main): Unit 268 · Integration 106 · Security 92 · Contract 9 · Performance 4. `DatabaseTest` no se corrió en local (sin Docker); lo corre el CI.

- `TestShared/Listings/ListingRepositoryContract.cs` (corre en `UnitTest/Data/InMemoryListingRepositoryTests` y `DatabaseTest/MongoListingRepositoryTests`):
  - `Filters_ByPriceRange_IsInclusive_AndAcceptsOnlyOneBound`.
  - `Filters_BySeveralCategories_AndByCondition`.
  - `Filters_CombinePriceCategoryConditionAndSearch`.
  - `Sorts_ByNewestAndOldest_WithCursor`.
  - `Sorts_ByPrice_WithTieBreakerAndCursor` (asc y desc, cruzando el empate de precio).
- `UnitTest/Listings/ProductCursorCodecTests.cs`: ida y vuelta por orden; legacy aceptado solo para `newest`/`relevance`; orden ajeno, precio fuera de rango, id inválido y partes de más se rechazan; ObjectId en mayúsculas se canonicaliza.
- `IntegrationTest/Listings/ListingFlowTests.cs`:
  - `Showcase_FiltersByCategoriesPriceAndCondition`.
  - `Showcase_SortsByPrice_AndPagesWithTheCursor`.
  - `Showcase_RejectsInvalidFilters`.
- `SecurityTest/Auth/ListingsAuthTests.cs`: la vitrina con filtros sigue pública; valores extraños no cruzan campus ni inyectan filtros.
- `ContractTest/ListingsContractTests.cs`: `GET /api/v1/products` con filtros y orden mantiene la forma de `product-page.json`.
- `DatabaseTest/SchemaScriptTests.cs`: existen los cuatro índices y `_meta.version` es 9.
- R12 (no persistir ni registrar filtros) se verifica por revisión: no hay escritura ni log de los parámetros; solo viven como clave de la caché de salida. No tiene prueba automática porque la ausencia no se puede asertar sin instrumentar el pipeline.

## Criterios
- [x] `dotnet build Musubi.slnx -c Release` → 0 errores, 0 advertencias.
- [x] `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` → 268/268, incluidos los 5 casos del contrato compartido y los 19 del codec del cursor.
- [x] `dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build` → 106/106, incluidos los 3 flujos de filtros.
- [x] `dotnet test --project SecurityTest/SecurityTest.csproj -c Release --no-build` → 92/92; incluye filtros públicos sin cruce de campus.
- [x] `dotnet test --project ContractTest/ContractTest.csproj -c Release --no-build` → 9/9 con la página filtrada.
- [x] `dotnet test --project PerformanceTest/PerformanceTest.csproj -c Release --no-build` → 4/4.
- [x] `GET /api/v1/products?category=libros&category=laptops&minPriceCents=10000&maxPriceCents=50000&condition=used&sort=priceAsc` devuelve solo coincidencias y en orden (`Showcase_FiltersByCategoriesPriceAndCondition`).
- [x] `GET /api/v1/products?minPriceCents=50000&maxPriceCents=10000` devuelve `400 listings.invalid_price_range` (`Showcase_RejectsInvalidFilters`).
- [x] `GET /api/v1/products?condition=roto` devuelve `400 listings.invalid_condition` y `sort=roto` devuelve `400 listings.invalid_sort`.
- [x] `pwsh scripts/check_endpoints.ps1` → `OK (40 rutas con fila, 0 huerfanas)`.
- [x] `pwsh scripts/critic-guardrails.ps1` → `PASS (24 archivos analizados, 0 avisos)`.
- [ ] `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release` en CI → 0 fallos (local sin Docker: no se corre).

## Límites
- **Relevancia:** no hay puntaje; `relevance` ordena como `newest`. Un orden por relevancia real queda para una HU posterior. `NOTE (HU-10)`.
- **Orden por precio con `q`:** el índice de prefijo resuelve la búsqueda, pero el orden por precio puede hacerse en memoria sobre las coincidencias. Aceptable con `limit <= 50`; si el volumen crece, se mide y se decide con `02-02`. `NOTE (HU-10)`.
- **Plan con `$in` de estado:** con `hideSoldOut=false` el filtro es `status: $in [active, soldOut]`; Mongo puede resolverlo con `SORT_MERGE`. Los índices cubren el orden, pero el plan real se confirma con `explain` en `DatabaseTest`/CI. `NOTE (HU-10)`.
- **Sin tope propio de categorías:** el límite es el tamaño de la línea de petición. Si se vuelve abuso, se agrega un tope y su error. `NOTE (HU-10)`.
- **Cursor legado:** el ObjectId crudo solo se acepta para `newest`/`relevance`; un cliente que lo reutilice con otro orden recibe `400`. Es intencional.
- **Sin filtro por vendedor, texto libre en categoría ni rango por defecto:** la app manda los valores; la API no inventa precios.
- **`hideSoldOut` conserva su valor por defecto actual (`false`):** la app manda `true` cuando la maqueta oculta agotados.
- **Sin cambios en la app:** la contraparte Flutter implementa la hoja en su repo; esta spec cubre solo la API.
- **DatabaseTest no corre en local sin Docker:** la evidencia de los índices es el job `database` del CI.
- **Sin rate limit nuevo:** la vitrina sigue sin límite por IP; queda para el endurecimiento de abuso (RNF-11).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 07-Nov-2026
- **Detalle:** alcance acordado con el usuario: filtros completos de la maqueta `Busqueda-filtros` (multi-categoría, precio, condición y orden), no solo los tres criterios de Jira. Se reutiliza `Modules/Listings/`, se agrega cursor compuesto e índices de precio. API implementada y verificada en local (Unit 268, Integration 106, Security 92, Contract 9, Performance 4, critic PASS, endpoints OK); `DatabaseTest` queda para el CI por falta de Docker en local. Pendiente el revisor para pasar a "Aprobado".

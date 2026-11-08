# HU-10 — Filtrar por categoría y precio

**HU / Jira / Sprint / Módulo / Contraparte:** HU-10 / SCRUM-27 / Sprint 2 (12 – 23 oct 2026) / M-04 / [musubi-app:HU-10](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-10-filtrar-categoria-precio)

## Contexto
Jira (SCRUM-27): Como **comprador**, quiero **filtrar los resultados por categoría y precio**, para **descartar lo que no me sirve**.

- Maqueta: `Busqueda`, `Busqueda-filtros`, `Busqueda-vacia`.
- Trazabilidad: RF-13 · CU-06.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-02`, `02-03`, `HU-09` (búsqueda), `HU-48` (vitrina).

La vitrina `GET /api/v1/products` ya acepta `campus`, `category`, `q`, `hideSoldOut`, `cursor` y `limit` (HU-48 y HU-09). Esta HU completa la hoja **"Filtros"** de la maqueta: agrega el **rango de precio** y el **estado** (`Nuevo`/`Usado`). Categoría y "Ocultar agotados" ya existen y se conservan: la HU los documenta y los cubre con pruebas de combinación, pero no los reimplementa. No se crea un módulo nuevo: el endpoint, el repositorio y los DTO viven en `Modules/Listings/` y M-04 los reutiliza.

## Requisitos
1. `GET /api/v1/products` acepta tres parámetros opcionales más: `minPriceCents` y `maxPriceCents` (`long?`, en centavos, igual que `priceCents`) y `condition` (`new` o `used`). Sin ellos, la vitrina se comporta igual que hoy.
2. El rango es **inclusivo** en ambos extremos: `priceCents >= minPriceCents` y `priceCents <= maxPriceCents`. Cada extremo puede venir solo.
3. `minPriceCents` y `maxPriceCents` van de `0` a `99 999 900` (`ListingService.MaxPriceCents`) y `minPriceCents` no puede ser mayor que `maxPriceCents`. Un valor fuera de rango o invertido responde `400 listings.invalid_price_range` con texto en español.
4. `condition` solo acepta `new` o `used`; cualquier otro valor responde `400 listings.invalid_condition` (código que ya existe por HU-06).
5. Los filtros se combinan con **AND**: `category`, `q`, `condition`, `minPriceCents`, `maxPriceCents`, `hideSoldOut`, `campus`, `cursor` y `limit` acotan el mismo conjunto.
6. Con `q` solo salen publicaciones `active` (HU-09). Sin `q`, `hideSoldOut=true` sigue ocultando las agotadas y `false` las incluye; el rango de precio y el estado no cambian esa regla.
7. El orden (más nueva a más vieja), el cursor `_id` y `total` no cambian: `total` cuenta todas las coincidencias, no solo la página.
8. La ruta mantiene `[AllowAnonymous]`, la sesión opcional, `ETag`/`304` y la caché de salida; los parámetros nuevos entran en la clave de caché por `SetVaryByQuery("*")` y nadie con sesión recibe una respuesta anónima.
9. No hay cambio de esquema ni índice nuevo: se reusan `ix_community_status_id`, `ix_community_category_status_id` y, con `q`, `ix_community_status_search`.
10. RNF-09: en proceso, p95 ≤ 100 ms para `GET /api/v1/products` con los filtros de esta HU bajo la carga de `PerformanceTest`.

RNF que aplican: RNF-09 (filtros en tiempo aceptable) y RNF-07 (la búsqueda y sus filtros no se persisten ni se registran).

## Diseño
- **Servicio:** `ListingService.ShowcaseAsync` recibe `long? minPriceCents`, `long? maxPriceCents` y `string? condition`. Antes de consultar valida el rango (límites, `min ≤ max`) y el estado; devuelve `ListingsErrors.InvalidPriceRange` o `ListingsErrors.InvalidCondition` sin tocar los repositorios.
- **Contrato de datos:** `ProductQuery` agrega `long? MinPriceCents = null`, `long? MaxPriceCents = null` y `string? Condition = null` como parámetros opcionales al final, para no tocar las llamadas existentes.
- **MongoDB:** `MongoProductRepository.Showcase` agrega `Filter.Gte`/`Filter.Lte` sobre `PriceCents` y `Filter.Eq` sobre `Condition`; `CountAsync` usa el mismo filtro. La comunidad, el estado de venta y la categoría se siguen combinando con `&`.
- **En memoria:** `InMemoryProductRepository.Showcase` aplica las mismas comparaciones (`PriceCents` y `Condition`) para que la batería compartida pase en ambas implementaciones.
- **Controller:** `ProductsController.Showcase` agrega `[FromQuery] long? minPriceCents`, `[FromQuery] long? maxPriceCents` y `[FromQuery] string? condition`. Un parámetro vacío se trata como ausente.
- **Errores:** `ListingsErrors.InvalidPriceRange` → `listings.invalid_price_range` (400), texto "Revisa el rango de precio."; `InvalidCondition` se reutiliza con su texto actual.
- **Datos:** sin script en `db/` y sin cambio en `MongoSchema.ExpectedVersion`: el filtro no necesita campos ni índices nuevos.
- **Rama:** `feature/SCRUM-27-filtrar-categoria-precio`, desde `main` una vez fusionado HU-09 (la spec depende de esa búsqueda).

## Contratos
- Fila canónica: [`docs/endpoints.md`](../../../docs/endpoints.md) → `GET /api/v1/products`. Cambia a `?campus=&category=&q=&minPriceCents=&maxPriceCents=&condition=&hideSoldOut=&cursor=&limit=` y suma `400 listings.invalid_price_range` a los códigos. Se enlaza, no se copia.
- Respuesta sin cambios: `ProductPage { items: ProductResponse[], nextCursor, total }`. Se reutiliza `ContractTest/Fixtures/product-page.json`; no hace falta fixture nuevo.
- Error nuevo en [`docs/errores.md`](../../../docs/errores.md): `listings.invalid_price_range` | 400 | rango negativo, fuera de límites o invertido | HU-10. `listings.invalid_condition` (HU-06) pasa a citar también HU-10.
- Esquema: fila `products` de [`docs/modelo-datos.md`](../../../docs/modelo-datos.md) sin cambios; sus índices ya cubren la consulta.
- Configuración: sin claves nuevas. `Catalog:DefaultCampus` sigue igual.

## Tests
- `TestShared/Listings/ListingRepositoryContract.cs` (corre en `UnitTest/Data/InMemoryListingRepositoryTests` y `DatabaseTest/MongoListingRepositoryTests`):
  - `Filter_ByPriceRange_IsInclusive`: incluye los extremos exactos y excluye un centavo fuera; solo mínimo y solo máximo.
  - `Filter_ByCondition`: `new` y `used` devuelven solo su estado.
  - `Filter_CombinesPriceConditionCategoryAndSearch`: categoría + rango + estado + `q` devuelven la intersección; con un filtro que no coincide, página vacía.
- `IntegrationTest/Listings/ListingFlowTests.cs`:
  - `Filter_ByPriceAndCondition_CombinesWithSearch`: publica varios productos y comprueba `items`, `total` y que no salgan los fuera de rango ni el estado contrario.
  - `Filter_WithOnlyOnePriceExtremum_IsAccepted`: mínimo solo y máximo solo.
  - `Filter_WithAnInvalidRange_IsRejected`: `min > max`, negativo y fuera de `MaxPriceCents` → `400 listings.invalid_price_range`.
  - `Filter_WithAnUnknownCondition_IsRejected`: `condition=roto` → `400 listings.invalid_condition`.
- `ContractTest/ListingsContractTests.cs`: agrega `GET /api/v1/products?minPriceCents=10000&maxPriceCents=60000&condition=used` y compara su forma con `product-page.json` y `product.json`.
- `PerformanceTest/LatencyTests.cs`: agrega `/api/v1/products?category=calculadoras&minPriceCents=10000&maxPriceCents=60000&condition=used` a `PublicRoutes()` (RNF-09).
- `SecurityTest`: sin cambios de auth; la ruta sigue anónima. Los parámetros inválidos no exponen detalle interno (mismo `problem+json` de `docs/errores.md`).
- `UnitTest`: la validación de rango y estado se cubre por los flujos HTTP; si se extrae a un validador propio, se prueba en `UnitTest/Listings/`.

## Criterios
- [x] `dotnet build Musubi.slnx -c Release` → 0 errores, 0 advertencias.
- [x] `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` → verde (184; +3 de `TestShared`).
- [x] `dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build` → verde (104; +4 flujos de filtros).
- [ ] `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release --no-build` → verde (Docker). Sin Docker en local; lo corre el CI sobre el mismo `TestShared`.
- [x] `dotnet test --project SecurityTest/SecurityTest.csproj -c Release --no-build` → verde (84).
- [x] `dotnet test --project ContractTest/ContractTest.csproj -c Release --no-build` → verde (8) con la consulta filtrada.
- [x] `dotnet test --project PerformanceTest/PerformanceTest.csproj -c Release --no-build` → p95 ≤ 100 ms en la ruta filtrada (5).
- [x] `pwsh scripts/check_endpoints.ps1` → `OK` (36 rutas con fila, 0 huérfanas).
- [x] `pwsh scripts/critic-guardrails.ps1` → `PASS` exit 0.
- [x] `GET /api/v1/products?minPriceCents=10000&maxPriceCents=60000` devuelve solo publicaciones dentro del rango, con `total` correcto (`Filter_WithOnlyOnePriceExtremum_IsAccepted`).
- [x] `GET /api/v1/products?condition=used` devuelve solo usadas; `condition=roto` responde `400 listings.invalid_condition` (`Filter_WithAnUnknownCondition_IsRejected`).
- [x] `GET /api/v1/products?minPriceCents=60000&maxPriceCents=10000` responde `400 listings.invalid_price_range` (`Filter_WithAnInvalidRange_IsRejected`).
- [x] Los filtros se combinan con `q` y `category`: la intersección es la esperada (`Filter_CombinesPriceConditionCategoryAndSearch`, `Filter_ByPriceAndCondition_CombinesWithSearch`).

## Límites
- **Sin índice propio para el precio:** el rango es un predicado residual sobre las coincidencias de comunidad/categoría/estado; el `count` recorre esas coincidencias. Con el piloto (decenas de miles de documentos) y `limit ≤ 50` es aceptable; si `products` crece, se evalúa `(communityId, categorySlug, status, priceCents)`, a costa del orden por `_id`. `NOTE (HU-10)`.
- **Sin orden por precio:** Jira no lo pide y la maqueta ordena por relevancia; "Menor precio" del `Busqueda` es de otra iteración de la app.
- **Categoría desconocida:** sigue devolviendo página vacía, no `400`; la app solo envía slugs del catálogo de `GET /api/v1/categories`.
- **Agotados con `condition`:** cuando `hideSoldOut=false`, una agotada del estado pedido sí aparece; el filtro de estado no cambia la regla de estados de venta.
- **Valor no numérico:** `minPriceCents=abc` lo rechaza el model binding con `400` genérico; `listings.invalid_price_range` cubre los rangos numéricos inválidos.
- **Filtros y privacidad:** no se persisten ni se registran (RNF-07); viven en la URL y en la clave de caché.
- **Spec de la app:** la contraparte Flutter (`Busqueda-filtros`) ya existe en `musubi-app`; esta spec cubre solo la API.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia (implementada en la rama apilada sobre HU-09; pasa a Aprobado con revisión)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 07-Nov-2026
- **Detalle:** decisiones acordadas con el usuario: parámetros `minPriceCents`/`maxPriceCents`, filtro de estado de la maqueta (`condition`), `400` con código propio para el rango y reutilización de `listings.invalid_condition`, y sin índice nuevo. Depende de que HU-09 esté fusionada antes de implementar.

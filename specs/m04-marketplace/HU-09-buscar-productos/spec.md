# HU-09 — Buscar productos por palabra clave

**HU / Jira / Sprint / Módulo / Contraparte:** HU-09 / SCRUM-26 / Sprint 2 (12 – 23 oct 2026) / M-04 / [musubi-app:HU-09](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-09-buscar-productos)

## Contexto
Jira (SCRUM-26): Como **comprador**, quiero **buscar productos por nombre o palabra clave**, para **encontrar rápido lo que necesito**.

- Maqueta: `Inicio-visitante`, `Se-busca-visitante`, `Producto-visitante`, `Visitante-modal`, `Busqueda`, `Busqueda-filtros`, `Busqueda-vacia`.
- Trazabilidad: RF-12 · CU-06 · RNF-09 · RNF-07.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-02`, `02-03`, `HU-06`, `HU-48` (vitrina), `HU-11` (detalle).

La vitrina `GET /api/v1/products` ya existe con `campus`, `category`, `hideSoldOut`, `cursor` y `limit`. Esta HU agrega la búsqueda por texto sobre esa misma ruta y el índice `tx_title_description` que `docs/modelo-datos.md` ya previó para `products`. No se crea un módulo nuevo: el endpoint, el repositorio y los DTO viven en `Modules/Listings/` y M-04 los reutiliza.

## Requisitos
1. `GET /api/v1/products` acepta el parámetro opcional `q` (texto de búsqueda). Se recorta; si queda vacío se trata como ausente. Si trae texto, debe tener hasta 80 caracteres y al menos una letra o un dígito; si no cumple: `400 listings.invalid_query`.
2. Con `q`, la búsqueda devuelve **solo publicaciones `active`** de la comunidad pedida. Los agotados (`soldOut`) nunca aparecen, aunque llegue `hideSoldOut=false` (criterio 1 de Jira).
3. La coincidencia es por **palabra completa** sobre `title` y `description`: ignora mayúsculas y acentos y aplica el *stemming* español de MongoDB (un plural encuentra su singular y viceversa). `calculadora` encuentra `Calculadoras`; `calc` no encuentra nada.
4. Un documento coincide si contiene **cualquiera** de las palabras de `q` (semántica OR de `$text`). Las palabras vacías del español se ignoran.
5. Los resultados salen de la **más nueva a la más vieja** y se paginan con el mismo cursor `_id` de la vitrina (`nextCursor`). `total` es el número de coincidencias, no el de la página.
6. `q` se combina con `campus` y `category` (los filtros de precio y condición llegan en HU-10).
7. Sin resultados: `200` con `items: []`, `nextCursor: null` y `total: 0`. La app muestra las opciones de HU-12; la API no inventa un error.
8. Sin `q`, o con `q` vacío, la vitrina se comporta **igual que hoy** (incluye agotados salvo `hideSoldOut=true`).
9. La búsqueda no se persiste ni se registra: no hay historial, ni texto de `q` en logs o analítica (RNF-07).
10. La ruta mantiene `[AllowAnonymous]`, la sesión opcional, `ETag`/`304` y la caché de salida actuales.
11. El índice `tx_title_description` lo crea un script nuevo `db/007-busqueda.js`; `MongoSchema.ExpectedVersion` pasa a `7`. El repositorio en memoria debe aproximar el mismo comportamiento para que la batería compartida pase en ambas implementaciones.
12. RNF-09: en proceso, p95 ≤ 100 ms para `GET /api/v1/products?q=<palabra>` bajo la carga de `PerformanceTest`.

RNF que aplican: RNF-09 (búsqueda en tiempo aceptable) y RNF-07 (búsqueda privada).

## Diseño
- **Servicio:** `ListingService.ShowcaseAsync` recibe `string? q`; valida y normaliza (recorte). Si `q` no está vacío, fuerza `IncludeSoldOut = false`. Sin `q`, la consulta actual no cambia.
- **Contrato de datos:** `ProductQuery` agrega `string? Search = null` como último parámetro opcional, para no tocar las llamadas existentes.
- **MongoDB:** `MongoProductRepository` agrega `Filter.Text(query.Search)` al filtro de `Showcase` (la comunidad y la categoría se combinan con `&`). El orden sigue siendo `_id` descendente y el cursor se aplica igual. `CountAsync` usa el mismo filtro.
- **Índice:** `{ title: "text", description: "text" }`, nombre `tx_title_description`, `default_language: "spanish"`, pesos `{ title: 3, description: 1 }`. Los pesos no cambian el orden (no se ordena por relevancia), pero documentan la intención y permiten activar relevancia sin reindexar.
- **En memoria:** `InMemoryProductRepository` normaliza título y descripción (minúsculas, sin acentos), parte en palabras y compara por palabra completa con tolerancia de plural (`s`/`es` final). Aproxima el *stemming*; los tests compartidos usan palabras donde ambas implementaciones coinciden.
- **Controller:** `ProductsController.Showcase` agrega `[FromQuery] string? q`. La política `showcase` de `OutputCache` ya varía por todos los parámetros de consulta (`SetVaryByQuery("*")`), así que `q` entra en la clave sin cambios; la etiqueta `showcase` sigue invalidando al publicar.
- **Errores:** `ListingsErrors.InvalidQuery` → `listings.invalid_query` (400), texto en español.
- **Saneado de la consulta:** `ListingService.SearchTerms` deja solo letras, dígitos y espacios antes de pasar el texto a `$text`. Así un `-`, una comilla o un `+` del usuario no se vuelven operadores del índice (OWASP API3).
- **Datos:** `db/007-busqueda.js` idempotente, `SCHEMA_VERSION = 7`, `createIndex` del índice de texto y `$max` en `_meta.version`. No hay *backfill*: MongoDB indexa los documentos existentes al crear el índice.
- **Rama:** `feature/SCRUM-26-buscar-productos`.
- **Módulo:** el catálogo `docs/modulos.md` mapea M-04 a `Modules/Marketplace/`, pero la vitrina, el repositorio y los DTO ya viven en `Modules/Listings/` (M-03). Esta HU reutiliza ese código y actualiza la fila del catálogo; crear un módulo paralelo duplicaría el endpoint y las pruebas.

## Contratos
- Fila canónica: [`docs/endpoints.md`](../../../docs/endpoints.md) → `GET /api/v1/products`. Cambia a `?campus=&category=&q=&hideSoldOut=&cursor=&limit=`; con `q` solo disponibles y orden por `_id`. Se enlaza, no se copia.
- Respuesta sin cambios: `ProductPage { items: ProductResponse[], nextCursor, total }`. Se reutiliza `ContractTest/Fixtures/product-page.json`; no hace falta fixture nuevo.
- Error nuevo en [`docs/errores.md`](../../../docs/errores.md): `listings.invalid_query` | 400 | texto de búsqueda sin letras ni dígitos, o de más de 80 caracteres | HU-09.
- Esquema: fila `products` de [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el índice `tx_title_description` pasa de 📐 a ✅ con `db/007`.
- Configuración: sin claves nuevas. `Catalog:DefaultCampus` sigue igual.

## Tests
Conteos reales al cerrar (07-Nov-2026): Unit 179 · Integration 99 · Security 83 · Contract 8 · Performance 4. `DatabaseTest` no se corrió en local (sin Docker); lo corre el CI.

- `TestShared/Listings/ListingRepositoryContract.cs` (corre en `UnitTest/Data/InMemoryListingRepositoryTests` y `DatabaseTest/MongoListingRepositoryTests`):
  - `Search_MatchesTitleAndDescription_AndOnlyAvailable`: coincide por título y por descripción; excluye `soldOut`, `hidden` y `removed` aunque se pidan los agotados; respeta la comunidad.
  - `Search_IgnoresCaseAccentsAndPlurals`: `CALCULADORA`, `calculadoras`, `cálculo`, `FISICA` y `  Casio  ` encuentran el mismo producto.
  - `Search_MatchesAnyWord_AndCombinesWithTheCategory`: una palabra de dos basta; con otra categoría no devuelve nada.
  - `Search_PagesWithTheCursor_AndCountsEveryMatch`: `nextCursor` continúa y `total` cuenta todas las coincidencias.
  - `Search_WithoutMatches_ReturnsNothing`.
- `DatabaseTest/SchemaScriptTests.cs`: `Products_HaveTheShowcaseAndSearchIndexes_AndRejectAFreeProduct` verifica que `tx_title_description` existe con `default_language: spanish` y los pesos 3 y 1. El guard que mantiene `ExpectedVersion = 7` alineado con el último script es `UnitTest/Data/SchemaVersionTests.cs`.
- `DatabaseTest/MongoListingRepositoryTests.cs`: `Search_WithOnlyStopWords_ReturnsAnEmptyPage` comprueba que una consulta con solo palabras vacías del español (`de la`) devuelve página vacía y no un error del índice.
- `IntegrationTest/Listings/ListingFlowTests.cs`:
  - `Search_FindsProductsByTitleAndDescription_NewestFirst`: publica dos productos con una palabra común (una en el título, otra en la descripción) y comprueba el orden y `total`.
  - `Search_WithoutMatches_ReturnsAnEmptyPage`: `items` vacío, `total: 0` y `nextCursor: null`.
  - `Search_WithAnInvalidQuery_IsRejected_AndAnEmptyOneIsTheShowcase`: 81 caracteres o sin letras ni dígitos → `400 listings.invalid_query`; `q=` vacío → `200`.
- `ContractTest/ListingsContractTests.cs`: `PhotoProductPageAndCategories_MatchTheirFixtures` agrega `GET /api/v1/products?q=calculadora` y compara su forma con `product-page.json` y `product.json`.
- `PerformanceTest/LatencyTests.cs`: `/api/v1/products?q=casio` en `PublicRoutes()` (RNF-09).
- `SecurityTest`: sin cambios de ruta; `Anonymous_IsServedFromTheCache_ButASession_NeverIs` ahora usa `?category=libros&limit=7&q=calculadora` para cubrir `q` en la clave de la caché sin que una sesión reciba la respuesta anónima. RNF-07 (no persistir ni registrar `q`) no tiene prueba automática: se cubre por revisión en `security-reviewer` (el log HTTP no incluye query string y `q` no se guarda), aceptado como *waiver* explícito.

## Criterios
Evidencia del 07-Nov-2026 (rama `feature/SCRUM-26-buscar-productos`):

- [x] `dotnet build Musubi.slnx -c Release` → 0 errores, 0 advertencias.
- [x] `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` → 179/179, incluidos los 5 casos de búsqueda en memoria.
- [x] `dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build` → 99/99, incluidos los 3 flujos de búsqueda.
- [x] `dotnet test --project SecurityTest/SecurityTest.csproj -c Release --no-build` → 83/83.
- [x] `dotnet test --project ContractTest/ContractTest.csproj -c Release --no-build` → 8/8 con `?q=calculadora`.
- [x] `dotnet test --project PerformanceTest/PerformanceTest.csproj -c Release --no-build` → 4/4; `/api/v1/products?q=casio` bajo el umbral de 100 ms.
- [ ] `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release --no-build` → sin Docker en local; lo corre el CI (job `database`).
- [x] `pwsh scripts/check_endpoints.ps1` → `OK (36 rutas con fila, 0 huerfanas)`.
- [x] `pwsh scripts/critic-guardrails.ps1` → `PASS (24 archivos analizados, 0 avisos)`.
- [x] `GET /api/v1/products?q=<palabra>` sin sesión devuelve solo `active`, de la más nueva a la más vieja, y `total` coincide con las coincidencias (`Search_FindsProductsByTitleAndDescription_NewestFirst`).
- [x] `GET /api/v1/products?q=zzzzqqqq` devuelve `200` con `items: []` y `total: 0` (`Search_WithoutMatches_ReturnsAnEmptyPage`).
- [x] `GET /api/v1/products?q=<81 caracteres>` devuelve `400 listings.invalid_query` (`Search_WithAnInvalidQuery_IsRejected_AndAnEmptyOneIsTheShowcase`).

## Límites
- **Orden por relevancia diferido:** se usa `_id` descendente. El puntaje de `$text` y un cursor compuesto quedan para una HU posterior; el índice ya tiene pesos para hacerlo sin reindexar. `NOTE (HU-09)`.
- **Sin coincidencia parcial ni por prefijo:** `calc` no encuentra `Calculadoras`. Si el PO lo pide, se necesita un campo normalizado y un *backfill*. `NOTE (HU-09)`.
- **Semántica OR:** una palabra basta para aparecer. No se exige que estén todas.
- **Campos:** solo `title` y `description`. Categoría y vendedor no se buscan (la categoría se filtra en HU-10).
- **Ordenamiento en memoria:** `$text` + orden por `_id` hace un *sort* bloqueante en MongoDB. Con el piloto (decenas de miles de documentos) y `limit ≤ 50` es aceptable; si la colección crece, se ordena por `textScore` o se agrega un índice compuesto. `NOTE (HU-09)`.
- **Estado vacío:** las opciones de guardar búsqueda o convertirla en solicitud son de HU-12 (app y endpoints propios). Aquí solo se garantiza la página vacía correcta.
- **Sin historial ni analítica:** RNF-07. `q` no se guarda ni se registra.
- **`q` en la clave de la caché de salida:** la política `showcase` varía por todos los parámetros y guarda la entrada hasta 30 s en memoria o Redis; el texto buscado vive ahí como clave, no como historial. Un `q` distinto crea una entrada distinta (sin tope propio), aceptado con el límite de 80 caracteres.
- **Rendimiento medido contra el *fake* en memoria:** `PerformanceTest` corre con `UseInMemoryDatabase=true`, así que el p95 de `?q=` no mide `$text` ni el *sort* bloqueante de MongoDB. Se confirma con `PERF_BASE_URL` contra el despliegue antes de cerrar RNF-09 en producción.
- **Palabras vacías:** `q` con solo palabras vacías (`de la`) pasa la validación y el analizador español la deja sin términos; `DatabaseTest` verifica que devuelve página vacía y no error.
- **Sin *rate limit* propio en la vitrina anónima:** solo `/auth/*` y la subida de fotos lo tienen. Un cliente puede generar `q` distintos y saltarse la caché; queda para el endurecimiento de abuso (RNF-11) y no bloquea esta HU (`security-reviewer`, WARN 2).
- **Paridad aproximada en memoria:** el *stemming* exacto de MongoDB no se replica; los tests compartidos usan casos equivalentes en ambas implementaciones.
- **Sin prueba HTTP de agotados:** ninguna ruta produce aún el estado `soldOut` (llega con HU-23/24), así que la exclusión se prueba en el contrato del repositorio y no en un flujo HTTP.
- **Filtros de precio y condición:** HU-10.
- **Spec de la app:** la contraparte Flutter ya existe en `musubi-app`; esta spec cubre solo la API.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 07-Nov-2026
- **Detalle:** decisiones acordadas con el usuario: palabra completa con índice de texto en español, orden por más recientes, `q` sobre `GET /products` y búsqueda solo en título y descripción. API implementada y verificada en local (Unit 179, Integration 99, Security 83, Contract 8, Performance 4, critic PASS, endpoints OK); `DatabaseTest` queda para el CI por falta de Docker en local. Pendiente el revisor para pasar a "Aprobado".

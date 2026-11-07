# HU-09 — Buscar productos por palabra clave

**HU / Jira / Sprint / Módulo / Contraparte:** HU-09 / SCRUM-26 / Sprint 2 (12 – 23 oct 2026) / M-04 / [musubi-app:HU-09](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-09-buscar-productos)

## Contexto
Jira (SCRUM-26): Como **comprador**, quiero **buscar productos por nombre o palabra clave**, para **encontrar rápido lo que necesito**.

- Maqueta: `Inicio-visitante`, `Se-busca-visitante`, `Producto-visitante`, `Visitante-modal`, `Busqueda`, `Busqueda-filtros`, `Busqueda-vacia`.
- Trazabilidad: RF-12 · CU-06 · RNF-09 · RNF-07.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-02`, `02-03`, `HU-06`, `HU-48` (vitrina), `HU-11` (detalle).

La vitrina `GET /api/v1/products` ya existe con `campus`, `category`, `hideSoldOut`, `cursor` y `limit`. Esta HU agrega la búsqueda por texto sobre esa misma ruta. No se crea un módulo nuevo: el endpoint, el repositorio y los DTO viven en `Modules/Listings/` y M-04 los reutiliza. El esquema gana `searchWords` (palabras normalizadas) y su índice, creados por `db/007-busqueda.js`.

## Requisitos
1. `GET /api/v1/products` acepta el parámetro opcional `q` (texto de búsqueda). Se recorta; si queda vacío se trata como ausente. Si trae texto, debe tener hasta 80 caracteres y al menos una letra o un dígito; si no cumple: `400 listings.invalid_query`.
2. Con `q`, la búsqueda devuelve **solo publicaciones `active`** de la comunidad pedida. Los agotados (`soldOut`) nunca aparecen, aunque llegue `hideSoldOut=false` (criterio 1 de Jira).
3. La coincidencia es por **prefijo de palabra** sobre `title` y `description`: basta el comienzo de la palabra, e ignora mayúsculas y acentos. `calc`, `calcula` y `calculadora` encuentran `Calculadora`; `calc` no encuentra `Calculadoras` escrito al revés ni `culadora`.
4. El plural final se tolera: la consulta pierde su `s`/`es` final antes de comparar, así `calculadoras` también encuentra `Calculadora`.
5. Un documento coincide si contiene **cualquiera** de los términos de `q`. Las palabras vacías del español son términos literales (`de` o `la` también buscan).
6. Los resultados salen de la **más nueva a la más vieja** y se paginan con el mismo cursor `_id` de la vitrina (`nextCursor`). `total` es el número de coincidencias, no el de la página.
7. `q` se combina con `campus` y `category` (los filtros de precio y condición llegan en HU-10).
8. Sin resultados: `200` con `items: []`, `nextCursor: null` y `total: 0`. La app muestra las opciones de HU-12; la API no inventa un error.
9. Sin `q`, o con `q` vacío, la vitrina se comporta **igual que hoy** (incluye agotados salvo `hideSoldOut=true`).
10. La búsqueda no se persiste ni se registra: no hay historial, ni texto de `q` en logs o analítica (RNF-07).
11. La ruta mantiene `[AllowAnonymous]`, la sesión opcional, `ETag`/`304` y la caché de salida actuales.
12. El campo `searchWords[]` (palabras normalizadas de título y descripción) y el índice `ix_community_status_search` los crea `db/007-busqueda.js`; `MongoSchema.ExpectedVersion` pasa a `7`. El script rellena las publicaciones existentes.
13. RNF-09: en proceso, p95 ≤ 100 ms para `GET /api/v1/products?q=<palabra>` bajo la carga de `PerformanceTest`.

RNF que aplican: RNF-09 (búsqueda en tiempo aceptable) y RNF-07 (búsqueda privada).

## Diseño
- **Servicio:** `ListingService.ShowcaseAsync` recibe `string? q`; valida y normaliza (recorte). Si `q` no está vacío, fuerza `IncludeSoldOut = false`. Sin `q`, la consulta actual no cambia.
- **Contrato de datos:** `ProductQuery` agrega `string? Search = null` como último parámetro opcional, para no tocar las llamadas existentes.
- **Normalización compartida:** `SearchWord.Normalize` (en `ListingRepositories.cs`) pasa una palabra a minúsculas, le quita acentos y su plural final (`s`/`es`); `SearchWord.Words` parte un texto y devuelve las palabras normalizadas sin repetir. La usan el documento y las dos implementaciones, así consulta y contenido se comparan en la misma forma.
- **Documento:** `ProductDocument.SearchWords` es una propiedad calculada sobre `title` + `description` con `SearchWord.Words`; el serializador la escribe en `searchWords` al insertar, así nunca queda desactualizada respecto del texto.
- **MongoDB:** cada término normalizado se busca con `^` + `Regex.Escape(term)` sobre `searchWords` (opción `i`) y los términos se combinan con `$or`. El `^` es un rango de prefijo que usa el índice `ix_community_status_search` (`communityId`, `status`, `searchWords`). El orden sigue siendo `_id` descendente y `CountAsync` usa el mismo filtro.
- **En memoria:** `InMemoryProductRepository` compara `SearchWords` con `StartsWith`; misma semántica que el prefijo de Mongo.
- **Controller:** `ProductsController.Showcase` agrega `[FromQuery] string? q`. La política `showcase` de `OutputCache` ya varía por todos los parámetros de consulta (`SetVaryByQuery("*")`), así que `q` entra en la clave sin cambios; la etiqueta `showcase` sigue invalidando al publicar.
- **Errores:** `ListingsErrors.InvalidQuery` → `listings.invalid_query` (400), texto en español.
- **Saneado de la consulta:** `ListingService.SearchTerms` deja solo letras, dígitos y espacios antes de construir el filtro. Así un `-`, una comilla o un `+` del usuario no se interpretan como sintaxis de búsqueda ni rompen el patrón (OWASP API3).
- **Datos:** `db/007-busqueda.js` idempotente, `SCHEMA_VERSION = 7`, rellena `searchWords` en las publicaciones que ya existían y crea `ix_community_status_search`; `$max` en `_meta.version`. El relleno replica las reglas de `SearchWord.Normalize`.
- **Rama:** `feature/SCRUM-26-buscar-productos`.
- **Módulo:** el catálogo `docs/modulos.md` mapea M-04 a `Modules/Marketplace/`, pero la vitrina, el repositorio y los DTO ya viven en `Modules/Listings/` (M-03). Esta HU reutiliza ese código y actualiza la fila del catálogo; crear un módulo paralelo duplicaría el endpoint y las pruebas.

## Contratos
- Fila canónica: [`docs/endpoints.md`](../../../docs/endpoints.md) → `GET /api/v1/products`. Cambia a `?campus=&category=&q=&hideSoldOut=&cursor=&limit=`; con `q` solo disponibles, coincidencia por prefijo y orden por `_id`. Se enlaza, no se copia.
- Respuesta sin cambios: `ProductPage { items: ProductResponse[], nextCursor, total }`. Se reutiliza `ContractTest/Fixtures/product-page.json`; no hace falta fixture nuevo.
- Error nuevo en [`docs/errores.md`](../../../docs/errores.md): `listings.invalid_query` | 400 | texto de búsqueda sin letras ni dígitos, o de más de 80 caracteres | HU-09.
- Esquema: fila `products` de [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); agrega `searchWords[]` e `ix_community_status_search` con `db/007`.
- Configuración: sin claves nuevas. `Catalog:DefaultCampus` sigue igual.

## Tests
Conteos reales al cerrar (07-Nov-2026): Unit 181 · Integration 100 · Security 84 · Contract 8 · Performance 4. `DatabaseTest` no se corrió en local (sin Docker); lo corre el CI.

- `TestShared/Listings/ListingRepositoryContract.cs` (corre en `UnitTest/Data/InMemoryListingRepositoryTests` y `DatabaseTest/MongoListingRepositoryTests`):
  - `Search_MatchesTitleAndDescription_AndOnlyAvailable`: coincide por título y por descripción; excluye `soldOut`, `hidden` y `removed` aunque se pidan los agotados; respeta la comunidad.
  - `Search_IgnoresCaseAccentsAndPlurals`: `CALCULADORA`, `calculadoras`, `cálculo`, `FISICA` y `  Casio  ` encuentran el mismo producto.
  - `Search_MatchesAnyWord_AndCombinesWithTheCategory`: una palabra de dos basta; con otra categoría no devuelve nada.
  - `Search_PagesWithTheCursor_AndCountsEveryMatch`: `nextCursor` continúa y `total` cuenta todas las coincidencias.
  - `Search_WithoutMatches_ReturnsNothing`.
  - `Search_MatchesWordPrefixes`: `calc`, `calcula`, `CASI`, `lap` y `leno` encuentran su producto; `culadora` (prefijo que no empieza la palabra) no encuentra nada.
- `UnitTest/Data/ProductSearchWordsTests.cs`: `SearchWords_AreSerializedNormalized_AndTheDocumentRoundTrips` comprueba que el BSON lleva `searchWords` en minúsculas, sin acentos y sin plural, y que el documento se puede deserializar.
- `DatabaseTest/SchemaScriptTests.cs`: `Products_HaveTheShowcaseIndexes_AndRejectAFreeProduct` verifica además que existe `ix_community_status_search`.
- `IntegrationTest/Listings/ListingFlowTests.cs`:
  - `Search_FindsProductsByTitleAndDescription_NewestFirst`: publica dos productos con una palabra común (una en el título, otra en la descripción) y comprueba el orden y `total`.
  - `Search_MatchesWordPrefixes`: `q=calc`, `q=calcula` y `q=CASI` encuentran la calculadora publicada.
  - `Search_WithoutMatches_ReturnsAnEmptyPage`: `items` vacío, `total: 0` y `nextCursor: null`.
  - `Search_WithAnInvalidQuery_IsRejected_AndAnEmptyOneIsTheShowcase`: 81 caracteres o sin letras ni dígitos → `400 listings.invalid_query`; `q=` vacío → `200`.
- `ContractTest/ListingsContractTests.cs`: `PhotoProductPageAndCategories_MatchTheirFixtures` agrega `GET /api/v1/products?q=calculadora` y compara su forma con `product-page.json` y `product.json`.
- `PerformanceTest/LatencyTests.cs`: `/api/v1/products?q=calc` en `PublicRoutes()` (RNF-09).
- `SecurityTest/Auth/ListingsAuthTests.cs`: `ShowcaseSearch_WithMetacharactersOrManyTerms_IsStillAPage` comprueba que `q=a.*|b` y 16 términos (justo bajo los 80 caracteres) devuelven `200` (sin inyección de regex y con el costo acotado).
- `SecurityTest/Caching/OutputCacheTests.cs`: `Anonymous_IsServedFromTheCache_ButASession_NeverIs` usa `?category=libros&limit=7&q=calculadora` para cubrir `q` en la clave de la caché sin que una sesión reciba la respuesta anónima. RNF-07 (no persistir ni registrar `q`) no tiene prueba automática: se cubre por revisión en `security-reviewer` (el log HTTP no incluye query string y `q` no se guarda), aceptado como *waiver* explícito.

## Criterios
Evidencia del 07-Nov-2026 (rama `feature/SCRUM-26-buscar-productos`):

- [x] `dotnet build Musubi.slnx -c Release` → 0 errores, 0 advertencias.
- [x] `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` → 180/180, incluidos los 6 casos de búsqueda en memoria.
- [x] `dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build` → 100/100, incluidos los 4 flujos de búsqueda.
- [x] `dotnet test --project SecurityTest/SecurityTest.csproj -c Release --no-build` → 84/84; incluye el saneo de metacaracteres y el tope de términos.
- [x] `dotnet test --project ContractTest/ContractTest.csproj -c Release --no-build` → 8/8 con `?q=calculadora`.
- [x] `dotnet test --project PerformanceTest/PerformanceTest.csproj -c Release --no-build` → 4/4; `/api/v1/products?q=calc` bajo el umbral de 100 ms.
- [ ] `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release --no-build` → sin Docker en local; lo corre el CI (job `database`).
- [x] `pwsh scripts/check_endpoints.ps1` → `OK (36 rutas con fila, 0 huerfanas)`.
- [x] `pwsh scripts/critic-guardrails.ps1` → `PASS (26 archivos analizados, 0 avisos)`.
- [x] `GET /api/v1/products?q=calc` sin sesión devuelve solo `active` y encuentra `Calculadora` (`Search_MatchesWordPrefixes`).
- [x] `GET /api/v1/products?q=<palabra>` devuelve de la más nueva a la más vieja y `total` coincide con las coincidencias (`Search_FindsProductsByTitleAndDescription_NewestFirst`).
- [x] `GET /api/v1/products?q=zzzzqqqq` devuelve `200` con `items: []` y `total: 0` (`Search_WithoutMatches_ReturnsAnEmptyPage`).
- [x] `GET /api/v1/products?q=<81 caracteres>` devuelve `400 listings.invalid_query` (`Search_WithAnInvalidQuery_IsRejected_AndAnEmptyOneIsTheShowcase`).

## Límites
- **Orden por relevancia diferido:** se usa `_id` descendente. Un orden por puntaje y un cursor compuesto quedan para una HU posterior. `NOTE (HU-09)`.
- **Sin coincidencia a mitad de palabra:** `culadora` no encuentra `Calculadora`; la coincidencia es por prefijo. Si el PO pide infijo, hace falta otro campo normalizado y su *backfill*. `NOTE (HU-09)`.
- **`searchWords` se calcula al insertar:** una edición futura del título o la descripción debe recalcularlo (hoy no hay edición; `NOTE (HU-09)`). Si quedara desactualizado, la búsqueda lo ignora en silencio.
- **Orden por `_id` con índice de prefijo:** el índice resuelve la coincidencia, pero el orden por `_id` se hace en memoria sobre las coincidencias; con `limit ≤ 50` y un prefijo razonable es aceptable. `NOTE (HU-09)`.
- **Términos acotados a 5:** `q` se sanea y se toman los primeros 5 términos; el resto se ignora. Acota el costo del `$regex` (OWASP API4) sin afectar el uso real.
- **Semántica OR:** una palabra basta para aparecer. No se exige que estén todas.
- **Palabras vacías:** a diferencia de `$text`, los términos cortos (`de`, `la`, `que`) son literales y también buscan.
- **Campos:** solo `title` y `description`. Categoría y vendedor no se buscan (la categoría se filtra en HU-10).
- **Estado vacío:** las opciones de guardar búsqueda o convertirla en solicitud son de HU-12 (app y endpoints propios). Aquí solo se garantiza la página vacía correcta.
- **Sin historial ni analítica:** RNF-07. `q` no se guarda ni se registra.
- **`q` en la clave de la caché de salida:** la política `showcase` varía por todos los parámetros y guarda la entrada hasta 30 s en memoria o Redis; el texto buscado vive ahí como clave, no como historial. Un `q` distinto crea una entrada distinta (sin tope propio), aceptado con el límite de 80 caracteres.
- **Rendimiento medido contra el *fake* en memoria:** `PerformanceTest` corre con `UseInMemoryDatabase=true`, así que el p95 de `?q=` no mide el `$regex` de MongoDB. Se confirma con `PERF_BASE_URL` contra el despliegue antes de cerrar RNF-09 en producción.
- **Sin *rate limit* ni `MaxTime` en la vitrina anónima:** solo `/auth/*` y la subida de fotos tienen límite. Con el tope de 5 términos el escaneo por petición queda acotado, pero un cliente puede generar `q` distintos y saltarse la caché; el límite por IP y un `MaxTime` en `Find`/`CountDocuments` quedan para el endurecimiento de abuso (RNF-11).
- **Paridad entre implementaciones:** ambas normalizan igual consulta y documento (`SearchWord`); solo cambia el motor (`StartsWith` contra `$regex` con índice).
- **Sin prueba HTTP de agotados:** ninguna ruta produce aún el estado `soldOut` (llega con HU-23/24), así que la exclusión se prueba en el contrato del repositorio y no en un flujo HTTP.
- **Filtros de precio y condición:** HU-10.
- **Spec de la app:** la contraparte Flutter ya existe en `musubi-app`; esta spec cubre solo la API.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 07-Nov-2026
- **Detalle:** decisiones acordadas con el usuario: búsqueda por prefijo de palabra (sin mayúsculas ni acentos), orden por más recientes, `q` sobre `GET /products` y búsqueda solo en título y descripción. API implementada y verificada en local (Unit 181, Integration 100, Security 83, Contract 8, Performance 4, critic PASS, endpoints OK); `DatabaseTest` queda para el CI por falta de Docker en local. Pendiente el revisor para pasar a "Aprobado".

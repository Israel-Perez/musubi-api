# plan.md — HU-10 Filtrar por categoría y precio

1. **Componentes:**
   - `Musubi.Api/Modules/Listings/ProductsController.cs`: `category` repetido y parámetros `minPriceCents`, `maxPriceCents`, `condition`, `sort`, `page`.
   - `Musubi.Api/Modules/Listings/ListingService.cs`: normalización, validación, cursor y `nextCursor`.
   - `Musubi.Api/Modules/Listings/ListingsErrors.cs`: `InvalidPriceRange`, `InvalidSort` e `InvalidPage`.
   - `Musubi.Api/Modules/Listings/Data/ListingRepositories.cs`: `ProductQuery`, `ProductCursor`, `ProductCursorCodec` y `ProductSorts`.
   - `Musubi.Api/Modules/Listings/Data/MongoListingRepositories.cs`: filtros, cursor compuesto y orden.
   - `Musubi.Api/Modules/Listings/Data/InMemoryListingRepositories.cs`: misma semántica que Mongo.
   - `db/010-filtros-precio.js` y `Musubi.Api/Platform/Data/MongoSchema.cs`: cuatro índices de precio (asc/desc, con y sin categoría) y `ExpectedVersion = 10`.
   - `TestShared/Listings/ListingRepositoryContract.cs`, `IntegrationTest/Listings/ListingFlowTests.cs`, `SecurityTest/Auth/ListingsAuthTests.cs`, `ContractTest/ListingsContractTests.cs` y `DatabaseTest/SchemaScriptTests.cs`.
   - `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `db/README.md`, `docs/trazabilidad.md` y `Memoria.md`.

2. **Decisiones:**
   - **Extender `GET /products` en lugar de una ruta nueva:** la app ya consume esa ruta y comparte respuesta, auth, caché, `ETag` y cursor. Otra ruta duplicaría controller, política y pruebas.
   - **`category` repetido en lugar de un parámetro nuevo:** una sola categoría sigue funcionando y la maqueta puede mandar varias sin un formato coma que haya que parsear.
   - **Cursor compuesto:** ordenar por precio con un cursor solo de `_id` salta o repite resultados. El token lleva el orden y la clave de orden; el cliente lo trata opaco.
   - **Aceptar el cursor viejo solo para `newest`/`relevance`:** evita romper la app de HU-09 mientras se cambia el formato. Un cursor viejo con otro orden no tiene sentido y se rechaza.
   - **`relevance` como alias de `newest`:** no hay puntaje de relevancia y HU-09 lo difirió. La maqueta conserva su etiqueta; el orden es honesto y documentado.
   - **Índices de precio en `db/010`:** sin ellos, `priceAsc`/`priceDesc` ordenan en memoria. El script solo crea índices; no reescribe documentos.
   - **Reutilizar `listings.invalid_condition`:** el valor inválido es el mismo en publicación y filtro; no se duplica un código.
   - **Tope de precio con `MaxPriceCents`:** reutiliza el máximo de publicación (99 999 900) para que el rango nunca pida valores imposibles.
   - **`page` para la barra de paginación:** `skip = (page - 1) * limit` en `long`, con `MaxPage = 50`; ignora `cursor` y no devuelve uno. `cursor` queda para el desplazamiento continuo.
   - **`hideSoldOut` opcional:** sin el parámetro la búsqueda oculta los agotados (HU-09) y la vitrina los muestra; `false` explícito los incluye aunque haya `q`.

3. **Excepciones al stack (00-01):** ninguna. Se usa MongoDB, ASP.NET y lo ya instalado.

4. **Guardarraíles:**
   - Validación en el borde: rango, condición, orden y página tienen lista blanca; lo inválido es 400 y no toca la base.
   - La comunidad se aplica siempre: ningún filtro cruza campus (RNF-21, OWASP API1).
   - Las categorías se comparan como igualdad (`$in`), nunca como regex: no hay inyección.
   - `q` conserva su saneado y su tope de términos; los filtros nuevos no lo relajan.
   - El cursor valida orden, precio e `id`; no se confía en el cliente.
   - `CountAsync` no aplica cursor: `total` no cambia al paginar.
   - Los índices van en el mismo PR que el código; sin ellos el orden por precio no es indexado.
   - `NOTE (HU-10)` para relevancia real, orden por precio con `q` y tope de categorías.

5. **Validación:**
   - Build Release 0/0.
   - `UnitTest`, `IntegrationTest`, `SecurityTest` y `ContractTest` verdes con `--no-build`.
   - `DatabaseTest` con Docker o en CI: contrato compartido e índices de precio.
   - `pwsh scripts/check_endpoints.ps1` y `pwsh scripts/critic-guardrails.ps1`.
   - Prueba manual: publicar productos con categorías, precios y condiciones distintas; filtrar y ordenar; recorrer dos páginas con el cursor.
   - `semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error` antes del primer push.

6. **Secuencia de implementación:**
   1. Specs y rama.
   2. `db/010-filtros-precio.js` y `MongoSchema.ExpectedVersion = 10`.
   3. `ProductQuery`, `ProductCursor`, `ProductSorts` y codec.
   4. Repositorios Mongo e InMemory (rojo → verde con la batería compartida).
   5. Servicio, errores y controller.
   6. Pruebas de integración, seguridad y contrato.
   7. Documentación y `Memoria.md`.
   8. Verificación completa y revisores.

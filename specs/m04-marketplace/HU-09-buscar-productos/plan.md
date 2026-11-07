# plan.md — HU-09 Buscar productos por palabra clave

1. **Componentes:**
   - `Musubi.Api/Modules/Listings/ProductsController.cs`: parámetro `q` en `Showcase`.
   - `Musubi.Api/Modules/Listings/ListingService.cs`: `ShowcaseAsync(..., string? q, ...)`, validación y forzar solo disponibles.
   - `Musubi.Api/Modules/Listings/ListingsErrors.cs`: `InvalidQuery`.
   - `Musubi.Api/Modules/Listings/Data/ListingRepositories.cs`: `ProductQuery.Search`.
   - `Musubi.Api/Modules/Listings/Data/MongoListingRepositories.cs`: filtro `$text` y `CountAsync` con el mismo filtro.
   - `Musubi.Api/Modules/Listings/Data/InMemoryListingRepositories.cs`: aproximación por palabra completa con plural.
   - `db/007-busqueda.js`: índice `tx_title_description` y versión 7.
   - `Musubi.Api/Platform/Data/MongoSchema.cs`: `ExpectedVersion = 7`.
   - `TestShared/Listings/ListingRepositoryContract.cs`, `DatabaseTest/SchemaScriptTests.cs`, `IntegrationTest/Listings/ListingFlowTests.cs`, `ContractTest/ListingsContractTests.cs`, `PerformanceTest/LatencyTests.cs`.

2. **Decisiones:**
   - **Extender `GET /products` con `q` en lugar de una ruta nueva:** la app ya consume esa ruta y comparte respuesta, auth, caché, `ETag` y cursor. Una ruta `/products/search` duplicaría controller, política de caché y pruebas.
   - **Índice de texto en español en lugar de regex o campo normalizado:** MongoDB ya resuelve mayúsculas, acentos, plurales y palabras vacías, y el modelo de datos ya previó `tx_title_description`. Un campo normalizado exigiría *backfill* y un segundo camino que mantener.
   - **Solo disponibles cuando hay `q`:** el criterio de Jira es explícito. La vitrina sin `q` conserva su comportamiento actual para no romper HU-48.
   - **Orden por más recientes:** reutiliza el cursor `_id` existente y evita un cursor compuesto con `textScore`. La relevancia puede llegar después sin reindexar.
   - **Título y descripción:** cubre "nombre o palabra clave". Buscar en categoría o vendedor mezcla búsqueda con filtros y expone nombres de vendedores (RNF-05); la categoría ya se filtra.
   - **Índice nuevo en `db/007` y versión 7:** un cambio de esquema es un script nuevo; `/health/ready` impide que una instancia arranque contra una base sin el índice.
   - **Sin persistencia de `q`:** RNF-07. No hay historial ni analítica en esta HU; HU-12 guarda búsquedas con consentimiento y su propia colección.
   - **`q` vacío equivale a ausente:** evita un error por un campo de búsqueda limpio y mantiene la vitrina.
   - **Máximo 80 caracteres:** igual al campo más largo que se indexa; corta consultas abusivas sin truncar en silencio.

3. **Excepciones al stack (00-01):** ninguna. Se usa MongoDB y lo que ya está instalado.

4. **Guardarraíles:**
   - Validación en el borde: `q` recortado; vacío se ignora; con texto, hasta 80 caracteres y al menos una letra o dígito; si no, `listings.invalid_query` (400).
   - El filtro de comunidad se mantiene siempre: una búsqueda nunca cruza campus (RNF-21, OWASP API1).
   - Solo `active`: agotados, pausados y quitados no aparecen.
   - Sin texto de búsqueda en logs ni en la caché de salida más allá de la clave de la URL (la política actual varía por `q`).
   - El índice se crea con `default_language: spanish`; sin él, `$text` falla y `/health/ready` ya bloquea la instancia por versión.
   - `NOTE (HU-09)` para relevancia, coincidencia parcial y *sort* bloqueante.

5. **Validación:**
   - Build Release 0/0.
   - `UnitTest`, `IntegrationTest`, `ContractTest` y `SecurityTest` en verde con `--no-build`.
   - `DatabaseTest` con Docker: contrato compartido y `tx_title_description`.
   - `PerformanceTest`: p95 ≤ 100 ms en `?q=casio`.
   - `pwsh scripts/check_endpoints.ps1` y `pwsh scripts/critic-guardrails.ps1`.
   - Prueba manual: publicar dos productos, buscar una palabra del título y otra de la descripción; marcar uno como agotado y confirmar que desaparece de la búsqueda pero sigue en la vitrina.
   - `semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error` antes del primer push.

6. **Secuencia de implementación:**
   1. Script `db/007-busqueda.js` y `MongoSchema.ExpectedVersion = 7`.
   2. `ProductQuery.Search` y las dos implementaciones del repositorio.
   3. Batería compartida en `TestShared/Listings` (rojo → verde en memoria y Mongo).
   4. Servicio, error y controller.
   5. Pruebas de integración, contrato y rendimiento.
   6. Documentación (`endpoints`, `errores`, `modelo-datos`, `db/README`, `trazabilidad`, `Memoria`) y PR. `versionado` y `<Version>` no se tocan: la fila y el salto van en el PR que despliega.

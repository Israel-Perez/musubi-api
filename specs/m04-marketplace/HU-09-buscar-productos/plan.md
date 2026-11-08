# plan.md — HU-09 Buscar productos por palabra clave

1. **Componentes:**
   - `Musubi.Api/Modules/Listings/ProductsController.cs`: parámetro `q` en `Showcase`.
   - `Musubi.Api/Modules/Listings/ListingService.cs`: `ShowcaseAsync(..., string? q, ...)`, validación y forzar solo disponibles.
   - `Musubi.Api/Modules/Listings/ListingsErrors.cs`: `InvalidQuery`.
   - `Musubi.Api/Modules/Listings/Data/ListingRepositories.cs`: `ProductQuery.Search`, `SearchWord.Normalize` y `SearchWord.Words`.
   - `Musubi.Api/Modules/Listings/Data/ListingDocuments.cs`: `ProductDocument.SearchWords` (propiedad calculada que se serializa).
   - `Musubi.Api/Modules/Listings/Data/MongoListingRepositories.cs`: prefijo indexado (`^` + `$regex`) y `CountAsync` con el mismo filtro.
   - `Musubi.Api/Modules/Listings/Data/InMemoryListingRepositories.cs`: misma normalización y comparación con `StartsWith`.
   - `db/007-busqueda.js`: relleno de `searchWords` e índice `ix_community_status_search`; `Musubi.Api/Platform/Data/MongoSchema.cs`: `ExpectedVersion = 7`.
   - `TestShared/Listings/ListingRepositoryContract.cs`, `DatabaseTest/SchemaScriptTests.cs`, `IntegrationTest/Listings/ListingFlowTests.cs`, `ContractTest/ListingsContractTests.cs`, `PerformanceTest/LatencyTests.cs`.

2. **Decisiones:**
   - **Extender `GET /products` con `q` en lugar de una ruta nueva:** la app ya consume esa ruta y comparte respuesta, auth, caché, `ETag` y cursor. Una ruta `/products/search` duplicaría controller, política de caché y pruebas.
   - **Prefijo de palabra en lugar de palabra completa:** `calc` y `calcula` deben encontrar `Calculadora`. `$text` no soporta prefijos y Atlas Search no está disponible en el despliegue (MongoDB Community), así que se compara por prefijo con `$regex`.
   - **Campo `searchWords` indexado en lugar de regex sobre el texto original:** el `^` sobre un campo multikey usa `ix_community_status_search`; un regex sobre `title`/`description` recorrería toda la comunidad. El costo es un campo derivado y su *backfill*.
   - **Acentos y plural resueltos al escribir, no al consultar:** `SearchWord.Normalize` deja las palabras en minúsculas, sin acentos y sin `s`/`es` final; el documento y la consulta se guardan/buscan en esa forma. Sin variantes que mantener en el índice.
   - **Solo disponibles cuando hay `q`:** el criterio de Jira es explícito. La vitrina sin `q` conserva su comportamiento actual para no romper HU-48.
   - **Orden por más recientes:** reutiliza el cursor `_id` existente. La relevancia puede llegar después.
   - **Título y descripción:** cubre "nombre o palabra clave". Buscar en categoría o vendedor mezcla búsqueda con filtros y expone nombres de vendedores (RNF-05); la categoría ya se filtra.
   - **Sin índice ni script nuevos:** un `$regex` anclado a una clase de borde no aprovecha un índice de texto ni uno compuesto; se evita un cambio de esquema que no aportaría. El costo de escaneo queda acotado por comunidad y estado.
   - **Sin persistencia de `q`:** RNF-07. No hay historial ni analítica en esta HU; HU-12 guarda búsquedas con consentimiento y su propia colección.
   - **`q` vacío equivale a ausente:** evita un error por un campo de búsqueda limpio y mantiene la vitrina.
   - **Máximo 80 caracteres:** corta consultas abusivas sin truncar en silencio.

3. **Excepciones al stack (00-01):** ninguna. Se usa MongoDB y lo que ya está instalado.

4. **Guardarraíles:**
   - Validación en el borde: `q` recortado; vacío se ignora; con texto, hasta 80 caracteres y al menos una letra o dígito; si no, `listings.invalid_query` (400).
   - El filtro de comunidad se mantiene siempre: una búsqueda nunca cruza campus (RNF-21, OWASP API1).
   - Solo `active`: agotados, pausados y quitados no aparecen.
   - Sin texto de búsqueda en logs; en la caché de salida solo vive como parte de la clave de la URL.
   - `SearchTerms` deja solo letras, dígitos y espacios y toma como máximo 5 términos: el patrón se construye después con `Regex.Escape`, así nada del usuario se vuelve sintaxis y el costo queda acotado (OWASP API3/API4).
   - El índice `ix_community_status_search` y el relleno de `searchWords` van en el mismo PR que el código; sin ellos el prefijo no es un rango indexado.
   - `NOTE (HU-09)` para relevancia, coincidencia a mitad de palabra y recálculo de `searchWords` si algún día se edita el texto.

5. **Validación:**
   - Build Release 0/0.
   - `UnitTest`, `IntegrationTest`, `ContractTest` y `SecurityTest` en verde con `--no-build`.
   - `DatabaseTest` con Docker: contrato compartido e `ix_community_status_search` presente.
   - `PerformanceTest`: p95 ≤ 100 ms en `?q=calc`.
   - `pwsh scripts/check_endpoints.ps1` y `pwsh scripts/critic-guardrails.ps1`.
   - Prueba manual: publicar dos productos, buscar un prefijo del título y otro de la descripción; marcar uno como agotado y confirmar que desaparece de la búsqueda pero sigue en la vitrina.
   - `semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error` antes del primer push.

6. **Secuencia de implementación:**
   1. `ProductQuery.Search`, `SearchWord` y `ProductDocument.SearchWords`.
   2. `db/007-busqueda.js` y `MongoSchema.ExpectedVersion = 7`.
   3. Las dos implementaciones del repositorio (rojo → verde con la batería compartida).
   4. Servicio, error y controller.
   5. Pruebas de integración, contrato y rendimiento.
   6. Documentación (`endpoints`, `errores`, `modelo-datos`, `db/README`, `trazabilidad`, `Memoria`) y PR. `versionado` y `<Version>` no se tocan: la fila y el salto van en el PR que despliega.

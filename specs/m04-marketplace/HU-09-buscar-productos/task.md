# HU-09 — Buscar productos por palabra clave (tareas)

## T1 — Campo, índice y repositorios
- **Crear:** `db/007-busqueda.js` (`SCHEMA_VERSION = 7`): relleno de `searchWords` en las publicaciones existentes e índice `{ communityId: 1, status: 1, searchWords: 1 }` (`ix_community_status_search`).
- **Cambiar:** `Musubi.Api/Platform/Data/MongoSchema.cs` → `ExpectedVersion = 7`.
- **Cambiar:** `Modules/Listings/Data/ListingRepositories.cs` → `ProductQuery(..., string? Search = null)`, `SearchWord.Normalize` (minúsculas, sin acentos, sin plural final) y `SearchWord.Words`.
- **Cambiar:** `ListingDocuments.cs` → `ProductDocument.SearchWords`, calculada sobre título y descripción y serializada por el driver.
- **Cambiar:** `MongoListingRepositories.cs` → por término, `^` + `Regex.Escape` sobre `searchWords` (opción `i`) y `$or` entre términos.
- **Cambiar:** `InMemoryListingRepositories.cs` → `SearchWords` y comparación con `StartsWith`.
- **Pruebas:** `TestShared/Listings/ListingRepositoryContract.cs` con los 6 casos de `spec.md` → `Tests`; corren en `UnitTest` y `DatabaseTest`.
- **Verificar:** `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` y `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release --no-build` (Docker).
- **Estado:** ✅ Hecho (07-Nov-2026). Rojo primero (`Search_MatchesWordPrefixes`: `Assert.NotEmpty() Failure: Collection was empty`), luego verde. `DatabaseTest` sin Docker en local: lo corre el CI.

## T2 — Servicio y controller
- **Cambiar:** `ListingService.ShowcaseAsync` → parámetro `string? q`; se recorta, vacío se ignora, con texto valida ≤ 80 y al menos una letra o dígito, y `SearchTerms` deja solo letras, dígitos y espacios antes de construir el filtro.
- **Cambiar:** `ListingsErrors.cs` → `InvalidQuery` (`listings.invalid_query`, 400).
- **Cambiar:** `ProductsController.Showcase` → `[FromQuery] string? q`.
- **Verificar:** `dotnet build Musubi.slnx -c Release` 0/0.
- **Estado:** ✅ Hecho (07-Nov-2026). La exclusión de agotados quedó en el repositorio (decisión del `plan.md`): cualquier consulta con `Search` devuelve solo `active`.

## T3 — Pruebas de flujo, contrato y rendimiento
- **Cambiar:** `IntegrationTest/Listings/ListingFlowTests.cs` → `Search_FindsProductsByTitleAndDescription_NewestFirst`, `Search_MatchesWordPrefixes`, `Search_WithoutMatches_ReturnsAnEmptyPage`, `Search_WithAnInvalidQuery_IsRejected_AndAnEmptyOneIsTheShowcase`.
- **Cambiar:** `ContractTest/ListingsContractTests.cs` → llamada `?q=calculadora` contra `Fixtures/product-page.json` y `Fixtures/product.json`.
- **Cambiar:** `PerformanceTest/LatencyTests.cs` → `"/api/v1/products?q=calc"` en `PublicRoutes()`.
- **Verificar:** Integration 100/100, Contract 8/8 y Performance 4/4 en verde.
- **Estado:** ✅ Hecho (07-Nov-2026). Sin prueba HTTP de agotados: ninguna ruta produce `soldOut` todavía (HU-23/24).

## T4 — Documentación
- **Actualizar:** fila `GET /api/v1/products` de `docs/endpoints.md` (agregar `q`, HU-09, prefijo y solo disponibles).
- **Actualizar:** `docs/errores.md` con `listings.invalid_query`.
- **Actualizar:** fila `products` de `docs/modelo-datos.md` (`searchWords[]` e `ix_community_status_search` con `db/007`).
- **Actualizar:** fila de M-04 en `docs/modulos.md` → `Musubi.Api/Modules/Listings/` (reutiliza la vitrina).
- **Actualizar:** `db/README.md` (fila `007`), `docs/trazabilidad.md` (estado y pruebas de HU-09) y `Memoria.md`.
- **Verificar:** `pwsh scripts/check_endpoints.ps1` → `OK`; `pwsh scripts/critic-guardrails.ps1` → `PASS`.
- **Estado:** ✅ Hecho (07-Nov-2026). `endpoints: OK (36 rutas con fila, 0 huerfanas)`; `critic: PASS`. Sin cambio en `docs/versionado.md` ni en `<Version>`: la fila y el salto de versión van en el PR que despliega.

## T5 — Cierre
- **Verificar:** build 0/0; Unit, Integration, Security, Contract y Performance en verde; Semgrep limpio.
- **Revisar:** `security-reviewer` (OWASP API Top 10) y `traceability-clerk` (spec ↔ código ↔ test ↔ Jira).
- **Estado:** ✅ Hecho (07-Nov-2026). `security-reviewer`: **PASS** (avisos atendidos o documentados en Límites; la revisión original era con `$text`, la validación de frontera y el filtro de comunidad no cambiaron con el paso a prefijo). `traceability-clerk`: drift corregido en spec, plan y task. Semgrep no está instalado en local: lo corre el job `semgrep` del CI.

## Pendientes
- `NOTE (HU-09)`: orden por relevancia y cursor compuesto; hoy es `_id` descendente.
- `NOTE (HU-09)`: coincidencia a mitad de palabra (infijo); hoy es prefijo.
- `NOTE (HU-09)`: `searchWords` se calcula al insertar; si algún día se edita título o descripción, hay que recalcularlo.
- `NOTE (HU-09)`: el índice resuelve el prefijo, pero el orden por `_id` se hace en memoria sobre las coincidencias.
- Historial de búsquedas y estado vacío con opciones: HU-12 (app); sin `NOTE` aquí porque no hay código de esta HU que difiera.
- Filtros de precio y condición combinados con `q`: HU-10; sin `NOTE` aquí porque no hay código de esta HU que difiera.

# HU-09 — Buscar productos por palabra clave (tareas)

## T1 — Esquema e índice de texto
- **Crear:** `db/007-busqueda.js` (`SCHEMA_VERSION = 7`).
- **Cambiar:** `Musubi.Api/Platform/Data/MongoSchema.cs` → `ExpectedVersion = 7`.
- **Detalle:** `createIndex({ title: "text", description: "text" }, { name: "tx_title_description", default_language: "spanish", weights: { title: 3, description: 1 } })`; `$max` de `_meta.version`; sin *backfill*.
- **Verificar:** `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build`; `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release --no-build`.
- **Estado:** ✅ Hecho (07-Nov-2026). Unit 179/179; `DatabaseTest` sin Docker en local, lo corre el CI.

## T2 — Repositorios
- **Cambiar:** `Modules/Listings/Data/ListingRepositories.cs` → `ProductQuery(..., string? Search = null)`.
- **Cambiar:** `MongoListingRepositories.cs` → `Filter.Text(query.Search)` en `Showcase` y en `CountAsync`.
- **Cambiar:** `InMemoryListingRepositories.cs` → normalizar (minúsculas, sin acentos), partir en palabras y comparar por palabra completa con plural `s`/`es`.
- **Pruebas:** `TestShared/Listings/ListingRepositoryContract.cs` con los 5 casos de `spec.md` → `Tests`; corren en `UnitTest` y `DatabaseTest`.
- **Verificar:** `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` y `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release --no-build` (Docker).
- **Estado:** ✅ Hecho (07-Nov-2026). Rojo primero (`CS1729: 'ProductQuery' no contiene un constructor que tome 6 argumentos`), luego verde.

## T3 — Servicio y controller
- **Cambiar:** `ListingService.ShowcaseAsync` → parámetro `string? q`; se recorta, vacío se ignora, con texto valida ≤ 80 y al menos una letra o dígito, y `SearchTerms` deja solo letras, dígitos y espacios antes de `$text`.
- **Cambiar:** `ListingsErrors.cs` → `InvalidQuery` (`listings.invalid_query`, 400).
- **Cambiar:** `ProductsController.Showcase` → `[FromQuery] string? q`.
- **Verificar:** `dotnet build Musubi.slnx -c Release` 0/0.
- **Estado:** ✅ Hecho (07-Nov-2026). La exclusión de agotados quedó en el repositorio (decisión del `plan.md`): cualquier consulta con `Search` devuelve solo `active`.

## T4 — Pruebas de flujo, contrato y rendimiento
- **Cambiar:** `IntegrationTest/Listings/ListingFlowTests.cs` → `Search_FindsProductsByTitleAndDescription_NewestFirst`, `Search_WithoutMatches_ReturnsAnEmptyPage`, `Search_WithAnInvalidQuery_IsRejected_AndAnEmptyOneIsTheShowcase`.
- **Cambiar:** `ContractTest/ListingsContractTests.cs` → llamada `?q=calculadora` contra `Fixtures/product-page.json` y `Fixtures/product.json`.
- **Cambiar:** `PerformanceTest/LatencyTests.cs` → `"/api/v1/products?q=casio"` en `PublicRoutes()`.
- **Verificar:** Integration 99/99, Contract 8/8 y Performance 4/4 en verde.
- **Estado:** ✅ Hecho (07-Nov-2026). Sin prueba HTTP de agotados: ninguna ruta produce `soldOut` todavía (HU-23/24).

## T5 — Documentación
- **Actualizar:** fila `GET /api/v1/products` de `docs/endpoints.md` (agregar `q`, HU-09 y el comportamiento solo disponibles).
- **Actualizar:** `docs/errores.md` con `listings.invalid_query`.
- **Actualizar:** fila `products` de `docs/modelo-datos.md` (`tx_title_description` ✅, `db/007`).
- **Actualizar:** fila de M-04 en `docs/modulos.md` → `Musubi.Api/Modules/Listings/` (reutiliza la vitrina).
- **Actualizar:** `db/README.md` (filas de `005`, `006` y `007`).
- **Actualizar:** `docs/trazabilidad.md` (estado y pruebas de HU-09) y `Memoria.md`.
- **Verificar:** `pwsh scripts/check_endpoints.ps1` → `OK`; `pwsh scripts/critic-guardrails.ps1` → `PASS`.
- **Estado:** ✅ Hecho (07-Nov-2026). `endpoints: OK (36 rutas con fila, 0 huerfanas)`; `critic: PASS (24 archivos analizados, 0 avisos)`. Sin cambio en `docs/versionado.md` ni en `<Version>`: la fila y el salto de versión van en el PR que despliega.

## T6 — Cierre
- **Verificar:** build 0/0; Unit, Integration, Security, Contract y Performance en verde; Semgrep limpio.
- **Revisar:** `security-reviewer` (OWASP API Top 10) y `traceability-clerk` (spec ↔ código ↔ test ↔ Jira).
- **Estado:** ✅ Hecho (07-Nov-2026). `security-reviewer`: **PASS** con 6 avisos; el script quedó versionado, se agregaron la prueba de palabras vacías, `q` en la prueba de caché y los límites de caché/rendimiento/rate limit, y la validación de frontera se mantiene. `traceability-clerk`: 8 drift corregidos (nombres de prueba, orden de parámetros, estado en `trazabilidad.md`, `plan.md`, `NOTE` inexistentes, evidencia del guard de esquema y waiver de RNF-07). Semgrep no está instalado en local: lo corre el job `semgrep` del CI.

## Pendientes
- `NOTE (HU-09)`: orden por relevancia (`textScore`) y cursor compuesto; hoy es `_id` descendente.
- `NOTE (HU-09)`: coincidencia parcial o por prefijo; hoy es palabra completa.
- `NOTE (HU-09)`: *sort* bloqueante de `$text` + `_id`; revisar si `products` crece.
- Historial de búsquedas y estado vacío con opciones: HU-12 (app); sin `NOTE` aquí porque no hay código de esta HU que difiera.
- Filtros de precio y condición combinados con `q`: HU-10; sin `NOTE` aquí porque no hay código de esta HU que difiera.

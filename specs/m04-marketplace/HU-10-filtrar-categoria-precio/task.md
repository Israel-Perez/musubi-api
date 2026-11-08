# HU-10 — Filtrar por categoría y precio (tareas)

## T1 — Repositorios
- **Cambiar:** `Modules/Listings/Data/ListingRepositories.cs` → `ProductQuery(..., long? MinPriceCents = null, long? MaxPriceCents = null, string? Condition = null)`.
- **Cambiar:** `MongoListingRepositories.cs` → `Filter.Gte`/`Filter.Lte` sobre `PriceCents` y `Filter.Eq` sobre `Condition` en `Showcase` y `CountAsync`.
- **Cambiar:** `InMemoryListingRepositories.cs` → mismas comparaciones dentro de `Showcase`.
- **Pruebas:** `TestShared/Listings/ListingRepositoryContract.cs` con los 3 casos de `spec.md` → `Tests`; corren en `UnitTest` y `DatabaseTest`.
- **Verificar:** `dotnet test --project UnitTest/UnitTest.csproj -c Release` y `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release` (Docker).
- **Estado:** ✅ Hecho (Unit 184; Database pendiente en local sin Docker, lo corre el CI).

## T2 — Servicio, error y controller
- **Cambiar:** `ListingService.ShowcaseAsync` → parámetros `long? minPriceCents`, `long? maxPriceCents`, `string? condition`; valida `0..MaxPriceCents`, `min ≤ max` y estado.
- **Cambiar:** `ListingsErrors.cs` → `InvalidPriceRange` (`listings.invalid_price_range`, 400); reutiliza `InvalidCondition`.
- **Cambiar:** `ProductsController.Showcase` → los tres `[FromQuery]`.
- **Verificar:** `dotnet build Musubi.slnx -c Release` 0/0.
- **Estado:** ✅ Hecho.

## T3 — Pruebas de flujo, contrato y rendimiento
- **Cambiar:** `IntegrationTest/Listings/ListingFlowTests.cs` → `Filter_ByPriceAndCondition_CombinesWithSearch`, `Filter_WithOnlyOnePriceExtremum_IsAccepted`, `Filter_WithAnInvalidRange_IsRejected`, `Filter_WithAnUnknownCondition_IsRejected`.
- **Cambiar:** `ContractTest/ListingsContractTests.cs` → consulta con `minPriceCents`, `maxPriceCents` y `condition` contra los fixtures.
- **Cambiar:** `PerformanceTest/LatencyTests.cs` → la ruta filtrada en `PublicRoutes()`.
- **Verificar:** Integration, Contract y Performance en verde.
- **Estado:** ✅ Hecho (Integration 104, Contract 8, Performance 5).

## T4 — Documentación
- **Actualizar:** fila `GET /api/v1/products` de `docs/endpoints.md` (parámetros nuevos y `400 listings.invalid_price_range`).
- **Actualizar:** `docs/errores.md` con `listings.invalid_price_range` y la fila de `listings.invalid_condition` (HU-06 / HU-10).
- **Actualizar:** `docs/trazabilidad.md` (estado y pruebas de HU-10) y `Memoria.md`.
- **Sin cambios:** `docs/modelo-datos.md` (no hay campo ni índice nuevo) y `db/README.md` (no hay script nuevo).
- **Verificar:** `pwsh scripts/check_endpoints.ps1` → `OK`; `pwsh scripts/critic-guardrails.ps1` → `PASS`.
- **Estado:** ✅ Hecho (endpoints OK, critic PASS).

## T5 — Cierre
- **Verificar:** build 0/0; Unit, Integration, Security, Contract, Database y Performance en verde; Semgrep limpio.
- **Revisar:** `security-reviewer` (OWASP API Top 10) y `traceability-clerk` (spec ↔ código ↔ test ↔ Jira).
- **Estado:** ⏳ Por hacer.

## Pendientes
- `NOTE (HU-10)`: el rango de precio no usa índice propio; si `products` crece, evaluar `(communityId, categorySlug, status, priceCents)` a costa del orden por `_id`.
- `NOTE (HU-10)`: orden por precio ("Menor precio") fuera de esta HU; la maqueta ordena por relevancia.
- Estado vacío y guardado de filtros: HU-12 (app); sin `NOTE` aquí porque no hay código de esta HU que difiera.
- Spec de la app: `musubi-app/specs/m04-marketplace/HU-10-filtrar-categoria-precio/`; se detalla en su repo.

# HU-12 — Guardar una búsqueda sin resultados (tareas)

## T1 — Spec
- **Estado:** ✅ Hecho (14-Nov-2026).

## T2 — Datos
- **Prueba:** `TestShared/Listings/SavedSearchRepositoryContract.cs` (`UnitTest/Data/InMemorySavedSearchRepositoryTests.cs`, `DatabaseTest/MongoSavedSearchRepositoryTests.cs`).
- **Estado:** ✅ Hecho.

## T3 — Rutas
- **Prueba:** `UnitTest/Listings/SavedSearchServiceTests.cs`, `SecurityTest/Auth/SavedSearchesAuthTests.cs`, `ContractTest/WantedContractTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Aviso
- **Prueba:** `UnitTest/Matching/ProductMatchTests.cs`, `IntegrationTest/Matching/WantedMatchFlowTests.cs`.
- **Estado:** ✅ Hecho.

## T5 — Pruebas y documentos
- **Estado:** ✅ Hecho: Unit 723 · Integration 187 · Security 240 · Contract 25 · `check_endpoints` OK (95) · critic PASS.

## T6 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.32` desplegada, buscar algo que no existe, tocar "Avísame cuando aparezca" y publicarlo desde otra cuenta.
- **Estado:** ⏳ Por hacer.

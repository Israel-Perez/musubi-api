# HU-58 — Ver un precio sugerido y saber si un precio es bueno (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (23-Nov-2026). Decisiones para revisar: 5 precios por categoría y 20 por campus; lo pagado pesa 3.

## T2 — Datos
- **Qué:** `FindPricedAsync`, `FindCompletedByCommunityAsync`, `IPriceCatalog`, `ISoldPrices` y `db/033-precios.js`.
- **Prueba:** `TestShared/Listings/ListingRepositoryContract.cs`, `TestShared/Orders/OrderRepositoryContract.cs` y `DatabaseTest/SchemaScriptTests.cs`.
- **Estado:** ✅ Hecho.

## T3 — Modelo, servicio y endpoints
- **Qué:** `PriceModel`, `PriceModelStore`, `PriceSuggestionService` y los dos endpoints.
- **Prueba:** `UnitTest/Recommendations/PriceModelTests.cs` (MAE $87.50 contra $717.90), `IntegrationTest/Recommendations/PriceSuggestionFlowTests.cs`, `SecurityTest/Auth/PricingAuthTests.cs` y `ContractTest/PricingContractTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Documentación
- **Estado:** ✅ Hecho: endpoints (128), errores, modelo de datos, `db/README.md`, trazabilidad, versionado (alpha.44), README, `AGENTS.md` y `Memoria.md`.

## T5 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.44`: "Precio sugerido" en `Vender-3` y la estimación en `Producto`.
- **Estado:** ⏳ Por hacer (siguiente lote de Google Play).

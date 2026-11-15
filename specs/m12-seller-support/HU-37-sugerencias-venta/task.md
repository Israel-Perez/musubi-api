# HU-37 — Recibir sugerencias para vender (tareas)

## T1 — Spec
- **Estado:** ✅ Hecho (15-Nov-2026).

## T2 — Ideas
- **Qué:** `SalesTips`.
- **Prueba:** `UnitTest/SellerSupport/SalesTipsTests.cs`.
- **Estado:** ✅ Hecho.

## T3 — Volver a buscar al editar
- **Qué:** `MyListingsService.UpdateAsync`.
- **Prueba:** `UnitTest/Listings/MyListingsServiceTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Pruebas y documentos
- **Estado:** ✅ Hecho: Build Release 0/0 · Unit 773, Integration 189, Security 246 y Contract 26 · `check_endpoints` OK (98) · critic PASS.

## T6 — Ideas con IA (R6, criterio del 13-Nov-2026)
- **Qué:** `Modules/GenAi/ListingCopy.cs` (`IListingCopywriter`, `ListingCopyText`, `ListingCopyJson`, `GeminiListingCopywriter`, `InMemoryListingCopywriter`, `OffListingCopywriter`), `Modules/GenAi/GeminiApi.cs` (la llamada compartida), `SellerSupportService.IdeasAsync` y `POST /products/{id}/ideas`.
- **Prueba:** `UnitTest/GenAi/ListingCopyTests.cs`, `UnitTest/SellerSupport/SellerSupportServiceTests.cs` (`Ideas_*`), `IntegrationTest/SellerSupport/SellerSupportFlowTests.cs`, `SecurityTest/Auth/SellerSupportAuthTests.cs` y `ContractTest/SellerSupportContractTests.cs`.
- **Estado:** ✅ Hecho (15-Nov-2026). Falta medir con Gemini real (latencia y calidad del texto) cuando esté desplegada.

## T5 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.33` desplegada, una publicación de más de 3 semanas y una solicitud compatible desde otra cuenta.
- **Estado:** ⏳ Por hacer.

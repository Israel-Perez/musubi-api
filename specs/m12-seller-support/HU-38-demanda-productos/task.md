# HU-38 — Saber si hay demanda para mis productos (tareas)

## T1 — Spec
- **Estado:** ✅ Hecho (15-Nov-2026).

## T2 — Demanda
- **Qué:** `IProductDemand` y `DemandRules` (M-06).
- **Prueba:** `UnitTest/SellerSupport/SellerSupportServiceTests.cs`, `UnitTest/Matching/ProductMatchTests.cs`.
- **Estado:** ✅ Hecho.

## T3 — Ruta
- **Qué:** `GET /products/{id}/demand`.
- **Prueba:** `IntegrationTest/SellerSupport/SellerSupportFlowTests.cs`, `SecurityTest/Auth/SellerSupportAuthTests.cs`, `ContractTest/SellerSupportContractTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Pruebas y documentos
- **Estado:** ✅ Hecho: Build Release 0/0 · Unit 748, Integration 188, Security 244 y Contract 26 · `check_endpoints` OK (97) · critic PASS.

## T5 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.33` desplegada, una publicación de más de 3 semanas y una solicitud compatible desde otra cuenta.
- **Estado:** ⏳ Por hacer.

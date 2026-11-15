# HU-36 — Ver mis productos con baja rotación (tareas)

## T1 — Spec
- **Estado:** ✅ Hecho (15-Nov-2026).

## T2 — Datos
- **Qué:** disponibles por fecha, vistas desde un día y conversaciones por publicación.
- **Prueba:** `TestShared/Listings/ListingRepositoryContract.cs`, `TestShared/Chat/ChatRepositoryContract.cs`, `DatabaseTest/MongoListingRepositoryTests.cs`.
- **Estado:** ✅ Hecho.

## T3 — Ruta
- **Qué:** `GET /products/mine/slow`.
- **Prueba:** `UnitTest/SellerSupport/SellerSupportServiceTests.cs`, `SecurityTest/Auth/SellerSupportAuthTests.cs`, `ContractTest/SellerSupportContractTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Aviso de las 3 semanas
- **Qué:** `SlowListingJob` y `db/026`.
- **Prueba:** `SellerSupportServiceTests` → `TheNotice_…`, `DatabaseTest/SchemaScriptTests.cs`.
- **Estado:** ✅ Hecho.

## T5 — Pruebas y documentos
- **Estado:** ✅ Hecho: Build Release 0/0 · Unit 748, Integration 188, Security 244 y Contract 26 · `check_endpoints` OK (97) · critic PASS.

## T6 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.33` desplegada, una publicación de más de 3 semanas y una solicitud compatible desde otra cuenta.
- **Estado:** ⏳ Por hacer.

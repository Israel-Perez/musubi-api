# HU-18 — Ver coincidencias al publicar una solicitud (tareas)

## T1 — Spec
- **Estado:** ✅ Hecho (14-Nov-2026).

## T2 — Registro (RF-25)
- **Prueba:** `TestShared/Matching/MatchRepositoryContract.cs` (`UnitTest/Data/InMemoryMatchRepositoryTests.cs`, `DatabaseTest/MongoMatchRepositoryTests.cs`).
- **Estado:** ✅ Hecho.

## T3 — Coincidencias
- **Prueba:** `UnitTest/Matching/WantedMatchesTests.cs`, `IntegrationTest/Matching/WantedMatchFlowTests.cs`, `SecurityTest/Auth/ManageWantedAuthTests.cs`, `ContractTest/WantedContractTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Terminó en pedido
- **Prueba:** `UnitTest/Orders/OrderServiceTests.cs` → `Create_FromAListingThatMatchedMyRequest_MarksTheMatchAsOrdered`.
- **Estado:** ✅ Hecho.

## T5 — Pruebas y documentos
- **Estado:** ✅ Hecho: Unit 697 · Integration 186 · Security 234 · Contract 24 · `check_endpoints` OK (92) · critic PASS.

## T6 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.31` desplegada, publicar una solicitud y ver "Ya coinciden con lo que buscas".
- **Estado:** ⏳ Por hacer.

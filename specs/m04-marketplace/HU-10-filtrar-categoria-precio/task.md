# HU-10 — Filtrar por categoría y precio (tareas)

## T1 — Spec detallada
- **Qué:** Completar `spec.md` y `plan.md` con el alcance acordado (multi-categoría, precio, condición, orden, cursor e índices).
- **Archivos:** `specs/m04-marketplace/HU-10-filtrar-categoria-precio/spec.md`, `plan.md`, `task.md`.
- **Verificación:** encabezados de `specs/_template.md` completos; contratos enlazados a `docs/endpoints.md` y `docs/errores.md`.
- **Estado:** ✅ Hecho.

## T2 — Datos e índices
- **Qué:** Crear `db/008-filtros-precio.js` con los cuatro índices de precio (asc/desc, con y sin categoría) y subir `MongoSchema.ExpectedVersion` a 8.
- **Archivos:** `db/008-filtros-precio.js`, `db/README.md`, `Musubi.Api/Platform/Data/MongoSchema.cs`, `docs/modelo-datos.md`, `DatabaseTest/SchemaScriptTests.cs`.
- **Verificación:** `DatabaseTest` en CI (sin Docker local); `node --check db/008-filtros-precio.js`; `_meta.version = 8`.
- **Estado:** ✅ Hecho.

## T3 — Consulta y cursor
- **Qué:** Extender `ProductQuery`, agregar `ProductCursor`, `ProductCursorCodec` y `ProductSorts`; implementar filtros, orden y cursor en Mongo e InMemory.
- **Archivos:** `Musubi.Api/Modules/Listings/Data/ListingRepositories.cs`, `MongoListingRepositories.cs`, `InMemoryListingRepositories.cs`, `TestShared/Listings/ListingRepositoryContract.cs`.
- **Verificación:** `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` con los casos nuevos del contrato compartido.
- **Estado:** ✅ Hecho.

## T4 — Servicio, errores y controller
- **Qué:** Normalizar y validar filtros, resolver el cursor, devolver `nextCursor`; agregar `InvalidPriceRange` e `InvalidSort`; exponer los parámetros en `Showcase`.
- **Archivos:** `ListingService.cs`, `ListingsErrors.cs`, `ProductsController.cs`.
- **Verificación:** `IntegrationTest` cubre combinación, orden, paginación y errores 400.
- **Estado:** ✅ Hecho.

## T5 — Pruebas HTTP y contrato
- **Qué:** Agregar flujos de integración, seguridad y contrato; confirmar que la forma JSON no cambia.
- **Archivos:** `IntegrationTest/Listings/ListingFlowTests.cs`, `SecurityTest/Auth/ListingsAuthTests.cs`, `ContractTest/ListingsContractTests.cs`.
- **Verificación:** `dotnet test --project IntegrationTest/...`, `SecurityTest/...` y `ContractTest/...` con `--no-build`.
- **Estado:** ✅ Hecho.

## T6 — Documentación y memoria
- **Qué:** Actualizar `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `db/README.md`, `docs/trazabilidad.md` y `Memoria.md`.
- **Verificación:** `pwsh scripts/check_endpoints.ps1` → `OK`; `docs/trazabilidad.md` con la HU al día.
- **Estado:** ✅ Hecho.

## T7 — Verificación y cierre
- **Qué:** Build 0/0, suites en verde, `critic`, `endpoints`, Semgrep; conciliar conteos reales en `spec.md`; `security-reviewer` y `traceability-clerk`.
- **Verificación:** comandos de `spec.md` → Criterios; sin fallos. Evidencia: Unit 186 · Integration 103 · Security 85 · Contract 8 · Performance 4; `critic` PASS (31 archivos) y `endpoints` OK. `security-reviewer` PASS con avisos; `traceability-clerk` reportó 3 gaps medios (NOTE en código/task, cursor `relevance`, índice `priceDesc`), corregidos en este PR. Semgrep no está instalado en el entorno local; queda para el CI. `DatabaseTest` queda para el CI (sin Docker local).
- **Estado:** ✅ Hecho con Semgrep y `DatabaseTest` pendientes del CI.

## Pendientes
- `NOTE (HU-10)` — relevancia real: `relevance` hoy ordena como `newest`; una HU posterior define puntaje y cursor.
- `NOTE (HU-10)` — orden por precio con `q`: el índice de prefijo resuelve la búsqueda, pero el orden puede resolverse en memoria sobre las coincidencias; medir con `02-02` si el volumen crece.
- `NOTE (HU-10)` — tope de categorías: hoy lo acota la línea de petición; si se vuelve abuso, agregar tope y error propio.
- `NOTE (HU-10)` — plan con `$in` de estado: confirmar con `explain` en `DatabaseTest`/CI que `priceAsc`/`priceDesc` usan el índice (posible `SORT_MERGE`).
- `DatabaseTest` — índices de `db/008` y contrato compartido contra MongoDB real; lo corre el job `database` del CI.
- Semgrep — `semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error` antes del primer push; no disponible en el entorno local.

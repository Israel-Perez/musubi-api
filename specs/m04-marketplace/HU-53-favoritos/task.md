# HU-53 — Guardar productos en favoritos (tareas)

## T1 — Spec
- **Qué:** Detallar `spec.md` y `plan.md`; resolver la decisión de offline, corregir el módulo y fijar el contrato del corazón.
- **Verificar:** encabezado con Sprint 4, módulo `M-04`/`Modules/Listings/`, dependencias HU-57 y HU-46, y siete secciones de contenido + Aprobación.
- **Estado:** ✅ Hecho (09-Nov-2026).

## T2 — Datos
- **Crear:** `db/013-favoritos.js`, `FavoriteDocument` e `IFavoriteRepository`; `IProductRepository.FindByIdsAsync`.
- **Cambiar:** `MongoSchema.ExpectedVersion = 13` y `db/README.md`.
- **Verificar:** `TestShared/Listings/FavoriteRepositoryContract.cs` en `UnitTest/Data/InMemoryFavoriteRepositoryTests.cs` y `DatabaseTest/MongoFavoriteRepositoryTests.cs`; `SchemaScriptTests`.
- **Estado:** ✅ Hecho (09-Nov-2026); `DatabaseTest` pendiente de Docker/CI.

## T3 — Servicio y endpoints
- **Crear:** `FavoriteService`, `FavoritesController` y `FavoritesDtos`.
- **Cambiar:** `ListingService.ToResponse` a `internal static`; registro en `ListingsModuleServiceCollectionExtensions`; `ListingAccountEraser` (HU-46).
- **Reglas:** mismo campus, `active`/`soldOut`, bloqueo HU-57, idempotencia, tres estados y tope 200. Sin `isFavorite`: no cambian `ProductsDtos`, `ProductsController` ni `Fixtures/product*.json` (enfoque B).
- **Verificar:** `FavoriteServiceTests`, `FavoriteFlowTests`, `ListingsAuthTests`, `ListingsContractTests` y `ListingAccountEraserTests`.
- **Estado:** ✅ Hecho (09-Nov-2026).

## T4 — Contrato y documentación
- **Agregar:** filas `GET/PUT/DELETE /api/v1/favorites` en la sección `## M-03 · Publicaciones` de `docs/endpoints.md` (`Spec` HU-53, `Consumidor` musubi-app HU-53); nota de HU-53 en `docs/errores.md`; `favorites.json` en `ContractTest/Fixtures/`.
- **Actualizar:** la fila `favorites` de `docs/modelo-datos.md` de 📐 a ✅ (no duplicarla); `docs/trazabilidad.md` (Spec, Contraparte y Tests), `db/README.md` y `Memoria.md`.
- **Verificar:** `pwsh scripts/check_endpoints.ps1` → OK y `ContractTest` con la forma exacta.
- **Estado:** ✅ Hecho (09-Nov-2026); endpoints OK con 53 rutas.

## T5 — Cierre
- **Verificar:**
  - `dotnet build Musubi.slnx -c Release` → 0/0;
  - Unit, Integration, Security, Contract y Database al 100 % con `--no-build`;
  - `pwsh scripts/critic-guardrails.ps1` → PASS;
  - `security-reviewer` PASS;
  - `traceability-clerk` sin drift.
- **Estado:** 🚧 En progreso (09-Nov-2026): build 0/0, Unit 366, Integration 132, Security 111, Contract 13, critic PASS y endpoints OK (53). `DatabaseTest` pendiente por Docker/CI; PR pendiente.

## Pendientes
- **App:** `musubi-app:HU-53` (`Favoritos`, corazones en el detalle y las tarjetas, estados de carga y "Ya no disponible"). La app carga `GET /favorites` para armar el set del corazón.
- **`NOTE (HU-53)`:** la lista devuelve hasta 200; paginar si una persona supera ese tope.
- **Límite:** no hay "Bajó $" ni aviso de cambio de precio; requiere historial de precios y notificaciones.
- **Límite:** los favoritos de otras personas que apuntaban a productos eliminados se conservan como `unavailable`; no hay limpieza automática (no se agrega `NOTE` porque no hay código que lo difiera).
- **Contraparte:** actualizar `musubi-app:HU-53` (Sprint 4 y decisión de no offline) en su PR; hoy su spec sigue "por asignar" y con la decisión pendiente.
- **DatabaseTest:** correr `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release --no-build` con Docker o dejarlo al CI; incluye el script 013 y el contrato de favoritos.
- **Prueba manual:** guardar, listar, repetir `PUT`, quitar y repetir `DELETE` en Scalar/Development.

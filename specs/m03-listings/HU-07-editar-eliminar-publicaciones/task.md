# HU-07 — Editar o eliminar mis publicaciones (tareas)

## T1 — Detallar la spec
- **Qué:** conciliar Jira (SCRUM-24), la maqueta (`Mis-productos`, `Vender-2…4`) y HU-06; definir rutas, errores y datos; completar `spec.md` y `plan.md`.
- **Verificar:** el usuario aprueba las decisiones D1–D6 de `plan.md`.
- **Estado:** ✅ Hecho (05-Nov-2026): decisiones D1–D6 aprobadas por el usuario.

## T2 — Datos
- **Cambiar:** `IProductRepository` (`UpdateDetailsAsync`, `SetPausedAsync`, `DeleteAsync`) en `Mongo*` e `InMemory*`.
- **Verificar:** casos nuevos de `TestShared/Listings/ListingRepositoryContract.cs` en `UnitTest` (en memoria) y `DatabaseTest` (MongoDB real, en el CI).
- **Sin script en `db/`:** el `$jsonSchema` de `products` ya acepta `hidden`.
- **Estado:** ✅ Hecho (05-Nov-2026): 4 casos nuevos en la batería; en memoria en verde, en MongoDB real en el job `database` del CI.

## T3 — Servicio y endpoints
- **Crear:** `ProductRules`, `MyListingsService`, `MyProductsController`, `UpdateProductRequest` y `ListingsErrors.ProductNotFound`.
- **Verificar:** `UnitTest/Listings/MyListingsServiceTests.cs` (10), `IntegrationTest/Listings/MyListingsFlowTests.cs` (5), `SecurityTest/Auth/MyListingsAuthTests.cs` (16) y `ContractTest/ListingsContractTests.cs` (amplía la prueba existente).
- **Estado:** ✅ Hecho (05-Nov-2026). Mutaciones comprobadas: sin el dueño en el filtro → 4 Security y 5 Unit en rojo; otra etiqueta de caché → 3 Integration en rojo.

## T4 — Documentación y versión
- **Actualizar:** `docs/endpoints.md` (4 filas y el total), `docs/errores.md`, `docs/modelo-datos.md` (uso de `products`), `docs/versionado.md` + `<Version>`, la fila de HU-07 en `docs/trazabilidad.md` y `Memoria.md`.
- **Estado:** ✅ Hecho (05-Nov-2026): `0.5.0-alpha.2`.

## T5 — Revisión y PR
- **Qué:** build, suites, critic, `check_endpoints`, `check_shared_sections`, Semgrep, `security-reviewer`, `traceability-clerk`; PR con la plantilla; Jira a "En revisión".
- **Estado:** ⏳ Por hacer.

## Pendientes
- `NOTE (HU-18)`: volver a buscar coincidencias cuando cambian el nombre, la categoría, el precio o la descripción (RF-08).
- `NOTE (HU-23)`: no eliminar con pedidos pendientes o confirmados (RN-07) → `409 listings.product_has_open_orders`.
- `NOTE (02-05)`: fotos huérfanas si S3 falla a medias al eliminar (regla de ciclo de vida del bucket).

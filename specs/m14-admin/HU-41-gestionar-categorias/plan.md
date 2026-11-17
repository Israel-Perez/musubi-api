# plan.md — HU-41 Gestionar categorías

## Componentes
- `Musubi.Api/Modules/Listings/`:
  - `CategoryCatalog.cs` (`ICategoryCatalog`, `ManagedCategory`, `CategoryChange`).
  - `Data/*`: `ICategoryRepository.ListAllAsync`, `TryInsertAsync` y `UpdateAsync` en Mongo y en memoria, y `CachedCategoryRepository`.
  - `ListingService.DetailAsync`: el nombre de una categoría desactivada.
  - `MyListingsService.UpdateAsync`: conservar la categoría desactivada al editar.
- `Musubi.Api/Modules/Admin/`:
  - `AdminCategoriesController.cs`, `CategoryAdminService.cs` y `AdminGate.cs` (sale de `ModerationService`).
  - DTOs, errores y acciones de la bitácora.
- `Platform/Caching`: etiqueta `catalog` en la política de las categorías.
- `db/031-categorias.js`; API `0.5.0-alpha.39` y `MongoSchema.ExpectedVersion = 31`.

## Decisiones
- **Desactivar en vez de borrar.** Un producto o una solicitud con una categoría borrada se quedaría sin nombre y sin filtro.
- **El slug no cambia al renombrar.** Lo guardan productos, solicitudes, intereses y búsquedas guardadas; cambiarlo obligaría a migrar cuatro colecciones.
- **Repetidas por nombre sin acentos ni mayúsculas, también contra las desactivadas.** "Electronica" y "Electrónica" serían la misma en los filtros. Si ya existe desactivada, se reactiva en vez de crear otra.
- **Al menos una activa.** Sin ninguna, "Vender-3" no deja publicar.
- **Hasta 30 por campus.** "Vender-3", "Filtrar" e "Intereses" las muestran todas como chips, sin paginar; la maqueta tiene 6.
- **La caché guarda todas y filtra las activas.** Así el detalle de un producto viejo sigue mostrando el nombre de su categoría desactivada. La llave no cambia (`categories:{campus}`): antes de HU-41 todas estaban activas, así que lo guardado tiene la misma forma.
- **Las reglas viven en M-03; la autorización y la bitácora, en M-14.** Admin no lee `categories` (Modules/README).
- **`AdminGate`** junta la revisión del administrador (activo, con el rol en la base) que ya hacía `ModerationService`.
- **Sin cambio de orden.** No lo pide Jira y necesitaría una pantalla para arrastrar. Una nueva va al final.

## Guardarraíles
- Solo `[Authorize(Roles = "admin")]`, y `AdminGate` vuelve a leer la cuenta (RNF-03).
- Otro campus es 404 (`admin.category_not_found`).
- El nombre se recorta y se valida (2 a 40, con al menos 2 letras o números); el slug cumple `^[a-z0-9-]{2,40}$` del validador de `db/002`.
- Si dos crean el mismo slug a la vez, `ux_community_slug` hace que la segunda responda `admin.category_exists`.

## Validación
Las pruebas de `spec.md` → Tests. `DatabaseTest` en un MongoDB 8.0 local con una carpeta de datos nueva (sin Docker; en el CI, con Testcontainers).

## Secuencia
1. Repositorios y batería de `TestShared/`.
2. `CategoryCatalog` y sus pruebas.
3. `CategoryAdminService`, controller y `AdminGate`.
4. Detalle y edición con una categoría desactivada.
5. `db/031`, versión, documentos y PR (apilado sobre el de HU-39/40).

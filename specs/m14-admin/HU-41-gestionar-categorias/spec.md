# HU-41 — Gestionar categorías

**HU / Jira / Sprint / Módulo / Contraparte:** HU-41 / SCRUM-64 / Sprint 5 (23 nov – 4 dic 2026) / M-14 / [musubi-app:HU-41](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m14-admin/HU-41-gestionar-categorias)

## Contexto
Jira (SCRUM-64): Como **administrador**, quiero **gestionar las categorías**, para **mantener organizada la plataforma**.

- Maqueta: sin pantalla propia. Se diseña con la guía de estilo de la maqueta; el lineamiento 4 permite agregar, nunca quitar.
- Trazabilidad: RF-56 · CU-26 · RNF-03 · RNF-04. Origen: Propuesta inicial §11.
- La nota de Jira ("desde Sprint 2 se requiere un catálogo inicial") ya está cubierta: `db/002-publicaciones.js` siembra las 6 de la maqueta para la UTTT.

**Depende de:** `HU-06` (las categorías y su caché, 02-02), `HU-39` (módulo Admin, rol `admin`, `moderationLog`).

## Requisitos
1. El administrador agrega una categoría con un nombre de 2 a 40 caracteres. Queda activa y al final.
2. Puede renombrarla, desactivarla y volver a activarla. El slug sale del nombre al crearla y no cambia: lo guardan los productos, las solicitudes, los intereses y las búsquedas guardadas.
3. No puede haber dos con el mismo nombre (sin acentos ni mayúsculas), aunque una esté desactivada. Debe quedar al menos una activa y hay hasta 30 por campus.
4. Solo las activas salen al publicar productos (`Vender-3`) y solicitudes, en los filtros y en "Mis intereses", también para el visitante (la caché de salida se borra al cambiar una).
5. Una categoría desactivada no borra nada: sus publicaciones siguen visibles con su nombre y su dueño las puede editar sin cambiarla, pero nadie la puede elegir de nuevo.
6. Todo es del campus del administrador y cada cambio queda en la bitácora con quién y cuándo (RNF-04).

## Diseño
- M-03 (dueño de `categories`): contrato `ICategoryCatalog` / `CategoryCatalog` con las reglas, y `ICategoryRepository` con `ListAllAsync`, `TryInsertAsync` y `UpdateAsync` (`Mongo*` e `InMemory*` con la batería de `TestShared/`).
- `CachedCategoryRepository` guarda todas (también las desactivadas, para el nombre de un producto viejo) y borra su entrada al escribir; `ListActiveAsync` filtra las activas.
- M-14: `AdminCategoriesController` y `CategoryAdminService`, con `AdminGate` (la misma revisión de HU-39/40) y `moderationLog` (`targetType: category`).
- Sin cambios en `categories`. `db/031-categorias.js` solo agrega las acciones de categorías al validador de `moderationLog`.

## Contratos
- [`GET`, `POST /api/v1/admin/categories` y `PATCH /api/v1/admin/categories/{slug}`](../../../docs/endpoints.md) (M-14).
- Errores `admin.invalid_category_name`, `admin.no_category_changes`, `admin.category_not_found`, `admin.category_exists`, `admin.last_category` y `admin.category_limit` en [`docs/errores.md`](../../../docs/errores.md).
- Fixtures `admin-category.json` y `admin-category-page.json`.

## Tests
- `UnitTest/Listings/CategoryCatalogTests.cs`: slug, nombre, repetidas (también desactivadas), máximo, última activa, sin cambios y cachés.
- `UnitTest/Admin/CategoryAdminServiceTests.cs`: bitácora de cada acción, otro campus, sin el rol en la base o suspendido.
- `UnitTest/Platform/CachingTests.cs`: cambiar una categoría borra la entrada de la caché.
- `TestShared/Listings/ListingRepositoryContract.cs` y `TestShared/Admin/ModerationLogRepositoryContract.cs`: escribir categorías y guardar sus acciones (en `DatabaseTest`, con el validador de `db/031`).
- `IntegrationTest/Admin/CategoryAdminFlowTests.cs`: una nueva sale de inmediato al publicar, en las solicitudes y en la caché del visitante; una desactivada sale de ahí pero su publicación conserva el nombre y se puede editar; errores.
- `SecurityTest/Auth/AdminAuthTests.cs`: las tres rutas con 401 y 403.
- `ContractTest/AdminContractTests.cs`: forma de la lista y de una categoría.

## Criterios
- [x] El administrador puede crear, editar y desactivar categorías: `CategoryAdminFlowTests.NewCategory_ShowsUpForPublishingAndFilters_RightAway` y `Deactivated_LeavesPublishingAndFilters_ButItsProductsKeepTheirName`.
- [x] Las categorías activas son las que aparecen al publicar productos y solicitudes, y en los filtros: los mismos, más `DeactivatedCategory_CannotBeChosen_ForAnotherProduct`.
- [x] Build Release 0/0 · Unit 915, Integration 207, Security 305 y Contract 31 · `DatabaseTest` 194 · `check_endpoints` OK (125) · critic PASS.

## Límites
- No se borran categorías: se desactivan, para no dejar productos sin categoría.
- No se cambia el orden: una nueva va al final.
- Las solicitudes "Se busca" guardan una copia del nombre (R8): al renombrar, las que ya existen conservan el anterior.
- La caché de salida es por instancia: la otra instancia le muestra el cambio al visitante a más tardar en 10 minutos (`CatalogTtl`); con sesión, de inmediato.
- Las carreras (`NOTE (HU-41)` en M-02) quedan fuera: Jira solo pide categorías.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 revisor distinto del autor)
- **Fecha:** 16-Nov-2026
- **Detalle:** la spec ligera del 29-Oct-2026 se detalló al implementar. API `0.5.0-alpha.39`, esquema 31.

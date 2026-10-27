# HU-06 — Publicar un producto (tareas)

## T1 — Imágenes
- **Crear:** `Platform/Media/{Media.cs, PhotoProcessor.cs, MediaController.cs, MediaServiceCollectionExtensions.cs}`.
- **Agregar:** `Media:*` en appsettings, `user-data` y `compute.tf`.
- **Verificar:** `PhotoProcessorTests` (5) y `ListingsAuthTests.InTheCloud_TheApiDoesNotServeMedia`.
- **Estado:** ✅ Hecho (27-Oct-2026).

## T2 — Datos
- **Crear:** `db/002-publicaciones.js` y `Modules/Listings/Data/*`.
- **Cambiar:** `MongoSchema.ExpectedVersion = 2`; `ICommunityRepository.FindBySlugAsync`.
- **Verificar:** `ListingRepositoryContract` (en memoria y Mongo) y `SchemaScriptTests`.
- **Estado:** ✅ Hecho (27-Oct-2026); `DatabaseTest` corre en el CI.

## T3 — Publicar, vitrina y "mis productos"
- **Crear:** `ListingService`, `PhotoUploadService`, `ProductsController`, `CategoriesController`, `ListingsErrors` e `IMemberDirectory`.
- **Verificar:** `ListingFlowTests` (7), `ListingsAuthTests` (7) y `ListingsContractTests` (1).
- **Estado:** ✅ Hecho (27-Oct-2026).

## T4 — Documentación y despliegue
- **Actualizar:** `docs/{endpoints,errores,modelo-datos,versionado}.md`, `01-04`, la constitución §6 (excepción) y `Memoria.md`.
- **Desplegar:** `api-v0.3.0-alpha.1`, `infra-v0.2.0` y el script `002` en Atlas. Requiere la autorización del usuario.
- **Estado:** 🚧 Documentación hecha; despliegue pendiente de autorización.

## Pendientes
- `NOTE (HU-18)`: coincidencias con solicitudes al publicar (RF-23).
- `NOTE (HU-08)`: paginar "Mis productos".
- `NOTE (02-07)`: moderación de imágenes y texto.
- `NOTE (02-05)`: regla de ciclo de vida del bucket para fotos que nunca se publicaron.

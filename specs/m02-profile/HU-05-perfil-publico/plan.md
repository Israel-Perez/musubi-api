# plan.md — HU-05 Consultar perfil y reputación de otro usuario

## 1. Componentes
- **M-02 (Perfil), sin cambios de contrato:**
  - `Modules/Profiles/ProfileService.cs`, `ProfilesController.cs` y `ProfilesDtos.cs` ya exponen `GET /profiles/{id}` con la visibilidad, el campus, los bloqueos y sin correo (HU-04); solo se actualizan comentarios `NOTE (HU-27)` en `ProfilesController`, `ProfilesDtos` y `ProfileDirectory`.
- **M-03 (Publicaciones), cambios mínimos:**
  - `Modules/Listings/Data/ListingRepositories.cs`: `ProductQuery.SellerId` opcional.
  - `Modules/Listings/Data/MongoListingRepositories.cs`: filtro por vendedor en `Showcase`.
  - `Modules/Listings/Data/InMemoryListingRepositories.cs`: el mismo filtro en la versión en memoria.
  - `Modules/Listings/ListingService.cs`: `ShowcaseAsync` recibe `sellerId` y lo pasa a la consulta.
  - `Modules/Listings/ProductsController.cs`: `[FromQuery] string? sellerId` en `GET /products`.
- **Specs y documentación:**
  - `specs/m02-profile/HU-05-perfil-publico/{spec,plan,task}.md`;
  - `docs/endpoints.md` (dos filas modificadas);
  - `docs/trazabilidad.md`, `docs/versionado.md`, `Memoria.md`.
- **Sin cambios:** `db/NNN-*.js`, `MongoSchema.ExpectedVersion`, `docs/modelo-datos.md`, `ContractTest/Fixtures/`, `ProfilesContractTests` y `ListingsContractTests` (la forma JSON no cambia).

## 2. Decisiones
- **D1 · Reutilizar `GET /products` con `sellerId`, no una ruta nueva:** la vitrina ya trae paginación, cursor, `page`, caché, campus y bloqueos; la app ya parsea `ProductPage`. Una ruta `GET /profiles/{id}/products` o una lista embebida en el perfil duplicaría DTOs, mapeo y pruebas. La composición en la app es `GET /profiles/{id}` + `GET /products?sellerId={id}&hideSoldOut=true`.
- **D2 · “Publicaciones activas” = `status = active`:** el perfil no muestra pausadas (`hidden`), vendidas (`sold`), agotadas (`soldOut`) ni quitadas (`removed`). La app pide `hideSoldOut=true`; la vitrina ya traduce eso a `[active]`.
- **D3 · El contador es `total` de la misma consulta:** la pestaña “Publicaciones (N)” usa `total` y las tarjetas usan `items[]`; no se agrega `activeListings` al perfil ni se guarda un contador en `profiles`.
- **D4 · “Mostrar mis ventas” no oculta las publicaciones:** el interruptor de HU-04 oculta el número de ventas (`stats.sales`), no las publicaciones, que ya son públicas en la vitrina. El perfil sigue mostrando la pestaña.
- **D5 · `sellerId` vacío o ausente no filtra; mal formado devuelve página vacía:** no se agrega un error nuevo; la app solo lo usa después de un perfil `200`. Un `sellerId` de otro campus no devuelve nada porque la vitrina filtra por la comunidad del token.
- **D6 · La reputación no se calcula en esta HU:** `ratings` es de HU-27/HU-45; `GET /profiles/{id}` ya responde `ratingAvg: null` y `ratingCount: 0`. La app muestra “Sin calificaciones” (RN-26). `NOTE (HU-27)`.
- **D7 · Sin índice nuevo:** `ix_seller_id` de `products` (HU-06) cubre `sellerId + status`. Si alguna vez se mide lento, se optimiza con un índice compuesto y su medición; no se inventa un umbral.
- **D8 · “Seguir”, “Responde en” y “Mensaje” quedan fuera:** no están en los criterios de Jira; “Mensaje” es HU-20 y los otros no tienen datos. La pestaña “Opiniones” queda vacía hasta HU-27/HU-45.

## 3. Excepciones al stack (00-01)
Ninguna. No se agregan dependencias ni tecnologías.

## 4. Guardarraíles
- **Campus (RNF-21):** `Showcase` sigue filtrando por `ProductQuery.CommunityId`; un `sellerId` de otro campus no devuelve publicaciones.
- **Bloqueos (HU-57):** `Showcase` sigue aplicando `ExcludeSellers` con `IBlockDirectory.HiddenForAsync`; un bloqueo en cualquier dirección deja la página vacía. El perfil ya responde `404` en ese caso.
- **Privacidad (RNF-05, OWASP API3):** el perfil no lleva correo; el filtro no expone datos nuevos: las publicaciones activas ya están en la vitrina.
- **Sin enumeración:** un `sellerId` inexistente devuelve página vacía, no un error distinto; el perfil mantiene el mismo `404` para inexistente, inactivo, otro campus o bloqueo.
- **Límites:** `limit` 1 a 50 (ya validado por la vitrina), cursor validado (`listings.invalid_cursor`) y `page` 1 a 50 (`listings.invalid_page`).
- **Caché:** la política `showcase` varía por query (`*`); `sellerId` entra en la clave y las peticiones con sesión no se guardan. No se agrega caché nueva.
- **Sin PII:** no se copian nombres, correos ni fotos a `profiles`; la tarjeta de la app usa el `ProductResponse` de la vitrina.

## 5. Validación
- **Prueba compartida:** `TestShared/Listings/ListingRepositoryContract.cs` (en memoria y MongoDB real).
- **Integración:** `IntegrationTest/Profiles/ProfileFlowTests.cs` (detalle → perfil → publicaciones activas).
- **Contrato:** `ContractTest/ListingsContractTests.cs` y `ProfilesContractTests.cs` siguen pasando sin fixture nuevo.
- **Seguridad:** `SecurityTest/Auth/ProfilesAuthTests.cs` (401 y 404 entre campus) y `ListingsAuthTests.cs` (vitrina anónima y campus con `sellerId`); el bloqueo con `sellerId` está en `ReputationFlowTests`.
- **Comandos:**
  - `dotnet build Musubi.slnx -c Release`
  - `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build`
  - `dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build`
  - `dotnet test --project SecurityTest/SecurityTest.csproj -c Release --no-build`
  - `dotnet test --project ContractTest/ContractTest.csproj -c Release --no-build`
  - `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release --no-build` (si hay Docker)
  - `pwsh scripts/check_endpoints.ps1`, `pwsh scripts/critic-guardrails.ps1`
  - `UnitTest/Common/VersioningTests.cs` con `0.5.0-alpha.21`.
- **Subagentes:** `security-reviewer` antes del push y `traceability-clerk` al cerrar.

## 6. Secuencia de implementación
1. **T1 — Specs:** escribir `spec.md`, `plan.md` y `task.md`; enlazar las filas de `docs/endpoints.md`.
2. **T2 — Prueba primero:** agregar `SellerFilter_OnlyTheActiveListingsOfThatSeller_AndCountsThem` a `TestShared/Listings/ListingRepositoryContract.cs`; compila en rojo hasta que exista `SellerId`.
3. **T3 — Filtro:** agregar `SellerId` a `ProductQuery` y a los dos `Showcase`; pasar el parámetro por `ListingService.ShowcaseAsync` y `ProductsController.Showcase`.
4. **T4 — Integración:** agregar `SellerProfile_IsReachableFromProductDetail_AndCountsOnlyActiveListings` a `ProfileFlowTests` (publicar, pausar, vender, consultar perfil y publicaciones).
5. **T5 — Documentación y versión:** `docs/endpoints.md` (dos filas), `docs/trazabilidad.md`, `docs/versionado.md` (`0.5.0-alpha.21`), `Musubi.Api.csproj`, `Memoria.md`; `spec.md`/`task.md` con evidencia.
6. **T6 — Verificación y cierre:** build, suites, guardarraíles, `security-reviewer`, `traceability-clerk`, PR (sin push sin autorización).

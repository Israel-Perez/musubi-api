# HU-05 — Consultar perfil y reputación de otro usuario

**HU / Jira / Sprint / Módulo / Contraparte:** HU-05 / SCRUM-34 / Sprint 4 (9–20 nov 2026) / M-02 (consume M-03) / [musubi-app:HU-05](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m02-profile/HU-05-perfil-publico)

## Contexto
Jira (SCRUM-34): Como **usuario**, quiero **consultar el perfil y la reputación de otro usuario**, para **decidir si hago un trato con él**.

- Maqueta: `Producto`, `Perfil-vendedor`, `Chat-Valeria`. `Perfil-vendedor` muestra la foto, el nombre, la carrera y el periodo, la reputación, el número de calificaciones, las ventas, “En Musubi desde” y dos pestañas: **Publicaciones (N)** y **Opiniones (N)**.
- Trazabilidad: RF-05, RF-36 · CU-03 · RN-26.
- Origen: Propuesta inicial §11.
- **Decisión de Jira (08-Nov-2026):** el alcance de este sprint es la pantalla `Perfil-vendedor`: nombre, foto, campus y publicaciones activas. La reputación se muestra como **“Sin calificaciones”** mientras no existan calificaciones; **HU-27 y HU-45 todavía no están**, así que esta HU no calcula promedios. No se expone el correo ni datos personales (RNF-05).
- **Estado de la reputación:** `ratings` es de HU-27/HU-45 y sigue en diseño. `GET /profiles/{id}` ya devuelve `stats { ratingAvg, ratingCount, sales }`; sin calificaciones, `ratingAvg` es `null` y `ratingCount` es `0` (RN-26). La app pinta “Sin calificaciones” con esos valores. `NOTE (HU-27)`.

**Depende de:** `02-01` (modelo de datos), `02-03` (sesión y errores), `HU-04` (perfil público) y `HU-06`/`HU-11` (vitrina y detalle). **Diferido:** `HU-27`/`HU-45` (calificaciones y opiniones).

## Requisitos
1. **Cabecera del perfil público (RF-05, RNF-05):**
   - `GET /api/v1/profiles/{id}` devuelve `id`, `fullName`, `campus`, `photo`, `memberSince`, `career` y `term` (si “Mostrar mi carrera” está encendido) y `stats { ratingAvg, ratingCount, sales }` (`sales` solo si “Mostrar mis ventas” está encendido).
   - No incluye el correo ni otros datos privados.
   - Responde el mismo `404 profiles.not_found` si la cuenta no existe, está inactiva, es de otro campus o hay un bloqueo en cualquier dirección (HU-57); no revela cuál.
   - HU-04 ya implementó esta lectura; HU-05 la verifica y la documenta, sin duplicar el código.
2. **Reputación sin calificaciones (RN-26):**
   - Mientras `ratings` no exista (HU-27/HU-45), `ratingAvg` es `null` y `ratingCount` es `0`.
   - Esta HU no calcula ni inventa un promedio y no agrega la lista de opiniones.
   - La app muestra “Sin calificaciones” cuando `stats.ratingAvg` es `null`.
3. **Publicaciones activas del perfil (RF-05):**
   - `GET /api/v1/products?sellerId={id}&hideSoldOut=true` devuelve solo las publicaciones `active` de esa persona, de la más nueva a la más vieja, con `items[]`, `nextCursor` y `total`.
   - Reutiliza la vitrina: el `total` es el número de “Publicaciones (N)” y `items[]` son las tarjetas de la pestaña.
   - Quedan fuera las publicaciones `soldOut`, `hidden` (pausadas), `removed` y `sold` (vendidas), las de otro campus y las de una persona con la que hay un bloqueo (HU-57).
   - Se mantienen la paginación por cursor, `limit` (1 a 50), `page` y la caché de salida de la vitrina.
4. **Acceso desde el detalle de una publicación (RF-14):**
   - `GET /api/v1/products/{id}` ya devuelve `seller.id` cuando hay sesión; la app abre `GET /api/v1/profiles/{id}`.
   - No se crea una ruta nueva para el perfil del vendedor.
5. **Privacidad, campus y bloqueos (RNF-05, RNF-07, RNF-21, OWASP API1/API3):**
   - El filtro `sellerId` no salta el campus del token ni la exclusión de bloqueados que ya aplica la vitrina.
   - El perfil fuera del campus o con bloqueo sigue siendo `404`, igual que uno inexistente.
   - No se agrega un campo `activeListings` al perfil ni se copian datos de publicaciones en `profiles`: el conteo sale de la misma consulta de la vitrina.
6. **Sin cambios de esquema:**
   - No hay colección, script `db/NNN-*.js`, índice ni `MongoSchema.ExpectedVersion` nuevos.
   - El índice `ix_seller_id` de `products` (HU-06) cubre el filtro por vendedor.

RNF que aplican: RNF-03 (sesión y propiedad; esta HU no escribe), RNF-05 (datos mínimos), RNF-07 (privacidad), RNF-21 (campus) y OWASP API1/API3.

## Diseño
- **M-02 (Perfil):** no cambia el contrato. `ProfileService.PublicAsync` ya aplica la visibilidad, el campus y los bloqueos, y no devuelve el correo; solo se actualizan los comentarios `NOTE (HU-27)` en `ProfilesController`, `ProfilesDtos` y `ProfileDirectory`.
- **M-03 (Publicaciones):**
  - `Data/ListingRepositories.cs`: `ProductQuery` agrega `SellerId` (opcional).
  - `Data/MongoListingRepositories.cs` e `Data/InMemoryListingRepositories.cs`: `Showcase` filtra por `SellerId` cuando viene.
  - `ListingService.ShowcaseAsync`: recibe `sellerId` y lo pasa a `ProductQuery`.
  - `ProductsController.Showcase`: expone `[FromQuery] string? sellerId`.
- **Por qué se reutiliza la vitrina:** una sola ruta y un solo DTO (`ProductPage`); se heredan paginación, cursor, `page`, caché, campus y bloqueos; la app ya sabe leer `ProductPage`. Una ruta nueva o una lista embebida en el perfil duplicaría el mapeo de productos y el contrato.
- **Composición en la app:** `GET /profiles/{id}` para la cabecera y `GET /products?sellerId={id}&hideSoldOut=true` para la pestaña “Publicaciones (N)”. La app usa `total` para el contador y `items[]` para las tarjetas.
- **Datos:** sin cambios en `profiles` ni en `products`. El conteo es una consulta a `products` por `sellerId` + `status = active`, con `ix_seller_id`.
- **Pendientes en el código:** `NOTE (HU-27)` en `ProfileDirectory`/`profiles.stats` (calificaciones y promedio); la lista de opiniones y el cálculo del promedio llegan con HU-27/HU-45. `NOTE (HU-20)` si la app abre “Mensaje” desde el perfil: esa conversación es de HU-20.
- **Fuera de alcance:** “Seguir” y “Responde en” de la maqueta no están en los criterios de Jira y no tienen datos en la API; “Opiniones” queda vacía hasta HU-27/HU-45.

## Contratos
- **Fila modificada** en [`docs/endpoints.md`](../../../docs/endpoints.md): `GET /api/v1/products` suma `sellerId` al query. La respuesta sigue siendo `ProductPage { items, nextCursor, total }`; con `sellerId` + `hideSoldOut=true`, `total` es el número de publicaciones activas. `Spec` suma HU-05 y `Consumidor` suma `musubi-app` HU-05 (`Perfil-vendedor` → “Publicaciones”).
- **Fila modificada** en [`docs/endpoints.md`](../../../docs/endpoints.md): `GET /api/v1/profiles/{id}` pasa a `HU-04 · HU-05`; se documenta que `ratingAvg` es `null` y `ratingCount` es `0` sin calificaciones (RN-26) y que no lleva correo. La forma JSON no cambia.
- **Sin DTOs nuevos** ni cambios en `PublicProfileResponse`/`ProductPage`.
- **Sin fixtures nuevos:** la forma de `ProductPage` y de `PublicProfileResponse` no cambia; `ContractTest/ListingsContractTests.cs` y `ContractTest/ProfilesContractTests.cs` siguen cubriendo las formas existentes.
- **Errores:** no se agregan códigos. Un `sellerId` vacío o ausente no filtra; uno con formato inválido (no ObjectId) devuelve una página vacía (`total: 0`), no un error; la app solo lo usa después de un perfil `200`. Se reutilizan `profiles.not_found`, `listings.invalid_cursor`, `listings.invalid_page` y `listings.unknown_campus`.
- **Contraparte:** [`musubi-app:HU-05`](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m02-profile/HU-05-perfil-publico).

## Tests
- `TestShared/Listings/ListingRepositoryContract.cs` (+1, corre en memoria y en MongoDB real):
  - `SellerFilter_OnlyTheActiveListingsOfThatSeller_AndCountsThem`: con `IncludeSoldOut=false` solo devuelve las `active` de ese vendedor; con `IncludeSoldOut=true` suma las `soldOut`; nunca devuelve `hidden`, `sold`, `removed` ni las de otro vendedor; `CountAsync` coincide con el total.
- `IntegrationTest/Profiles/ProfileFlowTests.cs` (+1):
  - `SellerProfile_IsReachableFromProductDetail_AndCountsOnlyActiveListings`: Ana publica 3, pausa 1 y marca 1 como vendida; Luis abre el detalle de la activa, obtiene `seller.id`, consulta `GET /profiles/{anaId}` (200, `ratingAvg: null`, `ratingCount: 0`, sin `email`) y `GET /products?sellerId={anaId}&hideSoldOut=true` (un solo elemento activo y `total: 1`); un `sellerId` mal formado devuelve `total: 0`.
- `SecurityTest/Auth/ProfilesAuthTests.cs`: se reutilizan `401` sin sesión y `404` para otro campus; el bloqueo con `sellerId` está en `ReputationFlowTests`.
- `SecurityTest/Auth/ListingsAuthTests.cs`: `ShowcaseFilters_ArePublic_AndRespectTheCampus` suma el caso `sellerId`: el producto de otro campus no aparece.
- `IntegrationTest/Reputation/ReputationFlowTests.cs`: el bloqueo en cualquier dirección deja en `0` el total de `GET /products?sellerId=`.
- `ContractTest`: sin fixture nuevo; las formas de `ProductPage` y `PublicProfileResponse` no cambian.
- `DatabaseTest`: sin script nuevo; la prueba compartida del repositorio corre contra MongoDB real en el CI.

## Criterios
- [x] `GET /products?sellerId=…&hideSoldOut=true` devuelve solo las publicaciones activas de esa persona y `total` es su número → `SellerFilter_OnlyTheActiveListingsOfThatSeller_AndCountsThem` y `SellerProfile_IsReachableFromProductDetail_AndCountsOnlyActiveListings` en verde.
- [x] Pausadas, vendidas, agotadas, quitadas, de otro campus y de personas bloqueadas quedan fuera → prueba compartida (estados), `ShowcaseFilters_ArePublic_AndRespectTheCampus` (campus) y `ReputationFlowTests` (bloqueo).
- [x] `GET /profiles/{id}` devuelve `ratingAvg: null`, `ratingCount: 0` y no incluye `email` → `SellerProfile_IsReachableFromProductDetail_AndCountsOnlyActiveListings` y `ProfilesContractTests`.
- [x] El perfil del vendedor es accesible desde el detalle: `seller.id` → `GET /profiles/{id}` responde 200 → `SellerProfile_IsReachableFromProductDetail_AndCountsOnlyActiveListings`.
- [x] Build Release 0/0 · Unit 564 · Integration 165 · Security 207 · Contract 20 al 100 % con `--no-build`. Database queda para el CI (Docker apagado en local); la prueba compartida del repositorio corre también contra MongoDB real.
- [x] `pwsh scripts/check_endpoints.ps1` OK (81 rutas) · `pwsh scripts/critic-guardrails.ps1` PASS · `pwsh scripts/check_shared_sections.ps1` OK (6 bloques).
- [x] `UnitTest/Common/VersioningTests.cs` pasa con `0.5.0-alpha.21` y la última fila de `docs/versionado.md`.
- [x] `security-reviewer` PASS y `traceability-clerk` OK (sin drift).

## Límites
- **Sin calificaciones, promedio ni opiniones:** `ratings`, el cálculo del promedio y la pestaña “Opiniones” son de HU-27/HU-45. `NOTE (HU-27)`. La app muestra “Sin calificaciones” con `ratingAvg: null` (RN-26).
- **Sin “Seguir”:** no está en los criterios de Jira y no hay colección de seguidores.
- **Sin “Responde en”:** la API no mide tiempos de respuesta.
- **“Mensaje” desde el perfil:** la conversación es de HU-20; esta HU no la abre.
- **Sin campo `activeListings` en `PublicProfileResponse`:** el contador sale de `total` de la vitrina filtrada; así no se duplican datos ni se agrega una consulta al perfil.
- **Sin índice nuevo:** `ix_seller_id` cubre el filtro. Si la consulta se vuelve lenta con muchas publicaciones, un índice compuesto es una optimización posterior, con medición (`02-03`), no un umbral inventado.
- **Sin paginación nueva:** se reutilizan `cursor`/`page` y `limit` de la vitrina.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 11-Nov-2026
- **Detalle:**
  - spec detallada desde Jira (SCRUM-34), la maqueta (`Perfil-vendedor`) y HU-04/HU-08; se reutiliza `GET /products?sellerId=` y la reputación queda en `null`/`0` hasta HU-27/HU-45, como indicó Jira el 08-Nov-2026.
  - verificación local (11-Nov-2026, sobre `main` `b7bdb1d`, ya con HU-24): build Release 0/0; Unit 564, Integration 165, Security 207 y Contract 20 al 100 % con `--no-build`; `check_endpoints` OK (81 rutas); `critic` PASS (28 archivos, 0 avisos); `check_shared_sections` OK (6 bloques). `DatabaseTest` queda para el CI porque Docker no estaba corriendo en local; la prueba compartida del repositorio corre también contra MongoDB real.
  - versión `0.5.0-alpha.21`, esquema 19 (`MongoSchema.ExpectedVersion = 19` desde HU-24); sin script `db/` ni índice nuevo. `VersioningTests` en verde.
  - `security-reviewer` PASS (sin bloqueantes; W1/W2 corregidos) y `traceability-clerk` OK (sin drift).

# HU-05 — Consultar perfil y reputación de otro usuario (tareas)

## T1 — Detallar la spec
- **Qué:** conciliar Jira (SCRUM-34), la maqueta (`Perfil-vendedor`) y HU-04/HU-08; decidir la composición del perfil y las publicaciones activas.
- **Verificar:** el alcance acordado: `GET /profiles/{id}` + `GET /products?sellerId=…&hideSoldOut=true`; reputación en `null`/`0` hasta HU-27/HU-45.
- **Estado:** ✅ Hecho (11-Nov-2026).

## T2 — Prueba compartida del filtro
- **Crear:** `SellerFilter_OnlyTheActiveListingsOfThatSeller_AndCountsThem` en `TestShared/Listings/ListingRepositoryContract.cs`.
- **Cubre:** solo las `active` del vendedor; con `IncludeSoldOut=true` suma `soldOut`; excluye `hidden`, `sold`, `removed` y a otro vendedor; `CountAsync` coincide.
- **Verificar:** `UnitTest/Data/InMemoryListingRepositoryTests.cs` y `DatabaseTest/MongoListingRepositoryTests.cs` (heredan la batería).
- **Estado:** ✅ Hecho (11-Nov-2026). Unit 564 en verde; Database en el CI.

## T3 — Filtro en la vitrina
- **Cambiar:**
  - `Modules/Listings/Data/ListingRepositories.cs`: `ProductQuery.SellerId`;
  - `Modules/Listings/Data/MongoListingRepositories.cs`: filtro en `Showcase`;
  - `Modules/Listings/Data/InMemoryListingRepositories.cs`: el mismo filtro;
  - `Modules/Listings/ListingService.cs`: `ShowcaseAsync(..., sellerId, ...)`;
  - `Modules/Listings/ProductsController.cs`: `[FromQuery] string? sellerId`.
- **Verificar:** la prueba compartida y `UnitTest`/`DatabaseTest` verdes.
- **Estado:** ✅ Hecho (11-Nov-2026).

## T4 — Integración del perfil
- **Crear:** `SellerProfile_IsReachableFromProductDetail_AndCountsOnlyActiveListings` en `IntegrationTest/Profiles/ProfileFlowTests.cs`.
- **Cubre:** publicar 3, pausar 1, vender 1; `seller.id` del detalle → `GET /profiles/{id}` (200, `ratingAvg: null`, `ratingCount: 0`, sin `email`) → `GET /products?sellerId=…&hideSoldOut=true` (un elemento, `total: 1`).
- **Verificar:** `IntegrationTest` verde.
- **Estado:** ✅ Hecho (11-Nov-2026). Integration 165 en verde.

## T5 — Documentación y versión
- **Actualizar:**
  - `docs/endpoints.md`: `GET /products` suma `sellerId` y HU-05; `GET /profiles/{id}` pasa a `HU-04 · HU-05` y documenta RN-26;
  - `docs/trazabilidad.md`: fila HU-05 con tests y estado;
  - `docs/versionado.md`: `0.5.0-alpha.21`, esquema 19 (`MongoSchema.ExpectedVersion = 19` desde HU-24), app `0.8.0+10`;
  - `Musubi.Api/Musubi.Api.csproj`: `<Version>0.5.0-alpha.21</Version>`;
  - `AGENTS.md`: conteo real de rutas (78);
  - `Memoria.md`: qué se hizo, decisiones y verificación.
- **Verificar:** `VersioningTests`, `check_endpoints` OK y `check_shared_sections` OK.
- **Estado:** ✅ Hecho (11-Nov-2026): `check_endpoints` OK (81), `check_shared_sections` OK (6 bloques), `VersioningTests` en verde; la fila `alpha.21` se replicó en `musubi-app` y en su `Memoria.md`.

## T6 — Verificación y cierre
- **Correr:** build Release 0/0; Unit, Integration, Security y Contract con `--no-build`; Database si hay Docker; `check_endpoints`; critic; `security-reviewer`; `traceability-clerk`.
- **Actualizar:** `spec.md` a `🚧 Borrador con evidencia` con conteos reales; `task.md`; `docs/trazabilidad.md` a `Borrador con evidencia`; `Memoria.md`.
- **Estado:** ✅ Hecho (11-Nov-2026): `security-reviewer` PASS (W1/W2 corregidos) y `traceability-clerk` OK; rama rebasada sobre HU-24/#72. Falta el `DatabaseTest` del CI.

## Pendientes
- `NOTE (HU-27)` / `NOTE (HU-45)`: calificaciones, promedio y lista de opiniones; la app muestra “Sin calificaciones” mientras tanto (RN-26).
- `NOTE (HU-20)`: “Mensaje” desde el perfil, si la app lo habilita.
- “Seguir” y “Responde en” no están en los criterios de Jira ni tienen datos en la API.
- La app de HU-05 (pantalla `Perfil-vendedor`) va en su PR de `musubi-app`.

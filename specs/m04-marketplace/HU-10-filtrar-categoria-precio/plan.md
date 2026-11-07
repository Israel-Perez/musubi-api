# plan.md — HU-10 Filtrar por categoría y precio

1. **Componentes:**
   - `Musubi.Api/Modules/Listings/ProductsController.cs`: parámetros `minPriceCents`, `maxPriceCents` y `condition` en `Showcase`.
   - `Musubi.Api/Modules/Listings/ListingService.cs`: `ShowcaseAsync(..., long? minPriceCents, long? maxPriceCents, string? condition, ...)`, validación del rango y del estado.
   - `Musubi.Api/Modules/Listings/ListingsErrors.cs`: `InvalidPriceRange`; reutiliza `InvalidCondition`.
   - `Musubi.Api/Modules/Listings/Data/ListingRepositories.cs`: `ProductQuery` con `MinPriceCents`, `MaxPriceCents` y `Condition`.
   - `Musubi.Api/Modules/Listings/Data/MongoListingRepositories.cs`: `Filter.Gte`/`Filter.Lte` sobre `PriceCents` y `Filter.Eq` sobre `Condition` en `Showcase` y `CountAsync`.
   - `Musubi.Api/Modules/Listings/Data/InMemoryListingRepositories.cs`: mismas comparaciones en el fake.
   - `TestShared/Listings/ListingRepositoryContract.cs`, `IntegrationTest/Listings/ListingFlowTests.cs`, `ContractTest/ListingsContractTests.cs`, `PerformanceTest/LatencyTests.cs`.
   - Sin script en `db/` y sin cambio en `MongoSchema.ExpectedVersion`.

2. **Decisiones:**
   - **Extender `GET /products` con parámetros en lugar de una ruta nueva:** la app ya consume esa ruta y comparte respuesta, auth, caché, `ETag`, cursor y `total`. Una ruta `/products/filter` duplicaría controller y política de caché.
   - **Precio en centavos (`minPriceCents`/`maxPriceCents`):** toda la API expone dinero en centavos (`priceCents`, `lowCents`, `highCents`); la app convierte desde pesos en su capa de presentación.
   - **Estado (`condition`) incluido aunque Jira no lo enumere:** la hoja "Filtros" de `Busqueda-filtros` lo muestra (Todos/Nuevo/Usado) y el lineamiento 4 prohíbe quitar funciones de la maqueta.
   - **`400` con código propio en vez de ignorar el filtro inválido:** un rango invertido o negativo es un error de la app, no una búsqueda vacía; el catálogo de `docs/errores.md` mantiene el contrato.
   - **Reusar `listings.invalid_condition`:** ya existe por HU-06 con el texto "Elige el estado del producto."; un código nuevo duplicaría semántica.
   - **Sin índice nuevo:** el orden por `_id` es la paginación de la vitrina; un índice con `priceCents` rompería ese orden (sort bloqueante) y R5 pide solo índices con consulta real. El rango se filtra sobre `ix_community_status_id` / `ix_community_category_status_id`, que ya acotan el campus y la categoría.
   - **Validación en el servicio:** el repositorio queda simple y las dos implementaciones comparten las mismas reglas de frontera.
   - **Caché y sesión sin cambios:** `SetVaryByQuery("*")` ya incluye los parámetros nuevos; la respuesta con `Authorization` no se cachea ni comparte.
   - **Sin persistencia ni log de filtros:** RNF-07; no hay historial de filtros en esta HU.

3. **Excepciones al stack (00-01):** ninguna. Se usa MongoDB y lo que ya está instalado.

4. **Guardarraíles:**
   - Validación en el borde: rango `0..MaxPriceCents`, `min ≤ max`, `condition` en `{new, used}`; si no, `listings.invalid_price_range` o `listings.invalid_condition`.
   - El filtro de comunidad se mantiene siempre: un filtro nunca cruza campus (RNF-21, OWASP API1).
   - Solo `active` con `q`; sin `q`, `hideSoldOut` sigue gobernando los estados.
   - Los parámetros nuevos no se guardan ni se registran; solo viven en la URL y en la clave de caché.
   - Respuestas de error con `problem+json`, sin detalle interno.
   - `NOTE (HU-10)` para el rango sin índice y para un posible orden por precio.

5. **Validación:**
   - Build Release 0/0.
   - `UnitTest`, `IntegrationTest`, `SecurityTest`, `ContractTest`, `DatabaseTest` (Docker) y `PerformanceTest` en verde con `--no-build`.
   - `pwsh scripts/check_endpoints.ps1` y `pwsh scripts/critic-guardrails.ps1`.
   - Prueba manual: publicar tres productos con precios y estados distintos; filtrar por rango, por estado y combinar con `category` y `q`; confirmar `total` y los límites exactos.
   - `semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error` antes del primer push.

6. **Secuencia de implementación:**
   1. `ProductQuery` con los tres campos nuevos.
   2. Las dos implementaciones del repositorio (rojo → verde con la batería compartida).
   3. Servicio, error y controller.
   4. Pruebas de integración, contrato y rendimiento.
   5. Documentación (`endpoints`, `errores`, `trazabilidad`, `Memoria`) y PR. `versionado` y `<Version>` no se tocan: la fila y el salto van en el PR que despliega.

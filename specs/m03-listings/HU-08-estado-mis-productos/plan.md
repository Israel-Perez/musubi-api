# plan.md — HU-08 Consultar el estado de mis productos

1. **Componentes:**
   - `db/013-publicacion-vendida.js` y `MongoSchema.ExpectedVersion = 13`;
   - `Data/ListingDocuments.cs` (`ProductStatuses.Sold`, `SoldAt`), `ListingRepositories.cs`, `MongoListingRepositories.cs` e `InMemoryListingRepositories.cs`;
   - `ListingService` (`MineAsync` por pestaña y el detalle de una vendida para su dueño), `MyListingsService` (`MarkSoldAsync` y el 409);
   - `ProductsController` (`GET /mine`) y `MyProductsController` (`POST /{id}/sold`);
   - `ProductsDtos.cs` (`MyProductPage`, `MyProductCounts`, `soldAt`), `ListingsErrors.cs` (`ProductSold`, `InvalidTab`);
   - pruebas en `TestShared/`, `UnitTest/`, `IntegrationTest/`, `SecurityTest/`, `ContractTest/` y `DatabaseTest/`.
2. **Decisiones:**
   - **Ya decididas por la usuaria (09-Nov-2026):** estados como la maqueta; "Marcar como vendido" con el estado `sold`; el contador +/− y "Reponer" se quedan en HU-50; la rama sale de `main`, que ya trae HU-07.
   - **D1 · Una sola ruta `GET /products/mine` con `?tab=`, y no tres rutas:** las tres pestañas tienen la misma forma. Sin `tab` responde lo de hoy, así la app `0.6.0+8` publicada no se rompe.
   - **D2 · Los conteos vienen en cada respuesta:** la maqueta los muestra en las pestañas. Es un `$group` sobre las publicaciones del vendedor (con `ix_seller_id`), sin una segunda llamada desde la app.
   - **D3 · Pausada va en Activos:** la maqueta no tiene pestaña de pausadas; la fila dice "Pausada" por su `status`.
   - **D4 · Una vendida no se edita, no se pausa ni se reactiva (409 `listings.product_sold`), pero sí se elimina:** reactivar la devolvería a la vitrina; eliminar es la forma de quitarla de "Vendidos".
   - **D5 · `soldAt` en la publicación y en `ProductResponse`:** la pestaña Vendidos puede decir "Vendido hace N días". Es aditivo en el contrato (`product.json`).
3. **Excepciones al stack (00-01):** ninguna.
4. **Guardarraíles:**
   - el dueño va en el filtro de `MarkSoldAsync` (OWASP API1, como HU-07);
   - `UpdateDetailsAsync` y `SetPausedAsync` excluyen `sold` en el filtro (sin ventana entre leer y escribir);
   - la pestaña se valida contra una lista cerrada (400);
   - `limit` de 1 a 50 y cursor validado (`listings.invalid_cursor`);
   - la vitrina se borra de la caché de salida al vender.
5. **Validación:** las pruebas de la `spec.md`, el script contra MongoDB real (`DatabaseTest` en el CI), los guardarraíles (`check_endpoints`, `check_shared_sections`, critic, Semgrep) y los sub-agentes `security-reviewer` y `traceability-clerk`.
6. **Secuencia:** `db/013` → documentos y repositorios con su batería → servicio y controllers → pruebas → documentación (`endpoints`, `errores`, `modelo-datos`, `db/README`, `versionado`, `trazabilidad` y `Memoria.md`) → revisión → PR.

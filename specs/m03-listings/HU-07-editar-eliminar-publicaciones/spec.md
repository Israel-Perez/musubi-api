# HU-07 — Editar o eliminar mis publicaciones

**HU / Jira / Sprint / Módulo / Contraparte:** HU-07 / SCRUM-24 / Sprint 4 / M-03 / [musubi-app:HU-07](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m03-listings/HU-07-editar-eliminar-publicaciones)

## Contexto
Jira (SCRUM-24): Como **vendedor**, quiero **editar o eliminar mis publicaciones**, para **que la información siempre sea correcta**.

- **Maqueta `Mis-productos`:** al tocar una publicación sale el menú "Ver publicación", "Editar", "Marcar como vendido" y "Eliminar".
  - "Editar" reutiliza `Vender-2…4` con el título "Editar publicación" y no muestra `Vender-1` (las fotos).
  - "Eliminar" pide confirmación: "Ya no aparecerá en Musubi. Esta acción no se puede deshacer."
  - "Marcar como vendido" y "Reponer" (+/− piezas) son de HU-08 y HU-50.
- **Pausar:** la maqueta no lo tiene, pero el estado `hidden` ("Pausada por su vendedor (HU-07)") ya existe en `ProductStatuses` y en el `$jsonSchema` de `db/002-publicaciones.js`. Esta HU lo agrega en la API (lineamiento 4: se puede agregar, nunca quitar).
- Trazabilidad: RF-08, RF-09 · CU-05 · RN-07 · RNF-03.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-02` (caché de la vitrina), `02-03`, `02-07` (moderación), `HU-06`.

## Requisitos
1. **Solo el dueño** edita, pausa, reactiva o elimina una publicación (RNF-03). A otra persona, a una publicación que no existe y a una quitada por moderación se les responde lo mismo: `404 listings.product_not_found` (OWASP API1: no revela cuál).
2. **Editar** (`Vender-2…4`): nombre, descripción, precio, categoría y estado (nuevo, o usado con su detalle), con las mismas reglas que al publicar (RN-03, RN-04). No cambia las fotos, las piezas ni el estado de la publicación.
3. **Moderación (02-07):** el nombre y la descripción que cambian pasan por `IContentModerator` antes de guardarse; lo inapropiado responde `422 moderation.text_rejected` y no se guarda nada.
4. **Pausar y reactivar:** pausar deja la publicación en `hidden` (sale de la vitrina y del chat, sigue en "Mis productos"); reactivar la regresa a `active`, o a `soldOut` si no tiene piezas. Las dos son idempotentes.
5. **Eliminar:** borrado real de la publicación y de sus fotos en S3 ("Esta acción no se puede deshacer"). Una conversación sobre ella sigue existiendo y muestra `available: false` con el nombre guardado (HU-20).
6. **Vitrina al día:** editar, pausar, reactivar o eliminar borra la vitrina de la caché de salida (`EvictByTagAsync`, igual que al publicar).
7. **Coincidencias (RF-08):** si cambian el nombre, la categoría, el precio o la descripción, el sistema vuelve a buscar coincidencias. Las coincidencias no existen todavía: `NOTE (HU-18)`.
8. **Pedidos abiertos (RN-07):** una publicación con pedidos **Pendientes** o **Confirmados** no se puede eliminar. Los pedidos no existen todavía: `NOTE (HU-23)`.

RNF que aplican: RNF-03 (solo el dueño), RNF-05 (la vitrina anónima sigue sin vendedor).

## Diseño
- **Módulo `Modules/Listings/`:**
  - `MyListingsService` (nuevo): editar, pausar, reactivar y eliminar. Comparte las reglas de nombre, descripción, precio, estado y categoría con `ListingService.PublishAsync` (se extraen a `ProductRules`, sin cambiar su comportamiento).
  - `MyProductsController` (nuevo, mismo prefijo `api/v{version}/products`, `[Authorize]`): las 4 rutas de escritura. Va en su propio archivo (`check_endpoints` lee una ruta por archivo) y deja a `ProductsController` con la lectura y la publicación (Sonar S6960).
  - `ListingsErrors.ProductNotFound` (nuevo).
- **Dueño en el filtro de cada escritura:** el repositorio filtra por `_id` **y** `sellerId` (y `status != removed`), con `FindOneAndUpdate` / `FindOneAndDelete`. Si el filtro no encuentra nada, el servicio responde 404. Pausar, reactivar y eliminar no leen antes. Editar sí lee antes, para saber qué texto cambió y moderarlo, pero vuelve a filtrar por el dueño al escribir: una carrera no deja escribir sobre una publicación ajena ni revivir una borrada.
- **Repositorio (`IProductRepository`, Mongo e InMemory):**
  - `UpdateDetailsAsync(id, sellerId, details, now)` → el documento nuevo o `null`;
  - `SetPausedAsync(id, sellerId, paused, now)` → el documento nuevo o `null`; reactivar calcula `active`/`soldOut` con las piezas en la misma operación (update con pipeline en Mongo);
  - `DeleteAsync(id, sellerId)` → el documento borrado (para saber sus fotos) o `null`.
- **Fotos:** al eliminar, `IMediaStorage.DeleteAsync` por cada llave de la publicación. Primero se borra el documento y después las fotos: si S3 falla a medias, la publicación ya no se ve y las fotos huérfanas quedan para la regla de ciclo de vida del bucket (`NOTE (02-05)`). Es un caso nuevo: HU-06 solo registró las fotos que nunca se publican.
- **Datos:** sin script nuevo en `db/`. El `$jsonSchema` de `products` ya acepta `hidden` y `updatedAt`; `MongoSchema.ExpectedVersion` sigue en 5. Fila de `products` en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md): se agrega HU-07 al uso.
- **Pendientes en el código:** `NOTE (HU-18)` en `MyListingsService.UpdateAsync` y `NOTE (HU-23)` en `MyListingsService.DeleteAsync`.

## Contratos
- **Filas nuevas** en [`docs/endpoints.md`](../../../docs/endpoints.md) (M-03):
  - `PUT /api/v1/products/{id}` — `UpdateProductRequest { title, description, category, priceCents, condition, conditionDetail }` → `ProductResponse`;
  - `POST /api/v1/products/{id}/pause` → `ProductResponse` (`status: hidden`);
  - `POST /api/v1/products/{id}/resume` → `ProductResponse` (`status: active` o `soldOut`);
  - `DELETE /api/v1/products/{id}` → 204.
- **Errores:** reutiliza `listings.product_not_found` (404, "Esta publicación ya no está disponible."), que HU-11 creó con el mismo sentido; la fila de [`docs/errores.md`](../../../docs/errores.md) nombra las dos HU. Los demás ya existen (`listings.invalid_*`, `moderation.*`, `auth.forbidden`, `request.too_large`).
- **Fixtures:** las tres respuestas con cuerpo usan la forma de `ContractTest/Fixtures/product.json` (sin cambios).
- **Coordinación con HU-11 y HU-09:** `GET /api/v1/products/{id}` (detalle, HU-11) comparte la plantilla `{id}` con otro verbo, así que no chocan; una publicación pausada solo la ve su dueño en el detalle. Editar recalcula `searchWords` (búsqueda de HU-09) en la misma operación.

## Tests
- `TestShared/Listings/ListingRepositoryContract.cs` (en memoria en `UnitTest` y en MongoDB real en `DatabaseTest`):
  - editar solo con el `sellerId` correcto, y nunca una quitada;
  - pausar y reactivar (con piezas → `active`; sin piezas → `soldOut`);
  - eliminar devuelve el documento solo al dueño.
- `UnitTest/Listings/MyListingsServiceTests.cs`:
  - cada validación con su código;
  - solo se modera el texto que cambió;
  - eliminar borra cada foto del almacén;
  - cada escritura borra la vitrina de la caché.
- `IntegrationTest/Listings/MyListingsFlowTests.cs`:
  - editar y verlo en "Mis productos" y en la vitrina anónima (prueba que la caché se borró);
  - texto con `[[inapropiado]]` → 422 y la publicación no cambia;
  - pausar → sale de la vitrina y sigue en "Mis productos"; reactivar → vuelve;
  - eliminar → 204, ya no está en ningún lado, su foto responde 404 y la conversación muestra `available: false`.
- `SecurityTest/Auth/MyListingsAuthTests.cs`:
  - 401 sin sesión en las 4 rutas;
  - otra persona del mismo campus recibe 404 en las 4 y la publicación no cambia (OWASP API1);
  - un id que no es ObjectId → 404;
  - una cuenta no activa → 403.
- `ContractTest/ListingsContractTests.cs`: la respuesta de editar, pausar y reactivar tiene la forma de `product.json`.

## Criterios
- [x] Solo el dueño edita, pausa, reactiva o elimina (RNF-03): `MyListingsAuthTests` (17: 401 sin sesión, 404 para otra persona del campus, 404 con un id inválido y 403 con una cuenta suspendida, en las 4 rutas; y el tope de 16 KB del cuerpo de la edición, comprobado por reflexión porque TestServer no lo aplica; en la nube lo aplica Kestrel).
- [x] Editar aplica las reglas de publicar y modera solo el texto que cambió: `MyListingsServiceTests` (10) y `MyListingsFlowTests` (5).
- [x] Pausar, reactivar y eliminar se reflejan en la vitrina anónima en la siguiente consulta: `MyListingsFlowTests`. Si la etiqueta de la caché cambia, 3 pruebas quedan en rojo (mutación comprobada).
- [x] RF-08 y RN-07 quedan con `NOTE (HU-18)` y `NOTE (HU-23)` en `MyListingsService`, esta spec, `task.md` y `Memoria.md` (T24).
- [x] Build Release 0/0 · Unit 365 · Integration 134 · Security 124 · Contract 13 (100 %, con main del 09-Nov-2026 fusionado) · `check_endpoints` OK (54 rutas) · `check_shared_sections` OK · critic PASS. `DatabaseTest` (batería del repositorio contra MongoDB real, incluida `UpdateDetails_TheSearchFindsTheNewNameAndNotTheOldOne`) corre en el CI.

## Límites
- **Fotos:** no se editan en esta HU (la maqueta no muestra `Vender-1` al editar).
- **Piezas:** no se editan aquí. "Reponer" (+/−) es de HU-50 y "Marcar como vendido" de HU-08; así la edición no pisa el descuento atómico de un pedido (R7).
- `NOTE (HU-18)`: volver a buscar coincidencias al editar (RF-08).
- `NOTE (HU-23)`: no eliminar con pedidos pendientes o confirmados (RN-07); se agregará `409 listings.product_has_open_orders`.
- `NOTE (02-05)`: fotos huérfanas si S3 falla a medias al eliminar (regla de ciclo de vida del bucket).
- Sin control de concurrencia optimista: si el dueño edita desde dos teléfonos a la vez, gana la última edición.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 05-Nov-2026
- **Detalle:**
  - detallada desde Jira (SCRUM-24), la maqueta (`Mis-productos`, `Vender-2…4`) y HU-06; el sprint pasa de S2 a S3, como está en Jira;
  - decisiones D1–D6 de `plan.md` aprobadas por el usuario el 05-Nov-2026;
  - `ProductRules` reúne las reglas de publicar y editar; al publicar, las piezas ahora se validan después de la categoría (solo cambia qué error sale primero si hay varios).

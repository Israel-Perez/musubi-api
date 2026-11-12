# HU-50 — Manejar la cantidad disponible de un producto

**HU / Jira / Sprint / Módulo / Contraparte:** HU-50 / SCRUM-67 / Sprint 4 / M-03 / [musubi-app:HU-50](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m03-listings/HU-50-cantidad-disponible)

## Contexto
Jira (SCRUM-67): Como **vendedor**, quiero **indicar cuántas piezas tengo de un producto**, para **vender varias unidades con una sola publicación y que los compradores sepan cuántas quedan**.

- Maqueta: `Vender-4` (cantidad al publicar), `Producto` ("Quedan N", "Último", "Agotado") y `Mis-productos` (piezas de cada fila y "Reponer" en las agotadas).
- **Comentario del PO en Jira (12-Nov-2026):** tres criterios ya existen y solo se dejan con evidencia. Falta cambiar las piezas desde "Mis productos" con `PUT /products/{id}/stock`, siguiendo el modelo de `SetPausedAsync`.
- **Decisiones de la usuaria (12-Nov-2026):**
  - en la app, "Cambiar piezas" en el menú y "Reponer" en las filas de Agotados;
  - el choque con un pedido que aparta piezas al mismo tiempo se acepta y se documenta (gana la última escritura).
- Desde HU-24, `stock` cuenta solo las piezas **disponibles**: las apartadas en un pedido aceptado ya se restaron.
- Trazabilidad: sin RF/RNF en Jira (historia que nació de la maqueta, SCRUM-75).

**Depende de:** `HU-06`, `HU-07`, `HU-08`, `HU-10` y `HU-24`.

## Requisitos
1. **Al publicar se captura la cantidad** (1 a 10, `Vender-4`). Ya existe desde HU-06.
2. **El detalle muestra "Quedan N", "Último" o "Agotado"** según el inventario. Ya existe desde HU-11 (app).
3. **Un apartado o una venta descuenta de forma atómica, sin vender de más.** Ya existe desde HU-24 (`IProductStock.TryReserveAsync`): el filtro `stock >= cantidad` y la resta van en la misma escritura.
4. **El buscador oculta los agotados.** Ya existe desde HU-10 (`hideSoldOut`).
5. **Cambiar las piezas (nuevo):** `PUT /api/v1/products/{id}/stock` con `{ stock }` de 0 a 10.
   - Solo el dueño; a otra persona, a una inexistente o a una quitada se responde `404 listings.product_not_found` (OWASP API1).
   - Con 0 piezas pasa a `soldOut`; con más de 0, a `active`. **Una pausada se queda pausada.**
   - Una vendida no se cambia: `409 listings.product_sold`, igual que editar o pausar.
   - Fuera de rango: `400 listings.invalid_stock` ("Las piezas disponibles van de 0 a 10.").
   - Borra la vitrina de la caché de salida.
6. **Apartado vencido:** que regrese la pieza solo depende de los apartados. `NOTE (HU-52)`.

RNF que aplican: RNF-03 (solo el dueño).

## Diseño
- **Repositorio:** `IProductRepository.SetStockAsync(id, sellerId, stock, now)`, en Mongo y en memoria. Usa el mismo filtro `Editable` de HU-07/08 (dueño, ni quitada ni vendida) y un update con pipeline que decide el estado en la misma escritura: si estaba pausada sigue pausada; si no, `active` con piezas y `soldOut` sin ellas.
- **Servicio:** `MyListingsService.SetStockAsync`. Si el filtro no encuentra la publicación, el servicio decide entre 409 (es suya y está vendida) y 404 con `WhyUnchangedAsync`.
- **Controller:** `MyProductsController.SetStock` (`PUT {id}/stock`, cuerpo de hasta 1 KB).
- **Datos:** sin script en `db/`; el validador ya acepta `stock` de 0 en adelante.

## Contratos
- **Fila nueva** en [`docs/endpoints.md`](../../../docs/endpoints.md): `PUT /api/v1/products/{id}/stock` con `SetStockRequest { stock }` → `ProductResponse`.
- **Errores** en [`docs/errores.md`](../../../docs/errores.md): se reutilizan `listings.invalid_stock` (ahora también para el rango 0 a 10 al cambiar), `listings.product_not_found` y `listings.product_sold`.
- La respuesta tiene la forma de `ContractTest/Fixtures/product.json`.

## Tests
- `TestShared/Listings/ListingRepositoryContract.cs` (en memoria y en MongoDB real): `SetStock_OnlyTheOwner_KeepsPaused_AndDecidesSoldOut`.
- `UnitTest/Listings/MyListingsServiceTests.cs`: rango, 409 de una vendida, vitrina borrada y cuenta no activa.
- `IntegrationTest/Listings/MyProductsTabsFlowTests.cs`: con 0 pasa a Agotados y sale de la vitrina con "Ocultar agotados"; "Reponer" la regresa a Activos.
- `SecurityTest/Auth/MyListingsAuthTests.cs`: la ruta nueva en las teorías de dueño (401 sin sesión, 404 para otra persona, 404 con un id inválido y 403 con una cuenta suspendida).
- `ContractTest/ListingsContractTests.cs`: la respuesta tiene la forma de `product.json`.
- **Ya existentes** (evidencia de los requisitos 1 a 4):
  - `ListingFlowTests.Publish_ExplainsWhatIsMissing` (`listings.invalid_stock` al publicar);
  - `Stock_ReserveRelease_AndSoldWhenEmpty` (apartado atómico);
  - `Filters_ByCategory_AndHideSoldOut`;
  - en la app, `demo_catalog_repository_test.dart` ("Último" y "Quedan 2").

## Criterios
- [x] Cambiar las piezas solo por el dueño: `MyListingsAuthTests` en verde (la ruta en las 4 teorías de dueño y en la de una vendida ajena).
- [x] Una pausada sigue pausada y 0 piezas la deja agotada: `SetStock_OnlyTheOwner_KeepsPaused_AndDecidesSoldOut` en verde (`DatabaseTest` en el CI). Mutación: sin conservar la pausa, en rojo.
- [x] Requisitos 1 a 4 con sus pruebas existentes en verde.
- [x] Build Release 0/0 · Unit 569 · Integration 165 · Security 212 · Contract 20 (100 %) · `check_endpoints` OK (82) · `check_shared_sections` OK · critic PASS.

## Límites
- **Choque con un pedido al mismo tiempo** (decisión de la usuaria): `PUT { stock }` fija el número y gana la última escritura. Si un pedido aparta piezas en el mismo instante, el número del vendedor puede pisar esa resta. Es poco probable (el vendedor fija lo que tiene a la mano) y se corrige volviendo a cambiar las piezas.
- `NOTE (HU-52)`: un apartado vencido regresa la pieza sola.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 12-Nov-2026
- **Detalle:** detallada desde Jira (SCRUM-67 y el comentario del PO), la maqueta y el código de HU-24.

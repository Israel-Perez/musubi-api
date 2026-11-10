# HU-08 — Consultar el estado de mis productos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-08 / SCRUM-25 / Sprint 4 / M-03 / [musubi-app:HU-08](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m03-listings/HU-08-estado-mis-productos)

## Contexto
Jira (SCRUM-25): Como **vendedor**, quiero **consultar el estado de mis productos**, para **saber cuáles están disponibles, reservados o vendidos**.

- **Maqueta `Mis-productos`:** tres pestañas con su conteo, **Activos (n)**, **Vendidos (n)** y **Agotados (n)**. Cada fila muestra la foto, el nombre, el precio y las piezas ("Quedan N", "Agotado"). El menú de cada publicación ofrece "Ver publicación", "Editar", "Marcar como vendido" ("¡Felicidades por tu venta!") y "Eliminar".
- **Jira frente a la maqueta** (decisión de la usuaria, 09-Nov-2026: manda la maqueta):
  - **Disponible** → pestaña Activos (`active`);
  - **Inactiva** → la pausada de HU-07 (`hidden`), también en Activos y marcada como pausada;
  - **Vendida** → estado nuevo `sold`, pestaña Vendidos;
  - **Agotada** (0 piezas, `soldOut`) → pestaña Agotados;
  - **Reservada** no existe todavía: llega con los apartados (HU-52) y los pedidos (HU-23).
- Fuera de esta HU, en HU-50 (decisión de la usuaria): el contador +/− de piezas y "Reponer".
- Trazabilidad: RF-10, RF-11 · CU-05 · RN-05.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-02` (caché de la vitrina), `02-03`, `HU-06`, `HU-07`.

## Requisitos
1. **Mis productos por pestaña (RF-11):** el vendedor ve sus publicaciones de una pestaña (`active`, `sold` o `soldOut`), de la más nueva a la más vieja, con su estado y su fecha de publicación (la app muestra "Publicado hace N días"). Se pagina por cursor (hasta 50 por página).
2. **Conteos de las pestañas:** cada respuesta trae cuántas hay en Activos, Vendidos y Agotados.
3. **Estados (RF-10):** Activos = disponibles y pausadas (`active` y `hidden`); Agotados = sin piezas (`soldOut`); Vendidos = marcadas como vendidas. Las quitadas por moderación no aparecen.
4. **Marcar como vendido:** solo el dueño (RNF-03; otra persona recibe `404 listings.product_not_found`). La publicación pasa a `sold` con la fecha de la venta. Sale de la vitrina, del chat y de las ofertas, y la vitrina se borra de la caché. Es idempotente.
5. **Una vendida no se edita, no se pausa y no se reactiva:** responde `409 listings.product_sold`; si no fuera así, reactivarla la "des-vendería". Sí se puede eliminar (HU-07).
6. **El dueño sigue viendo su vendida** en el detalle (`GET /products/{id}`, HU-11); para cualquier otra persona es `404`, igual que una pausada.
7. **Cambio automático según los pedidos (RN-05):** los pedidos no existen todavía. `NOTE (HU-23)`: al completar un pedido se descuentan las piezas y, sin piezas, la publicación pasa a vendida.

RNF que aplican: RNF-03 (solo el dueño), RNF-05 (la vitrina anónima sigue sin vendedor).

## Diseño
- **Datos:** `db/015-publicacion-vendida.js`, que vuelve a escribir el validador completo de `products` (lección de `db/012`: `collMod` reemplaza el anterior) con `sold` en el `enum` de `status` y el campo opcional `soldAt` (fecha). `MongoSchema.ExpectedVersion = 15`. Sin índices nuevos: "Mis productos" ya usa `ix_seller_id`, y un vendedor tiene pocas publicaciones.
- **Repositorio (`IProductRepository`, Mongo e InMemory):**
  - `FindBySellerAsync` recibe la pestaña y el cursor;
  - `CountBySellerAsync` cuenta las tres pestañas en una sola consulta (`$group` por pestaña);
  - `MarkSoldAsync(id, sellerId, now)`: el dueño va en el filtro (como en HU-07) y la publicación no debe estar quitada;
  - `UpdateDetailsAsync` y `SetPausedAsync` excluyen `sold` en su filtro.
- **Servicio:** `ListingService.MineAsync` (pestaña, cursor y conteos) y `MyListingsService.MarkSoldAsync`. Cuando el filtro no encuentra la publicación, el servicio la lee para distinguir `409 listings.product_sold` (es suya y está vendida) de `404` (no existe o no es suya).
- **Controller:** `GET /products/mine` (amplía `ProductsController`) y `POST /products/{id}/sold` (`MyProductsController`).
- **Detalle (HU-11):** `ListingService` deja ver una `sold` a su dueño, igual que una pausada.
- **Pendientes en el código:** `NOTE (HU-23)` en `MarkSoldAsync` (venta automática con los pedidos), `NOTE (HU-52)` en `ProductStatuses` (Reservada) y `NOTE (HU-25)` en `MineAsync` (en Vendidos, el comprador y el monto real los da el pedido).

## Contratos
- **Fila cambiada** en [`docs/endpoints.md`](../../../docs/endpoints.md): `GET /api/v1/products/mine?tab=&cursor=&limit=` → `MyProductPage { items, nextCursor, total, counts { active, sold, soldOut } }`. Sin `tab` se devuelve lo de hoy: todas menos las quitadas, así la app publicada no se rompe.
- **Fila nueva:** `POST /api/v1/products/{id}/sold` → `ProductResponse` (`status: sold`).
- **`ProductResponse` agrega `soldAt`** (`null` si no está vendida). Es un cambio aditivo: se actualiza `ContractTest/Fixtures/product.json` y se avisa a la app.
- **Errores** en [`docs/errores.md`](../../../docs/errores.md):
  - nuevo: `listings.product_sold` (409, "Esta publicación ya se vendió.");
  - nuevo: `listings.invalid_tab` (400) para una pestaña desconocida;
  - se reutilizan `listings.invalid_cursor` y `listings.product_not_found`.

## Tests
- `TestShared/Listings/ListingRepositoryContract.cs` (en memoria y en MongoDB real):
  - cada pestaña trae solo sus estados y pagina con cursor;
  - los conteos;
  - marcar vendida solo con el dueño;
  - una vendida no se edita ni se pausa.
- `DatabaseTest/SchemaScriptTests.cs`: el validador acepta `sold` con `soldAt` y sigue rechazando un estado desconocido; `015` es idempotente.
- `UnitTest/Listings/MyListingsServiceTests.cs`:
  - vender borra la vitrina de la caché;
  - 409 al editar, pausar o reactivar una vendida;
  - 404 si es de otra persona.
- `IntegrationTest/Listings/MyProductsTabsFlowTests.cs`:
  - publicar, pausar, dejar sin piezas (por el repositorio) y vender: cada una en su pestaña, con sus conteos;
  - la vendida sale de la vitrina y el dueño la sigue viendo en el detalle;
  - pestaña desconocida → 400.
- `SecurityTest/Auth/MyListingsAuthTests.cs`: `POST .../sold` con 401 sin sesión, 404 para otra persona y 403 con una cuenta suspendida; otra persona no ve una vendida en el detalle.
- `ContractTest/ListingsContractTests.cs`: `MyProductPage` (fixture nuevo `my-product-page.json`) y `product.json` con `soldAt`.

## Criterios
- [x] Pestañas y conteos (RF-10, RF-11): `MyProductsTabsFlowTests` (4, también la vendida fuera del chat y de las ofertas) y la batería del repositorio (`BySeller_ByStatus_PagesWithACursor`, `CountBySeller_GroupsByStatus_WithoutRemoved`) en verde.
- [x] Marcar como vendido solo por el dueño (RNF-03): `MyListingsAuthTests` (la ruta `sold` en las 4 teorías, `ASoldProduct_IsOnlyVisibleToItsOwner` y `SomeoneElse_OnASoldProduct_Gets404_Not409`) en verde.
- [x] Una vendida no vuelve a la vitrina por pausar o reactivar: `ASoldProduct_IsNotEditedPausedOrResumed_ButCanBeDeleted` y `MarkSold_OnlyTheOwner_KeepsTheFirstDate_AndThenItCannotBeEditedOrPaused` en verde. Mutación: permitir pausar una vendida o quitar la vendida del detalle del dueño → 3 Unit, 1 Integration y 2 Security en rojo.
- [x] RN-05 y "Reservada" quedan con `NOTE (HU-23)` y `NOTE (HU-52)` en el código, esta spec, `task.md` y `Memoria.md` (T35).
- [x] Build Release 0/0 · Unit 449 · Integration 153 · Security 169 · Contract 16 (100 %, con main del 10-Nov-2026 fusionado: HU-14, HU-53 y HU-23) · `check_endpoints` OK (66) · `check_shared_sections` OK · critic PASS. `db/015` probado contra un `mongod` 8 temporal (001→015 dos veces; acepta `sold`, rechaza un estado desconocido). La última página llena ya no da `nextCursor` (revisión del PR #61). `DatabaseTest` en el CI.

## Límites
- **"Reservada":** llega con los apartados y los pedidos. `NOTE (HU-52)`, `NOTE (HU-23)`.
- **Venta automática al llegar a 0 piezas por pedidos (RN-05):** `NOTE (HU-23)`.
- **Historial de "Vendidos" con comprador, monto y calificación** (la cabecera "N ventas · $total" de la maqueta): `NOTE (HU-25)`. Mientras tanto, la app suma el precio publicado de las vendidas.
- **Contador +/− de piezas y "Reponer":** HU-50.
- **Vender no se deshace** (la maqueta no ofrece "Deshacer").

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 09-Nov-2026
- **Detalle:**
  - revisión del PR #61 (10-Nov-2026): el script pasó de `013` a `015` y la versión a `0.5.0-alpha.16`, porque favoritos (HU-53) y pedidos (HU-23) tomaron `013`/`014`; `favorites.json` lleva `soldAt`; "Mis productos" pide una de más para no dar un cursor a una página vacía; detallada desde Jira (SCRUM-25), la maqueta (`Mis-productos`) y HU-07. La usuaria decidió el 09-Nov-2026: estados como la maqueta, "Marcar como vendido" con el estado `sold`, y las piezas (+/− y Reponer) en HU-50; aprobó D1–D5 de `plan.md`.

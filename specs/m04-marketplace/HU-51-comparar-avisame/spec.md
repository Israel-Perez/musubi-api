# HU-51 — Comparar opciones y pedir aviso de disponibilidad

**HU / Jira / Sprint / Módulo / Contraparte:** HU-51 / SCRUM-70 / Sprint 5 (14 nov – 1 dic 2026) / M-04 / [musubi-app:HU-51](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-51-comparar-avisame)

## Contexto
Jira (SCRUM-70): Como **comprador**, quiero **comparar un producto con publicaciones similares y pedir un aviso cuando vuelva a estar disponible**, para **elegir la mejor opción y no perder un producto agotado**.

- Maqueta: `Producto` ("Compara antes de comprar", "Avísame" / "Te avisaremos" y "Ver parecidos" de una agotada).
- Origen: maqueta (sección 3, G-04). Trazabilidad: RNF-03, RNF-05, RNF-11, RNF-15.
- **Decisiones de la usuaria (16-Nov-2026):**
  - **"Similar" es la regla de la maqueta:** misma categoría del campus; primero el mismo modelo y después el precio más cercano (sin el modelo de ML de HU-35).
  - **Las dos secciones conviven en `Producto`:** el comparador (HU-51) y "Productos parecidos" (HU-35).
  - **El aviso se usa una vez:** al avisar se borra; si se vuelve a agotar, se pide otro.

**Depende de:** `02-01`, `02-03`, `HU-11` (detalle), `HU-50` (piezas), `HU-57` (bloqueos), `HU-27` (reputación), `HU-31` (avisos), `HU-12`/`HU-29` (la cola de coincidencias).

## Requisitos
1. **R1 · Comparar:** `GET /products/{id}/compare` da hasta 5 publicaciones disponibles de la misma categoría y campus, sin la misma, sin las mías y sin las de personas bloqueadas (HU-57). Primero las del mismo modelo (comparten un código del nombre, como "991ES"), luego el precio más cercano y luego la más nueva. Cada una trae quien vende con su calificación, sus opiniones y sus ventas (sin las ventas si las oculta, HU-04).
2. **R2 · Avísame:** `PUT /products/{id}/stock-alert` registra el interés en una publicación **agotada** que no es mía; `GET` dice si ya lo tengo y `DELETE` lo quita (idempotente). Hasta 20 por persona; vence a los 90 días.
3. **R3 · Volvió:** cuando una agotada vuelve a tener piezas (quien vende repone o se cancela un pedido o un apartado), a cada persona con el aviso le llega "¡Volvió lo que esperabas!" (historial "Coincidencias" y push) y su aviso se borra. Nunca entre personas bloqueadas.
4. **R4 · Privacidad:** los avisos son privados (nadie ve quién los pidió) y se borran con la cuenta (HU-46).

## Diseño
- `Modules/Listings/`:
  - `ProductComparisonService` (R1) y `ModelCodes` (el mismo modelo).
  - `StockAlertService` (R2 y R4) y `Data/StockAlertRepositories.cs` (`stockAlerts`).
  - `IProductRestocks` (contrato hacia M-06) e `IStockAlertDirectory` (la toma de los avisos).
- `Modules/Matching/BackInStock.cs`: la cola, `BackInStockNotifier` y su worker (R3), como `ProductMatchWorker`.
- `ProductStock.ReleaseAsync` y `MyListingsService` (cambiar piezas y reactivar) encolan la revisión.
- Colección `stockAlerts` (`db/029-avisos-disponibilidad.js`); el validador de `notifications` admite `back_in_stock`.

## Contratos
Filas de HU-51 en [`docs/endpoints.md`](../../../docs/endpoints.md) (M-04), errores `listings.product_available`, `listings.own_product` y `listings.too_many_stock_alerts` en [`docs/errores.md`](../../../docs/errores.md), `stockAlerts` en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md) y fixtures `ContractTest/Fixtures/product-comparison.json` y `stock-alert.json`.

## Tests
- `UnitTest/Listings/ProductComparisonTests.cs`: códigos de modelo, orden, límite, exclusiones y ventas ocultas.
- `UnitTest/Listings/StockAlertServiceTests.cs`: solo agotadas, no las mías, el límite, idempotencia y borrado con la cuenta.
- `UnitTest/Matching/BackInStockTests.cs`: avisa una vez, solo si volvió, nunca a bloqueados ni a quien vende.
- `TestShared/Listings/StockAlertRepositoryContract.cs` (+ `InMemory` en UnitTest y `Mongo` en DatabaseTest).
- `IntegrationTest/Listings/CompareAndStockAlertTests.cs`: comparar; "Avísame" → reponer → aviso con destino `product`; una sola vez.
- `SecurityTest/Auth/StockAlertAuthTests.cs`: 401 sin sesión y 404 de otro campus.
- `ContractTest`: la forma de las dos respuestas nuevas.
- `DatabaseTest/SchemaScriptTests.cs`: `029` aplicado y `ux_user_product`.

## Criterios
- [x] El detalle muestra publicaciones similares comparadas por reputación del vendedor, ventas y precio → `ProductComparisonTests`, `CompareAndStockAlertTests.Compare_*`.
- [x] En un producto agotado, "Avísame" registra el interés → `StockAlertServiceTests`, `CompareAndStockAlertTests.StockAlert_*`.
- [x] Al volver a tener existencias llega una notificación push → `BackInStockTests`, `CompareAndStockAlertTests.Restock_*`.
- [x] Build Release 0/0 · Unit 869, Integration 196, Security 273 y Contract 29 al 100 % · `check_endpoints` OK (116) · critic PASS.

## Límites
- El "mismo modelo" es una regla del nombre: dos publicaciones comparten una palabra de al menos 3 caracteres con un dígito ("991es", "ti84"); un nombre sin código no tiene modelo.
- Se leen las 50 más nuevas de la categoría para elegir 5; en un campus más grande la elección se queda entre las recientes.
- El puntaje "Para ti", las insignias y la barra de precio se calculan en la app con estas 5 (son de presentación).
- Sin sesión no hay comparador: compara a quienes venden y un visitante no los ve (RNF-05).
- Un aviso de una publicación que se quita o se vende sin volver se borra solo a los 90 días.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 16-Nov-2026 (detallado e implementado, con las decisiones de la usuaria; API `0.5.0-alpha.37`, esquema 29)

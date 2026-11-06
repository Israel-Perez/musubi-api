# HU-11 — Ver el detalle de un producto

**HU / Jira / Sprint / Módulo / Contraparte:** HU-11 / SCRUM-28 / Sprint 2 (12 – 23 oct 2026) / M-04 / [musubi-app:HU-11](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-11-detalle-producto)

## Contexto
Jira (SCRUM-28): Como **comprador**, quiero **ver el detalle de un producto**, para **decidir si me interesa antes de contactar al vendedor**.

- Maqueta: `Inicio-visitante`, `Se-busca-visitante`, `Producto-visitante`, `Visitante-modal`, `Producto`, `Perfil-vendedor`, `Chat-Valeria`.
- Trazabilidad: RF-14 · CU-06 · RNF-19.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-06`.

## Requisitos
1. El detalle muestra fotografías, descripción, precio, estado, cantidad, datos del vendedor y su reputación.
2. Desde el detalle se puede contactar al vendedor (chat) o comprar.
3. La interfaz distingue visualmente los productos publicados de las solicitudes de compra (RNF-19).

RNF que aplican: RNF-19.

## Diseño
- `ListingService.DetailAsync` y `GET /products/{id}` en `ProductsController` (M-03, junto a la vitrina). Detalle en [`plan.md`](plan.md).
- **Público** como la vitrina (HU-48): sin sesión solo se ven las disponibles o agotadas, y sin el vendedor (RNF-05).
- **Con sesión:**
  - trae `seller { id, name }`;
  - la app completa la foto, la calificación y la carrera con `GET /profiles/{id}` (HU-04/HU-05), así M-03 no lee perfiles;
  - la de otro campus responde 404.
- **La pausada solo la ve su dueño. La quitada no la ve nadie.** En todos esos casos la respuesta es la misma, `404 listings.product_not_found`: no se revela cuál aplica (OWASP API1).

## Contratos
- Fila `GET /api/v1/products/{id}` en [`docs/endpoints.md`](../../../docs/endpoints.md).
- Error `listings.product_not_found` en [`docs/errores.md`](../../../docs/errores.md).
- Fixture `ContractTest/Fixtures/product-detail.json`.

## Tests
- `IntegrationTest/Listings/ListingFlowTests.cs`: el detalle completo, el vendedor solo con sesión, y un producto desconocido responde 404.
- `SecurityTest/Auth/ListingsAuthTests.cs`: pausada (solo su dueño), quitada (nadie) y otro campus (404 con sesión).
- `ContractTest/ListingsContractTests.cs`: forma de `ProductDetailResponse`.

## Criterios
- [x] El detalle muestra fotografías, descripción, precio, estado, cantidad y datos del vendedor → `Detail_ShowsEverything_AndTheSellerOnlyWithASession`. La reputación sale de `GET /profiles/{id}` (HU-27 la llena).
- [x] Desde el detalle se puede contactar al vendedor → `POST /conversations` (HU-20) con el id del detalle. "Comprar" llega con HU-23.
- [ ] La interfaz distingue los productos de las solicitudes (RNF-19) → `musubi-app:HU-11`.
- [x] Build Release 0/0 · Unit 174, Integration 96, Security 83 y Contract 8 · `check_endpoints` OK · critic PASS.

## Límites
- Contar las vistas para "Populares": `NOTE (HU-49)`.
- "Compara antes de comprar": `NOTE (HU-51)`. Apartar: `NOTE (HU-52)`. Guardar: `NOTE (HU-53)`.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia (API). La app queda pendiente (musubi-app:HU-11).
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 06-Nov-2026
- **Detalle:** 29-Oct-2026: spec ligera (SCRUM-86). 06-Nov-2026 (SCRUM-28): API implementada para que el chat abra el detalle ("Ver", HU-21) y el detalle abra el chat ("Mensaje").

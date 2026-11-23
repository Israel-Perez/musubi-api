# HU-58 — Ver un precio sugerido y saber si un precio es bueno (ML)

**HU / Jira / Sprint / Módulo / Contraparte:** HU-58 / SCRUM-98 / Sprint 5 (14 nov – 1 dic 2026) / M-11 / [musubi-app:HU-58](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m11-recommendations/HU-58-precio-sugerido)

## Contexto
Jira (SCRUM-98):
- Como **vendedor**, quiero **ver un precio sugerido al publicar**, para **poner un precio justo y vender más rápido**.
- Como **comprador**, quiero **saber si un precio es bueno comparado con productos parecidos**, para **decidir con confianza**.

- Pantallas: `Vender-3` (precio) y `Producto` → "Compara antes de comprar".
- Origen: ideas del 13-Nov-2026 para sumar ML a la app. Trazabilidad: lineamiento L-16i (al menos una función de ML).
- "Compara antes de comprar" ya tenía su veredicto con la regla de la maqueta: el precio frente a las opciones de la misma categoría (HU-51). HU-58 suma la estimación del modelo.
- **Decisiones (23-Nov-2026, para que la usuaria las revise):**
  - Se sugiere con **al menos 5 precios de la categoría** y **20 del campus**. Con menos, no se muestra nada.
  - Lo que **se pagó** en un pedido completado pesa **3 veces** lo publicado.

**Depende de:** `HU-35` (el texto como lo ve ML.NET), `HU-26` (pedidos completados), `HU-51` ("Compara antes de comprar").

## Requisitos
1. **R1 · Modelo:**
   - Es una regresión de **ML.NET** (SDCA) sobre el logaritmo del precio, una por campus.
   - Usa las palabras del nombre (TF-IDF de palabras y trigramas de letras), la categoría y el estado (`new`, `used:likeNew`, `used:good` o `used:fair`).
   - Entrena con las publicaciones disponibles, agotadas y vendidas y, con peso 3, con lo pagado en los pedidos completados.
2. **R2 · Rango:**
   - La estimación ± el error típico medido (cuantil 60 del error absoluto, de ±10 % a ±60 %).
   - En pesos redondos: de 5 en 5 hasta $100, de 10 en 10 hasta $1,000 y de 50 en 50 arriba.
3. **R3 · Sin inventar:** con menos de 5 precios de la categoría o 20 del campus, el rango es `null`.
4. **R4 · En segundo plano (RNF-11):**
   - El primer modelo se entrena al pedirlo.
   - Después de 30 minutos se responde con el vigente y se reentrena aparte.
   - Sin base, se queda el anterior.
5. **R5 · Medición fuera de línea:**
   - Cada entrenamiento aparta 1 de cada 5 publicaciones (por un hash estable de su id) y mide el MAE en pesos contra la mediana de la categoría.
   - Lo registra en los logs.
6. **R6 · Endpoints:**
   - `POST /products/price-suggestion` para lo que se va a publicar o editar.
   - `GET /products/{id}/price-estimate` para una publicación visible, con su veredicto (`good`, `fair` o `high`).
   - Nunca cambia un precio (RN-38).

## Diseño
- `Modules/Recommendations/PriceModel.cs`: `PriceModel.Train` y `Estimate`, `PriceSample`, `PriceRange` y `PriceEvaluation`.
- `Modules/Recommendations/PriceSuggestionService.cs`: `PriceModelStore`, `PriceSuggestionService`, DTOs, `PriceVerdicts` y `PricingErrors`.
- Contratos nuevos entre módulos:
  - `IPriceCatalog` (M-03, `Listings/PriceCatalog.cs`, con `IProductRepository.FindPricedAsync`);
  - `ISoldPrices` (M-08, con `IOrderRepository.FindCompletedByCommunityAsync`).
- `db/033-precios.js`: `orders.ix_community_status_id`. Esquema 33; API `0.5.0-alpha.44`.

## Contratos
- Filas `POST /api/v1/products/price-suggestion` y `GET /api/v1/products/{id}/price-estimate` de [`docs/endpoints.md`](../../../docs/endpoints.md).
- Error `recommendations.invalid_price_request` en [`docs/errores.md`](../../../docs/errores.md).
- Fixtures `price-suggestion.json` y `price-estimate.json`.

## Tests
- `UnitTest/Recommendations/PriceModelTests.cs`:
  - **MAE fuera de línea:** el modelo se equivoca menos que la mediana de la categoría. Con los precios sintéticos del campus, **$87.50 contra $717.90**, con 153 para entrenar y 39 apartados.
  - El rango sigue las palabras y el estado, y va en pesos redondos.
  - Con pocos precios no sugiere nada.
  - Lo pagado pesa más.
  - El veredicto.
  - El almacén une lo pagado con su publicación y se reentrena en segundo plano.
  - El servicio: validación, sin datos y la estimación.
- `TestShared/Listings/ListingRepositoryContract.cs` y `TestShared/Orders/OrderRepositoryContract.cs`, en memoria y en MongoDB real.
- `IntegrationTest/Recommendations/PriceSuggestionFlowTests.cs`, `SecurityTest/Auth/PricingAuthTests.cs`, `ContractTest/PricingContractTests.cs` y `DatabaseTest/SchemaScriptTests.cs`.

## Criterios
- [x] Una regresión de ML.NET entrenada con lo publicado y, sobre todo, con lo vendido estima un rango con la categoría, el estado y las palabras del nombre → `TheRange_FollowsTheWordsAndTheCondition_AndContainsTheTypicalPrice`, `WhatWasPaid_WeighsMoreThanWhatIsListed`.
- [x] Al publicar o editar se puede pedir "Precio sugerido: $X – $Y", sin cambiar el precio → `PriceSuggestionFlowTests`; en la app, la contraparte.
- [x] En `Producto`, el veredicto contra productos parecidos (regla de HU-51) y la estimación del modelo → `TheVerdict_ComparesThePriceWithTheRange`, `PriceSuggestionFlowTests`.
- [x] Con pocos datos de una categoría no se muestra nada → `WithFewPricesInTheCategory_OrInTheCampus_ItSuggestsNothing`.
- [x] Se reentrena en segundo plano y su error se mide fuera de línea (MAE) → `AfterThirtyMinutes_TheStoreAnswersWithTheCurrentModel_AndRetrainsInTheBackground`, `MeasuredOffline_TheModelBeatsTheCategoryMedian`.
- [x] Build Release 0/0 · Unit 1010, Integration 219, Security 345 y Contract 32 · `check_endpoints` OK (128) · critic PASS.

## Límites
- Con el volumen de un campus, el modelo es lineal (SDCA). Uno de árboles (FastTree) agregaría un paquete y no se justifica con estos datos.
- La medición del CI usa precios sintéticos; la de producción queda en los logs de cada entrenamiento.
- No sugiere precio para una oferta directa (HU-16): no tiene categoría.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 23-Nov-2026 (detallada e implementada)

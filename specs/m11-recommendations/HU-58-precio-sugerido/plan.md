# plan.md — HU-58 Ver un precio sugerido y saber si un precio es bueno (ML)

## 1. Componentes
- `Modules/Recommendations/PriceModel.cs` (entrenar, estimar y medir) y `PriceSuggestionService.cs` (almacén, servicio, DTOs y errores).
- `RecommendationsController`: `PriceSuggestion` y `PriceEstimate`.
- `Listings/PriceCatalog.cs` (`IPriceCatalog`) y `IProductRepository.FindPricedAsync` (índice `ix_community_status_id` de `products`).
- `ISoldPrices` en `OrdersModuleServiceCollectionExtensions.cs` e `IOrderRepository.FindCompletedByCommunityAsync`.
- `db/033-precios.js` y `MongoSchema.ExpectedVersion = 33`.

## 2. Decisiones (con su porqué)
- **Regresión lineal sobre el logaritmo:** los precios del campus van de $20 a $10,000. En logaritmo, una palabra como "graficadora" multiplica el precio en lugar de sumarle. SDCA viene en `Microsoft.ML`, sin paquete nuevo.
- **Lo pagado pesa 3:** es el precio real de mercado. Lo publicado puede estar inflado.
- **El rango sale del error medido**, no de un porcentaje fijo: con datos parecidos el rango es angosto y con datos dispersos, ancho.
- **Reentrenar en segundo plano:** la primera petición espera; las demás responden con el vigente (RNF-11).
- **`Transform` en lugar de `PredictionEngine`:** es seguro entre hilos y no necesita una clase de predicción.
- **Prueba fuera de línea por el id de la publicación:** su precio publicado y sus ventas caen del mismo lado, así no se mide con datos que el modelo ya vio.

## 3. Excepciones al stack
Ninguna (ML.NET ya estaba por HU-35).

## 4. Guardarraíles
- Con sesión: los precios del campus no son públicos (RNF-03). La estimación respeta la visibilidad del detalle (OWASP API1).
- Tope del cuerpo de 4 KB (API4).
- No expone precios individuales: solo un rango con al menos 5 precios.

## 5. Validación
Unit (MAE), Integration, Security, Contract y DatabaseTest (CI); prueba en el celular (T5).

## 6. Secuencia
T1 → T2 → T3 → T4 → T5.

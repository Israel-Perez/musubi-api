# plan.md — HU-51 Comparar opciones y pedir aviso de disponibilidad

## 1. Componentes
- `Modules/Listings/`:
  - `ProductComparisonService.cs` (con `ModelCodes`) y `StockAlertService.cs` (con `IProductRestocks` e `IStockAlertDirectory`);
  - `StockAlertsController` (las rutas `/products/{id}/stock-alert`) y la acción `compare` en `ProductsController`;
  - `Data/StockAlertRepositories.cs` (`Mongo*` e `InMemory*`);
  - cambios: `ProductStock.ReleaseAsync`, `MyListingsService` (piezas y reactivar), `ListingsErrors`, `ProductSummary.SellerName`.
- `Modules/Profiles/ProfileDirectory.cs`: `ProfileCard.Sales` (`null` si la persona oculta sus ventas).
- `Modules/Matching/BackInStock.cs`: `BackInStockQueue` (`IProductRestocks`), `BackInStockNotifier` y `BackInStockWorker`.
- `Modules/Notifications/NotificationKinds.cs`: `back_in_stock`.
- `db/029-avisos-disponibilidad.js`, `MongoSchema.StockAlerts` y `ExpectedVersion = 29`.

## 2. Decisiones (con su porqué)
- **Regla de la maqueta (usuaria):** es explicable ("mismo modelo", "más barato") y la maqueta ordena así; el ML de HU-35 sigue en "Productos parecidos".
- **La API elige y la app ordena:** las cuatro pestañas ("Para ti", Reputación, Ventas y Precio) cambian al instante sin otra llamada, y el puntaje es de presentación.
- **Un aviso por persona y publicación (`ux_user_product`):** pedirlo dos veces no duplica.
- **Se toma con `FindOneAndDelete` uno por uno:** con dos instancias, cada aviso sale una sola vez (usuaria: se usa una vez).
- **La revisión va en segundo plano (RNF-11, RNF-15):** cancelar un pedido o reponer piezas nunca falla por los avisos. El worker vuelve a leer la publicación: solo avisa si de verdad hay piezas.
- **No hace falta saber si estaba agotada:** un aviso solo se crea en una agotada y se borra al avisar.
- **El aviso vive en M-03 y la cola en M-06:** igual que las búsquedas guardadas (HU-12); Listings no depende de Notifications.

## 3. Excepciones al stack
Ninguna.

## 4. Guardarraíles
- Misma visibilidad que el detalle (`FindVisibleAsync`): otro campus, pausada o con un bloqueo responden 404 (OWASP API1).
- Hasta 20 avisos por persona y 90 días de vida (OWASP API4); el comparador lee como máximo 50 candidatas.
- El aviso es privado: no hay endpoint que diga quién lo pidió.
- Las ventas solo salen si "Mostrar mis ventas" está encendido (HU-04).

## 5. Validación
Unit, Integration, Security, Contract y DatabaseTest; prueba en el celular con la app (T5 de la contraparte).

## 6. Secuencia
Script y repositorio → servicio del aviso y endpoints → cola y notificador → comparador → documentación.

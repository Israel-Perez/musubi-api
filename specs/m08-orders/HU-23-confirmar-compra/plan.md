# plan.md — HU-23 Confirmar la compra de un producto

## 1. Componentes
- **Nuevo `Musubi.Api/Modules/Orders/`:**
  - `OrdersController` (`api/v1/orders`): `meeting-points`, crear, por conversación, detalle y pago; con un tope de 16 KB en los cuerpos.
  - `OrderService`, `OrdersErrors` y `OrdersDtos`.
  - `OrdersModuleServiceCollectionExtensions`, más `IOpenOrders` (para Listings) y `OrderAccountEraser` (HU-46).
  - `Data/OrderDocuments.cs` y `Data/OrderRepositories.cs`: `IOrderRepository` e `IMeetingPointRepository`, cada uno en Mongo y en memoria. La semilla en memoria es la misma que la de `db/014`.
- **Contratos entre módulos** (Modules/README: un módulo no lee las colecciones de otro):
  - `IConversationDirectory.FindAsync` → `ConversationSummary`;
  - `ProductSummary.Stock`;
  - `IOfferDirectory` / `OfferSummary` (nuevo, en PurchaseRequests);
  - `IProfileDirectory.FindCareerSlugAsync`.
- **Listings:** `MyListingsService.DeleteAsync` aplica RN-07 con `IOpenOrders`, y se agrega `ListingsErrors.ProductHasOrders`.
- **Datos:** `db/014-pedidos.js` (`orders`, `meetingPoints` y `counters`; esquema 14) y `MongoSchema.Orders`, `MeetingPoints` y `Counters`.

## 2. Decisiones
- **Sin IA (decisión de la usuaria, 10-Nov-2026):** el pedido sale de la conversación (RN-21) y no de un acuerdo. Así HU-23 funciona hoy con publicaciones y con ofertas aceptadas. HU-34 pondrá el acuerdo de los dos encima, sin cambiar el pedido.
- **Quien vende no crea el pedido (`403 orders.only_buyer`):** RN-16 dice que el vendedor lo *acepta*. Su confirmación es la aceptación de HU-24.
- **Uno abierto por conversación:** se revisa antes de crear y además lo asegura el índice único parcial `ux_conversation_open`. Así un doble toque no duplica ni gasta números.
- **No se apartan piezas al crear:** RF-34 aparta al aceptar (HU-24). RN-17 se valida contra lo disponible en ese momento.
- **Oferta directa:** como no tiene publicación ni piezas, la cantidad va de 1 a la que pidió la solicitud. Una oferta vinculada se valida contra su publicación.
- **Puntos de entrega (decisión de la usuaria):** los 14 edificios de la UTTT en una colección por campus. Cada edificio lleva sus carreras para recomendarlo, y la usuaria aprobó la relación carrera → edificio. Además hay un detalle libre de hasta 120 caracteres, que se modera.
- **Ventana de entrega:** de 30 minutos a 14 días, entre las 7:00 y las 21:00 del campus (UTC-6, como las fechas de HU-13).
- **Número `MS-NNNN`:** un `counters` con `$inc` atómico por campus, que empieza en 1001 (la maqueta muestra `#MS-2481`).
- **Pago (D-01, decisión de la usuaria):** solo `cash`. `transfer` responde `409 orders.payment_unavailable` hasta HU-55, y la respuesta lleva `transferAvailable: false` para que la app la muestre deshabilitada como en la maqueta.
- **RN-07 primero revisa el dueño:** así a otra persona se le responde 404 y no se le revela que la publicación tiene pedidos.
- **Borrar la cuenta (R11):** los pedidos no se borran, porque la otra parte conserva su historial. Los abiertos se cancelan (`reason: account_deleted`) y el nombre queda "Usuario eliminado".

## 3. Excepciones al stack
Ninguna.

## 4. Guardarraíles
- El dueño y el estado van en el filtro de cada escritura (`SetPaymentAsync`: id, comprador y `pending`). Se lee para dar el error correcto, pero la escritura no depende de esa lectura (OWASP API1).
- 404 para quien no es parte (RNF-03). Cuenta no activa: 403 (RN-01). Bloqueos de HU-57 en cualquier dirección.
- Tope de 16 KB en `POST /orders` y `PUT /orders/{id}/payment` (OWASP API4).
- Moderación del detalle (02-07). El resto viene del catálogo.

## 5. Validación
- Unit 443 (50 nuevas en `OrderServiceTests`, la batería `InMemoryOrderRepositoryTests` y RN-07 en `MyListingsServiceTests`), Integration 149, Security 161 y Contract 16.
- `MeetingPoints_SeedMatchesTheScript_AndEveryCareerExists`: la semilla en memoria y `db/014` no se desalinean, y las 38 carreras tienen edificio.
- Mutación: quitar "solo sus partes" en `GetAsync` deja 1 Unit, 1 Integration y 1 Security en rojo.
- `DatabaseTest`: la batería contra Mongo real y `Orders_HaveTheirIndexes_SeedTheCampusPoints_AndRejectAnInvalidOrder` corren en el CI (job `database`).

## 6. Secuencia
1. Script y documentos → repositorios y su batería.
2. Contratos entre módulos → servicio → controller → RN-07 y borrado de cuenta.
3. Pruebas Unit, Integration, Security y Contract → documentación (endpoints, errores, modelo, trazabilidad y versionado) → Memoria.

# plan.md — HU-11 Ver el detalle de un producto

## 1. Componentes
- `ListingService.DetailAsync`.
- `ProductsController.Detail` (`GET /products/{id}`, `[AllowAnonymous]` y `[ConditionalGet]`).
- `ProductDetailResponse` y `ProductSellerResponse`.
- `ListingsErrors.ProductNotFound`.
- Reutiliza `IProductRepository.FindByIdAsync` (HU-20).

## 2. Decisiones (con su porqué)
- **Público, como la vitrina:** el visitante (HU-48) explora sin cuenta. El vendedor solo con sesión (RNF-05).
- **El vendedor viene como `{ id, name }`:** la tarjeta completa de la maqueta (foto, calificación, ventas y carrera) sale de `GET /profiles/{id}`. Así M-03 no lee las colecciones de M-02, y "Quién te ve" (HU-04) se respeta en un solo lugar.
- **Un solo 404 para todo lo que no se debe ver:** inexistente, quitada, pausada para otros u otro campus con sesión (OWASP API1).
- **Sin caché de salida:** la respuesta cambia con la sesión. `ETag` sí: la app revalida con 304.

## 3. Validación
Suites al 100 %; en AWS, abrir el detalle sin sesión (sin vendedor) y con sesión (con vendedor y "Mensaje").

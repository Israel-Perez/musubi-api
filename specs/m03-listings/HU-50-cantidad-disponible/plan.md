# plan.md — HU-50 Manejar la cantidad disponible de un producto

1. **Componentes:**
   - `IProductRepository.SetStockAsync` (Mongo e InMemory);
   - `MyListingsService.SetStockAsync`, `MyProductsController.SetStock`, `SetStockRequest` y `ListingsErrors.InvalidStockChange`;
   - pruebas en `TestShared/`, `UnitTest/`, `IntegrationTest/`, `SecurityTest/` y `ContractTest/`.
2. **Decisiones:**
   - **Ruta que fija el número** (`PUT { stock }`, como pidió el PO), no `+1/−1`: la app manda una sola petición por cambio.
   - **El choque con un pedido se acepta y se documenta** (decisión de la usuaria).
   - **El estado se decide en la misma escritura**, con el filtro `Editable` de HU-07/08 (dueño, ni quitada ni vendida).
   - **Una pausada sigue pausada:** cambiar piezas no la regresa a la vitrina.
   - **Mismo código de error con el rango 0 a 10.**
3. **Excepciones al stack (00-01):** ninguna; sin script de base de datos.
4. **Guardarraíles:** dueño en el filtro (OWASP API1), cuerpo de hasta 1 KB, rango validado y vitrina borrada de la caché.
5. **Validación:** las pruebas de la spec, los guardarraíles y `DatabaseTest` en el CI.
6. **Secuencia:** repositorio y batería → servicio y controller → pruebas → documentación → app.

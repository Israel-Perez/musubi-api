# plan.md — HU-07 Editar o eliminar mis publicaciones

1. **Componentes:**
   - `Modules/Listings/MyListingsService.cs` (nuevo) y `ProductRules.cs` (reglas extraídas de `ListingService.PublishAsync`, mismo comportamiento);
   - `Modules/Listings/MyProductsController.cs` (nuevo, 4 rutas);
   - `ProductsDtos.cs` (`UpdateProductRequest`), `ListingsErrors.cs` (`ProductNotFound`), `ListingsModuleServiceCollectionExtensions.cs` (registro);
   - `Data/ListingRepositories.cs`, `MongoListingRepositories.cs` e `InMemoryListingRepositories.cs` (3 métodos);
   - pruebas en `TestShared/`, `UnitTest/`, `IntegrationTest/`, `SecurityTest/` y `ContractTest/`;
   - sin script en `db/` (el esquema ya acepta `hidden`).
2. **Decisiones** (D1–D6 aprobadas por el usuario el 05-Nov-2026):
   - **D1 · `PUT` con el conjunto completo de campos editables, no `PATCH`:** la app ya tiene la publicación completa de `GET /products/mine` y la manda entera al guardar. Se validan las mismas reglas que al publicar, sin la ambigüedad de un `null` en `PATCH` ("¿lo borro o no lo mandaste?"). Es la misma forma que `PUT /profiles/me` (HU-04).
   - **D2 · Eliminar es un borrado real, no un estado:** la maqueta dice "Esta acción no se puede deshacer". Un estado `deleted` exigiría un script `db/006` (el `enum` del `$jsonSchema`) y filtrarlo en cada consulta, y los datos se quedarían en Atlas M0 (512 MB) sin uso (R11, R12). Lo que debe conservarse ya tiene su copia: la conversación guarda el nombre (HU-20) y los pedidos guardarán `item{productId, title, price}` (HU-23).
   - **D3 · Las fotos se borran de S3 al eliminar, salvo las que otro módulo sigue mostrando:** sin la publicación nadie las ve, y S3 cobra por guardarlas. Se borran por llave (`IMediaStorage.DeleteAsync`), no por prefijo, porque el prefijo es de todas las publicaciones del vendedor. Una oferta vinculada (HU-16) copia la llave de la portada, así que esa foto se queda (decisión del usuario, 09-Nov-2026): Listings pregunta por `IListingPhotoReferences`, que implementan las ofertas con el índice `ix_seller` (sin script nuevo).
   - **D4 · Las fotos no se editan en esta HU:** la maqueta no muestra `Vender-1` al editar. Si el equipo lo quiere después, se agrega con su propia HU.
   - **D5 · Pausar y reactivar entran en la API**, aunque la maqueta no lo tenga: el estado `hidden` ya existe para eso, cuesta dos rutas pequeñas y HU-08 lo mostrará como "Inactiva". La app decide en su PR si agrega "Pausar" al menú de `Mis-productos` (lineamiento 4: se puede agregar).
   - **D6 · Las piezas no se editan:** `Vender-4` en modo edición muestra la cantidad, pero "Reponer" (+/−) es de HU-50. Si la edición sobrescribiera `stock`, pisaría el descuento atómico de un pedido (R7). Propuesta para la app: mostrarla sin poder cambiarla.
   - **Solo se modera lo que cambió:** el texto que no cambió ya pasó por la moderación al publicar; así no se cobra dos veces a AWS por el mismo texto.
   - **404 y no 403 para otra persona:** con 403 se sabría que la publicación existe (OWASP API1), igual que el chat (`chat.conversation_not_found`).
   - **Rutas `POST /{id}/pause` y `/{id}/resume`, y no `PUT /{id}/status`:** una ruta de estado genérica adelantaría el diseño de "Marcar como vendido" (HU-08).
3. **Excepciones al stack (00-01):** ninguna; no hay paquetes nuevos.
4. **Guardarraíles:**
   - el dueño va en el filtro de cada escritura (`_id` + `sellerId` + `status != removed`), sin ventana entre leer y escribir;
   - el cuerpo de la edición tiene un tope de 16 KB (`RequestSizeLimit`, OWASP API4);
   - una cuenta no activa responde 403 `auth.forbidden`, igual que al publicar;
   - los textos pasan por la moderación antes de guardarse; si AWS falla, 503 y nada cambia;
   - la vitrina anónima se borra de la caché de salida después de cada escritura que tuvo efecto;
   - `[Authorize]` explícito en `MyProductsController` (critic B3 y `ExplicitAuthTests`).
5. **Validación:** las pruebas de la `spec.md`, build Release 0/0, las cuatro suites, `check_endpoints`, `check_shared_sections`, critic, Semgrep en local, y los sub-agentes `security-reviewer` y `traceability-clerk`. La prueba en el celular va con el PR de la app.
6. **Secuencia:** repositorio y su batería → `ProductRules` (refactor sin cambio de comportamiento) → servicio → controller → pruebas → documentación (`endpoints`, `errores`, `modelo-datos`, `versionado`, `trazabilidad`, `Memoria.md`) → revisión → PR.

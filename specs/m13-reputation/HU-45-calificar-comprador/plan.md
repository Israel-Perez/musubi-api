# HU-45 — Calificar al comprador al terminar un pedido (plan, API)

Se implementó junto con HU-27: misma ruta, colección y reputación.

1. **T1 — Spec:** decisiones de la usuaria (un promedio, 7 días, "A tiempo" con "Puntual", chips de quien compra). ✅
2. **T2 — Datos:** `db/020-calificaciones.js`, `RatingDocument`, `IRatingRepository`, `orders.ratings` y `profiles.stats.ratingSum`/`onTimeCount`. ✅
3. **T3 — Servicio y rutas:** `RatingService`, `RatingsController`, `IOrderRatingGate` e `IProfileRatings`. ✅
4. **T4 — Pruebas:** unitarias, contratos de repositorio, integración, seguridad, contrato JSON y script de la base. ✅
5. **T5 — Documentación y versión:** catálogo (85 filas), errores, modelo de datos, trazabilidad, versionado (`0.5.0-alpha.25`, esquema 20) y Memoria. ✅

**Decisiones:**
- **D1 · Se marca el pedido antes de guardar la calificación:** la marca es atómica (`ratings.<rol>At` no existe), así que dos toques a la vez dejan una sola; `ux_order_rater` es la segunda red.
- **D2 · Promedio guardado:** `$inc` de suma y número, y luego `ratingAvg` con un pipeline que lo deriva de lo guardado. Lo leen el perfil, las tarjetas y las ofertas sin cambiar.
- **D3 · Comentario de 280 caracteres:** la maqueta no fija un tope; 280 alcanza para "Muy puntual en la biblioteca y la calculadora funciona perfecto." con margen.

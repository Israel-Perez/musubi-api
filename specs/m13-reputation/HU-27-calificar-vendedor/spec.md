# HU-27 — Calificar al vendedor al terminar un pedido

**HU / Jira / Sprint / Módulo / Contraparte:** HU-27 / SCRUM-46 / Sprint 4 (9 – 20 nov 2026) / M-13 / [musubi-app:HU-27](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m13-reputation/HU-27-calificar-vendedor)

## Contexto
Jira (SCRUM-46): Como **comprador**, quiero **calificar la experiencia al terminar un pedido**, para **ayudar a otros usuarios a decidir**.

- Maqueta: `Calificar` (estrellas, "¿Qué destacas?", "Cuéntale a la comunidad", "Enviar calificación") y "Opiniones" de `Perfil-vendedor`.
- Trazabilidad: RF-35, RF-36 · CU-19 · RN-24, RN-25, RN-26.
- HU-27 y HU-45 comparten la ruta, la colección y la reputación: se implementaron juntas (HU-45 es la otra mitad).
- **Decisiones de la usuaria (12-Nov-2026):**
  - un solo promedio con lo recibido como quien vende y como quien compra (cada calificación guarda su rol);
  - 7 días después de completado para calificar;
  - "A tiempo" de `Perfil-vendedor` es el porcentaje de calificaciones con "Puntual";
  - quien compra recibe sus propios chips: Puntual, Buena comunicación, Amable y Pagó lo acordado.

**Depende de:** `HU-24` (pedido completado con las dos confirmaciones) y `HU-05` (perfil público).

## Requisitos
1. **R1 · Calificar (RN-24, RN-25):** `POST /api/v1/orders/{id}/rating` con `stars` (1 a 5), `tags` opcionales y `comment` opcional (hasta 280 caracteres, moderado). Solo las partes de un pedido **completado**, una vez cada una, hasta 7 días después.
2. **R2 · Chips por rol:** a quien vende, `on_time`, `as_described`, `good_communication`, `fair_price`, `friendly`; a quien compra, `on_time`, `good_communication`, `friendly`, `paid_as_agreed`. Se guardan en el orden de la lista.
3. **R3 · Reputación (RF-36):** cada calificación suma a la de quien la recibe: `stats.ratingAvg` y `ratingCount` (perfil, tarjetas y publicaciones) y `stats.onTime` ("A tiempo", 0 a 100). Sin calificaciones sigue "Sin calificaciones" (RN-26).
4. **R4 · Opiniones:** `GET /api/v1/profiles/{id}/ratings`, de la más nueva a la más vieja, con el nombre corto de quien calificó; mismas reglas que el perfil (campus, cuenta activa y bloqueos).
5. **R5 · El pedido dice si ya calificaste:** `OrderResponse.ratedByMe` y `rateUntil` (o `null` si no está completado).
6. **R6 · Borrado de cuenta (HU-46, R11):** se borran las que recibió; en las que dio queda "Usuario eliminado".

RNF que aplican: RNF-03 (solo las partes), RNF-07 (moderación del comentario).

## Diseño
- **Datos:** `ratings` (`db/020-calificaciones.js`): `ux_order_rater` (único) e `ix_rated_id`. `orders.ratings { buyerAt, sellerAt }`; `profiles.stats.ratingSum` y `onTimeCount`.
- **M-13:** `RatingService` (`RateAsync`, `ListAsync`), `RatingsController` y `IRatingRepository` (Mongo y memoria).
- **Contratos con otros módulos:** `IOrderRatingGate` (M-08: leer el pedido y marcar quién calificó, atómico) e `IProfileRatings` (M-02: `$inc` y luego `ratingAvg = ratingSum / ratingCount`).
- **Orden:** se valida la entrada, luego el estado (completado, ya calificado, plazo), se modera el comentario, se marca el pedido y al final se guarda la calificación y se suma al perfil.

## Contratos
- Filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md): `POST /api/v1/orders/{id}/rating` y `GET /api/v1/profiles/{id}/ratings`; `GET /profiles/{id}` suma `stats.onTime` y `OrderResponse` suma `ratedByMe` y `rateUntil`.
- Errores en [`docs/errores.md`](../../../docs/errores.md): `ratings.invalid_stars`, `ratings.invalid_tags`, `ratings.invalid_comment`, `ratings.not_completed`, `ratings.expired`, `ratings.already_rated` y `ratings.invalid_cursor`.
- Fixtures: `rating.json`, `profile-rating-page.json`, `profile-rating.json`; `order.json` y `public-profile.json` actualizados.

## Tests
- `UnitTest/Reputation/RatingServiceTests.cs` (15): las dos partes, una vez, solo completado, plazo de 7 días, estrellas, chips por rol, comentario y moderación, ajenos y suspendidos, un solo promedio, opiniones con cursor y reglas del perfil.
- `TestShared/Reputation/RatingRepositoryContract.cs` (3), `OrderRepositoryContract.MarkRated_*` y `ProfileRepositoryContract.AddRating_*` (memoria y MongoDB real).
- `IntegrationTest/Orders/OrderLifecycleFlowTests.cs`: `BothRateACompletedOrder_OnceEach_AndTheProfileShowsTheReputation`.
- `SecurityTest`: 401 en las dos rutas, un pedido ajeno responde 404 sin cambiar nada, el tope del cuerpo y las opiniones fuera del campus.
- `ContractTest`: `Rate_AndTheOpinions_MatchTheirFixtures`.
- `DatabaseTest/SchemaScriptTests.cs`: `Ratings_HaveTheirIndexes_AndRejectAnInvalidRating` (020).

## Criterios
- [x] Solo se califica un pedido completado, una vez por parte (RN-24) → `OncePerOrder_PerSide`, `OnlyACompletedOrder_CanBeRated`.
- [x] Escala de 1 a 5 y comentario opcional (RN-25) → `StarsOutOfOneToFive_AreRejected_AndNothingChanges`, `TheComment_HasALimit_AndIsModerated`.
- [x] La reputación (promedio y número) considera lo recibido como quien vende y como quien compra → `TheReputation_IsASingleAverage_AsSellerAndAsBuyer` e integración.
- [x] Build Release 0/0 · Unit 610 · Integration 168 · Security 220 · Contract 22 (100 %) · `check_endpoints` OK (85) · critic PASS.

## Límites
- Sin aviso push de "Te calificaron": la maqueta no lo tiene.
- Una calificación no se edita ni se borra (RN-24).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 12-Nov-2026 (detallado e implementado)
- **Detalle:** HU-27 y HU-45 juntas, con las decisiones de la usuaria del 12-Nov-2026.

# HU-19 — Aviso al vendedor de solicitud compatible

**HU / Jira / Sprint / Módulo / Contraparte:** HU-19 / SCRUM-41 / Sprint 4 (9 – 20 nov 2026) / M-06 / [musubi-app:HU-19](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m06-matching/HU-19-aviso-solicitud-compatible)

## Contexto
Jira (SCRUM-41): Como **vendedor**, quiero **que el sistema me avise cuando una solicitud sea compatible con alguno de mis productos**, para **ofrecerlo a tiempo**.

- Maqueta: `Notificaciones` → "Coincidencias": "Hay una solicitud compatible con tu producto · Fernanda busca una bata de laboratorio talla M. Tu publicación coincide."
- Trazabilidad: RF-23, RF-39 · CU-13, CU-14 · RN-28 a RN-31.
- Decisiones de la usuaria (13-Nov-2026):
  - **compatible (RN-28):** misma categoría (o cualquiera si la solicitud es "otro"), la mitad o más de las palabras del nombre buscado en el nombre o la descripción de la publicación, y el precio no más de 20 % arriba del presupuesto;
  - **no repetir (RN-29):** un aviso por solicitud y persona, que nombra su publicación más parecida;
  - **límite diario (RN-30):** hasta 3 por persona y día del campus.

**Depende de:** `HU-13` (publicar una solicitud), `HU-16` ("Yo tengo este producto"), `HU-31` (avisos) y `HU-57` (bloqueos).

## Requisitos
1. **R1 · Aviso:** al publicar una solicitud, quien tiene una publicación **Disponible** compatible recibe `wanted_match` en "Coincidencias" con el texto de la maqueta y el nombre de su publicación (RF-39).
2. **R2 · Compatible:** las reglas de la decisión de la usuaria (RN-28). Las palabras se comparan normalizadas y por prefijo, como el buscador (HU-09); se ignoran las que no dicen qué se busca ("de", "la", "para"…).
3. **R3 · Una vez y con límite:** una sola vez por solicitud y persona (RN-29, `dedupeKey`), y hasta 3 al día (RN-30); las demás siguen en el tablero.
4. **R4 · Nunca lo propio:** ni la publicación de quien busca, ni entre personas bloqueadas (RN-31, HU-57), ni de otro campus.
5. **R5 · Abrir:** el aviso abre la solicitud del tablero (`target.type: wanted_board`) para responder "Yo tengo este producto".
6. **R6 · En segundo plano (RNF-11):** publicar no espera la búsqueda; si falla, la solicitud sigue publicada (RNF-15).

## Diseño
- `Modules/Matching/WantedMatching.cs`: `WantedPublished`, `IWantedMatching` (contrato para M-05), `MatchRules`, `WantedMatchNotifier`, `WantedMatchQueue` y `WantedMatchWorker`.
- M-03: `IProductMatches` sobre `IProductRepository.FindMatchCandidatesAsync` (`ix_community_status_search`; hasta 200 candidatas).
- M-09: `INotificationCenter.CountSinceAsync` (límite diario) y los tipos `wanted_match` y el destino `wanted_board`.

## Contratos
- Sin rutas nuevas. La fila `GET /api/v1/notifications` de [`docs/endpoints.md`](../../../docs/endpoints.md) suma el tipo `wanted_match` y el destino `wanted_board`.

## Tests
- `UnitTest/Matching/WantedMatchTests.cs`: el texto de la maqueta, cada regla de compatibilidad, la descripción y los plurales, solo disponibles del campus y nunca lo propio, bloqueos en los dos sentidos, la publicación más parecida, 3 al día desde la medianoche del campus y no repetir.
- `UnitTest/PurchaseRequests/WantedServiceTests.cs`: publicar encola la búsqueda ("otro" = cualquier categoría).
- `TestShared`: `MatchCandidates_*` (publicaciones) y `CountSince_*` (avisos), en memoria y en MongoDB real.
- `IntegrationTest/Matching/WantedMatchFlowTests.cs`: de punta a punta, y una solicitud que no es compatible no avisa.

## Criterios
- [x] Cuando se publica una solicitud compatible con un producto **Disponible**, el vendedor recibe una notificación (RF-39) → `ACompatibleListing_TellsItsSeller_WithTheMockupText` e integración.
- [x] Solo se notifican coincidencias que superen el umbral de relevancia (RN-28) → `Compatible_IsTheCategory_HalfTheWords_AndUpTo20PercentOverBudget`.
- [x] Una misma coincidencia no se notifica más de una vez (RN-29) y se respeta el límite diario (RN-30) → `TheSameRequest_IsNotNotifiedTwice`, `AtMostThreeADay_*` y `TheDailyLimit_CountsFromMidnightOnCampus`.
- [x] No se notifican coincidencias sobre publicaciones o solicitudes propias (RN-31) → `OnlyAvailableListings_OfThisCampus_AndNeverMine` y `ABlock_*`.
- [x] Desde la notificación el vendedor abre la solicitud y puede responder "Yo tengo este producto" → destino `wanted_board` (la app lo abre, `musubi-app:HU-19`).
- [x] Build Release 0/0 · Unit 638 · Integration 171 · Security 222 · Contract 22 · `check_endpoints` OK · critic PASS.

## Límites
- La cola vive en memoria (como la de push): si la instancia se reinicia con solicitudes pendientes, esas no avisan. El `outbox` (R14) lo resolvería.
- Solo al publicar una solicitud. Avisar a quien busca cuando aparece una publicación compatible es HU-29; mostrarle lo que ya coincide es HU-18 (registro de coincidencias, RF-25).
- Con dos instancias, dos solicitudes al mismo tiempo podrían dar un cuarto aviso en el día.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 13-Nov-2026 (detallado e implementado)
- **Detalle:** con las decisiones de la usuaria del 13-Nov-2026.

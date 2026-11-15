# HU-18 — Ver coincidencias al publicar una solicitud (plan)

1. **T1 — Spec** con las decisiones del 14-Nov-2026. ✅
2. **T2 — Registro (RF-25):** `matches`, `IMatchRepository` (contrato compartido), `MatchLedger` y `db/024`. ✅
3. **T3 — Coincidencias:** `WantedMatchesService`, `IWantedMatchDirectory` y `GET /wanted/{id}/matches`. ✅
4. **T4 — Terminó en pedido:** `OrderService` marca la coincidencia. ✅
5. **T5 — Pruebas y documentos.** ✅

**Decisiones:**
- **D1 · Las parecidas usan la misma consulta de candidatas** (una palabra basta) con un margen de 50 %: no hace falta otro índice.
- **D2 · Las tarjetas salen de `IRecommendationCatalog.CardsAsync`** (HU-35): mismo orden, solo disponibles y con quien vende.
- **D3 · El registro nunca rompe el flujo:** `MatchLedger` atrapa los errores de MongoDB (es una métrica).

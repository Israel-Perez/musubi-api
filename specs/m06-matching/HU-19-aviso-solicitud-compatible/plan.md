# HU-19 — Aviso al vendedor de solicitud compatible (plan, API)

1. **T1 — Spec:** decisiones de la usuaria (compatible, no repetir, límite diario). ✅
2. **T2 — Candidatas (M-03):** `FindMatchCandidatesAsync` e `IProductMatches`. ✅
3. **T3 — Reglas y aviso (M-06):** `MatchRules` y `WantedMatchNotifier`. ✅
4. **T4 — Segundo plano:** `IWantedMatching`, `WantedMatchQueue` y `WantedMatchWorker`; `WantedService` encola al publicar. ✅
5. **T5 — Pruebas y documentación.** ✅

**Decisiones:**
- **D1 · Cola en memoria:** igual que la de push; el `outbox` sigue en diseño (límite en la spec).
- **D2 · Un aviso por persona con su publicación más parecida:** se elige la de más palabras en común y, al empate, la de precio más cercano al presupuesto.
- **D3 · El límite diario se cuenta en el historial de avisos:** no hace falta otra colección; `ix_user_id` cubre la consulta.

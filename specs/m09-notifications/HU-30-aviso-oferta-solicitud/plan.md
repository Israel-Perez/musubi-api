# HU-30 — Aviso de oferta en mi solicitud (plan, API)

1. **T1 — Spec:** el aviso de oferta ya existe (HU-31); decisión de la usuaria sobre el cierre. ✅
2. **T2 — Cierre:** `offer_closed` a cada oferta pendiente al cerrar o cancelar. ✅
3. **T3 — Pruebas y documentación.** ✅

**Decisiones:**
- **D1 · Sin destino:** la solicitud cerrada ya no se puede abrir (404 para quien no la publicó); el aviso solo informa.
- **D2 · `dedupeKey` por oferta:** cerrar dos veces (idempotente) no repite el aviso.

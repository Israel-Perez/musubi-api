# HU-12 — Guardar una búsqueda sin resultados (plan)

1. **T1 — Spec** con las decisiones del 14-Nov-2026. ✅
2. **T2 — Datos:** `savedSearches`, `ISavedSearchRepository` (contrato compartido) y `db/025`. ✅
3. **T3 — Rutas:** `SavedSearchService` y `/saved-searches`. ✅
4. **T4 — Aviso:** `ProductMatchNotifier` con `ISavedSearchDirectory`. ✅
5. **T5 — Pruebas y documentos.** ✅

**Decisiones:**
- **D1 · `key` canónica** (palabras normalizadas + filtros): escribir lo mismo de otra forma ("TI 84" o "ti-84") no duplica la alerta.
- **D2 · Las vigentes del campus se leen enteras al publicar** (10 por persona, tope de 2000) y se filtran en memoria: la regla "todas las palabras" con prefijo no se resuelve con un índice.
- **D3 · Un solo aviso por persona y publicación:** si ya llegó el de su solicitud (HU-29), no llega el de su búsqueda.

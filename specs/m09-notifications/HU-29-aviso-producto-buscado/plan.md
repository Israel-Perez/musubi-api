# HU-29 — Aviso de producto buscado disponible (plan)

1. **T1 — Spec.** ✅
2. **T2 — Cola:** `IProductPublications` en `ListingService`, `ProductMatchQueue` y `ProductMatchWorker`. ✅
3. **T3 — Aviso:** `ProductMatchNotifier` con `IWantedMatchDirectory` y el registro (RF-25). ✅
4. **T4 — Base:** `db/024` con el validador de `notifications` y la prueba de todos los tipos y destinos. ✅

**Decisiones:**
- **D1 · Presupuesto mínimo:** la consulta pide solicitudes con presupuesto de al menos `ceil(precio / 1.2)` (`MinBudget`), así el 20 % de margen se resuelve en MongoDB.
- **D2 · Una por persona:** si alguien tiene dos solicitudes compatibles, solo le llega la que más se parece.
- **D3 · La prueba de tipos y destinos lee las constantes del código:** un tipo nuevo sin script falla en `DatabaseTest`.

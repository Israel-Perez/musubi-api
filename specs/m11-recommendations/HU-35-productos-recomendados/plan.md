# HU-35 — Ver productos recomendados (plan, API)

1. **T1 — Spec:** decisiones de la usuaria y ML real (L-16i). ✅
2. **T2 — Señales:** `interactions`, `IInteractionRecorder` y los cuatro puntos de registro. ✅
3. **T3 — Modelo:** `RecommendationModel` (TF-IDF, perfil, factorización de matrices) y su evaluación leave-one-out. ✅
4. **T4 — Rutas:** `GET /recommendations` y `GET /products/{id}/similar`, con el catálogo de M-03. ✅
5. **T5 — Pruebas y documentación.** ✅

**Decisiones:**
- **D1 · ML.NET dentro de la API:** costo cero (sin SageMaker ni servicios extra) y el mismo stack (.NET). Alternativa descartada: un servicio de Python, que agrega despliegue y costo.
- **D2 · Modelo en memoria por campus, 30 minutos:** con el volumen de un campus entrena en milisegundos; no hace falta guardar recomendaciones.
- **D3 · Híbrido:** el texto funciona desde el primer día (arranque en frío); la factorización entra cuando hay datos.
- **D4 · Versión 5.0.1:** 6.0.0 salió el 11-Nov-2026 y el proyecto espera 7 días (cooldown).

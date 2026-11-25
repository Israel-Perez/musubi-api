# HU-60 — Coincidencias por significado (tareas)

## T1 — Detallar la spec y medir
- **Qué:** el conjunto de evaluación del campus (`UnitTest/Matching/Fixtures/semantic-eval.json`), sus vectores de Gemini (`scripts/semantic_eval_vectors.ps1`) y la elección del modelo, el tipo de tarea y el umbral.
- **Estado:** ✅ Hecho (25-Nov-2026). Decisiones para revisar: Gemini, los textos públicos que salen solos y el umbral 0.74.

## T2 — Vectores
- **Qué:** `ITextEmbedder`, `SemanticMatcher`, `matchEmbeddings` y `db/035`.
- **Prueba:** `TextEmbeddingsTests`, `SemanticMatchingTests`, `EmbeddingRepositoryContract` y `SchemaScriptTests`.
- **Estado:** ✅ Hecho. `DatabaseTest` corre en el CI (no hay Docker en esta máquina).

## T3 — Reglas
- **Qué:** `MatchRules.Compatible` en HU-18, HU-19, HU-29 y HU-38; `ProductMatchQuery.AnyWords`.
- **Prueba:** `SemanticEvaluationTests`, `SemanticMatchingTests`, las de HU-18/19/29/38 y `IntegrationTest/Matching/WantedMatchFlowTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Documentación
- **Estado:** ✅ Hecho: modelo de datos, `db/README.md`, endpoints (fila de HU-18), trazabilidad, versionado (alpha.47, esquema 35), lineamientos (L-16i), 01-04, `AGENTS.md` y `Memoria.md`.

## T5 — Producción
- **Qué:** después de `infra-up` con `0.5.0-alpha.47` y `db/035`, revisar en los logs que no haya "Gemini no dio los vectores" seguidos (cuota) y probar una solicitud que no comparta palabras con una publicación.
- **Estado:** ⏳ Por hacer (siguiente despliegue).

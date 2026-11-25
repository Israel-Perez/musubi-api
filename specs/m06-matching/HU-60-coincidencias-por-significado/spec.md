# HU-60 — Encontrar coincidencias por significado y no solo por palabras

**HU / Jira / Sprint / Módulo / Contraparte:** HU-60 / SCRUM-100 / Sprint 5 (14 nov – 1 dic 2026) / M-06 / [musubi-app:HU-60](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m06-matching/HU-60-coincidencias-por-significado) (aviso de privacidad y Play; sin pantallas nuevas)

## Contexto
Jira (SCRUM-100): Como **comprador o vendedor**, quiero **que las coincidencias entiendan lo que busco aunque use otras palabras**, para **que "calculadora científica" encuentre una "Casio fx-991"**.

- Mejora HU-18 ("Ya coinciden con lo que buscas"), HU-19 (aviso a quien vende), HU-29 (aviso a quien busca) y HU-38 ("Quienes la buscan"). Trazabilidad: RN-28 y lineamiento **L-16i** (ML).
- **Decisiones (25-Nov-2026, para que la usuaria las revise):**
  - **Embeddings de Gemini** (`gemini-embedding-001`, capa gratuita, 256 dimensiones), el mismo proveedor de la IA. Un modelo local necesitaría un paquete nuevo (ONNX) y un archivo de cientos de MB.
  - **La similitud se calcula en la API**, no con Atlas Vector Search: un campus tiene pocos cientos de publicaciones y la consulta ya filtra por campus, categoría y precio.
  - **Los textos salen solos, sin que nadie lo pida:** son públicos dentro del campus (el nombre y la descripción de una publicación y lo que dice una solicitud "Se busca"), van sin datos de contacto y no son conversaciones (RNF-06 sigue igual). El aviso de privacidad y Play se actualizan (musubi-app). Se apaga con `GenAi:Provider=Off`.
  - **Umbral 0.74**, medido con ejemplos del campus (ver Criterios).

**Depende de:** `HU-18`, `HU-19`, `HU-29`, `HU-38`; `HU-32/33` (Gemini).

## Requisitos
1. **R1 · Un vector por publicación y solicitud:**
   - Se calcula en segundo plano (RNF-11), en los workers de HU-19 y HU-29: al publicar, al editar y la primera vez que una publicación vieja sale como candidata.
   - Se guarda en `matchEmbeddings` con la huella del texto y el modelo; si cambia alguno, se recalcula.
   - Las consultas desde la app (HU-18 y HU-38) solo usan los vectores guardados: nunca llaman a la IA.
2. **R2 · Compatibilidad (RN-28):** una publicación le sirve a una solicitud si tiene la mitad de las palabras buscadas **o** la similitud de sus vectores es de 0.74 o más. Siguen la categoría y el presupuesto (20 % arriba). En HU-18, "parecida" también puede ser por significado (0.70).
3. **R3 · Se mantienen las reglas de HU-19:** una vez por solicitud y persona, hasta 3 avisos al día, nunca sobre lo propio ni entre personas bloqueadas.
4. **R4 · Candidatas:** con vectores, la búsqueda de publicaciones no filtra por palabras (las más recientes de la categoría y el precio, hasta 200).
5. **R5 · Sin proveedor (RNF-15):** sin llave, con la cuota agotada, sin red o si la base falla, no hay similitud y deciden las palabras, como antes.
6. **R6 · Privacidad:**
   - A Gemini solo llegan el nombre y la descripción (o lo que se busca), sin correos, teléfonos, cuentas ni enlaces (`TranscriptRedactor`).
   - Los logs dicen el motivo de una falla, nunca los textos.
   - Los vectores se borran con la cuenta (HU-46).

## Diseño
- `Modules/GenAi/TextEmbeddings.cs`: `ITextEmbedder` (Gemini `batchEmbedContents`, hasta 100 por llamada, `RETRIEVAL_QUERY`/`RETRIEVAL_DOCUMENT`), `InMemoryTextEmbedder` (Development y pruebas) y `OffTextEmbedder`; `Embeddings.Text` y `Embeddings.Cosine`.
- `Modules/Matching/SemanticMatching.cs`: `SemanticMatcher.SimilaritiesAsync` (guardados o calculados) y `RememberAsync`; borra con la cuenta.
- `Modules/Matching/Data/EmbeddingRepositories.cs`: colección `matchEmbeddings`, `db/035-coincidencias-semanticas.js` y esquema 35.
- `MatchRules.Compatible`, `Score`, `SemanticThreshold` y `SimilarSemanticThreshold`; `WantedMatchNotifier`, `ProductMatchNotifier`, `DemandRules.BestRequests`, `ProductDemandService` y `WantedMatchesService.Rank`.
- `ProductMatchQuery.AnyWords`; `ProductCandidate` y `ProductPublished` llevan la descripción.

## Contratos
Sin endpoints nuevos ni cambios de forma. La fila `GET /api/v1/wanted/{id}/matches` de [`docs/endpoints.md`](../../../docs/endpoints.md) dice que "exacta" también es por significado. Colección `matchEmbeddings` en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md).

## Tests
- `UnitTest/Matching/SemanticEvaluationTests.cs`: las dos formas con 16 solicitudes y 24 publicaciones del campus y sus vectores reales de Gemini (`Fixtures/semantic-eval*.json`, `scripts/semantic_eval_vectors.ps1`).
- `UnitTest/Matching/SemanticMatchingTests.cs`:
  - "Calculadora científica" encuentra la "Casio fx-991" solo por significado;
  - las reglas de HU-19 siguen;
  - los vectores se guardan y se recalculan al cambiar el texto;
  - desde la app no se llama a la IA;
  - sin proveedor, si falla o si la base falla, deciden las palabras;
  - se borran con la cuenta;
  - exactas y parecidas de HU-18 y la dirección de HU-29.
- `UnitTest/GenAi/TextEmbeddingsTests.cs`: el texto sin contacto, el coseno, la IA en memoria, el cuerpo de Gemini (llave en el header, papel, dimensiones y lotes de 100) y sus fallas.
- `TestShared/Matching/EmbeddingRepositoryContract.cs` (en memoria y MongoDB) y `DatabaseTest/SchemaScriptTests.cs` (`db/035`, `ix_owner`).
- `IntegrationTest/Matching/WantedMatchFlowTests.cs`: por significado en los dos sentidos (HU-19 y HU-29) y en "Ya coinciden" (HU-18).

## Criterios
- [x] Cada publicación y cada solicitud guarda su vector, calculado en segundo plano (RNF-11) → `Vectors_AreSaved_AndRecomputedOnlyWhenTheTextChanges`, `FromTheApp_OnlyTheSavedVectorsAreUsed_AndTheAiIsNotCalled`, `EmbeddingRepositoryContract`.
- [x] La compatibilidad usa la similitud además de la categoría y el presupuesto; las palabras quedan de respaldo → `ACalculadoraCientifica_FindsTheCasio_WithoutSharedWords_OnlyBySense`, `BySense_ACalculadoraCientifica_FindsTheCasio_InBothDirections`.
- [x] Se mantienen las reglas de HU-19 → `TheRulesOfHu19_Stay_CategoryBudgetOwnAndBlocks` y las pruebas de HU-19 y HU-29 (sin cambios de comportamiento).
- [x] Una prueba compara las dos formas con ejemplos del campus → `BySense_FindsMuchMoreThanByWords_WithBetterPrecision`. Dentro de la categoría:
  - **por palabras:** 4 de 21 encontradas, 2 de más (precisión 0.67, cobertura 0.19);
  - **por significado:** 18 de 21 encontradas, 6 de más (precisión 0.75, cobertura 0.86).
- [x] Sin el proveedor, deciden las palabras (RNF-15) → `WithoutAProvider_OrIfItFails_TheWordsDecide`, `Gemini_WhenItRefuses_IsNull`.
- [ ] Probar con Gemini real en producción (T5).

## Límites
- El umbral se midió con un conjunto chico (40 textos) y se eligió con ese mismo conjunto: hay que revisarlo con datos reales del campus.
- No distingue matices: "calculadora científica" también acepta una "calculadora básica" (0.77). La categoría y el presupuesto siguen filtrando y quien busca decide.
- Las búsquedas guardadas de HU-12 siguen por palabras (piden todas las palabras, como la búsqueda).
- Una solicitud editada (HU-14) se vuelve a comparar por significado cuando el worker la vuelve a ver; mientras, deciden las palabras.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 25-Nov-2026 (detallada e implementada)

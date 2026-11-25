# plan.md — HU-60 Coincidencias por significado

## 1. Componentes
- M-10: `Modules/GenAi/TextEmbeddings.cs` y su registro (singleton; el cliente HTTP se pide por llamada).
- M-06: `SemanticMatching.cs`, `Data/EmbeddingRepositories.cs`, `MatchRules` y los tres puntos que deciden la compatibilidad (`WantedMatchNotifier`, `DemandRules.BestRequests` y `WantedMatchesService.Rank`).
- M-03: `ProductMatchQuery.AnyWords` (Mongo y en memoria) y la descripción en `ProductCandidate`/`ProductPublished`.
- `db/035-coincidencias-semanticas.js`, `MongoSchema.ExpectedVersion = 35`.

## 2. Decisiones (con su porqué)
- **Gemini y no un modelo local:** costo cero, mismo proveedor y llave, sin paquetes nuevos. Detrás de `ITextEmbedder`: se cambia por configuración.
- **`RETRIEVAL_QUERY`/`RETRIEVAL_DOCUMENT` y no `SEMANTIC_SIMILARITY`:** con el conjunto del campus, `SEMANTIC_SIMILARITY` junta todo entre 0.80 y 0.90 y ningún umbral separa bien (F1 máximo 0.75). Con los tipos de búsqueda, 0.74 da precisión 0.75 y cobertura 0.86 (F1 0.80).
- **Umbral fijo y no relativo ("cerca de la mejor"):** el relativo da un poco más (F1 0.89), pero solo sirve de la solicitud a las publicaciones. Para HU-29 (de una publicación nueva a las solicitudes) habría que comparar cada solicitud con todas las publicaciones. Un umbral fijo da lo mismo en los dos sentidos.
- **Vectores guardados con huella:** no se vuelve a llamar a Gemini por el mismo texto; editar la publicación o cambiar el modelo los invalida solos. Sin TTL: un vector viejo solo se ignora.
- **La IA solo en segundo plano:** los workers calculan; las consultas de la app (HU-18 y HU-38) solo leen. Así la app nunca espera a Gemini ni gasta su cuota.
- **Candidatas sin filtro de palabras cuando hay vectores:** si no, "Casio" nunca llegaría a "calculadora científica". El índice `ix_community_status_search` sigue resolviendo campus y estado.
- **Las palabras siguen:** una coincidencia por palabras vale aunque la similitud sea baja (`Compatible` es un "o").

## 3. Excepciones al stack
Ninguna.

## 4. Guardarraíles
- RNF-06/RNF-03: solo textos públicos sin contacto; los logs sin textos.
- RNF-15: cualquier falla deja las palabras; ninguna excepción llega a quien publica.
- HU-46: `SemanticMatcher` es `IAccountDataEraser`.
- API4: lotes de 100 y solo en los workers.

## 5. Validación
Unit (con la evaluación), Integration, Security (sin cambios de rutas), Contract y DatabaseTest (`db/035`, en el CI con Docker); `check_endpoints` y `check_endpoint_tests`.

## 6. Secuencia
T1 → T2 → T3 → T4 → T5.

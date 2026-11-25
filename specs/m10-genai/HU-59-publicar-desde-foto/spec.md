# HU-59 — Publicar un producto a partir de una foto (IA generativa)

**HU / Jira / Sprint / Módulo / Contraparte:** HU-59 / SCRUM-99 / Sprint 5 (14 nov – 1 dic 2026) / M-10 / [musubi-app:HU-59](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m10-genai/HU-59-publicar-desde-foto)

## Contexto
Jira (SCRUM-99): Como **vendedor**, quiero **que la app proponga el nombre, la categoría, el estado y la descripción a partir de la foto de mi producto**, para **publicar más rápido**.

- Pantallas: `Vender-1` a `Vender-4`. La maqueta no tiene "Llenar con IA": se agrega sin quitar nada (lineamiento 4).
- Trazabilidad: lineamiento **L-16i** (IA generativa). Relacionada con HU-06 (publicar), HU-37 (ideas con IA) y 02-07 (moderación).
- **Decisiones (25-Nov-2026, para que la usuaria las revise):**
  - Solo se manda **la portada** (la primera foto), no todas: gasta menos cuota y basta para reconocer el producto.
  - **Comparte el tope de la IA** (`genai`, 6 por hora por persona) con "Resumir acuerdo" e "Ideas con IA". Es la respuesta a la nota de Jira "definir un límite por persona".
  - **La foto se comparte con Google** en la capa gratuita de Gemini: se actualizan el aviso de privacidad (HU-47) y "Seguridad de los datos" de Play (en musubi-app).

**Depende de:** `HU-06` (subir la foto), `HU-32/33` (Gemini y `GeminiApi`), `HU-41` (categorías del campus), `02-07` (moderación).

## Requisitos
1. **R1 · Propuesta:**
   - `POST /products/photo-draft` con la llave de una foto que la misma persona subió por `POST /products/photos`.
   - La IA recibe la foto (WebP, ya moderada y sin metadatos) y las categorías **activas** de su campus.
   - Devuelve el nombre, la categoría, el estado (con su detalle si es usado) y una descripción corta.
2. **R2 · Reglas de la publicación (RN-03, RN-04):**
   - Nombre de 3 a 80 y descripción de 1 a 500; si no, no se usa.
   - Una categoría que no sea del campus se devuelve como `null`.
   - `new` va sin detalle; un usado sin detalle válido queda en "Buen estado" (`good`).
3. **R3 · Moderación (02-07):** el nombre y la descripción que propone la IA se moderan como si los hubiera escrito quien vende; si no pasan, no se muestran.
4. **R4 · Nada se guarda (RN-38):** la API no guarda la propuesta. La app la pone en el borrador y quien vende la revisa antes de publicar.
5. **R5 · Sin IA (RNF-15):** sin proveedor, sin red, con la cuota agotada o con una respuesta fuera de las reglas, responde `503 listings.draft_unavailable` y se publica a mano.
6. **R6 · Privacidad (RNF-06, OWASP API1/API4):**
   - Solo salen la foto y los nombres de las categorías: ni ids, ni nombres, ni correos.
   - Con la llave de otra persona, o con una que no tiene la forma de una foto de producto, responde 404 sin leer nada.
   - Sin proveedor configurado no se lee la foto.
   - Tope `genai` por persona.

## Diseño
- `Modules/GenAi/PhotoDraft.cs`:
  - `IPhotoDraftWriter` (contrato de M-10 para M-04), con `PhotoDraftPrompt` y `PhotoDraft`;
  - `PhotoDraftText` (instrucciones y categorías) y `PhotoDraftJson` (lee y valida);
  - `GeminiPhotoDraftWriter` (la foto como `inlineData` y un `responseSchema` con `enum`), `InMemoryPhotoDraftWriter` y `OffPhotoDraftWriter`.
- `Modules/Listings/PhotoDraftService.cs`: valida la cuenta y la llave (`ListingService.IsOwnPhotoKey`), lee la foto (`IMediaStorage.GetAsync`), pide la propuesta y modera el texto.
- `ProductsController.PhotoDraft`, con el rate limit `genai`.

## Contratos
- Fila `POST /api/v1/products/photo-draft` de [`docs/endpoints.md`](../../../docs/endpoints.md).
- Errores `listings.photo_not_found` y `listings.draft_unavailable`.
- Fixture `photo-draft.json`.

## Tests
- `UnitTest/GenAi/PhotoDraftTests.cs`:
  - el texto lleva solo las categorías;
  - la respuesta se valida (categoría del campus, estado, largos y forma);
  - Gemini recibe la llave en el header, la foto, el esquema y nada más;
  - 429, 500, bloqueo, sin red y sin llave dejan la publicación a mano;
  - la IA en memoria es determinista.
- `UnitTest/Listings/PhotoDraftServiceTests.cs`:
  - la foto propia con las categorías activas;
  - una ajena, una ruta o una que nunca subió dan 404 sin llamar a la IA;
  - cuenta suspendida, sin IA y la IA sin respuesta;
  - el texto que no pasa la moderación.
- `IntegrationTest/Listings/PhotoDraftFlowTests.cs`: subir la foto, la propuesta y publicar después de revisarla; la foto de otra persona; sin IA se publica a mano.
- `SecurityTest/Auth/PhotoDraftAuthTests.cs`: 401, 404 con llaves ajenas o rutas, 400, 403, Production sin llave (503) y el tope `genai`.
- `ContractTest/ListingsContractTests.cs` (`photo-draft.json`).

## Criterios
- [x] Después de subir la primera foto, "Llenar con IA" propone el nombre, la categoría del campus, el estado y una descripción corta, con Gemini detrás de una interfaz → `AnOwnPhoto_GetsTheProposal_WithTheCampusCategories`, `Gemini_GetsTheKeyInAHeader_ThePhoto_TheSchema_AndNothingElse`, `ThePhoto_FillsTheDraft_AndItPublishesAfterReviewingIt`.
- [x] La propuesta queda como borrador editable: nada se publica sin que quien vende lo revise (RN-38) → `ThePhoto_FillsTheDraft_AndItPublishesAfterReviewingIt` (no hay publicación hasta `POST /products`).
- [x] La foto ya pasó la moderación antes de enviarse, y el texto propuesto también se modera → `PhotoUploadService` (02-07) y `AProposalThatDoesNotPassModeration_IsNotShown`.
- [x] Sin IA, la publicación sigue igual, a mano (RNF-15) → `WithoutAi_ItSaysSo_AndPublishingByHandKeepsWorking`, `InProduction_WithoutAGeminiKey_MyPhotoIs503_AndTheAiIsOff`, `Gemini_WhenItRefuses_LeavesItByHand`.
- [x] Aviso de privacidad y "Seguridad de los datos" → contraparte en musubi-app.
- [x] Build Release 0/0 · `check_endpoints` OK (131) · `check_endpoint_tests` OK.

## Límites
- La propuesta sale solo de la portada; las demás fotos no se mandan.
- No propone el precio: eso es "Precio sugerido" (HU-58), en `Vender-3`.
- La calidad depende de la foto y del modelo de la capa gratuita; quien vende siempre la revisa.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 25-Nov-2026 (detallada e implementada)

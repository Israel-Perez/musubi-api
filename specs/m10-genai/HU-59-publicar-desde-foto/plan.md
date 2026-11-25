# plan.md — HU-59 Publicar un producto a partir de una foto

## 1. Componentes
- `Modules/GenAi/PhotoDraft.cs` (contrato, texto, lectura y adaptadores) y su registro en `GenAiModuleServiceCollectionExtensions`.
- `Modules/Listings/PhotoDraftService.cs`, `ProductsController.PhotoDraft`, `PhotoDraftRequest`/`PhotoDraftResponse` y dos errores en `ListingsErrors`.

## 2. Decisiones (con su porqué)
- **Una llave y no la foto otra vez:** la foto ya se subió y pasó la moderación en `POST /products/photos` (02-07). Mandar la llave evita subirla dos veces y garantiza que a la IA solo llega lo moderado.
- **Solo la portada:** basta para reconocer el producto y gasta una sola imagen de la cuota gratuita.
- **Las categorías van en el texto y la respuesta se valida contra ellas:** la IA no puede inventar una categoría; si no reconoce ninguna, la app deja que quien vende la elija.
- **El texto de la IA se modera:** lo que se muestra en el borrador puede terminar publicado, así que pasa la misma regla que un texto escrito a mano.
- **Sin proveedor no se lee la foto:** el 503 sale antes de abrir S3.
- **Mismo tope `genai`:** cada llamada gasta la cuota de Gemini como un resumen; un solo tope por persona es más fácil de explicar.

## 3. Excepciones al stack
Ninguna. Usa el mismo `GeminiApi` de HU-33/37.

## 4. Guardarraíles
- OWASP API1: `IsOwnPhotoKey` (patrón estricto y prefijo de la persona) antes de cualquier lectura.
- API4: `[MaxLength(120)]` en la llave y rate limit `genai`.
- RNF-06: a Gemini solo llegan la foto (WebP sin EXIF) y los nombres de las categorías; los logs solo dicen el motivo de una falla.
- L-16b: la foto se moderó al subirla y el texto se modera aquí.

## 5. Validación
Unit, Integration, Security y Contract; `check_endpoints` y `check_endpoint_tests`; prueba con Gemini real (T4).

## 6. Secuencia
T1 → T2 → T3 → T4.

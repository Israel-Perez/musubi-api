# 02-07 — Moderación de contenido antes de guardarlo

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-92 (antes SCRUM-86) / S4 / Plataforma / musubi-app: los mensajes de rechazo llegan como `ApiFailure` (`code` y mensaje en español)

## Contexto
El documento de la materia (requisito 16) pide que, antes de almacenar texto, imágenes o video, se evalúe con una API de la nube si la información es inapropiada; si lo es, no se permite subirla.

Lo que se guardaba sin moderar:
- fotos y textos de las publicaciones (HU-06);
- la foto de perfil (HU-04).

Después llegarán solicitudes (HU-13), ofertas (HU-16), mensajes (HU-20) y reportes (HU-28).

**Depende de:** `HU-06` (`PhotoProcessor` y `Platform/Media`) y `HU-04` (foto de perfil).

## Requisitos
1. **Imágenes:**
   - Amazon Rekognition (`DetectModerationLabels`) revisa la foto ya procesada;
   - con una etiqueta prohibida y una confianza de 80 % o más, la foto se rechaza y **no se guarda en S3**;
   - etiquetas prohibidas: desnudez, violencia, contenido perturbador, drogas, símbolos de odio y gestos groseros. Se permiten la ropa de baño y el alcohol (ropa y vasos que se venden en el campus).
2. **Texto:**
   - el título y la descripción de una publicación se revisan antes de guardarla;
   - si uno se rechaza, la respuesta nombra el campo con un mensaje en español.
3. **Video:** la app no sube video (la maqueta no lo incluye). Si se agrega, aplica la misma regla.
4. **Sin moderación no se guarda (RNF-15):** si AWS no responde, se responde `503 moderation.unavailable` y no se guarda nada. Nunca se publica sin moderar.
5. **Rastro sin el contenido:** cada rechazo queda en los logs (CloudWatch) con el tipo y el motivo, nunca con el texto ni la foto.

## Diseño
- **`Platform/Moderation/`:**
  - `IContentModerator` (`CheckImageAsync` y `CheckTextsAsync`).
  - `AwsContentModerator`. Las reglas son funciones puras con pruebas: `BlockedLabel` (por la etiqueta o por su padre en la taxonomía) e `IsToxic`.
  - `InMemoryContentModerator`, solo en Development y pruebas: rechaza el marcador `[[inapropiado]]` y las fotos magenta.
  - `ModerationOptionsValidator`: fuera de Development exige AWS (fail-fast).
- **Texto en español: léxico local (`SpanishLexicon`) desde el 08-Nov-2026.**
  - El plan gratuito de AWS de la cuenta no incluye Translate ni Comprehend: las dos responden `SubscriptionRequiredException`, y todo mensaje y publicación quedaba en `503 moderation.unavailable`. Rekognition sí está incluido.
  - El léxico rechaza groserías, insultos, odio, contenido sexual, amenazas y venta de drogas (español de México y algo de inglés), sin costo ni llamadas a la nube.
  - Compara palabras completas, nunca subcadenas ("computadora" no contiene "puta"). Antes normaliza lo que se usa para esquivar un filtro: mayúsculas, acentos (conserva la ñ: "cono" no es "coño"), letras repetidas, números y símbolos por letras (`p3nd3j0`, `put@`) y letras sueltas (`p u t a`).
  - `Moderation:ExtraBlockedTerms` suma palabras o frases sin desplegar.
  - `Moderation:TextProvider=Aws` vuelve a usar Translate (`es` → `en`) + Comprehend `DetectToxicContent` después del léxico (la toxicidad de Comprehend solo acepta inglés). Sirve si la cuenta pasa al plan de pago.
  - El moderador en memoria también aplica el léxico, como producción.
- **Fotos:** Rekognition no acepta WebP. `PhotoProcessor` agrega una copia JPEG (`ProcessedPhoto.ModerationCopy`) que se modera y nunca se guarda.
- **Integración:**
  - `PhotoUploadService` (productos) y `ProfilePhotoService` (perfil): moderan antes de `PutAsync`;
  - `ListingService.PublishAsync`: modera el título y la descripción antes de insertar;
  - `ChatService` (HU-20): modera cada mensaje antes de guardarlo.
- **Permisos:** el rol de las instancias tiene `rekognition:DetectModerationLabels`, `comprehend:DetectToxicContent` y `translate:TranslateText` (statement `Moderar`; estas acciones no admiten restringir el recurso). Infra `0.3.3`.

## Contratos
- **Errores** ([`docs/errores.md`](../../../docs/errores.md)):
  - `moderation.image_rejected` (422);
  - `moderation.text_rejected` (422, el mensaje nombra el campo);
  - `moderation.unavailable` (503).
- **Filas afectadas** en [`docs/endpoints.md`](../../../docs/endpoints.md): `POST /products`, `POST /products/photos` y `PUT /profiles/me/photo`.
- **Configuración:** `Moderation:*` (`01-04`).
- **Paquetes:** `AWSSDK.Rekognition`, `AWSSDK.Comprehend` y `AWSSDK.Translate` (constitución §6; patrón `AWSSDK.*` ya mapeado). Comprehend y Translate solo se llaman con `TextProvider=Aws`.

## Tests
- **`UnitTest/Platform/ModerationTests.cs` (21):**
  - qué etiquetas y qué confianza rechazan;
  - el umbral de toxicidad;
  - las reglas del moderador en memoria;
  - la copia JPEG;
  - el fail-fast fuera de Development.
- **`UnitTest/Platform/SpanishLexiconTests.cs` (32):**
  - lo ofensivo se rechaza aunque se disfrace;
  - lo inocente que contiene una palabra prohibida pasa;
  - la normalización y los términos extra;
  - con `TextProvider=Lexicon` no se llama a AWS, y con `Aws` caído se conserva el rechazo del léxico.
- **`IntegrationTest/Moderation/ModerationFlowTests.cs` (4):**
  - una foto inapropiada responde 422 en productos y en perfil, y no se guarda;
  - un texto inapropiado responde 422 nombrando el campo, y no se publica;
  - lo apropiado se publica.

## Criterios
- [x] Ninguna foto ni texto publicado se guarda sin pasar por `IContentModerator`.
- [x] Los rechazos llevan un mensaje en español y nombran el campo.
- [x] Si AWS no responde, no se guarda nada y la respuesta es `503`, nunca `500`.
- [x] El costo estimado está en `infra/README.md` (~1 USD/mes en el piloto; el texto ya no cuesta).
- [x] Un error de red de AWS (`HttpRequestException`, que el SDK v4 no envuelve) también responde `503`.
- [ ] Probado contra AWS después de `infra-up`: una foto normal se publica.

## Límites
- **Sin cola de pendientes:** si AWS falla se pide reintentar en lugar de guardar como pendiente. El `outbox` y la revisión humana llegan con HU-39 (`NOTE (HU-39)`).
- **Pendientes:** `NOTE (HU-13)` y `NOTE (HU-16)` moderan su texto con el mismo `IContentModerator`. HU-20 ya modera cada mensaje. El detalle de un reporte (HU-28) **no** se modera: no se publica y la cita del abuso es la evidencia.
- **Umbrales:** se ajustan con datos del piloto.
- **El léxico no entiende contexto:** un insulto sin groserías o una amenaza con otras palabras pasa. Se completa con los reportes (HU-28) y la revisión humana (HU-39). Las palabras con usos comunes inocentes ("perra", "zorra", "pinche", "coger") quedan fuera a propósito.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador) · 03-Nov-2026 (implementación)
- **Detalle:**
  - texto en español con Translate + Comprehend (la toxicidad de Comprehend solo acepta inglés, según la documentación de AWS consultada el 03-Nov-2026);
  - se rechaza en lugar de dejar pendiente cuando AWS no responde;
  - 08-Nov-2026 (SCRUM-92): el texto pasa al léxico local porque el plan gratuito de AWS no incluye Translate ni Comprehend (en producción, el chat y las publicaciones respondían 503). API `0.5.0-alpha.4`.

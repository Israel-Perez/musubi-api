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
- **Texto en español:** la detección de toxicidad de Comprehend **solo acepta inglés**. Por eso el texto se traduce con Amazon Translate (`es` → `en`) y luego pasa por `DetectToxicContent`. Las dos son APIs de AWS.
- **Fotos:** Rekognition no acepta WebP. `PhotoProcessor` agrega una copia JPEG (`ProcessedPhoto.ModerationCopy`) que se modera y nunca se guarda.
- **Integración:**
  - `PhotoUploadService` (productos) y `ProfilePhotoService` (perfil): moderan antes de `PutAsync`;
  - `ListingService.PublishAsync`: modera el título y la descripción antes de insertar.
- **Permisos:** el rol de las instancias tiene `rekognition:DetectModerationLabels`, `comprehend:DetectToxicContent` y `translate:TranslateText` (statement `Moderar`; estas acciones no admiten restringir el recurso). Infra `0.3.3`.

## Contratos
- **Errores** ([`docs/errores.md`](../../../docs/errores.md)):
  - `moderation.image_rejected` (422);
  - `moderation.text_rejected` (422, el mensaje nombra el campo);
  - `moderation.unavailable` (503).
- **Filas afectadas** en [`docs/endpoints.md`](../../../docs/endpoints.md): `POST /products`, `POST /products/photos` y `PUT /profiles/me/photo`.
- **Configuración:** `Moderation:*` (`01-04`).
- **Paquetes:** `AWSSDK.Rekognition`, `AWSSDK.Comprehend` y `AWSSDK.Translate` (constitución §6; patrón `AWSSDK.*` ya mapeado).

## Tests
- **`UnitTest/Platform/ModerationTests.cs` (17):**
  - qué etiquetas y qué confianza rechazan;
  - el umbral de toxicidad;
  - las reglas del moderador en memoria;
  - la copia JPEG;
  - el fail-fast fuera de Development.
- **`IntegrationTest/Moderation/ModerationFlowTests.cs` (4):**
  - una foto inapropiada responde 422 en productos y en perfil, y no se guarda;
  - un texto inapropiado responde 422 nombrando el campo, y no se publica;
  - lo apropiado se publica.

## Criterios
- [x] Ninguna foto ni texto publicado se guarda sin pasar por `IContentModerator`.
- [x] Los rechazos llevan un mensaje en español y nombran el campo.
- [x] Si AWS no responde, no se guarda nada y la respuesta es `503`, nunca `500`.
- [x] El costo estimado está en `infra/README.md` (~1 USD/mes en el piloto).
- [ ] Probado contra AWS después de `infra-up`: una foto normal se publica.

## Límites
- **Sin cola de pendientes:** si AWS falla se pide reintentar en lugar de guardar como pendiente. El `outbox` y la revisión humana llegan con HU-39 (`NOTE (HU-39)`).
- **Pendientes:** `NOTE (HU-13)`, `NOTE (HU-16)`, `NOTE (HU-20)` y `NOTE (HU-28)` moderan su texto con el mismo `IContentModerator`.
- **Umbrales:** se ajustan con datos del piloto.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador) · 03-Nov-2026 (implementación)
- **Detalle:**
  - texto en español con Translate + Comprehend (la toxicidad de Comprehend solo acepta inglés, según la documentación de AWS consultada el 03-Nov-2026);
  - se rechaza en lugar de dejar pendiente cuando AWS no responde.

# 02-07 — Moderación de contenido antes de guardarlo

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-86 / S3–S4 / Plataforma / musubi-app: mensajes de rechazo en las pantallas de cada HU

## Contexto
Los lineamientos piden moderar texto, imágenes y video con las APIs de la nube **antes de guardar** (L-16b).

Lo que hoy se guarda sin moderar:
- fotos y textos de publicaciones (HU-06);
- foto de perfil (HU-04);
- después: solicitudes (HU-13), ofertas (HU-16), mensajes (HU-20) y reportes (HU-28).

Los `NOTE (02-07)` del código y de las specs apuntan aquí.

**Depende de:** `HU-06` (`PhotoProcessor` y `Platform/Media`) y `02-02` (`outbox`, para revisiones diferidas).

## Requisitos
1. **Imágenes:** Amazon Rekognition (`DetectModerationLabels`) sobre la foto ya procesada.
   - Si hay etiquetas prohibidas (desnudez, violencia, armas, drogas) por encima del umbral, la foto se rechaza y no se guarda en S3.
2. **Texto:** títulos, descripciones, solicitudes y mensajes pasan por un servicio de moderación de texto antes de guardarse.
   - Si se rechaza, se responde con el campo marcado y un mensaje en español.
3. **Video:** la app no sube video (la maqueta no lo incluye). Si se agrega, aplica la misma regla.
4. **Degradación (RNF-15):** si el proveedor no responde, el contenido queda `moderation.status = pending` y oculto hasta revisarse (`outbox`). Nunca se publica sin moderar.
5. **Rastro:** cada decisión queda en `moderationLog` (quién, qué, resultado, umbral), sin guardar el contenido rechazado.
6. **Revisión humana:** el administrador ve lo pendiente y lo reportado (HU-39).

## Diseño
- **`Platform/Moderation/`:** `IContentModerator` con un adaptador AWS y uno en memoria (Development y pruebas, con reglas deterministas).
- **Umbrales** en configuración, por categoría de etiqueta.
- **Integración:** `PhotoUploadService` (HU-06), `ProfilePhotoService` (HU-04) y los servicios de texto de cada HU llaman al moderador antes del repositorio.
- **Decisión pendiente, texto en español:** Amazon Comprehend detecta toxicidad solo en algunos idiomas. Al detallar hay que verificar el soporte de español y, si no lo tiene, elegir una alternativa con costo cero (lista de términos + modelo, u otro servicio).

## Contratos
- Errores nuevos:
  - `moderation.image_rejected` (422);
  - `moderation.text_rejected` (422, con el campo);
  - `moderation.unavailable` (202 o pendiente, según la decisión).
- Configuración: `Moderation:*` (`01-04`).
- El rol de las instancias tiene permiso de `rekognition:DetectModerationLabels` (Terraform).

## Tests
- `UnitTest/Platform/`: el umbral decide; si el proveedor falla, queda `pending`.
- `IntegrationTest/`:
  - una foto marcada por el moderador falso responde 422 y no llega al almacenamiento;
  - un texto marcado responde 422 con el campo.
- `SecurityTest/`: el contenido pendiente no aparece en la vitrina pública.

## Criterios
- [ ] Ninguna foto ni texto se guarda sin pasar por `IContentModerator`.
- [ ] Rechazos con mensaje en español y el campo marcado.
- [ ] Si se apaga el proveedor, el contenido queda pendiente y oculto, sin error 500.
- [ ] Costo estimado dentro de los créditos (Rekognition y Comprehend), registrado en `infra/README.md`.

## Límites
- **Decisión pendiente:** proveedor de moderación de texto en español.
- Los umbrales iniciales se ajustan con datos del piloto.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** spec técnica creada con el backlog en borrador (SCRUM-86). Cubre L-16b.

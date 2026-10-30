# 02-04 — Rendimiento y endurecimiento HTTP

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-86 / S3 / Plataforma / N/A

## Contexto
Los lineamientos piden endpoints eficientes (L-15d), además de rate limit, middleware, compresión y serializadores (L-15i).

Hoy:
- el rate limit existe solo para `auth` (por IP) y `uploads` (por usuario);
- los encabezados de seguridad los pone CloudFront (`SecurityHeadersPolicy`);
- los límites de Kestrel vienen de la configuración (01-02).

Esta spec cierra lo que falta.

**Depende de:** `01-02` (pipeline), `02-03` (errores) y `02-05` (CloudFront).

## Requisitos
1. **Compresión** Brotli y Gzip de las respuestas JSON (con el nivel medido). CloudFront comprime también: se evita comprimir dos veces.
2. **Rate limit global por usuario o IP**, con respuesta 429 + `Retry-After` en `problem+json` (`rate_limit.exceeded`) y límites por grupo:
   - lectura;
   - escritura;
   - `auth`;
   - `uploads`.
3. **Timeouts por petición** y cancelación propagada a MongoDB (`CancellationToken`).
4. **Límites de cuerpo por endpoint:**
   - JSON pequeño por defecto;
   - las fotos con su propio límite (HU-06).
5. **Encabezados de seguridad también en la API**, como defensa si alguien llega sin CloudFront: `X-Content-Type-Options` y `Referrer-Policy`, y HSTS fuera de Development (ya existe).
6. **Serialización:** `System.Text.Json` con source generators en los DTOs más usados, si la medición lo justifica.
7. **Tiempo de respuesta medido:** p95 de la vitrina y del detalle dentro del umbral de 00-03 (NFR), con una prueba de carga reproducible.

## Diseño
- **Middleware en `Platform/Hosting`:** compresión, timeouts y rate limiter.
- **Endpoints:** atributos de rate limit y de tamaño por endpoint.
- **Carga:** script de prueba de carga en `scripts/` (k6 o NBomber, con la regla de 00-01). No corre en el CI de PR.

## Contratos
- `429` con `Retry-After` y el código `rate_limit.exceeded` ([`docs/errores.md`](../../../docs/errores.md)).
- Configuración: `RateLimiting:*` y `Compression:*` (`01-04`).

## Tests
- `IntegrationTest/Platform/`:
  - `Content-Encoding: br` con `Accept-Encoding: br`;
  - el límite de lectura devuelve 429 al superarse;
  - un timeout responde `problem+json`.
- `SecurityTest/`: encabezados presentes en Production y cuerpo grande rechazado (413).

## Criterios
- [ ] Respuestas JSON comprimidas, medidas antes y después (bytes).
- [ ] Rate limit global activo con 429 + `Retry-After`.
- [ ] p95 de `GET /api/v1/products` ≤ el umbral de 00-03 con la carga definida.
- [ ] Build 0/0, pruebas al 100 % y critic PASS.

## Límites
- La prueba de carga es manual (no está en el CI de PR).
- **Decisión pendiente:** herramienta de carga.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** spec técnica creada con el backlog en borrador (SCRUM-86). Cubre L-15d y L-15i.

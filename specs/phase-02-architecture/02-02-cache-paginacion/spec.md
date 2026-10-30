# 02-02 — Caché en 3 niveles, paginación y trabajo en segundo plano

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-86 / S3–S4 / Plataforma / musubi-app: caché local de consultas (`NOTE (HU-53)`, L-16h)

## Contexto
Los lineamientos piden caché en la base, en el back y en el front (L-09). También piden paginación, índices y serializadores eficientes (L-15i).

La vitrina ya pagina por cursor (HU-06) y los índices siguen la regla ESR (02-01). Falta:
- la caché compartida entre las 2 instancias;
- la caché de respuestas HTTP;
- un patrón para el trabajo en segundo plano (`outbox`, R14), que el modelo de datos asigna a esta spec.

**Depende de:** `02-01` (modelo e índices), `02-05` (2 instancias detrás del ALB) y `HU-06` (vitrina y categorías).

## Requisitos
1. **Caché de datos compartida (cache-aside):**
   - categorías, populares y perfiles públicos se leen de Redis o Valkey antes que de MongoDB;
   - se invalidan al escribir;
   - las dos instancias ven la misma caché.
2. **Caché de respuestas HTTP:**
   - las respuestas públicas y estables (categorías, vitrina de visitante) llevan `Cache-Control` y `ETag`, y responden `304` si no cambiaron;
   - nada con sesión se guarda en una caché compartida.
3. **Paginación uniforme:** toda lista crece por cursor (`cursor`/`limit` con un tope), nunca con `skip`.
4. **Degradación:** si la caché no responde, la API sigue leyendo de MongoDB (RNF-15); nunca devuelve error por la caché.
5. **`outbox` (R14):**
   - los efectos pesados (coincidencias, avisos, refresco de copias) se escriben como evento junto con el cambio;
   - se procesan en segundo plano con reintentos;
   - el TTL de procesados es de 7 días.

## Diseño
- **`Platform/Caching/`:** una interfaz `ICache`, con adaptadores Redis y en memoria (Development y pruebas). Las llaves llevan versión y campus.
- **Salida HTTP:** el output caching de ASP.NET Core, con políticas por endpoint y solo para respuestas anónimas.
- **`Platform/Outbox/`:** colección `outbox` (ver `docs/modelo-datos.md`), un `BackgroundService` con lease para que no lo procesen dos instancias a la vez, y handlers por tipo de evento.
- **Decisión pendiente:** dónde vive Redis con costo cero. Opciones:
  - Redis Cloud gratuito (30 MB, compartido);
  - Valkey en contenedor en cada instancia (no compartido: obliga a TTL cortos);
  - ElastiCache (tiene costo).

## Contratos
- **Encabezados:** `Cache-Control`, `ETag` e `If-None-Match` en las filas públicas de `docs/endpoints.md`.
- **Configuración:** `Cache:Provider`, `Cache:ConnectionString` (Parameter Store) y `Outbox:*`. Se registran en `01-04`.

## Tests
- `UnitTest/Platform/`: llaves, invalidación y degradación cuando falla la caché.
- `IntegrationTest/`:
  - `304` con `If-None-Match`;
  - una respuesta con sesión nunca sale con `public`;
  - el `outbox` procesa y reintenta.
- `DatabaseTest/`: índice `ix_status_next` y TTL del `outbox`.

## Criterios
- [ ] Una segunda lectura de categorías no consulta MongoDB (contador en la prueba).
- [ ] Si se apaga la caché, la API responde igual y `/health/ready` sigue sano.
- [ ] `GET /api/v1/categories` responde `304` con un `ETag` vigente.
- [ ] Un evento del `outbox` se procesa una sola vez aunque corran 2 instancias.
- [ ] Build 0/0, pruebas al 100 % y critic PASS.

## Límites
- La caché local de la app (tercer nivel) es de `musubi-app` (HU-53 y L-16h).
- **Decisión pendiente:** proveedor de Redis con costo cero.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** spec técnica creada con el backlog en borrador (SCRUM-86). Cubre L-09 y L-15i.

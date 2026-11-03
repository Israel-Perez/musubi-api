# 02-02 — Caché en 3 niveles y paginación

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-91 (antes SCRUM-86) / S4 / Plataforma / [musubi-app:02-06](https://github.com/Israel-Perez/musubi-app/tree/main/specs/phase-02-architecture/02-06-cache) (caché de la app)

## Contexto
El documento de la materia pide caché a nivel base de datos, backend y frontend (requisito 9). También pide "Caching, Rate limited, Pagination, Indexing" en el rendimiento de la API (requisito 15), y Redis distribuido (v2).

Lo que ya existía:
- la vitrina pagina por cursor (HU-06);
- los índices siguen la regla ESR (02-01).

Faltaban la caché de datos compartida entre las 2 instancias y la caché de las respuestas HTTP.

**Depende de:** `02-01` (modelo e índices), `02-05` (2 instancias detrás del ALB) y `HU-06` (vitrina y categorías).

## Requisitos
1. **Caché de datos (cache-aside), el nivel de la base:**
   - categorías y carreras se leen de la caché antes que de MongoDB, por comunidad, con TTL de 10 minutos;
   - con `Cache:ConnectionString`, la caché es Redis y la comparten las 2 instancias; sin ella, cada instancia usa su memoria.
2. **Caché de respuestas HTTP, el nivel del backend:**
   - la vitrina y las categorías **anónimas** salen de la caché de salida (30 s y 10 min);
   - publicar o eliminar una cuenta borra la vitrina de la caché (etiqueta `showcase`);
   - nada con sesión se guarda en la caché compartida ni se sirve de ella.
3. **GET condicional:** categorías, vitrina y carreras llevan `ETag` y `Cache-Control: no-cache`, y responden `304` sin cuerpo si el `If-None-Match` coincide. La app (el nivel del frontend) reutiliza su copia.
4. **Degradación:** si Redis no responde, la API lee de MongoDB y no devuelve error (RNF-15). Los tiempos de espera son de 1 a 2 s.
5. **Paginación uniforme:** toda lista crece por cursor (`cursor`/`limit` con tope), nunca con `skip`.

## Diseño
- **`Platform/Caching/`:**
  - `AppCache`: cache-aside sobre `IDistributedCache`, en JSON y con el prefijo de versión `v1:`. Solo degrada las fallas de la caché (`RedisException`, `TimeoutException`, `JsonException`); un error de MongoDB se propaga.
  - `AddMusubiCaching`:
    - la cadena se lee de forma lazy, con `IServiceProvider`;
    - con cadena, `RedisCache` (`AbortOnConnectFail=false`, tiempos cortos); sin ella, `MemoryDistributedCache`;
    - registra la caché de salida con las políticas `catalog` y `showcase`.
  - `[ConditionalGet]`: filtro de resultado que calcula el `ETag` (SHA-256 del JSON) y responde 304.
- **Repositorios:** `CachedCategoryRepository` y `CachedCareerRepository` decoran al de Mongo o al de memoria.
- **Pipeline:** `UseOutputCache` después de `UseAuthorization` (`01-02`, paso 11).
- **Perfil público sin caché:** el criterio de HU-04 pide que un cambio se vea de inmediato.
- **Redis gratuito:** Redis Cloud (30 MB) es compartido y fuera de AWS, como Atlas. ElastiCache tiene costo. La cadena va en Parameter Store y llega como `Cache__ConnectionString`.

## Contratos
- **Encabezados:** `ETag`, `Cache-Control: no-cache` e `If-None-Match` → `304` en las filas de categorías, vitrina y carreras de [`docs/endpoints.md`](../../../docs/endpoints.md).
- **Configuración:** `Cache:ConnectionString` (`01-04`; secreto en `/musubi/prod/Cache/ConnectionString`, opcional).
- **Paquetes:** `Microsoft.Extensions.Caching.StackExchangeRedis` y `StackExchange.Redis` (Redis está en el stack de la constitución §6). El mapeo de `nuget.config` suma `StackExchange.Redis` y `RESPite`.

## Tests
- **`UnitTest/Platform/CachingTests.cs` (5):**
  - la segunda lectura no toca la fuente;
  - cada comunidad tiene su entrada;
  - con Redis caído se lee la fuente y no falla;
  - un error de la fuente no se oculta;
  - `RemoveAsync`.
- **`IntegrationTest/Caching/CachingFlowTests.cs` (4):**
  - `ETag` y 304 en categorías y vitrina;
  - 304 en carreras con sesión;
  - la vitrina del visitante muestra lo recién publicado. Prueba de mordida: sin la invalidación, falla.
- **`SecurityTest/Caching/OutputCacheTests.cs` (1):** la vitrina anónima sale de la caché (`Age`); con sesión, nunca.

## Criterios
- [x] Una segunda lectura de categorías no consulta la fuente (`SecondRead_OfCategories_DoesNotTouchTheSource`).
- [x] Con Redis caído la API responde igual (`RedisDown_ReadsTheSource_AndNeverFails`).
- [x] `GET /api/v1/categories` responde `304` con un `ETag` vigente.
- [x] Una respuesta con sesión no sale de la caché de salida.
- [x] Build 0/0, pruebas al 100 % y critic PASS.

## Límites
- **La caché de salida es por instancia:** la otra instancia ve una publicación nueva a más tardar en 30 s. Con Redis se podría compartir (`Microsoft.AspNetCore.OutputCaching.StackExchangeRedis`, otro paquete), pero no hace falta con esa vida tan corta.
- **`NOTE (HU-18)`/`NOTE (HU-31)`:** el `outbox` (R14) nace con su primer consumidor (coincidencias y avisos push).
- **`NOTE (HU-11)`:** cachear "Populares" cuando exista.
- **`NOTE (HU-41)`:** borrar la entrada de categorías o carreras al editarlas.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador) · 03-Nov-2026 (implementación)
- **Detalle:**
  - Redis Cloud gratuito como Redis compartido, opcional (sin cadena, memoria);
  - el `outbox` se movió a su primer consumidor;
  - el perfil público no se cachea (HU-04).

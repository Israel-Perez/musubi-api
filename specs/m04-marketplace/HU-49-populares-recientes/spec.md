# HU-49 — Ver populares y recién publicados

**HU / Jira / Sprint / Módulo / Contraparte:** HU-49 / [SCRUM-69](https://uttt-startup-musubi.atlassian.net/browse/SCRUM-69) / Sprint 4 (12-Nov-2026; según comentario Jira) / M-04 / [musubi-app:HU-49](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-49-populares-recientes)

## Contexto

Como comprador, quiero descubrir productos populares de hoy o de la semana y publicaciones recientes de mi campus sin tener que buscarlos. La historia proviene de la maqueta: Inicio, Recien-publicados y Recien-publicados-antiguas.

Jira confirma que el endpoint existente de productos ya soporta orden reciente/antiguo, cursor y caché, y propone contar aperturas del detalle en un agregado separado. Decisión del usuario (12-Nov-2026): cada apertura válida cuenta, incluso anónima o repetida, salvo la del vendedor; Hoy es el día UTC y Semana los últimos siete días UTC. **Depende de:** `02-01`, `02-02`, `02-03`, `HU-11` y HU-10 para filtros/orden de la vitrina.

## Requisitos

1. Mostrar en Inicio una sección Populares con selector Hoy/Semana y productos del campus correspondiente, ordenados por las vistas del periodo. Hoy es el día UTC actual y Semana son los últimos 7 días UTC.
2. Ofrecer la lista de recién publicados del más nuevo al más antiguo y permitir continuar hacia publicaciones más antiguas. Reutilizar `GET /api/v1/products` con sus parámetros `sort`, `cursor` y `limit`, sin duplicar contrato si cubre la pantalla.
3. Paginar el ranking con cursor/continuación estable y limitar tamaño de página conforme al contrato existente de la vitrina, salvo decisión de contrato justificada.
4. Contabilizar vistas en un agregado independiente `productStatsDaily`, nunca incrementando un campo caliente en `products` (R9). Una vista es cada apertura válida del detalle; incluir visitantes anónimos, excluir al vendedor y no deduplicar aperturas repetidas (decisión del usuario, 12-Nov-2026).
5. Aislar consulta y resultados por comunidad/campus (RNF-21, modelo R1). No devolver productos ocultos, quitados ni fuera del campus.
6. No exponer conteos ni identidades de quienes vieron un producto. No almacenar datos personales para deduplicar vistas sin decisión expresa (RNF-05/RNF-07).
7. Servir ranking desde caché pública con variación por campus y periodo, sin que una sesión reciba una respuesta pública incompatible. TTL y objetivo de frescura: 🎯 Objetivo (Por medir); Jira sugiere “unos minutos”, sin fijar valor.
8. Aplicar en Mongo el filtro de comunidad/estado, orden y paginación antes de materializar productos; la consulta del ranking no realizará una lectura individual por producto (OWASP API4).

RNF aplicables: RNF-03 (sesión y acceso explícito cuando aplique), RNF-05/RNF-07 (privacidad), RNF-21 (multi-comunidad), RNF-23 (módulos) y RNF-25 (pruebas automatizadas). También aplican R1, R2, R5, R9 y R13 de `docs/modelo-datos.md` y OWASP API1/API4.

## Diseño

- **Módulo:** `Musubi.Api/Modules/Listings/`; M-04 reutiliza la vitrina de M-03 según `docs/modulos.md`. No crear `Modules/Marketplace/`.
- **Ranking:** `GET /api/v1/products/popular?period=today|week&campus=&cursor=&limit=`; con sesión usa comunidad del token e ignora `campus`, sin sesión resuelve campus como `GET /products`. Respuesta `PopularProductPage { items: [{ product: ProductResponse, views }], nextCursor, total }`. `views` es conteo agregado, no identifica visitantes. Límite 1–50, 20 por defecto; cursor opaco keyset sobre vistas, fecha e id.
- **Recientes:** reutilizar `GET /api/v1/products` con `sort=newest|oldest`, `cursor` y `limit`. El endpoint ya tiene caché de salida y validación de campus.
- **Registro:** `ListingService.DetailAsync` incrementa en cada respuesta válida de detalle, incluso visitante; omite cuando `userId` es el vendedor. El conteo usa interfaz/repositorios Mongo e InMemory y colección `productStatsDaily`; R9 exige `$inc` + upsert.
- **Índices/retención:** `ux_product_day`, `ix_community_day_views` y TTL de 30 días, conforme al catálogo canónico. La consulta semanal filtra solo siete días UTC.
- **Consulta y recursos (OWASP API4):** Mongo filtra por comunidad/rango máximo de siete días (índice `ix_community_day_views`), agrupa, hace `$lookup` con `products`, descarta en base los estados/comunidades no elegibles y pagina por keyset con `$facet`; `total` se calcula sin cursor. El servicio no carga el ranking completo ni hace N+1, y no acepta offsets arbitrarios. La implementación en memoria mantiene orden/filtrado/cursor para pruebas. La respuesta pública mantiene el output cache de 30 s.
- **Script:** tras integrar `origin/main` con `db/021-ventas-compras.js` y esquema 21, HU-49 usa `db/022-popularidad.js` y `MongoSchema.ExpectedVersion = 22`; no se renumeran scripts ya aplicados.
- **Caché:** política de salida pública variada por campus y periodo; usar el TTL medido/configurado del catálogo y documentar frescura observada. La ventana “today” es UTC day; “week” filtra `day >= utcToday - 6 days`.
- **Contraparte:** la app implementa selector Hoy/Semana y navegación “Ver todo” para recientes en la spec enlazada; esta spec no afirma que esa contraparte ya esté implementada.

## Contratos

- **Fila existente a conservar:** [`docs/endpoints.md`](../../../docs/endpoints.md), `GET /api/v1/products` (orden, cursor, filtros y caché).
- **Fila implementada:** `GET /api/v1/products/popular?period=today|week` en [`docs/endpoints.md`](../../../docs/endpoints.md): respuesta paginada con cursor opaco, auth opcional, errores y caché.
- **Detalle existente:** [`docs/endpoints.md`](../../../docs/endpoints.md), `GET /api/v1/products/{id}`; la respuesta pública actual no debe revelar estadísticas.
- **Datos:** fila `productStatsDaily` en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); script `db/022-popularidad.js` y `MongoSchema.ExpectedVersion = 22`.
- **Fixture:** `ContractTest/Fixtures/popular-product-page.json` fija la forma del ranking.
- **Errores:** catálogo [`docs/errores.md`](../../../docs/errores.md): errores existentes de periodo, cursor y campus.

## Tests

- `UnitTest/Data/SchemaVersionTests.cs`: scripts consecutivos y versión 22.
- `IntegrationTest/Listings/PopularProductsFlowTests.cs`: aperturas anónimas/repetidas, exclusión del vendedor, periodos, campus, cursor keyset y semántica InMemory.
- `SecurityTest/Auth/ListingsAuthTests.cs`: endpoint público y token de sesión restringido a su comunidad aunque se envíe `campus` ajeno.
- `ContractTest/ListingsContractTests.cs` + `Fixtures/popular-product-page.json`: forma estable del ranking y seller nulo para visitante.
- `DatabaseTest/SchemaScriptTests.cs` y `DatabaseTest/MongoListingRepositoryTests.cs`: script idempotente, validador, índices, TTL, agregación Mongo, total y keyset.
- `PerformanceTest/`: medir ranking y política de caché antes de fijar umbrales; objetivo queda “🎯 Objetivo (Por medir)”.
- En la contraparte Flutter: pruebas de selector, navegación, paginación y estados de carga/vacío/error (ver spec hermana).

## Criterios

- [ ] Inicio permite elegir Hoy/Semana y muestra productos del campus en orden descendente de vistas → suite Flutter completa pendiente por SDK incompatible.
- [ ] Abrir “recién publicados” muestra de más nuevo a más antiguo y permite continuar con cursor → suite Flutter completa pendiente por SDK incompatible.
- [x] Cada apertura válida suma una vista salvo la del vendedor; anónimos y repeticiones cuentan → Integration.
- [x] `today` cuenta el día UTC actual y `week` los últimos 7 días UTC → Integration y Unit.
- [x] Una petición de ranking de otra comunidad no devuelve publicaciones ajenas y no revela conteos/identidad → Integration y Security.
- [x] `productStatsDaily` usa incremento atómico, índices y TTL del modelo → Database y SchemaScriptTests.
- [x] Caché diferencia campus/periodo y no filtra una respuesta a una sesión incompatible → Security/Caching; TTL queda como 🎯 Objetivo (Por medir).
- [x] El ranking pagina y cuenta productos elegibles dentro de una agregación Mongo con `$lookup`/`$facet`, sin N+1 ni materialización de todo el ranking en el servicio → `DatabaseTest/MongoListingRepositoryTests.cs` y `IntegrationTest/Listings/PopularProductsFlowTests.cs`.
- [x] Build Release 0 errores/0 advertencias; Unit 615, Integration 173, Security 223, Contract 23 y Database 145 en verde; `check_endpoints` OK y `critic-guardrails` PASS (13-Nov-2026, tras integrar HU-26).

## Límites

- Decisión del usuario (12-Nov-2026): cada apertura válida del detalle cuenta, incluyendo anónimos y repeticiones, salvo la apertura del vendedor; Hoy = día UTC actual y Semana = últimos 7 días UTC.
- No se fija ahora TTL de caché, p95 ni carga objetivo; requiere medición empírica.
- No se añade analítica con IP, user-agent, historial individual ni identificadores de visitantes.
- Sin historial de vistas visible al usuario ni total público de vistas.
- No se crea una ruta adicional para recientes mientras `GET /products` cubra el flujo.
- Implementación Flutter vive en el repo contraparte y requiere su propia spec/PR; la suite completa está pendiente de un SDK Dart compatible (el disponible es 3.12.2, el proyecto requiere ^3.13.5).

## Aprobación y Control de Cambios

- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 13-Nov-2026
- **Detalle:** Implementada sobre `origin/main` con `db/021`/esquema 21; HU-49 agrega `db/022`/esquema 22. Se optimizó el ranking con agregación, join y paginación Mongo para evitar N+1 y cargar toda la lista en el servicio. Build y suites API verificadas el 13-Nov-2026; suite Flutter completa pendiente por SDK insuficiente. Falta revisor indicado por la usuaria; no aprobada.

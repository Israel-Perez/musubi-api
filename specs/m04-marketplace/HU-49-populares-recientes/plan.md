# Plan — HU-49 Ver populares y recién publicados

## Componentes previstos

- Reutilizar `Musubi.Api/Modules/Listings/`; M-04 reutiliza la vitrina de M-03 según `docs/modulos.md`.
- Extender `ProductsController` con `GET /api/v1/products/popular` y `ListingService` con la consulta del periodo, salvo que el detalle de implementación justifique separar el controlador.
- Incorporar repositorio Mongo/InMemory para `productStatsDaily`, documento y contrato compartido; registrar dependencias en `ListingsModuleServiceCollectionExtensions`.
- `ListingService.DetailAsync` registra la vista válida; nunca actualizar `products` con un contador.
- Crear `db/022-popularidad.js` y subir `MongoSchema.ExpectedVersion` a 22, ya que `origin/main` trae `db/021-ventas-compras.js` y esquema 21; no reescribir scripts ya aplicados.
- Enlazar la fila `productStatsDaily` ya diseñada en `docs/modelo-datos.md`; al implementar, actualizar también `db/README.md`, `docs/endpoints.md`, `docs/errores.md` si aparece algún error nuevo y `docs/trazabilidad.md`.
- La contraparte es `musubi-app:HU-49` en `specs/m04-marketplace/HU-49-populares-recientes/`; este plan no sustituye su spec ni autoriza cambios fuera de este repositorio.

## Decisiones y bloqueos

1. **Decisión confirmada por el usuario:** una vista es abrir el detalle; no cuenta la vista propia del vendedor. Se registra durante `GET /products/{id}` conforme a la propuesta Jira.
2. **Ventana temporal (decisión del usuario, 12-Nov-2026):** cada apertura cuenta, incluso anónima y repetida, salvo la del vendedor. `today` es el día UTC actual; `week` son los últimos 7 días UTC.
3. **Popularidad:** `GET /api/v1/products/popular?period=today|week`, limitada al campus de sesión o al campus visitante pedido de acuerdo con las reglas de vitrina. Solo devolver publicaciones disponibles y no exponer estadísticas individuales.
4. **Orden y empates:** ordenar por vistas descendentes; desempate estable por fecha de publicación descendente y después id ascendente.
5. **Caché:** respuesta pública variada por `campus` y `period`; visitante y sesión se aíslan si el contrato devuelve seller personalizado. Conservar TTL de catálogo como punto de partida y medir frescura/latencia antes de fijar un nuevo valor.
6. **Agregados diarios:** seguir `productStatsDaily` existente en el modelo y R9: `$inc` + upsert, índices `ux_product_day` y `ix_community_day_views`, TTL de 30 días. Verificar cómo se consultan siete días sin exceder memoria/recursos; no fijar umbrales sin medición.
7. **Consulta del ranking (OWASP API4):** filtrar por comunidad y ventana máxima de siete días con índice; agrupar por producto, usar `$lookup` a `products`, filtrar comunidad/estado activo y paginar por cursor keyset dentro de `$facet` para obtener página y total. No consultar cada producto por separado, aceptar offsets arbitrarios ni materializar todos los productos candidatos en el servicio. La respuesta usa output cache 30 s; el fake reproduce elegibilidad, orden, total y continuación.
8. **Recién publicados:** reutilizar `GET /api/v1/products` y sus parámetros `sort=newest|oldest`, cursor y caché existentes. No crear otra ruta salvo que la contraparte detecte una necesidad contractual que no cubra ese endpoint.

## Guardarraíles

- R1/R9/R13 de `docs/modelo-datos.md`: incluir campus, usar contador separado y validar documentos en Mongo.
- Mantener `[AllowAnonymous]`/sesión opcional según contrato actual de vitrina y detalle; declarar auth explícita y cubrir visitante, usuario del mismo campus, otro campus y vendedor.
- El ranking no debe revelar conteos de vistas por producto ni identidad de quienes lo abrieron. No persistir IP, user-agent ni identificadores de visitantes para deduplicar sin aprobación expresa.
- Excluir publicaciones `hidden`, `removed` y, según contrato acordado, `soldOut`; validar también campus en la consulta para evitar IDOR y filtración entre comunidades.
- Respetar paginación acotada y límites anti-abuso; el detalle no debe fallar por una degradación no crítica del contador si se aprueba esa política.
- Mantener los errores en `problem+json`; sin `try/catch` en controllers.

## Validación prevista

- Build Release sin errores ni advertencias.
- Unit: semántica periodo, orden, límites, aislamiento por campus, exclusión del vendedor según decisión aprobada y degradación del registro.
- `TestShared/Listings`: contrato común de repositorio Mongo/InMemory, idempotencia/upsert y expiración/consulta.
- Integration: apertura de detalle y ranking para `today`/`week`, campus y estados de publicaciones; `GET /products` mantiene orden reciente/antiguo y cursor.
- Security: 401 donde aplique; acceso cruzado entre campus no filtra productos ni estadísticas; vistas propias según decisión de PO; no hay respuesta personalizada servida desde caché pública.
- Contract: fixture del ranking y de las respuestas existentes sin cambios incompatibles.
- Database: script idempotente, validador, índices y TTL reales en MongoDB.
- Performance: medir antes de fijar TTL, p95 o límite de carga; dejar “🎯 Objetivo (Por medir)” hasta tener evidencia.
- `pwsh scripts/check_endpoints.ps1` → OK; `pwsh scripts/critic-guardrails.ps1` → PASS.

## Secuencia

1. Registrar en Jira/spec la definición del usuario: cada apertura válida, anónimos y repetidos incluidos, vendedor excluido y periodos UTC.
2. Conciliar esta spec API con la contraparte Flutter: contrato, campus visitante/autenticado, estados vacíos y selección Hoy/Semana.
3. Cerrar requisitos, esquema y nombres de rutas; actualizar `spec.md` y criterios antes del scaffolding/implementación.
4. Implementar repositorio compartido, script Mongo y pruebas de datos; después servicio/controlador y pruebas de integración/seguridad/contrato.
5. Actualizar catálogo de endpoints, modelo de datos, errores (si aplica), trazabilidad y Memoria; verificar suite y guardarraíles.
6. Cerrar como “🚧 Borrador con evidencia”; solo cambiar a “✅ Aprobado” con revisor indicado por la usuaria y PR verde.

# db — scripts de la base (MongoDB)

Scripts `mongosh`, numerados e idempotentes, que crean las colecciones, sus validaciones `$jsonSchema`, sus índices y los datos semilla. Juntos forman el **script de la base de datos** que se entrega en la evaluación (lineamiento 19). El modelo completo está en [`docs/modelo-datos.md`](../docs/modelo-datos.md).

| Script | Qué crea | Versión del esquema |
|---|---|---|
| `001-identidad.js` | `communities` (+ UTTT), `users`, `verificationCodes`, `refreshTokens`, `_meta` | 1 |
| `002-publicaciones.js` | `categories` (+ 6 de la UTTT), `products` | 2 |
| `003-dominio-revision.js` | Dominio `musubiapp.com.mx` en la UTTT, para la cuenta de los revisores de Google Play (SCRUM-88) | 3 |
| `004-perfiles.js` | `profiles`, `careers` (+ 38 de la UTTT, a validar) y `communities.academicTerm` (UTTT: 11 cuatrimestres) (HU-04) | 4 |
| `005-chat.js` | `conversations` y `messages` (HU-20) | 5 |
| `006-chat-bandeja.js` | No leídos por lado y copia de nombres y portada en `conversations` (HU-22) | 6 |
| `007-busqueda.js` | Palabras normalizadas en `products.searchWords` + índice `ix_community_status_search` (HU-09) | 7 |
| `008-reportes-bloqueos.js` | `reports` (HU-28) y `blocks` (HU-57) | 8 |
| `009-solicitudes.js` | `wantedRequests` (HU-13) | 9 |
| `010-filtros-precio.js` | Índices de precio asc/desc, con y sin categoría, para ordenar sin memoria (HU-10) | 10 |
| `011-ofertas.js` | `offers` ("Yo tengo este producto", HU-16): una por persona y solicitud | 11 |
| `012-ofertas-chat.js` | Conversaciones de una oferta (`context.type` `offer`) y ofertas aceptadas o rechazadas (HU-17) | 12 |

## Aplicar

Se aplican **en orden** contra la base que indica la cadena de conexión. Correrlos otra vez no cambia nada.

**En la nube los aplica `infra-up`** antes de reemplazar las instancias (paso "Esquema de la base", con la cadena de Parameter Store). A mano solo hace falta en local o para revisar un script antes del despliegue.

```powershell
# Local (deploy/docker-compose.local.yml)
Get-ChildItem db/*.js | Sort-Object Name | ForEach-Object {
  mongosh "mongodb://musubi:<clave>@localhost:27017/musubi?authSource=admin" --quiet --file $_.FullName
}
# Atlas: la misma línea con la cadena mongodb+srv:// (secreto; nunca en el repo)
```

## Reglas

- Un cambio de esquema es un **script nuevo** (`NNN-<slug>.js`); nunca se edita uno ya aplicado en la nube.
- Cada script sube `_meta.version` con `$max` y se registra en `_meta.applied`.
- `MongoSchema.ExpectedVersion` (en `Musubi.Api/Platform/Data/`) debe ser igual al número del último script. Lo verifica `UnitTest/Data/SchemaVersionTests.cs`. Si la base tiene una versión menor, `/health/ready` responde Unhealthy y el balanceador no le manda tráfico a esa instancia.
- `DatabaseTest` corre los scripts reales contra un MongoDB en contenedor (Testcontainers) antes de probar los repositorios.

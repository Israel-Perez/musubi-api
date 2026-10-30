# db — scripts de la base (MongoDB)

Scripts `mongosh`, numerados e idempotentes, que crean las colecciones, sus validaciones `$jsonSchema`, sus índices y los datos semilla. Juntos forman el **script de la base de datos** que se entrega en la evaluación (lineamiento 19). El modelo completo está en [`docs/modelo-datos.md`](../docs/modelo-datos.md).

| Script | Qué crea | Versión del esquema |
|---|---|---|
| `001-identidad.js` | `communities` (+ UTTT), `users`, `verificationCodes`, `refreshTokens`, `_meta` | 1 |
| `002-publicaciones.js` | `categories` (+ 6 de la UTTT), `products` | 2 |
| `003-dominio-revision.js` | Dominio `musubiapp.com.mx` en la UTTT, para la cuenta de los revisores de Google Play (SCRUM-88) | 3 |

## Aplicar

Se aplican **en orden** contra la base que indica la cadena de conexión. Correrlos otra vez no cambia nada.

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

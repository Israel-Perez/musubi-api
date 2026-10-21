# 02-01 — Modelo de datos MongoDB e identidad (tareas)

## T1 — Catálogo y script de identidad
- **Crear:** `docs/modelo-datos.md`, `db/001-identidad.js`, `db/README.md`.
- **Verificar:** `DatabaseTest/SchemaScriptTests.cs` (idempotencia, índices, validación, readiness) en el job `database`.
- **Estado:** ✅ Hecho (21-Oct-2026).

## T2 — Convenciones, repositorios y fakes
- **Crear:** `Platform/Data/{MongoConventions,MongoSchema}.cs` y `Modules/Auth/{AuthModuleServiceCollectionExtensions.cs,Data/*}`.
- **Verificar:** `UnitTest/Data/*` (43/43 en la suite) y `DatabaseTest/MongoAuthRepositoryTests.cs`.
- **Estado:** ✅ Hecho (21-Oct-2026).

## T3 — Readiness con versión de esquema y CI
- **Modificar:** `Platform/Data/MongoHealthCheck.cs`, `.github/workflows/ci-pr.yml` (job `database`), `nuget.config`.
- **Verificar:** `actionlint` sin hallazgos; restore con caché vacía sin NU1100; job `database` en verde.
- **Estado:** ✅ Hecho (21-Oct-2026).

## Pendientes
- `NOTE (HU-56)`: recuperar la contraseña (está en la maqueta, sin HU); usará `verificationCodes` con el propósito `passwordReset`.
- `NOTE (02-05)`: aplicar `db/*.js` contra Atlas como paso del despliegue, antes del *instance refresh*.
- Cada HU que agregue colecciones: script `NNN-*.js`, fila en `docs/modelo-datos.md` y `MongoSchema.ExpectedVersion`.

# HU-54 — Plan de implementación

## Componentes
- `Musubi.Api/Modules/Profiles/`: endpoint autenticado, servicio, DTO, validador, documento Mongo y repositorios Mongo/InMemory.
- `db/015-preferencias.js`: colección `preferences`, validador e índice único por usuario; `MongoSchema.ExpectedVersion` pasa a 15 (HU-23 ya ocupa el script 014).
- `Musubi.Api/Modules/Auth/` integración del borrado de preferencias con HU-46 sin cambiar su contrato.
- Catálogos: `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `docs/versionado.md`; fixture de contrato.
- Tests: Unit, Integration, Security, Contract y Database en los proyectos existentes.

## Decisiones
- Tema como enum serializado en minúsculas: `system`, `light`, `dark`, alineado con Flutter `ThemeMode`.
- Un documento por usuario (`_id = userId`); GET ausente devuelve `system` sin materializar un registro; PUT hace upsert.
- `userId` procede del subject JWT y nunca del body; se comprueba que la cuenta siga activa antes de leer/escribir.
- Solo el tema entra al MVP. No se inventan tipos de notificación mientras Jira deja esa decisión pendiente.
- Se conserva el orden del pipeline y se reutiliza el patrón del módulo Profiles, errores y erasers actuales.

## Guardarraíles
- Autorización explícita en el controller; sin `try/catch`.
- Límite de request y validación del valor enum en la frontera; errores con catálogo y Problem Details.
- Repositorio Mongo y en memoria con comportamientos equivalentes; ningún dato de una cuenta se devuelve a otra.
- Sin secretos ni datos personales en logs; borrar preferencia en eliminación de cuenta.

## Validación
- Build Release 0 errores/0 advertencias antes de ejecutar suites.
- Unit, Integration, Security, Contract; DatabaseTest con Mongo real si Docker está disponible.
- `pwsh scripts/check_endpoints.ps1`, `pwsh scripts/critic-guardrails.ps1`, `git diff --check`.

## Secuencia
1. Completar el slice de API desde el scaffolder; endurecer errores, validación, auth y pruebas.
2. Añadir script 014, versión esperada, integración con borrado de cuenta y baterías Mongo.
3. Conciliar catálogos, fixtures, trazabilidad y Memoria.
4. Verificar y dejar commit aislado `SCRUM-71` en rama propia; no actualizar Jira desde este cambio.

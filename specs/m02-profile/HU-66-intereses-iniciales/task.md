# HU-66 — Elegir mis intereses la primera vez que entro (tareas API)

## T1 — Datos
- [x] `PreferenceDocument.Interests` y `SetInterestsAsync` (Mongo con `$setOnInsert theme`, memoria sin pisar el tema). `SetThemeAsync` en memoria conserva los intereses.

## T2 — Slice API
- [x] `InterestsController`, `InterestsService`, DTOs y `profiles.invalid_interest`; registro en `ProfilesModuleServiceCollectionExtensions`.

## T3 — Catálogos
- [x] `docs/endpoints.md` (88 filas), `docs/errores.md`, `docs/modelo-datos.md` (`preferences.interests`), `docs/trazabilidad.md` y `docs/versionado.md` (`0.5.0-alpha.28`, sección compartida).

## T4 — Evidencia y cierre
- [x] Unit 642 · Integration 177 · Security 227 · Contract 23 (14-Nov-2026). `DatabaseTest` en el CI.
- [x] `check_endpoints` OK, critic PASS, `check_shared_sections` OK.
- [ ] Revisor de la spec y transición de SCRUM-106 en Jira (responsable).

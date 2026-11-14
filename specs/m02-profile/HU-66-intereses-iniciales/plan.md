# HU-66 — Plan de implementación

## Componentes
- `Modules/Profiles/InterestsController.cs` (`/profiles/me/interests`, GET y PUT, `[Authorize]`).
- `Modules/Profiles/InterestsService.cs` + `IInterestsService.cs`: cuenta activa (`ProfileService.ActiveAccountAsync`), validación con `ICategoryDirectory`, sin repetidos.
- `Modules/Profiles/InterestsDtos.cs`: `InterestsResponse` y `UpdateInterestsRequest` (`[Required]`, `[MaxLength(20)]`).
- `Data/PreferenceDocument.Interests` y `IPreferenceRepository.SetInterestsAsync` (Mongo y memoria).

## Decisiones
- **En `preferences` y no en `profiles`:** el perfil público lee `profiles`; guardarlos aparte hace que la privacidad (RNF-07) no dependa de acordarse de filtrarlos. Además ya se borran con la cuenta (HU-46) y ya se editan desde Ajustes (HU-54).
- **Ruta propia (`/profiles/me/interests`) y no un campo más de `/preferences`:** el `PUT` de HU-54 recibe solo `theme`; agregarle `interests` obligaría a la app publicada a mandar los dos o a borrar uno sin querer.
- **Sin script en `db/`:** el validador de `preferences` acepta campos nuevos. Un `db/022` chocaría con el de HU-49 (PR #96), y ninguno de los dos lo necesitaría para funcionar.
- **Tope de 20:** el campus tiene 6 categorías; 20 deja margen sin abrir la puerta a listas enormes (OWASP API4).
- **Slugs, no nombres:** el nombre puede cambiar y HU-35 filtra por slug.

## Guardarraíles
- Sin `try/catch` en el controller; errores con `this.Respond` y el catálogo.
- `userId` solo de `MusubiClaims.Subject`.
- critic, `check_endpoints` y `check_shared_sections` (fila `0.5.0-alpha.28` replicada en `musubi-app`).

## Validación
- Build Release 0/0 y las 4 suites con `--no-build`.
- `DatabaseTest` en el CI (sin Docker local).

## Secuencia
1. Documento y repositorios → 2. Servicio, DTOs, controller y error → 3. Pruebas → 4. Catálogos (`endpoints`, `errores`, `modelo-datos`, `trazabilidad`, `versionado`) → 5. Memoria.

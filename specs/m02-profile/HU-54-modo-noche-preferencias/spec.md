# HU-54 — Elegir modo noche y preferencias

**HU / Jira / Sprint / Módulo / Contraparte:** HU-54 / SCRUM-71 / Sprint 4 / M-02 / [musubi-app:HU-54](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m02-profile/HU-54-modo-noche-preferencias)

## Contexto
Jira (SCRUM-71): Como **usuario**, quiero **cambiar entre modo claro y modo noche y ajustar mis preferencias**, para **usar la app con comodidad a cualquier hora**.

- Maqueta: `Ajustes`.
- Origen: maqueta (sección 10, G-07).
- **Depende de:** `02-01`, `02-03`, `HU-04`.

## Requisitos
1. Exponer preferencias solo para la cuenta autenticada; el cliente no puede elegir el `userId`.
2. Devolver `theme: "system"` si el usuario aún no guardó una preferencia.
3. Aceptar y persistir `system`, `light` o `dark`; rechazar cualquier otro valor con `profiles.invalid_theme`.
4. Borrar la preferencia al eliminar la cuenta (HU-46, R11).

## Diseño
- Módulo existente `Musubi.Api/Modules/Profiles/` (M-02), con documento, repositorio Mongo/en memoria, servicio, DTOs, validador y controller.
- Colección `preferences`, `_id = userId`, campo `theme` limitado a `system|light|dark` e índice único de `_id`.
- El usuario se obtiene solo de `MusubiClaims.Subject`; GET y PUT requieren `[Authorize]`.
- Sin documento, GET aplica el tema de sistema sin crear documento ni escribir en la BD.

## Contratos
- [`GET /api/v1/profiles/me/preferences`](../../../docs/endpoints.md): responde `{ theme }`, por defecto `system`.
- [`PUT /api/v1/profiles/me/preferences`](../../../docs/endpoints.md): recibe `{ theme }` y responde `{ theme }`; valor inválido: `400 profiles.invalid_theme`.
- Error en [`docs/errores.md`](../../../docs/errores.md); fixtures de request/response en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Profiles/PreferencesServiceTests.cs`: default, enum permitido, aislamiento por cuenta y rechazo.
- `IntegrationTest/Profiles/PreferencesFlowTests.cs`: GET default, PUT persistente, valor inválido y borrado de cuenta.
- `SecurityTest/Auth/ProfilesAuthTests.cs`: 401 sin token y aislamiento entre tokens.
- `ContractTest/ProfilesContractTests.cs` y fixtures `preferences.json` / `update-preferences.json`.
- `DatabaseTest/MongoPreferenceRepositoryTests.cs` y `DatabaseTest/SchemaScriptTests.cs`: upsert, borrado, script 015 e índice en Mongo real.

## Criterios
- [x] GET sin documento devuelve `system`; PUT persiste los tres valores admitidos y rechaza otros con `profiles.invalid_theme`.
- [x] GET/PUT solo afectan al subject del JWT; sin sesión responden 401 y ningún request admite `userId`.
- [x] Mongo conserva la preferencia entre lecturas y la elimina al borrar la cuenta (HU-46).
- [x] `dotnet build Musubi.slnx -c Release` 0/0; Unit, Integration, Security, Contract y Database en verde; `check_endpoints` OK; critic PASS.

## Límites
- Las preferencias de notificaciones por tipo quedan fuera porque Jira deja su alcance MVP pendiente; esta HU persiste solo el tema.
- No se agregan preferencias por dispositivo: el valor se guarda por cuenta y se sincroniza entre sesiones.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 revisor distinto del autor)
- **Fecha:** 10-Nov-2026
- **Detalle:** alcance detallado desde SCRUM-71. El tema se guarda por cuenta; notificaciones quedan fuera porque Jira no define cuáles entran al MVP. El front consume la API para persistir entre aperturas.

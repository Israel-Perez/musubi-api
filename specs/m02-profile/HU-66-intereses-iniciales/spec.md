# HU-66 — Elegir mis intereses la primera vez que entro

**HU / Jira / Sprint / Módulo / Contraparte:** HU-66 / SCRUM-106 / Sprint 5 / M-02 / [musubi-app:HU-66](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m02-profile/HU-66-intereses-iniciales)

## Contexto
Jira (SCRUM-106): Como **estudiante nuevo**, quiero **elegir las categorías que me interesan al entrar por primera vez**, para **ver desde el inicio productos que me sirven**.

- Maqueta: no tiene la pantalla (Jira lo deja pendiente); la app la arma con piezas existentes.
- Origen: ideas del 13-Nov-2026 para dar un plus a la app. Trazabilidad: RN-46 · alimenta HU-35 ("Para ti") y HU-60 · RNF-07.
- **Depende de:** `HU-06` (categorías del campus, `ICategoryDirectory`), `HU-54` (colección `preferences`), `HU-46` (borrado de cuenta).

## Requisitos
1. Leer y reemplazar los intereses solo de la cuenta autenticada; el cliente no elige el `userId`.
2. Sin intereses guardados, responder una lista vacía sin crear documento.
3. Aceptar solo slugs de categorías activas del campus de la cuenta (hasta 20; los repetidos cuentan una vez); cualquier otro valor responde `profiles.invalid_interest` y no guarda nada. Una lista vacía los borra.
4. Los intereses son privados (RNF-07): el perfil público no los muestra.
5. Cambiar los intereses no cambia el tema (HU-54) y al revés.
6. Borrarlos al eliminar la cuenta (HU-46).

## Diseño
- Módulo `Musubi.Api/Modules/Profiles/` (M-02): `InterestsController`, `InterestsService` (+ `IInterestsService`), `InterestsDtos`, `ProfilesErrors.InvalidInterest`.
- Se guardan en `preferences.interests[]` (documento de HU-54, `_id = userId`). Esa colección no la lee el perfil público, así que la privacidad no depende de un filtro.
- La validación usa `ICategoryDirectory` (M-03, con su caché): Profiles no lee `categories`.
- `IPreferenceRepository.SetInterestsAsync`: upsert con `$setOnInsert theme: "system"` (el validador de `db/018` exige `theme`) y `$set interests`. El repositorio en memoria conserva el campo que no cambia.
- **Sin script en `db/`:** el validador de `preferences` no cierra los campos (`additionalProperties`), así que acepta `interests`. El esquema sigue en 21.

## Contratos
- [`GET /api/v1/profiles/me/interests`](../../../docs/endpoints.md): `{ categories[] }`, vacío por defecto.
- [`PUT /api/v1/profiles/me/interests`](../../../docs/endpoints.md): recibe y responde `{ categories[] }`; slug inválido: `400 profiles.invalid_interest`; lista ausente o de más de 20: `400 validation.failed`.
- Error en [`docs/errores.md`](../../../docs/errores.md); fixtures `interests.json` y `update-interests.json` en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Profiles/InterestsRepositoryTests.cs`: upsert con tema `system`, tema e intereses no se pisan, aislamiento por cuenta, borrado y copia defensiva.
- `IntegrationTest/Profiles/InterestsFlowTests.cs`: vacío por defecto, guardar sin repetidos, vaciar sin tocar el tema, slug inválido o vacío (no guarda), lista ausente o de 21 y borrado de cuenta.
- `SecurityTest/Auth/ProfilesAuthTests.cs`: 401 sin sesión, sin ruta con id, `userId` del cuerpo ignorado y el perfil público sin los intereses.
- `ContractTest/ProfilesContractTests.cs`: forma de `interests.json` vacía y guardada.
- `DatabaseTest/MongoPreferenceRepositoryTests.cs`: upsert contra el validador real de `db/018` y conservación del tema (corre en el CI).

## Criterios
- [x] GET sin documento devuelve `[]`; PUT guarda categorías activas sin repetidos y rechaza otras con `profiles.invalid_interest` sin guardar: `InterestsFlowTests`.
- [x] Solo cambia la cuenta del token y sin sesión responde 401: `ProfilesAuthTests`.
- [x] Privados (RNF-07): `GET /profiles/{id}` no los incluye: `ProfilesAuthTests.Interests_IgnoreAnyUserId_AndNeverReachThePublicProfile`.
- [x] Se borran con la cuenta (HU-46) y no pisan el tema (HU-54): `InterestsFlowTests`, `InterestsRepositoryTests`.
- [x] `dotnet build Musubi.slnx -c Release` 0/0; Unit 642, Integration 177, Security 227 y Contract 23 en verde; `check_endpoints` OK (88); critic PASS.

## Criterios originales Jira
- [x] Pantalla opcional "¿Qué te interesa?" con las categorías del campus como chips después de verificar el correo: evidencia en `musubi-app:HU-66`.
- [x] Se guardan en la cuenta y se cambian en Ajustes: esta API + `musubi-app:HU-66`.
- [ ] HU-35 los usa en el arranque en frío (RN-46): `NOTE (HU-35)` en `InterestsService`; "Para ti" todavía no existe.
- [x] Privados (RNF-07): ver arriba.

## Límites
- "Para ti" (HU-35) y HU-60 todavía no los leen; cuando existan, leerán `preferences.interests` por un contrato del módulo, no la colección.
- Si una categoría se desactiva después de elegirla, el slug se queda guardado; HU-35 debe ignorar los que ya no existan.
- La API no sabe si la persona ya vio la pantalla: la app la muestra una vez, justo después de verificar el correo.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 revisor distinto del autor)
- **Fecha:** 14-Nov-2026
- **Detalle:** alcance desde SCRUM-106. API `0.5.0-alpha.29`, sin script nuevo. Jira sigue en «Por hacer»; transicionarlo queda para el responsable.

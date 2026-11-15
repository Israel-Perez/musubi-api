# HU-12 — Guardar una búsqueda sin resultados

**HU / Jira / Sprint / Módulo / Contraparte:** HU-12 / SCRUM-51 / Sprint 5 (14 nov – 1 dic 2026) / M-04 / [musubi-app:HU-12](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-12-guardar-busqueda)

## Contexto
Jira (SCRUM-51): Como **comprador**, quiero **que se conserve una búsqueda que no tuvo resultados**, para **recibir un aviso cuando aparezca un producto compatible**.

- Maqueta: `Busqueda-vacia` ("Avísame cuando aparezca" → "Te avisaremos cuando aparezca") y `Notificaciones` ("El producto que buscaste ya está disponible", push "¡Ya apareció lo que buscabas!").
- Trazabilidad: RF-15, RF-23, RF-38 · CU-07 · Flujo D · RN-34 · RNF-07.
- Decisiones de la usuaria (14-Nov-2026): vence a los **30 días**; se consultan y quitan en **Ajustes → "Mis alertas"**; coincide como la búsqueda: **todas las palabras** y los filtros que tenía.

**Depende de:** `HU-09`/`HU-10` (búsqueda y filtros), `HU-29` (la cola al publicar), `HU-31` (avisos).

## Requisitos
1. **R1 · Guardar:** `POST /saved-searches` con lo que se buscó y sus filtros (mismas reglas que `GET /products`). La misma búsqueda (mismas palabras normalizadas y filtros) solo renueva su vencimiento (200); una nueva da 201.
2. **R2 · Límite:** 10 vigentes por persona (`listings.too_many_saved_searches`); renovar una existente siempre se puede.
3. **R3 · Consultar y quitar:** `GET /saved-searches` (vigentes, de la más nueva a la más vieja) y `DELETE /saved-searches/{id}` (idempotente, solo las mías).
4. **R4 · Vence a los 30 días** (RN-34, TTL).
5. **R5 · Aviso:** al publicar algo con **todas** las palabras guardadas (cada una es el inicio de una palabra del nombre o la descripción) que cumple las categorías, el rango de precio y el estado, llega "¡Ya apareció lo que buscabas!" (`saved_search_match`, abre la publicación). Uno por búsqueda y publicación, hasta 3 al día, de cada persona solo una búsqueda, y nunca a quien ya recibió el aviso de su solicitud (HU-29) por esa publicación.
6. **R6 · Privadas (RNF-07):** nadie más las ve; se borran con la cuenta (HU-46). Nunca a quien vende ni entre personas bloqueadas (HU-57).

## Diseño
- M-04: `SavedSearchService` (+ `ISavedSearchDirectory`, `IAccountDataEraser`), `SavedSearchesController`, `ISavedSearchRepository` (`Mongo*` / `InMemory*`), `savedSearches` (`db/025`).
- M-06: `ProductMatchNotifier` lee `ISavedSearchDirectory` después de las solicitudes (`SavedSearchCandidate.Matches`).
- Palabras: `SavedSearchService.Terms` las parte igual que `SearchWord.Words` ("TI-84" = "ti" + "84").

## Contratos
Filas `POST`, `GET` y `DELETE /api/v1/saved-searches` de [`docs/endpoints.md`](../../../docs/endpoints.md); error `listings.too_many_saved_searches` en [`docs/errores.md`](../../../docs/errores.md); fixture `ContractTest/Fixtures/saved-search.json`.

## Tests
- `UnitTest/Listings/SavedSearchServiceTests.cs`: 30 días, renovar, límite, validaciones, suspendida, privacidad, vencimiento, borrar y la regla de coincidencia.
- `UnitTest/Matching/ProductMatchTests.cs`: el aviso con el texto de la maqueta, todas las palabras, quien vende y otro campus, no repetir con la solicitud y una por persona.
- `TestShared/Listings/SavedSearchRepositoryContract.cs` (memoria y MongoDB real).
- `IntegrationTest/Matching/WantedMatchFlowTests.cs` → `ASavedSearch_TellsWhoSavedIt_AndCanBeListedAndRemoved`.
- `SecurityTest/Auth/SavedSearchesAuthTests.cs`; `ContractTest/WantedContractTests.cs` → `SavedSearches_MatchTheFixture`; `DatabaseTest/SchemaScriptTests.cs` (`025`).

## Criterios
- [x] Sin resultados se puede guardar la búsqueda (o convertirla en solicitud, HU-13 ya existente) → integración.
- [x] Se consultan y se quitan → `SavedSearchServiceTests` e integración.
- [x] Al publicar algo compatible llega el aviso → `ProductMatchTests` e integración.
- [x] Privadas y vencen a los 30 días (RN-34) → `TheyArePrivate_AndExpire` y seguridad.
- [x] Build Release 0/0 · Unit 723, Integration 187, Security 240 y Contract 25 · `check_endpoints` OK (95) · critic PASS.

## Límites
- La búsqueda de HU-09 devuelve publicaciones con cualquiera de las palabras (ordenadas por relevancia); la alerta pide todas para no avisar de más.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 14-Nov-2026 (detallado e implementado)

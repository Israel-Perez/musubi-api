# HU-18 — Ver coincidencias al publicar una solicitud

**HU / Jira / Sprint / Módulo / Contraparte:** HU-18 / SCRUM-40 / Sprint 5 (14 nov – 1 dic 2026) / M-06 / [musubi-app:HU-18](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m06-matching/HU-18-coincidencias-solicitud)

## Contexto
Jira (SCRUM-40): Como **comprador**, quiero **que al publicar mi solicitud el sistema me muestre productos ya publicados que coinciden**, para **no tener que buscarlos manualmente**.

- Maqueta: `Necesidad-publicada` ("Ya coinciden con lo que buscas", con "Dentro de tu presupuesto" y "Modelo parecido").
- Trazabilidad: RF-22, RF-24, RF-25 · CU-13 · RN-31, RN-32, RN-33 · RNF-11.
- Decisiones de la usuaria (14-Nov-2026): exactas y parecidas; registrar cada coincidencia (RF-25) con una colección.

**Depende de:** `HU-13`, `HU-19` (`MatchRules`), `HU-57` (bloqueos), `HU-23` (pedidos, para RF-25).

## Requisitos
1. **R1 · Exactas:** las publicaciones disponibles del campus que cumplen las reglas de HU-19: misma categoría (o cualquiera con "otro"), la mitad o más de las palabras buscadas en el nombre o la descripción y el precio hasta 20 % sobre el presupuesto. Van primero, por palabras y por el precio más cercano al presupuesto.
2. **R2 · Parecidas (RF-24):** después, las de la categoría con al menos una palabra y hasta 50 % sobre el presupuesto.
3. **R3 · Solo quien la publicó:** otra persona o una solicitud inexistente dan `wanted.not_found` sin revelar cuál (OWASP API1). Una cerrada, cancelada o vencida da la lista vacía.
4. **R4 · Nunca lo propio ni de personas bloqueadas** (RN-31, HU-57).
5. **R5 · Sin coincidencias:** lista vacía; la solicitud sigue en el tablero para recibir ofertas.
6. **R6 · Registro (RF-25):** cada coincidencia exacta que encuentran los avisos (HU-19 y HU-29) se guarda en `matches` con a quién se avisó; al crear un pedido de esa publicación, quien la buscaba la marca como terminada en pedido (`orderedAt`). 90 días.
7. **R7 · Segundo plano (RNF-11):** los avisos y el registro corren en las colas de HU-19 y HU-29; la consulta de R1–R2 es directa (acotada por `ix_community_status_search`).

## Diseño
- M-06: `WantedMatchesService` + `WantedMatchesController` (`GET /wanted/{id}/matches`), `MatchLedger` (`IMatchLedger`, nunca falla hacia quien llama) sobre `IMatchRepository` (`Mongo*` / `InMemory*`).
- M-05: `IWantedMatchDirectory.FindOwnAsync`. M-03: `IProductMatches` (HU-19) y `IRecommendationCatalog.CardsAsync` (HU-35) para las tarjetas. M-08: `OrderService` llama `IMatchLedger.OrderedAsync`.
- Datos: `matches` (`db/024-coincidencias.js`, fila en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md)).

## Contratos
Fila `GET /api/v1/wanted/{id}/matches` de [`docs/endpoints.md`](../../../docs/endpoints.md); fixture `ContractTest/Fixtures/wanted-match-page.json`. Sin errores nuevos.

## Tests
- `UnitTest/Matching/WantedMatchesTests.cs`: orden de exactas y parecidas y el margen de 50 %.
- `UnitTest/Matching/WantedMatchTests.cs` → `EachMatch_IsRecorded_WithWhetherTheSellerWasTold`; `UnitTest/Orders/OrderServiceTests.cs` → `Create_FromAListingThatMatchedMyRequest_MarksTheMatchAsOrdered`.
- `TestShared/Matching/MatchRepositoryContract.cs` (memoria y MongoDB real).
- `IntegrationTest/Matching/WantedMatchFlowTests.cs` → `MyRequest_ShowsWhatAlreadyMatches_ExactFirst_ThenSimilar_AndOnlyToMe`.
- `SecurityTest/Auth/ManageWantedAuthTests.cs` → `Matches_WithoutSession_Is401_AndOfSomeoneElse_Is404`.
- `ContractTest/WantedContractTests.cs`; `DatabaseTest/SchemaScriptTests.cs` (`024`).

## Criterios
- [x] Al publicar se ven las que coinciden (exactas primero) → integración.
- [x] La coincidencia considera categoría, palabras y presupuesto (RN-33) → `WantedMatchesTests` y las reglas de HU-19.
- [x] Sin exactas se sugieren parecidas (RF-24) → `ExactOnesFirst_ThenSimilar_ByWordsAndThenTheClosestPrice`.
- [x] Sin coincidencias la solicitud sigue para recibir ofertas → lista vacía, sin cambios en la solicitud.
- [x] Cada coincidencia se registra, si se avisó y si terminó en pedido (RF-25) → contrato de `matches` y `OrderServiceTests`.
- [x] El cálculo de los avisos corre en segundo plano (RNF-11) → colas de HU-19 y HU-29.
- [x] Build Release 0/0 · Unit 697, Integration 186, Security 234 y Contract 24 · `check_endpoints` OK (92) · critic PASS.

## Límites
- Solo se registran las exactas que encuentran los avisos; las parecidas que se muestran no se guardan (son sugerencias).
- "Ya hay 3 publicaciones parecidas" de `Necesidad-1` (antes de publicar) queda en la app con la búsqueda de HU-09.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 14-Nov-2026 (detallado e implementado)
- **Detalle:** con las decisiones de la usuaria del 14-Nov-2026.

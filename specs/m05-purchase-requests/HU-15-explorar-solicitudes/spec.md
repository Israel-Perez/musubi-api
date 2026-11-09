# HU-15 — Explorar solicitudes publicadas

**HU / Jira / Sprint / Módulo / Contraparte:** HU-15 / SCRUM-37 / Sprint 4 (9 – 20 nov 2026) / M-05 / [musubi-app:HU-15](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m05-purchase-requests/HU-15-explorar-solicitudes)

## Contexto
Jira (SCRUM-37): Como **vendedor**, quiero **consultar y filtrar las solicitudes publicadas**, para **encontrar oportunidades de venta**.

- Maqueta: `Se-busca` (con sesión: lupa y "Filtrar solicitudes") y `Se-busca-visitante` (HU-48). "Yo tengo este producto" lo abre HU-16.
- Trazabilidad: RF-19 · CU-10 · RNF-19 · RN-06 · RNF-05.

**Depende de:** `02-01`, `02-02` (caché), `02-03`, `HU-13` y `HU-57`.

## Requisitos
1. **R1 · Activas:** el tablero lista solo las solicitudes activas y sin vencer del campus, de la más nueva a la más vieja (RN-10).
2. **R2 · Filtros de la maqueta:** categoría (varias), presupuesto ("Hasta $300", "$300 – $1,000", "Más de $1,000") y fecha límite ("En 3 días o menos", "Esta semana"). Se combinan con AND.
3. **R3 · Lupa:** palabras del producto o de la descripción, por inicio de palabra, sin distinguir mayúsculas ni acentos.
4. **R4 · Con sesión:** el campus del token; sin las propias (RN-06) ni las de quien bloqueaste o te bloqueó (HU-57). Quien busca sale con su nombre completo, su foto y su carrera, solo si la muestra (HU-04).
5. **R5 · Visitante (HU-48, RNF-05):** el campus que pide (o el del piloto) y solo el nombre corto ("Fernanda O."), sin id, foto ni carrera.
6. **R6 · Página:** 20 por defecto (1 a 50) con cursor.
7. **R7 · Caché:** la respuesta del visitante sale de la caché de salida (30 s) y se borra al publicar una solicitud o eliminar una cuenta; con sesión nunca. `ETag` en todas.

RNF que aplican: RNF-05, RNF-19 (lo cumple la app con el boleto "Se busca"), RNF-21.

## Diseño
- **Módulo:** `Modules/PurchaseRequests/` — `WantedService.BoardAsync`, `GET /api/v1/wanted` (`[AllowAnonymous]`), `WantedBoardFilter`, `WantedPage` y `IWantedRepository.FindBoardAsync` (Mongo y en memoria, misma batería).
- **Búsqueda:** `WantedSearch` arma un patrón por término (normalizado como HU-09) con las vocales acentuadas en la clase de caracteres; la consulta solo acepta letras y dígitos, hasta 5 términos (OWASP API3, API4). No hay índice de palabras: ver `plan.md`.
- **Datos de quien busca:** `IMemberDirectory.FindMembersAsync` (nombres, una consulta) e `IProfileDirectory.FindCardsAsync` (foto y carrera visible, una consulta + carreras en caché).
- **Datos:** `wantedRequests` sin cambios; usa `ix_community_status_id` de `db/009-solicitudes.js`. Esquema 10.

## Contratos
- Fila `GET /api/v1/wanted` de [`docs/endpoints.md`](../../../docs/endpoints.md) (M-05).
- Errores `wanted.invalid_budget_range`, `wanted.invalid_deadline`, `wanted.invalid_query`, `wanted.invalid_cursor` y `listings.unknown_campus` en [`docs/errores.md`](../../../docs/errores.md).
- Fixture `ContractTest/Fixtures/wanted-page.json`; `wanted.json` suma `requester.photo` y `requester.career` (en `null` fuera del tablero).

## Tests
- `UnitTest/PurchaseRequests/WantedServiceTests.cs`: R1–R7 (tablero con y sin sesión, filtros, lupa, página, errores y la caché al publicar).
- `TestShared/PurchaseRequests/WantedRepositoryContract.cs` (`FindBoard_*`) en memoria y en MongoDB (`DatabaseTest`); `TestShared/Auth/AuthRepositoryContract.cs` (`Users_FindMany_*`).
- `IntegrationTest/PurchaseRequests/WantedFlowTests.cs` (`Board_*`), `SecurityTest/Auth/WantedAuthTests.cs` (`Board_*`) y `ContractTest/WantedContractTests.cs`.

## Criterios
- [x] Se listan las solicitudes **Activas** (R1).
- [x] Se pueden filtrar por categoría y presupuesto, además de fecha límite y palabras (R2, R3).
- [x] Con sesión no salen las propias ni las de personas bloqueadas; un visitante no recibe datos personales (R4, R5).
- [x] Las solicitudes se distinguen visualmente de los productos publicados (RNF-19): lo cumple `musubi-app:HU-15` (boleto "Se busca").
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.
- [ ] `DatabaseTest` en el CI (`FindBoard_*` y `Users_FindMany_*` contra MongoDB real).

## Límites
- `NOTE (HU-16)`: "Yo tengo este producto" (ofertas) y `offersCount`.
- `NOTE (HU-18)`: "Coincide con tu …" (la publicación propia que encaja con la solicitud).
- Sin pestañas "Para ti", "Por vencer" y "Recientes": el script de la maqueta las tiene, pero la pantalla `Se-busca` no las muestra.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador) · 09-Nov-2026 (implementación)
- **Detalle:**
  - filtros de la maqueta además de los de Jira (fecha límite y lupa);
  - nombre completo y carrera solo con sesión;
  - sin índice de palabras.

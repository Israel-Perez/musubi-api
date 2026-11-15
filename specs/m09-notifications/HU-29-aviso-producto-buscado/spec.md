# HU-29 — Aviso de producto buscado disponible

**HU / Jira / Sprint / Módulo / Contraparte:** HU-29 / SCRUM-47 / Sprint 5 (14 nov – 1 dic 2026) / M-09 (en M-06) / [musubi-app:HU-29](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m09-notifications/HU-29-aviso-producto-buscado)

## Contexto
Jira (SCRUM-47): Como **comprador**, quiero **recibir una notificación cuando aparezca un producto que busqué o uno similar**, para **no revisar la aplicación constantemente**.

- Maqueta: `Notificaciones` → "Coincidencias".
- Trazabilidad: RF-38 · CU-14 · RN-28 a RN-31.
- Decisión de la usuaria (14-Nov-2026): igual que HU-19 (push y aviso en "Coincidencias", solo exactas, uno por solicitud y publicación, hasta 3 al día; abre la publicación).

**Depende de:** `HU-31` (avisos), `HU-19` (`MatchRules`), `HU-57`.

## Requisitos
1. **R1 · Aviso:** al publicar algo compatible con una solicitud activa (las reglas de HU-19), a quien la publicó le llega "Alguien publicó lo que buscas" con «nombre» · precio y lo que buscaba (`product_match`, categoría `matches`, push incluido).
2. **R2 · Abre la publicación:** `target { type: product, id }`.
3. **R3 · Relevancia, no repetición y límite (RN-28 a RN-30):** la mitad de las palabras y hasta 20 % sobre el presupuesto; uno por solicitud y publicación (`dedupeKey`); de cada persona solo su solicitud que más se parece; hasta 3 al día del campus por persona.
4. **R4 · Nunca a quien vende ni entre personas bloqueadas** (RN-31, HU-57); solo solicitudes activas y sin vencer del mismo campus.
5. **R5 · Segundo plano (RNF-11):** `ListingService` encola con `IProductPublications` y `ProductMatchWorker` revisa; una que falla no detiene la cola.
6. **R6 · Registro (RF-25):** cada coincidencia queda en `matches` con `notifiedRequester` (también las que pasaron el límite, con `false`).

## Diseño
- M-06: `ProductMatchNotifier`, `ProductMatchQueue`, `ProductMatchWorker`. M-05: `IWantedMatchDirectory.ActiveCandidatesAsync` (la consulta del tablero, HU-15). M-03: `ProductPublished` / `IProductPublications`.
- `db/024-coincidencias.js`: el validador de `notifications` admite `product_match` y `target.type: product` (y, de paso, `wanted_match`, `offer_closed`, `wanted_board` y `none` de HU-19 y HU-30, que MongoDB rechazaba).

## Contratos
Sin rutas nuevas: el aviso sale en `GET /api/v1/notifications` (fila HU-31 de [`docs/endpoints.md`](../../../docs/endpoints.md)) con `type: product_match`.

## Tests
- `UnitTest/Matching/ProductMatchTests.cs`: texto y destino, reglas, solicitudes cerradas/vencidas/de otro campus/propias, bloqueos, una por persona, límite diario, no repetir y `MinBudget`.
- `IntegrationTest/Matching/WantedMatchFlowTests.cs` → `ACompatibleListing_TellsWhoLooksForIt_AndOpensTheListing`.
- `TestShared/Notifications/NotificationRepositoryContract.cs` → `EveryTypeAndTargetOfTheCode_CanBeStored` (contra MongoDB real en `DatabaseTest`).

## Criterios
- [x] Push cuando se publica algo que coincide con una solicitud activa → `ProductMatchTests` e integración.
- [x] Al tocarlo se abre el producto → `target.type: product`.
- [x] Umbral, no repetición y límite diario (RN-28 a RN-30) → `ProductMatchTests`.
- [x] Build Release 0/0 · Unit 697, Integration 186, Security 234 y Contract 24 · critic PASS.

## Límites
- Las búsquedas guardadas (HU-12) se suman cuando exista esa HU.
- Solo al publicar; editar el precio de una publicación no vuelve a avisar.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 14-Nov-2026 (detallado e implementado)
- **Detalle:** con la decisión de la usuaria del 14-Nov-2026.

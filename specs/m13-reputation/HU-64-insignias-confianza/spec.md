# HU-64 — Ver insignias de confianza de quien vende

**HU / Jira / Sprint / Módulo / Contraparte:** HU-64 / SCRUM-104 / Sprint 5 (14 nov – 1 dic 2026) / M-13 / [musubi-app:HU-64](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m13-reputation/HU-64-insignias-confianza)

## Contexto
Jira (SCRUM-104): Como **comprador**, quiero **ver insignias de confianza de quien vende**, para **saber rápido si es alguien con quien conviene tratar**.

- Maqueta: `Producto` y `Perfil-vendedor` ya dicen "Responde en ~1 h"; "Vendedor confiable" no está y se arma con sus piezas (lineamiento 4).
- Trazabilidad: RF-36 · relacionada con HU-05, HU-26 y HU-27/45. Origen: ideas del 13-Nov-2026.
- **Decisiones de la usuaria (16-Nov-2026):**
  - **"Vendedor confiable":** 4.5 ★ o más con al menos 5 ventas completadas (la propuesta de Jira).
  - **"Responde en ~N":** la mediana de las respuestas de quien vende en los últimos 30 días, con al menos 3.
  - Se muestran en `Producto` y `Perfil-vendedor` (no en las tarjetas).

**Depende de:** `HU-04`/`HU-05` (perfil público), `HU-20` (chat), `HU-26` (ventas), `HU-27` (calificaciones).

## Requisitos
1. **R1 · Medir:** cuando quien vende contesta en el chat un mensaje de quien compra, se guarda cuánto tardó (desde el último mensaje de la otra persona). Se guardan las últimas 20 en `profiles.responses`.
2. **R2 · "Responde en ~N":** `GET /profiles/{id}` da `stats.responseMinutes`, la mediana en minutos de las respuestas de los últimos 30 días, o `null` con menos de 3.
3. **R3 · "Vendedor confiable":** `stats.trusted` es `true` con `ratingAvg` de 4.5 o más y al menos 5 ventas completadas.
4. **R4 · "Estudiante UTTT verificado":** ya existe: solo se registra quien verificó su correo institucional (HU-02); la app lo muestra con la insignia de siempre.
5. **R5 · No se inventa:** sin datos suficientes no hay insignia; las insignias se calculan en la API, nunca en el teléfono.

## Diseño
- `Modules/Profiles/`: `ProfileDocument.Responses` (`ResponseSample`), `IProfileRepository.AddResponseAsync` (`$push` con `$slice: -20`), `IProfileResponses` (contrato para M-07) y `SellerBadges` (las reglas).
- `Modules/Chat/ChatService.SendAsync`: registra la respuesta de quien vende.
- `PublicStatsResponse`: `responseMinutes` y `trusted`.
- Sin script nuevo: el validador de `profiles` (`db/004`) no limita campos nuevos.

## Contratos
Fila `GET /api/v1/profiles/{id}` en [`docs/endpoints.md`](../../../docs/endpoints.md) (dos campos nuevos en `stats`); fixture `ContractTest/Fixtures/public-profile.json`.

## Tests
- `UnitTest/Profiles/SellerBadgesTests.cs`: umbrales, mediana, ventana de 30 días, mínimo de 3 y las últimas 20.
- `UnitTest/Chat/ChatServiceTests.cs` (`Send_OnlyTheSellersReplyToTheBuyer_CountsForTheResponseTime`).
- `TestShared/Profiles/ProfileRepositoryContract.cs` (`AddResponse_KeepsTheLast20_…`, también contra MongoDB real).
- `IntegrationTest/Profiles/SellerBadgesFlowTests.cs`: tres respuestas en el chat y el perfil público.
- `ContractTest`: la forma de `stats`.

## Criterios
- [x] "Responde en ~N": mediana del tiempo de respuesta en el chat de los últimos 30 días (solo con suficientes conversaciones) → `SellerBadgesTests.ResponseMinutes_*`, `SellerBadgesFlowTests`.
- [x] "Vendedor confiable": 4.5 ★ o más con al menos 5 ventas completadas → `SellerBadgesTests.Trusted_*`.
- [x] "Estudiante UTTT verificado": correo institucional verificado → HU-02 (sin cambios).
- [x] Las insignias se calculan en la API y se actualizan solas → `ChatServiceTests.Send_OnlyTheSellersReplyToTheBuyer_CountsForTheResponseTime`, `ProfileRepositoryContract.AddResponse_*`.
- [x] Sin datos suficientes no se muestra la insignia → `ResponseMinutes_NeedsThreeRecentReplies_AndIsAtLeastOneMinute`, `Trusted_WithoutRatings_IsFalse`.
- [x] Build Release 0/0 · Unit 925, Integration 208, Security 305 y Contract 31 (con HU-39/40 y HU-41) al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- La respuesta se mide desde el último mensaje de quien compra (no desde el primero sin contestar).
- "Vendedor confiable" usa las ventas reales aunque la persona oculte su número ("Mostrar mis ventas"): la insignia no dice cuántas.
- Se actualiza al contestar (no con un proceso periódico): la mediana se calcula al leer el perfil con la ventana de 30 días.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 16-Nov-2026 (detallada e implementada, con las decisiones de la usuaria; API `0.5.0-alpha.40`)

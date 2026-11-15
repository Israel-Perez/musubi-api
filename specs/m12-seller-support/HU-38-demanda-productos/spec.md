# HU-38 — Saber si hay demanda para mis productos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-38 / SCRUM-61 / Sprint 5 (14 nov – 1 dic 2026) / M-12 / [musubi-app:HU-38](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m12-seller-support/HU-38-demanda-productos)

## Contexto
Jira (SCRUM-61): Como **vendedor**, quiero **saber si hay usuarios buscando productos similares a los que no he vendido**, para **ofrecérselos directamente**.

- Maqueta: `Mis-productos` ("3 estudiantes la buscan · Ofrécesela") y `Vender-listo` ("2 estudiantes buscan algo similar · Ofréceles tu calculadora directo con “Yo tengo este producto” y vende más rápido." → "Ver solicitudes").
- Trazabilidad: RF-53 · CU-23 · RN-37.
- Decisiones de la usuaria (15-Nov-2026): **una tarjeta por producto** en el panel; las ideas en una **hoja al tocar la tarjeta** ("Ideas para venderla"); el aviso **una sola vez, a las 3 semanas**. El umbral de **21 días** sale de la maqueta ("2 productos con más de 3 semanas").

**Depende de:** `HU-13`/`HU-15` (solicitudes y tablero), `HU-16` ("Yo tengo este producto"), `HU-12` (búsquedas guardadas), `HU-29` (reglas), `HU-57` (bloqueos).

## Requisitos
1. **R1 · Quién la busca:** las solicitudes "Se busca" activas que coinciden con la publicación con las reglas de HU-29 (la mitad de las palabras, misma categoría u "otro", el precio hasta 20 % sobre el presupuesto; una por persona) y las búsquedas guardadas que coinciden con las de HU-12 (todas las palabras y los filtros). Nunca lo de quien vende ni entre personas bloqueadas.
2. **R2 · RN-37:** las solicitudes son públicas y se listan (lo que busca, el presupuesto, quién y cuándo vence); las búsquedas guardadas solo se cuentan (`alerts`), sin decir de quién son.
3. **R3 · Ofrecer:** desde la lista, el vendedor responde "Yo tengo este producto" (HU-16) a cada solicitud.
4. **R4 · Solo lo propio:** la demanda de una publicación ajena responde 404, como si no existiera (OWASP API1).

## Diseño
- M-06: `IProductDemand` / `ProductDemandService` y `DemandRules` (`BestRequests`, `MatchingSearches`), que también usa `ProductMatchNotifier` (las mismas reglas que los avisos). `WantedForMatch` lleva `RequesterName` y `ExpiresAt`.
- M-12: `SellerSupportService.DemandAsync` y `demand` en cada publicación de HU-36.

## Contratos
Fila `GET /api/v1/products/{id}/demand` de [`docs/endpoints.md`](../../../docs/endpoints.md); fixture `ContractTest/Fixtures/product-demand.json`.

## Tests
- `UnitTest/SellerSupport/SellerSupportServiceTests.cs` → `Slow_SaysWhoLooksForIt_…`, `Demand_ListsThePublicRequests_AndOnlyCountsTheAlerts`, `Demand_OfSomeoneElsesListing_IsNotFound`.
- `UnitTest/Matching/ProductMatchTests.cs` (los avisos de HU-29 y HU-12 siguen igual con `DemandRules`).
- `IntegrationTest/SellerSupport/SellerSupportFlowTests.cs`, `SecurityTest/Auth/SellerSupportAuthTests.cs`, `ContractTest/SellerSupportContractTests.cs`.

## Criterios
- [x] Con demanda relacionada se dice cuántas personas la buscan → `Slow_SaysWhoLooksForIt_…` e integración.
- [x] Las búsquedas guardadas solo de forma agregada (RN-37) → `Demand_ListsThePublicRequests_AndOnlyCountsTheAlerts`.
- [x] Se puede ofrecer en las solicitudes públicas → `requests[].id` abre "Yo tengo este producto" en la app.
- [x] Build Release 0/0 · Unit 748, Integration 188, Security 244 y Contract 26 · `check_endpoints` OK (97) · critic PASS.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 15-Nov-2026 (detallado e implementado, junto con HU-36, HU-37 y HU-38)

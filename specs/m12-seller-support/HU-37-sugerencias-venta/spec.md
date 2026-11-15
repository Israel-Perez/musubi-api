# HU-37 — Recibir sugerencias para vender

**HU / Jira / Sprint / Módulo / Contraparte:** HU-37 / SCRUM-60 / Sprint 5 (14 nov – 1 dic 2026) / M-12 / [musubi-app:HU-37](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m12-seller-support/HU-37-sugerencias-venta)

## Contexto
Jira (SCRUM-60): Como **vendedor**, quiero **recibir sugerencias como revisar el precio, mejorar la descripción o actualizar las fotografías**, para **aumentar la probabilidad de venta**.

- Maqueta: no tiene pantalla; el aviso de `Notificaciones` dice "Te damos ideas para venderla". Se agrega (lineamiento 4) la hoja "Ideas para venderla" al tocar una tarjeta de "Llevan tiempo sin venderse".
- Trazabilidad: RF-52 · CU-23 · RN-38.
- Decisiones de la usuaria (15-Nov-2026): **una tarjeta por producto** en el panel; las ideas en una **hoja al tocar la tarjeta** ("Ideas para venderla"); el aviso **una sola vez, a las 3 semanas**. El umbral de **21 días** sale de la maqueta ("2 productos con más de 3 semanas").

**Depende de:** [HU-36](../HU-36-baja-rotacion/spec.md), [HU-38](../HU-38-demanda-productos/spec.md), `HU-06` (rango de precios), `HU-07` (editar).

## Requisitos
1. **R1 · Las tres ideas:** cada publicación de "Llevan tiempo sin venderse" trae siempre las tres sugerencias predefinidas ("Revisa el precio", "Mejora la descripción" y "Actualiza las fotos"), primero las que piden los datos (`applies`).
2. **R2 · Precio:** si el precio pasa el mayor presupuesto de las solicitudes que coinciden, "Quienes buscan algo así ofrecen hasta $220 y tú pides $250."; si no, y pasa el percentil 75 de su categoría, "Productos parecidos se venden entre $180 y $300.".
3. **R3 · Descripción y fotos:** menos de 80 letras o menos de 3 fotos piden mejorarlas.
4. **R4 · Orientativas (RN-38):** el sistema nunca cambia precios ni publicaciones por su cuenta.
5. **R5 · Volver a buscar:** al editar una publicación disponible y cambiar el nombre, la descripción, la categoría, el estado o el precio, se vuelven a buscar coincidencias en segundo plano (la cola de HU-29); quien ya recibió el aviso de esa publicación no lo recibe otra vez (`dedupeKey`). Cierra la `NOTE (HU-18)` de `MyListingsService`.
6. **R6 · Ideas con IA (criterio del 13-Nov-2026):** en "Ideas para vender más rápido", "Proponer con IA" pide a Gemini (el mismo proveedor del acuerdo, detrás de `IListingCopywriter`) un nombre más claro y una mejor descripción con los datos de demanda: vistas de 30 días, conversaciones, personas que la buscan y lo que escriben las 5 solicitudes más parecidas. Solo sale la publicación y números: sin ids, sin el nombre de quien vende ni de quienes buscan, y sin correos, teléfonos, cuentas ni enlaces (`TranscriptRedactor`, RNF-06). La propuesta pasa las reglas de la publicación (nombre de 3 a 80, descripción de 1 a 500) o no se usa. Nada se guarda: "Usar en mi publicación" abre la edición con el texto propuesto y quien vende decide si guarda (RN-38). Sin IA (sin llave, sin red, cuota o respuesta fuera de las reglas) responde `503 seller_support.ideas_unavailable` y quedan las ideas predefinidas (RNF-15). Comparte el límite `genai` (6 por hora por persona) con "Resumir acuerdo".

## Diseño
- `SalesTips.For(product, demand, range)` en `Modules/SellerSupport/`; `ListingService.PriceRangeAsync(IProductRepository, …)` (el rango de `Vender-3`).
- `MyListingsService.UpdateAsync` encola `ListingService.Publication(updated)` en `IProductPublications`.
- R6: `SellerSupportService.IdeasAsync` arma el `CopyPrompt` y llama a `IListingCopywriter` (M-10, `Modules/GenAi/ListingCopy.cs`: `GeminiListingCopywriter`, `InMemoryListingCopywriter` y `OffListingCopywriter`, elegidos por `GenAi:Provider`). La llamada REST a `generateContent` vive en `GeminiApi` y la comparten el acuerdo y las ideas.

## Contratos
`tips` dentro de la fila `GET /api/v1/products/mine/slow` y la fila `POST /api/v1/products/{id}/ideas` de [`docs/endpoints.md`](../../../docs/endpoints.md); error `seller_support.ideas_unavailable` en [`docs/errores.md`](../../../docs/errores.md).

## Tests
- `UnitTest/SellerSupport/SalesTipsTests.cs`: las tres siempre, el precio contra los presupuestos y contra la categoría, descripción corta y pocas fotos.
- `UnitTest/Listings/MyListingsServiceTests.cs` → `Update_WithNewMatchingDetails_LooksForMatchesAgain` y `Update_WithoutChangesThatMatter_OrWhilePaused_DoesNotLookAgain`.
- `IntegrationTest/SellerSupport/SellerSupportFlowTests.cs` (las tres aplican, con el presupuesto de la solicitud; `IdeasWithAi_ProposeANameAndDescription_ButChangeNothing`).
- `UnitTest/GenAi/ListingCopyTests.cs`: el texto que lee la IA, la respuesta dentro de las reglas, la llave en el header y cada falla de Gemini.
- `UnitTest/SellerSupport/SellerSupportServiceTests.cs` → `Ideas_SendTheListingAndItsDemand_WithoutNamesOrContactData`, `Ideas_WithoutAi_AreUnavailable_AndSendNothing`, `Ideas_OfSomeoneElsesListing_AreNotFound_AndSendNothing`.
- `SecurityTest/Auth/SellerSupportAuthTests.cs` (401, 404 ajena y 503 sin llave) y `ContractTest/SellerSupportContractTests.cs` (`listing-ideas.json`).

## Criterios
- [x] Se muestran las sugerencias predefinidas → `AlwaysTheThreeIdeas_InTheirOrder_WhenNoneApplies`.
- [x] Precio arriba de los presupuestos de solicitudes similares → `APriceAboveTheirBudgets_SaysHowMuchTheyOffer` e integración.
- [x] Solo orientan (RN-38): ningún endpoint de M-12 escribe.
- [x] Al editar se vuelven a buscar coincidencias → `MyListingsServiceTests`.
- [x] Gemini propone un nombre y una descripción con la demanda, solo con la publicación y sin datos de personas; sin IA quedan las predefinidas → `ListingCopyTests`, `Ideas_*` y `IdeasWithAi_*`.
- [x] Build Release 0/0 · Unit 773, Integration 189, Security 246 y Contract 26 · `check_endpoints` OK (98) · critic PASS.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 15-Nov-2026 (detallado e implementado, junto con HU-36, HU-37 y HU-38; R6 de IA agregada el mismo día a petición de la usuaria)

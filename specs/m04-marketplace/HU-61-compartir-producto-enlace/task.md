# HU-61 — Compartir un producto con un enlace

## T1 — Conciliar privacidad y reglas de visitante
- **Qué:** Definir preview web genérico y preservar detalle visitante de HU-48; documentar filtro de campus con sesión.
- **Evidencia:** SCRUM-101, `docs/endpoints.md` y spec hermana Flutter.
- **Estado:** ✅ Decisión de preview privada registrada; exclusividad de campus con sesión requiere confirmación del PO si se mantiene como literal de Jira.

## T2 — Reutilizar el contrato de detalle
- **Qué:** No crear endpoint/DTO; comprobar que el GET actual aplica visibilidad y campus con token.
- **Verificación:** `IntegrationTest/Listings/ListingFlowTests.cs`, `SecurityTest/Auth/ListingsAuthTests.cs`, `ContractTest/ListingsContractTests.cs`.
- **Estado:** ✅ Verificado: build Release 0/0; Unit 974, Integration 216, Security 333 y Contract 31 en verde; el endpoint/DTO existente basta.

## T3 — Coordinar enlace y landing
- **Qué:** Verificar que la contraparte implementa `/p/{id}`, App Links y fallback sin metadatos de producto.
- **Verificación:** pruebas de app y HTML/OG del repo del sitio.
- **Estado:** 🟡 La app implementó `/p/{id}`/App Links; `assetlinks.json` respondió 404 y fallback HTML/OG queda pendiente del dueño externo del sitio.

## T4 — Cierre de trazabilidad
- **Qué:** Conciliar la spec con Jira, contraparte y evidencia; actualizar Memoria y trazabilidad al implementar.
- **Verificación:** `pwsh scripts/check_endpoints.ps1` → `OK`; suites API y Flutter que correspondan.
- **Estado:** 🟡 Trazabilidad/spec actualizadas; faltan evidencia del sitio y revisión antes del PR.

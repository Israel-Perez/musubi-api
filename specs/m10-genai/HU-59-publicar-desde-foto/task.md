# HU-59 — Publicar un producto a partir de una foto (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (25-Nov-2026). Decisiones para revisar: solo la portada, el tope `genai` compartido y la foto compartida con Google.

## T2 — Proveedor, servicio y ruta
- **Qué:** `IPhotoDraftWriter` (Gemini, en memoria y apagado), `PhotoDraftService` y `POST /products/photo-draft`.
- **Prueba:** `UnitTest/GenAi/PhotoDraftTests.cs`, `UnitTest/Listings/PhotoDraftServiceTests.cs`, `IntegrationTest/Listings/PhotoDraftFlowTests.cs`, `SecurityTest/Auth/PhotoDraftAuthTests.cs` y `ContractTest/ListingsContractTests.cs`.
- **Estado:** ✅ Hecho.

## T3 — Documentación
- **Estado:** ✅ Hecho: endpoints (131), errores, trazabilidad, versionado (alpha.46), lineamientos (L-16i), README, `AGENTS.md` y `Memoria.md`.

## T4 — Prueba con Gemini real
- **Qué:** con la API `0.5.0-alpha.46` desplegada y la llave de Gemini: fotos de una calculadora, una bata y un libro; revisar que la categoría y el estado tengan sentido y que la cuota alcance.
- **Estado:** ⏳ Por hacer (siguiente lote de Google Play).

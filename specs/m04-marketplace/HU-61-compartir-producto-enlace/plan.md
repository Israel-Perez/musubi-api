# Plan — HU-61 Compartir un producto con un enlace

## Componentes
- M-04: `Musubi.Api/Modules/Listings/` existente.
- Contrato reutilizado: `GET /api/v1/products/{id}` en `docs/endpoints.md`.
- Pruebas existentes: `IntegrationTest/Listings/ListingFlowTests.cs`, `SecurityTest/Auth/ListingsAuthTests.cs`, `ContractTest/ListingsContractTests.cs`.
- Contraparte Flutter: deep link, share sheet y App Links; fallback web/`assetlinks.json` por el dueño del sitio.

## Decisiones
1. No agregar una ruta dedicada: el detalle y sus políticas ya están en `GET /products/{id}`.
2. Privacidad del preview significa landing genérica y sin OG dinámico; no restringe la experiencia visitante prevista por HU-48.
3. Con sesión, el token determina campus y visibilidad. Sin sesión se conserva el comportamiento público vigente.
4. El id de producto en el URL no autoriza por sí mismo; la API evalúa cada lectura.

## Guardarraíles
- No agregar datos personales, tokens ni identificadores del vendedor al URL.
- No permitir que el enlace eluda filtros existentes de estado, campus o bloqueo.
- No cambiar el contrato público de HU-48 desde esta spec. Si el PO requiere ocultar el detalle a visitantes, conciliar HU-48, API y la contraparte antes de implementarlo.
- Mantener las respuestas indistinguibles de no visible que define el endpoint.

## Validación y secuencia
1. Verificar conducta actual de GET detalle para sesión y visitante en Integration/Security.
2. Confirmar que la app reusa el endpoint y que los enlaces entran por la ruta prevista.
3. Probar por separado el HTML/OG genérico de landing en el repo del sitio.
4. Ejecutar build Release 0/0, Unit/Integration/Security/Contract, `check_endpoints` y critic al implementar cambios API; si no hay cambio API, documentar las suites preexistentes pertinentes.
5. Conciliar ambas specs, trazabilidad y evidencia real antes del cierre.

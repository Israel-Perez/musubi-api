# 02-10 — Cierre de brechas de la evaluación (plan)

1. **Revocación:** `IAccessTokenRevocations` en `Platform/Auth` sobre `IDistributedCache` (Redis), con `iat_ms` en el token y la consulta en `OnTokenValidated`. La llaman los servicios que cierran sesiones.
2. **Compresión:** `PayloadCompression` en `Platform/Hosting`, justo después del registro de peticiones en el pipeline (01-02). El nivel sale de `Compression:Level`, medido antes de fijarlo (skill `empirical-verification`).
3. **Cobertura por endpoint:** `EndpointCoverage` como fixture de ensamblado en IntegrationTest y SecurityTest, y `scripts/check_endpoint_tests.ps1` en los jobs `unit`, `integration` y `security` del CI (patrón `drift-guards`).
4. **Pruebas faltantes:** primero las que el guard marque; luego `docs/trazabilidad.md` con las unitarias que ya existían.
5. **Documentación:**
   - `docs/endpoints.md` (convenciones y fila de logout);
   - `AGENTS.md`, `Memoria.md` y las skills `testing-strategy` y `traceability`;
   - `01-04` (`Compression:Level`) y `02-04` (compresión hecha);
   - `docs/versionado.md` (`0.5.0-alpha.41`).

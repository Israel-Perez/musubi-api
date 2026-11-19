# 02-10 — Cierre de brechas de la evaluación (tareas)

## T1 — Lista de revocación en Redis
- **Qué:** `AccessTokenRevocations.cs`, `iat_ms` y `OnTokenValidated`; logout, suspensión, contraseña, cuenta eliminada y reutilización.
- **Estado:** ✅ Hecho (17-Nov-2026). Pruebas: `UnitTest/Auth/AccessTokenRevocationTests.cs`, `IntegrationTest/Auth/TokenRevocationFlowTests.cs`, `SecurityTest/Auth/TokenRevocationTests.cs`.

## T2 — Compresión
- **Qué:** Brotli/Gzip con `Compression:Level` medido; sin `/auth`.
- **Estado:** ✅ Hecho (17-Nov-2026). Pruebas: `UnitTest/Hosting/PayloadCompressionTests.cs`, `SecurityTest/Hosting/PayloadCompressionTests.cs`.

## T3 — Guard de pruebas por endpoint
- **Qué:** `EndpointCoverage` + `scripts/check_endpoint_tests.ps1` en el CI.
- **Estado:** ✅ Hecho (17-Nov-2026): OK en las tres suites.

## T4 — Pruebas que faltaban
- **Qué:** unitarias de HU-39/40, HU-49 y HU-56; seguridad de las 9 rutas de auth; integración de `GET /reservations/{id}`, bancos y quitar la cuenta de pagos; `docs/trazabilidad.md`.
- **Estado:** ✅ Hecho (17-Nov-2026).

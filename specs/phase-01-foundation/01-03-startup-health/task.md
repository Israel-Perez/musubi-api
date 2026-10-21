# 01-03 — Arranque, health y ping (tareas)

## T1 — Health, ping y errores genéricos
- **Crear:** `Musubi.Api/Platform/Ping/PingController.cs`, `Musubi.Api/Platform/Data/*` (MongoDB desde el 20-Oct-2026; antes `AppDbContext`), `IntegrationTest/{Health,Ping}/*`, `SecurityTest/Errors/*`, `SecurityTest/Auth/*`, `ContractTest/*`, `docs/endpoints.md`.
- **Verificar:** Integration 8/8, Security 11/11, Contract 2/2; `pwsh scripts/check_endpoints.ps1` → OK (3).
- **Estado:** ✅ Hecho (15-Oct-2026).

## Pendientes
- `NOTE (02-01)`: documentos, índices, script `mongosh` y readiness contra MongoDB real (Testcontainers en `DatabaseTest`).

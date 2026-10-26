# HU-02 — Iniciar y cerrar sesión (tareas, back)

## T1 — Login, refresh y logout
- **Crear:** `Modules/Auth/LoginService.cs`, `SessionIssuer.cs` y `SessionController.cs`; los DTO de entrada; `UpdatePasswordHashAsync` en los dos repositorios.
- **Verificar:** `IntegrationTest/Auth/SessionFlowTests.cs`, `ContractTest/AuthContractTests.cs` y el self-test del estado pendiente.
- **Estado:** ✅ Hecho (21-Oct-2026).

## Pendientes
- `NOTE (HU-40)`: suspender una cuenta (el login ya responde `auth.account_suspended` y el refresh corta la sesión).

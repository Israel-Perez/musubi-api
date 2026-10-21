# HU-01 — Crear cuenta (tareas, back)

## T1 — Campus y registro
- **Crear:** `Modules/Auth/{AuthController,AuthDtos,RegistrationService,EmailAddresses,AuthEmails}.cs` y `Platform/Errors/Result.cs`.
- **Verificar:** `IntegrationTest/Auth/RegistrationFlowTests.cs`, `UnitTest/Auth/RegistrationTests.cs`, `ContractTest/AuthContractTests.cs` y `check_endpoints` OK (7).
- **Estado:** ✅ Hecho (21-Oct-2026).

## Pendientes
- `NOTE (02-05)`: `KnownProxies` del ALB, para que el rate limit vea la IP real.
- `NOTE (HU-04)`: editar el nombre y los datos del perfil.

# HU-56 — Recuperar mi contraseña (tareas, back)

## T1 — Pedir el código y cambiar la contraseña
- **Crear:** `Modules/Auth/PasswordRecoveryService.cs`, `PasswordController.cs` y `VerificationCodeService.cs`; el correo `PasswordResetCode`; los DTO.
- **Verificar:** `IntegrationTest/Auth/PasswordRecoveryFlowTests.cs` y el self-test de la revocación de sesiones.
- **Estado:** ✅ Hecho (21-Oct-2026).

## Pendientes
- `NOTE (02-05)`: dominio propio y SES en producción para que el correo llegue a Google Workspace.

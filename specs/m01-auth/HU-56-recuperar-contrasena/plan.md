# plan.md — HU-56 Recuperar mi contraseña (back)

1. **Componentes:** `Modules/Auth/PasswordRecoveryService.cs`, `PasswordController.cs`, `VerificationCodeService.cs` (extraído de `RegistrationService`), `AuthEmails.PasswordResetCode` y los DTO `RecoveryRequest` y `RecoveryResetRequest`.
2. **Decisiones:**
   - **Código y no enlace:** misma experiencia que la verificación (la maqueta ya tiene el campo de 6 dígitos). Además no hace falta un dominio web para el deep link.
   - **Respuesta genérica en el pedido:** ni siquiera la espera de 60 s se reporta como error, para no revelar qué cuentas existen.
   - **Revocar todas las sesiones:** cambiar la contraseña es la reacción esperada ante un robo.
   - **Propósitos separados** en `verificationCodes` (índice único `userId + purpose`, 02-01): un código de un flujo no sirve en el otro.
3. **Guardarraíles:** rate limit `auth`, 5 intentos por código, vigencia de 10 min y solo hashes.
4. **Validación:** IntegrationTest con reloj y correo controlados + el self-test de la revocación.
5. **Secuencia:** extraer `VerificationCodeService` → servicio → controller → filas → pruebas.

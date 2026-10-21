# plan.md — HU-03 Validar correo (back)

1. **Componentes:** `Modules/Auth/VerificationCodes.cs`, los métodos `ResendAsync` y `ConfirmAsync` de `RegistrationService`, las acciones del controller y `AuthEmails`.
2. **Decisiones:**
   - **Abrir la sesión al confirmar:** la maqueta pasa de `Verificacion` a `Verificacion-lista` y luego a "Empezar a explorar", sin pedir la contraseña otra vez.
   - **Bloqueo duro a los 5 intentos:** ni siquiera el código correcto pasa. Así 5 adivinanzas por código y la espera entre reenvíos acotan la fuerza bruta (1 en 200 000 por código).
   - **Respuesta genérica en el reenvío:** no revela qué correos tienen cuenta pendiente.
3. **Guardarraíles:** solo el hash del código, comparación en tiempo constante, rate limit `auth` y pruebas de vencimiento con reloj controlado.
4. **Validación:** IntegrationTest con `ManualTimeProvider` y `RecordingEmailSender` (`TestShared/`).
5. **Secuencia:** generador → reenvío → confirmación → sesión → pruebas.

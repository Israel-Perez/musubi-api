# plan.md — HU-02 Iniciar y cerrar sesión (back)

1. **Componentes:** `Modules/Auth/LoginService.cs`, `SessionIssuer.cs`, `SessionController.cs`, los DTO `LoginRequest`, `RefreshRequest` y `LogoutRequest`, `PasswordHashing.VerifyAgainstDecoy` y `IUserRepository.UpdatePasswordHashAsync`.
2. **Decisiones:**
   - **Mismo error para correo inexistente y contraseña equivocada**, con un hash señuelo: no se pueden enumerar las cuentas de un campus.
   - **El estado se revela solo con la contraseña correcta:** quien conoce la contraseña es el dueño, y sin ese dato la app no podría llevarlo a verificar.
   - **Logout idempotente:** la app siempre limpia su sesión local, aunque el token ya no exista en el servidor.
   - **`SessionIssuer` compartido:** registro y login devuelven exactamente la misma forma (un solo fixture).
3. **Guardarraíles:** rate limit `auth`, refresh rotativo con detección de reutilización (02-03) y pruebas que comparan las dos respuestas de error.
4. **Validación:** IntegrationTest con `AuthApiFactory` (en memoria, reloj y correo controlados) + ContractTest + la batería de repositorios.
5. **Secuencia:** DTO → servicio → controller → filas de endpoints → pruebas → self-test.

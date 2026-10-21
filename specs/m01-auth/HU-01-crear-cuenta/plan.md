# plan.md — HU-01 Crear cuenta (back)

1. **Componentes:**
   - `Modules/Auth/{AuthController,AuthDtos,RegistrationService,EmailAddresses,AuthEmails}.cs` y `Platform/Errors/Result.cs`;
   - el registro en `AddAuthModule`;
   - las filas de `docs/endpoints.md`.
2. **Decisiones:**
   - **`Result<T>` y no excepciones:** el controller solo traduce `Result` a HTTP, sin `try/catch` (B4).
   - **Controller sin constructor:** los servicios del módulo son `internal`, así que se resuelven con `HttpContext.RequestServices`. MVC exige controllers públicos, y un constructor público con un parámetro `internal` no compila.
   - **Consentimientos como `bool?` + `[Required]`:** omitirlos da 400 de validación (S6964, sin *under-posting*); enviarlos en `false` da `auth.consent_required`.
3. **Guardarraíles:**
   - `check_endpoints` (4 filas nuevas);
   - excepción `critic-allow` en la contraseña del DTO;
   - pruebas de que la respuesta no lleva secretos.
4. **Validación:** IntegrationTest (flujo HTTP real en memoria con reloj y correo controlados), UnitTest y ContractTest.
5. **Secuencia:** DTO → normalización → servicio → controller → filas → pruebas → fixtures.

# plan.md — 02-03 Contrato de errores, autenticación y correo

1. **Mapeo de componentes e impacto**
   - **Crear:**
     - `Platform/Errors/*`, `Platform/Auth/*`, `Platform/Email/*`;
     - `Modules/Auth/{AuthErrors,Passwords,RefreshTokenService}.cs`;
     - `docs/errores.md` y `ContractTest/Fixtures/problem.json`.
   - **Modificar:**
     - `PlatformServiceCollectionExtensions` (ProblemDetails, validación de entrada, auth, correo) y `PlatformPipelineExtensions` (StatusCodePages, Authentication, RateLimiter, salud anónima);
     - `AddAuthModule`, los appsettings, `nuget.config` (`AWSSDK.*`) y las factories de las pruebas.
2. **Decisiones de arquitectura**
   - **Refresh opaco en la base, en lugar de un refresh JWT:** se puede revocar (logout real, L-10), se detecta su reutilización y no viaja ningún dato del usuario.
   - **HS256 compartida y no RS256:** solo emite y valida esta API (monolito) y la llave vive en SSM. RS256 se justificaría con validadores externos.
   - **`PasswordHasher<T>` de Identity sin el resto de Identity:** algoritmo probado y formato versionado (avisa con `SuccessRehashNeeded`). Las 210 000 iteraciones siguen OWASP. Argon2id quedó descartado porque exigía un paquete nativo extra.
   - **Errores como valores (`ApiError`) y no como excepciones:** sin `try/catch` en los controllers y sin depender de que el manejador de excepciones esté activo en Development.
   - **Ruta inexistente sin sesión → 401:** la política de respaldo también cubre las peticiones sin endpoint, así que no se pueden enumerar rutas. Con sesión responde 404.
   - **`ISesClient`:** probar el mapeo no requiere un *fake* de toda la interfaz del SDK de AWS.
3. **Guardarraíles DevSecOps**
   - Fail-fast sin llave fuera de Development, y sin SES fuera de Development.
   - Solo HS256.
   - Claims mínimos (RNF-05).
   - `AppSettingsTests` impide que `SigningKey` llegue a un appsettings versionado.
   - La llave de pruebas se genera al vuelo, sin literales.
4. **Validación:** SecurityTest (JWT, rate limit, errores), UnitTest (contraseñas, sesión, correo), Contract (forma del error) y self-test de mutación.
5. **Secuencia:** errores → JWT y política → rate limit → contraseñas → refresh → correo → pruebas → catálogo.

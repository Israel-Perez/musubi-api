# plan.md — 01-03 Arranque, health y ping

1. **Componentes:** `Platform/Ping/PingController.cs`, `Platform/Data/AppDbContext.cs`, los health checks de las extensiones de hosting, `docs/endpoints.md`, `ContractTest/`.
2. **Decisiones:**
   - Liveness sin checks (el contenedor no se reinicia si la BD cae) y readiness con la BD.
   - Ping como sonda de versión que el front usa para mostrar el estado de conexión (contrato mínimo entre repos).
   - `ProblemDetails` estándar (RFC 9457) para los errores genéricos; el contrato de errores de negocio va en 02-03.
3. **Guardarraíles:** `[AllowAnonymous]` explícito en el ping; fila en `docs/endpoints.md` verificada por el guard.
4. **Validación:** Integration (health y ping), Security (errores con grupo de control), Contract (fixture).
5. **Secuencia:** DbContext → health → ping → manejo de errores → catálogo → fixture.

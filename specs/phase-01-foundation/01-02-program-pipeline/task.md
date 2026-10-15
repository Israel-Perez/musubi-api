# 01-02 — Pipeline y DI (tareas)

## T1 — Registro DI y pipeline en orden fijo
- **Crear:** `Musubi.Api/Program.cs`, `Musubi.Api/Platform/Hosting/*.cs`, `UnitTest/DI/ServiceRegistrationTests.cs`, `IntegrationTest/Middleware/MiddlewareTests.cs`, `SecurityTest/Hsts/HstsTests.cs`, `SecurityTest/Cors/CorsTests.cs`.
- **Verificar:** Unit 19/19, Integration 8/8, Security 11/11 con `--no-build`.
- **Evidencia (15-Oct-2026):** suites en verde; sin `UseHsts` → Security 10/11 (la prueba de HSTS en el wire falla, como se esperaba).
- **Estado:** ✅ Hecho.

## Pendientes
- `NOTE (HU-02)`: autenticación JWT + FallbackPolicy `RequireAuthenticatedUser`.
- `NOTE (02-05)`: KnownNetworks/KnownProxies del balanceador en ForwardedHeaders; `AllowedHosts` real.
- `02-04`: headers de seguridad y rate limiting.

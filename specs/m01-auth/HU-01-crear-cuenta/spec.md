# HU-01 — Crear cuenta de estudiante

**HU / Jira / Sprint / Módulo / Contraparte:** HU-01 / SCRUM-19 / S2 / M-01 / [musubi-app:HU-01](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m01-auth/HU-01-crear-cuenta)

## Contexto
Un estudiante crea su cuenta solo con su correo institucional, como en las pantallas `Registro` y `Registro-error` de la maqueta. El campus se detecta por el dominio del correo. **Depende de**: `02-01` (colecciones de identidad) y `02-03` (errores, contraseñas, correo y rate limit).

## Requisitos
1. **Detectar el campus** mientras se escribe el correo (`POST /auth/campus`). Un correo personal o mal formado responde `auth.email_not_institutional` (422).
2. **Registrar** con correo institucional, nombre completo, contraseña y la aceptación de términos y aviso de privacidad (HU-47):
   - contraseña: 8 o más caracteres, una letra y un número, según la maqueta (RNF-01);
   - el correo es único sin importar mayúsculas ni espacios;
   - la cuenta nace `pendingVerification` y se envía el código de HU-03.
3. **Sin sesión hasta verificar:** el registro no entrega tokens. Así el usuario no puede publicar, pedir, ofertar, conversar ni comprar hasta verificar (RN-01).
4. **Una sola cuenta** compra y vende (RN-02): el rol es `student`.
5. **Errores claros en español** (RNF-18), con códigos del catálogo.

## Diseño
- **`AuthController`** (`[AllowAnonymous]` + `[EnableRateLimiting("auth")]`) delega en **`RegistrationService`**, que devuelve `Result<T>`, sin excepciones de negocio. Orden de validación del registro:
  1. modelo (400 `validation.failed`);
  2. consentimientos (400 `auth.consent_required`);
  3. política de contraseña (400 `auth.weak_password`);
  4. dominio del correo (422);
  5. correo duplicado (409, por el índice único de 02-01).
- **`EmailAddresses.TryNormalize`:** minúsculas y sin espacios; rechaza `Nombre <correo>` y dominios sin punto.
- **Campus:** solo comunidades `active` (RNF-21: otro campus es otro documento en `communities`).
- **Enumeración aceptada:** el 409 revela que un correo ya está registrado. La UX de la maqueta lo necesita ("Inicia sesión"); el riesgo lo mitiga el rate limit por IP.

## Contratos
- Filas en [`docs/endpoints.md`](../../../docs/endpoints.md): `POST /api/v1/auth/campus` y `POST /api/v1/auth/register`.
- Fixtures: `ContractTest/Fixtures/{campus,register}.json`.
- Errores: [`docs/errores.md`](../../../docs/errores.md).

## Tests
- `IntegrationTest/Auth/RegistrationFlowTests.cs`:
  - el campus detecta UTTT con mayúsculas;
  - rechaza correos personales o mal formados;
  - flujo completo de registro y confirmación;
  - las respuestas no llevan hashes, código ni contraseña;
  - consentimientos faltantes;
  - contraseña débil (3 casos);
  - correo personal;
  - duplicado en cualquier caso de mayúsculas.
- `UnitTest/Auth/RegistrationTests.cs`: normalización (7 casos) y correo con el nombre codificado en HTML.
- `ContractTest/AuthContractTests.cs`: formas de `campus` y `register`.

## Criterios
- Unit 90/90, Integration 28/28, Security 26/26 (el controller nuevo declara su auth) y Contract 4/4.
- `pwsh scripts/check_endpoints.ps1` → OK (7 rutas) · critic PASS (la contraseña del DTO lleva su excepción `critic-allow: input-credential (HU-01)`).

## Límites
- Sin captcha: la protección es el rate limit por IP (`NOTE (02-05)`: necesita los `KnownProxies` del ALB).
- El nombre se guarda tal como se escribe (2–120 caracteres); el perfil lo edita en HU-04.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 21-Oct-2026
- **Detalle:** Primera HU del módulo M-01. Se entrega en el mismo PR que HU-03 y HU-47 porque comparten el flujo y los endpoints.

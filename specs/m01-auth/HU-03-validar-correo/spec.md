# HU-03 — Validar pertenencia con correo institucional

**HU / Jira / Sprint / Módulo / Contraparte:** HU-03 / SCRUM-21 / S2 / M-01 / [musubi-app:HU-03](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m01-auth/HU-03-validar-correo)

## Contexto
Para validar la pertenencia, un código de 6 dígitos llega al correo institucional (pantallas `Verificacion` y `Verificacion-lista`). Resuelve el riesgo R-13. **Depende de**: `HU-01` y `02-03`.

## Requisitos
1. **Código:** 6 dígitos de un CSPRNG que vencen a los **10 minutos**. Se guarda solo su hash, ligado al usuario.
2. **Reenvío:** cada **60 s** como mínimo (`auth.resend_too_soon`). Responde igual exista o no la cuenta y solo envía si la cuenta está pendiente.
3. **Intentos:** se aceptan **5**. Al quinto fallo el código se bloquea (`auth.code_attempts_exceeded`) aunque después llegue el correcto: hay que pedir otro.
4. **Confirmación:** marca el correo verificado (`active`, `emailVerifiedAt`), borra el código y **abre la sesión** con el access JWT y el refresh token de 02-03. La respuesta trae el usuario y su campus (`emailVerified: true`), que es lo que muestra `Verificacion-lista`.
5. **Sin pistas:** un correo desconocido o ya verificado responde igual que un código inválido (`auth.invalid_code`).
6. **Multi-campus** (RNF-21): el campus sale de la comunidad del usuario, no de una constante.

## Diseño
- `VerificationCodes` (`Generate`, `Hash` = SHA-256 de `userId:código`, `Matches` en tiempo constante).
- `RegistrationService.ResendAsync` / `ConfirmAsync`, sobre `VerificationCodeService` (códigos por propósito, compartido con HU-56).
- La atomicidad del conteo de intentos es la de `IncrementAttemptsAsync` (02-01, R7).
- El correo lo arma `AuthEmails.VerificationCode` (texto + HTML con el nombre codificado) y lo envía `IEmailSender`: log en Development, SES en la nube.

## Contratos
- Filas en [`docs/endpoints.md`](../../../docs/endpoints.md): `POST /api/v1/auth/verification/resend` y `POST /api/v1/auth/verification/confirm`.
- Fixture: `ContractTest/Fixtures/session.json`.

## Tests
- `IntegrationTest/Auth/RegistrationFlowTests.cs`:
  - flujo completo con sesión;
  - 5 intentos y bloqueo;
  - código vencido (reloj +11 min);
  - correo desconocido igual a código inválido;
  - el reenvío respeta la espera y reemplaza el código;
  - reenvío a un correo desconocido sin enviar nada.
- `UnitTest/Auth/RegistrationTests.cs`: formato, variedad, hash ligado al usuario y comparación.
- Self-test (21-Oct-2026): sin el bloqueo de 5 intentos, falla `Confirm_WrongCode_FailsAndLocksAfterFiveAttempts`.

## Criterios
- Integration 28/28 · Unit 90/90 · Contract 4/4.

## Límites
- El hash del código no lleva *pepper*: si la base se filtrara, un código vigente (10 min) se podría adivinar fuera de línea. El riesgo se acota con la vigencia corta y el borrado al usarse.
- Sin dominio propio, SES no entrega bien a Gmail/Workspace (la UTTT usa Google). El dominio queda pendiente del equipo (`NOTE (02-05)`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 21-Oct-2026
- **Detalle:** Valores de la propuesta D-03 (6 dígitos, 10 min, 5 intentos, 60 s).

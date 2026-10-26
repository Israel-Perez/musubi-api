# HU-56 — Recuperar mi contraseña

**HU / Jira / Sprint / Módulo / Contraparte:** HU-56 / SCRUM-78 / S2 / M-01 / [musubi-app:HU-56](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m01-auth/HU-56-recuperar-contrasena)

## Contexto
Desde `Iniciar sesion` → "¿Olvidaste tu contraseña?", el estudiante pide un código a su correo institucional y define una contraseña nueva. **Depende de**: `HU-02` y del mecanismo de códigos de `HU-03`.

## Requisitos
1. **Pedir el código:** responde **siempre** 202 con la misma forma, exista o no la cuenta. Solo envía si la cuenta está **activa** y pasó la espera de **60 s**.
2. **Código:** el de HU-03 (6 dígitos, 10 min, 5 intentos, solo el hash), con el propósito `passwordReset`. Un código de verificación del correo no sirve aquí.
3. **Cambiar la contraseña:** correo, código y contraseña nueva con la política de HU-01 (`auth.weak_password`). Un correo desconocido o una cuenta no activa responden `auth.invalid_code`.
4. **Efecto:** reemplaza el hash, borra el código (un solo uso) y **cierra todas las sesiones** de la cuenta: quien tuviera la contraseña vieja o una sesión robada queda fuera.
5. El correo dice qué pasó y aclara que, si no lo pidió, su contraseña no cambia.

## Diseño
- `PasswordRecoveryService` (`StartAsync`, `CompleteAsync`) y `PasswordController` (`api/v1/auth/password`).
- `VerificationCodeService` es el generador de códigos por propósito y lo comparten HU-03 y HU-56.
- `AuthEmails.PasswordResetCode`.
- `IRefreshTokenRepository.RevokeAllForUserAsync` (02-01).

## Contratos
- Filas en [`docs/endpoints.md`](../../../docs/endpoints.md): `POST /api/v1/auth/password/forgot` y `POST /api/v1/auth/password/reset`.
- Errores: `auth.invalid_code`, `auth.code_attempts_exceeded` y `auth.weak_password` en [`docs/errores.md`](../../../docs/errores.md).

## Tests
- `IntegrationTest/Auth/PasswordRecoveryFlowTests.cs` (9):
  - flujo completo: la contraseña vieja falla, la nueva entra y la sesión abierta queda cerrada;
  - cuenta desconocida, pendiente o correo malformado: la misma respuesta y ningún correo;
  - dos pedidos dentro de la espera envían un solo correo;
  - 5 intentos y bloqueo (la contraseña no cambia);
  - el código es de un solo uso;
  - código vencido (reloj +11 min);
  - contraseña débil: la anterior sigue funcionando;
  - el código de verificación de una cuenta pendiente no sirve;
  - código malformado → `validation.failed`.
- Self-test (21-Oct-2026): sin `RevokeAllForUserAsync`, falla `ForgotThenReset_ChangesThePasswordAndClosesEverySession`.

## Criterios
- Los de HU-02 (misma corrida): Integration 46/46 · Contract 5/5 · critic PASS · Semgrep 0.

## Límites
- La entrega real del correo depende del dominio y de SES en producción (`NOTE (02-05)`, ver HU-03).
- Mientras la espera de 60 s esté corriendo, un segundo pedido no envía nada y la respuesta no lo dice (para no revelar la cuenta).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 21-Oct-2026
- **Detalle:** HU nueva (SCRUM-78), nace de la maqueta; asignada a S2 junto con HU-02.

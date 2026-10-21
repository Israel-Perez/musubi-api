# HU-47 — Aceptar aviso de privacidad y términos de uso

**HU / Jira / Sprint / Módulo / Contraparte:** HU-47 / SCRUM-33 / S2 / M-01 / [musubi-app:HU-47](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m01-auth/HU-47-aviso-privacidad) (texto y pantallas)

## Contexto
El registro exige aceptar los Términos y condiciones y el Aviso de privacidad, con las casillas de la pantalla `Registro`. El texto vive en la app y en la landing (lineamientos 16 y 17). El back **registra qué versión aceptó cada usuario y cuándo**, como evidencia ante la LFPDPPP. **Depende de**: `HU-01`.

## Requisitos
1. Sin aceptar los dos documentos no hay cuenta: `auth.consent_required` (400). Omitir los campos es un error de validación (400).
2. El usuario guarda `consents { termsVersion, privacyVersion, acceptedAt }` con las versiones vigentes de la configuración (`Legal:*`).
3. Cambiar la versión de un documento solo cambia la configuración. Volver a pedir la aceptación a los usuarios existentes es otra HU, cuando haya un cambio material (`NOTE (HU-47)`).

## Diseño
`LegalOptions` (`Legal:TermsVersion`, `Legal:PrivacyVersion`; default `2026-10`) y la validación en `RegistrationService.RegisterAsync`, antes de crear la cuenta.

## Contratos
- `RegisterRequest.acceptTerms` y `acceptPrivacy` (fila `POST /api/v1/auth/register`).
- Configuración: ver `01-04` (`Legal:*`).
- El texto del aviso es `musubi-app/docs/legal/` (canónico; la app y la landing lo usan).

## Tests
- `IntegrationTest/Auth/RegistrationFlowTests.cs`:
  - sin términos o sin aviso, responde `auth.consent_required` (2 casos);
  - si se omiten, responde `validation.failed`;
  - las versiones aceptadas quedan guardadas en el usuario.

## Criterios
- Integration 28/28.

## Límites
- El aviso es un **borrador** apegado a la LFPDPPP. Requiere revisión del equipo, del PO o de un asesor antes de publicarse.
- No hay un endpoint para consultar las versiones. Si la app necesita detectar un cambio, se agrega con la HU de reaceptación.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 21-Oct-2026
- **Detalle:** Lado back de HU-47. Las pantallas y el texto van en musubi-app.

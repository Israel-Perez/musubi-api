# Catálogo de errores de musubi-api (canónico)

Contrato de errores (spec `02-03`). **musubi-app traduce por `code`**, nunca por el texto, así que un `code` publicado no cambia de significado. Las specs enlazan este catálogo y no lo copian. En el código, los genéricos viven en `Platform/Errors/ApiError.cs` y los de cada módulo en `Modules/<Modulo>/<Modulo>Errors.cs`.

## Forma

Cada error es `application/problem+json` (RFC 9457):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
  "title": "Inicia sesión para continuar.",
  "status": 401,
  "code": "auth.unauthorized",
  "traceId": "0HN7Q:00000001"
}
```

- `title`: mensaje en español, apto para mostrarse tal cual (RNF-18).
- `code`: estable, en minúsculas con punto (`<área>.<motivo>`).
- `traceId`: para buscar el error en los logs (02-06).
- **Nunca** trae `detail`, tipo de excepción ni stack trace (`SecurityTest/Errors/`).
- Los errores de validación agregan `errors: { "<Campo>": ["mensaje"] }`.
- Un 429 agrega el header `Retry-After` (segundos).
- La forma la fija `ContractTest/Fixtures/problem.json`.

## Genéricos (cualquier endpoint)

| code | HTTP | Mensaje | Cuándo |
|---|---|---|---|
| `validation.failed` | 400 | Revisa los datos enviados. | Entrada inválida (incluye `errors`) |
| `auth.unauthorized` | 401 | Inicia sesión para continuar. | Sin token, token vencido o inválido. También en rutas inexistentes sin sesión (no se pueden enumerar rutas) |
| `auth.forbidden` | 403 | No tienes permiso para hacer esto. | Con sesión, pero sin el rol o sin ser el dueño (RNF-03) |
| `resource.not_found` | 404 | No encontramos lo que buscas. | Con sesión, recurso o ruta inexistente |
| `request.method_not_allowed` | 405 | Esta operación no está disponible. | Verbo no soportado |
| `resource.conflict` | 409 | La información cambió o ya existe. Actualiza e inténtalo de nuevo. | Concurrencia optimista o duplicado genérico |
| `request.too_large` | 413 | El archivo o los datos son demasiado grandes. | Cuerpo de más de 10 MB (Kestrel) |
| `request.unprocessable` | 422 | No se pudo completar la operación. | Regla de negocio genérica |
| `rate_limit.exceeded` | 429 | Demasiados intentos. Espera un momento e inténtalo de nuevo. | Rate limit (por ahora en `/auth/*`) |
| `server.error` | 500 | Ocurrió un error inesperado. Inténtalo más tarde. | Excepción no controlada |
| `server.unavailable` | 503 | El servicio no está disponible por ahora. Inténtalo en unos minutos. | Dependencia caída |

## M-01 · Autenticación y usuarios (`Modules/Auth/AuthErrors.cs`)

| code | HTTP | Mensaje | HU |
|---|---|---|---|
| `auth.invalid_credentials` | 401 | El correo o la contraseña no coinciden. Revísalos e inténtalo otra vez. | HU-02 (nunca revela si el correo existe) |
| `auth.session_expired` | 401 | Tu sesión terminó. Inicia sesión de nuevo. | HU-02 (refresh inválido, vencido o reutilizado) |
| `auth.email_not_verified` | 403 | Verifica tu correo institucional para continuar. | HU-02 / HU-03 |
| `auth.account_suspended` | 403 | Tu cuenta está suspendida. Escríbenos si crees que es un error. | HU-40 |
| `auth.email_not_institutional` | 422 | Usa el correo que te dio tu universidad, por ejemplo tu.nombre@uttt.edu.mx. | HU-01 (maqueta `Registro-error`) |
| `auth.email_already_registered` | 409 | Ese correo ya tiene una cuenta. Inicia sesión. | HU-01 |
| `auth.weak_password` | 400 | La contraseña debe tener al menos 8 caracteres, una letra y un número. | HU-01 (política de la maqueta) |
| `auth.consent_required` | 400 | Acepta los Términos y condiciones y el Aviso de privacidad para continuar. | HU-01 / HU-47 |
| `auth.invalid_code` | 400 | El código no es válido o ya venció. Pide uno nuevo. | HU-03 |
| `auth.code_attempts_exceeded` | 429 | Demasiados intentos con este código. Pide uno nuevo. | HU-03 |
| `auth.resend_too_soon` | 429 | Espera un momento antes de pedir otro código. | HU-03 |

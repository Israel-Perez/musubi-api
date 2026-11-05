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

## Plataforma · Moderación (`Platform/Moderation/Moderation.cs`, 02-07)

| code | HTTP | Mensaje | HU |
|---|---|---|---|
| `moderation.image_rejected` | 422 | Esta foto no cumple las reglas de la comunidad. Usa otra. | 02-07 (Rekognition; fotos de productos y de perfil). La foto nunca llega a S3 |
| `moderation.text_rejected` | 422 | Revisa el nombre del producto / la descripción: no cumple las reglas de la comunidad. | 02-07 (Translate + Comprehend). El mensaje nombra el campo |
| `moderation.unavailable` | 503 | No pudimos revisar tu contenido. Inténtalo en unos minutos. | 02-07: si AWS no responde no se guarda nada (nunca se publica sin moderar) |

## M-01 · Autenticación y usuarios (`Modules/Auth/AuthErrors.cs`)

| code | HTTP | Mensaje | HU |
|---|---|---|---|
| `auth.invalid_credentials` | 401 | El correo o la contraseña no coinciden. Revísalos e inténtalo otra vez. | HU-02 (nunca revela si el correo existe) |
| `auth.session_expired` | 401 | Tu sesión terminó. Inicia sesión de nuevo. | HU-02 (refresh inválido, vencido o reutilizado) |
| `auth.email_not_verified` | 403 | Verifica tu correo institucional para continuar. | HU-02 / HU-03 |
| `auth.account_suspended` | 403 | Tu cuenta está suspendida. Escríbenos si crees que es un error. | HU-40 |
| `auth.email_not_institutional` | 422 | Usa el correo que te dio tu universidad, por ejemplo tu.nombre@uttt.edu.mx. | HU-01 (maqueta `Registro-error`) |
| `auth.email_already_registered` | 409 | Ese correo ya tiene una cuenta. Inicia sesión. | HU-01 |
| `auth.weak_password` | 400 | La contraseña debe tener al menos 8 caracteres, una letra y un número. | HU-01 / HU-56 (política de la maqueta) |
| `auth.wrong_password` | 400 | La contraseña no es correcta. | HU-46 (y HU-04 al cambiarla). Es 400 y no 401: la app trata un 401 como sesión vencida |
| `auth.consent_required` | 400 | Acepta los Términos y condiciones y el Aviso de privacidad para continuar. | HU-01 / HU-47 |
| `auth.invalid_code` | 400 | El código no es válido o ya venció. Pide uno nuevo. | HU-03 / HU-56 |
| `auth.code_attempts_exceeded` | 429 | Demasiados intentos con este código. Pide uno nuevo. | HU-03 / HU-56 |
| `auth.resend_too_soon` | 429 | Espera un momento antes de pedir otro código. | HU-03 |

## M-02 · Perfil (`Modules/Profiles/ProfilesErrors.cs`)

La foto de perfil usa los errores `listings.photo_*` de M-03 (mismo procesamiento).

| code | HTTP | Mensaje | HU |
|---|---|---|---|
| `profiles.invalid_career` | 400 | Elige una carrera de la lista. | HU-04 (carrera que no está activa en el campus) |
| `profiles.invalid_term` | 400 | Elige un periodo que corresponda a tu carrera. | HU-04 (fuera del rango de la carrera, o del campus si no hay carrera) |
| `profiles.not_found` | 404 | No encontramos ese perfil. | HU-04 / HU-05 (inexistente, inactiva o de otro campus: no revela cuál, OWASP API3) |

## M-03 · Publicaciones (`Modules/Listings/ListingsErrors.cs`)

| code | HTTP | Mensaje | HU |
|---|---|---|---|
| `listings.invalid_photos` | 400 | Agrega de 1 a 8 fotos tuyas para continuar. | HU-06 (RN-03; solo llaves subidas por quien publica, OWASP API1) |
| `listings.invalid_title` | 400 | Escribe el nombre del producto. | HU-06 (3 a 80 caracteres) |
| `listings.invalid_description` | 400 | La descripción puede tener hasta 500 caracteres. | HU-06 |
| `listings.invalid_price` | 400 | Ponle un precio a tu producto. | HU-06 (RN-04: mayor que cero; hasta $999,999) |
| `listings.invalid_category` | 400 | Elige una categoría. | HU-06 |
| `listings.invalid_condition` | 400 | Elige el estado del producto. | HU-06 (nuevo, o usado con su detalle) |
| `listings.invalid_stock` | 400 | Las piezas disponibles van de 1 a 10. | HU-06 / HU-50 |
| `listings.invalid_cursor` | 400 | No pudimos cargar más publicaciones. Actualiza la lista. | HU-48 (cursor que no emitió la API) |
| `listings.unknown_campus` | 404 | No encontramos ese campus. | HU-48 (vitrina del visitante) |
| `listings.product_not_found` | 404 | No encontramos esta publicación. | HU-07 (inexistente, de otra persona o quitada por moderación: no revela cuál, OWASP API1) |
| `listings.photo_unsupported` | 415 | No pudimos leer esa foto. Prueba con otra en JPG, PNG o WebP. | HU-06 |
| `listings.photo_too_small` | 422 | La foto es muy pequeña. Usa una de al menos 200 px. | HU-06 |
| `listings.photo_too_large` | 413 | La foto es demasiado grande. Prueba con otra. | HU-06 (más de 50 MP o de 10 MB) |

## M-07 · Chat (`Modules/Chat/ChatErrors.cs`)

| code | HTTP | Mensaje | HU |
|---|---|---|---|
| `chat.product_unavailable` | 404 | Esta publicación ya no está disponible. | HU-20 (inexistente, de otro campus, pausada o quitada: no revela cuál) |
| `chat.own_product` | 400 | Es tu publicación: aquí te escribirán los compradores. | HU-20 (el vendedor no se escribe a sí mismo) |
| `chat.conversation_not_found` | 404 | No encontramos esta conversación. | HU-20 (inexistente o de otras personas: no revela cuál, RN-22, OWASP API1) |
| `chat.invalid_message` | 400 | Escribe un mensaje de hasta 1000 caracteres. | HU-20 (vacío o de más de 1000 caracteres) |
| `chat.invalid_cursor` | 400 | No pudimos cargar mensajes anteriores. Vuelve a abrir la conversación. | HU-20 (cursor que no emitió la API) |

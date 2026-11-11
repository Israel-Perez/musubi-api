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
| `rate_limit.exceeded` | 429 | Demasiados intentos. Espera un momento e inténtalo de nuevo. | Rate limit (`/auth/*`, mensajes del chat y "Resumir acuerdo") |
| `server.error` | 500 | Ocurrió un error inesperado. Inténtalo más tarde. | Excepción no controlada |
| `server.unavailable` | 503 | El servicio no está disponible por ahora. Inténtalo en unos minutos. | Dependencia caída |

## Plataforma · Moderación (`Platform/Moderation/Moderation.cs`, 02-07)

| code | HTTP | Mensaje | HU |
|---|---|---|---|
| `moderation.image_rejected` | 422 | Esta foto no cumple las reglas de la comunidad. Usa otra. | 02-07 (Rekognition; fotos de productos y de perfil). La foto nunca llega a S3 |
| `moderation.text_rejected` | 422 | Revisa el nombre del producto / la descripción: no cumple las reglas de la comunidad. | 02-07 (léxico local; Translate + Comprehend con `Moderation:TextProvider=Aws`). El mensaje nombra el campo |
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
| `listings.invalid_condition` | 400 | Elige el estado del producto. | HU-06 (nuevo, o usado con su detalle); HU-10 (filtro) |
| `listings.invalid_stock` | 400 | Las piezas disponibles van de 1 a 10. | HU-06 / HU-50 |
| `listings.invalid_cursor` | 400 | No pudimos cargar más publicaciones. Actualiza la lista. | HU-48 / HU-10 (cursor que no emitió la API o de otro orden) |
| `listings.invalid_query` | 400 | Escribe lo que quieres buscar. | HU-09 (búsqueda sin letras ni dígitos, o de más de 80 caracteres) |
| `listings.invalid_price_range` | 400 | Revisa el rango de precio: debe ir de 0 a $999,999 y el mínimo no puede superar al máximo. | HU-10 (mínimo negativo, máximo fuera de rango o mínimo mayor que el máximo) |
| `listings.invalid_sort` | 400 | Elige un orden para los resultados. | HU-10 (orden fuera de la lista permitida) |
| `listings.invalid_page` | 400 | Elige una página del 1 al 50. | HU-10 (`page` menor que 1 o mayor que 50; acota el `skip` de Mongo, OWASP API4) |
| `listings.product_has_orders` | 409 | Tiene pedidos pendientes o confirmados. Termínalos o cancélalos antes de eliminarla. | HU-23 (RN-07: eliminar una publicación propia con pedidos abiertos; a otra persona se le responde 404) |
| `listings.product_sold` | 409 | Esta publicación ya se vendió. | HU-08 (editar, pausar o reactivar una publicación vendida; reactivarla la devolvería a la vitrina) |
| `listings.invalid_tab` | 400 | No pudimos cargar esa lista. Actualiza la pantalla. | HU-08 (pestaña de "Mis productos" que no es `active`, `sold` ni `soldOut`) |
| `listings.unknown_campus` | 404 | No encontramos ese campus. | HU-48 (vitrina del visitante) |
| `listings.product_not_found` | 404 | Esta publicación ya no está disponible. | HU-11 (inexistente, quitada, pausada para otros o de otro campus), HU-07 (editar, pausar, reactivar o eliminar una publicación ajena), HU-08 (marcar como vendida una publicación ajena; una vendida ajena tampoco responde 409) y HU-53 (guardar un favorito no visible): no revela cuál, OWASP API1 |
| `listings.photo_unsupported` | 415 | No pudimos leer esa foto. Prueba con otra en JPG, PNG o WebP. | HU-06 |
| `listings.photo_too_small` | 422 | La foto es muy pequeña. Usa una de al menos 200 px. | HU-06 |
| `listings.photo_too_large` | 413 | La foto es demasiado grande. Prueba con otra. | HU-06 (más de 50 MP o de 10 MB) |

## M-07 · Chat (`Modules/Chat/ChatErrors.cs`)

| code | HTTP | Mensaje | HU |
|---|---|---|---|
| `chat.product_unavailable` | 404 | Esta publicación ya no está disponible. | HU-20 (inexistente, de otro campus, pausada o quitada: no revela cuál) |
| `chat.own_product` | 400 | Es tu publicación: aquí te escribirán los compradores. | HU-20 (el vendedor no se escribe a sí mismo) |
| `chat.conversation_not_found` | 404 | No encontramos esta conversación. | HU-20 (inexistente o de otras personas: no revela cuál, RN-22, OWASP API1) |
| `chat.blocked` | 403 | No puedes escribirle a esta persona. | HU-57 (bloqueo en cualquier dirección: el mismo mensaje para los dos, no revela quién bloqueó) |
| `chat.invalid_message` | 400 | Escribe un mensaje de hasta 1000 caracteres. | HU-20 (vacío o de más de 1000 caracteres) |
| `chat.invalid_filter` | 400 | No pudimos aplicar ese filtro. Elige Todos, Compras o Ventas. | HU-22 (`role` que no es `buyer` ni `seller`) |
| `chat.invalid_cursor` | 400 | No pudimos cargar más. Vuelve a abrir la lista. | HU-20 / HU-22 (cursor que no emitió la API) |
| `reports.invalid_reason` | 400 | Elige el motivo del reporte. | HU-28 (motivo fuera de la lista de la maqueta) |
| `reports.invalid_details` | 400 | Cuéntanos qué pasó en hasta 1000 caracteres. | HU-28 |
| `reports.invalid_target` | 400 | No pudimos enviar este reporte. Revisa a quién o qué reportas. | HU-28 (tipo desconocido, id donde no va, reportarse a uno mismo o a su propia publicación) |
| `reports.target_not_found` | 404 | No encontramos lo que quieres reportar. | HU-28 (persona, publicación o conversación inexistente, de otro campus o ajena: no revela cuál) |
| `blocks.self` | 400 | No puedes bloquearte a ti mismo. | HU-57 |
| `blocks.user_not_found` | 404 | No encontramos a esta persona. | HU-57 (inexistente o de otro campus: no revela cuál) |
| `wanted.invalid_title` | 400 | Escribe qué buscas, de 3 a 80 caracteres. | HU-13 (RN-09) |
| `wanted.invalid_category` | 400 | Elige una categoría. | HU-13 (RN-09; slug del campus u "otro") |
| `wanted.invalid_description` | 400 | La descripción puede tener hasta 300 caracteres. | HU-13 |
| `wanted.invalid_features` | 400 | Agrega hasta 5 características de hasta 40 caracteres. | HU-13 |
| `wanted.invalid_budget` | 400 | Indica tu presupuesto máximo, de $1 a $999,999. | HU-13 (RN-09) |
| `wanted.invalid_quantity` | 400 | La cantidad va de 1 a 10 piezas. | HU-13 (RN-09) |
| `wanted.invalid_date` | 400 | Elige una fecha de hoy a 60 días. | HU-13 (día del campus, UTC-6) |
| `wanted.too_many_active` | 409 | Ya tienes 5 solicitudes activas. Espera a que alguna termine para publicar otra. | HU-13 (RN-12) |
| `wanted.not_active` | 409 | Esta solicitud ya no está activa, así que no se puede cambiar. | HU-14 (editar, cerrar o cancelar una cerrada, cancelada o vencida; RN-11) |
| `wanted.has_offers` | 409 | Ya recibiste ofertas: el producto y la categoría ya no se pueden cambiar. | HU-14 (quien ofreció respondió a ese producto) |
| `wanted.invalid_budget_range` | 400 | Revisa el rango de presupuesto: debe ir de 0 a $999,999 y el mínimo no puede superar al máximo. | HU-15 ("Presupuesto" de "Filtrar solicitudes") |
| `wanted.invalid_deadline` | 400 | Elige una fecha límite de 1 a 60 días. | HU-15 ("Fecha límite") |
| `wanted.invalid_query` | 400 | Escribe lo que quieres buscar. | HU-15 (más de 80 caracteres o sin letras ni dígitos) |
| `wanted.invalid_cursor` | 400 | No pudimos cargar más solicitudes. Actualiza la lista. | HU-15 (cursor que no es el id de una solicitud) |
| `offers.own_request` | 409 | No puedes ofrecer en tu propia solicitud. | HU-16 (RN-06) |
| `offers.product_unavailable` | 400 | Elige una de tus publicaciones disponibles. | HU-16 (no es tuya, no está disponible o es de otro campus) |
| `offers.invalid_description` | 400 | Describe lo que ofreces, de 3 a 300 caracteres. | HU-16 (oferta directa) |
| `offers.invalid_price` | 400 | Escribe tu precio, de $1 a $999,999. | HU-16 (oferta directa) |
| `offers.invalid_condition` | 400 | Elige si es nuevo o usado. | HU-16 (oferta directa) |
| `offers.invalid_photo` | 400 | Vuelve a agregar la foto. | HU-16 (llave que no subió esa persona o que no existe, OWASP API1) |
| `offers.invalid_message` | 400 | El mensaje puede tener hasta 300 caracteres. | HU-16 |
| `offers.not_editable` | 409 | Ya no puedes cambiar esta oferta: quien busca ya la eligió o la rechazó. | HU-17 |
| `offers.offer_not_found` | 404 | No encontramos esta oferta. | HU-17 (no es de esa solicitud o hay un bloqueo con quien la hizo) |
| `offers.not_pending` | 409 | Esta oferta ya no está pendiente. | HU-17 (ya aceptada o rechazada) |
| `offers.request_closed` | 409 | Tu solicitud ya no está activa. | HU-17 (aceptar en una solicitud cerrada, cancelada o vencida) |
| `offers.not_found` | 404 | Todavía no has ofrecido nada en esta solicitud. | HU-16 (`GET .../offers/mine`) |
| `wanted.not_found` | 404 | No encontramos esta solicitud. | HU-13 (inexistente, de otro campus, vencida para otros o con bloqueo: no revela cuál); HU-14 (editar, cerrar o cancelar la de otra persona) |

## M-08 · Pedidos (`Modules/Orders/OrdersErrors.cs`)

| code | HTTP | Mensaje | HU |
|---|---|---|---|
| `orders.not_found` | 404 | No encontramos este pedido. | HU-23 (inexistente o de otras personas: no revela cuál, RNF-03, OWASP API1) |
| `orders.conversation_not_found` | 404 | No encontramos esta conversación. | HU-23 (crear o listar desde una conversación inexistente o ajena, RN-22) |
| `orders.only_buyer` | 403 | El pedido lo crea quien compra. Tú lo aceptarás cuando llegue. | HU-23 (quien vende en esa conversación intenta crear el pedido o elegir el pago) |
| `orders.own_product` | 409 | No puedes comprar tu propia publicación. | HU-23 (RN-06) |
| `orders.product_unavailable` | 409 | Esta publicación ya no está disponible. | HU-23 (pausada, agotada, vendida, quitada, eliminada o de otro campus) |
| `orders.offer_not_accepted` | 409 | Primero acepta la oferta para crear el pedido. | HU-23 (conversación de una oferta todavía pendiente, HU-17) |
| `orders.blocked` | 403 | No puedes hacer un pedido con esta persona. | HU-57 (bloqueo en cualquier dirección: no revela quién bloqueó) |
| `orders.invalid_quantity` | 400 | Elige una cantidad de 1 a 10 piezas. | HU-23 |
| `orders.not_enough_stock` | 409 | No hay tantas piezas disponibles. Elige una cantidad menor. | HU-23 (RN-17; en una oferta directa, más de las que pidió la solicitud) |
| `orders.invalid_meeting_point` | 400 | Elige un punto de entrega del campus. | HU-23 (punto que no es de los activos del campus) |
| `orders.invalid_detail` | 400 | El detalle del lugar puede tener hasta 120 caracteres. | HU-23 |
| `orders.invalid_delivery_time` | 400 | Elige una fecha de hoy a 14 días, entre las 7:00 y las 21:00. | HU-23 (antes de 30 minutos, después de 14 días o fuera de 7:00 a 21:00 del campus, UTC-6) |
| `orders.already_open` | 409 | Ya hay un pedido abierto en esta conversación. | HU-23 (uno pendiente o confirmado por conversación; un doble toque no duplica) |
| `orders.not_pending` | 409 | Este pedido ya no está pendiente. | HU-23 (elegir el pago de un pedido confirmado, completado o cancelado) |
| `orders.invalid_payment` | 400 | Elige cómo quieres pagar. | HU-23 (forma que no es `cash` ni `transfer`) |
| `orders.payment_unavailable` | 409 | Quien vende aún no registra su cuenta. Por ahora paga en efectivo al recibir. | HU-23 (transferencia antes de HU-55, D-01) |
| `orders.invalid_conversation` | 400 | No pudimos cargar el pedido. Vuelve a abrir el chat. | HU-23 (`GET /orders` sin `conversationId` o con uno que no es un id) |
| `orders.agreement_not_found` | 404 | Este acuerdo ya no existe. Pide otro resumen en el chat. | HU-34 (crear el pedido con un acuerdo inexistente, vencido, ya usado o ajeno) |
| `orders.agreement_not_confirmed` | 409 | Los dos tienen que confirmar el acuerdo antes de crear el pedido. | HU-34 (RN-41) |

## M-10 · IA generativa (`Modules/GenAi/AgreementsErrors.cs`)

| code | HTTP | Mensaje | HU |
|---|---|---|---|
| `genai.unavailable` | 503 | La IA no está disponible ahora. Llena el resumen a mano o inténtalo más tarde. | HU-32 (RNF-15: sin proveedor, sin red, cuota excedida o respuesta con otra forma; la app abre el resumen sin IA) |
| `agreements.not_found` | 404 | Este acuerdo ya no existe. Pide otro resumen en el chat. | HU-34 (inexistente, vencido, descartado o ajeno: no revela cuál, OWASP API1) |
| `agreements.conversation_not_found` | 404 | No encontramos esta conversación. | HU-32 (conversación inexistente o ajena, RN-22) |
| `agreements.invalid_conversation` | 400 | No pudimos leer la conversación. Vuelve a abrir el chat. | HU-32 (sin `conversationId` o con uno que no es un id) |
| `agreements.blocked` | 403 | No puedes hacer un acuerdo con esta persona. | HU-57 (bloqueo en cualquier dirección) |
| `agreements.order_open` | 409 | Esta conversación ya tiene un pedido abierto. | HU-32 (ya hay uno pendiente o confirmado) |
| `agreements.product_unavailable` | 409 | Esta publicación ya no está disponible. | HU-33 (publicación pausada, agotada o quitada, u oferta no aceptada) |
| `agreements.nothing_to_summarize` | 422 | Todavía no hay mensajes que resumir. Platiquen primero el trato. | HU-32 (sin mensajes de texto: no se llama a la IA) |
| `agreements.changed` | 409 | El acuerdo cambió. Revísalo de nuevo antes de confirmarlo. | HU-34 (RN-41: se confirma o se corrige la versión que se vio) |
| `agreements.invalid_revision` | 400 | No pudimos leer el acuerdo. Vuelve a abrirlo. | HU-34 (sin `revision`) |
| `agreements.incomplete` | 422 | Completa los datos que faltan antes de confirmar. | HU-34 (RN-42: algo sigue "No identificado") |
| `agreements.invalid_title` | 400 | Escribe el producto en hasta 80 caracteres. | HU-34 |
| `agreements.invalid_price` | 400 | Escribe un precio de $1 a $999,999. | HU-34 |
| `agreements.invalid_quantity` | 400 | Elige una cantidad de 1 a lo disponible. | HU-34 (RN-17) |
| `agreements.invalid_meeting_point` | 400 | Elige un punto de entrega del campus. | HU-34 |
| `agreements.invalid_detail` | 400 | El detalle del lugar puede tener hasta 120 caracteres. | HU-34 |
| `agreements.invalid_delivery_time` | 400 | Elige una fecha de hoy a 14 días, entre las 7:00 y las 21:00. | HU-34 (también al confirmar si la fecha ya pasó) |
| `agreements.invalid_conditions` | 400 | Las condiciones pueden tener hasta 200 caracteres. | HU-34 |

# Matriz de versionamiento — MUSUBI

<!-- Sección compartida con musubi-app/musubi-api: si cambia, cámbiala en ambos repos y regístralo en las dos Memoria.md -->
Lineamiento 11 de la materia: la numeración se define **antes del desarrollo** y se aplica **en cada despliegue** (checklist en `musubi-api/docs/bitacora/lineamientos-materia.md`, L-11). Vigente desde el 20-Oct-2026 (SCRUM-66).

## 1. Esquema

Todos los componentes usan **SemVer 2.0** (`MAJOR.MINOR.PATCH[-pre][+build]`):

| Sube | Cuándo (antes de 1.0.0) | Cuándo (desde 1.0.0) |
|---|---|---|
| `MAJOR` | Solo al pasar a `1.0.0` (versión evaluada el 02-Dic-2026) | Cambio incompatible del contrato HTTP, del esquema de datos o de la app mínima soportada |
| `MINOR` | Cierre de sprint con HU nuevas, o un cambio incompatible (en 0.x se permite) | Funcionalidad nueva compatible |
| `PATCH` | Corrección sin funcionalidad nueva (`fix/SCRUM-NN-*`) | Igual |
| `-pre` | `-alpha.N` (interno), `-beta.N` (prueba cerrada de Google Play), `-rc.N` (candidata a la evaluación) | Igual |

Reglas:

- Nada se despliega dos veces con el mismo número. Un despliegue sin cambio de versión es un error del pipeline.
- La versión vive en **un solo lugar por componente** (tabla 2) y el tag de Git se crea al desplegar, nunca a mano.
- Antes del primer despliegue no se sube el número: los PR acumulan cambios sobre la versión en curso.
- El título del PR (clave Jira) es la nota de versión; el changelog se arma con los PR fusionados entre dos tags.

## 2. Componentes

| Componente | Fuente de la versión | Tag de Git | Artefacto | Regla propia |
|---|---|---|---|---|
| API (`musubi-api`) | `<Version>` en `Musubi.Api/Musubi.Api.csproj` | `api-vX.Y.Z` | Imagen `musubi-api:X.Y.Z` | La ruta `api/v1` es independiente: solo cambia a `v2` con un cambio incompatible del contrato y convive con `v1` mientras exista una app que la use |
| App Android (`musubi-app`) | `version: X.Y.Z+N` en `pubspec.yaml` | `app-vX.Y.Z+N` | AAB firmado | `N` (`versionCode`) crece en **cada** subida a Google Play, también entre pistas; nunca se reutiliza |
| Esquema de datos (MongoDB) | Documento `{ _id: "schema", version: N }` en la colección `_meta` | Va con el tag de la API | Script `mongosh` numerado (`NNN-<slug>.js`) | Entero creciente; cada script es idempotente y sube `N` en 1 |
| Infraestructura (Terraform) | `infra/VERSION` | `infra-vX.Y.Z` | Plan de Terraform | `MAJOR` si recrea recursos con datos (balanceador, base, bucket) |
| Landing page | `package.json` o `VERSION` del repo de la landing | `web-vX.Y.Z` | Sitio estático | Publica el enlace a Google Play y el aviso de privacidad vigente |

## 3. Matriz de compatibilidad

Una fila por despliegue. La app declara la versión mínima de API que necesita; la API declara la versión de esquema que espera.

| Fecha | API | Ruta | Esquema | App (versión+código) | Pista de Google Play | Infra | Notas |
|---|---|---|---|---|---|---|---|
| 20-Oct-2026 | 0.1.0 | `v1` | — | 0.1.0+1 | — (sin publicar) | — | Fundación + stack NoSQL (SCRUM-66). Sin despliegue todavía |
| 25-Oct-2026 | 0.2.0-alpha.1 | `v1` | 1 | 0.1.0+1 | — (sin publicar) | 0.1.0 | Primer despliegue en AWS (SCRUM-80): registro, verificación, sesión y recuperación de contraseña (HU-01/02/03/47/56) |
| 27-Oct-2026 | 0.3.0-alpha.1 | `v1` | 2 | 0.2.0+2 | — (sin publicar) | 0.2.0 | Publicar productos y vitrina pública (SCRUM-23, HU-06/50 y HU-48 fase 2): fotos en S3 + CloudFront, categorías y productos (`db/002-publicaciones.js`) |
| 29-Oct-2026 | 0.3.0-alpha.2 | `v1` | 2 | 0.2.0+2 | — (sin publicar) | 0.3.0 | Dominio propio (SCRUM-85): `api.musubiapp.com.mx` con certificado ACM y TLS 1.2 como mínimo, y correo desde `no-reply@musubiapp.com.mx`. API sin cambios funcionales: dependencias de Dependabot (Scalar 2.18.2, AWSSDK.S3 4.0.104.6, AWSSDK.SimpleEmailV2 4.0.106.6, imágenes .NET 10.0.14 / SDK 10.0.403) |
| 30-Oct-2026 | 0.3.0-alpha.2 | `v1` | 2 | 0.3.0+3 | Prueba interna | 0.3.0 | Primera versión en Google Play (SCRUM-87): AAB firmado con la llave de subida y Play App Signing, contra `https://api.musubiapp.com.mx` |
| 30-Oct-2026 | 0.3.0-alpha.3 | `v1` | 3 | 0.3.0+3 | Prueba interna | 0.3.0 | Cuenta de revisión de Google Play (SCRUM-88): dominio `musubiapp.com.mx` en la UTTT (`db/003-dominio-revision.js`); sin otros cambios en la API |
| 30-Oct-2026 | 0.3.0-alpha.4 | `v1` | 3 | 0.3.0+4 | Prueba cerrada | 0.3.1 | Eliminar mi cuenta desde la app (SCRUM-32, HU-46): `POST /api/v1/account/delete` con la contraseña actual, borrado real de la cuenta, las sesiones, las publicaciones y las fotos; `s3:ListBucket` en las fotos |
| 03-Nov-2026 | 0.4.0-alpha.1 | `v1` | 4 | 0.3.0+4 | Prueba cerrada | 0.3.2 | Editar mi perfil (SCRUM-22, HU-04), solo la API: perfil, "Quién te ve", foto de perfil, carreras del campus y cambiar la contraseña (8 rutas); `profiles`, `careers` y el periodo del campus (`db/004-perfiles.js`); `s3:ListBucket` también en `media/avatars/*`. La app lo usa desde su versión de HU-04 |
| 03-Nov-2026 | 0.4.0-alpha.1 | `v1` | 4 | 0.4.0+5 | Prueba cerrada | 0.3.2 | Editar mi perfil en la app (SCRUM-22, HU-04): `Perfil` con estadísticas y mosaicos, `Ajustes`, "Datos personales" (carrera, cuatrimestre y foto de perfil), "Correo institucional", "Cambiar contraseña" y "Quién te ve" en `Privacidad` |
| 03-Nov-2026 | 0.4.0-alpha.2 | `v1` | 4 | 0.4.0+5 | Prueba cerrada | 0.3.2 | Caché (SCRUM-91, 02-02): cache-aside de categorías y carreras (Redis compartido si existe `/musubi/prod/Cache/ConnectionString`; si no, memoria), caché de salida de la vitrina y las categorías anónimas, y `ETag`/`304` en categorías, vitrina y carreras. Sin cambios en la app ni en el esquema |
| 03-Nov-2026 | 0.4.0-alpha.3 | `v1` | 4 | 0.4.0+5 | Prueba cerrada | 0.3.3 | Moderación (SCRUM-92, 02-07): fotos (productos y perfil) con Rekognition y texto de las publicaciones con Translate + Comprehend antes de guardar; `422 moderation.*` y `503 moderation.unavailable`. Permisos `Moderar` en el rol de las instancias |
| 03-Nov-2026 | 0.4.0-alpha.4 | `v1` | 4 | 0.4.0+5 | Prueba cerrada | 0.3.4 | Observabilidad (SCRUM-93, 02-06): logs JSON con `TraceId`, una línea por petición sin datos personales, alarmas por correo (errores, 5xx, instancias sanas, latencia p95), dashboard `musubi-api` y Trivy en el CI |
| 04-Nov-2026 | 0.5.0-alpha.1 | `v1` | 5 | 0.4.0+5 | Prueba cerrada | 0.3.4 | Iniciar una conversación con el vendedor (SCRUM-29, HU-20), solo la API: 4 rutas de `/conversations` y el hub `/hubs/chat` (SignalR, solo WebSockets, con backplane de Redis si existe `/musubi/prod/Cache/ConnectionString`); `conversations` y `messages` (`db/005-chat.js`); texto moderado antes de guardar y rate limit `chat` |
| 06-Nov-2026 | 0.5.0-alpha.1 | `v1` | 5 | 0.4.0+5 | Prueba cerrada | 0.3.5 | Salida del grupo de seguridad de la API hacia Redis Cloud (`redis_port`, SCRUM-29): sin ella, la caché compartida y el backplane del chat no se conectaban. Misma imagen de la API |
| 06-Nov-2026 | 0.5.0-alpha.2 | `v1` | 6 | 0.4.0+5 | Prueba cerrada | 0.3.5 | Bandeja de conversaciones (SCRUM-31, HU-22), solo la API: `GET /conversations` (Todos, Compras y Ventas, por actividad reciente) con no leídos por lado y `POST /conversations/{id}/read`; copia de los nombres en la conversación (`db/006-chat-bandeja.js`) |
| 06-Nov-2026 | 0.5.0-alpha.2 | `v1` | 6 | 0.5.0+6 | Prueba cerrada | 0.3.5 | Chat en la app (SCRUM-29 y SCRUM-31, HU-20 y HU-22): `Chat-Valeria` en tiempo real (SignalR propio sobre WebSocket) desde un producto de Inicio, y la bandeja `Mensajes` con Todos, Compras, Ventas y no leídos |
| 06-Nov-2026 | 0.5.0-alpha.3 | `v1` | 6 | 0.5.0+6 | Prueba cerrada | 0.3.5 | Detalle de un producto (SCRUM-28, HU-11), solo la API: `GET /products/{id}` (público, con el vendedor solo con sesión; la pausada solo la ve su dueño) |
| 06-Nov-2026 | 0.5.0-alpha.3 | `v1` | 6 | 0.5.1+7 | Prueba cerrada | 0.3.5 | Detalle del producto en la app (SCRUM-28 y SCRUM-30, HU-11 y HU-21): `Producto` desde Inicio (también para visitantes), "Mensaje" y "Comprar" al chat, y "Ver" desde el chat |
| 08-Nov-2026 | 0.5.0-alpha.4 | `v1` | 6 | 0.5.1+7 | Prueba cerrada | 0.3.5 | Moderación de texto con léxico local (SCRUM-92, 02-07): el plan gratuito de AWS no incluye Translate ni Comprehend, y el chat y las publicaciones respondían `503 moderation.unavailable`. Las fotos siguen con Rekognition. Sin cambios en la app ni en el esquema |
| 08-Nov-2026 | 0.5.0-alpha.5 | `v1` | 7 | 0.5.1+7 | Prueba cerrada | 0.3.5 | Buscar productos por palabra clave (SCRUM-26, HU-09), solo la API: `GET /products?q=` por prefijo de palabra en el título y la descripción, sin distinguir mayúsculas ni acentos; `searchWords` con el índice `ix_community_status_search` (`db/007-busqueda.js`, con relleno de lo existente) y `400 listings.invalid_query` |
| 08-Nov-2026 | 0.5.0-alpha.6 | `v1` | 8 | 0.5.1+7 | Prueba cerrada | 0.3.5 | Reportar y bloquear (SCRUM-57 y SCRUM-95, HU-28 y HU-57), solo la API: `POST /reports` (aviso al equipo por correo con `Reports:NotifyEmail`), `GET`/`POST /blocks` y `DELETE /blocks/{userId}`; un bloqueo en cualquier dirección corta el chat (`chat.blocked`, `canReply`) y oculta publicaciones y perfil. La foto de perfil de la otra persona en el chat (`counterpart.photo`). `reports` y `blocks` (`db/008-reportes-bloqueos.js`) |
| 08-Nov-2026 | 0.5.0-alpha.7 | `v1` | 9 | 0.5.1+7 | Prueba cerrada | 0.3.5 | Filtros de la búsqueda (SCRUM-27, HU-10), solo la API: `GET /products` con `category` repetido, `minPriceCents`/`maxPriceCents`, `condition`, `sort`, `page` (1 a 50) y cursor compuesto; `hideSoldOut` explícito; cuatro índices de precio (`db/009-filtros-precio.js`); `listings.invalid_price_range`, `invalid_condition`, `invalid_sort` e `invalid_page` |

## 4. Plan hacia la evaluación

| Hito | API | App | Pista |
|---|---|---|---|
| Fin de S3 (08-Nov-2026) | 0.3.0 | 0.3.0+N | Prueba interna |
| Prueba cerrada (desde el 09-Nov-2026) | 0.4.0-beta.N | 0.4.0-beta.N+N | Prueba cerrada |
| Candidata (fin de S5) | 1.0.0-rc.N | 1.0.0-rc.N+N | Prueba cerrada o producción |
| Evaluación (02-Dic-2026) | 1.0.0 | 1.0.0+N | Producción |

## 5. Aplicación en cada despliegue

- NOTE (musubi-api:02-05): el workflow de despliegue lee `<Version>`, falla si el tag `api-vX.Y.Z` ya existe, publica la imagen con ese tag, crea el tag y agrega la fila de la tabla 3.
- NOTE (musubi-app:02-02): el workflow de Google Play lee `pubspec.yaml`, falla si `versionCode` no es mayor que el último publicado, sube el AAB, crea el tag y agrega la fila de la tabla 3.
- Hasta que existan esos workflows, la fila se agrega a mano en el PR que despliega.
<!-- Fin de sección compartida -->

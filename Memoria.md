# Memoria del proyecto — musubi-api

Bitácora viva: estado, decisiones con su porqué y errores a evitar. Reglas de mantenimiento en `AGENTS.md` §2. Las lecciones permanentes viven en `specs/phase-00-constitution/00-04-lessons-learned/spec.md`.

## Estado actual (04-Nov-2026)

- **En producción (03-Nov-2026):** API `0.4.0-alpha.4`, infra `0.3.4` y esquema 4 en `https://api.musubiapp.com.mx` (`infra-up` 41743342605). Incluye HU-46, HU-04 (perfil), caché (02-02), moderación (02-07) y observabilidad (02-06). SES envía desde `no-reply@musubiapp.com.mx`, y el sitio `musubiapp.com.mx` lo sirve el repo `musubi-landing`.
- **App en Google Play:** `0.4.0+5` en la prueba cerrada (HU-04), publicada desde `release-play` (musubi-app). La solicitud de producción se puede enviar desde el ~13-Nov.
- **Pendientes del usuario:** confirmar el correo de suscripción de SNS (alarmas), **Redis Cloud** (`/musubi/prod/Cache/ConnectionString`: desde HU-20 lo necesita el chat en tiempo real entre las 2 instancias), proveedor de IA (HU-32) y proyecto de Firebase (HU-31). La lista de carreras de la UTTT está pendiente de validar con el equipo.
- **En producción (06-Nov-2026):** API `0.5.0-alpha.1` con el chat (HU-20), infra `0.3.5` y Redis Cloud conectado en las 2 instancias. **En curso:** HU-22 (bandeja, API `0.5.0-alpha.2`) y la app del chat.

## T27 — Filtros por categoría y precio (07-Nov-2026, SCRUM-27, HU-10)

**Qué se hizo**
- `GET /api/v1/products` acepta `minPriceCents`, `maxPriceCents` (rango inclusivo) y `condition` (`new`/`used`); se combinan con AND con `category`, `q`, `hideSoldOut`, `campus`, cursor y límite.
- Error nuevo `listings.invalid_price_range` (400); el estado desconocido reutiliza `listings.invalid_condition`.
- Sin esquema ni índice nuevos: precio y estado son predicados residuales sobre `ix_community_status_id` / `ix_community_category_status_id`, y el orden por `_id` no cambia.
- Unit 184 · Integration 104 · Security 84 · Contract 8 · Performance 5; critic PASS y endpoints OK. `DatabaseTest` sin Docker en local: lo corre el CI.

**Decisiones (con su porqué)**
- **Extender la vitrina en lugar de una ruta nueva:** comparte auth anónima, caché de salida, `ETag`, cursor y `total`; una ruta `/products/filter` duplicaría controller y política de caché.
- **Precio en centavos:** toda la API ya expone `priceCents`, `lowCents` y `highCents`; la app convierte desde pesos.
- **`condition` aunque Jira no lo enumere:** la hoja "Filtros" de `Busqueda-filtros` muestra Todos/Nuevo/Usado y el lineamiento 4 prohíbe quitar funciones de la maqueta.
- **Validar en el servicio:** las dos implementaciones del repositorio comparten las reglas de frontera.
- **Sin índice con `priceCents`:** rompería el orden por `_id` (sort bloqueante) y encarece cada inserción; el rango se filtra sobre las coincidencias de comunidad/categoría. Con el piloto y `limit ≤ 50` es aceptable (`NOTE (HU-10)`).
- **Rama apilada y PR Draft #50:** `main` aún no tiene `q` (HU-09, #49); al fusionarse #49 el PR se retargetea solo a `main`.

**Errores a evitar**
- **`new` no lleva detalle:** `conditionDetail` solo aplica a `used`; mandar `likeNew` con `new` responde `400` (`ValidCondition`). En las pruebas se usa `detail: null`.
- **El rango no se recorta ni se ignora:** `min > max`, negativo o fuera de `MaxPriceCents` responde `400`; devolver resultados como si no hubiera filtro oculta el error de la app.

## T26 — Búsqueda de productos por palabra clave (07-Nov-2026, SCRUM-26, HU-09)

**Qué se hizo**
- `GET /api/v1/products?q=` (API): coincidencia por **prefijo de palabra** en `title` y `description`, sin distinguir mayúsculas ni acentos, solo disponibles, de la más nueva a la más vieja, con el cursor `_id` de la vitrina. `calc` y `calcula` encuentran `Calculadora`.
- Error nuevo `listings.invalid_query` (400): más de 80 caracteres o sin letras ni dígitos. `q` vacío equivale a no buscar.
- Campo nuevo `searchWords[]` (palabras normalizadas de título y descripción) e índice `ix_community_status_search`; `db/007-busqueda.js` y esquema 7, con relleno de lo existente.
- Unit 181 · Integration 100 · Security 84 · Contract 8 · Performance 4; critic PASS y endpoints OK. `DatabaseTest` sin Docker en local: lo corre el CI.

**Decisiones (con su porqué)**
- **Prefijo en lugar de palabra completa:** el requisito pide encontrar `Calculadora` escribiendo `calc`; `$text` no soporta prefijos y Atlas Search no existe en MongoDB Community, así que se compara por prefijo.
- **Campo `searchWords` indexado:** un `$regex` sobre `title`/`description` recorría toda la comunidad; con el campo multikey y el índice `ix_community_status_search` (comunidad, estado, palabras) el `^prefijo` es un rango indexado. El costo es el campo derivado y su relleno.
- **Acentos, mayúsculas y plural resueltos al escribir:** `SearchWord.Normalize`/`Words` dejan las palabras en minúsculas, sin acentos y sin `s`/`es` final, tanto en el documento como en la consulta; no hay variantes en el índice.
- **El campo se calcula en `ProductDocument`:** el serializador lo escribe al insertar, así no hay servicio ni *backfill* dentro de la API; el script rellena solo lo anterior.
- **Solo disponibles cuando hay `q`, orden por `_id`:** cumple el criterio de Jira y reutiliza el cursor de la vitrina; la relevancia se difiere.
- **La exclusión de agotados vive en el repositorio, no en el servicio:** cualquier consulta con `Search` devuelve solo `active`, aunque el llamador pida los agotados.
- **Título y descripción, sin categoría ni vendedor:** cubre "nombre o palabra clave" y evita mezclar búsqueda con filtros (HU-10).
- **`SearchTerms` deja solo letras, dígitos y espacios:** nada del usuario se vuelve sintaxis del patrón (OWASP API3).
- **Sin persistir `q`:** RNF-07; el historial y las búsquedas guardadas son de HU-12. El texto sí vive hasta 30 s como clave de la caché de salida, que no es historial; queda escrito en los límites de la spec.
- **`searchWords` se calcula al insertar:** si algún día se edita el título o la descripción hay que recalcularlo; hoy no hay edición (`NOTE (HU-09)`).
- **Orden por `_id`:** el índice resuelve el prefijo, pero el orden de las coincidencias se hace en memoria; aceptable con `limit ≤ 50` (`NOTE (HU-09)`).
- **`q` se acota a 5 términos:** el `$regex` se evalúa por término en dos campos y `CountAsync` repite el filtro; con 80 caracteres se llegaban a 40 términos por petición. El tope mantiene el costo acotado sin afectar el uso real (`security-reviewer`, API4).
- **Rendimiento medido contra el *fake* en memoria:** el p95 de `PerformanceTest` no cubre el `$regex` de MongoDB; el RNF-09 real se confirma con `PERF_BASE_URL` contra el despliegue.
- **M-04 reutiliza `Modules/Listings/`:** la vitrina, el repositorio y los DTO ya viven ahí; un módulo paralelo duplicaría el endpoint y las pruebas. Se corrigió la fila de `docs/modulos.md`.

**Errores a evitar**
- **`$text` no hace prefijos:** si alguien vuelve a "simplificar" con `$text`, `calc` deja de encontrar `Calculadora`; la decisión y su motivo están en `plan.md`.
- **El prefijo necesita `^` y las palabras normalizadas:** un regex sin `^` no aprovecha el índice; y si el documento guarda `Calculadoras` con mayúscula o acento, el prefijo normalizado no coincide.
- **`SearchWord.Normalize`/`Words` es compartida:** si cambia el plural o los acentos, deben cambiar juntos el API, el script `db/007` (que rellena) y la batería de `TestShared`.
- **CA1865 rompe el build:** `EndsWith("s", StringComparison.Ordinal)` debe ser `EndsWith('s')`.

## T25 — Detalle de un producto (06-Nov-2026, SCRUM-28, HU-11)

**Qué se hizo**
- `GET /api/v1/products/{id}`:
  - es público, y el vendedor `{ id, name }` solo aparece con sesión;
  - la publicación pausada solo la ve su dueño;
  - la quitada, o la de otro campus con sesión, responde `404 listings.product_not_found`.
- API `0.5.0-alpha.3`. Unit 174 · Integration 96 · Security 83 · Contract 8.

**Decisiones (con su porqué)**
- **El vendedor solo con `{ id, name }`:** su tarjeta completa sale de `GET /profiles/{id}`, y así M-03 no lee perfiles. "Quién te ve" (HU-04) se respeta en un solo lugar.

**Errores a evitar**
- **En un controller con la propiedad `userId`, una variable local `userId` rompe el build** (Sonar S1117): usa otro nombre (`viewer`).

## T24 — Bandeja de conversaciones (06-Nov-2026, SCRUM-31, HU-22)

**Qué se hizo**
- `GET /conversations`: Todos, Compras y Ventas, ordenados por actividad, con cursor y no leídos.
- `POST /conversations/{id}/read`.
- `db/006-chat-bandeja.js` (esquema 6): no leídos por lado y copia de los nombres.
- API `0.5.0-alpha.2`. Unit 174 · Integration 93 · Security 82 · Contract 8.

**Decisiones (con su porqué)**
- **Solo conversaciones con mensajes en la bandeja:** tocar "Mensaje" sin escribir no llena la bandeja del vendedor.
- **No leídos por lado (`buyerUnread` / `sellerUnread`) con `$inc`:** atómico entre las 2 instancias y validable con `$jsonSchema`.
- **Copia de los nombres (R8):** la bandeja no consulta `users` por fila.

**Errores a evitar**
- **Redis en producción (06-Nov-2026):** quedaron tres fallas encadenadas, todas resueltas.
  - En Parameter Store iba el comando `redis-cli` en vez de la cadena, y la contraseña terminó en los logs.
  - El grupo de seguridad no dejaba salir al puerto de Redis Cloud.
  - La contraseña guardada no era la vigente: `NOAUTH`, que se diagnosticó reproduciendo la conexión con la misma librería.
- **Para diagnosticar Redis sin entrar a las instancias:** un programa local con StackExchange.Redis y la cadena de Parameter Store, con la contraseña tapada en la salida. El log de la conexión dice exactamente en qué paso falla.
- **El plan gratuito de Redis Cloud no tiene TLS:** la caché y los mensajes del backplane viajan sin cifrar entre AWS y Redis Cloud (ambos en us-east-1), protegidos por la contraseña. Riesgo aceptado para el piloto.

## T23 — Iniciar una conversación con el vendedor (04-Nov-2026, SCRUM-29, HU-20)

**Qué se hizo**
- Módulo `Chat` (M-07):
  - 4 rutas de `/conversations`;
  - hub `/hubs/chat` (evento `messageCreated`);
  - `conversations` y `messages` (`db/005-chat.js`, esquema 5).
- `Platform/Realtime`: SignalR con backplane de Redis cuando existe `Cache:ConnectionString`.
- M-03 expone `IProductDirectory`, para que el chat no lea `products`.
- `check_endpoints` ahora también compara los hubs (`| WS | /ruta |`).
- API `0.5.0-alpha.1`. Unit 172 · Integration 87 · Security 80 · Contract 8.

**Decisiones (con su porqué)**
- **SignalR + Redis** (decisión del usuario): sin backplane, un mensaje que entra por una instancia no llega a quien está conectado a la otra.
- **Solo WebSockets y sin negociar:** así no hacen falta sesiones pegajosas en el ALB.
- **El token va en `?access_token=` solo en `/hubs`:** el WebSocket del celular no lleva encabezados. En REST la query se ignora.
- **Se envía por REST y se entrega por el hub:** validación, moderación y rate limit en un solo lugar.
- **Si Redis falla al entregar, el mensaje ya está guardado:** se registra un aviso y la app lo ve al recargar.

**Errores a evitar**
- **`dotnet add package` dice "no es compatible con los marcos de trabajo"** cuando una dependencia transitiva no está en el source mapping de `nuget.config`. Con SignalR+Redis faltaba `MessagePack`. No es un problema del TFM.
- **Sonar S1135 lee "Todo" en español como un TODO:** en un comentario que empieza con "Todo con sesión", escribe "Siempre con sesión".
- **`IHubContext<T>.Clients` y `IHubClients.Clients(...)` chocan en un fake** que implementa los dos: implementa los miembros de `IHubContext` de forma explícita.
- **Un `Id` en un subdocumento se guarda como `_id`** (convención de id del driver de C#). La validación de `db/005-chat.js` pedía `context.id` y rechazó la conversación, pero solo en `DatabaseTest` (CI): los repositorios en memoria no serializan. Se resolvió con `[BsonNoId]` + `[BsonElement("id")]` y con `MongoConventionsTests.Conversation_KeepsContextIdAsId_AndParticipantsAsObjectIds`, que no necesita Docker.
- **El grupo de seguridad de la API solo dejaba salir 443, 27017 y 80:** Redis Cloud (puerto 15092) respondía `UnableToConnect (0-read, 0-sent)`. Se agregó la salida `api_redis` con la variable `redis_port` (infra `0.3.5`).
- **En Parameter Store va la cadena de StackExchange.Redis, no el comando `redis-cli -u redis://default:<clave>@host:puerto`** que muestra Redis Cloud. Con el comando, la API lo toma como nombre de host y **la contraseña termina en los logs** dentro del mensaje de error. Hubo que rotarla (06-Nov-2026).
- **La prueba del hub tiene que morder:** se comprobó que falla (timeout) si cambia el nombre del evento.

## T22 — Mutación y rendimiento (03-Nov-2026, SCRUM-94, 02-09)

**Qué se hizo**
- `PerformanceTest`: p95 de `ping`, categorías y vitrina con 400 peticiones (20 a la vez); job `performance`.
- `MutationTest`: Stryker.NET 5 sobre `Passwords.cs` y `EmailAddresses.cs`, con puntaje de 87.5 %; workflow `mutation.yml`.
- Con eso, la solución completa los 8 proyectos de la plantilla `WebAPIDevSecOps`.

**Mediciones (03-Nov-2026)**
- **En proceso:** p95 de 1.1 ms (ping), 2.7 ms (categorías) y 4.6 ms (vitrina).
- **Contra AWS** (`0.4.0-alpha.1`, sin caché): p95 de 127 ms (ping), 895 ms (categorías) y 994 ms (vitrina); p50 de la vitrina, 908 ms.
- **Contra AWS con caché** (`0.4.0-alpha.4`, 2 corridas desde la laptop de desarrollo): p95 de 216–251 ms (ping), 143–178 ms (categorías) y 158–164 ms (vitrina); p50 de ~100–113 ms en las tres. La vitrina bajó de 994 a ~160 ms de p95. El ping sin caché queda más alto que antes por la variación de la red doméstica.

**Errores a evitar**
- **Stryker no funciona con xUnit v3:** no soporta MTP (#3094) y activa los mutantes dentro del proceso de VSTest, pero xUnit v3 corre las pruebas en otro proceso. El síntoma: los mutantes "sobreviven" y el puntaje sale en 0 %.
- **Un mutante en el inicializador de un campo** solo se activa si la prueba crea la instancia, no con una estática.
- **`dotnet test` con `global.json` en MTP no corre proyectos VSTest:** `MutationTest` se corre con `dotnet stryker`.
- **No escribas un SHA de una action de memoria:** cópialo de un workflow verificado o de `git ls-remote`.

## T21 — Observabilidad (03-Nov-2026, SCRUM-93, 02-06)

**Qué se hizo**
- Logs JSON fuera de Development con `TraceId`, y una línea por petición (método, ruta, código y duración) sin query, encabezados ni cuerpos, y sin `/health*`.
- `observability.tf` (infra `0.3.4`):
  - SNS con correo a `alert_email`;
  - filtro `Musubi/ApiErrors`;
  - 4 alarmas;
  - dashboard `musubi-api`.
- Trivy `0.76.1` (por digest) en `docker-build`. API `0.4.0-alpha.4`.

**Decisiones (con su porqué)**
- **`Musubi` en `Information` en producción:** antes el nivel era `Warning` y los eventos del negocio (cuenta eliminada, moderación) no llegaban a CloudWatch.
- **Sin query en el log de peticiones:** puede llevar un correo (RNF-05).

**Errores a evitar**
- **`terraform validate` en local** usa el `.terraform` ya inicializado contra S3: valida en una copia limpia con `../VERSION`, como el job del CI.
- **La suscripción de SNS por correo queda pendiente** hasta que alguien confirme el correo de AWS.

## T20 — Moderación antes de guardar (03-Nov-2026, SCRUM-92, 02-07)

**Qué se hizo**
- `Platform/Moderation`: `IContentModerator` con dos adaptadores:
  - AWS: Rekognition para las fotos y Translate + Comprehend para el texto;
  - en memoria: el marcador `[[inapropiado]]` y las fotos magenta.
- Se modera antes de guardar en tres lugares:
  - fotos de productos;
  - foto de perfil;
  - título y descripción al publicar.
- Statement `Moderar` en el rol de las instancias (infra `0.3.3`). API `0.4.0-alpha.3`. Unit 151 · Integration 70.

**Decisiones (con su porqué)**
- **Translate antes de Comprehend:** la toxicidad de Comprehend solo acepta inglés (documentación de AWS, 03-Nov-2026).
- **Copia JPEG para Rekognition:** no acepta WebP; la copia no se guarda.
- **Si AWS falla, 503 y nada se guarda:** nunca se publica sin moderar, sin la complejidad de una cola de pendientes (eso llega con HU-39).

**Errores a evitar**
- **Un marcador de imagen en bytes crudos nunca llega al moderador:** `PhotoProcessor` rechaza antes lo que no es imagen. La regla de prueba tiene que sobrevivir al procesamiento (color magenta).

## T19 — Caché en 3 niveles (03-Nov-2026, SCRUM-91, 02-02)

**Qué se hizo**
- `Platform/Caching`:
  - `AppCache` (cache-aside sobre `IDistributedCache`, Redis o memoria, con degradación);
  - caché de salida (`catalog` y `showcase`);
  - `[ConditionalGet]` (`ETag` y 304).
- `CachedCategoryRepository` y `CachedCareerRepository`. Publicar o eliminar una cuenta borra la vitrina de la caché (`EvictByTagAsync`).
- API `0.4.0-alpha.2`. Unit 139 · Integration 70 · Security 69.

**Decisiones (con su porqué)**
- **Redis opcional (Redis Cloud gratuito):** sin la cadena en Parameter Store, cada instancia usa su memoria y la API funciona igual. Con ella, la caché de datos es compartida (requisito "Redis distribuido").
- **La caché de salida solo es para anónimos y dura poco (30 s en la vitrina):** es por instancia; una publicación nueva se ve al instante en la instancia que la recibió y en 30 s en la otra.
- **`no-cache` + `ETag`:** el cliente revalida siempre, así que no hay datos viejos, y un 304 no manda cuerpo.

**Errores a evitar**
- **Una prueba de mordida con un build que falla** corre contra binarios viejos y "pasa": verifica que el build compile antes de leer el resultado.
- **`StackExchange.Redis` 3.x depende de `RESPite`:** el mapeo de `nuget.config` necesita los dos patrones (`Pipelines.Sockets.Unofficial` ya no).
- **El constructor `RedisConnectionException(tipo, mensaje)` es obsoleto (CS0618):** en las pruebas usa `RedisException`.

## T18 — Gobernanza: ruleset, MCP y spec de CI/CD (03-Nov-2026, SCRUM-90)

**Qué se hizo**
- Ruleset "Main Proteccion" en `main` de api, app y landing:
  - no borrar la rama ni hacer force push;
  - PR con 1 aprobación;
  - checks obligatorios;
  - revisión de Copilot.
- Spec `02-08-ci-cd`, que junta el CI (`01-05`) y el CD (`02-05`).
- `docs/mcp.md` con los MCP que usa el equipo (Atlassian y AWS) y su configuración en Claude Code y OpenCode.

**Decisiones (con su porqué)**
- **El administrador puede saltar la aprobación solo desde un PR** (`bypass_mode: pull_request`): hoy no hay un revisor fijo y sin esto nada se podría fusionar. El CI sigue siendo obligatorio.
- **El ruleset convive con la protección clásica**, que ya exigía los checks.

**Errores a evitar**
- **`infra-up` ya aplica `db/*.js`:** no hace falta correr los scripts a mano antes de desplegar. Son idempotentes, así que correrlos dos veces no daña.
- **Documentar un MCP con su configuración real:** el de AWS es el proxy remoto (`mcp-proxy-for-aws`), no el servidor local de AWS Labs.

## T17 — HU-04 Editar mi perfil: API (03-Nov-2026, SCRUM-22)

**Qué se hizo**
- Módulo `Modules/Profiles/` (M-02): `ProfileService`, `ProfilePhotoService`, `ProfilesController` (`/profiles/me`, `/me/visibility`, `/me/photo`, `/{id}`), `CareersController`, `ProfilesErrors` y `ProfileAccountEraser` (HU-46).
- `POST /auth/password/change` en `PasswordChangeController`: pide la actual, revoca todas las sesiones y devuelve una nueva.
- `db/004-perfiles.js`: `profiles`, `careers` (38 de la UTTT), `communities.academicTerm` (cuatrimestre, 11) y el `$jsonSchema` de `communities` con ese campo.
- `IMemberDirectory.FindAccountAsync` (cuenta y campus para M-02). `PhotoProcessor.TryProcessSquare` (512 px), `IMediaStorage.DeleteAsync` y `PhotoUploadService.ReadAsync` (compartido entre las dos fotos).
- `s3:ListBucket` también en `media/avatars/*` (infra `0.3.2`).
- Build 0/0 · Unit 134 · Integration 66 · Security 68 · Contract 7 · endpoints OK (28) · critic PASS · Semgrep 0.

**Decisiones (con su porqué)**
- **Perfil con upsert atómico (`FindOneAndUpdate` + `$setOnInsert`):** el primer cambio crea el documento sin leerlo antes, y dos escrituras a la vez no se pisan los valores por defecto.
- **Un campo vacío se quita con `$unset`:** el `$jsonSchema` no acepta `null` (y la convención `IgnoreIfNull` ya los omite al insertar).
- **El perfil público responde 404 igual para inexistente, inactiva y de otro campus** (OWASP API3): no deja saber si la cuenta existe.
- **Controlador aparte para cambiar la contraseña:** Sonar S6960 marcó `PasswordController` (anónimo) con dos responsabilidades.
- **Sin cuenta activa, las rutas del perfil responden 403 `auth.forbidden`**, igual que publicar (HU-06).

**Errores a evitar**
- **B2 del critic también revisa el nombre del record:** `ChangePasswordRequest(` necesita su waiver en esa línea.
- **`FindOneAndUpdateAsync<TProjection>` no acepta lambda como filtro:** usa `Builders<T>.Filter.Eq`.
- **La fila de `docs/versionado.md` es sección compartida:** agrégala también en `musubi-app` (va en el PR de HU-04 de la app).

## T11 — HU-04 Editar mi perfil: spec (27-Oct-2026, SCRUM-22)

**Qué se hizo**
- Se concilió Jira con la maqueta: `Perfil`, `Ajustes` ("Datos personales", "Correo institucional", "Cambiar contraseña") y `Privacidad` ("Quién te ve"). Los "campos visibles [POR DEFINIR]" de Jira quedaron resueltos por la maqueta.
- Spec con 8 rutas nuevas (7 de M-02 y `POST /auth/password/change`), las colecciones `profiles` y `careers` y `communities.academicTerm`. El diseño de `profiles` y `careers` ya está en `docs/modelo-datos.md` (📐).

**Decisiones (con su porqué)**
- **Cuatrimestre configurable por campus** (usuario): la UTTT cursa 11 (TSU del 1 al 6, ingeniería y licenciatura del 7 al 11) y otra universidad podría usar semestres (RNF-21).
- **Carrera de una lista del campus** (usuario): datos uniformes, y el periodo se valida contra la carrera. Semilla propuesta desde el sitio oficial de la UTTT (38 carreras), **a validar con el equipo**.
- **Alcance completo de la maqueta más la foto de perfil** (usuario). La foto reutiliza `PhotoProcessor` (cuadrada, 512 px, sin metadatos) y borra la anterior de S3.
- **La visibilidad se aplica en la API**, no solo en la app (OWASP API3). El perfil público solo existe dentro del campus y responde 404 fuera de él.
- **`auth.wrong_password` responde 400 y no 401**, porque la app trata un 401 como sesión vencida.

## T16 — HU-46 Eliminar mi cuenta y mis datos (30-Oct-2026, SCRUM-32)

**Qué se hizo**
- `POST /api/v1/account/delete` (`AccountController`, con sesión y rate limit `auth`) pide la contraseña actual. Hace borrado real de cuenta, sesiones, códigos, publicaciones y fotos (S3 por prefijo).
- `IAccountDataEraser`: cada módulo borra sus propios datos. Listings es el primero (`ListingAccountEraser`).
- Repositorios: `DeleteAllForUserAsync` en códigos y sesiones, y `DeleteBySellerAsync`. `IMediaStorage.DeletePrefixAsync` en S3 y en memoria.
- `iam.tf`: `s3:ListBucket` en `media/products/*` (infra `0.3.1`). API `0.3.0-alpha.4`.
- Pruebas: Unit 112 · Integration 57 · Security 52 · Contract 6 · endpoints 20.

**Decisiones (con su porqué)**
- **Pedir la contraseña** antes de borrar: con una sesión abierta en un teléfono ajeno no se puede borrar la cuenta.
- **El usuario se borra al final:** sin transacciones, así un fallo a medias permite reintentar.
- **`POST .../delete` y no `DELETE` con cuerpo:** algunos proxies descartan el cuerpo de un `DELETE`.
- **`auth.wrong_password` responde 400:** la app trata un 401 como sesión vencida.

**Errores a evitar**
- **La palabra "todo" en un comentario de C#** ("Borra todo lo del usuario") rompió el build por S1135. Usa "los datos", "cada", etc.
- **Sin `s3:ListBucket`**, S3 responde 403 (no 404) a una llave que no existe, y no se puede listar un prefijo.

## T15 — Cuenta de revisión de Google Play (29-Oct-2026, SCRUM-88)

**Qué se hizo**
- `db/003-dominio-revision.js` agrega `musubiapp.com.mx` a los dominios de la UTTT (esquema 3; `MongoSchema.ExpectedVersion = 3`). La semilla en memoria hace lo mismo.
- API `0.3.0-alpha.3`. Prueba nueva: `Campus_TeamReviewDomain_BelongsToUttt`; `DatabaseTest` revisa el dominio y la idempotencia.

**Decisiones (con su porqué)**
- **Un dominio del equipo dentro de la UTTT, no un campus aparte:** el revisor de Google ve el catálogo real del campus, y nadie de fuera puede registrarse. Solo existen las direcciones que se crean en Cloudflare Email Routing (catch-all apagado), y el registro exige el código enviado a esa dirección.
- **HU-04 usará `db/004-perfiles.js`**, no `003` como decía su spec: hay que ajustarla al detallarla.

## T14 — Specs en borrador de todo el backlog (29-Oct-2026, SCRUM-86)

**Qué se hizo**
- 47 HU pendientes con spec ligera (🚧 Borrador) en los dos repos: 94 carpetas en `specs/mNN-*/HU-NN-*/` con `spec.md`, `plan.md` y `task.md`.
- Las generó un script con datos de cuatro fuentes:
  - Jira: historia, criterios, RF/RNF y decisiones pendientes;
  - la maqueta: las pantallas de cada HU;
  - `docs/modelo-datos.md`: las colecciones;
  - `docs/modulos.md`: la carpeta de cada módulo.
- Specs técnicas en borrador:
  - musubi-api: 02-02 caché y `outbox`, 02-04 rendimiento HTTP, 02-06 observabilidad y 02-07 moderación;
  - musubi-app: 02-02 Google Play.
- `docs/trazabilidad.md` en "Spec en borrador", y la regla de `specs/README.md` (sección compartida) actualizada.

**Decisiones (con su porqué)**
- **Spec ligera para todo y detalle al iniciar cada HU:** el docente pide las specs desde el inicio. Detallar diseño y contratos semanas antes los desactualiza; así lo hacen Spec Kit y Kiro (constitución al inicio, specs por funcionalidad).
- **HU-43 sin spec** (fuera de alcance, D-01). **HU-42** con spec, marcada como posterior al MVP. **HU-48** en la API se cubre con HU-06 (vitrina pública).

**Errores a evitar**
- Una spec ligera **no** es una spec aprobada. Antes de programar una HU, completa su `plan.md` y resuelve las decisiones pendientes de su sección Límites.

## T12 — Dominio propio musubiapp.com.mx (29-Oct-2026, SCRUM-84/85)

**Qué se hizo**
- **DNS en Cloudflare:**
  - `contacto@` con Email Routing;
  - SES verificado con DKIM, más MAIL FROM `mail.` y DMARC;
  - sitio `musubi-landing` en la raíz.
- **Remitente:** `/musubi/prod/Email/From` = `Musubi <no-reply@musubiapp.com.mx>`. Se aplica al reemplazar las instancias.
- **API:** certificado ACM para `api.musubiapp.com.mx` (pedido con la CLI). Terraform usa la variable `api_domain`, alias de CloudFront, `sni-only`, `TLSv1.2_2021` y la salida `cloudfront_domain`. Infra `0.3.0`.

**Decisiones (con su porqué)**
- **Certificado fuera de Terraform y buscado por nombre:** su validación es un CNAME en Cloudflare, que no maneja Terraform. Si el apply lo creara, se quedaría esperando la validación.
- **CNAME de `api` en modo DNS only:** CloudFront ya da TLS, caché e IPv6. Con el proxy de Cloudflare habría doble CDN y el SNI no coincidiría con el certificado.

**Errores a evitar**
- **Git Bash convierte `/musubi/prod/...` en una ruta de Windows** y la CLI de AWS responde `ParameterNotFound`. Usa `MSYS_NO_PATHCONV=1`.
- **`data.aws_acm_certificate` solo busca llaves RSA_2048 por defecto** (igual que `aws acm list-certificates`). Un certificado EC (`EC_prime256v1`) necesita `key_types`; sin eso falla con "empty result" (infra-up del 29-Oct).
- **La sesión de `aws login` dura poco:** un vigilante largo falla con `ExpiredTokenException`, que no es un error del recurso.
## T13 — Guía del equipo y agentes de OpenCode (27-Oct-2026, SCRUM-83)

**Qué se hizo**
- `docs/guia-equipo.md`, idéntica en los dos repos (sección compartida): accesos, herramientas, cómo lee el proyecto cada herramienta, el flujo de una HU en 9 pasos, prompts para copiar, recursos que chocan entre personas, cómo probar en el celular y reglas del CI.
- `.opencode/agents/` con `security-reviewer`, `traceability-clerk` y `slice-scaffolder`. Remiten a `.claude/agents/`, que sigue siendo la fuente única.

**Decisiones (con su porqué)**
- **Mismas reglas para Claude Code y OpenCode:** OpenCode lee `AGENTS.md` y las skills de `.claude/skills/` (verificado en su documentación), pero no `.claude/agents/`. Por eso los agentes tienen una versión corta en `.opencode/agents/`.
- **La solo lectura en OpenCode se impone por `permission`:** `edit: deny` y bash solo para `git diff`, `git log` y `git show`, con la última regla que coincide ganando. Los hooks de Claude Code no corren en OpenCode.
- **Una sesión de IA por repo:** abierta en la carpeta `musubi/`, ninguna de las dos herramientas carga el `AGENTS.md` de los repos.

**Errores a evitar**
- **Números compartidos entre ramas:** `db/NNN`, `ExpectedVersion`, versiones, "T" de esta memoria y totales de `endpoints.md`. Quien fusiona segundo rebasa y renumera (guía §5).

## T10 — HU-06 Publicar un producto y vitrina pública (27-Oct-2026, SCRUM-23)

**Qué se hizo**
- `Platform/Media/`: `IMediaStorage` (S3 o en memoria), `PhotoProcessor` (SkiaSharp: endereza por EXIF, ≤ 1600 px, WebP sin metadatos) y `MediaController` (solo Development).
- `Modules/Listings/`: publicar, vitrina pública con cursor y filtros, "mis productos", rango de precios y categorías. M-01 expone `IMemberDirectory`.
- `db/002-publicaciones.js`; `user-data` recibe `Media__*`; filas en `endpoints`, `errores`, `modelo-datos`, `versionado` y `01-04`.

**Decisiones (con su porqué)**
- **Excepción al stack (aprobada por el usuario):** SkiaSharp (MIT) y AWSSDK.S3. Sin reprocesar, la ubicación GPS de la cámara llegaba a S3 y las fotos verticales salían giradas. Ver el `plan.md` de HU-06.
- **Rutas de imagen relativas (`/media/...`):** CloudFront sirve la API y las imágenes en el mismo dominio, y en Development la misma ruta la sirve la API.
- **"Populares" queda fuera** hasta HU-11 (vistas), por decisión del usuario.

**Errores a evitar**
- `scripts/check_endpoints.ps1` lee **una ruta por archivo**: un segundo controller en el mismo archivo no se detecta. Un controller por archivo.
- `AuthApiFactory` emite el JWT con su reloj manual (21-oct), pero JwtBearer lo valida con la hora real: alinear el reloj antes de llamar endpoints con sesión.
- En `.tftpl`, editar con Python una línea que termina en `\` puede comerse el salto: revisar el `git diff`.
- `SKCodec.Create(SKData)` no tiene la sobrecarga con `out SKCodecResult`: usar la de `Stream`. `SKCanvas.DrawBitmap` está obsoleto (`DrawImage` con `SKSamplingOptions`).

## T9 — 02-05 Despliegue en AWS (25-Oct-2026, SCRUM-80)

**Qué se hizo**
- `scripts/bootstrap-state.ps1` creó `musubi-tfstate-<cuenta>` (versionado, cifrado, sin acceso público). `infra/bootstrap` (ECR, OIDC, rol, presupuesto) quedó con `plan` de 6 recursos.
- `infra/stack`: VPC con 2 subredes públicas, ALB (solo CloudFront + cabecera secreta), ASG de 2 `t3.micro`, CloudFront (HTTPS, SecurityHeadersPolicy), S3 de imágenes con OAC, IAM mínimo de las instancias y CloudWatch.
- Parámetros SSM `/musubi/prod/{ConnectionStrings/Mongo, Jwt/SigningKey, Email/From}`; identidad de SES pendiente de confirmar; entorno `produccion` de GitHub (solo `main`) con `AWS_DEPLOY_ROLE_ARN`.
- `CloudFrontForwardedHeaders` + `SecurityTest/Hosting/CloudFrontHeadersTests.cs` (11). Job `terraform` en el CI.

**Decisiones (con su porqué)**
- **Sin Identity Center ni Organizations:** una cuenta del plan gratuito que entra a Organizations pasa al plan de pago y pierde los créditos. Se usa un usuario IAM con MFA + `aws login`.
- **IP real de `CloudFront-Viewer-Address`:** el `X-Forwarded-For` del ALB termina en la IP de CloudFront, y sin esto el rate limit de `/auth` sería uno solo para todos.
- **Desplegado el 26-Oct-2026:** `api-v0.2.0-alpha.1` + `infra-v0.1.0`; 2 instancias sanas, HTTPS por CloudFront, el ALB directo y el HTTP plano rechazados.
- **Sin NAT, sin Secrets Manager, sin ECS:** costo (ver `infra/README.md`, ~50 USD/mes encendido).
- **Presupuesto sin créditos:** si los contara, el gasto neto sería 0 y las alertas nunca sonarían.

**Self-tests**
- Si el middleware no se registra → `BehindCloudFront_EachViewerHasItsOwnRateLimit` en rojo.

**Errores a evitar**
- En una plantilla de Terraform (`.tftpl`), un `${` en un comentario también se interpreta: escribir `$${` o no usarlo.
- El clasificador de permisos bloquea `terraform apply` desde el agente: lo autoriza el administrador.
- **Primer `infra-up` (26-Oct-2026):** el primer Auto Scaling Group de la cuenta crea `AWSServiceRoleForAutoScaling` y el primer intento falla por propagación (se resuelve solo; volver a correr `infra-up`).
- **`t3` en créditos `standard` arranca sin créditos** (10 % de CPU): el arranque pasaba de 5 minutos y el ALB reemplazaba la instancia. Se usa `unlimited` y 10 minutos de margen.
- **GitHub usa sujetos OIDC inmutables** en este repo (`repo:Owner@id/repo@id:...`). Un rol que confía en `repo:Owner/repo:...` falla con "Not authorized to perform sts:AssumeRoleWithWebIdentity". Ver el formato real con `gh api repos/<owner>/<repo>/actions/oidc/customization/sub`.
- Probar las actions por SHA del commit, no del tag anotado (`git ls-remote ... refs/tags/vX^{}`).

## T8 — HU-02 + HU-56 Sesión y recuperación de contraseña (21-Oct-2026, SCRUM-20/78)

**Qué se hizo**
- `SessionController` (`login`, `refresh`, `logout`) y `PasswordController` (`password/forgot`, `password/reset`); 5 filas nuevas en `docs/endpoints.md` (12 en total).
- `LoginService` (señuelo para correo inexistente, estado revelado solo con la contraseña correcta, rehash), `PasswordRecoveryService` (respuesta genérica, cambio de hash y revocación de todas las sesiones).
- Extraídos de `RegistrationService`: `SessionIssuer` (una sola forma de `SessionResponse`) y `VerificationCodeService` (códigos por propósito para HU-03 y HU-56).
- `IUserRepository.UpdatePasswordHashAsync` en Mongo e InMemory + su prueba en la batería de `TestShared/`.
- Pruebas: `IntegrationTest/Auth/SessionFlowTests.cs` (9), `PasswordRecoveryFlowTests.cs` (9), helper `AuthFlow.cs`, y el contrato de login/refresh contra `session.json`.

**Decisiones (con su porqué)**
- **Tres controllers en `/auth`**: S6960 (SonarAnalyzer) marca un controller con varias responsabilidades y el build es TWAE. `this.Respond(result, status)` pasó a `ApiProblemExtensions` para no duplicarlo.
- **Logout idempotente** (204 aunque el token no exista): la app limpia su sesión local de todos modos.
- **El pedido de recuperación nunca reporta la espera de 60 s**, a diferencia del reenvío de HU-03: allí la cuenta pendiente ya se conoce, aquí no se debe revelar.

**Self-tests**
- Sin `RevokeAllForUserAsync` en el reset → `ForgotThenReset_ChangesThePasswordAndClosesEverySession` en rojo.
- Revelar `email_not_verified` con la contraseña equivocada → `Login_PendingUser_IsAskedToVerifyOnlyWithTheRightPassword` en rojo.

**Errores a evitar**
- **PR apilados: fusionar en orden (padre → hijo).** El #7 (que ya incluía el #6) se fusionó antes que el #6; al fusionar después el #6, la resolución tomó su versión y `main` perdió parte de HU-02 en 12 archivos (build rojo). Se restauró con `fix/SCRUM-20-restaurar-hu02` (árbol idéntico al merge del #7). Después de fusionar un padre con squash, rebasar el hijo sobre `main` para que solo lleve su commit.
- Una mutación que deja un parámetro del constructor primario sin usar no compila: las pruebas corren sobre binarios viejos y salen verdes. Mutar con `_ = campo;` y revisar que el build diga 0 errores.
- `ArgumentNullException.ThrowIfNull` sobre un `struct` (`Result<T>`) es CA2264.
- La salida de `dotnet test` está en español: el resumen dice `error: N`, no `failed`.

## T7 — HU-01/03/47 Registro, verificación y consentimientos (21-Oct-2026, SCRUM-19/21/33)

**Qué se hizo**
- `AuthController` (`/api/v1/auth`): `campus`, `register`, `verification/resend` y `verification/confirm`; anónimo y con rate limit `auth`. Filas en `docs/endpoints.md` (7 rutas en total).
- `RegistrationService` con `Result<T>`: campus por dominio (solo comunidades activas), registro `pendingVerification` con consentimientos versionados (`Legal:*`), código de 6 dígitos (10 min, 5 intentos, reenvío cada 60 s) y confirmación que abre la sesión (access + refresh de 02-03).
- `EmailAddresses` (normalización), `VerificationCodes` (CSPRNG, hash ligado al usuario, comparación en tiempo constante) y `AuthEmails` (nombre codificado en el HTML).
- Pruebas: `IntegrationTest/Auth/` con `AuthApiFactory` (reloj y correo controlados vía `TestShared/`), `UnitTest/Auth/RegistrationTests.cs` y `ContractTest/AuthContractTests.cs` (fixtures `campus`, `register` y `session`).

**Decisiones (con su porqué)**
- **Sin sesión hasta verificar** (RN-01): el registro no da tokens; la confirmación sí.
- **El controller resuelve el servicio de `RequestServices`**: los servicios del módulo son `internal` y MVC exige un controller público (CS0051 con un constructor público).
- **Consentimientos `bool?` + `[Required]`** (S6964): omitirlos es un 400 de validación y no se toman como "falso".
- **El 409 de registro revela si un correo existe** (la UX de la maqueta lo pide); el reenvío y la confirmación no revelan nada.

**Self-tests**
- Sin el bloqueo de 5 intentos → `Confirm_WrongCode_FailsAndLocksAfterFiveAttempts` en rojo.

**Errores a evitar**
- En los tests de integración que pegan muchas veces a `/auth`, subir `RateLimiting:Auth:PermitLimit` en la factory: el limitador comparte la "IP" de TestServer entre pruebas.
- Una línea de documentación con la palabra de la contraseña en `*Dtos.cs` también necesita `critic-allow: input-credential (HU-NN)` (B2a revisa cada línea).

## T6 — 02-03 Contrato de errores, autenticación y correo (21-Oct-2026, SCRUM-77)

**Qué se hizo**
- Errores: `problem+json` con `code`, título en español y `traceId` en cualquier error (validación, 401/403/404/405/429 vía `UseStatusCodePages`, 500). Catálogo canónico `docs/errores.md`; los controllers usan `this.Problem(AuthErrors.X)`.
- Sesión: JWT HS256 de 15 min (`AccessTokenIssuer`, claims mínimos) + refresh opaco y rotativo de 60 días (`RefreshTokenService`: solo hash, consumo atómico, la reutilización revoca la familia, el logout revoca la familia del dispositivo).
- Política de respaldo (sesión obligatoria por defecto), rate limit `auth` por IP (10/min, 429 + `Retry-After`) y salud/ping/OpenAPI anónimos.
- Contraseñas: `PasswordHasher<T>` de Identity (PBKDF2-SHA512, 210 000 iteraciones) y la política de la maqueta.
- Correo: `IEmailSender` → `LogEmailSender` (solo Development) o `SesEmailSender` (AWSSDK.SimpleEmailV2 4.0.106.4) sobre `ISesClient`; `EmailOptionsValidator` impide Log fuera de Development.

**Decisiones (con su porqué)**
- **Refresh opaco en la base** (no JWT): permite logout real y detectar robo por reutilización.
- **HS256 compartida en SSM** (no RS256): solo esta API emite y valida.
- **Errores como valores** (`ApiError`), no excepciones: sin `try/catch` en los controllers y con el mismo comportamiento en todos los ambientes.
- **Ruta inexistente sin sesión → 401** (la política de respaldo cubre las peticiones sin endpoint): no se pueden enumerar rutas. Se ajustaron las pruebas de 01-02 y 01-03 que esperaban 404.
- **Fail-fast fuera de Development** sin `Jwt:SigningKey` y sin SES/`Email:From`. Las factories de SecurityTest pasan una llave de 64 bytes generada al vuelo.

**Self-tests**
- Aceptar HS512 + política de respaldo permisiva → 4 pruebas en rojo (JWT, respaldo, OpenAPI anónimo e integración).

**Errores a evitar**
- La palabra "todo" en comentarios ("todo endpoint", "todo error") vuelve a romper el build (S1135): escribir "cada".
- `AddRateLimiter` vive en `Microsoft.AspNetCore.Builder` (no en `Microsoft.Extensions.DependencyInjection`).
- HS512 con `JsonWebTokenHandler` exige una llave de 64 bytes o más: la llave de pruebas es de 64 para poder probar la confusión de algoritmos.
- Un restore en paralelo puede fallar con "No se puede crear un archivo que ya existe": reintentar con `--disable-parallel`.
- Una mutación con un `using` sobrante no compila (IDE0005) y deja binarios viejos: mutar sin cambiar las dependencias.

## T5 — 02-01 Modelo de datos MongoDB e identidad (21-Oct-2026, SCRUM-76)

**Qué se hizo**
- `docs/modelo-datos.md`: catálogo canónico con las reglas R1–R14 (escalabilidad) y las 27 colecciones de las 55 HU (📐 Diseño, salvo las de identidad).
- `db/001-identidad.js` (script de entrega, idempotente): `communities` (+ UTTT), `users`, `verificationCodes`, `refreshTokens` y `_meta`, con `$jsonSchema` estricto, índices con nombre (únicos y TTL) y `_meta.version = 1`.
- `Platform/Data/{MongoConventions,MongoSchema}.cs`; `MongoHealthCheck` lee `_meta`: la readiness da Unhealthy si la base tiene una versión menor que `ExpectedVersion`.
- `Modules/Auth/Data/`: documentos, contratos, `Mongo*` e `InMemory*`; `AddAuthModule()` elige la implementación de forma lazy.
- `TestShared/Auth/AuthRepositoryContract.cs` (13 pruebas) enlazado en UnitTest (fakes) y DatabaseTest (Mongo real). DatabaseTest ya no es un esqueleto: Testcontainers.MongoDb 4.16.0 y job `database` en el CI.

**Decisiones (con su porqué)**
- **El script es la fuente del esquema** (no la API al arrancar): dos instancias no compiten por crear índices, y el script es el entregable. La API solo verifica la versión.
- **Atomicidad de un documento** (`findOneAndUpdate` con condición) en lugar de transacciones: igual en Atlas M0 y con N instancias. Ejemplo: el refresh token se consume una sola vez aunque lleguen 10 peticiones a la vez (probado).
- **`users` separado de `profiles`**: las credenciales nunca viajan con los datos públicos (R10).
- **Estados como `string` en camelCase**, no enums de C#: coinciden con el `enum` del validador.
- **Contraseña según la maqueta:** al menos 8 caracteres, una letra y un número (la maqueta manda sobre D-02).

**Self-tests**
- Fake que permite consumir dos veces un refresh token → 2 pruebas de contrato en rojo.

**Errores a evitar**
- CA2007 aplica a los métodos auxiliares de las pruebas (los `[Fact]` están exentos): `ConfigureAwait(false)` fuera de los métodos de prueba.
- DatabaseTest no corre sin Docker: en esta máquina solo compila; la evidencia es el job `database` del CI.
- `mongod` 8.0 se niega a arrancar en kernels Linux >= 6.19 ("known incompatibility", SERVER-121912) y algunos runners `ubuntu-latest` ya los traen: el job `database` corre en `ubuntu-22.04` e imprime `uname -r`. No afecta a Atlas ni a WSL2. Revisarlo al subir la imagen de `mongo`.
- En `mongosh`, `db._meta` da `undefined` (los nombres con `_` no se resuelven como propiedad): usar `db.getCollection('_meta')`. Lo detectó el job `database` en su primera corrida; el script de entrega habría fallado en Atlas.

## T4 — Backlog alineado con la maqueta (21-Oct-2026, SCRUM-75)

- **D-01 decidido por el usuario:** el pago es solo el de la maqueta (transferencia a la CLABE del vendedor o efectivo con código de entrega, sin comisión). HU-43 (Mercado Pago, SCRUM-55) queda fuera de alcance (etiqueta `fuera-de-alcance` + comentario en Jira) y la reemplaza **HU-55** (SCRUM-72). Constitución §1 (compartida) y §6/§11, 01-04 (`Payments:*` en lugar de `MercadoPago:*`), AGENTS, skills y Semgrep actualizados.
- **HU nuevas de la maqueta** (Historias en SCRUM con épica y etiqueta `maqueta`, sin sprint): HU-48 visitante (SCRUM-73), HU-49 populares (SCRUM-69), HU-50 inventario (SCRUM-67), HU-51 comparar/Avísame (SCRUM-70), HU-52 apartados QR (SCRUM-74), HU-53 favoritos (SCRUM-68), HU-54 modo noche (SCRUM-71), HU-55 pago (SCRUM-72). `docs/trazabilidad.md` pasa a 55 HU.
- Dependabot: `mongo` solo recibe parches de la línea 8.0 (`ignore` de minor/major); se cerró el PR que subía a 8.3.11.
- **Error a evitar:** el MCP de Atlassian da timeout en `createJiraIssue`/`editJiraIssue` aunque la operación sí se aplique. Antes de reintentar, buscar con JQL (`key > SCRUM-NN`): un reintento ciego duplicó HU-48 (la copia SCRUM-74 se reutilizó para HU-52).

## T3 — Lineamientos de la materia y stack NoSQL (20-Oct-2026, SCRUM-66)

**Qué se hizo**
- Lineamientos del docente (PDF del 20-Oct-2026) transcritos en `docs/bitacora/lineamientos-materia.md` con el checklist L-01…L-19 (canónico; el front lo enlaza). La constitución §1 (compartida) dice que **mandan sobre la constitución** y fija la evaluación el **02-Dic-2026**.
- PostgreSQL/EF Core → **MongoDB** (lineamiento 14): `MongoDB.Driver` 3.12.0 (Apache-2.0, publicado el 17-Sep-2026); `Platform/Data/MongoServiceCollectionExtensions.cs` (cliente y base lazy, fail-fast sin `ConnectionStrings:Mongo`), `MongoHealthCheck` (`ping`, timeout de 5 s) e `InMemoryDatabaseHealthCheck` (con `UseInMemoryDatabase=true`). Se borró `AppDbContext`.
- Claves: `ConnectionStrings:Default` → `ConnectionStrings:Mongo` + `Mongo:DatabaseName` (default `musubi`). Compose local con `mongo:8.0.32` (`MONGO_PASSWORD` en `deploy/.env`).
- Source Mapping: se quitaron `Npgsql*`; entran `MongoDB.*`, `DnsClient`, `SharpCompress`, `Snappier`, `ZstdSharp.Port` (transitivos del driver, leídos de su nuspec). Verificado con caché vacía: 64 paquetes, sin NU1100 (20-Oct-2026).
- critic B2b, Semgrep (`musubi-connection-string-with-password`) y `AppSettingsTests` detectan URIs `mongodb(+srv)://usuario:clave@`.
- Matriz de versionamiento `docs/versionado.md` (sección compartida, idéntica en el front) + `<Version>0.1.0</Version>` en `Musubi.Api.csproj` + `UnitTest/Common/VersioningTests.cs` (la versión debe coincidir con la última fila de la matriz).
- Dependabot: ecosistema `docker-compose` para `/deploy`.
- Roadmap de la fase 02 rehecho en `specs/README.md`: 02-01 documentos e índices + `mongosh` · 02-02 caché y paginación · 02-03 errores + JWT/refresh/logout · 02-04 rate limit y compresión · 02-05 AWS con Terraform · 02-06 CloudWatch · 02-07 moderación (Rekognition/Comprehend).

**Decisiones (con su porqué)**
- **MongoDB Atlas M0** (decisión del usuario): NoSQL obligatorio, gratis y externo permitido. DynamoDB descartado (filtros combinados de HU-10 y script portable para la entrega).
- **`UseInMemoryDatabase` se conserva** como "sin MongoDB": `dotnet run` y las suites WAF no necesitan una base. Los repositorios de 02-01 tendrán un fake en memoria.
- **Health check por factory** (`HealthCheckRegistration` con `IServiceProvider`): la bandera se lee lazy (00-04, regla 3).
- **`mongo:8.0.32` y no 8.0.34**: misma línea que Atlas y respeta el `cooldown` de 7 días (8.0.34 salió el 17-Oct).
- **Matriz como sección compartida + prueba** en cada repo: el lineamiento 11 pide aplicarla en cada despliegue; la prueba evita que la matriz y el código se desalineen. La automatización del tag queda en `NOTE (02-05)` / `NOTE (musubi-app:02-02)`.

**Self-tests**
- `MongoHealthCheck` mutado para devolver Healthy al fallar → `Readiness_WithUnreachableMongo_IsUnhealthy` en rojo.
- URI `mongodb+srv://u:p@…` en `appsettings.Production.json` → `TrackedSettings_ContainNoSecrets` en rojo; en un appsettings de prueba → critic FAIL (B2) y Semgrep FAIL.
- Matriz con otra versión → `ApiVersion_MatchesLatestRowOfCompatibilityMatrix` en rojo.

**Errores a evitar**
- Una mutación que no compila (los analizadores la rechazan) deja binarios viejos: `--no-build` da un falso verde. Mutar de forma que compile.
- `HealthCheckResult.Healthy(desc, ex)` no existe (el 2.º parámetro es `data`).
- El restore con caché vacía puede fallar por timeouts de nuget.org (no es NU1100): reintentar antes de concluir.
- Heredocs muy largos en el Bash de la herramienta pueden fallar al parsear: escribir el script a un archivo del scratchpad.

## T2 — Publicación en GitHub (15-Oct-2026)

- Repos **públicos**: `https://github.com/Israel-Perez/musubi-api` (y `musubi-app`). En GitHub gratuito, público habilita la protección de rama, minutos de Actions ilimitados y secret scanning sin costo. La plantilla ya parte de que no hay nada secreto en el repo.
- **Sin nombres de personas en el repo público** (decisión del usuario): se quitaron del §1 de la constitución (sección compartida, los dos repos), del crédito del README y del prompt archivado. Los nombres viven en Jira y Confluence.
- **Commit inicial sin clave Jira** (decisión del usuario: la tarea de plantilla no tiene clave). Desde la primera HU, cada commit y PR empieza con `SCRUM-NN`.
- Protección de `main`: ver el README (sección "Protección de rama recomendada") y la configuración aplicada con `gh`.

## T1 — Plantilla inicial SDD + DevSecOps (15-Oct-2026)

**Qué se hizo**
- Solución `Musubi.slnx` con `Musubi.Api` (monolito modular: `Platform/` + `Modules/`) y 5 suites (`DatabaseTest` es un esqueleto).
- Pipeline 01-02 en extensiones (`AddMusubiPlatform`/`UseMusubiPipeline`); `/health`, `/health/ready` (check de `AppDbContext`) y `GET /api/v1/ping`.
- Gobernanza: constitución (§1–§5 compartidas con `musubi-app`), plantilla, specs 00-01…00-04 y 01-01…01-05 (spec/plan/task), `AGENTS.md`, `CLAUDE.md`, `docs/{endpoints,trazabilidad,modulos}.md`.
- Guardarraíles: `critic-guardrails.ps1` (B1–B4), `check_endpoints.ps1` (bidireccional), `check_shared_sections.ps1`, Semgrep (5 reglas), CI de 9 jobs, Dependabot (nuget/actions/docker con `cooldown`), plantilla de PR.
- Claude Code: 3 agentes (los 2 revisores con solo lectura forzada por `disallowedTools` + hook `PreToolUse` en PowerShell) y 10 skills portadas de `.opencode/`.
- Prompt original archivado en `docs/bitacora/prompt-plantilla-inicial.md`.

**Versiones medidas (15-Oct-2026)**
- .NET SDK 10.0.401 (local) · imágenes `sdk:10.0.402` / `aspnet:10.0.13` · `postgres:18.6-alpine` (la 19 sigue en beta).
- Asp.Versioning.Mvc/ApiExplorer 10.2.1 · Asp.Versioning.OpenApi 10.2.3 · Microsoft.AspNetCore.OpenApi, EF Core InMemory y HealthChecks.EFCore 10.0.13 · Npgsql.EFCore.PostgreSQL 10.0.3 · Scalar.AspNetCore 2.18.0 · SonarAnalyzer.CSharp 10.35.0.4138.
- Pruebas: xunit.v3 4.0.2 · coverlet.MTP 10.1.0 · Microsoft.AspNetCore.Mvc.Testing 10.0.13.
- CI: checkout v7.0.1, setup-dotnet v6.0.0, setup-python v7.0.0 (SHAs en `ci-pr.yml`) · Semgrep 1.181.0 · actionlint 1.7.12 (local) · pwsh 7.6.7 · Python 3.13.15.

**Decisiones (con su porqué)**
- **xUnit v3 + Microsoft.Testing.Platform** en lugar de xUnit 2.9 + VSTest: es la línea mantenida y el modo nativo de .NET 10; el comando cambia a `dotnet test --project`.
- **Asp.Versioning.OpenApi** en lugar de suprimir AV0029/AV0030 (la referencia los suprimía): un documento OpenAPI por versión sin deuda.
- **Source Mapping explícito** (9 patrones derivados de `project.assets.json`) en lugar de `*`; verificado con caché vacía (70 paquetes, sin NU1100).
- **CORS vacío por defecto**: la app Android no usa CORS; solo aplica a un panel web futuro.
- **`UseInMemoryDatabase=false` en la base y `true` en Development**: fail-fast si falta la BD en Production y `dotnet run` funciona sin PostgreSQL.
- **Waiver `critic-allow: input-credential (HU-NN)`** por línea: HU-01/HU-02 necesitan la contraseña en el request; la excepción queda visible (WARN) y trazable.
- **Guard de endpoints bidireccional**: la referencia solo detectaba rutas sin fila; ahora también filas huérfanas.
- **Job `docker-build`** en el CI: no hay Docker en la máquina de creación (pedido del usuario).
- **Solo lectura forzada** en `security-reviewer`/`traceability-clerk` (pedido del usuario): campos verificados en la documentación oficial de Claude Code (`disallowedTools`, `hooks` en el frontmatter, `shell: powershell`).
- No se instalaron Stryker, NBomber, Pact, RESTler ni chaos (quedan en `00-03` con su fase dueña). SonarCloud queda para `02-06` (requiere la organización y `SONAR_TOKEN`).

**Self-tests (los guardarraíles muerden)**
- Sin `UseHsts` → Security 10/11 (falla `Production_HttpsResponse_CarriesHstsHeader`).
- `AllowAnyOrigin()` → el build falla con S5122 (SonarAnalyzer).
- critic con un scratch (TODO + controller sin auth + `catch` + DTO con password + appsettings con password) → `FAIL (5 bloqueantes, 2 avisos)`, exit 1.
- endpoints con una ruta sin fila → FAIL; con una fila renombrada → FAIL (1 faltante + 1 huérfana).
- Semgrep con un scratch → 5/5 reglas; sobre el repo → 0. `--config=auto` detectó la falta de `cooldown` en Dependabot (corregido).
- Hook `readonly-git-guard.ps1` con 12 payloads → `git diff/log/show` permitidos; `push`, `rm`, `;`, `>`, `--output`, `-c`, `$(…)` y `Remove-Item` bloqueados (exit 2); `Read` ignorado.

**Errores a evitar**
- La palabra española "todo" en comentarios C# rompe el build (S1135).
- `dotnet test` posicional con xunit.v3 en .NET 10 → "Testing with VSTest target is no longer supported": usar `--project` con el runner MTP en `global.json`.
- En Git Bash, `/p:` se convierte en una ruta (MSB1008): usar `-p:`.
- Si la versión mutada no compila, `--no-build` prueba binarios viejos y el resultado es falso.
- `git fetch --depth` sobre un clon completo puede volverlo superficial: el critic hace `fetch` sin `--depth`.

**Confluence (15-Oct-2026)**
- Publicado con confirmación del usuario en el espacio `DDS` ("Desarrollo de software") de `uttt-startup-musubi.atlassian.net`: página padre "MUSUBI · Documentación técnica" (id 4947970) con las hijas "00 · Constitución y gobernanza SDD", "01 · Fundación musubi-api", "01 · Fundación musubi-app", "Catálogo de endpoints" y "Trazabilidad HU ↔ specs".
- Regla: las páginas resumen y **enlazan** al repo; nunca copian tablas vivas. Los enlaces a GitHub funcionan después del primer push. Al cerrar una fase o HU relevante, actualizar el resumen de la página correspondiente.

**Pendientes con dueño**
- `02-01` modelo de documentos, índices, script `mongosh`, readiness contra MongoDB real, Testcontainers (`NOTE (02-01)`).
- `HU-02` JWT + FallbackPolicy (`NOTE (HU-02)`). `HU-20` SignalR. `HU-31` FCM. `HU-32` IA. `HU-35` ML. `HU-43` Mercado Pago.
- `02-02` caché (Redis/Valkey) y paginación. `02-04` headers, rate limit y compresión. `02-05` AWS con Terraform (ALB + 2 EC2 + CloudFront), Atlas, ForwardedHeaders (`NOTE (02-05)`), `AllowedHosts`, Trivy, ZAP, respaldos y tags de versión. `02-06` CloudWatch, cobertura y SonarCloud. `02-07` moderación. Landing page (repo propio).
- Equipo: roles PO/SM, revisores de las specs, umbrales "Por definir con el PO" (RNF-09, 10, 14, 16, 22), protección de `main` en GitHub y la clave Jira de la tarea de plantilla (commit `SCRUM-XX`).

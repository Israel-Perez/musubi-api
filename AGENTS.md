# AGENTS.md — Guía operativa de musubi-api para agentes y desarrolladores

> Qué no se negocia: `.specify/memory/constitution.md` (manda ante cualquier conflicto).
> Principios y stack: `specs/phase-00-constitution/00-01-principles-stack/spec.md`. No se introducen tecnologías fuera del stack sin la regla de excepción de 00-01.
> Repo hermano: [`musubi-app`](https://github.com/Israel-Perez/musubi-app) (front Flutter). El contrato entre ambos es `docs/endpoints.md`.

## 1. Forma de trabajo

- **Setup:** `dotnet restore Musubi.slnx` → `dotnet run --project Musubi.Api` (Development usa `UseInMemoryDatabase=true`, sin MongoDB; API en `http://localhost:5080`, Scalar en `/scalar`). Para usar MongoDB local o Atlas, ver `deploy/README.md`.
- **Comandos:**
  - `dotnet build Musubi.slnx -c Release` (0 errores / 0 advertencias)
  - `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` (igual para `IntegrationTest`, `SecurityTest`, `ContractTest` y `PerformanceTest`)
  - Mutación: `dotnet tool restore` → `cd MutationTest` → `dotnet stryker` (Stryker no soporta MTP: usa xUnit v2, ver `02-09`)
  - `pwsh scripts/critic-guardrails.ps1` · `pwsh scripts/check_endpoints.ps1`
  - `pwsh scripts/check_endpoint_tests.ps1 -Suite UnitTest|IntegrationTest|SecurityTest` (después de correr esa suite: cada fila de `docs/endpoints.md` con su prueba, SCRUM-120)
- **Orden del CI (`ci-pr.yml`):** `build` → `unit` / `integration` / `security` / `contract` / `database` (MongoDB real; requiere Docker); en paralelo `critic`, `endpoints`, `semgrep` y `docker-build`.
- **Flujo SDD por HU:** spec (`specs/mNN-<modulo>/HU-NN-<slug>/`, skill `spec-first-writing`) → `slice-scaffolder` (Fase A) → implementación y pruebas (skill `vertical-slice`) → `security-reviewer` → `traceability-clerk` → PR.
- **Ramas y PR:** `feature/SCRUM-NN-slug` / `fix/SCRUM-NN-slug`; un PR = una HU o un fix; título con la clave Jira; checklist de `.github/pull_request_template.md` obligatorio.
- **Naming:** código en inglés, JSON camelCase, documentos y specs en español.
- Sin secretos, sin `TODO` (solo `NOTE (ID)`), sin usings sin usar, sin código muerto.
- Al terminar, resume qué cambiaste y qué decisiones debe revisar el usuario.

## 2. Memoria

> **Gobernanza SDD:** plantilla obligatoria en `specs/_template.md` y DoD en `specs/phase-00-constitution/00-02-sdd-governance/spec.md`. Cada spec nueva sigue la plantilla; los criterios siempre se verifican con un comando o una prueba.

- `AGENTS.md`: manual operativo vivo. `Memoria.md`: bitácora del estado, las decisiones y las lecciones.
- **Al empezar**, lee `Memoria.md` para conocer el estado del proyecto y las decisiones tomadas.
- **Al terminar una tarea**, actualízala: estado actual, decisiones importantes (con su porqué) y errores a evitar.
- **Al cerrar una spec**, concilia `spec.md`/`plan.md`/`task.md` con lo implementado (rutas de pruebas, conteos y nombres reales) y resuelve el bloque de aprobación: "Borrador con evidencia", o "Aprobado" solo si el usuario indicó un revisor.
- Mantenla breve (máximo ~10 000 líneas): resume o elimina lo que ya no aporte.
- Si algo se vuelve regla permanente, propón moverlo a `AGENTS.md` o a `00-04` en lugar de dejarlo en la memoria.
- Nunca guardes datos sensibles (llaves, tokens, datos personales).
- Si cambias una **sección compartida** (marcada con `<!-- Sección compartida … -->`), cámbiala también en `musubi-app`, regístralo en las dos `Memoria.md` y corre `pwsh scripts/check_shared_sections.ps1`.

## 3. Límites

- No uses la palabra española "todo" en comentarios C#: S1135 rompe el build.
- No leas configuración de forma eager en el registro DI si las pruebas la sobrescriben: usa la sobrecarga con `IServiceProvider` (ver `AddMusubiMongo` en `Platform/Data`).
- No reordenes el pipeline de `UseMusubiPipeline` sin actualizar la spec `01-02`.
- No pongas `try/catch` en controllers (critic B4).
- No hardcodees connection strings, llaves ni credenciales; tampoco en appsettings versionados.
- No dupliques tablas vivas en las specs: el canónico vive en `docs/`, la spec enlaza.
- No corras pruebas `--no-build` sobre un build que falló: leerías resultados de binarios viejos.
- En Git Bash usa `-p:` y no `/p:` en los comandos de `dotnet`.
- Siempre: actualiza `Memoria.md` al terminar cada tarea.

## 4. Verificación

- Build Release 0/0 antes de cualquier prueba.
- Unit, Integration, Security y Contract con `--no-build`: 100 % en verde en local.
- `pwsh scripts/critic-guardrails.ps1` → `PASS` exit 0 (también con `powershell -File` en 5.1).
- `pwsh scripts/check_endpoints.ps1` → `OK` exit 0 (cada ruta con fila y cada fila con ruta).
- `pwsh scripts/check_endpoint_tests.ps1 -Suite <UnitTest|IntegrationTest|SecurityTest>` → `OK` exit 0. Un endpoint nuevo (cualquier HU que venga) necesita una prueba de integración y una de seguridad que lo llamen, y su HU una ruta `UnitTest/` en `docs/trazabilidad.md`; si una suite no puede tocarlo, la excepción va en `$Waivers` con su motivo.
- YAML del CI validado en local: `python -c "import yaml; yaml.safe_load(open('.github/workflows/ci-pr.yml'))"` y `actionlint .github/workflows/ci-pr.yml`.
- SAST en local antes del primer push de una rama: `semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error`.

### 4.2 Verificación de requerimientos no funcionales

- Tabla completa y comandos: `specs/phase-00-constitution/00-03-nfr/spec.md`.
- Guardarraíl: si un cambio impacta un umbral (latencia, tamaño de cuerpo, disponibilidad), ejecuta su comando de verificación antes de pedir la aprobación.

### 4.3 Referencia de configuración

La tabla canónica de claves y su estado por fase vive en `specs/phase-01-foundation/01-04-config-reference/spec.md`. `appsettings.Example.json` solo documenta claves con código real. Los secretos van en user-secrets (`dotnet user-secrets --project Musubi.Api set …`) o en `appsettings.Local.json` (ignorado).

## 5. Convenciones técnicas

> Fase 01 implementada (15-Oct-2026), en "Borrador con evidencia". Fuente viva de rutas: `docs/endpoints.md` (132 filas, incluido el hub `WS /hubs/chat`; la verifica `scripts/check_endpoints.ps1`).

- Rutas `api/v{version:apiVersion}/<recurso>` con `[ApiVersion("1.0")]`; módulos en `Musubi.Api/Modules/<Modulo>/` (ver su README).
- Health: `/health` (liveness) y `/health/ready` (readiness con la BD). OpenAPI (`/openapi/v1.json`) y Scalar (`/scalar`) solo en Development.
- Datos: MongoDB (`IMongoClient`/`IMongoDatabase` lazy en `Platform/Data/MongoServiceCollectionExtensions.cs`; sin MongoDB con `UseInMemoryDatabase=true`). Modelo y reglas R1–R14 en `docs/modelo-datos.md` (02-01); esquema en `db/NNN-*.js` + `MongoSchema.ExpectedVersion`; cada repositorio tiene un `Mongo*` y un `InMemory*` que pasan la misma batería de `TestShared/`.
- Versión: `<Version>` de `Musubi.Api.csproj`, alineada con `docs/versionado.md` (lo verifica `UnitTest/Common/VersioningTests.cs`).
- Caché (02-02): un catálogo que cambia poco se lee con un decorador `Cached*Repository` sobre `AppCache` (Redis o memoria, siempre con degradación); una lista pública lleva `[OutputCache(PolicyName = ...)]` y `[ConditionalGet]`; lo que cambia con una escritura se invalida (`EvictByTagAsync`). Nunca se cachea algo con sesión en la caché de salida.
- Lineamientos de la materia (criterios de evaluación): `docs/bitacora/lineamientos-materia.md`.
- Errores: `return this.Problem(<Modulo>Errors.X)` con un código del catálogo `docs/errores.md` (nunca `try/catch`). Sesión: JWT (`AccessTokenIssuer`) + `RefreshTokenService` (02-03) + lista de revocación en Redis (`IAccessTokenRevocations`, 02-10): todo lo que cierre sesiones llama `RevokeUserAsync` (o `RevokeTokenAsync` con el `jti`) además de revocar los refresh tokens; cada endpoint exige sesión salvo `[AllowAnonymous]`; los de `/auth/*` llevan `[EnableRateLimiting(AuthPlatformServiceCollectionExtensions.AuthRateLimit)]`.
- Moderación (02-07): todo texto o foto que publica una persona pasa por `IContentModerator` antes de guardarse (`ModerationErrors.For(...)`); en Development el moderador en memoria rechaza `[[inapropiado]]` y las fotos magenta.
- Tiempo real (HU-20): SignalR en `Platform/Realtime`, hub `/hubs/chat` solo con WebSockets y backplane de Redis (`Cache:ConnectionString`). Los mensajes se envían por REST y el hub solo entrega; su único método es `Typing` (HU-65).
- Fotos, "escribiendo…" y "leído" (HU-65): `ChatService.SendPhotoAsync` usa `PhotoUploadService.ReadAsync` y la moderación de imágenes y guarda en `chat/{conversación}/{id}.webp` (fuera de `media/`: CloudFront no la sirve); `GET /conversations/{id}/photos/{name}` la entrega solo a los participantes (`IMediaStorage.GetAsync`, `Cache-Control: private`). `MarkReadAsync` guarda `buyerReadAt`/`sellerReadAt` y emite `conversationRead`; `TypingAsync` emite `typing` a la otra persona. Con un bloqueo nada de esto funciona. `ChatAccountEraser` borra las fotos con la conversación.
- Pedidos (HU-23): `Modules/Orders/`. El pedido sale de una conversación (`IConversationDirectory.FindAsync`), con la publicación (`IProductDirectory`) o la oferta aceptada (`IOfferDirectory`); uno abierto por conversación (`ux_conversation_open`) y número `MS-NNNN` por campus (`counters`). Listings pregunta por pedidos abiertos con `IOpenOrders` (RN-07).
- Acuerdo con IA (HU-32/33/34): `Modules/GenAi/`. `POST /agreements` lee el chat por `IConversationNotices` (sin nombres ni datos de contacto, `TranscriptRedactor`), llama a `IAgreementExtractor` (Gemini, `InMemory` en Development, `Off`) y valida cada valor con las reglas del pedido; deja el aviso `kind: notice` en el chat y emite `agreementChanged`. Los dos confirman la misma `revision` (RN-41) y `POST /orders` con `agreementId` crea el pedido (`IAgreementDirectory`). Sin IA, `503 genai.unavailable`.
- Avisos (HU-31): `Modules/Notifications/`. Otro módulo da un aviso con `INotificationCenter.NotifyAsync(new Notice(...))`: con `Category` queda en el historial (60 días, `dedupeKey` para no repetir) y siempre se encola el push; nunca falla hacia quien lo llama. `PushWorker` envía por `IPushSender` (`FcmPushSender`: JWT RS256 de la cuenta de servicio → access token → `messages:send`; `RecordingPushSender` en Development) y borra los tokens desinstalados. `WantedReminderWorker` revisa cada hora las solicitudes por vencer.
- Coincidencias (HU-19): `Modules/Matching/`. Al publicar una solicitud, `WantedService` la encola con `IWantedMatching` y `WantedMatchWorker` la revisa en segundo plano: `IProductMatches` (M-03) da las candidatas y `MatchRules` decide (misma categoría o cualquiera con "otro", la mitad de las palabras del nombre, hasta 20 % sobre el presupuesto; una vez por solicitud y persona y hasta 3 al día). Al cerrar o cancelar una solicitud, quien tenía una oferta pendiente recibe `offer_closed` (HU-30). Al publicar una publicación, `ListingService` la encola con `IProductPublications` y `ProductMatchWorker` avisa con `product_match` a quien tenga una solicitud activa compatible (HU-29, mismas reglas; `IWantedMatchDirectory` de M-05). `GET /wanted/{id}/matches` (HU-18) da las exactas y las parecidas. Cada coincidencia se registra en `matches` con `IMatchLedger` (RF-25: a quién se avisó y si terminó en pedido; Orders la marca al crear el pedido). Búsquedas guardadas (HU-12): `SavedSearchService` en Listings (`/saved-searches`, 30 días, 10 por persona) y `ISavedSearchDirectory`; al publicar, `ProductMatchNotifier` también avisa `saved_search_match` (todas las palabras y los filtros) a quien no recibió ya el de su solicitud.
- Recomendaciones (HU-35): `Modules/Recommendations/`. Las señales (ver el detalle, favorito, chat desde una publicación y compra) se registran con `IInteractionRecorder` (nunca falla hacia quien llama; `interactions`, 90 días). `RecommendationModelStore` entrena con ML.NET un `RecommendationModel` por campus cada 30 minutos (TF-IDF del catálogo de `IRecommendationCatalog`, perfil de la persona y factorización de matrices de una clase solo si la evaluación leave-one-out muestra que mejora) y registra su hit@5 en los logs.
- Apoyo a vendedores (HU-36/37/38): `Modules/SellerSupport/`, sin colecciones propias. `GET /products/mine/slow` da lo disponible con más de 21 días y sin pedidos (vistas de `productStatsDaily`, conversaciones de M-07, `demand` y `tips` de `SalesTips`) y `GET /products/{id}/demand` las solicitudes que coinciden y cuántas búsquedas guardadas (solo el número, RN-37). La demanda sale de `IProductDemand` (M-06, `DemandRules`: las mismas reglas que los avisos de HU-29 y HU-12). `SlowListingWorker` avisa `slow_listing` una vez a las 3 semanas. `POST /products/{id}/ideas` pide a `IListingCopywriter` (M-10: Gemini, `InMemory`, `Off`; la llamada HTTP es `GeminiApi`, compartida con el acuerdo) un nombre y una descripción con la demanda, sin nombres ni datos de contacto; no guarda nada y sin IA responde `503 seller_support.ideas_unavailable`. Al editar una publicación, `MyListingsService` la vuelve a encolar en `IProductPublications`.
- Pedidos (HU-24): `OrderLifecycleService` acepta (quien vende, con el pago elegido), cancela (motivo de `CancelReasons`) y confirma la entrega de cada parte; aparta, regresa y vende las piezas con `IProductStock` (Listings) y avisa con `INotificationCenter`.
- Intereses (HU-66): `GET`/`PUT /profiles/me/interests` guardan slugs de categorías en `preferences.interests` (privados: el perfil público no lee `preferences`); HU-35 podrá usarlos en el arranque en frío (`NOTE (HU-35)`).
- Pagos (HU-55, D-01): `Modules/Orders/`. La cuenta para recibir pagos (`/payouts/*`, `PayoutService`) valida la CLABE (`Clabe`, dígito verificador; `MexicanBanks`, claves de SPEI) y el titular (`HolderNames`), y la cifra con `ClabeProtector` (AES-256-GCM, `Payments:EncryptionKey`; sin llave, solo efectivo). Por transferencia, `OrderPaymentService` da la cuenta (completa a quien compra, enmascarada a quien vende), recibe la referencia y "todavía no me llega"; aceptar es "Sí, me llegó el pago". En efectivo, aceptar genera el código de 4 dígitos y `POST /orders/{id}/delivery-code` completa el pedido (5 intentos). `TransferReminderWorker` recuerda a las 24 h sin cancelar. NOTE (HU-55): el comprobante, con almacenamiento privado.
- Apartados (HU-52): `ReservationService` en `Modules/Orders/` (`/reservations`). Apartar resta una pieza con `IProductStock`, abre la conversación (`IConversationOpener.OpenForProductAsync`) y guarda un código de 6 dígitos (solo lo ve quien aparta; su QR es `musubi:apartado:{id}:{code}`). Uno activo por persona (`ux_buyer_active`). Quien vende verifica (5 intentos) y "Confirmar entrega" crea un pedido completado con `OrderLifecycleService.AfterCompletedAsync`. `ReservationExpiryWorker` vence a las 24 h y regresa la pieza.
- Comparar y "Avísame" (HU-51): `Modules/Listings/`. `ProductComparisonService` (`GET /products/{id}/compare`) elige hasta 5 disponibles de la misma categoría (de las 50 más nuevas, sin las mías ni bloqueos): el mismo modelo primero (`ModelCodes`: un código del nombre como "991ES") y luego el precio más cercano, con la reputación y las ventas de `IProfileDirectory` (`ProfileCard.Sales`, `null` si las oculta); el puntaje "Para ti" es de la app. `StockAlertService` (`/products/{id}/stock-alert`, `stockAlerts`) guarda "Avísame" en una agotada (20 por persona, 90 días). `IProductStock.ReleaseAsync`, "Reponer" y reactivar encolan `IProductRestocks`; en M-06 `BackInStockWorker` relee la publicación y, si tiene piezas, toma los avisos con `IStockAlertDirectory` (se borran: se usan una vez) y avisa `back_in_stock`.
- Moderación (HU-39/40): `Modules/Admin/`, solo `[Authorize(Roles = "admin")]`. `ModerationService` vuelve a leer la cuenta del administrador (la base manda sobre el token) y usa los contratos `IReportInbox` (M-13), `IProductModeration` (M-03) e `IAccountModeration` (M-01). Todo es del campus del administrador.
  - "Procedente" con una publicación la retira (`removed`).
  - Suspender cierra las sesiones y pausa las publicaciones.
  - Cada acción va a `moderationLog` (RNF-04).
  - El rol se da con `Admin:Emails` al iniciar sesión o en la base (`db/README.md`), y la sesión lo trae en `user.roles`.
- Categorías (HU-41): `CategoryAdminService` (`/admin/categories`, con `AdminGate`) usa `ICategoryCatalog` (M-03): el slug sale del nombre y no cambia, una desactivada sale de "Vender-3" y "Filtrar" pero sus publicaciones siguen visibles, y cada cambio borra la entrada de `CachedCategoryRepository` y las etiquetas `catalog` y `showcase` de la caché de salida.
- Insignias de confianza (HU-64): `Modules/Profiles/`. `ChatService.SendAsync` registra con `IProfileResponses` cuánto tardó quien vende en contestar a quien compra (`profiles.responses`, las últimas 20 con `$push`/`$slice`). `SellerBadges` decide: `responseMinutes` es la mediana de los últimos 30 días con al menos 3 y `trusted` es 4.5 ★ con 5 ventas; salen en `stats` de `GET /profiles/{id}`.
- Precio sugerido (HU-58): `Modules/Recommendations/PriceModel.cs` y `PriceSuggestionService.cs`. `PriceModelStore` entrena por campus una regresión SDCA de ML.NET sobre el logaritmo del precio (TF-IDF del nombre como HU-35, categoría y estado) con `IPriceCatalog` (M-03: disponibles, agotadas y vendidas) e `ISoldPrices` (M-08: lo pagado en pedidos completados, peso 3). El rango es la estimación ± el error típico medido (de ±10 % a ±60 %), en pesos redondos. Con menos de 5 precios en la categoría o 20 en el campus no sugiere nada. Se reentrena en segundo plano a los 30 min y registra el MAE contra la mediana de la categoría. `POST /products/price-suggestion` y `GET /products/{id}/price-estimate` (con veredicto); nunca cambia un precio (RN-38).
- "Llenar con IA" (HU-59): `Modules/GenAi/PhotoDraft.cs` (`IPhotoDraftWriter`: Gemini con la foto como `inlineData`, en memoria y apagado) y `Modules/Listings/PhotoDraftService.cs`. `POST /products/photo-draft` recibe la llave de una foto propia (`IsOwnPhotoKey`, antes de leer nada), manda la portada y las categorías activas del campus, valida la respuesta (`PhotoDraftJson`: categoría del campus o `null`, estado con su detalle) y modera el texto. No guarda nada; sin IA, `503 listings.draft_unavailable`. Comparte el rate limit `genai`.
- Coincidencias por significado (HU-60): `Modules/GenAi/TextEmbeddings.cs` (`ITextEmbedder`: Gemini `batchEmbedContents` con `RETRIEVAL_QUERY` para las solicitudes y `RETRIEVAL_DOCUMENT` para las publicaciones; en memoria con grupos de palabras; apagado) y `Modules/Matching/SemanticMatching.cs` (`SemanticMatcher`, colección `matchEmbeddings`). `MatchRules.Compatible` = la mitad de las palabras **o** similitud ≥ `SemanticThreshold` (0.74, medido en `SemanticEvaluationTests`). Los workers de HU-19/HU-29 calculan los vectores que falten (`computeMissing: true`); HU-18 y HU-38 se consultan desde la app y solo usan los guardados. Con vectores, `ProductMatchQuery.AnyWords` trae candidatas sin filtrar por palabras. Sin proveedor o si falla, deciden las palabras.
- Indicadores de uso (HU-42): `Modules/Admin/UsageIndicatorsService.cs` junta los 15 indicadores del apartado 19 de la propuesta. Cada módulo da los suyos detrás de su contrato (`IUserIndicators`, `IListingIndicators`, `IWantedIndicators`, `IMatchIndicators`, `IOrderIndicators`, `IChatIndicators`, `IAiIndicators`, `INotificationIndicators`, `ITrustIndicators`), con un método `UsageAsync`/`…BetweenAsync` por repositorio. El servicio solo cruza (conversaciones con pedido, avisos de "sin venderse" con ventas). `aiUsage` (`db/036`) anota los resúmenes y las confirmaciones porque el acuerdo se borra. Periodo `[ahora − 7 o 30 días, ahora]`; un porcentaje sin base es `null`.
- Entrega (HU-62): `Modules/Orders/DeliveryReminders.cs`. `DeliveryReminderWorker` revisa cada `Notifications:DeliveryReminderMinutes` (5) los confirmados con entrega en la próxima hora (`ix_confirmed_delivery`) y avisa `delivery_reminder` a los dos con `dedupeKey` por pedido y parte (la hora con `CampusClock`, sin ICU). `POST /orders/{id}/arrived` (`OrderArrivalService`) guarda `handover.*ArrivedAt` una vez por parte, desde 2 h antes; deja el aviso en el chat con `IConversationNotices.PostNoticeAsync(..., pushTitle: null)` (sin el push de mensaje) y avisa `order_arrived`.
- Pendientes con dueño: HU-43 Mercado Pago fuera de alcance (D-01).

## 6. Seguridad y DevSecOps

- HSTS de 365 días solo fuera de Dev; HTTPS obligatorio; CORS restringido por configuración (S5122 lo impone en el build).
- Errores: `problem+json` con `code`, título en español y `traceId`, sin detalle interno (`docs/errores.md`).
- Sesión obligatoria por defecto (FallbackPolicy), JWT solo HS256, llave fuera del repo (fail-fast sin `Jwt:SigningKey` fuera de Development); contraseñas PBKDF2-SHA512 de 210 000 iteraciones.
- Auth explícita por controller (critic B3 + `SecurityTest/Auth/ExplicitAuthTests.cs`). Referencia: OWASP API Security Top 10 – 2023 (checks del agente `security-reviewer`).
- Access token revocable (02-10): `jti` y "revocado desde" por persona en Redis, consultados en `OnTokenValidated`; si Redis no responde se acepta hasta que venza (RNF-15). Compresión Brotli/Gzip, nunca en `/auth` (BREACH).
- SAST: Semgrep (`--config=auto` + 5 reglas propias). Dependencias: NuGetAudit (incluidas las transitivas) + Dependabot con `cooldown` de 7 días.
- Contenedor: multi-stage con usuario no root; ECR con escaneo al subir y tags inmutables (02-05); Trivy escanea la imagen en el job `docker-build`.
- DAST (SCRUM-123): OWASP ZAP 2.17.0 en `dast.yml`.
  - Escanea la API levantada en el job, en modo Production y en memoria, con y sin sesión (token de `scripts/dast/mint_token.py`).
  - Falla con riesgo Alto (`scripts/dast/zap_gate.py`); las excepciones van en `.zap/rules.tsv`.
- gitleaks revisa el historial completo de Git (job `gitleaks`; las excepciones van en `.gitleaksignore`).
- SonarCloud sigue pendiente (02-06).
- Las llaves privadas (IA, Firebase Admin, cifrado de la CLABE) viven solo en el back, nunca en el front.

## 7. Testing

- xUnit v3 sobre Microsoft.Testing.Platform (`global.json` → `test.runner`): el comando es `dotnet test --project <csproj>`.
- WebApplicationFactory con `UseInMemoryDatabase=true` (sin MongoDB) en Integration, Security y Contract; las suites WAF corren en serie (`xunit.runner.json`).
- SecurityTest usa factories por ambiente (`ProductionApiFactory`, `DevelopmentApiFactory`) y un controller de prueba (`ThrowingProbeController`) que solo existe en ese ensamblado.
- Cobertura por endpoint (02-10): IntegrationTest y SecurityTest registran los endpoints que tocan (`IntegrationTest/Common/EndpointCoverage.cs`, fixture de ensamblado) y `scripts/check_endpoint_tests.ps1` lo compara con `docs/endpoints.md` en el CI.
- Matriz completa: skill `testing-strategy`.

## 8. Observabilidad

- Logs: JSON fuera de Development y una línea por petición (`Platform/Observability`, sin los health checks), en CloudWatch `/musubi/prod/api`; los errores llevan `traceId` (`docs/errores.md`).
- Métricas y alarmas (`infra/stack/observability.tf`), con aviso por correo (SNS):
  - errores de la API (filtro de métrica `ApiErrors`);
  - 5xx del balanceador;
  - instancias sanas;
  - latencia p95.
- Dashboard `musubi-api` en CloudWatch.
- Pendiente (02-06): trazas distribuidas.

## 9. Despliegue y operación

- **02-05 (Borrador con evidencia):** `infra/` (Terraform) + workflows `infra-up` / `infra-down` (OIDC, entorno `produccion` solo para `main`). Guía, secretos y costo en [`infra/README.md`](infra/README.md).
- CloudFront (HTTPS) → ALB → Auto Scaling Group de 2 EC2 con Docker; secretos en Parameter Store `/musubi/prod/*`; logs en CloudWatch `/musubi/prod/api`.
- Para desplegar: subir `<Version>` y agregar su fila en `docs/versionado.md`, fusionar y correr `infra-up`. Si el tag `api-vX.Y.Z` ya existe en otro commit, falla.
- No guardes secretos en Terraform: viven en Parameter Store y entran como variables de entorno.
- Dominio: `api.musubiapp.com.mx` en CloudFront con certificado ACM (TLS 1.2).
- Pendiente: respaldos (RNF-16; Atlas M0 no los incluye).

## 10. Reglas basadas en lecciones aprendidas

La fuente canónica es `specs/phase-00-constitution/00-04-lessons-learned/spec.md` (13 heredadas + 10 propias). Ningún agente debe contradecirlas.

## 11. Sub-agentes (híbrido agentes → skills)

- `slice-scaffolder`: Fase A del lado back de una HU (esqueleto compilable con pruebas que asertan cada diferido); nunca se commitea sin su Fase B.
- `security-reviewer`: gate pre-push contra OWASP API Top 10 + constitución; **solo lectura forzada** (`disallowedTools` + hook `readonly-git-guard.ps1`).
- `traceability-clerk`: spec ↔ código ↔ test ↔ Jira; solo lectura forzada.
- Invocación manual en desarrollo; en el CI solo corren los scripts (`critic`, `endpoints`).
- Fuente de reglas: `.claude/skills/` (10 skills); los agentes las enlazan, nunca las copian. Convención: `.claude/agents/README.md`.
- OpenCode: lee este `AGENTS.md` y las skills de `.claude/skills/`; sus agentes están en `.opencode/agents/` y remiten a `.claude/agents/` (solo lectura por `permission`). Flujo del equipo: `docs/guia-equipo.md`. MCP del equipo (Atlassian y AWS): `docs/mcp.md`.

---

## Estructura de la solución

`Musubi.slnx` usa el formato XML `.slnx` (no `.sln`).

| Proyecto / carpeta | Ruta | Tipo |
|---|---|---|
| `Musubi.Api` | `Musubi.Api/` | Web API (`Program.cs`, `Platform/`, `Modules/`) |
| `UnitTest` | `UnitTest/` | xUnit v3 |
| `IntegrationTest` | `IntegrationTest/` | xUnit v3 + WebApplicationFactory |
| `SecurityTest` | `SecurityTest/` | xUnit v3 + WebApplicationFactory por ambiente |
| `ContractTest` | `ContractTest/` | Forma JSON contra `Fixtures/` |
| `DatabaseTest` | `DatabaseTest/` | MongoDB real (Testcontainers) con `db/` aplicado; solo en el CI si no hay Docker local |
| `PerformanceTest` | `PerformanceTest/` | p95 bajo carga concurrente (en proceso; `PERF_BASE_URL` para AWS) |
| `MutationTest` | `MutationTest/` | Stryker.NET con xUnit v2 (no está en `Musubi.slnx`; workflow `mutation.yml`) |
| `TestShared` | `TestShared/` | Baterías de contrato enlazadas en UnitTest y DatabaseTest (no es un proyecto) |
| `db/` | `db/` | Scripts `mongosh` numerados e idempotentes (el script de la base que se entrega) |
| `docs/` | `docs/` | `endpoints.md` (132 filas), `trazabilidad.md` (64 HU), `modulos.md` (14 módulos), `versionado.md`, `bitacora/` (lineamientos) |
| `scripts/` | `scripts/` | critic, endpoints, secciones compartidas |
| `deploy/` | `deploy/` | Compose de MongoDB local |
| `.claude/` | `.claude/` | `agents/` (3), `skills/` (10), `hooks/` |
| `.github/` | `.github/` | CI, Dependabot, plantilla de PR |
| `.semgrep/` | `.semgrep/` | Reglas SAST propias |

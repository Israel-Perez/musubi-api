# Constitución de MUSUBI — musubi-api

> Documento rector bajo Spec Driven Development (SDD). Define los principios, estándares y límites que deben respetar cada spec, cada implementación y cada agente.
> Ante un conflicto entre este documento y otro artefacto, **manda esta constitución**.
> Las secciones 1 a 5 son compartidas con `musubi-app`; las secciones 6 a 11 son propias de este repo.

<!-- Sección compartida con musubi-app/musubi-api: si cambia, cámbiala en ambos repos y regístralo en las dos Memoria.md -->
## 1. Propósito y alcance

- **Producto:** MUSUBI, app Android de compra-venta entre estudiantes de la UTTT (Universidad Tecnológica de Tula-Tepeji). Conecta la **oferta** (productos publicados) con la **demanda** (solicitudes "busco…"), con coincidencias automáticas, chat, pedidos pagados por transferencia a la cuenta del vendedor o en efectivo (sin comisión; Musubi no procesa el dinero), reputación, IA generativa para resumir acuerdos y recomendaciones con ML.
- **Repos hermanos:** `musubi-api` (back, ASP.NET Core / .NET 10) y `musubi-app` (front, Flutter). Cada uno es autosuficiente: constitución, plantilla, specs, `AGENTS.md`, `Memoria.md`, CI y guardarraíles propios.
- **Contrato entre repos:** el contrato HTTP es canónico en `musubi-api/docs/endpoints.md` (más el OpenAPI que la API expone en Development). El front **enlaza** sus filas y nunca lo duplica.
- **Metodología:** Spec Driven Development. Cada funcionalidad nace de una spec en `specs/`; esta constitución gobierna su forma, su implementación y su verificación.
- **Calendario del MVP:** sprints de 2 semanas desde el 28-Sep-2026. **Evaluación el 02-Dic-2026**: empieza descargando la app desde Google Play. Prueba cerrada en Google Play desde el 09-Nov-2026; la versión `1.0.0` se publica antes de la evaluación (matriz en `docs/versionado.md`).
- **Lineamientos de la materia:** son criterios de evaluación y mandan sobre este documento. Texto y checklist (L-NN) en `musubi-api/docs/bitacora/lineamientos-materia.md`. Si una sección de esta constitución o una spec los contradice, se corrige ese artefacto. La app se construye a la imagen de la maqueta (`musubi-app/docs/maqueta/`) y nunca se le quitan funciones.
- **Equipo:** Start-Up 10IDGS-G3 (4 integrantes; nombres y roles en Jira y Confluence, no en el repo público). Roles Scrum (PO/SM) por definir.

## 2. Principios no negociables

| # | Principio | Consecuencia práctica |
|---|---|---|
| 1 | **Seguridad primero** | Validar en la frontera, rechazar por defecto, mínimo privilegio y auth explícita en cada endpoint. Sin secretos en el código, en los logs ni en el APK. Referencias: OWASP API Security Top 10 – 2023 (back) y OWASP MASVS (front). |
| 2 | **Privacidad por diseño** (RNF-05 a RNF-08) | Datos mínimos y aviso de privacidad aceptado (HU-47). La conversación solo se envía a la IA si un participante lo pide, y con datos mínimos. Búsquedas e historial privados (solo uso agregado). Eliminación real de la cuenta y sus datos (HU-46). |
| 3 | **IA y ML desacoplados con degradación controlada** (RNF-15, RNF-23) | La IA generativa y las recomendaciones viven detrás de una interfaz con proveedor intercambiable. Si fallan, búsqueda, chat y pedidos siguen funcionando, y la UI muestra un estado claro en lugar de romperse. |
| 4 | **Multi-comunidad** (RNF-21) | Nada asume una sola universidad: la comunidad (UTTT) es un dato y no una constante en el código. Sumar otra universidad no requiere rediseño. |
| 5 | **Trabajo pesado en segundo plano** (RNF-11, RNF-12) | Coincidencias, notificaciones y llamadas a IA no bloquean la petición del usuario. La UI muestra un indicador mientras espera. |
| 6 | **Empírico antes que teórico** | Versiones, umbrales, timeouts y nombres se miden antes de fijarse (skill `empirical-verification`). Mientras un umbral no se mida, queda como "🎯 Objetivo (Por medir)" o "🎯 Objetivo (Por definir con el PO)". |
| 7 | **Documentar límites** | Los límites conocidos y el alcance diferido se registran con `NOTE (ID)` en 4 capas (código, `spec.md`, `task.md`, `Memoria.md`). Prohibido el workaround no documentado. |

## 3. Gobernanza SDD

- Cada spec sigue `specs/_template.md` (7 secciones + Aprobación y Control de Cambios). Cada criterio cita el comando o la prueba que lo verifica.
- Cada spec vive en su carpeta con `spec.md` (qué y por qué), `plan.md` (cómo) y `task.md` (pasos, pendientes y evidencia).
- **Estados:** "🚧 Borrador" (en redacción) → "🚧 Borrador con evidencia" (implementada y verificada) → "✅ Aprobado" **solo** si el usuario indica el revisor (1 integrante distinto del autor) y el PR está en verde. Sin revisor indicado, la spec queda en "Borrador con evidencia" aunque todo esté en verde.
- **Alcance diferido:** `NOTE (ID)` y nunca `TODO`. El ID es la spec dueña del reemplazo (`NOTE (02-01)`, `NOTE (HU-02)`).
- **Catálogos canónicos** en `docs/`: las specs los enlazan y nunca copian sus tablas. Los guardarraíles detectan el drift.
- Un cambio de principio o de stack exige actualizar esta constitución, el bloque de aprobación de la spec afectada y las dos `Memoria.md` cuando la sección es compartida.
- `AGENTS.md` describe **cómo** se trabaja; esta constitución describe **qué no se negocia**.

## 4. Convención de IDs

| Tipo | Formato | Ejemplo | Carpeta |
|---|---|---|---|
| Spec técnica | `NN-NN` (fase-número) | `01-03` | `specs/phase-NN-<nombre>/NN-NN-<slug>/` |
| Spec funcional (historia de usuario) | `HU-NN` | `HU-01` | `specs/mNN-<modulo>/HU-NN-<slug>/` (mismo ID y misma carpeta en los dos repos) |
| Módulo / épica | `M-NN` | `M-01` | Tabla canónica en `docs/modulos.md` |
| Requerimiento no funcional | `RNF-NN` | `RNF-02` | Spec `00-03` de cada repo |
| Spec del otro repo | `<repo>:<ID>` | `musubi-api:02-01` | — |

- Los `NOTE (…)` usan estos mismos IDs: `NOTE (02-01)`, `NOTE (HU-20)`, `NOTE (musubi-api:HU-02)`.
- Cada spec funcional declara en su encabezado `**HU / Jira / Sprint / Módulo / Contraparte:**` con el enlace a la spec del otro repo.

## 5. Jira, ramas y commits

- **Backlog:** Jira, proyecto `SCRUM` (fuente de verdad de HU, sprints y estados). **Documentación funcional:** Confluence. Los repos enlazan y no duplican.
- **Trazabilidad:** `docs/trazabilidad.md` en cada repo (HU ↔ Jira ↔ sprint ↔ módulo ↔ spec ↔ contraparte ↔ tests ↔ estado).
- **Ramas:** `feature/SCRUM-NN-slug` y `fix/SCRUM-NN-slug`, iguales en los dos repos. Un PR = una HU o un fix.
- **Commits y títulos de PR:** empiezan con la clave Jira, por ejemplo `SCRUM-19 HU-01 Crear cuenta: <resumen> (Unit N/Integration N/Security N, critic PASS)`.
- **HU que toca los dos repos:** un PR por repo con la misma clave; el PR del front enlaza al del back.
- **Protección de `main`:** 1 aprobación y los jobs del CI como checks obligatorios (ver el README de cada repo).
<!-- Fin de sección compartida -->

## 6. Stack de musubi-api

| Capa | Tecnología |
|---|---|
| Runtime / API | .NET 10, ASP.NET Core (controllers), API versionada `api/v1/...` (Asp.Versioning) |
| Datos | **MongoDB** (NoSQL, lineamiento 14): Atlas M0 en la nube, `mongo` 8.0 en local; `MongoDB.Driver`. `UseInMemoryDatabase=true` solo en local y pruebas (sin MongoDB). Script de la base con `mongosh` — `NOTE (02-01)` |
| Caché | Redis/Valkey junto a las instancias (cache-aside) — `NOTE (02-02)` |
| Autenticación | JWT Bearer HS256 de 15 min + refresh token opaco y rotativo con detección de reutilización; contraseñas con PBKDF2-SHA512 de 210 000 iteraciones; sesión obligatoria por defecto (02-03) |
| Correo | Amazon SES (AWSSDK.SimpleEmailV2); en Development, en el log (02-03) |
| Tiempo real | SignalR (hub de chat y avisos dentro de la app) — `NOTE (HU-20)` |
| Notificaciones | Firebase Cloud Messaging; el back envía con credenciales de Firebase Admin — `NOTE (HU-31)` |
| IA generativa | API de un modelo de lenguaje detrás de una interfaz; proveedor intercambiable — `NOTE (HU-32)` |
| Machine Learning | ML.NET (recomendaciones) — `NOTE (HU-35)` |
| Pagos | Sin pasarela (decisión D-01): transferencia a la CLABE del vendedor, guardada cifrada, o efectivo con código de entrega — `NOTE (HU-55)` |
| Documentación de API | OpenAPI + Scalar, solo en Development |
| Contenedores y nube | Docker (multi-stage, usuario no root); AWS con Terraform: ALB + 2 EC2 (Auto Scaling Group) + CloudFront, imágenes en S3 + CloudFront — `NOTE (02-05)` |
| Imágenes | Amazon S3 (AWSSDK.S3) detrás de CloudFront; SkiaSharp (MIT) normaliza cada foto a WebP sin metadatos (HU-06, excepción de 00-01 del 27-Oct-2026) |
| Moderación de contenido | APIs de AWS (Rekognition para imágenes, Comprehend para texto) antes de guardar — `NOTE (02-07)` |
| Pruebas | xUnit v3 (Microsoft.Testing.Platform), WebApplicationFactory, coverlet; Testcontainers.MongoDb (`DatabaseTest`, job `database`) |
| Calidad y seguridad | Analizadores .NET (AnalysisMode=All) + SonarAnalyzer.CSharp, NuGetAudit, Semgrep, critic-guardrails; SonarCloud, Trivy y OWASP ZAP en fases posteriores |
| CI | GitHub Actions (actions fijadas por SHA) + Dependabot |

Regla de excepción: ver `specs/phase-00-constitution/00-01-principles-stack/spec.md`. No se agrega ninguna librería sin evaluar su licencia, mantenimiento y seguridad.

## 7. Arquitectura

- **Monolito modular** en un solo proyecto (`Musubi.Api`), organizado por los 14 módulos de `docs/modulos.md` (RNF-23):
  - `Platform/`: lo transversal (hosting y pipeline, datos, ping).
  - `Modules/<Modulo>/`: uno por módulo, creado con su primera HU (convención en `Musubi.Api/Modules/README.md`).
- Rutas `api/v{version:apiVersion}/<recurso>`; JSON camelCase; contrato canónico en `docs/endpoints.md`.
- Pipeline en orden fijo (spec `01-02`): ForwardedHeaders → ExceptionHandler + HSTS (fuera de Dev) → HttpsRedirection → CORS → OpenAPI/Scalar (solo Dev) → Authorization → Controllers → Health.
- Health: `/health` (liveness) y `/health/ready` (readiness, incluye el `ping` a MongoDB).
- La IA, el ML, la moderación de contenido y FCM son **adaptadores** detrás de una interfaz del módulo; su falla degrada solo esa funcionalidad (principio 3).

## 8. Estándares de código y API

- Código en inglés; documentos y specs en español. JSON camelCase.
- Advertencias = errores (`Directory.Build.props`); `Nullable` habilitado; usings explícitos.
- DTOs de entrada validados; códigos HTTP correctos (200/201/204/400/401/403/404/409/422/500).
- Inyección de dependencias e `IOptions`; la configuración se lee de forma lazy cuando las pruebas pueden sobrescribirla.
- Sin `try/catch` en controllers: los errores los maneja el pipeline (01-03) y el contrato de errores de negocio de `02-03`.
- En comentarios de C# no se usa la palabra española "todo": S1135 la trata como marcador TODO y rompe el build (lección 00-04).

## 9. Testing y QA

- Suites separadas: `UnitTest`, `IntegrationTest` (WebApplicationFactory), `SecurityTest`, `ContractTest` (forma JSON contra fixtures) y `DatabaseTest` (MongoDB real con Testcontainers y los scripts de `db/` aplicados). Los fakes en memoria y MongoDB comparten una batería de contrato (`TestShared/`).
- Las pruebas se ejecutan con `dotnet test --project <csproj> -c Release --no-build` después de un build Release 0/0.
- RNF-25: pruebas automatizadas obligatorias en coincidencias, pedidos y autenticación.
- Cobertura y mutation testing: umbrales "🎯 Objetivo (Por medir)" en `00-03`.

## 10. CI/CD

- `.github/workflows/ci-pr.yml` en `pull_request` y `push` a `main`, con los jobs `build`, `unit`, `integration`, `security`, `contract`, `database`, `critic`, `endpoints`, `semgrep` y `docker-build`.
- Actions fijadas por SHA con comentario de versión; YAML validado en local con PyYAML y actionlint.

## 11. Seguridad (DevSecOps)

- HSTS de 365 días solo fuera de Development; HTTPS obligatorio (RNF-02).
- CORS restringido por configuración (sin comodín; SonarAnalyzer S5122 lo bloquea en el build).
- Errores: `problem+json` con `code` estable, título en español y `traceId`, sin detalle interno (catálogo `docs/errores.md`, 02-03).
- Rechazar por defecto: la política de respaldo exige sesión en cada endpoint salvo `[AllowAnonymous]`; JWT solo HS256 del emisor y la audiencia propios; rate limit en `/auth/*`.
- Secretos solo en user-secrets, archivos ignorados o variables/secretos del despliegue. Los appsettings versionados solo llevan placeholders (pruebas en `UnitTest/Common/AppSettingsTests.cs`).
- Las llaves privadas (IA, Firebase Admin, cifrado de la CLABE) viven **solo** en el back.

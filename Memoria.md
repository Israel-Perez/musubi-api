# Memoria del proyecto — musubi-api

Bitácora viva: estado, decisiones con su porqué y errores a evitar. Reglas de mantenimiento en `AGENTS.md` §2. Las lecciones permanentes viven en `specs/phase-00-constitution/00-04-lessons-learned/spec.md`.

## Estado actual (20-Oct-2026)

- **SCRUM-66** (rama `feature/SCRUM-66-stack-nosql`, PR abierto): stack NoSQL (MongoDB), matriz de versionamiento y lineamientos de la materia. Fases 00 y 01 siguen en "🚧 Borrador con evidencia".
- Build Release 0/0 · Unit 23/23 · Integration 8/8 · Security 11/11 · Contract 2/2 · critic PASS · endpoints OK (3) · Semgrep 0 · actionlint 0 · shared OK (5 bloques).
- Siguiente: `02-01` (modelo de documentos, índices, script `mongosh`, Testcontainers MongoDB) antes de HU-01; luego `02-03` (errores + JWT/refresh/logout).

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

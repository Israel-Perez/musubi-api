# Memoria del proyecto — musubi-api

Bitácora viva: estado, decisiones con su porqué y errores a evitar. Reglas de mantenimiento en `AGENTS.md` §2. Las lecciones permanentes viven en `specs/phase-00-constitution/00-04-lessons-learned/spec.md`.

## Estado actual (15-Oct-2026)

- Plantilla inicial completa: fases 00 y 01 en "🚧 Borrador con evidencia" (sin revisores designados).
- Build Release 0/0 · Unit 19/19 · Integration 8/8 · Security 11/11 · Contract 2/2 · critic PASS · endpoints OK (3) · Semgrep 0 · actionlint 0.
- Repo hermano `musubi-app` creado el mismo día con su plantilla (consume `GET /api/v1/ping`; secciones compartidas idénticas: `pwsh scripts/check_shared_sections.ps1` → OK, 4 bloques).
- Siguiente: redactar `02-01` (modelo de datos + PostgreSQL + Testcontainers) antes de HU-01 (S1).

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
- `02-01` modelo de datos, migraciones, readiness contra PostgreSQL real, Testcontainers (`NOTE (02-01)`).
- `HU-02` JWT + FallbackPolicy (`NOTE (HU-02)`). `HU-20` SignalR. `HU-31` FCM. `HU-32` IA. `HU-35` ML. `HU-43` Mercado Pago.
- `02-04` headers de seguridad y rate limiting. `02-05` AWS, ForwardedHeaders (`NOTE (02-05)`), `AllowedHosts`, Trivy, ZAP y respaldos. `02-06` observabilidad, cobertura y SonarCloud.
- Equipo: roles PO/SM, revisores de las specs, umbrales "Por definir con el PO" (RNF-09, 10, 14, 16, 22), protección de `main` en GitHub y la clave Jira de la tarea de plantilla (commit `SCRUM-XX`).

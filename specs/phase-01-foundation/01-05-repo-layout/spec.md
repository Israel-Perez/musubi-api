# 01-05 — Estructura del repositorio, calidad y CI

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) · contraparte: `musubi-app:01-05`

## Contexto
Árbol objetivo del repo, guardarraíles ejecutables, SAST, CI, Dependabot, contenedor y herramientas de Claude Code. **Depende de**: `01-01`.

## Requisitos
1. Estructura documentada (tabla de Diseño) sin carpetas vacías.
2. `.gitignore`, `.dockerignore`, `.gitattributes` y `.editorconfig` defensivos.
3. Guardarraíles: `critic-guardrails.ps1` (4 bloqueantes) y `check_endpoints.ps1` (bidireccional), compatibles con PS 5.1 y pwsh.
4. Semgrep con al menos 2 reglas propias reales.
5. CI en GitHub Actions con actions fijadas por SHA, en `pull_request` y `push` a `main`.
6. Dependabot (nuget, github-actions, docker) con `cooldown`.
7. `Dockerfile` multi-stage con usuario no root y `deploy/docker-compose.local.yml` con MongoDB 8.0.
8. Sub-agentes y skills de Claude Code, con solo lectura forzada en los agentes revisores.

## Diseño

| Ruta | Tipo | Estado |
|---|---|---|
| `Musubi.Api/` (`Platform/`, `Modules/README.md`) | API | ✅ |
| `UnitTest/`, `IntegrationTest/`, `SecurityTest/`, `ContractTest/` | Suites con pruebas reales | ✅ (19/8/11/2) |
| `DatabaseTest/` | Esqueleto (compila) | ✅ — `NOTE (02-01)` |
| `docs/` | `endpoints.md`, `trazabilidad.md`, `modulos.md`, `bitacora/` | ✅ |
| `scripts/` | `critic-guardrails.ps1`, `check_endpoints.ps1`, `check_shared_sections.ps1` | ✅ |
| `deploy/` | `docker-compose.local.yml`, `.env.example`, README | ✅ |
| `.semgrep/semgrep.yaml` | 5 reglas propias | ✅ |
| `.github/` | `workflows/ci-pr.yml`, `dependabot.yml`, `pull_request_template.md` | ✅ |
| `.claude/` | `agents/` (3 + README), `skills/` (10), `hooks/readonly-git-guard.ps1` | ✅ |
| `Dockerfile`, `.dockerignore` | Contenedor | ✅ (build verificado en el job `docker-build`) |

**CI (`ci-pr.yml`):** `build` → `unit` / `integration` / `security` / `contract` (con `needs: build`); en paralelo `critic`, `endpoints`, `semgrep` y `docker-build`. Cada job con `timeout-minutes`; `permissions: contents: read`; `concurrency` cancela ejecuciones viejas de la misma rama.

**Guardarraíles del critic (B1–B4):** ver la skill `critic-guardrails`.

**Agentes de solo lectura:** `security-reviewer` y `traceability-clerk` con `disallowedTools: Write, Edit, NotebookEdit` y un hook `PreToolUse` (`shell: powershell`) que solo permite `git diff`, `git log` y `git show`.

## Contratos
- Job names del CI = checks obligatorios recomendados para la protección de `main` (README).

## Tests
- Self-tests de los guardarraíles (registrados en Memoria.md, 15-Oct-2026): critic con un scratch → FAIL 5 bloqueantes + 2 avisos; endpoints con una ruta faltante y una fila huérfana → FAIL; hook con 12 payloads → 3 `git` permitidos, 8 bloqueados y 1 herramienta que no es shell (Read) ignorada; Semgrep con un scratch → 5/5 reglas.
- `SecurityTest/Auth/ExplicitAuthTests.cs` complementa el bloqueante B3.

## Criterios
- `pwsh scripts/critic-guardrails.ps1` → PASS y `powershell -File scripts/critic-guardrails.ps1` → PASS.
- `pwsh scripts/check_endpoints.ps1` → OK (3 rutas).
- `python -c "import yaml; yaml.safe_load(open('.github/workflows/ci-pr.yml'))"` → sin error; `actionlint .github/workflows/ci-pr.yml` → exit 0.
- `semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error` → exit 0.
- `git status --short` sin `bin/`, `obj/`, `TestResults/` ni secretos.

## Límites
El `Dockerfile` no se construyó en local (no hay Docker en la máquina de creación); lo verifica el job `docker-build`. SonarCloud, Trivy y ZAP quedan para `02-05`/`02-06`. La protección de rama se configura en GitHub (ver README).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 15-Oct-2026
- **Detalle:** Agregados respecto de la referencia: job `docker-build`, `cooldown` en Dependabot, guard bidireccional y hook de solo lectura.

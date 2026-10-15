# 01-05 — Estructura, calidad y CI (tareas)

## T1 — Guardarraíles y SAST
- **Crear:** `scripts/critic-guardrails.ps1`, `scripts/check_endpoints.ps1`, `scripts/check_shared_sections.ps1`, `.semgrep/semgrep.yaml`.
- **Verificar:** PASS/OK en PS 5.1 y pwsh; self-tests (critic 5 FAIL, endpoints con ruta faltante y fila huérfana, Semgrep 5/5).
- **Estado:** ✅ Hecho (15-Oct-2026).

## T2 — CI, Dependabot, plantilla de PR y contenedor
- **Crear:** `.github/workflows/ci-pr.yml`, `.github/dependabot.yml`, `.github/pull_request_template.md`, `Dockerfile`, `.dockerignore`, `deploy/*`.
- **Verificar:** PyYAML + actionlint 1.7.12 sin hallazgos; Semgrep `--config=auto` exit 0.
- **Estado:** ✅ Hecho (15-Oct-2026). El `docker build` se verifica en el primer run del CI.

## T3 — Claude Code
- **Crear:** `CLAUDE.md`, `.claude/agents/*`, `.claude/skills/*/SKILL.md`, `.claude/hooks/readonly-git-guard.ps1`.
- **Verificar:** self-test del hook (12 payloads: 3 `git` permitidos, 8 bloqueados, 1 Read ignorado).
- **Estado:** ✅ Hecho (15-Oct-2026).

## Pendientes
- Trivy y ZAP → `02-05`; SonarCloud y cobertura → `02-06`; protección de `main` en GitHub → equipo.

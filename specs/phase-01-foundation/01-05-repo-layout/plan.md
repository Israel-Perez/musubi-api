# plan.md — 01-05 Estructura, calidad y CI

1. **Componentes:** `scripts/`, `.semgrep/`, `.github/`, `.claude/`, `Dockerfile`, `.dockerignore`, `deploy/`, `.gitignore`, `.gitattributes`, `.editorconfig`.
2. **Decisiones:**
   - El critic analiza el repo completo si no existe `origin/main` (repo nuevo).
   - Guard de endpoints bidireccional (la referencia solo detectaba rutas sin fila).
   - Job `docker-build` porque no hay Docker local en la máquina de creación.
   - Solo lectura forzada (disallowedTools + hook) en los agentes revisores, no solo documentada.
3. **Guardarraíles:** self-test de cada guard antes de darlo por bueno (00-04 #18).
4. **Validación:** critic y endpoints en PS 5.1 y pwsh; YAML con PyYAML y actionlint; Semgrep auto + reglas propias.
5. **Secuencia:** ignores → scripts + self-tests → Semgrep + self-test → CI + validación → Dependabot → Docker → Claude Code.

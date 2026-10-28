# Sub-agentes de musubi-api (convención híbrida agentes → skills)

Portados de `.opencode/agents/` del repo de referencia a Claude Code:

- Los agentes definen **rol, permisos y criterio de done**.
- Las reglas técnicas viven en `.claude/skills/<nombre>/SKILL.md`; los agentes **solo las enlazan, nunca las copian**. Si una skill cambia, el agente no se toca.

| Agente | Rol | Permisos | Cómo se imponen |
|---|---|---|---|
| `slice-scaffolder` | Fase A: esqueleto compilable del lado back de una HU (entidad → DTO → validador → servicio → controller → pruebas) | Lectura, edición y bash | `tools` en el frontmatter |
| `security-reviewer` | Gate pre-push: critica el diff contra OWASP API Security Top 10 2023 + constitución | Solo lectura + `git diff/log/show` | `disallowedTools: Write, Edit, NotebookEdit` + hook `PreToolUse` → `.claude/hooks/readonly-git-guard.ps1` |
| `traceability-clerk` | Verifica spec ↔ código ↔ test ↔ Jira y reporta drift | Solo lectura + `git diff/log/show` | Igual que `security-reviewer` |

## Invocación

- **Manual en desarrollo:** pídeselo a Claude Code ("usa el agente security-reviewer sobre mi diff") o menciónalo con `@security-reviewer`.
- **En el CI** no corren agentes; corre la versión ejecutable de los checks: `scripts/critic-guardrails.ps1` (job `critic`) y `scripts/check_endpoints.ps1` (job `endpoints`).

## Hook de solo lectura

`readonly-git-guard.ps1` recibe por stdin el JSON de `PreToolUse`. Permite solo `git diff`, `git log` y `git show` sin encadenar (`;`, `&&`, `|`), sin redirigir (`>`, `<`), sin sustitución (`$(…)`) y sin opciones que escriban o ejecuten (`--output`, `--ext-diff`, `--textconv`, `-c`). Cualquier otro comando sale con exit 2 y Claude Code bloquea la llamada.

- Self-test (15-Oct-2026): 12 payloads → 3 `git` permitidos, 8 bloqueados y 1 herramienta que no es shell ignorada (ver Memoria.md).
- Requisito: los hooks de un agente de proyecto solo se ejecutan **después de aceptar el diálogo de confianza del workspace** en Claude Code.
- Usa `shell: powershell` y `powershell -ExecutionPolicy Bypass -File`, así que funciona con la política de ejecución por defecto de Windows.

## OpenCode

`.opencode/agents/` tiene los mismos tres agentes para OpenCode. Su cuerpo solo remite a `.claude/agents/<nombre>.md`, que sigue siendo la fuente única. En OpenCode no corren los hooks de Claude Code: la solo lectura de `security-reviewer` y `traceability-clerk` la imponen sus `permission` (`edit: deny` y bash solo para `git diff`, `git log` y `git show`). Si cambias la descripción de un agente aquí, cámbiala también allá.

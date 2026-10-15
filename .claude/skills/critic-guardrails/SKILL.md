---
name: critic-guardrails
description: Dueña de scripts/critic-guardrails.ps1 de musubi-api — 4 bloqueantes sobre el diff (TODO, secretos, auth explícita, catch en controllers), waivers y hardening para PowerShell 5.1. Úsala antes de cada push, al endurecer el script o al declarar un waiver.
---

## Propósito
Un gate pre-push barato y determinista que corre igual en local y en el job `critic` del CI.

## Cuándo usarla
Antes de cada push o PR; al modificar el script; al necesitar un waiver.

## Precondiciones
PowerShell 5.1 (`powershell -File`) o 7 (`pwsh -File`); git en el PATH.

## Pasos
1. **Ámbito:** unión de `git diff origin/main...HEAD` + `git diff HEAD` + untracked (`git ls-files --others --exclude-standard`). Si no existe `origin/main` (repo nuevo), analiza **todos** los archivos versionados + untracked. El CI usa `fetch-depth: 0`.
2. **B1 TODO:** `TODO` en mayúsculas dentro de un comentario (`//`, `#`, `/*`, `<!--`) en código o configuración. En `.cs` el build ya rompe con S1135, incluso con la palabra española "todo" en minúsculas.
3. **B2 secretos:**
   - `password|secret` en `*Dtos.cs` o `Dtos/` fuera de las pruebas. **Waiver** para DTOs de **entrada** que necesitan la credencial (registro HU-01, login HU-02): comentario en **la misma línea** `// critic-allow: input-credential (HU-NN)`. Sale como WARN para que el revisor confirme que no es un DTO de respuesta.
   - Connection string con `Password=`/`Pwd=` en `appsettings*.json` versionados (los `*.Local.json` están ignorados).
4. **B3 auth explícita:** cada `*Controller.cs` fuera de las pruebas con `[Authorize]`, `[Authorize(...)]` o `[AllowAnonymous]`.
5. **B4 catch en controllers:** prohibido en las líneas agregadas (el manejo de errores vive en el pipeline, 01-03, y en el contrato de `02-03`).
6. **Avisos:** `NOTE` sin ID `(NN-NN)` o `(HU-NN)`; waivers usados.
7. **Hardening** (skill `powershell-quirks`): `Invoke-Git` con `2>&1` + filtrar `ErrorRecord` (con `EAP=Stop`, `git fetch` escribe en stderr y eso es terminating aunque haya `2>$null`); ASCII puro; mensaje `PASS (N archivos analizados…)`.
8. **Self-test** al modificar el script: un scratch con TODO + controller sin auth + `catch` + DTO con password + appsettings con password → `FAIL 5 bloqueantes, exit 1`; tras borrarlo → `PASS, exit 0` (medido el 15-Oct-2026).

## Checklist
4 bloqueantes en verde · avisos revisados · waivers justificados con su HU · `exit 0/1` correcto.

## Criterios de done
`pwsh scripts/critic-guardrails.ps1` → `critic: PASS` y exit 0 sobre el diff real del PR.

## Límites/trampas
- Los archivos que definen los guardarraíles (`scripts/critic-guardrails.ps1`, `.semgrep/semgrep.yaml`, `.claude/hooks/readonly-git-guard.ps1`) se excluyen de B1 porque contienen los patrones que buscan.
- El critic es un proxy estructural: los checks 5–12 del agente `security-reviewer` (BOLA, exposición de propiedades, privacidad) requieren criterio humano o del agente.
- No ampliar los bloqueantes sin self-test (00-04 #18).

## Referencias
`scripts/critic-guardrails.ps1`, `.claude/agents/security-reviewer.md`, `.github/workflows/ci-pr.yml` (job `critic`), `.github/pull_request_template.md`.

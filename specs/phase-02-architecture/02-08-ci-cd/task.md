# 02-08 — CI/CD (tareas)

## T1 — CI en cada PR
- **Qué:** `ci-pr.yml` con 11 jobs (desde `01-05`; `database` y `terraform` desde `02-01` y `02-05`).
- **Estado:** ✅ Hecho.

## T2 — CD manual
- **Qué:** `infra-up` e `infra-down` (desde `02-05`).
- **Estado:** ✅ Hecho. Última corrida: 41693850956 (03-Nov-2026, API `0.4.0-alpha.1`).

## T3 — Ruleset "Main Proteccion"
- **Qué:** no borrar, PR con 1 aprobación, checks, sin force push y revisión de Copilot.
- **Estado:** ✅ Hecho (03-Nov-2026, SCRUM-90).
- **Pendiente del usuario:** la aprobación extra en PR de Copilot sin atribuir se activa desde la interfaz.

## Pendientes
- `NOTE (02-06)`: SonarCloud, Trivy y ZAP.
- SCRUM-94: pruebas de mutación y de rendimiento en el CI.

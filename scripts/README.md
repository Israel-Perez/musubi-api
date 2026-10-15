# scripts

Guardarraíles ejecutables en local y en el CI. Compatibles con PowerShell 5.1 (`powershell -File`) y PowerShell 7 (`pwsh -File`), en ASCII puro (skill `powershell-quirks`).

| Script | Qué verifica | Job CI | Skill |
|---|---|---|---|
| `critic-guardrails.ps1` | 4 bloqueantes sobre el diff: B1 TODO, B2 secretos (DTOs y connection strings), B3 auth explícita en controllers, B4 `catch` en controllers. Si no existe `origin/main`, analiza el repo completo | `critic` | `critic-guardrails` |
| `check_endpoints.ps1` | `docs/endpoints.md` ↔ código, en ambos sentidos (ruta sin fila y fila huérfana) | `endpoints` | `drift-guards` |
| `check_shared_sections.ps1` | Las secciones compartidas son idénticas a las de `musubi-app` (solo en local; si no encuentra el otro repo, lo omite) | — | `traceability` |

Salida: `exit 0` PASS/OK, `exit 1` FAIL con la lista `ruta:línea`.

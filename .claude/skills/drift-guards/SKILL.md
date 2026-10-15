---
name: drift-guards
description: Patrón anti-drift de musubi-api — documento canónico + script extractor que falla en ambos sentidos + job de CI. Úsala al crear un documento que refleja el código (como docs/endpoints.md) o al agregar un guard nuevo.
---

## Propósito
Que los catálogos de `docs/` no se pudran: si el código y el documento divergen, el CI falla.

## Cuándo usarla
Al agregar o cambiar endpoints; al crear un catálogo nuevo; al escribir un extractor.

## Precondiciones
PowerShell 5.1/7 (skill `powershell-quirks`).

## Pasos
1. **Fuente única** en `docs/` (p. ej. `docs/endpoints.md`); las specs solo la enlazan (skill `spec-first-writing`).
2. **Extractor sin dependencias** en `scripts/`: `[regex]::Matches` sobre el código (`[Route]` + `[HttpX]` en `*Controller.cs`, `Map*`/`MapHealthChecks` con literal o constante `const string`); normalizar la versión `v{version:apiVersion}` → `v1` y pasar a minúsculas fuera de `{placeholders}`.
3. **Comparación en ambos sentidos:** ruta del código sin fila → FAIL; fila sin ruta en el código (huérfana) → FAIL.
4. **Self-test:** el primer run debe cazar un gap real o provocado (ruta faltante + fila huérfana; medido el 15-Oct-2026).
5. **Job de CI** paralelo, sin `needs`, `shell: pwsh`, exit 0/1.
6. Registrar el guard en esta skill + `Memoria.md`.

## Checklist
Documento canónico con conteo · script verde en local (5.1 y pwsh) · job en el CI · specs que enlazan sin duplicar.

## Criterios de done
`pwsh scripts/check_endpoints.ps1` → `endpoints: OK (N rutas con fila, 0 huerfanas)` y el job `endpoints` en verde.

## Límites/trampas
- El guard compara verbo + ruta; la auth y los códigos de cada fila los revisa `traceability-clerk`.
- Las rutas solo de Development (`/openapi`, `/scalar`) no son filas: van en la sección de convenciones del catálogo.
- No normalizar dentro de `{placeholders}` (`{id:guid}`).

## Guards vigentes
- `scripts/check_endpoints.ps1` ↔ `docs/endpoints.md` (3 rutas, job `endpoints`, `01-03`).
- `scripts/check_shared_sections.ps1` ↔ secciones compartidas con `musubi-app` (solo en local).

## Referencias
`docs/endpoints.md`, `scripts/check_endpoints.ps1`, `.github/workflows/ci-pr.yml`.

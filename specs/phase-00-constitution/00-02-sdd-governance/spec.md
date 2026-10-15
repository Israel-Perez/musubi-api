# 00-02 — Gobernanza SDD y Definition of Done

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) · contraparte: `musubi-app:00-02`

## Contexto
Define cómo se escriben, revisan, aprueban y verifican las specs de musubi-api. Las reglas comunes viven en la constitución §3 y §4 (secciones compartidas); esta spec fija el DoD ejecutable de este repo. **Depende de**: `00-01`.

## Requisitos
1. Cada spec usa `specs/_template.md` y vive en su carpeta con `spec.md`, `plan.md` y `task.md`.
2. Cada spec define criterios verificables por comando o prueba.
3. La trazabilidad spec ↔ código ↔ test ↔ Jira se cierra en `docs/trazabilidad.md`.
4. Revisión: 1 integrante distinto del autor. Una spec solo queda "✅ Aprobado" si el usuario indica el revisor; si no, queda en "🚧 Borrador con evidencia".
5. Un cambio de principios o de stack actualiza la constitución, el bloque de aprobación y `Memoria.md` (las dos Memoria.md si la sección es compartida).

## Diseño

**Definition of Done por tipo de spec:**

| Tipo | DoD |
|---|---|
| Constitucional (00-*) | Plantilla completa + bloque de aprobación + citas cruzadas (`AGENTS.md`, `Memoria.md`). |
| Técnica (01-*, 02-*) | Plantilla + build 0/0 + suites verdes + guardarraíles PASS + `spec/plan/task` conciliados con los archivos reales + Memoria.md. |
| Funcional (HU-NN) | Lo anterior + filas en `docs/endpoints.md` para cada endpoint + fixture de contrato si cambia la forma JSON + fila de `docs/trazabilidad.md` con Spec, Contraparte y Tests + PR con clave Jira. |
| Fix | Mismo DoD que su tipo + clave Jira del bug + prueba que reproduce el fallo. |

**Comandos del DoD (musubi-api):**

```powershell
dotnet build Musubi.slnx -c Release                                   # 0 errores / 0 advertencias
dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build
dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build
dotnet test --project SecurityTest/SecurityTest.csproj -c Release --no-build
dotnet test --project ContractTest/ContractTest.csproj -c Release --no-build
pwsh scripts/critic-guardrails.ps1                                    # PASS
pwsh scripts/check_endpoints.ps1                                      # OK
```

**Flujo:** spec (Borrador) → `slice-scaffolder` (esqueleto compilable) → implementación + pruebas → `security-reviewer` (gate pre-push) → `traceability-clerk` (drift) → PR → "Borrador con evidencia" → firma con revisor → "Aprobado".

## Contratos
N/A — spec de gobernanza. Referencia normativa: `specs/_template.md` y constitución §3–§5.

## Tests
N/A — se verifica aplicando la plantilla a las 9 specs de las fases 00 y 01 de este repo.

## Criterios
- Las 9 specs de las fases 00 y 01 tienen las 7 secciones + aprobación (`grep -c "^## " specs/phase-0*/*/spec.md` → 8 por archivo).
- Cada criterio de las specs 01-* cita un comando o una prueba concreta.
- Ningún cambio a la constitución queda sin entrada en `Memoria.md`.

## Límites
La trazabilidad es documental (Markdown + enlaces + Jira); no se usa una herramienta de gestión de specs. La firma de revisores depende de que el equipo designe roles (PO/SM pendientes).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 15-Oct-2026
- **Detalle:** Plantilla y DoD adaptados de la referencia; agregada la línea `HU / Jira / Sprint / Módulo / Contraparte` y los estados de la constitución §3.

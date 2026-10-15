---
name: traceability
description: Mantener la trazabilidad de MUSUBI en 4 capas (NOTE en código, spec.md, task.md, Memoria.md) más la clave Jira y docs/trazabilidad.md, con estados honestos (Borrador con evidencia vs Aprobado). Úsala en cada HU, PR y cierre de spec.
---

## Propósito
Que cada requisito se pueda seguir de Jira a la spec, al código y a la prueba, y que ningún pendiente quede sin dueño.

## Cuándo usarla
Al abrir una rama, al diferir alcance, al cerrar una spec y antes de cada PR.

## Precondiciones
Spec existente; clave Jira de la HU (`docs/trazabilidad.md`).

## Pasos
1. **Rama:** `feature/SCRUM-NN-slug` o `fix/SCRUM-NN-slug`.
2. **Commits y título de PR:** `SCRUM-NN HU-NN <Título>: <resumen> (Unit N/Integration N/Security N, critic PASS)` con conteos **reales**.
3. **Pruebas que citan su requisito:** el nombre o un comentario referencia la HU o la spec (`// HU-01 R3`).
4. **Pendientes en 4 capas:** `NOTE (ID)` en el código + `Límites` de `spec.md` + `Pendiente → ID` en `task.md` + entrada en `Memoria.md` (y fila en la tabla de `01-04` si es configuración).
5. **`docs/trazabilidad.md`:** llenar Spec, Contraparte (enlace a `musubi-app`) y Tests; actualizar el Estado técnico.
6. **Estado de la spec:** "🚧 Borrador con evidencia" al terminar con pruebas en verde; "✅ Aprobado" **solo** si el usuario indica el revisor (1 integrante distinto del autor) y el PR está en verde.
7. **HU de dos repos:** un PR por repo con la misma clave; el PR del front enlaza al del back.
8. **Secciones compartidas:** si cambias una, cámbiala en `musubi-app`, regístralo en las dos `Memoria.md` y corre `pwsh scripts/check_shared_sections.ps1`.

## Checklist
Cada requisito con ≥1 prueba · cada prueba con su spec · cada `NOTE` con dueño en 4 capas · fila de trazabilidad completa · clave Jira en el PR.

## Criterios de done
El agente `traceability-clerk` reporta `OK`; el checklist de la plantilla de PR está completo.

## Límites/trampas
- Nada de pruebas "sueltas" sin spec.
- No marcar "Aprobado" para "cerrar": es una mentira que el clerk reporta como gap.
- Jira es la fuente del estado de gestión; `docs/trazabilidad.md` solo registra el lado técnico.

## Referencias
`docs/trazabilidad.md`, `.specify/memory/constitution.md` §3–§5, agente `traceability-clerk`, `scripts/check_shared_sections.ps1`.

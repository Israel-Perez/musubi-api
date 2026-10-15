---
name: spec-first-writing
description: Redactar specs de MUSUBI (técnicas NN-NN o de HU) con la plantilla SDD y criterios de aceptación verificables por comando o prueba. Úsala antes de implementar cualquier HU o cambio técnico, y al refinar una spec.
---

## Propósito
Que cada cambio nazca de una spec vertical (`spec.md` + `plan.md` + `task.md`) con criterios que una persona o el CI puedan comprobar.

## Cuándo usarla
- Al iniciar el sprint de una HU (crear `specs/mNN-<modulo>/HU-NN-<slug>/`).
- Antes de un cambio técnico (fase 02 y siguientes).
- Al refinar una spec existente o al cerrarla (conciliar con lo implementado).

## Precondiciones
- Haber leído `.specify/memory/constitution.md`, `specs/_template.md` y `00-02`.
- Conocer la fila de la HU en `docs/trazabilidad.md` y su módulo en `docs/modulos.md`.

## Pasos
1. Copiar la estructura de `specs/_template.md`; llenar el encabezado `HU / Jira / Sprint / Módulo / Contraparte` (`N/A` en las técnicas).
2. **Contexto:** actor, problema y `**Depende de**: NN-NN`.
3. **Requisitos:** numerados, imperativos, citando los RNF que aplican (p. ej. RNF-03 en endpoints de escritura).
4. **Diseño:** archivos reales bajo `Musubi.Api/Modules/<Modulo>/`; si hay fake-first, declararlo con sus `NOTE (ID)` (skill `deferred-scope-fakes`).
5. **Contratos:** **enlazar** las filas de `docs/endpoints.md` (nuevas o modificadas); nunca copiar la tabla. Indicar el fixture de `ContractTest/Fixtures/` si cambia la forma JSON.
6. **Tests:** rutas reales (`UnitTest/<Modulo>/…`, `IntegrationTest/<Modulo>/…`, `SecurityTest/<Modulo>/…`).
7. **Criterios:** cada uno con su comando o prueba y su umbral (`dotnet test --project … --no-build` → N/N, `check_endpoints` OK, 401/403 por endpoint).
8. **Límites:** lo que no cubre + diferidos con su spec dueña.
9. **Aprobación:** "🚧 Borrador" al escribir; "🚧 Borrador con evidencia" al cerrar; "✅ Aprobado" solo con revisor indicado por el usuario.
10. `plan.md` (componentes, decisiones, guardarraíles, validación, secuencia) y `task.md` (T1…Tn con verificación y pendientes `→ ID`).

## Checklist
- Encabezado completo · Requisitos con RNF · Diseño con archivos reales · Contratos enlazados · Tests con rutas · Criterios con comando · Límites con dueños · Aprobación honesta.

## Criterios de done
Ningún criterio ambiguo: cada uno se puede ejecutar o comprobar; la spec pasa la comparación estructural de `00-02` (8 encabezados `##`).

## Límites/trampas
- No escribir specs horizontales por capa ("todos los DTOs"): una spec = una HU vertical.
- No duplicar tablas vivas (`docs/endpoints.md`, `docs/modulos.md`, `docs/trazabilidad.md`): se pudren (skill `drift-guards`).
- No inventar umbrales: usar "🎯 Objetivo (Por medir)" (principio 6).
- La spec del front de la misma HU vive en `musubi-app` con la misma carpeta; enlazarla en `Contraparte`.

## Referencias
`specs/_template.md`, `specs/phase-00-constitution/00-02-sdd-governance/spec.md`, `docs/endpoints.md`, skills `vertical-slice`, `traceability`, `deferred-scope-fakes`.

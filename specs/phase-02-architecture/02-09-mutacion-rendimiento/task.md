# 02-09 — Mutación y rendimiento (tareas)

## T1 — MutationTest
- **Estado:** ✅ Hecho (03-Nov-2026). Puntaje de 87.5 %. Workflow `mutation.yml`.

## T2 — PerformanceTest
- **Estado:** ✅ Hecho (03-Nov-2026). Job `performance`; medido en proceso y contra AWS.

## T3 — Ruleset
- **Qué:** agregar `performance` a los checks obligatorios de "Main Proteccion" **después** de fusionar (antes bloquearía los PR abiertos, que no tienen ese job).
- **Estado:** ✅ Hecho (03-Nov-2026).

## Pendientes
- `NOTE (02-09)`: ampliar la mutación a los servicios.
- Fijar con el PO el umbral de RNF-09 contra AWS (ya medido con la caché el 03-Nov-2026).

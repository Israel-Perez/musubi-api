<!-- Sección compartida con musubi-app/musubi-api: si cambia, cámbiala en ambos repos y regístralo en las dos Memoria.md -->
# Plantilla de spec — SDD (MUSUBI)

Cada spec del repositorio sigue esta estructura. Vive en su propia carpeta con tres archivos:

- `spec.md`: qué y por qué (esta plantilla).
- `plan.md`: cómo (componentes, decisiones, guardarraíles, validación, secuencia).
- `task.md`: tareas `T1…Tn` con objetivo, archivos, pasos, verificación y pendientes `→ ID`.

Las 7 secciones son obligatorias en las specs orientadas a código; las reglas de opcionalidad están al final.

```markdown
# NN-NN — Título            (spec técnica)
# HU-NN — Título            (spec funcional)

**HU / Jira / Sprint / Módulo / Contraparte:** HU-NN / SCRUM-NN / SN / M-NN / [<repo>:HU-NN](enlace)   ← `N/A` en specs técnicas

## Contexto
Qué problema resuelve y qué specs la preceden. Incluir **Depende de**: `NN-NN`.

## Requisitos
Qué debe hacer el sistema. Lista numerada en lenguaje imperativo. Citar los RNF que aplican.

## Diseño
Enfoque técnico: capas, patrones, archivos y rutas tocados, decisiones de arquitectura.
Si hay fake-first, declararlo con sus `NOTE (ID)` (skill `deferred-scope-fakes`).

## Contratos
APIs, DTOs, eventos, claves de configuración o esquemas de datos afectados.
Enlazar las filas de `docs/endpoints.md` (en musubi-app, las del back); nunca copiar la tabla. "N/A" si no aplica.

## Tests
Rutas y tipos de pruebas que cubren los requisitos.

## Criterios
Criterios de aceptación verificables con un comando o una prueba (umbrales `>=`/`<`, mensajes, ambas ramas).

## Límites
Lo que la spec explícitamente no cubre, los límites conocidos y el trabajo diferido (`NOTE (ID)` → spec dueña).

## Aprobación y Control de Cambios
- **Estado:** ✅ Aprobado | 🚧 Borrador con evidencia | 🚧 Borrador | ❌ Rechazado
- **Revisores:** @handle (1 revisor distinto del autor) | Pendiente de designar
- **Fecha:** DD-Mmm-AAAA
- **Detalle:** decisión registrada, desviaciones y excepciones documentadas.
```

## Reglas de opcionalidad

- En las specs constitucionales o no orientadas a código (principios, NFR, gobernanza), `Contratos` y `Tests` pueden ser "N/A", justificándolo en la propia sección.
- Cada cambio de principios o de stack exige actualizar la fecha y el `Detalle` del bloque de aprobación (y la constitución).
- Los criterios deben ser medibles: incluyen el comando o la prueba que los verifica.
- La trazabilidad es obligatoria al cerrar: spec ↔ código ↔ test ↔ Jira (`docs/trazabilidad.md`).
- "✅ Aprobado" solo con revisor indicado por el usuario y PR en verde; si no, "🚧 Borrador con evidencia".
<!-- Fin de sección compartida -->

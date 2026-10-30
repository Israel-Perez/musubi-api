# HU-39 — Revisar reportes y moderar contenido

**HU / Jira / Sprint / Módulo / Contraparte:** HU-39 / SCRUM-62 / Sprint 5 (23 nov – 4 dic 2026) / M-14 / [musubi-app:HU-39](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m14-admin/HU-39-moderar-contenido)

## Contexto
Jira (SCRUM-62): Como **administrador**, quiero **revisar los reportes**, para **retirar contenido falso o inapropiado**.

- Maqueta: sin pantalla propia. Se diseña con la guía de estilo de la maqueta; el lineamiento 4 permite agregar, nunca quitar.
- Trazabilidad: RF-54 · CU-24 · RNF-03, RNF-04.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-28`, `02-07`.

## Requisitos
1. El administrador ve los reportes con su motivo y el elemento reportado.
2. Puede retirar el contenido reportado o descartar el reporte.
3. Solo el administrador puede moderar contenido (RNF-03) y sus acciones quedan registradas (RNF-04).

RNF que aplican: RNF-03, RNF-04.

## Diseño
- Módulo `Musubi.Api/Modules/Admin/` (M-14). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `reports`, `moderationLog`, `auditLog` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Admin/`: reglas de negocio de cada criterio.
- `IntegrationTest/Admin/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] El administrador ve los reportes con su motivo y el elemento reportado.
- [ ] Puede retirar el contenido reportado o descartar el reporte.
- [ ] Solo el administrador puede moderar contenido (RNF-03) y sus acciones quedan registradas (RNF-04).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.

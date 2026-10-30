# HU-40 — Suspender y reactivar cuentas

**HU / Jira / Sprint / Módulo / Contraparte:** HU-40 / SCRUM-63 / Sprint 5 (23 nov – 4 dic 2026) / M-14 / [musubi-app:HU-40](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m14-admin/HU-40-suspender-cuentas)

## Contexto
Jira (SCRUM-63): Como **administrador**, quiero **suspender cuentas que incumplan las reglas**, para **proteger a la comunidad**.

- Maqueta: sin pantalla propia. Se diseña con la guía de estilo de la maqueta; el lineamiento 4 permite agregar, nunca quitar.
- Trazabilidad: RF-55 · CU-25 · RN-27 · RNF-04.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-02`.

## Requisitos
1. El administrador puede suspender y reactivar cuentas.
2. Una cuenta con [POR DEFINIR] reportes procedentes puede ser suspendida (RN-27).
3. Las suspensiones quedan registradas (RNF-04).

RNF que aplican: RNF-04.

## Diseño
- Módulo `Musubi.Api/Modules/Admin/` (M-14). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `users`, `moderationLog`, `auditLog` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
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
- [ ] El administrador puede suspender y reactivar cuentas.
- [ ] Una cuenta con [POR DEFINIR] reportes procedentes puede ser suspendida (RN-27).
- [ ] Las suspensiones quedan registradas (RNF-04).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.

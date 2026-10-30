# HU-27 — Calificar al vendedor al terminar un pedido

**HU / Jira / Sprint / Módulo / Contraparte:** HU-27 / SCRUM-46 / Sprint 3 (26 oct – 6 nov 2026) / M-13 / [musubi-app:HU-27](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m13-reputation/HU-27-calificar-vendedor)

## Contexto
Jira (SCRUM-46): Como **comprador**, quiero **calificar la experiencia al terminar un pedido**, para **ayudar a otros usuarios a decidir**.

- Maqueta: `Calificar`.
- Trazabilidad: RF-35, RF-36 · CU-19 · RN-24, RN-25, RN-26.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-24`.

## Requisitos
1. Solo se puede calificar un pedido **Completado**, una sola vez por pedido (RN-24).
2. La calificación usa una escala de 1 a 5 (RN-25, propuesta) y permite un comentario opcional.
3. La reputación se muestra como promedio junto con el número de calificaciones, en el perfil y en las publicaciones (RF-36).

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Reputation/` (M-13). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `ratings` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Reputation/`: reglas de negocio de cada criterio.
- `IntegrationTest/Reputation/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Solo se puede calificar un pedido **Completado**, una sola vez por pedido (RN-24).
- [ ] La calificación usa una escala de 1 a 5 (RN-25, propuesta) y permite un comentario opcional.
- [ ] La reputación se muestra como promedio junto con el número de calificaciones, en el perfil y en las publicaciones (RF-36).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.

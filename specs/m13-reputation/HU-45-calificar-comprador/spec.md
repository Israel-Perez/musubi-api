# HU-45 — Calificar al comprador al terminar un pedido

**HU / Jira / Sprint / Módulo / Contraparte:** HU-45 / SCRUM-50 / Sprint 3 (26 oct – 6 nov 2026) / M-13 / [musubi-app:HU-45](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m13-reputation/HU-45-calificar-comprador)

## Contexto
Jira (SCRUM-50): Como **vendedor**, quiero **calificar al comprador al terminar un pedido**, para **que la reputación refleje el comportamiento de ambas partes**.

- Maqueta: `Calificar`.
- Trazabilidad: RF-35, RF-36 · RN-24 · Entregable 1 §7 (calificación mutua).
- Origen: Entregable 1 §7 — historia nueva, no estaba en la lista original.

**Depende de:** `02-01`, `02-03`, `HU-24`.

## Requisitos
1. Al completarse un pedido, el vendedor puede calificar al comprador con la misma escala y un comentario opcional.
2. Solo una calificación por pedido.
3. La reputación del usuario considera las calificaciones recibidas como comprador y como vendedor.

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
- [ ] Al completarse un pedido, el vendedor puede calificar al comprador con la misma escala y un comentario opcional.
- [ ] Solo una calificación por pedido.
- [ ] La reputación del usuario considera las calificaciones recibidas como comprador y como vendedor.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): Resuelve la observación 8 del Anexo A. Pendiente: si la reputación de comprador y vendedor se muestra por separado.
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.

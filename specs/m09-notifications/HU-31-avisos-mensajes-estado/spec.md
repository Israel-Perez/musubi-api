# HU-31 — Avisos de mensajes y cambios de estado

**HU / Jira / Sprint / Módulo / Contraparte:** HU-31 / SCRUM-49 / Sprint 3 (26 oct – 6 nov 2026) / M-09 / [musubi-app:HU-31](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m09-notifications/HU-31-avisos-mensajes-estado)

## Contexto
Jira (SCRUM-49): Como **usuario**, quiero **recibir notificaciones de mensajes nuevos y de cambios de estado en mis solicitudes y pedidos**, para **dar seguimiento a mis tratos**.

- Maqueta: `Notificaciones`.
- Trazabilidad: RF-41, RF-42, RF-43 · CU-14.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-20`, `HU-24`.

## Requisitos
1. Se notifica cada mensaje nuevo (RF-42).
2. Se notifican los cambios de estado de solicitudes (próxima a expirar, expirada) y de pedidos (confirmado, cancelado, completado) (RF-41).
3. El centro de notificaciones muestra el historial; al seleccionar una se abre el elemento relacionado (RF-43).

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Notifications/` (M-09). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `notifications`, `deviceTokens` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.
- Avisos por FCM vía el módulo de notificaciones (HU-31), en segundo plano (`outbox`, R14).

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Notifications/`: reglas de negocio de cada criterio.
- `IntegrationTest/Notifications/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Se notifica cada mensaje nuevo (RF-42).
- [ ] Se notifican los cambios de estado de solicitudes (próxima a expirar, expirada) y de pedidos (confirmado, cancelado, completado) (RF-41).
- [ ] El centro de notificaciones muestra el historial; al seleccionar una se abre el elemento relacionado (RF-43).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.

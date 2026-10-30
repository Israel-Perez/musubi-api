# HU-22 — Bandeja de conversaciones

**HU / Jira / Sprint / Módulo / Contraparte:** HU-22 / SCRUM-31 / Sprint 2 (12 – 23 oct 2026) / M-07 / [musubi-app:HU-22](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m07-chat/HU-22-bandeja-conversaciones)

## Contexto
Jira (SCRUM-31): Como **usuario**, quiero **ver la lista de mis conversaciones**, para **dar seguimiento a varios tratos a la vez**.

- Maqueta: `Mensajes`, `Chat`, `Acuerdo-esperando`, `Acuerdo-confirmado`.
- Trazabilidad: RF-29 · CU-15.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-20`.

## Requisitos
1. Las conversaciones se ordenan por actividad reciente.
2. Se muestra un indicador de mensajes no leídos.
3. Cada conversación muestra al otro participante y el elemento de contexto.

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Chat/` (M-07). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `conversations` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Chat/`: reglas de negocio de cada criterio.
- `IntegrationTest/Chat/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Las conversaciones se ordenan por actividad reciente.
- [ ] Se muestra un indicador de mensajes no leídos.
- [ ] Cada conversación muestra al otro participante y el elemento de contexto.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
